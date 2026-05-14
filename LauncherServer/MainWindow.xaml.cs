using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Win32;
using PangyaLauncherServer.Database;
using PangyaLauncherServer.Helpers;
using PangyaLauncherServer.Models;
using PangyaLauncherServer.Services;

namespace PangyaLauncherServer
{
    public partial class MainWindow : Window
    {
        // ------------------------------------------------------------------ //
        //  Infrastructure                                                      //
        // ------------------------------------------------------------------ //

        private readonly DatabaseService _db;
        private readonly ServerManager   _manager;
        private readonly DispatcherTimer _watchdogTimer;
        private readonly DispatcherTimer _uptimeTimer;

        // Prevents toggle click feedback loops
        private bool _suspendToggle;

        // ------------------------------------------------------------------ //
        //  Construction                                                        //
        // ------------------------------------------------------------------ //

        public MainWindow()
        {
            InitializeComponent();

            // 1. Database
            string dbPath = Path.Combine(AppContext.BaseDirectory, "launcher.db");
            _db = new DatabaseService(dbPath);

            // 2. Server manager
            _manager = new ServerManager(_db);
            _manager.LogReceived    += OnLogReceived;
            _manager.StatusChanged  += OnStatusChanged;
            _manager.StatsUpdated   += OnStatsUpdated;

            // 3. Load XML config
            string xmlPath = Path.Combine(AppContext.BaseDirectory, "startup.xml");
            ServerConfiguration.GenerateDefault(xmlPath);

            try
            {
                var entries = ServerConfiguration.Load(xmlPath);
                _manager.LoadConfiguration(entries);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Config load error:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 4. Watchdog timer — runs every 5 seconds
            _watchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _watchdogTimer.Tick += (_, _) => _manager.RunWatchdog();
            _watchdogTimer.Start();

            // 5. Uptime refresh timer — every second
            _uptimeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _uptimeTimer.Tick += (_, _) => RefreshUptimeLabels();
            _uptimeTimer.Start();

            // 6. Auto-start servers flagged AutoRun
            Loaded += async (_, _) => await AutoStartServersAsync();
        }

        // ------------------------------------------------------------------ //
        //  Auto-start                                                          //
        // ------------------------------------------------------------------ //

        private async Task AutoStartServersAsync()
        {
            foreach (var entry in _manager.Configuration.Values.Where(e => e.AutoRun))
                await _manager.StartAsync(entry.Name);
        }

        // ------------------------------------------------------------------ //
        //  Event: log line received from ServerManager                        //
        // ------------------------------------------------------------------ //

        private void OnLogReceived(LogEntry entry)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var rtb = FindName(entry.ServerName + "Log") as RichTextBox;
                if (rtb != null)
                    UiHelper.AppendLog(rtb, entry);
            });
        }

        // ------------------------------------------------------------------ //
        //  Event: server status changed                                       //
        // ------------------------------------------------------------------ //

        private void OnStatusChanged(string name, ServerStatus status)
        {
            Dispatcher.InvokeAsync(() =>
            {
                // Status label
                var statusBlock = FindName(name + "Status") as System.Windows.Controls.TextBlock;
                if (statusBlock != null)
                {
                    statusBlock.Text       = UiHelper.StatusToLabel(status);
                    statusBlock.Foreground = UiHelper.StatusToBrush(status);
                }

                // Toggle button
                var toggle = FindName(name + "Toggle") as ToggleButton;
                if (toggle != null)
                {
                    _suspendToggle = true;
                    toggle.IsChecked = status == ServerStatus.Online ||
                                       status == ServerStatus.Starting ||
                                       status == ServerStatus.Restarting;
                    _suspendToggle = false;
                }
            });
        }

        // ------------------------------------------------------------------ //
        //  Event: stats updated                                               //
        // ------------------------------------------------------------------ //

        private void OnStatsUpdated(string name, ServerStats stats)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var crashLabel = FindName(name + "Crashes") as System.Windows.Controls.TextBlock;
                if (crashLabel != null)
                    crashLabel.Text = $"Crashes: {stats.TotalCrashes}  Restarts: {stats.TotalRestarts}";
            });
        }

        // ------------------------------------------------------------------ //
        //  Uptime refresh                                                     //
        // ------------------------------------------------------------------ //

        private void RefreshUptimeLabels()
        {
            foreach (var entry in _manager.Configuration.Values)
            {
                var lbl = FindName(entry.Name + "Uptime") as System.Windows.Controls.TextBlock;
                if (lbl != null)
                    lbl.Text = $"Up: {UiHelper.FormatUptime(entry.Uptime)}";
            }
        }

        // ------------------------------------------------------------------ //
        //  UI Event Handlers                                                   //
        // ------------------------------------------------------------------ //

        private async void ServerToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_suspendToggle) return;

            if (sender is ToggleButton toggle)
            {
                string name = toggle.Name.Replace("Toggle", "");
                if (_manager.Configuration.ContainsKey(name))
                {
                    toggle.IsEnabled = false;
                    try
                    {
                        if (toggle.IsChecked == true)
                            await _manager.StartAsync(name);
                        else
                            _manager.Stop(name);
                    }
                    finally
                    {
                        toggle.IsEnabled = true;
                    }
                }
            }
        }

        private async void StartAll_Click(object sender, RoutedEventArgs e)
        {
            BtnStartAll.IsEnabled = false;
            try   { await _manager.StartAllAsync(); }
            finally { BtnStartAll.IsEnabled = true; }
        }

        private void StopAll_Click(object sender, RoutedEventArgs e) =>
            _manager.StopAll();

        private async void RestartAll_Click(object sender, RoutedEventArgs e)
        {
            BtnRestartAll.IsEnabled = false;
            try   { await _manager.RestartAllAsync(); }
            finally { BtnRestartAll.IsEnabled = true; }
        }

        // ------------------------------------------------------------------ //
        //  Log Tools                                                           //
        // ------------------------------------------------------------------ //

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            // Clear the active tab's log box
            if (ServerTabControl.SelectedItem is TabItem tab)
            {
                var rtb = ((tab.Content as ScrollViewer)?.Content) as RichTextBox;
                if (rtb != null) UiHelper.ClearLog(rtb);
            }
        }

        private void ExportLog_Click(object sender, RoutedEventArgs e)
        {
            if (ServerTabControl.SelectedItem is TabItem tab)
            {
                string name      = tab.Header?.ToString()?.Replace(" Server", "") ?? "Unknown";
                string outputDir = Path.Combine(AppContext.BaseDirectory, "logs");
                string path      = LogExportService.ExportToCsv(_db, name, outputDir);
                MessageBox.Show($"Log exported to:\n{path}", "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ExportStats_Click(object sender, RoutedEventArgs e)
        {
            string outputDir = Path.Combine(AppContext.BaseDirectory, "logs");
            string path      = LogExportService.ExportStats(_db, outputDir);
            MessageBox.Show($"Stats exported to:\n{path}", "Export Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void PurgeLogs_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Delete logs older than 30 days?", "Confirm Purge",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                int n = _db.PurgeLogs(30);
                MessageBox.Show($"{n} log entries deleted.", "Purge Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ------------------------------------------------------------------ //
        //  Window closing                                                      //
        // ------------------------------------------------------------------ //

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (MessageBox.Show("Stop all servers and exit?", "Confirm Exit",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.No)
            {
                e.Cancel = true;
                return;
            }

            _watchdogTimer.Stop();
            _uptimeTimer.Stop();
            _manager.Dispose();
            _db.Dispose();
            base.OnClosing(e);
        }
    }
}
