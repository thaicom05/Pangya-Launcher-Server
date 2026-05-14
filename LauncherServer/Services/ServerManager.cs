using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PangyaLauncherServer.Database;
using PangyaLauncherServer.Models;

namespace PangyaLauncherServer.Services
{
    /// <summary>
    /// Orchestrates process start / stop / restart and delegates all
    /// persistence to <see cref="DatabaseService"/>.
    /// Raises <see cref="LogReceived"/> and <see cref="StatusChanged"/> events
    /// so the UI can stay decoupled.
    /// </summary>
    public class ServerManager : IDisposable
    {
        // ------------------------------------------------------------------ //
        //  Events (published to UI layer)                                      //
        // ------------------------------------------------------------------ //

        public event Action<LogEntry>?                       LogReceived;
        public event Action<string, ServerStatus>?           StatusChanged;
        public event Action<string, ServerStats>?            StatsUpdated;

        // ------------------------------------------------------------------ //
        //  Private state                                                       //
        // ------------------------------------------------------------------ //

        private readonly DatabaseService                    _db;
        private readonly Dictionary<string, Process>        _procs  = new();
        private readonly Dictionary<string, ServerEntry>    _config = new();
        private readonly Dictionary<string, CancellationTokenSource> _restartCts = new();
        private bool _disposed;

        public ServerManager(DatabaseService db) => _db = db;

        // ------------------------------------------------------------------ //
        //  Configuration                                                       //
        // ------------------------------------------------------------------ //

        public void LoadConfiguration(List<ServerEntry> entries)
        {
            _config.Clear();
            foreach (var e in entries)
                _config[e.Name] = e;
        }

        public IReadOnlyDictionary<string, ServerEntry> Configuration => _config;

        // ------------------------------------------------------------------ //
        //  Start / Stop single server                                          //
        // ------------------------------------------------------------------ //

        public async Task StartAsync(string name)
        {
            if (!_config.TryGetValue(name, out var entry))
                throw new ArgumentException($"Server '{name}' not found in configuration.");

            if (_procs.ContainsKey(name))
            {
                Log(name, LogLevel.Warn, "Start requested but process is already running.");
                return;
            }

            SetStatus(name, ServerStatus.Starting);

            if (entry.Delay > 0)
            {
                Log(name, LogLevel.Info, $"Startup delay: {entry.Delay} ms");
                await Task.Delay(entry.Delay);
            }

            await LaunchProcess(entry);
        }

        public void Stop(string name)
        {
            if (!_procs.TryGetValue(name, out var proc))
            {
                Log(name, LogLevel.Warn, "Stop requested but no running process found.");
                return;
            }

            // Cancel any pending auto-restart
            if (_restartCts.TryGetValue(name, out var cts))
            {
                cts.Cancel();
                _restartCts.Remove(name);
            }

            SetStatus(name, ServerStatus.Stopping);
            Log(name, LogLevel.Event, "Manual stop requested.");

            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Log(name, LogLevel.Error, $"Kill failed: {ex.Message}");
            }
        }

        public async Task RestartAsync(string name)
        {
            Stop(name);
            await Task.Delay(800);
            await StartAsync(name);
        }

        // ------------------------------------------------------------------ //
        //  Bulk operations                                                     //
        // ------------------------------------------------------------------ //

        public async Task StartAllAsync()
        {
            foreach (var name in _config.Keys.OrderBy(n => _config[n].Delay))
                if (!_procs.ContainsKey(name))
                    await StartAsync(name);
        }

        public void StopAll()
        {
            foreach (var name in _procs.Keys.ToList())
                Stop(name);
        }

        public async Task RestartAllAsync()
        {
            StopAll();
            await Task.Delay(1200);
            await StartAllAsync();
        }

        // ------------------------------------------------------------------ //
        //  Process launcher (private)                                          //
        // ------------------------------------------------------------------ //

        private async Task LaunchProcess(ServerEntry entry)
        {
            string fullPath = Path.GetFullPath(entry.Path);

            if (!File.Exists(fullPath))
            {
                Log(entry.Name, LogLevel.Error, $"Binary not found: {fullPath}");
                SetStatus(entry.Name, ServerStatus.Offline);
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName               = fullPath,
                Arguments              = entry.Parameters ?? string.Empty,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                WorkingDirectory       = Path.GetDirectoryName(fullPath)!
            };

            var proc = new Process
            {
                StartInfo           = psi,
                EnableRaisingEvents = true
            };

            // --- stdout ---
            proc.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Log(entry.Name, LogLevel.Info, e.Data);
            };

