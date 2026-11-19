using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace NetSuite
{
    public class MainForm : Form
    {
        public Config Cfg;
        public Snapshot Status = new Snapshot();
        public bool Busy
        {
            get { return busy; }
            set { busy = value; }
        }
        bool busy;

        Panel sidebar;
        Panel content;
        Label lblVer;
        List<Page> pages = new List<Page>();
        List<NavBtn> navs = new List<NavBtn>();
        System.Windows.Forms.Timer tick;
        bool probing;
        NotifyIcon tray;
        bool exiting;
        bool trayHinted;
        int pageIndex;

        public event Action Changed;

        public MainForm()
        {
            Cfg = Config.Load();
            Text = "net-suite — пульт";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Base;
            ClientSize = new Size(1180, 742);
            MinimumSize = new Size(1010, 640);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildSidebar();
            BuildContent();
            BuildTray();

            Log.Line += OnLogLine;
            if (Cfg.LoadError.Length > 0) Log.Warn("netsuite.json: " + Cfg.LoadError);

            tick = new System.Windows.Forms.Timer();
            tick.Interval = 3000;
            tick.Tick += delegate { RefreshStatus(); };
            tick.Start();
            Shown += delegate
            {
                DarkenTitleBar();
                Log.Info("net-suite GUI запущен, репозиторий: " + Paths.RepoRoot);
                RefreshStatus();
            };
            FormClosing += OnClosing;
            LayoutShell();
            ShowPage(0);
        }

        // WinForms docking order is a trap: a Dock=Fill panel added last swallows
        // the whole client area and the sidebar ends up on top of it. Lay it out
        // by hand instead - two rectangles, no surprises.
        void LayoutShell()
        {
            int sw = 212;
            sidebar.SetBounds(0, 0, sw, ClientSize.Height);
            content.SetBounds(sw, 0, Math.Max(100, ClientSize.Width - sw), ClientSize.Height);
            if (lblVer != null) lblVer.Location = new Point(24, sidebar.Height - 34);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (sidebar != null && content != null) LayoutShell();
        }

        // ---------------- layout ----------------

        void BuildSidebar()
        {
            sidebar = new SidePanel();
            sidebar.BackColor = Theme.Sidebar;
            Controls.Add(sidebar);

            string[] names = { "Пульт", "Конфиги", "DNS", "Диагностика", "Настройки" };
            for (int i = 0; i < names.Length; i++)
            {
                NavBtn n = new NavBtn(names[i]);
                n.BackColor = Theme.Sidebar;
                n.Bounds = new Rectangle(8, 96 + i * 42, 196, 38);
                int idx = i;
                n.Click += delegate { ShowPage(idx); };
                sidebar.Controls.Add(n);
                navs.Add(n);
            }

            lblVer = new Label();
            lblVer.Text = "v1.0 · DYTgN";
            lblVer.Font = Theme.Small;
            lblVer.ForeColor = Theme.Dim;
            lblVer.BackColor = Theme.Sidebar;
            lblVer.AutoSize = true;
            lblVer.Location = new Point(24, sidebar.Height - 34);
            lblVer.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            sidebar.Controls.Add(lblVer);
        }

        void BuildContent()
        {
            content = new Panel();
            content.BackColor = Theme.Bg;
            content.Padding = new Padding(26, 18, 26, 20);
            Controls.Add(content);

            pages.Add(new DashboardPage(this));
            pages.Add(new ConfigsPage(this));
            pages.Add(new DnsPage(this));
            pages.Add(new DiagPage(this));
            pages.Add(new SettingsPage(this));
            foreach (Page p in pages)
            {
                p.Dock = DockStyle.Fill;
                p.Visible = false;
                content.Controls.Add(p);
            }
        }

        void BuildTray()
        {
            try
            {
                tray = new NotifyIcon();
                tray.Icon = Icon != null ? Icon : SystemIcons.Application;
                tray.Text = "net-suite";
                tray.Visible = true;
                ContextMenuStrip m = new ContextMenuStrip();
                m.Items.Add("Открыть", null, delegate { RestoreWindow(); });
                m.Items.Add("Включить всё", null, delegate { RunAction("auto", "включить всё"); });
                m.Items.Add("Выключить", null, delegate { RunAction("off", "выключить"); });
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Выход", null, delegate { exiting = true; Close(); });
                tray.ContextMenuStrip = m;
                tray.DoubleClick += delegate { RestoreWindow(); };
            }
            catch { }
        }

        void RestoreWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        public void HardExit()
        {
            exiting = true;
            try { if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; } } catch { }
            Close();
            Application.Exit();
        }

        // Self-test: walk every page, let it settle, save a PNG. Used to check the
        // UI without touching the real desktop (see --shot in Program.cs).
        public void ShootPages(string dir)
        {
            Status = Probe.Take(Cfg);
            ApplyStatus();
            Directory.CreateDirectory(dir);
            string[] names = { "pult", "configs", "dns", "diag", "settings" };
            for (int i = 0; i < pages.Count; i++)
            {
                ShowPage(i);
                Application.DoEvents();
                Thread.Sleep(i == 3 ? 28000 : 1200);
                Application.DoEvents();
                try
                {
                    using (Bitmap bmp = new Bitmap(Width, Height))
                    {
                        DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                        bmp.Save(Path.Combine(dir, names[i] + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                    Log.Info("shot: " + names[i]);
                }
                catch (Exception ex) { Log.Bad("shot " + names[i] + ": " + ex.Message); }
            }
        }

        public void ShowPage(int index)
        {
            pageIndex = index;
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].Visible = i == index;
                navs[i].Active = i == index;
                navs[i].Invalidate();
            }
            pages[index].OnShow();
            pages[index].OnStatus(Status);
        }

        public void ShowPage(string key)
        {
            switch (key)
            {
                case "configs": ShowPage(1); return;
                case "dns": ShowPage(2); return;
                case "diag": ShowPage(3); return;
                case "settings": ShowPage(4); return;
                default: ShowPage(0); return;
            }
        }

        // ---------------- status polling ----------------

        public void RefreshStatus()
        {
            if (probing || busy) return;
            probing = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Snapshot s = null;
                try { s = Probe.Take(Cfg); } catch { }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        probing = false;
                        if (s != null)
                        {
                            Status = s;
                            ApplyStatus();
                        }
                    }));
                }
                catch { probing = false; }
            });
        }

        void ApplyStatus()
        {
            for (int i = 0; i < pages.Count; i++)
                if (pages[i].Visible) pages[i].OnStatus(Status);
            Action h = Changed;
            if (h != null) h();
            if (tray != null)
            {
                int n = Status.ProblemCount;
                tray.Text = n == 0 ? "net-suite · всё работает" : string.Format("net-suite · проблем: {0}", n);
            }
        }

        // ---------------- actions ----------------

        public void RunAction(string action, string label)
        {
            if (busy) { Log.Warn("уже выполняется другая операция, подождите"); return; }
            busy = true;
            ApplyStatus();
            Log.Step("▶ " + label);
            string script = Paths.InRepo("netsuite.ps1");
            Run.Script(script, "-Action " + action,
                delegate (string line)
                {
                    try { BeginInvoke(new Action(delegate { Log.Write(line, Classify(line)); })); } catch { }
                },
                delegate (int code)
                {
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            busy = false;
                            if (code == 0) Log.Good("■ " + label + ": готово");
                            else Log.Bad("■ " + label + ": выход с кодом " + code);
                            RefreshStatus();
                        }));
                    }
                    catch { busy = false; }
                });
        }

        static Color Classify(string line)
        {
            string l = line.ToLowerInvariant();
            if (l.Contains("error") || l.Contains("не удалось")) return Theme.Err;
            if (l.Contains("warn") || l.Contains("not running") || l.Contains("dead")) return Theme.Warn;
            if (l.Contains("healthy") || l.Contains("готово") || l.Contains("started")) return Theme.Ok;
            if (l.TrimStart().StartsWith("===")) return Theme.Accent;
            return Theme.Muted;
        }

        void OnLogLine(string line, Color c)
        {
            try
            {
                if (InvokeRequired) BeginInvoke(new Action(delegate { OnLogLine(line, c); }));
                else for (int i = 0; i < pages.Count; i++) pages[i].AppendLog(line, c);
            }
            catch { }
        }

        // ---------------- chrome ----------------

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!trayHinted && tray != null)
                {
                    trayHinted = true;
                    try { tray.ShowBalloonTip(2500, "net-suite", "Свёрнут в трей. Правый клик по значку — меню, двойной — открыть окно.", ToolTipIcon.Info); }
                    catch { }
                }
                return;
            }
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        void DarkenTitleBar()
        {
            try
            {
                int on = 1;
                DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int));
                int caption = ColorTranslator.ToWin32(Theme.Bg);
                DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));
                int border = ColorTranslator.ToWin32(Theme.Border);
                DwmSetWindowAttribute(Handle, 34, ref border, sizeof(int));
            }
            catch { }
        }

        // Sidebar draws its own logo block.
        class SidePanel : Panel
        {
            public SidePanel()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                DoubleBuffered = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(Theme.Sidebar)) g.FillRectangle(b, ClientRectangle);

                Rectangle logo = new Rectangle(22, 20, 36, 36);
                using (GraphicsPath p = Theme.Round(logo, 10))
                using (LinearGradientBrush lg = new LinearGradientBrush(logo, Theme.Accent, Color.FromArgb(0x7B, 0x5C, 0xF0), 45f))
                    g.FillPath(lg, p);
                Theme.Text_(g, "ns", new Font("Segoe UI Semibold", 12f), logo, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                Theme.Text_(g, "net-suite", Theme.H2, new Rectangle(68, 19, 130, 22), Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                Theme.Text_(g, "пульт сети", Theme.Small, new Rectangle(69, 39, 130, 18), Theme.Dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

                using (Pen pen = new Pen(Theme.BorderSoft))
                    g.DrawLine(pen, 20, 78, Width - 20, 78);
            }
        }
    }

    // Base for the five screens: title block on top, content below.
    public class Page : UserControl
    {
        public MainForm Host;
        public string Title;
        public string Subtitle;
        protected Panel header;
        protected Label lblTitle;
        protected Label lblSub;
        public LogView LogBox;

        public Page(MainForm host, string title, string subtitle)
        {
            Host = host;
            Title = title;
            Subtitle = subtitle;
            BackColor = Theme.Bg;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            header = new Panel();
            header.BackColor = Theme.Bg;
            lblTitle = new Label();
            lblTitle.Text = title;
            lblTitle.Font = Theme.H1;
            lblTitle.ForeColor = Theme.Text;
            lblTitle.BackColor = Theme.Bg;
            lblTitle.AutoSize = true;
            lblSub = new Label();
            lblSub.Text = subtitle;
            lblSub.Font = Theme.Small;
            lblSub.ForeColor = Theme.Dim;
            lblSub.BackColor = Theme.Bg;
            lblSub.AutoSize = true;
            header.Controls.Add(lblTitle);
            header.Controls.Add(lblSub);
            Controls.Add(header);
            header.SendToBack();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (header == null) return;
            header.Bounds = new Rectangle(0, 0, Width, 56);
            lblTitle.Location = new Point(0, 2);
            lblSub.Location = new Point(2, 32);
        }

        public virtual void OnStatus(Snapshot s) { }
        public virtual void OnShow() { }
        public virtual void AppendLog(string line, Color c) { }
    }
}
