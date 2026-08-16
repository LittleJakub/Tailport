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

        private TextBox _distro, _pythonw, _socksHost, _socksPort, _forwards;
        private Button _cancel;
        private readonly ToolTip _tip = new ToolTip();
        // design-time (96dpi) box heights, captured when each TextBox is created:
        // Scale() doubles Top/Left/Width but single-line TextBoxes are
        // font-locked and keep their height -> the scaled layout leaves a
        // shortfall between a box's real bottom and its hint. OnLoad compacts
        // rows by that shortfall (see CompactAfterScale).
        private readonly Dictionary<TextBox, int> _designHeights = new Dictionary<TextBox, int>();

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

            // Scale() doubles Top/Left/Width, but single-line TextBoxes are
            // font-locked and keep their design height (e.g. 39px at any DPI).
            // Hints (placed at design box.Bottom + 4) and every following row
            // would therefore float `designH*s - realH` px too low at >100%:
            // that's the dead space between each field and its hint. Shift each
            // row up by its box's shortfall so hints land at real bottom + 4*s.
            CompactAfterScale(s);

            // FixedSingle borders do NOT scale (1px at any DPI): a 160-wide
            // box becomes 318 at 200% (client scales, border doesn't), so
            // the boxes' real right edge ends 2px short of pad+fullW*s.
            // Right-align Cancel to the boxes' ACTUAL right edge so the
            // "cancel right edge == text box right edge" rule holds at
            // every DPI (Scale() leaves buttons exact: 80 -> 160). Done
            // BEFORE self-size so the window fits the aligned row exactly.
            if (_cancel != null && _forwards != null)
                _cancel.Left = _forwards.Right - _cancel.Width;

            // self-size: fit the window to the (possibly compacted) content -
            // right pad mirrors the left content pad (28 logical) so the
            // window stays symmetric at every DPI; height keeps a scaled
            // margin. Grows AND shrinks so no dead space remains at any DPI.
            int maxRight = 0, maxBottom = 0;
            foreach (Control c in Controls)
            {
                if (c.Right > maxRight) maxRight = c.Right;
                if (c.Bottom > maxBottom) maxBottom = c.Bottom;
            }
            const int pad = 9; // small content pad, mirrored on the right
            int wantW = Math.Max(maxRight + (int)(pad * s), 300);
            int wantH = Math.Max(maxBottom + (int)(pad * s), 200); // bottom pad == side pad (user: match all four)
            if (wantW != ClientSize.Width || wantH != ClientSize.Height)
                ClientSize = new Size(wantW, wantH);

            // the python path is long: show its tail (the executable name)
            if (_pythonw != null && _pythonw.Text.Length > 0)
            {
                _pythonw.SelectionStart = _pythonw.Text.Length;
                _pythonw.ScrollToCaret();
            }
        }

        /// <summary>
        /// Single-line TextBoxes are font-locked: Scale() doubles their Top and
        /// Width but their Height stays at the design value. Every coordinate
        /// below a box was computed from the box's design bottom, so at &gt;100%
        /// DPI those coordinates land `designH*s - realH` px lower than the
        /// box's real bottom. Walk rows top-down: shift each row up by the
        /// shortfall accumulated from the box rows above it, then add the
        /// row's own box shortfall for the rows below. Hints then sit exactly
        /// 4*s px under their box's real bottom and row pitch matches real
        /// heights - correct by construction at any DPI (no-op at 100%).
        /// </summary>
        private void CompactAfterScale(float s)
        {
            var sorted = Controls.Cast<Control>()
                .OrderBy(c => c.Top).ThenBy(c => c.Left)
                .ToList();
            int shift = 0;
            int i = 0;
            while (i < sorted.Count)
            {
                // one "row" = all controls sharing the same original Top
                int rowTop = sorted[i].Top;
                var row = new List<Control>();
                while (i < sorted.Count && sorted[i].Top == rowTop)
                {
                    row.Add(sorted[i]);
                    i++;
                }
                foreach (var c in row)
                    c.Top -= shift;

                // a row that contains a font-locked box adds its shortfall
                // for every row below it (counted once per row)
                var tb = row.OfType<TextBox>().FirstOrDefault();
                int designH;
                if (tb != null && _designHeights.TryGetValue(tb, out designH))
                {
                    int shortfall = (int)(designH * s) - tb.Height;
                    if (shortfall > 0)
                        shift += shortfall;
                }
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
            var box = new TextBox
            {
                Left = 0, Top = 0, Width = width, Height = height,
                Text = value,
                BackColor = InputBg,
                ForeColor = _c.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Font
            };
            _designHeights[box] = box.Height; // font-locked height BEFORE Scale
            return box;
        }

        /// <summary>One field cell: label, 24px gap, input, hint (14px tall, descenders never clip).</summary>
        private TextBox AddCell(string label, string value, string hint, int x, int y, int w, int hintW = 0)
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

            // Hint anchored to the box's REAL bottom: single-line TextBoxes are
            // font-locked (39px @96dpi, 36px @192dpi) so a fixed offset would
            // overlap the box at some DPI. box.Bottom + 4 is correct everywhere.
            var h = new Label
            {
                Text = hint,
                Left = x,
                Top = box.Bottom + 4,
                Width = hintW > 0 ? hintW : w, // allow hints to span wider than the cell
                Height = 14,
                ForeColor = _c.TextDisabled,
                Font = new Font(Font.FontFamily, 8.25f),
                BackColor = Color.Transparent
            };
            Controls.Add(h);
            _tip.SetToolTip(box, hint);

            return box;
        }

        private int Section(string title, int pad, int y, int lineW)
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
                Width = lineW,
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

            const int pad = 9;             // small content pad - mirrored by self-size so left/right match
            const int cell = 160;            // 3-column grid cell (door section)
            const int cgap = 12;             // gap between grid cells
            const int fullW = cell * 3 + cgap * 2; // 504 = full content width
            int y = 9;                       // top pad == side pad (user: match all four)

            // ---- tailnet door ----
            // One 3-column row for the short values; the long python path gets a
            // full-width row of its own below (label directly above its field).
            y = Section("TAILNET DOOR", pad, y, fullW - 170);

            _distro = AddCell("WSL distro", Get(_cfg, "wsl_distro", "Ubuntu"),
                "Linux distro running tailscaled in WSL2.", pad, y, cell, 210);
            _socksHost = AddCell("SOCKS host", Get(_cfg, "socks_host", "127.0.0.1"),
                "SOCKS5 proxy exposed by tailscaled.", pad + cell + cgap, y, cell, 210);
            _socksPort = AddCell("SOCKS port", Get(_cfg, "socks_port", "1055"),
                "Tailnet door - usually 1055.", pad + 2 * (cell + cgap), y, cell);

            // Row pitch is derived from the box's REAL (font-locked) height so
            // hints stay below the boxes at every DPI: label gap 24 + boxH +
            // hint gap 4 + hint 14 + row gap 4. 39px @96dpi -> 85, 36px @192dpi -> 82.
            int boxH = _distro.Height;
            int pitch = 24 + boxH + 4 + 14 + 4;
            y += pitch;

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
                Top = _pythonw.Bottom + 4,
                Width = fullW,
                Height = 14,
                ForeColor = _c.TextDisabled,
                Font = new Font(Font.FontFamily, 8.25f),
                BackColor = Color.Transparent
            };
            Controls.Add(pyHint);
            _tip.SetToolTip(_pythonw, "Runs the forwarder invisibly. Empty = PATH.");
            y += pitch;

            // ---- port forwards (the whole service list - one list) ----
            y += 6;
            y = Section("PORT FORWARDS", pad, y, fullW - 170);

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

            _forwards = MakeBox(string.Join("\r\n", ForwardLines()), fullW, 84);
            _forwards.Left = pad;
            _forwards.Top = y + 24;
            _forwards.Multiline = true;
            _forwards.ScrollBars = ScrollBars.None; // system scrollbars stay light in dark mode
            _forwards.AcceptsReturn = true;
            Controls.Add(_forwards);

            var fhint = new Label
            {
                Text = "One per line: local:tailnet-ip:port. The smallest local port is the status anchor.",
                Left = pad,
                Top = _forwards.Bottom + 4,
                Width = fullW,
                Height = 14,
                ForeColor = _c.TextDisabled,
                Font = new Font(Font.FontFamily, 8.25f),
                BackColor = Color.Transparent
            };
            Controls.Add(fhint);
            _tip.SetToolTip(_forwards,
                "One per line: local:tailnet-ip:port (e.g. 2283:100.101.102.103:2283). The smallest local port is the status anchor.");
            // footer row anchors to the hint's REAL bottom (box height varies)
            y = fhint.Bottom + 12; // breathy gap hint -> buttons (user: "a little more breathy")

            // ---- actions: footer left, buttons right ----
            var footer = new Label
            {
                Text = "Saved changes apply on the next Turn ON.",
                Left = pad,
                Top = y + 4, // up a tad (user): centers the text on the 28px buttons
                Width = 300, // keep clear of the button row (buttons start at pad+fullW-170)
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
                FlatStyle = FlatStyle.Flat
                // NO Anchor: the form self-sizes its ClientSize after Scale(),
                // and Top|Right anchors recalculate from the scaled size - the
                // buttons drift/separate. Absolute positions keep them put.
            };
            save.FlatAppearance.BorderSize = 0;
            save.BackColor = Color.FromArgb(124, 58, 237); // brand violet #7C3AED
            save.ForeColor = Color.White;
            save.Click += delegate { SaveClick(); };
            Controls.Add(save);

            var cancel = new Button
            {
                Text = "Cancel",
                Left = pad + fullW - 86, // right edge aligns with the text boxes' right edge
                Top = y,
                Width = 86,
                Height = 28,
                FlatStyle = FlatStyle.Flat
                // no Anchor - see Save
            };
            _cancel = cancel;
            cancel.FlatAppearance.BorderColor = _c.Border;
            cancel.BackColor = _c.Bg;
            cancel.ForeColor = _c.Text;
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);

            AcceptButton = save;
            CancelButton = cancel;

            // A transparent Label paints its PARENT's background over any
            // control added before it - the footer would erase the Save
            // button's fill and text. Buttons must sit on top of the row.
            save.BringToFront();
            cancel.BringToFront();
        }

        private void SaveClick()
        {
            var lines = new List<string>();

            string pythonw = _pythonw.Text.Trim();
            string distro = _distro.Text.Trim();
            string sHost = _socksHost.Text.Trim();
            string sPort = _socksPort.Text.Trim();

            if (distro.Length == 0) { Warn("WSL distro cannot be empty."); return; }
            int ignored;
            if (!int.TryParse(sPort, out ignored) || ignored <= 0) { Warn("SOCKS port must be a number."); return; }

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
            if (forwards.Count == 0)
            {
                Warn("Add at least one forward (local:tailnet-ip:port) - the door needs a service list.");
                return;
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
            lines.Add("# port forwards: local:tailnet-ip:port (one list)");
            for (int i = 0; i < forwards.Count; i++)
                lines.Add("forward." + (i + 1) + "=" + forwards[i]);
            lines.Add("");

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