            // --- stderr ---
            proc.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Log(entry.Name, LogLevel.Error, e.Data);
            };

            // --- exit ---
            proc.Exited += (_, _) => OnProcessExited(entry, proc);

            try
            {
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                entry.Pid       = proc.Id;
                entry.StartedAt = DateTime.Now;
                _procs[entry.Name] = proc;

                SetStatus(entry.Name, ServerStatus.Online);
                Log(entry.Name, LogLevel.Event, $"Process started (PID {proc.Id}).");
                _db.InsertEvent(entry.Name, "Started", proc.Id);
                PublishStats(entry.Name);
            }
            catch (Exception ex)
            {
                Log(entry.Name, LogLevel.Error, $"Failed to start: {ex.Message}");
                SetStatus(entry.Name, ServerStatus.Crashed);
            }
        }

        private void OnProcessExited(ServerEntry entry, Process proc)
        {
            int exitCode = -1;
            try { exitCode = proc.ExitCode; } catch { /* process already dead */ }

            _procs.Remove(entry.Name);
            entry.StoppedAt = DateTime.Now;
            entry.Pid       = null;

            bool crashed = exitCode != 0;

            if (crashed)
            {
                entry.RestartCount++;
                Log(entry.Name, LogLevel.Event, $"Process CRASHED (exit code {exitCode}). Restart #{entry.RestartCount}.");
                SetStatus(entry.Name, ServerStatus.Crashed);
                _db.InsertEvent(entry.Name, "Crashed", null, exitCode);

                bool canRestart = entry.AutoRestart &&
                                  (entry.MaxRestarts == 0 || entry.RestartCount <= entry.MaxRestarts);
                if (canRestart)
                    ScheduleRestart(entry);
                else
                    Log(entry.Name, LogLevel.Warn, "Max restarts reached. Staying offline.");
            }
            else
            {
                Log(entry.Name, LogLevel.Event, "Process exited cleanly.");
                SetStatus(entry.Name, ServerStatus.Offline);
                _db.InsertEvent(entry.Name, "Stopped", null, exitCode);
            }

            PublishStats(entry.Name);
        }

        // ------------------------------------------------------------------ //
        //  Auto-restart with exponential back-off                             //
        // ------------------------------------------------------------------ //

        private void ScheduleRestart(ServerEntry entry)
        {
            var cts = new CancellationTokenSource();
            _restartCts[entry.Name] = cts;

            int backoffMs = Math.Min(2000 * entry.RestartCount, 30_000); // cap at 30 s
            Log(entry.Name, LogLevel.Info, $"Auto-restart in {backoffMs / 1000} s...");
            SetStatus(entry.Name, ServerStatus.Restarting);

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(backoffMs, cts.Token);
                    _db.InsertEvent(entry.Name, "Restarted");
                    await LaunchProcess(entry);
                }
                catch (TaskCanceledException)
                {
                    Log(entry.Name, LogLevel.Info, "Scheduled restart was cancelled.");
                    SetStatus(entry.Name, ServerStatus.Offline);
                }
            });
        }

        // ------------------------------------------------------------------ //
        //  Health check / watchdog (call on a timer from UI)                  //
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Verifies that every Online server still has a live process.
        /// Returns names of any servers that were silently dead.
        /// </summary>
        public IReadOnlyList<string> RunWatchdog()
        {
            var dead = new List<string>();
            foreach (var kv in _procs.ToList())
            {
                try
                {
                    if (kv.Value.HasExited)
                    {
                        dead.Add(kv.Key);
                        Log(kv.Key, LogLevel.Warn, "Watchdog detected silent crash.");
                        OnProcessExited(_config[kv.Key], kv.Value);
                    }
                }
                catch
                {
                    dead.Add(kv.Key);
                }
            }
            return dead;
        }

        // ------------------------------------------------------------------ //
        //  Helpers                                                             //
        // ------------------------------------------------------------------ //

        private void Log(string serverName, LogLevel level, string message)
        {
            var entry = new LogEntry
            {
                ServerName = serverName,
                Level      = level,
                Message    = message,
                Timestamp  = DateTime.Now
            };

            _db.InsertLog(entry);
            LogReceived?.Invoke(entry);
        }

        private void SetStatus(string name, ServerStatus status)
        {
            if (_config.TryGetValue(name, out var e))
                e.Status = status;

            StatusChanged?.Invoke(name, status);
        }

        private void PublishStats(string name)
        {
            var all = _db.GetAllStats();
            var stat = all.FirstOrDefault(s => s.ServerName == name);
            if (stat != null)
                StatsUpdated?.Invoke(name, stat);
        }

        // ------------------------------------------------------------------ //
        //  IDisposable                                                         //
        // ------------------------------------------------------------------ //

        public void Dispose()
        {
            if (!_disposed)
            {
                StopAll();
                foreach (var cts in _restartCts.Values)
                    cts.Cancel();
                _disposed = true;
            }
        }
    }
}
