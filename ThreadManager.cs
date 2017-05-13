using System;
using System.Collections.Generic;
using System.Threading;

namespace GZipTest2
{
    public class ThreadManager
    {
        private readonly List<Exception> _exceptions = new List<Exception>();
        private readonly Thread[] _threads;
        private int _outOfMemoryException;

        public ThreadManager(int threads, Action<int> action)
        {
            if (action == null) throw new ArgumentNullException("action");
            if (threads < 1) throw new ArgumentException("threads");
            _threads = new Thread[threads];
            for (int n = 0; n < threads; ++n)
            {
                int n1 = n;
                _threads[n] = new Thread(() =>
                    {
                        try
                        {
                            action(n1);
                        }
                        catch (Exception ex)
                        {
                            try
                            {
                                lock (_exceptions)
                                    _exceptions.Add(ex);
                            }
                            catch
                            {
                                Interlocked.Exchange(ref _outOfMemoryException, 1);
                            }
                        }
                    })
                    {
                        Name = string.Format("Thread #{0}", n),
                        IsBackground = true
                    };
            }
        }

        public void Run()
        {
            foreach (Thread thread in _threads)
                thread.Start();
        }

        public void WaitForAll()
        {
            while (!WaitForAll(TimeSpan.FromSeconds(1)))
            {
            }
        }

        public bool WaitForAll(TimeSpan timeout)
        {
            DateTime endTime = DateTime.UtcNow + timeout;
            foreach (Thread thread in _threads)
            {
                DateTime time = DateTime.UtcNow;
                if (endTime < time)
                    return false;
                if (!thread.Join(endTime - time))
                    return false;
            }
            // Note: All threads are finished here, so don't use lock to access to _exceptions!
            if (_outOfMemoryException != 0)
                _exceptions.Add(new OutOfMemoryException());
            if (_exceptions.Count != 0)
                throw new AggregateException(_exceptions.ToArray());
            return true;
        }
    }
}