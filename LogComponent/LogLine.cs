namespace LogTest
{
    public sealed class LogLine
    {
        public LogLine(string text, DateTime timestamp)
        {
            Text = text;
            Timestamp = timestamp;
        }

        public string Text { get; }

        public DateTime Timestamp { get; }

        public string LineText()
        {
            return Text;
        }
    }
}
