namespace LogTest
{
    public sealed class AsyncLogOptions
    {
        public string LogDirectory { get; set; } = Path.Combine(".", "LogTest");

        public ILogClock Clock { get; set; } = new SystemLogClock();

        public TimeSpan IdleWaitTimeout { get; set; } = TimeSpan.FromMilliseconds(25);
    }
}
