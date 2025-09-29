using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace PangyaLauncherServer
{ 
    public partial class MainWindow : Window
    {
        private readonly Dictionary<string, Process> serverProcesses = new();
        private List<ServerProcessInfo> serversConfig;
        private readonly string exeDir = AppContext.BaseDirectory;

        // FLAG para evitar loop de eventos ao mudar IsChecked
        private bool isChangingToggle = false; 
        public MainWindow()
        {
            InitializeComponent();
            LoadServerConfig();
        }


        public int ExeCheaker(string name)
        {
            Process[] myProcesses;
            myProcesses = Process.GetProcessesByName(name);
            foreach (Process myProcess in myProcesses)
            {
                return 1;
            }

            return 0;

        }
         
        public void ProssKill(string kill)
        {
            Process[] myProcesses;
            myProcesses = Process.GetProcessesByName(kill);
            foreach (Process myProcess in myProcesses)
            {
                myProcess.Kill();
            }
        }

        private void LoadServerConfig()
        {
            try
            {
                serversConfig = ServerConfig.LoadProcesses("startup.xml"); // você precisa do método LoadProcesses
                foreach (var server in serversConfig)
                {
                    var toggle = FindName(server.Name + "Toggle") as ToggleButton;
                    var logBox = FindName(server.Name + "Log") as RichTextBox;

                    string fullPath = Path.Combine(exeDir, server.Path); 
                    if (!File.Exists(fullPath))
                    {
                        AppendText(logBox, $"[WARN] Binário não encontrado: {fullPath}\n", Brushes.OrangeRed);
                        toggle.IsEnabled = false;
                    }
                    else
                    {
                        ProssKill(server.Name+"Server");
                        ProssKill(server.Name);

                        Thread.Sleep(300);
                        toggle.IsEnabled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar XML: {ex.Message}");
            }
        }

        private void ServerToggle_Click(object sender, RoutedEventArgs e)
        {
            if (isChangingToggle) return;

            if (sender is ToggleButton toggle)
            {
                string serverName = toggle.Name.Replace("Toggle", "");
                var server = serversConfig.Find(s => s.Name == serverName);
                var logBox = FindName(serverName + "Log") as RichTextBox;

                if (serverProcesses.ContainsKey(serverName))
                    StopServer(serverName, toggle, logBox);
                else if (server != null)
                {

                    UpdateStatus(server.Name, "Iniciando...", Brushes.LightBlue); 
                    StartServer(server, toggle, logBox);
                }
            }
        }

        private async void StartServer(ServerProcessInfo server, ToggleButton toggle, RichTextBox logBox)
        {
            await Task.Delay(server.Delay);

            string fullPath = Path.Combine(exeDir, server.Path);
            if (!File.Exists(fullPath))
            {
                AppendText(logBox, $"[ERROR] Binário não encontrado: {fullPath}\n", Brushes.OrangeRed);
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = fullPath,
                Arguments = server.Parameters,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(fullPath)
            };

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    AppendText(logBox, e.Data + "\n", Brushes.LightGreen);
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    AppendText(logBox, "[ERROR] " + e.Data + "\n", Brushes.OrangeRed);
            };

            process.Exited += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    isChangingToggle = true;
                    AppendText(logBox, $"[{server.Name}] Processo encerrado.\n", Brushes.Gray);
                    toggle.IsChecked = false;
                    isChangingToggle = false;
                    serverProcesses.Remove(server.Name);
                });
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                serverProcesses[server.Name] = process;

                isChangingToggle = true;
                toggle.IsChecked = true;
                isChangingToggle = false;

                AppendText(logBox, $"[{server.Name}] Processo iniciado.\n", Brushes.LightBlue);
                UpdateStatus(server.Name, "Online", Brushes.LightGreen); 
            }
            catch (Exception ex)
            {
                UpdateStatus(server.Name, "Falhou", Brushes.Red);

                AppendText(logBox, $"[ERROR] Falha ao iniciar {server.Name}: {ex.Message}\n", Brushes.OrangeRed);
            }
        }

        private void StopServer(string serverName, ToggleButton toggle, RichTextBox logBox)
        {
            if (serverProcesses.TryGetValue(serverName, out var process))
            {
                try
                {
                    process.Kill(true);
                    UpdateStatus(serverName, "Offline", Brushes.Gray);

                    AppendText(logBox, $"[{serverName}] Processo finalizado manualmente.\n", Brushes.Gray);
                }
                catch (Exception ex)
                {
                    AppendText(logBox, $"[ERROR] Erro ao finalizar {serverName}: {ex.Message}\n", Brushes.OrangeRed);
                }
                finally
                {
                    serverProcesses.Remove(serverName);

                    isChangingToggle = true;
                    toggle.IsChecked = false;
                    isChangingToggle = false;
                }
            }
        }

        private void StartAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var server in serversConfig)
            {
                var toggle = FindName(server.Name + "Toggle") as ToggleButton;
                var logBox = FindName(server.Name + "Log") as RichTextBox;
                if (toggle != null && logBox != null && toggle.IsEnabled && !(toggle.IsChecked ?? false))
                    StartServer(server, toggle, logBox);
            }
        }

        private void StopAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var serverName in serverProcesses.Keys.ToList())
            {
                var toggle = FindName(serverName + "Toggle") as ToggleButton;
                var logBox = FindName(serverName + "Log") as RichTextBox;
                StopServer(serverName, toggle, logBox);
            }
        }

        private void AppendText(RichTextBox rtb, string text, Brush color)
        {
            Dispatcher.Invoke(() =>
            {
                var range = new TextRange(rtb.Document.ContentEnd, rtb.Document.ContentEnd)
                {
                    Text = text
                };
                range.ApplyPropertyValue(TextElement.ForegroundProperty, color);
                rtb.ScrollToEnd();
            });
        }
        private void UpdateStatus(string serverName, string status, Brush color)
        {
            var statusBlock = FindName(serverName + "Status") as TextBlock;
            if (statusBlock != null)
            {
                statusBlock.Text = $"Status: {status}";
                statusBlock.Foreground = color;
            }
        }

    }
}
