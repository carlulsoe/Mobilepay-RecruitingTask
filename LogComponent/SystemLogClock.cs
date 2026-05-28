namespace LogTest
{
    public sealed class SystemLogClock : ILogClock
    {
        public DateTime Now => DateTime.Now;
    }
}
