// StrataSmartToggle for Windows 10/11
// Smart native taskbar launcher, tray minimizer & lifecycle controller for Niko1221/Strata.
// Compiles out-of-the-box with built-in Windows .NET Framework csc.exe (zero external dependencies).
// License: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace StrataSmartToggle
{
    public class AppConfig
    {
        public string DataDir = "";
        public string Model = "Gyro-M";
        public string Context = "64k";
        public int Port = 8080;
        public bool Configured = false;

        private static string ConfigPath {
            get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "strata-smart-toggle.json"); }
        }

        public static AppConfig Load()
        {
            var cfg = new AppConfig();
            cfg.DataDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            try {
                if (File.Exists(ConfigPath)) {
                    string json = File.ReadAllText(ConfigPath);
                    cfg.DataDir = ExtractJsonString(json, "dataDir", cfg.DataDir);
                    cfg.Model   = ExtractJsonString(json, "model", cfg.Model);
                    cfg.Context = ExtractJsonString(json, "context", cfg.Context);
                    string pStr = ExtractJsonString(json, "port", "8080");
                    int.TryParse(pStr, out cfg.Port);
                    cfg.Configured = true;
                }
            } catch { }
            return cfg;
        }

        public void Save()
        {
            Configured = true;
            string safeDir = DataDir.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string safeMod = Model.Replace("\"", "\\\"");
            string json = "{\r\n" +
                          "  \"dataDir\": \"" + safeDir + "\",\r\n" +
                          "  \"model\": \"" + safeMod + "\",\r\n" +
                          "  \"context\": \"" + Context + "\",\r\n" +
                          "  \"port\": " + Port + "\r\n" +
                          "}\r\n";
            try { File.WriteAllText(ConfigPath, json); } catch { }
        }

        private static string ExtractJsonString(string json, string key, string fallback)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"?([^\",\\n\\r}]+)\"?");
            if (m.Success) return m.Groups[1].Value.Trim().Replace("\\\\", "\\");
            return fallback;
        }
    }

    public class Program : Application
    {
        [DllImport("shell32.dll", SetLastError = true)]
        static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private AppConfig config;
        private Window win;
        private WinForms.NotifyIcon trayIcon;
        private StackPanel setupPanel;
        private Border summaryBadge;
        private TextBlock summaryText;
        private ComboBox modelBox;
        private TextBox dirBox;
        private Button actionBtn;
        private Button gearBtn;
        private System.Windows.Shapes.Ellipse statusDot;
        private TextBlock statusLabel;
        private Button[] ctxButtons;
        private readonly string[] ctxOptions = new[] { "16k", "32k", "64k", "128k" };
        private bool isBusy = false;
        private bool isSetupVisible = false;
        private bool allowClose = false;

        private static readonly string[] OfficialModels = new[] {
            "Gyro-M", "Gyro-S", "IQ2_XS", "Q2_0", "IQ3_XXS", "IQ3_S",
            "IQ1_M", "UD-IQ4_XS", "UD-Q4_K_XL", "NVFP4", "NVFP4-QAD", "Qwen3.6-35B-A3B"
        };

        [STAThread]
        public static void Main()
        {
            SetCurrentProcessExplicitAppUserModelID("Strata.SmartToggle.Controller.1");
            var app = new Program();
            app.Run(app.CreateMainWindow());
        }

        private bool IsLightTheme()
        {
            try {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) {
                    if (k != null) {
                        object val = k.GetValue("AppsUseLightTheme");
                        if (val != null) return Convert.ToInt32(val) == 1;
                    }
                }
            } catch { }
            return false;
        }

        private bool IsPortListening(int port)
        {
            try {
                using (var client = new TcpClient()) {
                    var res = client.BeginConnect("127.0.0.1", port, null, null);
                    bool ok = res.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(160));
                    if (ok && client.Connected) { client.EndConnect(res); return true; }
                }
            } catch { }
            return false;
        }

        private Window CreateMainWindow()
        {
            config = AppConfig.Load();
            if (string.IsNullOrEmpty(config.DataDir)) config.DataDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            bool light = IsLightTheme();

            System.Windows.Media.Color bgGlass = light ? System.Windows.Media.Color.FromArgb(236, 246, 248, 252) : System.Windows.Media.Color.FromArgb(232, 17, 20, 28);
            System.Windows.Media.Color surface = light ? System.Windows.Media.Color.FromArgb(210, 255, 255, 255) : System.Windows.Media.Color.FromArgb(175, 28, 33, 46);
            System.Windows.Media.Color fgMain  = light ? System.Windows.Media.Color.FromRgb(15, 23, 42)          : System.Windows.Media.Color.FromRgb(241, 245, 249);
            System.Windows.Media.Color fgSub   = light ? System.Windows.Media.Color.FromRgb(100, 116, 139)       : System.Windows.Media.Color.FromRgb(148, 163, 184);
            System.Windows.Media.Color borderC = light ? System.Windows.Media.Color.FromArgb(45, 15, 23, 42)     : System.Windows.Media.Color.FromArgb(55, 255, 255, 255);
            System.Windows.Media.Color accent  = SystemParameters.WindowGlassColor;
            if (accent.A == 0) accent = System.Windows.Media.Color.FromRgb(99, 102, 241);

            win = new Window {
                Title = "StrataSmartToggle",
                Width = 368,
                SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                WindowStartupLocation = WindowStartupLocation.Manual
            };

            // Setup System Tray Icon
            string icoFile = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "strata-icon.ico");
            trayIcon = new WinForms.NotifyIcon();
            if (File.Exists(icoFile)) {
                trayIcon.Icon = new Icon(icoFile);
            } else {
                trayIcon.Icon = SystemIcons.Application;
            }
            trayIcon.Text = "StrataSmartToggle (Idle)";
            trayIcon.Visible = true;

            var trayMenu = new WinForms.ContextMenuStrip();
            trayMenu.Items.Add("Show Control Center", null, (s, e) => ShowWindow());
            trayMenu.Items.Add("Open Web UI (:8080)", null, (s, e) => Process.Start(new ProcessStartInfo("http://127.0.0.1:" + config.Port) { UseShellExecute = true }));
            trayMenu.Items.Add(new WinForms.ToolStripSeparator());
            trayMenu.Items.Add("Exit StrataSmartToggle", null, (s, e) => { allowClose = true; trayIcon.Visible = false; win.Close(); });
            trayIcon.ContextMenuStrip = trayMenu;

            trayIcon.MouseClick += (s, e) => {
                if (e.Button == WinForms.MouseButtons.Left) {
                    if (win.IsVisible && win.WindowState != WindowState.Minimized) {
                        HideWindow();
                    } else {
                        ShowWindow();
                    }
                }
            };

            win.SourceInitialized += (s, e) => {
                IntPtr hwnd = new WindowInteropHelper(win).Handle;
                int dark = light ? 0 : 1;
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            };

            win.Loaded += (s, e) => RepositionNearTaskbar();

            // Intercept close button (X) and minimize (-) to send window to tray instead of killing app
            win.Closing += (s, e) => {
                if (!allowClose) {
                    e.Cancel = true;
                    HideWindow();
                }
            };

            var card = new Border {
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(bgGlass),
                BorderBrush = new SolidColorBrush(borderC),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16)
            };
            card.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) win.DragMove(); };

            var root = new StackPanel();

            // --- HEADER ---
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var leftHdr = new StackPanel { Orientation = Orientation.Horizontal };
            statusDot = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var titleTxt = new TextBlock { Text = "STRATA SMART TOGGLE", FontWeight = FontWeights.Bold, FontSize = 12.5, Foreground = new SolidColorBrush(fgMain), VerticalAlignment = VerticalAlignment.Center };
            statusLabel = new TextBlock { Text = "IDLE", FontSize = 11, Margin = new Thickness(8, 0, 0, 0), Foreground = new SolidColorBrush(fgSub), VerticalAlignment = VerticalAlignment.Center };
            leftHdr.Children.Add(statusDot);
            leftHdr.Children.Add(titleTxt);
            leftHdr.Children.Add(statusLabel);

            var rightHdr = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            gearBtn = new Button {
                Content = "⚙", Width = 26, Height = 26, FontSize = 13,
                ToolTip = "Configure Model, Context & Path",
                Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(fgSub), Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 2, 0)
            };
            gearBtn.Click += (s, e) => ToggleSetupDrawer(!isSetupVisible);

            var minBtn = new Button {
                Content = "─", Width = 26, Height = 26, FontSize = 12,
                ToolTip = "Minimize to Tray",
                Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(fgSub), Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 2, 0)
            };
            minBtn.Click += (s, e) => HideWindow();

            var closeBtn = new Button {
                Content = "✕", Width = 26, Height = 26, FontSize = 12,
                ToolTip = "Minimize to Tray",
                Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(fgSub), Cursor = Cursors.Hand
            };
            closeBtn.Click += (s, e) => HideWindow();

            rightHdr.Children.Add(gearBtn);
            rightHdr.Children.Add(minBtn);
            rightHdr.Children.Add(closeBtn);
            DockPanel.SetDock(rightHdr, Dock.Right);
            header.Children.Add(rightHdr);
            header.Children.Add(leftHdr);
            root.Children.Add(header);

            // --- QUICK SUMMARY PILL ---
            summaryBadge = new Border {
                Background = new SolidColorBrush(surface),
                BorderBrush = new SolidColorBrush(borderC),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 12),
                Cursor = Cursors.Hand,
                ToolTip = "Click to change model, context or data directory"
            };
            summaryBadge.MouseLeftButtonUp += (s, e) => ToggleSetupDrawer(!isSetupVisible);
            summaryText = new TextBlock {
                FontSize = 12, FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(fgMain),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            summaryBadge.Child = summaryText;
            root.Children.Add(summaryBadge);

            // --- FIRST-TIME / SETUP DRAWER ---
            setupPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            setupPanel.Children.Add(new TextBlock {
                Text = "Model / Quantization (from MODELS.md or installed)",
                FontSize = 11, Foreground = new SolidColorBrush(fgSub), Margin = new Thickness(0, 0, 0, 4)
            });
            modelBox = new ComboBox {
                IsEditable = true, Height = 30, FontSize = 12.5,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            PopulateModels();
            modelBox.Text = config.Model;
            setupPanel.Children.Add(modelBox);

            setupPanel.Children.Add(new TextBlock {
                Text = "Context Window (--context)",
                FontSize = 11, Foreground = new SolidColorBrush(fgSub), Margin = new Thickness(0, 0, 0, 4)
            });
            var ctxGrid = new UniformGrid { Columns = 4, Height = 30, Margin = new Thickness(0, 0, 0, 10) };
            ctxButtons = new Button[ctxOptions.Length];
            for (int i = 0; i < ctxOptions.Length; i++) {
                string c = ctxOptions[i];
                var b = new Button {
                    Content = c.ToUpper(),
                    Margin = new Thickness(i == 0 ? 0 : 4, 0, 0, 0),
                    Cursor = Cursors.Hand, FontWeight = FontWeights.SemiBold, FontSize = 11.5,
                    BorderThickness = new Thickness(1)
                };
                b.Click += (s, e) => {
                    config.Context = c;
                    RefreshContextPills(accent, surface, fgMain, borderC);
                    UpdateSummaryBanner();
                };
                ctxButtons[i] = b;
                ctxGrid.Children.Add(b);
            }
            RefreshContextPills(accent, surface, fgMain, borderC);
            setupPanel.Children.Add(ctxGrid);

            setupPanel.Children.Add(new TextBlock {
                Text = "Strata Data Folder (--data-dir)",
                FontSize = 11, Foreground = new SolidColorBrush(fgSub), Margin = new Thickness(0, 0, 0, 4)
            });
            var dirRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var browseBtn = new Button {
                Content = "Browse...", Width = 68, Height = 28, FontSize = 11.5,
                Margin = new Thickness(6, 0, 0, 0), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(surface), Foreground = new SolidColorBrush(fgMain),
                BorderBrush = new SolidColorBrush(borderC), BorderThickness = new Thickness(1)
            };
            browseBtn.Click += (s, e) => {
                var dlg = new WinForms.FolderBrowserDialog();
                dlg.Description = "Select Strata Data Directory";
                if (Directory.Exists(dirBox.Text)) dlg.SelectedPath = dirBox.Text;
                if (dlg.ShowDialog() == WinForms.DialogResult.OK) {
                    dirBox.Text = dlg.SelectedPath;
                }
            };
            DockPanel.SetDock(browseBtn, Dock.Right);

            dirBox = new TextBox {
                Text = config.DataDir, Height = 28, FontSize = 12,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(surface), Foreground = new SolidColorBrush(fgMain),
                BorderBrush = new SolidColorBrush(borderC), Padding = new Thickness(6, 0, 6, 0)
            };
            dirRow.Children.Add(browseBtn);
            dirRow.Children.Add(dirBox);
            setupPanel.Children.Add(dirRow);

            root.Children.Add(setupPanel);

            // --- PRIMARY ACTION BUTTON ---
            actionBtn = new Button {
                Height = 40, FontSize = 13, FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White, Cursor = Cursors.Hand,
                BorderThickness = new Thickness(0), Margin = new Thickness(0, 2, 0, 8)
            };
            actionBtn.Click += async (s, e) => await ExecuteToggleAsync();
            root.Children.Add(actionBtn);

            // --- FOOTER ---
            var footer = new UniformGrid { Columns = 2, Height = 24 };
            var webBtn = new Button {
                Content = "Open Web UI (:8080)", FontSize = 11,
                Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(fgSub), Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            webBtn.Click += (s, e) => Process.Start(new ProcessStartInfo("http://127.0.0.1:" + config.Port) { UseShellExecute = true });

            var docsBtn = new Button {
                Content = "GitHub ↗", FontSize = 11,
                Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(fgSub), Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            docsBtn.Click += (s, e) => Process.Start(new ProcessStartInfo("https://github.com/VKVstudio/StrataSmartToggle") { UseShellExecute = true });

            footer.Children.Add(webBtn);
            footer.Children.Add(docsBtn);
            root.Children.Add(footer);

            card.Child = root;
            win.Content = card;

            ToggleSetupDrawer(!config.Configured);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            timer.Tick += (s, e) => UpdateLiveStatus(accent);
            timer.Start();
            UpdateLiveStatus(accent);

            return win;
        }

        private void ShowWindow()
        {
            win.Show();
            win.WindowState = WindowState.Normal;
            win.Activate();
            RepositionNearTaskbar();
        }

        private void HideWindow()
        {
            win.Hide();
        }

        private void PopulateModels()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in OfficialModels) {
                if (seen.Add(m)) modelBox.Items.Add(m);
            }
            try {
                string modelsDir = System.IO.Path.Combine(config.DataDir, "models");
                if (Directory.Exists(modelsDir)) {
                    foreach (var d in Directory.GetDirectories(modelsDir)) {
                        string name = System.IO.Path.GetFileName(d);
                        if (!string.IsNullOrEmpty(name) && seen.Add(name)) modelBox.Items.Add(name);
                    }
                }
            } catch { }
        }

        private void ToggleSetupDrawer(bool show)
        {
            isSetupVisible = show;
            setupPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            summaryBadge.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            if (!show) {
                config.Model = modelBox.Text.Trim();
                config.DataDir = dirBox.Text.Trim();
                config.Save();
            }
            UpdateSummaryBanner();
            win.Dispatcher.BeginInvoke(new Action(RepositionNearTaskbar), DispatcherPriority.Render);
        }

        private void UpdateSummaryBanner()
        {
            string m = modelBox != null ? modelBox.Text.Trim() : config.Model;
            string d = dirBox != null ? dirBox.Text.Trim() : config.DataDir;
            summaryText.Text = string.Format("⚡ {0}  •  {1}  •  {2}", m, config.Context.ToUpper(), d);
        }

        private void RepositionNearTaskbar()
        {
            var wa = SystemParameters.WorkArea;
            win.Left = wa.Right - win.ActualWidth - 14;
            win.Top  = wa.Bottom - win.ActualHeight - 14;
        }

        private void RefreshContextPills(System.Windows.Media.Color accent, System.Windows.Media.Color surface, System.Windows.Media.Color fgMain, System.Windows.Media.Color borderC)
        {
            for (int i = 0; i < ctxButtons.Length; i++) {
                bool active = string.Equals(ctxOptions[i], config.Context, StringComparison.OrdinalIgnoreCase);
                ctxButtons[i].Background  = new SolidColorBrush(active ? accent : surface);
                ctxButtons[i].Foreground  = new SolidColorBrush(active ? System.Windows.Media.Colors.White : fgMain);
                ctxButtons[i].BorderBrush = new SolidColorBrush(active ? accent : borderC);
            }
        }

        private void UpdateLiveStatus(System.Windows.Media.Color accent)
        {
            if (isBusy) return;
            bool online = IsPortListening(config.Port);
            if (online) {
                statusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
                statusLabel.Text = "RUNNING • :" + config.Port;
                actionBtn.Content = "■  STOP STRATA & FREE VRAM";
                actionBtn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));
                if (trayIcon != null) trayIcon.Text = "StrataSmartToggle (" + config.Model + " @ " + config.Context.ToUpper() + " - RUNNING)";
            } else {
                statusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
                statusLabel.Text = config.Configured ? "READY" : "SETUP";
                string verb = isSetupVisible ? "▶  SAVE & LAUNCH STRATA" : "▶  LAUNCH STRATA (" + config.Context.ToUpper() + ")";
                actionBtn.Content = verb;
                actionBtn.Background = new SolidColorBrush(accent);
                if (trayIcon != null) trayIcon.Text = "StrataSmartToggle (Idle)";
            }
        }

        private async Task ExecuteToggleAsync()
        {
            if (isBusy) return;
            isBusy = true;
            actionBtn.IsEnabled = false;

            config.Model = modelBox.Text.Trim();
            config.DataDir = dirBox.Text.Trim();
            config.Save();
            if (isSetupVisible) ToggleSetupDrawer(false);

            bool online = IsPortListening(config.Port);
            try {
                if (online) {
                    statusLabel.Text = "STOPPING...";
                    actionBtn.Content = "Unloading experts from VRAM & RAM...";
                    await Task.Run(() => {
                        string stopCmd = string.Format(
                            "/c strata launch --stop --data-dir \"{0}\" & taskkill /FI \"WINDOWTITLE eq STRATA [*\" /T /F >nul 2>&1",
                            config.DataDir
                        );
                        var psi = new ProcessStartInfo("cmd.exe", stopCmd) {
                            WorkingDirectory = Directory.Exists(config.DataDir) ? config.DataDir : "C:\\",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        using (var p = Process.Start(psi)) {
                            if (p != null) p.WaitForExit(15000);
                        }
                    });
                    for (int i = 0; i < 20 && IsPortListening(config.Port); i++) {
                        await Task.Delay(400);
                    }
                } else {
                    statusLabel.Text = "STARTING...";
                    actionBtn.Content = "Starting Strata Monitor...";
                    string modelFlag = config.Model.StartsWith("Gyro-", StringComparison.OrdinalIgnoreCase)
                        ? string.Format("--preset \"{0}\"", config.Model)
                        : string.Format("--model \"{0}\"", config.Model);

                    string cmdArgs = string.Format(
                        "/c title STRATA [{0} @ {1}] & strata launch {2} --data-dir \"{3}\" --context {1}",
                        config.Model, config.Context, modelFlag, config.DataDir
                    );

                    Process.Start(new ProcessStartInfo("cmd.exe", cmdArgs) {
                        WorkingDirectory = Directory.Exists(config.DataDir) ? config.DataDir : "C:\\",
                        UseShellExecute = true
                    });

                    for (int i = 0; i < 25 && !IsPortListening(config.Port); i++) {
                        await Task.Delay(500);
                    }
                }
            } catch (Exception ex) {
                System.Windows.Forms.MessageBox.Show("Failed to execute Strata CLI:\n" + ex.Message, "StrataSmartToggle", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Warning);
            } finally {
                isBusy = false;
                actionBtn.IsEnabled = true;
                UpdateLiveStatus(SystemParameters.WindowGlassColor);
            }
        }
    }
}
