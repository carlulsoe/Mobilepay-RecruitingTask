namespace LogTest
{
    public sealed class AsyncLogOptions
    {
        public string LogDirectory { get; set; } = Path.Combine(".", "LogTest");

        public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

        public int QueueCapacity { get; set; } = 10_000;
    }
}
