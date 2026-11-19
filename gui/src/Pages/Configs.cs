using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace NetSuite
{
    // Scans every zapret profile (zapret\scan.ps1 does the dirty work in an
    // elevated copy of this exe) and shows the ranking. This is the thing NetFix
    // does well and net-suite did not have: see which profiles actually work.
    public class ConfigsPage : Page
    {
        class Hit
        {
            public string Profile = "";
            public int Score;
            public int Total = 2;
            public bool Ok;
            public int Ms;
            public string Detail = "";
            public bool Current;
        }

        class Row : Control
        {
            public Hit H;
            public bool Selected;
            public ConfigsPage Owner;

            public Row()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Height = 48;
                Cursor = Cursors.Hand;
            }

            protected override void OnClick(EventArgs e) { base.OnClick(e); if (Owner != null && H != null) Owner.Select(H.Profile); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush bg = new SolidBrush(BackColor)) g.FillRectangle(bg, ClientRectangle);
                Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
                Theme.Fill(g, r, 10, Selected ? Theme.Hover : Theme.Card);
                Theme.Stroke(g, r, 10, Selected ? Theme.Accent : Theme.BorderSoft);

                int w = Width;
                Theme.Text_(g, H.Profile, Theme.Bold, new Rectangle(14, 5, w - 220, 20), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                Theme.Text_(g, H.Detail.Length > 0 ? H.Detail : "не проверялся", Theme.Small,
                    new Rectangle(14, 24, w - 220, 18), Theme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                Color sc = H.Total == 0 ? Theme.Dim : (H.Score >= H.Total ? Theme.Ok : (H.Score > 0 ? Theme.Warn : Theme.Err));
                string scoreText = H.Total == 0 ? "—" : H.Score + "/" + H.Total;
                Rectangle sr = new Rectangle(w - 86, 14, 46, 20);
                Theme.Fill(g, sr, 10, Color.FromArgb(36, sc.R, sc.G, sc.B));
                Theme.Text_(g, scoreText, Theme.Small, sr, sc, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                if (H.Ms > 0)
                    Theme.Text_(g, string.Format("{0:0.0} с", H.Ms / 1000.0), Theme.Small,
                        new Rectangle(w - 150, 14, 56, 20), Theme.Muted,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

                if (H.Current)
                    Theme.Text_(g, "текущий", Theme.Small, new Rectangle(w - 214, 14, 56, 20), Theme.Accent,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
        }

        FlowLayoutPanel list;
        Btn bScan, bApply, bOpen;
        Bar bar;
        Label lblProgress, lblHint;
        System.Windows.Forms.Timer poll;
        string selected;
        string currentProfile = "";
        bool scanning;
        int shownCount;

        public ConfigsPage(MainForm host) : base(host, "Конфиги zapret",
            "перебор профилей по-настоящему: 20 штук, каждый проверяется на живых сайтах")
        {
            bScan = new Btn();
            bScan.Text = "Сканировать все";
            bScan.Style = BtnStyle.Primary;
            bScan.Click += delegate { StartScan(); };
            Controls.Add(bScan);

            bApply = new Btn();
            bApply.Text = "Применить выбранный";
            bApply.Enabled = false;
            bApply.Click += delegate { ApplySelected(); };
            Controls.Add(bApply);

            bOpen = new Btn();
            bOpen.Text = "Открыть журнал";
            bOpen.Style = BtnStyle.Subtle;
            bOpen.Click += delegate { OpenInShell(Path.Combine(Paths.LogsDir, "zapret-scan.log")); };
            Controls.Add(bOpen);

            bar = new Bar();
            bar.BackColor = Theme.Bg;
            bar.Visible = false;
            Controls.Add(bar);

            lblProgress = new Label();
            lblProgress.Font = Theme.Small;
            lblProgress.ForeColor = Theme.Dim;
            lblProgress.BackColor = Theme.Bg;
            Controls.Add(lblProgress);

            lblHint = new Label();
            lblHint.Font = Theme.Small;
            lblHint.ForeColor = Theme.Dim;
            lblHint.BackColor = Theme.Bg;
            Controls.Add(lblHint);

            list = new FlowLayoutPanel();
            list.FlowDirection = FlowDirection.TopDown;
            list.WrapContents = false;
            list.AutoScroll = true;
            list.BackColor = Theme.Bg;
            list.Padding = new Padding(0);
            list.Resize += delegate { FitRows(); };
            list.HandleCreated += delegate { Theme.DarkScroll(list); };
            Controls.Add(list);

            poll = new System.Windows.Forms.Timer();
            poll.Interval = 700;
            poll.Tick += delegate { PollProgress(); };
        }

        static void OpenInShell(string path)
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + path + "\""); } catch { }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (bScan == null) return;
            int w = Width, h = Height;
            bScan.SetBounds(0, 62, 168, 38);
            bApply.SetBounds(180, 62, 196, 38);
            bOpen.SetBounds(390, 62, 140, 38);
            lblProgress.SetBounds(544, 62, Math.Max(100, w - 544), 38);
            int listTop = 112;
            bar.SetBounds(0, listTop - 8, w, 8);
            list.SetBounds(0, listTop, w, Math.Max(80, h - listTop - 26));
            lblHint.SetBounds(0, h - 20, w, 18);
            lblHint.Text = "нужен UAC · профиль ~12 с, весь скан ~5 минут · служба zapret возвращается как была, даже если прервать";
            FitRows();
        }

        void FitRows()
        {
            if (list == null) return;
            int w = list.ClientSize.Width - 22;
            if (w < 200) w = 200;
            foreach (Control c in list.Controls) c.Width = w;
        }

        public override void OnShow()
        {
            LoadCurrentProfile();
            if (list.Controls.Count == 0) LoadResults();
        }

        void LoadCurrentProfile()
        {
            currentProfile = "";
            try
            {
                string f = Path.Combine(Paths.RepoRoot, "zapret\\current.json");
                if (File.Exists(f))
                {
                    Dictionary<string, object> d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(f));
                    object o;
                    if (d != null && d.TryGetValue("profile", out o) && o != null) currentProfile = Convert.ToString(o);
                }
            }
            catch { }
        }

        // ---------------- scan ----------------

        void StartScan()
        {
            if (scanning || Host.Busy) return;
            scanning = true;
            Host.Busy = true;
            selected = null;
            bApply.Enabled = false;
            bScan.Enabled = false;
            bScan.Text = "Сканирую…";
            list.Controls.Clear();
            shownCount = 0;
            bar.Value = 0.02;
            bar.Visible = true;
            lblProgress.Text = "запускаю сканер, подтвердите UAC";
            Log.Step("▶ скан профилей zapret (20 × 5 с)");
            poll.Start();

            Thread t = new Thread(delegate ()
            {
                Dictionary<string, object> req = new Dictionary<string, object>();
                req["settle"] = 5;
                Dictionary<string, object> res;
                string err;
                bool ok = Elev.Ask("scan", req, 20 * 60 * 1000, out res, out err);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        poll.Stop();
                        scanning = false;
                        Host.Busy = false;
                        bScan.Enabled = true;
                        bScan.Text = "Сканировать все";
                        if (ok)
                        {
                            Log.Good("скан завершён");
                            LoadResults();
                            bar.Value = 1;
                            bar.Visible = false;
                            lblProgress.Text = "готово";
                        }
                        else
                        {
                            Log.Bad("скан не удался: " + err);
                            lblProgress.Text = err;
                            bar.Value = 0;
                            bar.Visible = false;
                        }
                        UpdateButtons();
                        Host.RefreshStatus();
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void PollProgress()
        {
            try
            {
                string f = Path.Combine(Paths.LogsDir, "scan-progress.json");
                if (!File.Exists(f)) return;
                Dictionary<string, object> d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(f, Encoding.UTF8));
                if (d == null) return;
                int total = Num(d, "total"), index = Num(d, "index");
                string cur = Str(d, "current", "");
                bool done = false;
                object o;
                if (d.TryGetValue("done", out o) && o != null) done = Convert.ToBoolean(o);
                if (total > 0) bar.Value = Math.Min(1.0, (index + 0.5) / total);
                lblProgress.Text = done ? "готово" : string.Format("{0}/{1} · {2}", index, total, cur);
                object[] results = null;
                if (d.TryGetValue("results", out o) && o != null) results = AsArray(o);
                if (results != null) BuildRows(results);
            }
            catch { }
        }

        // ---------------- results ----------------

        void LoadResults()
        {
            try
            {
                string f = Path.Combine(Paths.LogsDir, "scan-results.json");
                if (!File.Exists(f)) { lblProgress.Text = "результатов ещё нет — нажмите «Сканировать все»"; return; }
                Dictionary<string, object> d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(f, Encoding.UTF8));
                object o;
                if (d == null || !d.TryGetValue("results", out o) || o == null)
                {
                    Log.Warn("в результатах скана нет списка профилей");
                    return;
                }
                object[] results = AsArray(o);
                if (results == null || results.Length == 0) return;
                BuildRows(results);
                object b;
                string best = d.TryGetValue("best", out b) && b != null ? Convert.ToString(b) : "";
                lblProgress.Text = best.Length > 0 ? "лучший профиль: " + best : "готово";
                bar.Value = 1;
                bar.Visible = false;
            }
            catch (Exception ex) { Log.Warn("не прочитал результаты: " + ex.Message); }
        }

        void BuildRows(object[] results)
        {
            LoadCurrentProfile();
            List<Hit> hits = new List<Hit>();
            foreach (object o in results)
            {
                Dictionary<string, object> d = o as Dictionary<string, object>;
                if (d == null) continue;
                Hit h = new Hit();
                h.Profile = Str(d, "profile", "?");
                h.Score = Num(d, "score");
                h.Ms = Num(d, "ms");
                h.Detail = Str(d, "detail", "");
                object ok;
                if (d.TryGetValue("ok", out ok) && ok != null) h.Ok = Convert.ToBoolean(ok);
                h.Current = currentProfile.Length > 0 && h.Profile == currentProfile;
                hits.Add(h);
            }
            list.SuspendLayout();
            list.Controls.Clear();
            foreach (Hit h in hits)
            {
                Row r = new Row();
                r.H = h;
                r.Owner = this;
                r.Margin = new Padding(0, 0, 0, 6);
                r.Width = Math.Max(200, list.ClientSize.Width - 22);
                if (selected != null && selected == h.Profile) r.Selected = true;
                list.Controls.Add(r);
            }
            list.ResumeLayout();
            shownCount = hits.Count;
        }

        // JavaScriptSerializer may hand back ArrayList or object[] depending on the
        // shape of the json, so never cast blindly - that silently swallowed all rows.
        static object[] AsArray(object o)
        {
            if (o == null) return null;
            object[] arr = o as object[];
            if (arr != null) return arr;
            System.Collections.IEnumerable en = o as System.Collections.IEnumerable;
            if (en == null || o is string) return null;
            List<object> items = new List<object>();
            foreach (object x in en) items.Add(x);
            return items.ToArray();
        }

        static string Str(Dictionary<string, object> d, string k, string fallback)
        {
            object o;
            if (!d.TryGetValue(k, out o) || o == null) return fallback;
            return Convert.ToString(o);
        }

        static int Num(Dictionary<string, object> d, string k)
        {
            object o;
            if (!d.TryGetValue(k, out o) || o == null) return 0;
            try { return Convert.ToInt32(o); } catch { return 0; }
        }

        public void Select(string profile)
        {
            selected = profile;
            foreach (Control c in list.Controls)
            {
                Row r = c as Row;
                if (r == null) continue;
                r.Selected = r.H != null && r.H.Profile == profile;
                r.Invalidate();
            }
            UpdateButtons();
        }

        void UpdateButtons()
        {
            bool can = selected != null && !scanning && !Host.Busy;
            bApply.Enabled = can;
            bApply.Text = selected == null ? "Применить выбранный" : "Применить: " + Short(selected);
        }

        static string Short(string s)
        {
            if (s == null) return "";
            s = s.Replace("general", "").Replace(".bat", "").Trim();
            return s.Length == 0 ? "general" : s;
        }

        void ApplySelected()
        {
            if (selected == null || Host.Busy) return;
            string target = selected;
            Host.Busy = true;
            UpdateButtons();
            bScan.Enabled = false;
            Log.Step("▶ применяю профиль " + target);
            Thread t = new Thread(delegate ()
            {
                Dictionary<string, object> req = new Dictionary<string, object>();
                req["profile"] = target;
                Dictionary<string, object> res;
                string err;
                bool ok = Elev.Ask("apply", req, 180000, out res, out err);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        Host.Busy = false;
                        bScan.Enabled = true;
                        if (ok)
                        {
                            Log.Good("профиль применён и проверен: " + target);
                            currentProfile = target;
                        }
                        else Log.Bad("не применилось: " + err);
                        if (res != null)
                        {
                            string outp = Elev.Json(res, "output", "");
                            if (outp.Length > 0)
                                foreach (string line in outp.Split('\n'))
                                    if (line.Trim().Length > 0) Log.Info("  " + line.TrimEnd());
                        }
                        UpdateButtons();
                        Host.RefreshStatus();
                        LoadResults();
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }
    }
}

