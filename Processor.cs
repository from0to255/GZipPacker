using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace GZipTest2
{
    internal class Processor
    {
        private readonly Thread _inThread;
        private readonly Thread _outThread;
        private readonly DataAndState[] _queue;
        private readonly ThreadManager _threadManager;

        private int _cancelRequest;
        private Exception _inException;
        private int _inIndex;
        private Exception _outException;
        private int _outIndex;

        public Processor(
            int workingThreads,
            Func<Buffer, int> reader,
            Func<Buffer, int, Buffer, int> processor,
            Action<Buffer, int> writer)
        {
            _queue = new DataAndState[workingThreads];
            for (int n = _queue.Length; n-- > 0;)
                _queue[n] = new DataAndState();
            var actions = new Queue<Action<Action>>();
            _threadManager = new ThreadManager(_queue.Length, checkCanceled =>
                {
                    while (true)
                    {
                        checkCanceled();
                        Action<Action> action;
                        lock (actions)
                            action = actions.Count != 0 ? actions.Dequeue() : null;
                        if (action != null)
                            action(checkCanceled);
                        else
                            Thread.Sleep(10);
                    }
                });
            _inThread = new Thread(() =>
                {
                    try
                    {
                        while (_cancelRequest == 0)
                        {
                            DataAndState dataState = _queue[_inIndex];
                            if ((State) Interlocked.CompareExchange(ref dataState.State, (int) State.ReadingAndCompressing, (int) State.Free) == State.Free)
                            {
                                _inIndex = (_inIndex + 1)%_queue.Length;
                                if ((dataState.SrcDataSize = reader(dataState.SrcData)) == 0)
                                {
                                    Interlocked.Exchange(ref dataState.State, (int) State.Finished);
                                    break;
                                }
                                lock (actions)
                                    actions.Enqueue(checkCanceled =>
                                        {
                                            dataState.DstDataSize = processor(dataState.SrcData, dataState.SrcDataSize, dataState.DstData);
                                            Interlocked.Exchange(ref dataState.State, (int) State.Compressed);
                                        });
                            }
                            else
                                Thread.Sleep(10);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        _inException = ex;
                    }
                })
                {
                    Name = "InThread",
                    IsBackground = true
                };

            _outThread = new Thread(() =>
                {
                    try
                    {
                        while (_cancelRequest == 0)
                        {
                            DataAndState dataAndState = _queue[_outIndex];
                            var prevState = (State) Interlocked.CompareExchange(ref dataAndState.State, (int) State.Writting, (int) State.Compressed);
                            if (prevState == State.Finished)
                                break;
                            else if (prevState == State.Compressed)
                            {
                                _outIndex = (_outIndex + 1)%_queue.Length;
                                writer(dataAndState.DstData, dataAndState.DstDataSize);
                                Interlocked.Exchange(ref dataAndState.State, (int) State.Free);
                            }
                            else
                                Thread.Sleep(10);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        _outException = ex;
                    }
                })
                {
                    Name = "OutThread",
                    IsBackground = true
                };
        }

        public void Run()
        {
            _threadManager.Run();
            _outThread.Start();
            _inThread.Start();
        }

        public void CancelRequest()
        {
            Interlocked.Exchange(ref _cancelRequest, 1);
            _threadManager.CancelRequest();
        }

        private void ThrowExceptionIfNeed()
        {
            var exceptions = new List<Exception>();
            if (_inException != null)
                exceptions.Add(_inException);
            if (_outException != null)
                exceptions.Add(_outException);
            if (exceptions.Count > 0)
                throw new AggregateException(exceptions.ToArray());
        }

        public void WaitForAll()
        {
            _inThread.Join();
            _outThread.Join();
            ThrowExceptionIfNeed();
        }

        public bool WaitForAll(int timeout)
        {
            if (!_inThread.Join(timeout) || !_outThread.Join(timeout))
                return false;
            ThrowExceptionIfNeed();
            return true;
        }

        public sealed class Buffer
        {
            public Buffer(int initialCapacity)
            {
                _data = new byte[initialCapacity];
            }

            public byte[] Data { get { return _data; } }

            public int Capacity
            {
                get { return _data.Length; }
                set
                {
                    if (_data.Length < value)
                        _data = new byte[value];
                }
            }


            private byte[] _data;
        }

        private class DataAndState
        {
            public readonly Buffer DstData;
            public int DstDataSize;
            public readonly Buffer SrcData;
            public int SrcDataSize;
            public int State;

            public DataAndState()
            {
                SrcData = new Buffer(0);
                DstData = new Buffer(0);
            }
        }

        private enum State
        {
            Free,
            ReadingAndCompressing,
            Compressed,
            Writting,
            Finished
        }
    }
}