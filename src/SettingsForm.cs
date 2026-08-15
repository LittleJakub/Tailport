using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Tailport
{
    /// <summary>Settings window: edit tailport.config without touching the file.</summary>
    internal class SettingsForm : Form
    {
        private readonly string _path;
        private readonly ModernColors _c;
        private readonly Dictionary<string, string> _cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private TextBox _distro, _pythonw, _socksHost, _socksPort, _llmPort, _llmTarget, _forwards;
        private readonly ToolTip _tip = new ToolTip();

        public SettingsForm(string configPath)
        {
            _path = configPath;
            _c = new ModernColors(WinTheme.IsDark());
            ReadConfig();
            BuildUi();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WinTheme.Apply(Handle, _c.Dark);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // self-size: grow the window if any control exceeds it (safety net)
            int maxRight = 0, maxBottom = 0;
            foreach (Control c in Controls)
            {
                if (c.Right > maxRight) maxRight = c.Right;
                if (c.Bottom > maxBottom) maxBottom = c.Bottom;
            }
            if (maxRight + 24 > ClientSize.Width)
                ClientSize = new Size(maxRight + 24, ClientSize.Height);
            if (maxBottom + 24 > ClientSize.Height)
                ClientSize = new Size(ClientSize.Width, maxBottom + 24);

            // Render at the window's real DPI. WinForms' auto-scaling only fires
            // when AutoScaleDimensions (design) != CurrentAutoScaleDimensions
            // (runtime), but a form created fresh at 192dpi captures BOTH at 192
            // -> no scaling -> the layout would render at 96dpi size on a 200%
            // display (everything half-size). Scale() multiplies every child's
            // bounds by DeviceDpi/96; fonts need no scaling because a 9pt font
            // at 192dpi already renders at 2x physical size.
            float s = DeviceDpi / 96f;
            if (Math.Abs(s - 1f) > 0.01f)
                Scale(new SizeF(s, s));

            // the python path is long: show its tail (the executable name)
            if (_pythonw != null && _pythonw.Text.Length > 0)
            {
                _pythonw.SelectionStart = _pythonw.Text.Length;
                _pythonw.ScrollToCaret();
            }
        }

        private void ReadConfig()
        {
            try
            {
                if (!File.Exists(_path))
                    return;
                foreach (var raw in File.ReadAllLines(_path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                        continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    _cfg[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch { }
        }

        private static string Get(Dictionary<string, string> d, string k, string def)
        {
            string v;
            return d.TryGetValue(k, out v) && v.Length > 0 ? v : def;
        }

        private Color InputBg
        {
            get { return _c.Dark ? Color.FromArgb(38, 38, 43) : Color.White; }
        }

        private TextBox MakeBox(string value, int width, int height)
        {
            return new TextBox
            {
                Left = 0, Top = 0, Width = width, Height = height,
                Text = value,
                BackColor = InputBg,
                ForeColor = _c.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Font
            };
        }

        /// <summary>One field cell (70px pitch): label, 24px gap, input, hint (14px tall, descenders never clip).</summary>
        private TextBox AddCell(string label, string value, string hint, int x, int y, int w)
        {
            var lbl = new Label
            {
                Text = label,
                Left = x,
                Top = y,
                Width = w,
                ForeColor = _c.Text,
                Font = Font,
                BackColor = Color.Transparent // never paint over the field below
            };
            Controls.Add(lbl);

            var box = MakeBox(value, w, 26);
            box.Left = x;
            box.Top = y + 24;
            Controls.Add(box);

            var h = new Label
            {
                Text = hint,
                Left = x,
                Top = y + 52,
                Width = w,
                Height = 14,
                ForeColor = _c.TextDisabled,
                Font = new Font(Font.FontFamily, 8.25f),
                BackColor = Color.Transparent
            };
            Controls.Add(h);
            _tip.SetToolTip(box, hint);

            return box;
        }

        private int Section(string title, int pad, int y)
        {
            var lbl = new Label
            {
                Text = title,
                Left = pad,
                Top = y,
                Width = 160,
                ForeColor = _c.TextDisabled,
                Font = new Font(Font.FontFamily, 8.25f, FontStyle.Bold),
                BackColor = Color.Transparent
            };
            Controls.Add(lbl);

            var line = new Label
            {
                Left = pad + 170,
                Top = y + 7,
                Width = 480,
                Height = 1,
                BackColor = _c.Separator
            };
            Controls.Add(line);
            return y + 19;
        }

        private IEnumerable<string> ForwardLines()
        {
            var rows = new List<Tuple<int, string>>();
            foreach (var kv in _cfg)
            {
                if (!kv.Key.StartsWith("forward.", StringComparison.OrdinalIgnoreCase))
                    continue;
                int local;
                if (int.TryParse(kv.Value.Split(':')[0], out local))
                    rows.Add(Tuple.Create(local, kv.Value));
            }
            return rows.OrderBy(r => r.Item1).Select(r => r.Item2);
        }

        private void BuildUi()
        {
            Text = "Tailport settings";
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi; // WinForms scales layout to the window DPI
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            // AutoScaleMode stays None; ApplyDpiScale in OnLoad renders at the
            // window's real DPI so the layout is pixel-identical everywhere.
            ClientSize = new Size(740, 458);
            BackColor = _c.Bg;
            ForeColor = _c.Text;

            const int pad = 28;
            const int cell = 218;            // 3-column grid cell (door section)
            const int cgap = 14;             // gap between grid cells
            const int fullW = cell * 3 + cgap * 2; // 682 = full content width
            const int rowPitch = 70;
            int y = 12;

            // ---- tailnet door ----
            // One 3-column row for the short values; the long python path gets a
            // full-width row of its own below (label directly above its field).
            y = Section("TAILNET DOOR", pad, y);

            _distro = AddCell("WSL distro", Get(_cfg, "wsl_distro", "Ubuntu"),
                "Linux distro running tailscaled in WSL2.", pad, y, cell);
            _socksHost = AddCell("SOCKS host", Get(_cfg, "socks_host", "127.0.0.1"),
                "SOCKS5 proxy exposed by tailscaled.", pad + cell + cgap, y, cell);
            _socksPort = AddCell("SOCKS port", Get(_cfg, "socks_port", "1055"),
                "The tailnet door - usually 1055.", pad + 2 * (cell + cgap), y, cell);
            y += rowPitch;

            var pyLbl = new Label
            {
                Text = "Python (pythonw)",
                Left = pad,
                Top = y,
                Width = fullW,
                ForeColor = _c.Text,
                Font = Font,
                BackColor = Color.Transparent
            };
            Controls.Add(pyLbl);

            _pythonw = MakeBox(Get(_cfg, "pythonw", ""), fullW, 26);
            _pythonw.Left = pad;
            _pythonw.Top = y + 24;
            Controls.Add(_pythonw);
            var pyHint = new Label
            {
                Text = "Runs the forwarder invisibly. Empty = PATH.",
                Left = pad,
                Top = y + 57,
                Width = fullW,
                Height = 14,
                ForeColor = _c.TextDisabled,
                Font = new Font(Font.FontFamily, 8.25f),
                BackColor = Color.Transparent
            };
            Controls.Add(pyHint);
            _tip.SetToolTip(_pythonw, "Runs the forwarder invisibly. Empty = PATH.");
            y += 71;

            // ---- LLM forwarder ----
            y += 9;
            y = Section("LLM FORWARDER", pad, y);

            // Local port in grid column 1, target spanning columns 2-3.
            _llmPort = AddCell("Local port", Get(_cfg, "llm_local_port", "8080"),
                "Where the LLM answers here (status anchor).", pad, y, cell);
            _llmTarget = AddCell("Target (ip:port)", Get(_cfg, "llm_target", ""),
                "Your tailnet LLM - llama.cpp, Ollama...", pad + cell + cgap, y, cell * 2 + cgap);
            y += rowPitch;

            // ---- port forwards ----
            y += 9;
            y = Section("PORT FORWARDS", pad, y);

            var lbl = new Label
            {
                Text = "Forwards",
                Left = pad,
                Top = y,
                Width = fullW,
                ForeColor = _c.Text,
                Font = Font,
                BackColor = Color.Transparent
            };
            Controls.Add(lbl);

            _forwards = MakeBox(string.Join("\r\n", ForwardLines()), fullW, 52);
            _forwards.Left = pad;
            _forwards.Top = y + 24;
            _forwards.Multiline = true;
            _forwards.ScrollBars = ScrollBars.None; // system scrollbars stay light in dark mode
            _forwards.AcceptsReturn = true;
            Controls.Add(_forwards);

            var fhint = new Label
            {
                Text = "One per line: local:tailnet-ip:port   (e.g. 2283:100.101.102.103:2283).  Empty = LLM only.",
                Left = pad,
                Top = y + 80,
                Width = fullW,
                Height = 14,
                ForeColor = _c.TextDisabled,
                Font = new Font(Font.FontFamily, 8.25f),
                BackColor = Color.Transparent
            };
            Controls.Add(fhint);
            _tip.SetToolTip(_forwards, fhint.Text);
            y += 103;

            // ---- actions: footer left, buttons right ----
            var footer = new Label
            {
                Text = "Saved changes apply on the next Turn ON.",
                Left = pad,
                Top = y + 8,
                Width = 400,
                ForeColor = _c.TextDisabled,
                Font = Font,
                BackColor = Color.Transparent
            };
            Controls.Add(footer);

            var save = new Button
            {
                Text = "Save",
                Left = pad + fullW - 170,
                Top = y,
                Width = 80,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            save.FlatAppearance.BorderSize = 0;
            save.BackColor = Color.FromArgb(124, 58, 237); // brand violet #7C3AED
            save.ForeColor = Color.White;
            save.Click += delegate { SaveClick(); };
            Controls.Add(save);

            var cancel = new Button
            {
                Text = "Cancel",
                Left = pad + fullW - 86,
                Top = y,
                Width = 86,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            cancel.FlatAppearance.BorderColor = _c.Border;
            cancel.BackColor = _c.Bg;
            cancel.ForeColor = _c.Text;
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);

            AcceptButton = save;
            CancelButton = cancel;
        }

        private void SaveClick()
        {
            var lines = new List<string>();

            string pythonw = _pythonw.Text.Trim();
            string distro = _distro.Text.Trim();
            string sHost = _socksHost.Text.Trim();
            string sPort = _socksPort.Text.Trim();
            string lPort = _llmPort.Text.Trim();
            string lTarget = _llmTarget.Text.Trim();

            if (distro.Length == 0) { Warn("WSL distro cannot be empty."); return; }
            int ignored;
            if (!int.TryParse(sPort, out ignored) || ignored <= 0) { Warn("SOCKS port must be a number."); return; }
            if (!int.TryParse(lPort, out ignored) || ignored <= 0) { Warn("LLM local port must be a number."); return; }
            if (lTarget.IndexOf(':') <= 0) { Warn("LLM target must look like ip:port (e.g. 100.101.102.103:8080)."); return; }

            var forwards = new List<string>();
            foreach (var raw in _forwards.Text.Replace("\r", "").Split('\n'))
            {
                var f = raw.Trim();
                if (f.Length == 0)
                    continue;
                var parts = f.Split(':');
                int a, b;
                if (parts.Length != 3 || !int.TryParse(parts[0], out a) || !int.TryParse(parts[2], out b))
                {
                    Warn("Bad forward line: " + f + "\r\nUse: local:tailnet-ip:port");
                    return;
                }
                forwards.Add(f);
            }

            lines.Add("# ============================================================");
            lines.Add("#  Tailport configuration - edited from the Settings window.");
            lines.Add("#  Tailport is the Astrill-safe door to your Tailscale tailnet:");
            lines.Add("#  every service listed here answers on http://localhost:<port>.");
            lines.Add("# ============================================================");
            lines.Add("");
            lines.Add("# Python: full path to pythonw.exe (empty = use system PATH)");
            lines.Add("pythonw=" + pythonw);
            lines.Add("");
            lines.Add("# WSL2: the distro that runs tailscaled");
            lines.Add("wsl_distro=" + distro);
            lines.Add("");
            lines.Add("# tailnet door (usually leave as-is)");
            lines.Add("socks_host=" + sHost);
            lines.Add("socks_port=" + sPort);
            lines.Add("");
            lines.Add("# LLM forwarder (status anchor of the tray icon)");
            lines.Add("llm_local_port=" + lPort);
            lines.Add("llm_target=" + lTarget);
            lines.Add("");
            if (forwards.Count > 0)
            {
                lines.Add("# extra port forwards: local:tailnet-ip:port");
                for (int i = 0; i < forwards.Count; i++)
                    lines.Add("forward." + (i + 1) + "=" + forwards[i]);
                lines.Add("");
            }

            try
            {
                File.WriteAllLines(_path, lines);
            }
            catch (Exception ex)
            {
                Warn("Could not write " + _path + "\r\n" + ex.Message);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void Warn(string msg)
        {
            MessageBox.Show(this, msg, "Tailport settings",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
