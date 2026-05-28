using LogTest;

namespace LogComponent.Tests;

public sealed class AsyncLogInterfaceTests
{
    [Fact]
    public void WriteLog_WritesMessageToFile()
    {
        using TestLogDirectory directory = new();
        FakeLogTimeProvider timeProvider = new(new DateTime(2026, 5, 28, 10, 30, 0, 123));
        using AsyncLogInterface logger = CreateLogger(directory.Path, timeProvider);

        logger.WriteLog("hello world");
        logger.Stop_With_Flush();

        string content = ReadAllLogs(directory.Path);
        Assert.Contains("hello world", content);
        Assert.Contains("2026-05-28 10:30:00:123", content);
    }

    [Fact]
    public void WriteLog_CreatesNewFileWhenMidnightIsCrossed()
    {
        using TestLogDirectory directory = new();
        FakeLogTimeProvider timeProvider = new(new DateTime(2026, 5, 28, 23, 59, 59, 900));
        using AsyncLogInterface logger = CreateLogger(directory.Path, timeProvider);

        logger.WriteLog("before midnight");
        timeProvider.Now = new DateTime(2026, 5, 29, 0, 0, 0, 100);
        logger.WriteLog("after midnight");
        logger.Stop_With_Flush();

        string[] files = Directory.GetFiles(directory.Path, "*.log");
        Assert.Equal(2, files.Length);
        string beforeMidnightFile = AssertSingleFileForDate(files, "Log20260528-");
        string afterMidnightFile = AssertSingleFileForDate(files, "Log20260529-");
        Assert.Contains("before midnight", File.ReadAllText(beforeMidnightFile));
        Assert.Contains("after midnight", File.ReadAllText(afterMidnightFile));
    }

    [Fact]
    public void StopWithFlush_WaitsUntilAcceptedMessagesAreWritten()
    {
        using TestLogDirectory directory = new();
        FakeLogTimeProvider timeProvider = new(new DateTime(2026, 5, 28, 12, 0, 0));
        using AsyncLogInterface logger = CreateLogger(directory.Path, timeProvider);

        for (int i = 0; i < 250; i++)
        {
            logger.WriteLog("flush-" + i);
        }

        logger.Stop_With_Flush();

        string content = ReadAllLogs(directory.Path);
        for (int i = 0; i < 250; i++)
        {
            Assert.Contains("flush-" + i, content);
        }
    }

    [Fact]
    public void SeparateLoggerInstances_WriteToSeparateFiles()
    {
        using TestLogDirectory directory = new();
        FakeLogTimeProvider timeProvider = new(new DateTime(2026, 5, 28, 12, 0, 0));
        using AsyncLogInterface firstLogger = CreateLogger(directory.Path, timeProvider);
        using AsyncLogInterface secondLogger = CreateLogger(directory.Path, timeProvider);

        firstLogger.WriteLog("first logger");
        secondLogger.WriteLog("second logger");
        firstLogger.Stop_With_Flush();
        secondLogger.Stop_With_Flush();

        string[] files = Directory.GetFiles(directory.Path, "*.log");
        Assert.Equal(2, files.Length);
        Assert.Single(files, file => File.ReadAllText(file).Contains("first logger", StringComparison.Ordinal));
        Assert.Single(files, file => File.ReadAllText(file).Contains("second logger", StringComparison.Ordinal));
    }

    [Fact]
    public void StopWithoutFlush_DiscardsOutstandingMessages()
    {
        using TestLogDirectory directory = new();
        FakeLogTimeProvider timeProvider = new(new DateTime(2026, 5, 28, 12, 0, 0));
        using AsyncLogInterface logger = CreateLogger(directory.Path, timeProvider);

        const int messageCount = 25_000;
        for (int i = 0; i < messageCount; i++)
        {
            logger.WriteLog("discard-" + i);
        }

        logger.Stop_Without_Flush();

        int writtenMessages = CountMessages(directory.Path, "discard-");
        Assert.InRange(writtenMessages, 0, messageCount - 1);
    }

