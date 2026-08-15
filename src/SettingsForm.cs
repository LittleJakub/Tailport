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

        private TextBox AddRow(string label, string value, int pad, ref int y,
                               int labelW, int inputW, int rowH)
        {
            var lbl = new Label
            {
                Text = label,
                Left = pad,
                Top = y + 5,
                Width = labelW,
                ForeColor = _c.Text,
                Font = Font
            };
            Controls.Add(lbl);
            var box = new TextBox
            {
                Left = pad + labelW,
                Top = y,
                Width = inputW,
                Text = value,
                BackColor = InputBg,
                ForeColor = _c.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Font
            };
            Controls.Add(box);
            y += rowH;
            return box;
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
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 470);
            BackColor = _c.Bg;
            ForeColor = _c.Text;

            const int pad = 20, labelW = 140, inputW = 260, rowH = 34;
            int y = pad;

            _distro = AddRow("WSL distro", Get(_cfg, "wsl_distro", "Ubuntu"), pad, ref y, labelW, inputW, rowH);
            _pythonw = AddRow("Python (pythonw)", Get(_cfg, "pythonw", ""), pad, ref y, labelW, inputW, rowH);
            _socksHost = AddRow("SOCKS host", Get(_cfg, "socks_host", "127.0.0.1"), pad, ref y, labelW, inputW, rowH);
            _socksPort = AddRow("SOCKS port", Get(_cfg, "socks_port", "1055"), pad, ref y, labelW, inputW, rowH);
            _llmPort = AddRow("LLM local port", Get(_cfg, "llm_local_port", "8080"), pad, ref y, labelW, inputW, rowH);
            _llmTarget = AddRow("LLM target (ip:port)", Get(_cfg, "llm_target", ""), pad, ref y, labelW, inputW, rowH);

            var lbl = new Label
            {
                Text = "Port forwards",
                Left = pad,
                Top = y + 4,
                Width = labelW,
                ForeColor = _c.Text,
                Font = Font
            };
            Controls.Add(lbl);
            _forwards = new TextBox
            {
                Left = pad + labelW,
                Top = y,
                Width = inputW,
                Height = 84,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = InputBg,
                ForeColor = _c.Text,
                BorderStyle = BorderStyle.FixedSingle,
                Font = Font,
                AcceptsReturn = true
            };
            _forwards.Text = string.Join("\r\n", ForwardLines());
            Controls.Add(_forwards);
            y += 104;

            var hint = new Label
            {
                Text = "One per line: local:tailnet-ip:port\r\n(e.g. 2283:100.101.102.103:2283). Empty = LLM only.",
                Left = pad + labelW,
                Top = y,
                Width = inputW,
                Height = 32,
                ForeColor = _c.TextDisabled,
                Font = Font
            };
            Controls.Add(hint);
            y += 44;

            var save = new Button
            {
                Text = "Save",
                Left = pad + labelW + inputW - 164,
                Top = y + 8,
                Width = 78,
                Height = 28,
                FlatStyle = FlatStyle.Flat
            };
            save.FlatAppearance.BorderSize = 0;
            save.BackColor = Color.FromArgb(124, 58, 237); // brand violet #7C3AED
            save.ForeColor = Color.White;
            save.Click += delegate { SaveClick(); };
            Controls.Add(save);

            var cancel = new Button
            {
                Text = "Cancel",
                Left = pad + labelW + inputW - 82,
                Top = y + 8,
                Width = 82,
                Height = 28,
                FlatStyle = FlatStyle.Flat
            };
            cancel.FlatAppearance.BorderColor = _c.Border;
            cancel.BackColor = _c.Bg;
            cancel.ForeColor = _c.Text;
            cancel.Click += delegate { Close(); };
            Controls.Add(cancel);
            y += 52;

            var footer = new Label
            {
                Text = "Saved changes apply on the next Turn ON.",
                Left = pad,
                Top = y,
                Width = 420,
                ForeColor = _c.TextDisabled,
                Font = Font
            };
            Controls.Add(footer);

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
