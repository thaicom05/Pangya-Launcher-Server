using System;

namespace PangyaLauncherServer.Models
{
    /// <summary>
    /// Represents a managed server process configuration and runtime state.
    /// </summary>
    public class ServerEntry
    {
        // --- Configuration (from startup.xml) ---
        public string Name       { get; set; } = string.Empty;
        public string Path       { get; set; } = string.Empty;
        public string Parameters { get; set; } = string.Empty;
        public int    Delay      { get; set; }
        public bool   AutoRun    { get; set; }
        public int    MaxRestarts { get; set; } = 3;   // 0 = unlimited
        public bool   AutoRestart { get; set; } = true;

        // --- Runtime State ---
        public ServerStatus Status       { get; set; } = ServerStatus.Offline;
        public DateTime?    StartedAt    { get; set; }
        public DateTime?    StoppedAt    { get; set; }
        public int          RestartCount { get; set; }
        public int?         Pid          { get; set; }

        // Computed uptime (null when offline)
        public TimeSpan? Uptime =>
            Status == ServerStatus.Online && StartedAt.HasValue
                ? DateTime.Now - StartedAt.Value
                : null;
    }

    public enum ServerStatus
    {
        Offline,
        Starting,
        Online,
        Stopping,
        Crashed,
        Restarting
    }
}
