using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PangyaLauncherServer.Database;
using PangyaLauncherServer.Models;

namespace PangyaLauncherServer.Services
{
    /// <summary>
    /// Exports log data from the database to flat files.
    /// </summary>
    public static class LogExportService
    {
        /// <summary>Exports all logs for <paramref name="serverName"/> to a .txt file.</summary>
        public static string ExportToText(DatabaseService db, string serverName, string outputDir)
        {
            var logs = db.GetLogs(serverName, limit: 10_000);
            Directory.CreateDirectory(outputDir);

            string filename = $"log_{serverName}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string path     = Path.Combine(outputDir, filename);

            var sb = new StringBuilder();
            sb.AppendLine($"=== Pangya Launcher — Log Export ===");
            sb.AppendLine($"Server   : {serverName}");
            sb.AppendLine($"Exported : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Lines    : {logs.Count}");
            sb.AppendLine(new string('-', 80));

            foreach (var entry in logs)
                sb.AppendLine(entry.Display);

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        /// <summary>Exports logs to CSV for analysis in Excel / LibreOffice.</summary>
        public static string ExportToCsv(DatabaseService db, string serverName, string outputDir)
        {
            var logs = db.GetLogs(serverName, limit: 10_000);
            Directory.CreateDirectory(outputDir);

            string filename = $"log_{serverName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            string path     = Path.Combine(outputDir, filename);

            var sb = new StringBuilder();
            sb.AppendLine("\"Timestamp\",\"Server\",\"Level\",\"Message\"");
            foreach (var entry in logs)
            {
                string safe = entry.Message.Replace("\"", "\"\"");
                sb.AppendLine($"\"{entry.Timestamp:yyyy-MM-dd HH:mm:ss}\",\"{entry.ServerName}\",\"{entry.Level}\",\"{safe}\"");
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        /// <summary>Exports stats for all servers to a summary text file.</summary>
        public static string ExportStats(DatabaseService db, string outputDir)
        {
            var stats = db.GetAllStats();
            Directory.CreateDirectory(outputDir);

            string filename = $"stats_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string path     = Path.Combine(outputDir, filename);

            var sb = new StringBuilder();
            sb.AppendLine("=== Pangya Launcher — Server Statistics ===");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(new string('-', 60));

            foreach (var s in stats)
            {
                sb.AppendLine($"Server       : {s.ServerName}");
                sb.AppendLine($"  Starts     : {s.TotalStarts}");
                sb.AppendLine($"  Crashes    : {s.TotalCrashes}  (crash rate: {s.CrashRate}%)");
                sb.AppendLine($"  Restarts   : {s.TotalRestarts}");
                sb.AppendLine($"  Last Start : {s.LastStarted?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A"}");
                sb.AppendLine($"  Last Stop  : {s.LastStopped?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A"}");
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }
    }
}