    [Fact]
    public void WriteLog_DropsMessagesWhenQueueIsFull()
    {
        using TestLogDirectory directory = new();
        FakeLogTimeProvider timeProvider = new(new DateTime(2026, 5, 28, 12, 0, 0));
        using AsyncLogInterface logger = new(new AsyncLogOptions
        {
            LogDirectory = directory.Path,
            TimeProvider = timeProvider,
            QueueCapacity = 1
        });

        for (int i = 0; i < 100_000; i++)
        {
            logger.WriteLog("bounded-" + i);
        }

        logger.Stop_With_Flush();

        int writtenMessages = CountMessages(directory.Path, "bounded-");
        Assert.Equal(logger.DroppedMessagesDueToBackpressure, logger.DroppedMessages);
        Assert.True(logger.DroppedMessages > 0);
        Assert.InRange(writtenMessages, 1, 99_999);
    }

    [Fact]
    public void WriteLog_AfterStopIsIgnored()
    {
        using TestLogDirectory directory = new();
        FakeLogTimeProvider timeProvider = new(new DateTime(2026, 5, 28, 12, 0, 0));
        using AsyncLogInterface logger = CreateLogger(directory.Path, timeProvider);

        logger.WriteLog("before stop");
        logger.Stop_With_Flush();
        logger.WriteLog("after stop");

        string content = ReadAllLogs(directory.Path);
        Assert.Contains("before stop", content);
        Assert.DoesNotContain("after stop", content);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsInvalidQueueCapacity(int queueCapacity)
    {
        using TestLogDirectory directory = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => new AsyncLogInterface(new AsyncLogOptions
        {
            LogDirectory = directory.Path,
            QueueCapacity = queueCapacity
        }));
    }

    [Fact]
    public void StopMethods_AreSafeWhenCalledConcurrently()
    {
        using TestLogDirectory directory = new();
        FakeLogTimeProvider timeProvider = new(new DateTime(2026, 5, 28, 12, 0, 0));
        using AsyncLogInterface logger = CreateLogger(directory.Path, timeProvider);

        for (int i = 0; i < 1_000; i++)
        {
            logger.WriteLog("concurrent-stop-" + i);
        }

        Thread flushThread = new(logger.Stop_With_Flush);
        Thread immediateThread = new(logger.Stop_Without_Flush);

        flushThread.Start();
        immediateThread.Start();

        bool flushStopped = flushThread.Join(TimeSpan.FromSeconds(5));
        bool immediateStopped = immediateThread.Join(TimeSpan.FromSeconds(5));

        Assert.True(flushStopped);
        Assert.True(immediateStopped);
    }

    private static AsyncLogInterface CreateLogger(string directory, TimeProvider timeProvider)
    {
        return new AsyncLogInterface(new AsyncLogOptions
        {
            LogDirectory = directory,
            TimeProvider = timeProvider,
            QueueCapacity = 10_000
        });
    }

    private static string ReadAllLogs(string directory)
    {
        return string.Join(Environment.NewLine, Directory.GetFiles(directory, "*.log").Select(File.ReadAllText));
    }

    private static int CountMessages(string directory, string prefix)
    {
        return Directory.GetFiles(directory, "*.log")
            .SelectMany(File.ReadLines)
            .Count(line => line.Contains(prefix, StringComparison.Ordinal));
    }

    private static string AssertSingleFileForDate(string[] files, string datePrefix)
    {
        return Assert.Single(files, file => Path.GetFileName(file).StartsWith(datePrefix, StringComparison.Ordinal));
    }

    private sealed class FakeLogTimeProvider : TimeProvider
    {
        public FakeLogTimeProvider(DateTime now)
        {
            Now = now;
        }

        public DateTime Now { get; set; }

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow()
        {
            return new DateTimeOffset(DateTime.SpecifyKind(Now, DateTimeKind.Utc));
        }
    }

    private sealed class TestLogDirectory : IDisposable
    {
        public TestLogDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LogComponent.Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
