using LogTest;

namespace LogComponent.Tests;

public static class AsyncLogInterfaceTests
{
    public static int Main()
    {
        TestCase[] tests =
        [
            new("WriteLog_WritesMessageToFile", WriteLog_WritesMessageToFile),
            new("WriteLog_CreatesNewFileWhenMidnightIsCrossed", WriteLog_CreatesNewFileWhenMidnightIsCrossed),
            new("StopWithFlush_WaitsUntilAcceptedMessagesAreWritten", StopWithFlush_WaitsUntilAcceptedMessagesAreWritten),
            new("StopWithoutFlush_DiscardsOutstandingMessages", StopWithoutFlush_DiscardsOutstandingMessages)
        ];

        int failures = 0;
        foreach (TestCase test in tests)
        {
            try
            {
                test.Run();
                Console.WriteLine("PASS " + test.Name);
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine("FAIL " + test.Name);
                Console.WriteLine(ex);
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static void WriteLog_WritesMessageToFile()
    {
        using TestLogDirectory directory = new();
        FakeClock clock = new(new DateTime(2026, 5, 28, 10, 30, 0, 123));
        using AsyncLogInterface logger = CreateLogger(directory.Path, clock);

        logger.WriteLog("hello world");
        logger.Stop_With_Flush();

        string content = ReadAllLogs(directory.Path);
        AssertContains("hello world", content);
        AssertContains("2026-05-28 10:30:00:123", content);
    }

    private static void WriteLog_CreatesNewFileWhenMidnightIsCrossed()
    {
        using TestLogDirectory directory = new();
        FakeClock clock = new(new DateTime(2026, 5, 28, 23, 59, 59, 900));
        using AsyncLogInterface logger = CreateLogger(directory.Path, clock);

        logger.WriteLog("before midnight");
        clock.Now = new DateTime(2026, 5, 29, 0, 0, 0, 100);
        logger.WriteLog("after midnight");
        logger.Stop_With_Flush();

        string[] files = Directory.GetFiles(directory.Path, "*.log");
        AssertEqual(2, files.Length);
        AssertAny(files, file => Path.GetFileName(file).StartsWith("Log20260528", StringComparison.Ordinal));
        AssertAny(files, file => Path.GetFileName(file).StartsWith("Log20260529", StringComparison.Ordinal));
        AssertContains("before midnight", File.ReadAllText(files.Single(file => Path.GetFileName(file).StartsWith("Log20260528", StringComparison.Ordinal))));
        AssertContains("after midnight", File.ReadAllText(files.Single(file => Path.GetFileName(file).StartsWith("Log20260529", StringComparison.Ordinal))));
    }

    private static void StopWithFlush_WaitsUntilAcceptedMessagesAreWritten()
    {
        using TestLogDirectory directory = new();
        FakeClock clock = new(new DateTime(2026, 5, 28, 12, 0, 0));
        using AsyncLogInterface logger = CreateLogger(directory.Path, clock);

        for (int i = 0; i < 250; i++)
        {
            logger.WriteLog("flush-" + i);
        }

        logger.Stop_With_Flush();

        string content = ReadAllLogs(directory.Path);
        for (int i = 0; i < 250; i++)
        {
            AssertContains("flush-" + i, content);
        }
    }

    private static void StopWithoutFlush_DiscardsOutstandingMessages()
    {
        using TestLogDirectory directory = new();
        FakeClock clock = new(new DateTime(2026, 5, 28, 12, 0, 0));
        using AsyncLogInterface logger = CreateLogger(directory.Path, clock);

        const int messageCount = 25_000;
        for (int i = 0; i < messageCount; i++)
        {
            logger.WriteLog("discard-" + i);
        }

        logger.Stop_Without_Flush();

        int writtenMessages = CountMessages(directory.Path, "discard-");
        AssertInRange(writtenMessages, 0, messageCount - 1);
    }

    private static AsyncLogInterface CreateLogger(string directory, ILogClock clock)
    {
        return new AsyncLogInterface(new AsyncLogOptions
        {
            LogDirectory = directory,
            Clock = clock,
            IdleWaitTimeout = TimeSpan.FromMilliseconds(1)
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

    private static void AssertContains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected text was not found: " + expected);
        }
    }

    private static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException("Expected " + expected + ", got " + actual + ".");
        }
    }

    private static void AssertAny<T>(IEnumerable<T> values, Func<T, bool> predicate)
    {
        if (!values.Any(predicate))
        {
            throw new InvalidOperationException("Expected at least one matching value.");
        }
    }

    private static void AssertInRange(int actual, int minimum, int maximum)
    {
        if (actual < minimum || actual > maximum)
        {
            throw new InvalidOperationException("Expected value in range " + minimum + ".." + maximum + ", got " + actual + ".");
        }
    }

    private sealed record TestCase(string Name, Action Run);

    private sealed class FakeClock : ILogClock
    {
        public FakeClock(DateTime now)
        {
            Now = now;
        }

        public DateTime Now { get; set; }
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
