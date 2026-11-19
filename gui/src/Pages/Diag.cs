using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace NetSuite
{
    // Ping, speed and the five Telegram data centres. NetFix shows all of this,
    // net-suite only printed "youtube 200".
    public class DiagPage : Page
    {
        StatTile tPing, tDown, tUp, tTg;
        Card dcCard, siteCard;
        Dot[] dcDots = new Dot[5];
        Label[] dcVals = new Label[5];
        Dot[] sDots = new Dot[4];
        Label[] sVals = new Label[4];
        Btn bRun;
        Label lblStatus;
        bool running;
        bool everRun;

        static readonly string[] siteNames = { "youtube.com", "discord.com", "google.com", "chatgpt.com" };
        static readonly string[] siteUrls = { "https://www.youtube.com/", "https://discord.com/", "https://www.google.com/", "https://chatgpt.com/" };

        public DiagPage(MainForm host) : base(host, "Диагностика",
            "пинг, скорость и доступность дата-центров Telegram")
        {
            tPing = new StatTile("ПИНГ 1.1.1.1");
            Controls.Add(tPing);
            tDown = new StatTile("СКАЧАТЬ");
            Controls.Add(tDown);
            tUp = new StatTile("ОТДАТЬ");
            Controls.Add(tUp);
            tTg = new StatTile("TELEGRAM DC");
            Controls.Add(tTg);

            dcCard = new Card();
            Controls.Add(dcCard);
            Label dcCap = new Label();
            dcCap.Text = "Дата-центры Telegram";
            dcCap.Font = Theme.H2;
            dcCap.ForeColor = Theme.Text;
            dcCap.BackColor = Theme.Card;
            dcCap.AutoSize = true;
            dcCap.Location = new Point(16, 12);
            dcCard.Controls.Add(dcCap);
            for (int i = 0; i < 5; i++)
            {
                dcDots[i] = new Dot(12);
                dcDots[i].BackColor = Theme.Card;
                dcDots[i].Location = new Point(18, 52 + i * 30);
                dcCard.Controls.Add(dcDots[i]);

                Label n = new Label();
                n.Text = Probe.TelegramDc[i][0] + "  " + Probe.TelegramDc[i][1];
                n.Font = new Font("Consolas", 9f);
                n.ForeColor = Theme.Muted;
                n.BackColor = Theme.Card;
                n.AutoSize = true;
                n.Location = new Point(40, 48 + i * 30);
                dcCard.Controls.Add(n);

                dcVals[i] = new Label();
                dcVals[i].Text = "—";
                dcVals[i].Font = Theme.Bold;
                dcVals[i].ForeColor = Theme.Dim;
                dcVals[i].BackColor = Theme.Card;
                dcVals[i].AutoSize = false;
                dcVals[i].Width = 90;
                dcVals[i].Height = 20;
                dcVals[i].TextAlign = ContentAlignment.MiddleRight;
                dcVals[i].Location = new Point(dcCard.Width - 110, 46 + i * 30);
                dcVals[i].Anchor = AnchorStyles.Top | AnchorStyles.Right;
                dcCard.Controls.Add(dcVals[i]);
            }

            siteCard = new Card();
            Controls.Add(siteCard);
            Label scCap = new Label();
            scCap.Text = "Сайты напрямую (без прокси)";
            scCap.Font = Theme.H2;
            scCap.ForeColor = Theme.Text;
            scCap.BackColor = Theme.Card;
            scCap.AutoSize = true;
            scCap.Location = new Point(16, 12);
            siteCard.Controls.Add(scCap);
            for (int i = 0; i < 4; i++)
            {
                sDots[i] = new Dot(12);
                sDots[i].BackColor = Theme.Card;
                sDots[i].Location = new Point(18, 56 + i * 30);
                siteCard.Controls.Add(sDots[i]);

                Label n = new Label();
                n.Text = siteNames[i];
                n.Font = Theme.Base;
                n.ForeColor = Theme.Muted;
                n.BackColor = Theme.Card;
                n.AutoSize = true;
                n.Location = new Point(40, 52 + i * 30);
                siteCard.Controls.Add(n);

                sVals[i] = new Label();
                sVals[i].Text = "—";
                sVals[i].Font = Theme.Bold;
                sVals[i].ForeColor = Theme.Dim;
                sVals[i].BackColor = Theme.Card;
                sVals[i].AutoSize = false;
                sVals[i].Width = 80;
                sVals[i].Height = 20;
                sVals[i].TextAlign = ContentAlignment.MiddleRight;
                sVals[i].Location = new Point(siteCard.Width - 100, 50 + i * 30);
                sVals[i].Anchor = AnchorStyles.Top | AnchorStyles.Right;
                siteCard.Controls.Add(sVals[i]);
            }

            bRun = new Btn();
            bRun.Text = "Проверить всё";
            bRun.Style = BtnStyle.Primary;
            bRun.Click += delegate { Start(true); };
            Controls.Add(bRun);

            lblStatus = new Label();
            lblStatus.Font = Theme.Small;
            lblStatus.ForeColor = Theme.Dim;
            lblStatus.BackColor = Theme.Bg;
            Controls.Add(lblStatus);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (tPing == null) return;
            int w = Width, h = Height;
            int gap = 12;
            int tw = (w - gap * 3) / 4;
            tPing.SetBounds(0, 60, tw, 96);
            tDown.SetBounds(tw + gap, 60, tw, 96);
            tUp.SetBounds((tw + gap) * 2, 60, tw, 96);
            tTg.SetBounds((tw + gap) * 3, 60, tw, 96);

            bRun.SetBounds(0, 172, 160, 40);
            lblStatus.SetBounds(172, 172, Math.Max(100, w - 172), 40);

            int cardsTop = 226;
            int ch = Math.Max(180, h - cardsTop - 6);
            int half = (w - gap) / 2;
            dcCard.SetBounds(0, cardsTop, half, ch);
            siteCard.SetBounds(half + gap, cardsTop, w - half - gap, ch);
        }

        public override void OnShow()
        {
            if (!everRun) Start(false);
        }

        void Start(bool withSpeed)
        {
            if (running) return;
            running = true;
            everRun = true;
            bRun.Enabled = false;
            bRun.Text = "Проверяю…";
            lblStatus.Text = withSpeed
                ? "иду по пунктам: пинг, скорость (~10 с), дата-центры, сайты"
                : "иду по пунктам: пинг, дата-центры, сайты; скорость качает трафик — только по кнопке";
            Log.Step("▶ диагностика сети");

            Thread t = new Thread(delegate ()
            {
                int ping = Probe.PingMs("1.1.1.1", 3000);
                SetText(tPing, ping > 0 ? ping + " мс" : "нет", ping > 0 && ping < 60 ? Theme.Ok : (ping > 0 ? Theme.Warn : Theme.Err), "1.1.1.1");

                if (withSpeed)
                {
                    double down = Probe.DownloadMbps(6);
                    SetText(tDown, down > 0 ? string.Format("{0:0.0}", down) : "—", Theme.Text, "Мбит/с, 6 секунд");

                    double up = Probe.UploadMbps(5);
                    SetText(tUp, up > 0 ? string.Format("{0:0.0}", up) : "—", Theme.Text, "Мбит/с, 5 секунд");
                }
                else
                {
                    SetText(tDown, "—", Theme.Muted, "по кнопке: качает ~50 МБ");
                    SetText(tUp, "—", Theme.Muted, "по кнопке: отдаёт ~6 МБ");
                }

                int okDc = 0, bestMs = -1;
                for (int i = 0; i < 5; i++)
                {
                    int ms = Probe.TcpMs(Probe.TelegramDc[i][1], 443, 2500);
                    SetDc(i, ms);
                    if (ms > 0) { okDc++; if (bestMs < 0 || ms < bestMs) bestMs = ms; }
                }
                SetText(tTg, okDc + "/5", okDc == 5 ? Theme.Ok : (okDc > 0 ? Theme.Warn : Theme.Muted),
                    bestMs > 0 ? "лучший " + bestMs + " мс" : "напрямую нет — норма при TgWsProxy");

                for (int i = 0; i < 4; i++)
                {
                    string code = Probe.Http(siteUrls[i], 6000);
                    SetSite(i, code);
                }

                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        running = false;
                        bRun.Enabled = true;
                        bRun.Text = "Проверить заново";
                        lblStatus.Text = "готово · " + DateTime.Now.ToString("HH:mm:ss");
                        Log.Good("диагностика завершена");
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void SetText(StatTile tile, string value, Color color, string sub)
        {
            try
            {
                BeginInvoke(new Action(delegate
                {
                    tile.Value = value;
                    tile.ValueColor = color;
                    tile.Sub = sub;
                }));
            }
            catch { }
        }

        void SetDc(int i, int ms)
        {
            try
            {
                BeginInvoke(new Action(delegate
                {
                    bool ok = ms > 0;
                    dcDots[i].Color = ok ? (ms < 120 ? Theme.Ok : Theme.Warn) : Theme.Err;
                    dcVals[i].Text = ok ? ms + " мс" : "нет";
                    dcVals[i].ForeColor = ok ? Theme.Text : Theme.Err;
                }));
            }
            catch { }
        }

        void SetSite(int i, string code)
        {
            try
            {
                BeginInvoke(new Action(delegate
                {
                    bool ok = code == "200" || code == "204" || code == "301" || code == "302";
                    bool answered = code == "401" || code == "403" || code == "404" || code == "429";
                    Color c = ok ? Theme.Ok : (answered ? Theme.Warn : Theme.Err);
                    sDots[i].Color = c;
                    sVals[i].Text = answered ? code + " (отвечает)" : code;
                    sVals[i].ForeColor = c;
                }));
            }
            catch { }
        }
    }
}
