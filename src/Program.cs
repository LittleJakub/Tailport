using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace TailnetForward
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, "TailnetForward.SingleInstance", out createdNew))
            {
                if (!createdNew)
                    return; // another instance is already running
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
        }
    }

    internal class TrayContext : ApplicationContext
    {
        // ---- everything is relative to the exe folder: the app is portable ----
        private static readonly string AppBase = AppDomain.CurrentDomain.BaseDirectory;
        private static readonly string AssetsDir = Path.Combine(AppBase, "assets");
        private static readonly string PythonW =
            @"C:\Users\user\AppData\Local\Programs\Python\Python311\pythonw.exe";
        private static readonly string ForwarderScript = Path.Combine(AppBase, "tailnet-forward.py");
        private static readonly string ForwarderPid = Path.Combine(AppBase, "tailnet-forward.pid");
        private static readonly string KeeperPid = Path.Combine(AppBase, "keeper.pid");
        private static readonly string LogFile = Path.Combine(AppBase, "tailnet-forward.log");
        private const string HealthUrl = "http://127.0.0.1:8080/health";

        private readonly NotifyIcon _icon = new NotifyIcon();
        private readonly SynchronizationContext _ui;
        private ToolStripMenuItem _statusItem;
        private ToolStripMenuItem _toggleItem;
        private Icon _currentIcon;
        private DateTime _lastLeftClick;
        private volatile bool _running;
        private volatile bool _reachable;

        public TrayContext()
        {
            _ui = SynchronizationContext.Current ?? new SynchronizationContext();
            BuildMenu();

            var timer = new System.Windows.Forms.Timer { Interval = 10000 };
            timer.Tick += delegate { RefreshStatus(); };
            timer.Start();

            _icon.Visible = true;
            RefreshStatus();
        }

        // ================= menu =================

        private void BuildMenu()
        {
            var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = true };

            var header = new ToolStripMenuItem("Tailnet Forward")
            {
                Enabled = false,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            _statusItem = new ToolStripMenuItem("Status: checking...") { Enabled = false };
            _toggleItem = new ToolStripMenuItem("Turn ON", LoadImg("menu_power.png"));
            _toggleItem.Click += delegate { Toggle(); };

            var check = new ToolStripMenuItem("Check connection", LoadImg("menu_refresh.png"));
            check.Click += delegate
            {
                RefreshStatus();
                _icon.ShowBalloonTip(2500, "Tailnet Forward", StatusLine(), ToolTipIcon.Info);
            };

            var openLog = new ToolStripMenuItem("Open log file", LoadImg("menu_log.png"));
            openLog.Click += delegate { OpenFile(LogFile); };

            var openFolder = new ToolStripMenuItem("Open folder", LoadImg("menu_folder.png"));
            openFolder.Click += delegate { Process.Start("explorer.exe", AppBase); };

            var quit = new ToolStripMenuItem("Quit", LoadImg("menu_quit.png"));
            quit.Click += delegate
            {
                _icon.Visible = false;
                TurnOff(); // full service shutdown on quit
                Application.Exit();
            };

            menu.Items.Add(header);
            menu.Items.Add(_statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_toggleItem);
            menu.Items.Add(check);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(openLog);
            menu.Items.Add(openFolder);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(quit);

            _icon.ContextMenuStrip = menu;

            // left-click opens the menu (OneDrive-style); debounced
            // so the two events below can't double-fire.
            MouseEventHandler onLeft = delegate (object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left)
                    return;
                var now = DateTime.Now;
                if ((now - _lastLeftClick).TotalMilliseconds < 400)
                    return;
                _lastLeftClick = now;
                if (menu != null)
                    menu.Show(Control.MousePosition);
            };
            _icon.MouseClick += onLeft;
            _icon.MouseUp += onLeft;
        }

        // ================= toggle =================

        private void Toggle()
        {
            _toggleItem.Enabled = false;
            var worker = new Thread(delegate () { ToggleWorker(); })
            {
                IsBackground = true,
                Name = "TailnetToggle"
            };
            worker.Start();
        }

        private void ToggleWorker()
        {
            try
            {
                if (_running)
                {
                    TurnOff();
                    Ui(delegate { _icon.ShowBalloonTip(2500, "Tailnet Forward", "Service OFF", ToolTipIcon.Info); });
                }
                else
                {
                    TurnOn();
                    Ui(delegate { _icon.ShowBalloonTip(2500, "Tailnet Forward", "Service ON - syncing...", ToolTipIcon.Info); });
                }
            }
            catch (Exception ex)
            {
                Ui(delegate
                {
                    _icon.ShowBalloonTip(4000, "Tailnet Forward", "Error: " + ex.Message, ToolTipIcon.Error);
                });
            }
            finally
            {
                Ui(delegate { _toggleItem.Enabled = true; RefreshStatus(); });
            }
        }

        private void TurnOn()
        {
            // 0) sweep any stray keepers from previous runs
            KillStrayKeepers();
            // 1) boot WSL + start tailscaled (same as start.cmd)
            RunHidden("wsl", "-d Ubuntu -u root -- systemctl start tailscaled");
            // 2) kick the tailnet session into sync (no-op if already connected)
            RunHidden("wsl", "-d Ubuntu -u root -- timeout 20 tailscale up --accept-dns=true");
            // 3) launch the forwarder (hidden)
            Process.Start(new ProcessStartInfo(PythonW,
                "\"" + ForwarderScript + "\" --local 8080 --host 100.101.102.103 --port 8080")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            // 4) keeper: hold the WSL VM open (it dies ~60 s after the last wsl client)
            Process.Start(new ProcessStartInfo("wsl", "-d Ubuntu -u root -- sleep infinity")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }

        private void TurnOff()
        {
            KillByPidFile(ForwarderPid);
            KillByPidFile(KeeperPid);
            KillStrayKeepers();
        }

        /// <summary>Kill every wsl.exe running `sleep infinity` (stray keepers
        /// accumulate if the app toggled multiple times).</summary>
        private static void KillStrayKeepers()
        {
            try
            {
                var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId FROM Win32_Process WHERE Name='wsl.exe' AND CommandLine LIKE '%sleep infinity%'");
                foreach (ManagementObject mo in searcher.Get())
                {
                    try
                    {
                        int pid = Convert.ToInt32(mo["ProcessId"]);
                        try { Process.GetProcessById(pid).Kill(); } catch { }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // ================= status =================

        private void RefreshStatus()
        {
            var running = ProcessAlive(ForwarderPid);
            var reachable = running && ProbeHealth();
            _running = running;
            _reachable = reachable;

            // ON = white refresh glyph, OFF = greyed out (user's spec)
            string glyph = running ? "on.ico" : "off.ico";

            var newIcon = LoadIcon(Path.Combine(AssetsDir, glyph));
            if (newIcon != null)
            {
                var old = _currentIcon;
                _currentIcon = newIcon;
                _icon.Icon = newIcon;
                if (old != null)
                    old.Dispose();
            }

            _icon.Text = "Tailnet Forward - Service " + (running ? "ON" : "OFF");
            _toggleItem.Text = running ? "Turn OFF" : "Turn ON";
            _statusItem.Text = running ? "Service ON" : "Service OFF";
        }

        private string StatusLine()
        {
            if (!_running)
                return "Service OFF";
            return _reachable ? "Service ON - connection OK" : "Service ON - connection failed";
        }

        private static bool ProbeHealth()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(HealthUrl);
                req.Timeout = 3000;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                {
                    return resp.StatusCode == HttpStatusCode.OK
                        && sr.ReadToEnd().IndexOf("ok", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool ProcessAlive(string pidFile)
        {
            int pid = ReadPid(pidFile);
            if (pid <= 0)
                return false;
            try
            {
                using (var p = Process.GetProcessById(pid))
                    return !p.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static void KillByPidFile(string pidFile)
        {
            int pid = ReadPid(pidFile);
            if (pid <= 0)
                return;
            try { Process.GetProcessById(pid).Kill(); } catch { }
            try { File.Delete(pidFile); } catch { }
        }

        private static int ReadPid(string pidFile)
        {
            try
            {
                if (!File.Exists(pidFile))
                    return 0;
                int pid;
                return int.TryParse(File.ReadAllText(pidFile).Trim(), out pid) ? pid : 0;
            }
            catch
            {
                return 0;
            }
        }

        // ================= helpers =================

        private void Ui(Action action)
        {
            _ui.Post(delegate { action(); }, null);
        }

        private static void RunHidden(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (var p = Process.Start(psi))
            {
                if (p != null)
                    p.WaitForExit(30000);
            }
        }

        private static Image LoadImg(string name)
        {
            var path = Path.Combine(AssetsDir, name);
            if (File.Exists(path))
                return Image.FromFile(path);
            return null;
        }

        private static Icon LoadIcon(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                    return new Icon(fs);
            }
            catch
            {
                return null;
            }
        }

        private static void OpenFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    Process.Start("notepad.exe", path);
                else
                    Process.Start("explorer.exe", AppBase);
            }
            catch { }
        }
    }

    // ---------------- dark theme menu ----------------

    internal class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer()
            : base(new DarkColors())
        {
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Color.FromArgb(230, 230, 235);
            base.OnRenderItemText(e);
        }
    }

    internal class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(32, 32, 36);
        public override Color ImageMarginGradientBegin => Color.FromArgb(32, 32, 36);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(32, 32, 36);
        public override Color ImageMarginGradientEnd => Color.FromArgb(32, 32, 36);
        public override Color MenuItemSelected => Color.FromArgb(62, 62, 70);
        public override Color MenuItemBorder => Color.FromArgb(62, 62, 70);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(62, 62, 70);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(62, 62, 70);
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(45, 45, 52);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(45, 45, 52);
        public override Color MenuBorder => Color.FromArgb(45, 45, 52);
        public override Color SeparatorDark => Color.FromArgb(70, 70, 78);
        public override Color SeparatorLight => Color.FromArgb(70, 70, 78);
        public override Color ToolStripBorder => Color.FromArgb(45, 45, 52);
    }
}
