using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;
using PangyaLauncherServer.Models;

namespace PangyaLauncherServer.Database
{
    /// <summary>
    /// All database I/O for the launcher.
    /// Uses Microsoft.Data.Sqlite (no EF, no ORM overhead).
    /// Database file: launcher.db (next to the executable).
    /// </summary>
    public class DatabaseService : IDisposable
    {
        private readonly SqliteConnection _conn;
        private bool _disposed;

        // ------------------------------------------------------------------ //
        //  Construction & Schema                                               //
        // ------------------------------------------------------------------ //

        public DatabaseService(string dbPath)
        {
            _conn = new SqliteConnection($"Data Source={dbPath}");
            _conn.Open();
            EnableWAL();
            ApplySchema();
        }

        private void EnableWAL()
        {
            // WAL mode: concurrent reads while writing
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            cmd.ExecuteNonQuery();
        }

        private void ApplySchema()
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Logs (
                    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                    ServerName TEXT    NOT NULL,
                    Level      TEXT    NOT NULL,
                    Message    TEXT    NOT NULL,
                    Timestamp  TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
                );

                CREATE TABLE IF NOT EXISTS ServerEvents (
                    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                    ServerName TEXT    NOT NULL,
                    EventType  TEXT    NOT NULL,   -- Started | Stopped | Crashed | Restarted
                    Pid        INTEGER,
                    ExitCode   INTEGER,
                    Timestamp  TEXT    NOT NULL DEFAULT (datetime('now','localtime'))
                );

                CREATE INDEX IF NOT EXISTS idx_logs_server    ON Logs(ServerName);
                CREATE INDEX IF NOT EXISTS idx_logs_ts        ON Logs(Timestamp);
                CREATE INDEX IF NOT EXISTS idx_events_server  ON ServerEvents(ServerName);
            ";
            cmd.ExecuteNonQuery();
        }

        // ------------------------------------------------------------------ //
        //  Logging                                                             //
        // ------------------------------------------------------------------ //

        public void InsertLog(LogEntry entry)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Logs (ServerName, Level, Message, Timestamp)
                VALUES ($name, $level, $msg, $ts)";
            cmd.Parameters.AddWithValue("$name",  entry.ServerName);
            cmd.Parameters.AddWithValue("$level", entry.Level.ToString());
            cmd.Parameters.AddWithValue("$msg",   entry.Message);
            cmd.Parameters.AddWithValue("$ts",    entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        /// <summary>Retrieve last <paramref name="limit"/> log lines for a server.</summary>
        public List<LogEntry> GetLogs(string serverName, int limit = 500)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, ServerName, Level, Message, Timestamp
                FROM Logs
                WHERE ServerName = $name
                ORDER BY Id DESC
                LIMIT $limit";
            cmd.Parameters.AddWithValue("$name",  serverName);
            cmd.Parameters.AddWithValue("$limit", limit);

            var list = new List<LogEntry>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new LogEntry
                {
                    Id         = reader.GetInt32(0),
                    ServerName = reader.GetString(1),
                    Level      = Enum.Parse<LogLevel>(reader.GetString(2), true),
                    Message    = reader.GetString(3),
                    Timestamp  = DateTime.Parse(reader.GetString(4))
                });
            }

            list.Reverse(); // chronological order
            return list;
        }

        /// <summary>Search logs across all servers or filtered by name.</summary>
        public List<LogEntry> SearchLogs(string? serverName, string keyword, int limit = 200)
        {
            using var cmd = _conn.CreateCommand();
            var where = "WHERE Message LIKE $kw";
            if (!string.IsNullOrEmpty(serverName))
                where += " AND ServerName = $name";

            cmd.CommandText = $@"
                SELECT Id, ServerName, Level, Message, Timestamp
                FROM Logs
                {where}
                ORDER BY Id DESC
                LIMIT $limit";
            cmd.Parameters.AddWithValue("$kw",    $"%{keyword}%");
            if (!string.IsNullOrEmpty(serverName))
                cmd.Parameters.AddWithValue("$name", serverName);
            cmd.Parameters.AddWithValue("$limit", limit);

            var list = new List<LogEntry>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new LogEntry
                {
                    Id         = reader.GetInt32(0),
                    ServerName = reader.GetString(1),
                    Level      = Enum.Parse<LogLevel>(reader.GetString(2), true),
                    Message    = reader.GetString(3),
                    Timestamp  = DateTime.Parse(reader.GetString(4))
                });
            }

            list.Reverse();
            return list;
        }

        /// <summary>Purge logs older than <paramref name="days"/> days.</summary>
        public int PurgeLogs(int days = 30)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                DELETE FROM Logs
                WHERE Timestamp < datetime('now', $delta, 'localtime')";
            cmd.Parameters.AddWithValue("$delta", $"-{days} days");
            return cmd.ExecuteNonQuery();
        }

        // ------------------------------------------------------------------ //
        //  Server Events                                                       //
        // ------------------------------------------------------------------ //

        public void InsertEvent(string serverName, string eventType, int? pid = null, int? exitCode = null)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ServerEvents (ServerName, EventType, Pid, ExitCode, Timestamp)
                VALUES ($name, $evt, $pid, $code, $ts)";
            cmd.Parameters.AddWithValue("$name", serverName);
            cmd.Parameters.AddWithValue("$evt",  eventType);
            cmd.Parameters.AddWithValue("$pid",  (object?)pid  ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$code", (object?)exitCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$ts",   DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        // ------------------------------------------------------------------ //
        //  Statistics                                                          //
        // ------------------------------------------------------------------ //

        public List<ServerStats> GetAllStats()
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    ServerName,
                    SUM(CASE WHEN EventType = 'Started'   THEN 1 ELSE 0 END) AS Starts,
                    SUM(CASE WHEN EventType = 'Crashed'   THEN 1 ELSE 0 END) AS Crashes,
                    SUM(CASE WHEN EventType = 'Restarted' THEN 1 ELSE 0 END) AS Restarts,
                    MAX(CASE WHEN EventType = 'Started'   THEN Timestamp END) AS LastStarted,
                    MAX(CASE WHEN EventType = 'Stopped'   THEN Timestamp END) AS LastStopped
                FROM ServerEvents
                GROUP BY ServerName";

            var list = new List<ServerStats>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new ServerStats
                {
                    ServerName    = reader.GetString(0),
                    TotalStarts   = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                    TotalCrashes  = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    TotalRestarts = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    LastStarted   = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4)),
                    LastStopped   = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5))
                });
            }
            return list;
        }

        // ------------------------------------------------------------------ //
        //  IDisposable                                                         //
        // ------------------------------------------------------------------ //

        public void Dispose()
        {
            if (!_disposed)
            {
                _conn.Close();
                _conn.Dispose();
                _disposed = true;
            }
        }
    }
}
