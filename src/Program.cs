using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Tailport
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, "Tailport.SingleInstance", out createdNew))
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
        private static readonly string ForwarderScript = Path.Combine(AppBase, "forwarder.py");
        private static readonly string ForwarderPid = Path.Combine(AppBase, "forwarder.pid");
        private static readonly string KeeperPid = Path.Combine(AppBase, "keeper.pid");
        private static readonly string LogFile = Path.Combine(AppBase, "tailport.log");
        private const string HealthUrl = "http://127.0.0.1:8080/health";

        private readonly NotifyIcon _icon = new NotifyIcon();
        private readonly SynchronizationContext _ui;
        private readonly ModernColors _palette;
        private ToolStripMenuItem _statusItem;
        private ToolStripMenuItem _toggleItem;
        private Icon _currentIcon;
        private DateTime _lastLeftClick;
        private volatile bool _running;
        private volatile bool _reachable;

        public TrayContext()
        {
            _ui = SynchronizationContext.Current ?? new SynchronizationContext();
            _palette = new ModernColors(WinTheme.IsDark());
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
            var menu = new ContextMenuStrip
            {
                Renderer = new ModernRenderer(_palette),
                ShowImageMargin = false, // no icon column: text sits uniformly against the edges
                Font = new Font("Segoe UI", 9f),
                Padding = new Padding(4) // uniform breathing room inside the border
            };

            // Win11-style rounded corners + correct dark chrome on the dropdown window
            menu.Opened += delegate
            {
                try { WinTheme.Apply(menu.Handle, _palette.Dark); } catch { }
            };

            _statusItem = NewItem("Service OFF", false);
            // first row gets +2px top air so the menu's top gap matches the bottom gap
            _toggleItem = NewItem("Turn ON", true);
            _toggleItem.Click += delegate { Toggle(); };

            var check = NewItem("Check connection", true);
            check.Click += delegate
            {
                RefreshStatus();
                _icon.ShowBalloonTip(2500, "Tailport", StatusLine(), ToolTipIcon.Info);
            };

            var openLog = NewItem("Open log file", true);
            openLog.Click += delegate { OpenFile(LogFile); };

            var quit = NewItem("Quit", true);
            quit.Click += delegate
            {
                _icon.Visible = false;
                TurnOff(); // full service shutdown on quit
                Application.Exit();
            };

            menu.Items.Add(_statusItem);
            menu.Items.Add(Sep());
            menu.Items.Add(_toggleItem);
            menu.Items.Add(check);
            menu.Items.Add(Sep());
            menu.Items.Add(openLog);
            menu.Items.Add(Sep());
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

        private ToolStripMenuItem NewItem(string text, bool enabled)
        {
            var item = new ToolStripMenuItem(text)
            {
                Enabled = enabled,
                // row air via margin (the framework mishandles vertical item padding)
                Margin = new Padding(0, 5, 0, 5)
            };
            return item;
        }

        private static ToolStripSeparator Sep()
        {
            return new ToolStripSeparator();
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
                    Ui(delegate { _icon.ShowBalloonTip(2500, "Tailport", "Service OFF", ToolTipIcon.Info); });
                }
                else
                {
                    TurnOn();
                    Ui(delegate { _icon.ShowBalloonTip(2500, "Tailport", "Service ON - syncing...", ToolTipIcon.Info); });
                }
            }
            catch (Exception ex)
            {
                Ui(delegate
                {
                    _icon.ShowBalloonTip(4000, "Tailport", "Error: " + ex.Message, ToolTipIcon.Error);
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

            // ON = violet glyph, OFF = greyed out (brand states)
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

            _icon.Text = "Tailport - Service " + (running ? "ON" : "OFF");
            _toggleItem.Text = running ? "Turn OFF" : "Turn ON";
            _statusItem.Text = running ? "Service ON" : "Service OFF";
            _statusItem.Tag = running ? _palette.StateOn : _palette.StateOff;
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

    // ================= 2026 theme =================

    /// <summary>Windows shell helpers: system light/dark detection + DWM chrome.</summary>
    internal static class WinTheme
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_20 = 20; // Win11 22H2+
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_19 = 19; // older builds
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static bool IsDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    return k != null && (int)(k.GetValue("AppsUseLightTheme", 1) ?? 1) == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Round the window corners (Win11) and match the chrome to the theme.</summary>
        public static void Apply(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero)
                return;
            int darkVal = dark ? 1 : 0;
            // immersive dark mode: newer attribute first, older as fallback
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_20, ref darkVal, 4) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_19, ref darkVal, 4);
            int round = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
        }
    }

    /// <summary>Light/dark palettes with the Tailport violet accent.</summary>
    internal class ModernColors : ProfessionalColorTable
    {
        public bool Dark { get; }

        // base chrome
        public Color Bg { get; }
        public Color Border { get; }
        public Color Text { get; }
        public Color TextDisabled { get; }
        public Color HoverBg { get; }
        public Color HoverText { get; }
        public Color Separator { get; }
        public Color StateOn { get; }
        public Color StateOff { get; }

        public ModernColors(bool dark)
        {
            Dark = dark;
            if (dark)
            {
                Bg = Color.FromArgb(30, 30, 35);          // #1E1E23
                Border = Color.FromArgb(52, 52, 60);      // #34343C
                Text = Color.FromArgb(228, 228, 231);     // #E4E4E7
                TextDisabled = Color.FromArgb(113, 113, 122); // #71717A
                HoverBg = Color.FromArgb(61, 48, 115);    // violet-tinted #3D3073
                HoverText = Color.FromArgb(233, 228, 255);// #E9E4FF
                Separator = Color.FromArgb(44, 44, 51);   // #2C2C33
                StateOn = Color.FromArgb(110, 231, 183);  // pastel teal-green #6EE7B7
                StateOff = Color.FromArgb(252, 165, 165); // pastel red #FCA5A5
            }
            else
            {
                Bg = Color.White;
                Border = Color.FromArgb(228, 228, 231);   // #E4E4E7
                Text = Color.FromArgb(24, 24, 27);        // #18181B
                TextDisabled = Color.FromArgb(156, 163, 175); // #9CA3AF
                HoverBg = Color.FromArgb(243, 239, 253);  // #F3EFFD violet tint
                HoverText = Color.FromArgb(109, 40, 217); // #6D28D9
                Separator = Color.FromArgb(232, 232, 236);// #E8E8EC
                StateOn = Color.FromArgb(52, 211, 153);   // pastel teal-green #34D399
                StateOff = Color.FromArgb(248, 113, 113); // pastel red #F87171
            }
        }

        // --- ProfessionalColorTable wiring ---
        public override Color ToolStripDropDownBackground => Bg;
        public override Color ImageMarginGradientBegin => Bg;
        public override Color ImageMarginGradientMiddle => Bg;
        public override Color ImageMarginGradientEnd => Bg;
        public override Color MenuBorder => Border;
        public override Color ToolStripBorder => Border;
        public override Color SeparatorDark => Separator;
        public override Color SeparatorLight => Separator;
        public override Color MenuItemSelected => HoverBg;
        public override Color MenuItemBorder => HoverBg;
        public override Color MenuItemSelectedGradientBegin => HoverBg;
        public override Color MenuItemSelectedGradientEnd => HoverBg;
        public override Color MenuItemPressedGradientBegin => HoverBg;
        public override Color MenuItemPressedGradientEnd => HoverBg;
    }

    /// <summary>Renders the menu: rounded hover pills, palette text, state colors.</summary>
    internal class ModernRenderer : ToolStripProfessionalRenderer
    {
        private readonly ModernColors _c;

        // text margins: every row's text starts/ends at these offsets
        private const int LeftMargin = 16;
        private const int RightMargin = 16;

        public ModernRenderer(ModernColors colors)
            : base(colors)
        {
            _c = colors;
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            // pill + glow live HERE (fires once per item per paint, behind the text);
            // the text pass double-fires, which is why a pill there ended up in front
            if (!e.Item.Selected || !e.Item.Enabled)
                return;
            var item = e.Item;
            var f = item.Font ?? new Font("Segoe UI", 9f);
            var tr = TextRectFor(item, f);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // soft radial glow spilling beyond the highlight body
            var glowRect = tr;
            glowRect.Inflate(10, 6);
            using (var path = Rounded(glowRect, 8))
            using (var brush = new PathGradientBrush(path))
            {
                brush.CenterColor = Color.FromArgb(110, _c.HoverBg);
                brush.SurroundColors = new[] { Color.FromArgb(0, _c.HoverBg) };
                brush.CenterPoint = new PointF(
                    glowRect.X + glowRect.Width / 2f, glowRect.Y + glowRect.Height / 2f);
                g.FillPath(brush, path);
            }

            // highlight body
            var coreRect = tr;
            coreRect.Inflate(5, 2);
            using (var path = Rounded(coreRect, 5))
            using (var brush = new SolidBrush(Color.FromArgb(120, _c.HoverBg)))
                g.FillPath(brush, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // text only (drawn last -> always on top; may fire twice, harmless)
            var item = e.Item;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var f = e.TextFont ?? item.Font;

            var textRect = TextRectFor(item, f);

            Color col;
            if (item.Tag is Color state) col = state;
            else if (!item.Enabled) col = _c.TextDisabled;
            else if (item.Selected) col = _c.HoverText;
            else col = _c.Text;

            TextRenderer.DrawText(g, e.Text, f, textRect, col,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        /// <summary>Shared layout: text rect vertically centered in the row, my margins.</summary>
        private static Rectangle TextRectFor(ToolStripItem item, Font f)
        {
            return new Rectangle(LeftMargin, (item.Height - f.Height) / 2,
                                 item.Width - LeftMargin - RightMargin, f.Height);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var g = e.Graphics;
            var y = e.Item.Height / 2;
            using (var pen = new Pen(_c.Separator, 1f))
                g.DrawLine(pen, LeftMargin, y, e.Item.Width - RightMargin, y);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
