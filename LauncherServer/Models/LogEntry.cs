using System;

namespace PangyaLauncherServer.Models
{
    /// <summary>
    /// A single log line persisted to the SQLite database.
    /// </summary>
    public class LogEntry
    {
        public int      Id         { get; set; }
        public string   ServerName { get; set; } = string.Empty;
        public LogLevel Level      { get; set; }
        public string   Message    { get; set; } = string.Empty;
        public DateTime Timestamp  { get; set; } = DateTime.Now;

        // Formatted string for display in the RichTextBox
        public string Display =>
            $"[{Timestamp:HH:mm:ss}] [{Level,-5}] [{ServerName}] {Message}";
    }

    public enum LogLevel
    {
        Info,
        Warn,
        Error,
        Debug,
        Event   // lifecycle events (started / stopped / crashed)
    }
}
