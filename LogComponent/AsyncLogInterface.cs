using System.Collections.Concurrent;

namespace LogTest
{
    public sealed class AsyncLogInterface : LogInterface, IDisposable
    {
        private readonly AsyncLogOptions _options;
        private readonly ConcurrentQueue<LogLine> _lines = new();
        private readonly AutoResetEvent _workAvailable = new(false);
        private readonly object _lifecycleLock = new();
        private readonly Thread _workerThread;

        private StreamWriter? _writer;
        private DateOnly? _currentFileDate;
        private bool _acceptingWrites = true;
        private volatile bool _stopRequested;
        private bool _stopped;
        private StopMode _stopMode = StopMode.Flush;

        public AsyncLogInterface()
            : this(new AsyncLogOptions())
        {
        }

        public AsyncLogInterface(string logDirectory)
            : this(new AsyncLogOptions { LogDirectory = logDirectory })
        {
        }

        public AsyncLogInterface(AsyncLogOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            _workerThread = new Thread(MainLoop)
            {
                IsBackground = true,
                Name = "Async file logger"
            };
            _workerThread.Start();
        }

        public void WriteLog(string s)
        {
            lock (_lifecycleLock)
            {
                if (!_acceptingWrites)
                {
                    return;
                }

                _lines.Enqueue(new LogLine(s ?? string.Empty, _options.Clock.Now));
            }

            _workAvailable.Set();
        }

        public void Stop_Without_Flush()
        {
            RequestStop(StopMode.Immediate);
        }

        public void Stop_With_Flush()
        {
            RequestStop(StopMode.Flush);
        }

        public void Dispose()
        {
            Stop_With_Flush();
            _workAvailable.Dispose();
        }

        private void RequestStop(StopMode stopMode)
        {
            lock (_lifecycleLock)
            {
                if (_stopped)
                {
                    return;
                }

                _acceptingWrites = false;
                _stopMode = stopMode;
                _stopRequested = true;

                if (stopMode == StopMode.Immediate)
                {
                    while (_lines.TryDequeue(out _))
                    {
                    }
                }
            }

            _workAvailable.Set();

            if (Thread.CurrentThread != _workerThread)
            {
                _workerThread.Join();
            }

            lock (_lifecycleLock)
            {
                _stopped = true;
            }
        }

        private void MainLoop()
        {
            try
            {
                while (true)
                {
                    if (_stopRequested && _stopMode == StopMode.Immediate)
                    {
                        return;
                    }

                    if (_lines.TryDequeue(out LogLine? logLine))
                    {
                        TryWrite(logLine);
                        continue;
                    }

                    if (_stopRequested && _stopMode == StopMode.Flush)
                    {
                        return;
                    }

                    _workAvailable.WaitOne(_options.IdleWaitTimeout);
                }
            }
            finally
            {
                CloseWriter();
            }
        }

        private void TryWrite(LogLine logLine)
        {
            try
            {
                EnsureWriterFor(logLine.Timestamp);

                _writer!.Write(logLine.Timestamp.ToString("yyyy-MM-dd HH:mm:ss:fff"));
                _writer.Write('\t');
                _writer.Write(logLine.LineText());
                _writer.Write('\t');
                _writer.WriteLine();
            }
            catch (IOException)
            {
                CloseWriter();
            }
            catch (UnauthorizedAccessException)
            {
                CloseWriter();
            }
        }

        private void EnsureWriterFor(DateTime timestamp)
        {
            DateOnly entryDate = DateOnly.FromDateTime(timestamp);
            if (_writer is not null && _currentFileDate == entryDate)
            {
                return;
            }

            CloseWriter();
            Directory.CreateDirectory(_options.LogDirectory);

            string fileName = "Log" + timestamp.ToString("yyyyMMdd HHmmss fff") + ".log";
            string filePath = Path.Combine(_options.LogDirectory, fileName);

            _writer = new StreamWriter(filePath, append: true);
            _currentFileDate = entryDate;
            _writer.Write("Timestamp".PadRight(25, ' '));
            _writer.Write('\t');
            _writer.Write("Data".PadRight(15, ' '));
            _writer.Write('\t');
            _writer.WriteLine();
        }

        private void CloseWriter()
        {
            _writer?.Dispose();
            _writer = null;
            _currentFileDate = null;
        }

        private enum StopMode
        {
            Flush,
            Immediate
        }
    }
}
