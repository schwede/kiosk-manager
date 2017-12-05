﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PayBitForward.Models;

namespace PayBitForward.Messaging
{
    public class FileAssembler
    {
        public delegate void DownloadSuccess(Content content);

        public delegate void DownloadFail(string message);

        public event DownloadSuccess OnDownloadSucess;

        public event DownloadFail OnDownloadFail;

        private CancellationTokenSource CancelSource { get; set; } = new CancellationTokenSource();

        private CancellationToken Token { get; set; }

        private ConcurrentDictionary<int, byte[]> DataStore { get; set; } 
        
        private Content ContentInfo { get; set; }       

        private Thread WorkerThread { get; set; }

        private static readonly log4net.ILog Log = log4net.LogManager.GetLogger(typeof(FileAssembler));

        public FileAssembler(Content c, ConcurrentDictionary<int, byte[]> data)
        {
            ContentInfo = c;
            DataStore = data;

            Token = CancelSource.Token;
            CancelSource.Cancel();
        }

        public virtual void Start()
        {
            Log.Debug("Attempting to start file assembler for " + ContentInfo.FileName);

            // Make sure the thread does not start twice
            if (Token.IsCancellationRequested)
            {
                CancelSource = new CancellationTokenSource();
                Token = CancelSource.Token;

                try
                {
                    WorkerThread = new Thread(Run);
                    WorkerThread.Start();
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Message);
                }
            }
        }

        public virtual void Stop()
        {
            Log.Debug("Requesting thread to stop");
            CancelSource.CancelAfter(500);
        }

        protected virtual void Run()
        {
            int totalChunks = (int)Math.Ceiling(ContentInfo.ByteSize / 256.0);

            while(true)
            {
                if(CancelSource.IsCancellationRequested)
                {
                    return;
                }

                if(DataStore.Count >= totalChunks)
                {
                    for(int i = 0; i < totalChunks; i++)
                    {
                        if(!DataStore.ContainsKey(i))
                        {
                            Log.Info("Failed to get all the correct packets");
                            OnDownloadFail?.Invoke("Failed to download content");
                            CancelSource.Cancel();
                            return;
                        }
                    }

                    try
                    {
                        var path = Path.Combine(ContentInfo.LocalPath, ContentInfo.FileName);
                        using (var stream = new BinaryWriter(File.Open(path, FileMode.OpenOrCreate)))
                        {
                            for (int i = 0; i < totalChunks; i++)
                            {
                                stream.Seek(i, SeekOrigin.Begin);
                                stream.Write(DataStore[i]);
                            }
                        }

                        Log.Info("Finished downloading " + ContentInfo.FileName);
                        OnDownloadSucess?.Invoke(ContentInfo);
                        CancelSource.Cancel();
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex.Message);
                    }
                }
            }
        }
    }
}
