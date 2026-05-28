namespace LogTest
{
    public interface LogInterface
    {
        /// <summary>
        /// Stop the logging. Any outstanding logs that have not already been written are discarded.
        /// </summary>
        void Stop_Without_Flush();

        /// <summary>
        /// Stop the logging. The call will not return until all accepted logs have been written.
        /// </summary>
        void Stop_With_Flush();

        /// <summary>
        /// Enqueue a message to be written to the log.
        /// </summary>
        /// <param name="s">The message to write.</param>
        void WriteLog(string s);
    }
}
