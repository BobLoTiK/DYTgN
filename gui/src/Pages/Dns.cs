using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Forms;

namespace NetSuite
{
    // DNS servers and DoH. NetFix has this page, net-suite did not.
    public class DnsPage : Page
    {
        class Preset
        {
            public string Name;
            public string[] Servers;
            public string Doh;
            public Preset(string name, string[] servers, string doh) { Name = name; Servers = servers; Doh = doh; }
        }

        static Preset[] presets = new Preset[]
        {
            new Preset("Авто (DHCP)", new string[0], ""),
            new Preset("Cloudflare", new string[] { "1.1.1.1", "1.0.0.1" }, "https://cloudflare-dns.com/dns-query"),
            new Preset("Google", new string[] { "8.8.8.8", "8.8.4.4" }, "https://dns.google/dns-query"),
            new Preset("Yandex", new string[] { "77.88.8.8", "77.88.8.1" }, "https://common.dot.dns.yandex.net/dns-query"),
            new Preset("AdGuard", new string[] { "94.140.14.14", "94.140.15.15" }, "https://dns.adguard-dns.com/dns-query"),
            new Preset("Xbox-DNS", new string[] { "176.99.11.11", "80.78.247.254" }, "")
        };

        Card cur;
        Label lblNow, lblServers, lblAdapter;
        List<Btn> buttons = new List<Btn>();
        Toggle dohToggle;
        Btn bApply;
        Label lblStatus, lblDohNote;
        int chosen = 1;
        bool working;

        public DnsPage(MainForm host) : base(host, "DNS",
            "смена DNS-серверов и шифрованный DNS (DoH) — одной кнопкой")
        {
            cur = new Card();
            Controls.Add(cur);
            Label cap = Mk("Сейчас", Theme.Small, Theme.Dim, Theme.Card);
            cap.Location = new Point(14, 8);
            cur.Controls.Add(cap);
            lblAdapter = Mk("", Theme.Small, Theme.Muted, Theme.Card);
            lblAdapter.Location = new Point(14, 30);
            cur.Controls.Add(lblAdapter);
            lblServers = Mk("", new Font("Consolas", 10f), Theme.Text, Theme.Card);
            lblServers.Location = new Point(14, 52);
            cur.Controls.Add(lblServers);
            lblNow = Mk("", Theme.Small, Theme.Dim, Theme.Card);
            lblNow.Location = new Point(14, 74);
            cur.Controls.Add(lblNow);

            for (int i = 0; i < presets.Length; i++)
            {
                Btn b = new Btn();
                b.Text = presets[i].Name;
                b.Style = i == chosen ? BtnStyle.Primary : BtnStyle.Ghost;
                int idx = i;
                b.Click += delegate { Choose(idx); };
                Controls.Add(b);
                buttons.Add(b);
            }

            dohToggle = new Toggle();
            dohToggle.BackColor = Theme.Bg;
            dohToggle.Checked = false;
            Controls.Add(dohToggle);
            lblDohNote = Mk("шифровать DNS (DoH) для выбранного сервера", Theme.Base, Theme.Muted, Theme.Bg);
            Controls.Add(lblDohNote);

            bApply = new Btn();
            bApply.Text = "Применить";
            bApply.Style = BtnStyle.Primary;
            bApply.Click += delegate { Apply(); };
            Controls.Add(bApply);

            lblStatus = Mk("", Theme.Small, Theme.Dim, Theme.Bg);
            Controls.Add(lblStatus);
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
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (cur == null) return;
            int w = Width;
            cur.SetBounds(0, 60, Math.Max(200, w), 100);
            lblAdapter.Width = cur.Width - 28;
            lblServers.Width = cur.Width - 28;
            lblNow.Width = cur.Width - 28;

            int bw = Math.Max(120, (w - 3 * 10) / 4);
            int y = 174;
            for (int i = 0; i < buttons.Count; i++)
            {
                int col = i % 4, row = i / 4;
                buttons[i].SetBounds(col * (bw + 10), y + row * 46, bw, 38);
            }
            int rows = (buttons.Count + 3) / 4;
            int ty = y + rows * 46 + 10;
            dohToggle.Location = new Point(0, ty + 3);
            lblDohNote.Location = new Point(56, ty);
            lblDohNote.Height = 30;
            lblDohNote.TextAlign = ContentAlignment.MiddleLeft;
            bApply.SetBounds(0, ty + 48, 160, 40);
            lblStatus.Location = new Point(172, ty + 48);
            lblStatus.Height = 40;
            lblStatus.Width = Math.Max(100, w - 172);
        }

