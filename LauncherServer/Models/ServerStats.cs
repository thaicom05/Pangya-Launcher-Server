using System;

namespace PangyaLauncherServer.Models
{
    /// <summary>
    /// Aggregated statistics for a server process, pulled from the DB.
    /// </summary>
    public class ServerStats
    {
        public string    ServerName     { get; set; } = string.Empty;
        public int       TotalStarts    { get; set; }
        public int       TotalCrashes   { get; set; }
        public int       TotalRestarts  { get; set; }
        public TimeSpan? TotalUptime    { get; set; }
        public DateTime? LastStarted    { get; set; }
        public DateTime? LastStopped    { get; set; }

        public double CrashRate =>
            TotalStarts == 0 ? 0 : Math.Round((double)TotalCrashes / TotalStarts * 100, 1);
    }
}
