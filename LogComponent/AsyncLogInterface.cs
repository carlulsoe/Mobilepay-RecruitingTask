using System.Threading.Channels;

namespace LogTest
{
    public sealed class AsyncLogInterface : LogInterface, IDisposable
    {
        private readonly AsyncLogOptions _options;
        private readonly Channel<LogLine> _channel;
        private readonly object _joinLock = new();
        private readonly Thread _workerThread;

        private StreamWriter? _writer;
        private DateOnly? _currentFileDate;
        private long _droppedMessages;
        private int _stopRequested;
        private int _immediateStopRequested;
        private bool _joined;

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
            if (_options.QueueCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Queue capacity must be greater than zero.");
            }

            _channel = Channel.CreateBounded<LogLine>(new BoundedChannelOptions(_options.QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            _workerThread = new Thread(MainLoop)
            {
                IsBackground = true,
                Name = "Async file logger"
            };
            _workerThread.Start();
        }

        public long DroppedMessages => Interlocked.Read(ref _droppedMessages);

        public void WriteLog(string s)
        {
            if (Volatile.Read(ref _stopRequested) != 0)
            {
                return;
            }

            LogLine logLine = new(s ?? string.Empty, _options.TimeProvider.GetLocalNow().DateTime);
            if (!_channel.Writer.TryWrite(logLine) && Volatile.Read(ref _stopRequested) == 0)
            {
                Interlocked.Increment(ref _droppedMessages);
            }
        }

        public void Stop_Without_Flush()
        {
            RequestStop(flush: false);
        }

        public void Stop_With_Flush()
        {
            RequestStop(flush: true);
        }

        public void Dispose()
        {
            Stop_With_Flush();
        }

        private void RequestStop(bool flush)
        {
            if (!flush)
            {
                Interlocked.Exchange(ref _immediateStopRequested, 1);
            }

            if (Interlocked.Exchange(ref _stopRequested, 1) == 0)
            {
                _channel.Writer.TryComplete();
            }

            if (Thread.CurrentThread == _workerThread)
            {
                return;
            }

            lock (_joinLock)
            {
                if (!_joined)
                {
                    _workerThread.Join();
                    _joined = true;
                }
            }
        }

        private void MainLoop()
        {
            try
            {
                ChannelReader<LogLine> reader = _channel.Reader;
                while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
                {
                    if (Volatile.Read(ref _immediateStopRequested) != 0)
                    {
                        return;
                    }

                    while (reader.TryRead(out LogLine? logLine))
                    {
                        if (Volatile.Read(ref _immediateStopRequested) != 0)
                        {
                            return;
                        }

                        TryWrite(logLine);
                    }
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

            string fileName = "Log" + timestamp.ToString("yyyyMMdd") + ".log";
            string filePath = Path.Combine(_options.LogDirectory, fileName);
            bool shouldWriteHeader = !File.Exists(filePath) || new FileInfo(filePath).Length == 0;

            _writer = new StreamWriter(filePath, append: true);
            _currentFileDate = entryDate;
            if (shouldWriteHeader)
            {
                _writer.Write("Timestamp".PadRight(25, ' '));
                _writer.Write('\t');
                _writer.Write("Data".PadRight(15, ' '));
                _writer.Write('\t');
                _writer.WriteLine();
            }
        }

        private void CloseWriter()
        {
            _writer?.Dispose();
            _writer = null;
            _currentFileDate = null;
        }
    }
}