        void Choose(int idx)
        {
            chosen = idx;
            for (int i = 0; i < buttons.Count; i++)
            {
                buttons[i].Style = i == idx ? BtnStyle.Primary : BtnStyle.Ghost;
                buttons[i].Invalidate();
            }
            dohToggle.Checked = presets[idx].Doh.Length > 0;
            dohToggle.Enabled = presets[idx].Doh.Length > 0;
            lblStatus.Text = presets[idx].Doh.Length > 0
                ? "DoH: " + presets[idx].Doh
                : (presets[idx].Servers.Length == 0 ? "DNS от роутера/провайдера" : "у этого сервера нет DoH");
        }

        public override void OnShow()
        {
            RefreshNow();
            Choose(chosen);
        }

        void RefreshNow()
        {
            try
            {
                string name = ElevTasks.ActiveAdapter();
                if (name == null)
                {
                    lblAdapter.Text = "активный адаптер не найден";
                    return;
                }
                lblAdapter.Text = "адаптер: " + name;
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.Name != name) continue;
                    System.Collections.Generic.List<string> addrs = new System.Collections.Generic.List<string>();
                    foreach (System.Net.IPAddress ip in ni.GetIPProperties().DnsAddresses)
                        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) addrs.Add(ip.ToString());
                    lblServers.Text = addrs.Count == 0 ? "DNS: автоматически" : "DNS: " + string.Join(", ", addrs.ToArray());
                    return;
                }
                lblServers.Text = "DNS: неизвестно";
            }
            catch { }
        }

        void Apply()
        {
            if (working) return;
            Preset p = presets[chosen];
            working = true;
            bApply.Enabled = false;
            lblStatus.Text = "применяю, подтвердите UAC…";
            Log.Step("▶ DNS: " + p.Name + (dohToggle.Checked && p.Doh.Length > 0 ? " + DoH" : ""));
            bool wantDoh = dohToggle.Checked && p.Doh.Length > 0;
            Thread t = new Thread(delegate ()
            {
                Dictionary<string, object> req = new Dictionary<string, object>();
                List<string> servers = new List<string>(p.Servers);
                req["dhcp"] = servers.Count == 0;
                req["servers"] = servers.ToArray();
                Dictionary<string, object> res;
                string err;
                bool ok = Elev.Ask("dns", req, 60000, out res, out err);
                string adapt = Elev.Json(res, "adapter", "");
                string log = Elev.Json(res, "log", "");
                string dohErr = "";
                bool dohOk = true;
                if (ok && wantDoh)
                {
                    Dictionary<string, object> req2 = new Dictionary<string, object>();
                    req2["server"] = p.Servers[0];
                    req2["template"] = p.Doh;
                    Dictionary<string, object> res2;
                    dohOk = Elev.Ask("doh", req2, 60000, out res2, out dohErr);
                }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        working = false;
                        bApply.Enabled = true;
                        if (ok)
                        {
                            Log.Good("DNS применён: " + p.Name + (adapt.Length > 0 ? " на " + adapt : ""));
                            lblStatus.Text = "применено: " + p.Name + (wantDoh ? (dohOk ? " · DoH зарегистрирован" : " · DoH не включился") : "");
                        }
                        else
                        {
                            Log.Bad("DNS не применился: " + err);
                            lblStatus.Text = "ошибка: " + err;
                        }
                        if (log.Length > 0)
                            foreach (string line in log.Split('\n'))
                                if (line.Trim().Length > 0) Log.Info("  " + line.Trim());
                        RefreshNow();
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }
    }
}
