using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;

namespace WechatExport
{
    public class Downloader
    {
        private Queue<DownloadTask> tasks = new Queue<DownloadTask>();
        // private int pos = 0;
        private object alock = new object();
        private Thread[] threads;
        public Downloader(int num)
        {
            threads = new Thread[num];
            for (int i = 0; i < num; i++) threads[i] = new Thread(new ThreadStart(run));
        }

        public void AddTask(string url, string filename)
        {
            DownloadTask task = new DownloadTask() { filename = filename, url = url };
            lock (alock)
            {
                tasks.Enqueue(task);
            }
        }

        private void run()
        {
            var wc = new WebClient();
            while (true)
            {
                DownloadTask task = null;
                lock (alock)
                {
                    if (tasks.Count > 0)
                    {
                        task = tasks.Dequeue();
                    }
                }
                    
                if (task == null)
                {
                    break;
                }
                try
                {
                    wc.DownloadFile(task.url, task.filename);
                }
                catch (Exception) { }
            }
            wc.Dispose();
        }
        public void StartDownload()
        {
            foreach (var thread in threads)
            {
                thread.Start();
            }
        }
        public void WaitToEnd()
        {
            foreach (var thread in threads)
            {
                thread.Join();
            }
        }
    }

    public static class MyPath
    {
        public static string Combine(string a, string b, string c)
        {
            return Path.Combine(Path.Combine(a, b), c);
        }
        public static string Combine(string a, string b, string c, string d)
        {
            return Path.Combine(MyPath.Combine(a, b, c), d);
        }
    }

    static class ByteArrayLocater
    {
        public static readonly int[] Empty = new int[0];

        public static int[] Locate(this byte[] self, byte[] candidate)
        {
            if (IsEmptyLocate(self, candidate))
                return Empty;

            var list = new List<int>();

            for (int i = 0; i < self.Length; i++)
            {
                if (!IsMatch(self, i, candidate))
                    continue;

                list.Add(i);
            }

            return list.Count == 0 ? Empty : list.ToArray();
        }

        static bool IsMatch(byte[] array, int position, byte[] candidate)
        {
            if (candidate.Length > (array.Length - position))
                return false;

            for (int i = 0; i < candidate.Length; i++)
                if (array[position + i] != candidate[i])
                    return false;

            return true;
        }

        static bool IsEmptyLocate(byte[] array, byte[] candidate)
        {
            return array == null
                || candidate == null
                || array.Length == 0
                || candidate.Length == 0
                || candidate.Length > array.Length;
        }

        
    }

}