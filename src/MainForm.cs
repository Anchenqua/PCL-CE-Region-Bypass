using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace PclRegionBypass
{
    internal sealed class MainForm : Form
    {
        private readonly TextBox _pathBox;
        private readonly Button _browseButton;
        private readonly Button _startButton;
        private readonly Button _attachButton;
        private readonly Button _inspectButton;
        private readonly CheckBox _watchBox;
        private readonly RichTextBox _logBox;
        private readonly Label _statusLabel;
        private readonly System.Windows.Forms.Timer _pollTimer = new System.Windows.Forms.Timer();

        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _trayMenu;
        private readonly ToolStripMenuItem _autoStartItem;
        private readonly ToolStripMenuItem _silentItem;
        private readonly Icon _icon;

        private readonly bool _startHidden;
        private readonly EventWaitHandle _showEvent;
        private readonly System.Windows.Forms.Timer _housekeeping;

        private bool _shownOnce;
        private bool _exiting;
        private volatile bool _showRequested;
        private string _pendingBalloon;

        private LaunchSession _session;
        private int _waitedMs;

        public MainForm(string initialPath, bool startHidden, EventWaitHandle showEvent)
        {
            _startHidden = startHidden;
            _showEvent = showEvent;

            Text = AppInfo.Title;
            ClientSize = new Size(760, 540);
            MinimumSize = new Size(640, 420);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9f);
            AllowDrop = true;

            _icon = AppIcon.Create();
            Icon = _icon;

            var pathLabel = new Label
            {
                Text = "启动器路径",
                Location = new Point(14, 16),
                AutoSize = true
            };

            _pathBox = new TextBox
            {
                Location = new Point(100, 13),
                Width = 540,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            _browseButton = new Button
            {
                Text = "浏览…",
                Location = new Point(650, 11),
                Size = new Size(96, 27),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _browseButton.Click += BrowseClick;

            _startButton = new Button
            {
                Text = "启动并解除限制",
                Location = new Point(14, 52),
                Size = new Size(150, 34)
            };
            _startButton.Click += StartClick;

            _attachButton = new Button
            {
                Text = "附加到运行中",
                Location = new Point(172, 52),
                Size = new Size(150, 34)
            };
            _attachButton.Click += delegate { AttachToRunning(true); };

            _inspectButton = new Button
            {
                Text = "检测本机区域",
                Location = new Point(330, 52),
                Size = new Size(130, 34)
            };
            _inspectButton.Click += InspectClick;

            _watchBox = new CheckBox
            {
                Text = "持续监视（自更新后自动补丁）",
                Location = new Point(474, 60),
                AutoSize = true,
                Checked = true
            };

            _logBox = new RichTextBox
            {
                Location = new Point(14, 98),
                Size = new Size(732, 396),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                ReadOnly = true,
                BackColor = Color.FromArgb(28, 28, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                Font = new Font("Consolas", 9f),
                BorderStyle = BorderStyle.FixedSingle,
                DetectUrls = false
            };

            _statusLabel = new Label
            {
                Text = "就绪。补丁只作用于进程内存，不改动任何文件。",
                Location = new Point(14, 506),
                AutoSize = true,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            Controls.Add(pathLabel);
            Controls.Add(_pathBox);
            Controls.Add(_browseButton);
            Controls.Add(_startButton);
            Controls.Add(_attachButton);
            Controls.Add(_inspectButton);
            Controls.Add(_watchBox);
            Controls.Add(_logBox);
            Controls.Add(_statusLabel);

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;

            _pollTimer.Interval = 300;
            _pollTimer.Tick += PollTick;

            // ---- 托盘 ----
            _trayMenu = new ContextMenuStrip();

            var showItem = new ToolStripMenuItem("显示主界面");
            showItem.Font = new Font(_trayMenu.Font, FontStyle.Bold);
            showItem.Click += delegate { ShowFromTray(); };

            var attachItem = new ToolStripMenuItem("附加到运行中的 PCL");
            attachItem.Click += delegate { AttachToRunning(false); };

            _autoStartItem = new ToolStripMenuItem("开机启动");
            _autoStartItem.CheckOnClick = true;
            _autoStartItem.Click += delegate { ToggleAutoStart(_autoStartItem.Checked); };

            _silentItem = new ToolStripMenuItem("静默启动（启动后直接进托盘）");
            _silentItem.CheckOnClick = true;
            _silentItem.Click += delegate { ToggleSilent(_silentItem.Checked); };

            var exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += delegate { ExitApplication(); };

            _trayMenu.Items.Add(showItem);
            _trayMenu.Items.Add(attachItem);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(_autoStartItem);
            _trayMenu.Items.Add(_silentItem);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(exitItem);

            _tray = new NotifyIcon
            {
                Icon = _icon,
                Text = AppInfo.Name,
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _tray.DoubleClick += delegate { ShowFromTray(); };

            // ---- 配置 ----
            Settings.Load();
            AutoStart.Heal();

            _autoStartItem.Checked = AutoStart.IsEnabled();
            _silentItem.Checked = Settings.SilentStart;

            string detected = initialPath;
            if (string.IsNullOrEmpty(detected)) detected = Settings.LastLauncherPath;
            if (string.IsNullOrEmpty(detected) || !File.Exists(detected)) detected = Locator.FindLauncher(null);
            if (!string.IsNullOrEmpty(detected)) _pathBox.Text = detected;

            AppendLog("提示：选择 PCL2_CE_Release_x64.exe 或 PCL.exe，然后点「启动并解除限制」。");
            AppendLog("如果启动器已经在运行，点「附加到运行中」即可，不必重启它。");
            AppendLog("关闭本窗口默认会询问「最小化到托盘 / 直接退出」，托盘菜单里可改开机启动与静默启动。");
            if (string.IsNullOrEmpty(detected)) AppendLog("没有自动找到启动器，请手动浏览选择。");

            if (_startHidden) _pendingBalloon = "静默启动完成，已在托盘运行。双击托盘图标可打开窗口。";

            _housekeeping = new System.Windows.Forms.Timer { Interval = 400 };
            _housekeeping.Tick += HousekeepingTick;
            _housekeeping.Start();

            if (_showEvent != null) StartShowListener();
        }

        // ---------- 托盘与显隐 ----------

        /// <summary>静默启动时拦掉第一次「显示」，但仍然建立消息循环。</summary>
        protected override void SetVisibleCore(bool value)
        {
            if (_startHidden && !_shownOnce)
            {
                _shownOnce = true;
                base.SetVisibleCore(false);
                return;
            }

            base.SetVisibleCore(value);
        }

        private void StartShowListener()
        {
            var thread = new Thread(delegate()
            {
                while (true)
                {
                    try
                    {
                        _showEvent.WaitOne();
                        _showRequested = true;
                    }
                    catch
                    {
                        return;
                    }
                }
            });
            thread.IsBackground = true;
            thread.Name = "pcl-bypass-show-listener";
            thread.Start();
        }

        private void HousekeepingTick(object sender, EventArgs e)
        {
            if (_showRequested)
            {
                _showRequested = false;
                ShowFromTray();
            }

            if (_pendingBalloon != null)
            {
                string text = _pendingBalloon;
                _pendingBalloon = null;
                ShowBalloon(AppInfo.Name, text, ToolTipIcon.Info);
            }
        }

        public void ShowFromTray()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            if (!Visible) Show();
            Activate();
            BringToFront();
        }

        private void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;
        }

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            try
            {
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = text;
                _tray.BalloonTipIcon = icon;
                _tray.ShowBalloonTip(4000);
            }
            catch
            {
                // 托盘不可用时忽略
            }
        }

        private void ExitApplication()
        {
            _exiting = true;
            if (_tray != null) _tray.Visible = false;
            Close();
        }

        // ---------- 开关 ----------

        private void ToggleAutoStart(bool enabled)
        {
            try
            {
                AutoStart.SetEnabled(enabled);
                _autoStartItem.Checked = AutoStart.IsEnabled();
                AppendLog(enabled
                    ? "已开启开机启动：" + AutoStart.CurrentCommand()
                    : "已关闭开机启动。");
            }
            catch (Exception ex)
            {
                _autoStartItem.Checked = AutoStart.IsEnabled();
                AppendLog("设置开机启动失败：" + ex.Message);
                if (!Visible) ShowBalloon("设置开机启动失败", ex.Message, ToolTipIcon.Warning);
            }
        }

        private void ToggleSilent(bool enabled)
        {
            Settings.SilentStart = enabled;
            Settings.Save();
            AppendLog(enabled ? "已开启静默启动：下次启动不显示窗口，直接进托盘。" : "已关闭静默启动。");
        }

        // ---------- 关闭策略 ----------

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_exiting || e.CloseReason == CloseReason.WindowsShutDown ||
                e.CloseReason == CloseReason.TaskManagerClosing)
            {
                base.OnFormClosing(e);
                return;
            }

            string action = Settings.CloseAction;
            if (action == Settings.CloseAsk)
            {
                using (var dialog = new CloseChoiceDialog())
                {
                    dialog.Preselect(Settings.CloseTray);
                    DialogResult result = dialog.ShowDialog(this);
                    if (result != DialogResult.OK)
                    {
                        e.Cancel = true;
                        return;
                    }

                    action = dialog.Choice;
                    if (dialog.Remember)
                    {
                        Settings.CloseAction = action;
                        Settings.Save();
                    }
                }
            }

            if (action == Settings.CloseTray)
            {
                e.Cancel = true;
                HideToTray();
                ShowBalloon("仍在托盘运行", "双击托盘图标可重新打开窗口；右键菜单可退出。", ToolTipIcon.Info);
                return;
            }

            _exiting = true;
            if (_tray != null) _tray.Visible = false;
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_housekeeping != null) _housekeeping.Dispose();
                if (_pollTimer != null) _pollTimer.Dispose();
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                }

                if (_icon != null) _icon.Dispose();
            }

            base.Dispose(disposing);
        }

        // ---------- 原有功能 ----------

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return;
            if (string.Equals(Path.GetExtension(files[0]), ".exe", StringComparison.OrdinalIgnoreCase))
                _pathBox.Text = files[0];
        }

        private void BrowseClick(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "选择 PCL-CE 主程序";
                dialog.Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                string directory = Path.GetDirectoryName(_pathBox.Text);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                    dialog.InitialDirectory = directory;
                if (dialog.ShowDialog(this) == DialogResult.OK) _pathBox.Text = dialog.FileName;
            }
        }

        private void InspectClick(object sender, EventArgs e)
        {
            try
            {
                RegionSignals signals = RegionPolicyPort.ReadWindowsSignals();
                RegionAssessment assessment = RegionPolicyPort.Evaluate(signals);
                AppendLog("—— 本机地区信号 ——");
                AppendLog("GeoName              : " + Describe(signals.GeoName, signals.GeoNameAvailable));
                AppendLog("LocaleName           : " + Describe(signals.UserLocaleName, signals.UserLocaleAvailable));
                AppendLog("PreferredUILanguages : " + Describe(
                    signals.PreferredUiLanguages.Count == 0 ? null : signals.PreferredUiLanguages[0],
                    signals.PreferredUiLanguagesAvailable));
                AppendLog("TimeZone             : " + Describe(signals.TimeZoneId, signals.TimeZoneAvailable));
                AppendLog("DeviceRegion(HKLM)   : " +
                          (signals.DeviceRegionAvailable ? signals.DeviceRegionGeoId.ToString() : "不存在"));
                AppendLog("判定                 : " + assessment.ClassificationText + " / " + assessment.Reason +
                          " → " + (assessment.IsRestricted ? "受限" : "不受限"));
            }
            catch (Exception ex)
            {
                AppendLog("检测失败：" + ex.Message);
            }
        }

        private static string Describe(string value, bool available)
        {
            if (value == null) return "不可用";
            return value + (available ? "  [可用]" : "  [不可用]");
        }

        private void StartClick(object sender, EventArgs e)
        {
            string path = _pathBox.Text.Trim();
            if (!File.Exists(path))
            {
                MessageBox.Show(this, "找不到启动器：" + path, "路径无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_session != null && _session.ProcessId != 0)
            {
                DialogResult answer = MessageBox.Show(this, "已经启动过一次，确定再启动一个实例？", "确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) return;
            }

            _startButton.Enabled = false;
            _browseButton.Enabled = false;
            _statusLabel.Text = "正在启动…";

            try
            {
                _waitedMs = 0;
                _session = Launcher.Launch(path, string.Empty, false, _watchBox.Checked,
                    Launcher.DeviceRegionRequiresHook(), AppendLog);
                _pollTimer.Start();

                Settings.LastLauncherPath = path;
                Settings.Save();
            }
            catch (Exception ex)
            {
                _startButton.Enabled = true;
                _browseButton.Enabled = true;
                _statusLabel.Text = "启动失败。";
                AppendLog("启动失败：" + ex.Message);
            }
        }

        public void AttachToRunning(bool interactive)
        {
            Process target = FindRunningLauncher();
            if (target == null)
            {
                if (interactive && Visible)
                    MessageBox.Show(this, "没有找到正在运行的 PCL-CE。\r\n请先启动启动器，或用「启动并解除限制」。",
                        "未找到进程", MessageBoxButtons.OK, MessageBoxIcon.Information);
                else
                    ShowBalloon("未找到 PCL-CE", "没有检测到正在运行的启动器进程。", ToolTipIcon.Warning);
                return;
            }

            int pid = target.Id;
            string name = target.ProcessName;
            RememberPathOf(target);

            _attachButton.Enabled = false;
            _statusLabel.Text = "正在附加到 PID " + pid + "…";
            AppendLog("发现运行中的 " + name + "（PID " + pid + "），开始附加…");

            var thread = new Thread(delegate()
            {
                try
                {
                    Launcher.Attach(pid, Launcher.DeviceRegionRequiresHook(), AppendLog);
                    BeginInvoke(new Action(delegate
                    {
                        _attachButton.Enabled = true;
                        _statusLabel.Text = "PID " + pid + " 已解锁（重启启动器即恢复原状）。";
                    }));
                }
                catch (Exception ex)
                {
                    AppendLog("附加失败：" + ex.Message);
                    BeginInvoke(new Action(delegate
                    {
                        _attachButton.Enabled = true;
                        _statusLabel.Text = "附加失败，请查看日志。";
                    }));
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void RememberPathOf(Process process)
        {
            try
            {
                if (process.MainModule == null) return;
                string path = process.MainModule.FileName;
                if (string.IsNullOrEmpty(path)) return;
                _pathBox.Text = path;
                Settings.LastLauncherPath = path;
                Settings.Save();
            }
            catch
            {
                // 权限不足时拿不到路径，忽略
            }
        }

        private static Process FindRunningLauncher()
        {
            int self = Process.GetCurrentProcess().Id;
            Process found = null;
            foreach (Process process in Process.GetProcesses())
            {
                bool keep = false;
                try
                {
                    keep = process.Id != self &&
                           process.ProcessName.StartsWith("PCL", StringComparison.OrdinalIgnoreCase) &&
                           !process.ProcessName.StartsWith("PCLRegionBypass", StringComparison.OrdinalIgnoreCase) &&
                           process.MainWindowHandle != IntPtr.Zero;
                }
                catch
                {
                    keep = false;
                }

                if (keep && found == null) found = process;
                else process.Dispose();
            }

            return found;
        }

        private void PollTick(object sender, EventArgs e)
        {
            _waitedMs += _pollTimer.Interval;
            if (_session == null)
            {
                _pollTimer.Stop();
                return;
            }

            if (_session.CoreReady.IsSet)
            {
                _pollTimer.Stop();
                _startButton.Enabled = true;
                _browseButton.Enabled = true;
                _statusLabel.Text = _session.Error == null
                    ? "已解锁（PID " + _session.ProcessId + "）。关闭本窗口不会影响已运行的启动器。"
                    : "补丁失败，请查看日志。";
                return;
            }

            if (_waitedMs > 40000)
            {
                _pollTimer.Stop();
                _startButton.Enabled = true;
                _browseButton.Enabled = true;
                _statusLabel.Text = "等待超时，请查看日志。";
            }
        }

        private void AppendLog(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), text);
                return;
            }

            _logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine);
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }
    }
}
