using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PangyaLauncherServer.Models;

namespace PangyaLauncherServer.Helpers
{
    /// <summary>
    /// Reusable WPF helpers for the launcher UI.
    /// </summary>
    public static class UiHelper
    {
        // ------------------------------------------------------------------ //
        //  RichTextBox helpers                                                 //
        // ------------------------------------------------------------------ //

        private const int MaxLogLines = 1500; // keep memory bounded

        /// <summary>
        /// Appends a coloured log line to <paramref name="rtb"/> and scrolls to end.
        /// Must be called on the UI thread (use Dispatcher.Invoke if needed).
        /// </summary>
        public static void AppendLog(RichTextBox rtb, LogEntry entry)
        {
            var brush = LevelToBrush(entry.Level);
            var para  = new Paragraph(new Run(entry.Display))
            {
                Foreground = brush,
                Margin     = new Thickness(0),
                FontFamily = new FontFamily("Consolas"),
                FontSize   = 12
            };

            rtb.Document.Blocks.Add(para);
            TrimDocument(rtb);
            rtb.ScrollToEnd();
        }

        /// <summary>Clears all content from a RichTextBox.</summary>
        public static void ClearLog(RichTextBox rtb) =>
            rtb.Document.Blocks.Clear();

        /// <summary>Keep document size bounded to avoid GC pressure.</summary>
        private static void TrimDocument(RichTextBox rtb)
        {
            while (rtb.Document.Blocks.Count > MaxLogLines)
                rtb.Document.Blocks.Remove(rtb.Document.Blocks.FirstBlock);
        }

        // ------------------------------------------------------------------ //
        //  Status / colour helpers                                             //
        // ------------------------------------------------------------------ //

        public static Brush StatusToBrush(ServerStatus status) => status switch
        {
            ServerStatus.Online     => Brushes.LimeGreen,
            ServerStatus.Starting   => Brushes.SkyBlue,
            ServerStatus.Stopping   => Brushes.Orange,
            ServerStatus.Restarting => Brushes.Gold,
            ServerStatus.Crashed    => Brushes.OrangeRed,
            _                       => Brushes.Gray
        };

        public static string StatusToLabel(ServerStatus status) => status switch
        {
            ServerStatus.Online     => "● Online",
            ServerStatus.Starting   => "◌ Starting...",
            ServerStatus.Stopping   => "◌ Stopping...",
            ServerStatus.Restarting => "↻ Restarting...",
            ServerStatus.Crashed    => "✖ Crashed",
            _                       => "○ Offline"
        };

        private static Brush LevelToBrush(LogLevel level) => level switch
        {
            LogLevel.Error => new SolidColorBrush(Color.FromRgb(255, 90, 90)),
            LogLevel.Warn  => new SolidColorBrush(Color.FromRgb(255, 210, 80)),
            LogLevel.Event => new SolidColorBrush(Color.FromRgb(130, 200, 255)),
            LogLevel.Debug => new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            _              => new SolidColorBrush(Color.FromRgb(180, 255, 180))
        };

        // ------------------------------------------------------------------ //
        //  Uptime formatter                                                    //
        // ------------------------------------------------------------------ //

        public static string FormatUptime(TimeSpan? uptime)
        {
            if (uptime == null) return "—";
            var t = uptime.Value;
            if (t.TotalHours >= 1)
                return $"{(int)t.TotalHours}h {t.Minutes:D2}m {t.Seconds:D2}s";
            if (t.TotalMinutes >= 1)
                return $"{t.Minutes}m {t.Seconds:D2}s";
            return $"{t.Seconds}s";
        }
    }
}
