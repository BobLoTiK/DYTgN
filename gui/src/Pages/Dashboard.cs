using System;
using System.Drawing;
using System.Windows.Forms;

namespace NetSuite
{
    public class DashboardPage : Page
    {
        Card[] tiles = new Card[4];
        Dot[] dots = new Dot[4];
        Label[] caps = new Label[4];
        Label[] vals = new Label[4];
        Label[] dets = new Label[4];
        Btn bStart, bProfile, bCheck, bRestart, bOff;
        Card logCard;
        Label lblLog;
        Pill overall;
        Label stamp;
        Button bClear;

        public DashboardPage(MainForm host) : base(host, "Пульт", "состояние сети и одна кнопка, чтобы всё поднять")
        {
            string[] names = { "zapret (обход DPI)", "TgWsProxy (телеграм)", "notion-туннель", "PAC-сервер" };
            for (int i = 0; i < 4; i++)
            {
                Card c = new Card();
                c.Radius = 12;
                dots[i] = new Dot(16);
                dots[i].BackColor = Theme.Card;
                dots[i].Location = new Point(14, 13);
                caps[i] = Mk(names[i], Theme.Small, Theme.Dim, Theme.Card);
                caps[i].Location = new Point(38, 12);
                caps[i].Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
                vals[i] = Mk("—", Theme.H2, Theme.Text, Theme.Card);
                vals[i].Location = new Point(14, 40);
                vals[i].Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
                dets[i] = Mk("", Theme.Small, Theme.Muted, Theme.Card);
                dets[i].Location = new Point(14, 72);
                dets[i].Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
                c.Controls.Add(dots[i]);
                c.Controls.Add(caps[i]);
                c.Controls.Add(vals[i]);
                c.Controls.Add(dets[i]);
                tiles[i] = c;
                Controls.Add(c);
            }
            for (int i = 0; i < 4; i++) tiles[i].Resize += delegate { RelayoutTile(tiles[0]); };

            overall = new Pill();
            overall.BackColor = Theme.Bg;
            overall.Set("проверяю", Theme.Dim);
            header.Controls.Add(overall);
            stamp = Mk("", Theme.Small, Theme.Dim, Theme.Bg);
            header.Controls.Add(stamp);

            bStart = new Btn();
            bStart.Text = "Включить всё";
            bStart.Style = BtnStyle.Primary;
            bStart.Font = new Font("Segoe UI Semibold", 10.5f);
            bStart.Click += delegate { Host.RunAction("auto", "включить всё"); };
            Controls.Add(bStart);

            bProfile = new Btn();
            bProfile.Text = "Подобрать профиль";
            bProfile.Click += delegate { Host.ShowPage("configs"); };
            Controls.Add(bProfile);

            bCheck = new Btn();
            bCheck.Text = "Проверить";
            bCheck.Style = BtnStyle.Subtle;
            bCheck.Click += delegate { Host.RefreshStatus(); };
            Controls.Add(bCheck);

            bRestart = new Btn();
            bRestart.Text = "Перезапустить";
            bRestart.Click += delegate { Host.RunAction("restart", "перезапуск"); };
            Controls.Add(bRestart);

            bOff = new Btn();
            bOff.Text = "Выключить";
            bOff.Style = BtnStyle.Danger;
            bOff.Click += delegate { Host.RunAction("off", "выключить"); };
            Controls.Add(bOff);

            logCard = new Card();
            logCard.FillColor = Theme.LogBg;
            logCard.StrokeColor = Theme.Border;
            logCard.Resize += delegate { RelayoutLog(); };
            Controls.Add(logCard);

            lblLog = Mk("Журнал", Theme.Small, Theme.Dim, Theme.LogBg);
            lblLog.Location = new Point(14, 8);
            logCard.Controls.Add(lblLog);

            bClear = new Button();
            bClear.Text = "очистить";
            bClear.FlatStyle = FlatStyle.Flat;
            bClear.FlatAppearance.BorderSize = 0;
            bClear.Font = Theme.Small;
            bClear.ForeColor = Theme.Dim;
            bClear.BackColor = Theme.LogBg;
            bClear.Cursor = Cursors.Hand;
            bClear.Size = new Size(70, 20);
            bClear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            bClear.Location = new Point(logCard.Width - 80, 6);
            bClear.Click += delegate { LogBox.Clear(); };
            logCard.Controls.Add(bClear);

            LogBox = new LogView();
            logCard.Controls.Add(LogBox);
        }

        static Label Mk(string text, Font f, Color c, Color bg)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = f;
            l.ForeColor = c;
            l.BackColor = bg;
            l.AutoSize = false;
            l.Height = 18;
            l.Width = 200;
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        void RelayoutTile(Card c)
        {
            if (c.Width < 60) return;
            for (int i = 0; i < 4; i++)
            {
                if (tiles[i] == null || caps[i] == null) continue;
                caps[i].Width = tiles[i].Width - 52;
                vals[i].Width = tiles[i].Width - 28;
                dets[i].Width = tiles[i].Width - 28;
            }
        }

        void RelayoutLog()
        {
            if (LogBox == null) return;
            LogBox.Bounds = new Rectangle(12, 34, Math.Max(60, logCard.Width - 24), Math.Max(40, logCard.Height - 46));
            if (bClear != null) bClear.Location = new Point(logCard.Width - 80, 6);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (tiles[0] == null) return;
            int w = Width, h = Height;
            int gap = 12;
            int top = 60;
            int tileH = 104;
            int tw = (w - gap * 3) / 4;
            if (tw < 140) tw = 140;
            for (int i = 0; i < 4; i++)
                tiles[i].Bounds = new Rectangle(i * (tw + gap), top, tw, tileH);

            if (overall != null)
            {
                PlaceHeaderBits();
            }

            int y = top + tileH + 14;
            bStart.SetBounds(0, y, 176, 44);
            bProfile.SetBounds(188, y, 176, 44);
            bOff.SetBounds(w - 128, y, 128, 44);
            bRestart.SetBounds(w - 128 - 8 - 148, y, 148, 44);
            bCheck.SetBounds(w - 128 - 8 - 148 - 8 - 104, y, 104, 44);

            int logTop = y + 44 + 14;
            logCard.Bounds = new Rectangle(0, logTop, w, Math.Max(120, h - logTop));
            RelayoutLog();
        }

        void PlaceHeaderBits()
        {
            int w = Width;
            overall.Location = new Point(Math.Max(0, w - overall.Width - 2), 4);
            stamp.Location = new Point(Math.Max(0, w - 220), 30);
            stamp.Width = 220;
            stamp.TextAlign = ContentAlignment.MiddleRight;
        }

        public override void OnStatus(Snapshot s)
        {
            if (s == null) return;
            PaintTile(0, s.ZapretState, s.ZapretDetail,
                s.ZapretState == "ok" ? "работает" :
                s.ZapretState == "dead" ? "профиль не тянет" :
                s.ZapretState == "off" ? "выключен" : "нет интернета");
            PaintTile(1, s.TgwsPort ? "ok" : (s.TgwsProcess ? "warn" : "off"),
                "порт " + Host.Cfg.TgwsPort + (s.TgwsPort ? " открыт" : " закрыт"),
                s.TgwsPort ? "передаёт" : (s.TgwsProcess ? "процесс есть, порт нет" : "выключен"));
            PaintTile(2, s.Notion == "200" ? "ok" : (s.SocksPort ? "dead" : "off"),
                "notion.com → " + s.Notion,
                s.Notion == "200" ? "на связи" : (s.SocksPort ? "туннель мёртв" : "выключен"));
            PaintTile(3, s.PacOk ? "ok" : "dead", s.PacUrl,
                s.PacOk ? "на связи" : "не отвечает");

            int problems = s.ProblemCount;
            if (problems == 0) overall.Set("всё работает", Theme.Ok);
            else if (problems <= 2) overall.Set("проблем: " + problems, Theme.Warn);
            else overall.Set("сеть лежит", Theme.Err);
            stamp.Text = "проверено " + s.Stamp.ToString("HH:mm:ss");
            PlaceHeaderBits();

            bool on = !Host.Busy;
            bStart.Enabled = on;
            bProfile.Enabled = on;
            bRestart.Enabled = on;
            bOff.Enabled = on;
            bCheck.Enabled = on;
            bStart.Text = Host.Busy ? "работаю…" : "Включить всё";
        }

        void PaintTile(int i, string state, string detail, string word)
        {
            Color c = Theme.Dim;
            if (state == "ok") c = Theme.Ok;
            else if (state == "warn") c = Theme.Warn;
            else if (state == "dead") c = Theme.Err;
            else if (state == "no-internet") c = Theme.Err;
            dots[i].Color = c;
            vals[i].ForeColor = c;
            vals[i].Text = word;
            dets[i].Text = detail;
        }

        public override void AppendLog(string line, Color c)
        {
            if (LogBox != null) LogBox.AppendLine(line, c);
        }
    }
}
