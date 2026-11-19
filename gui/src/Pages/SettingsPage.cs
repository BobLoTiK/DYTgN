using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace NetSuite
{
    public class SettingsPage : Page
    {
        Card pathsCard, startCard, serviceCard, aboutCard;
        Btn bOpenRepo, bOpenCfg, bOpenZapret, bOpenLogs;
        Toggle tAutostart, tKeepalive;
        Label lAutostart, lKeepalive, lAbout;
        Btn bUpdate, bRebuild, bGithub;
        bool loading;

        public SettingsPage(MainForm host) : base(host, "Настройки",
            "пути, автозапуск и обслуживание")
        {
            pathsCard = new Card();
            Controls.Add(pathsCard);
            CardCap(pathsCard, "Пути");
            bOpenRepo = MakeBtn(pathsCard, "Открыть", delegate { Open(Paths.RepoRoot); });
            bOpenCfg = MakeBtn(pathsCard, "Открыть", delegate { Open(Paths.ConfigPath); });
            bOpenZapret = MakeBtn(pathsCard, "Открыть", delegate { Open(Host.Cfg.ZapretFolder); });
            bOpenLogs = MakeBtn(pathsCard, "Открыть", delegate { Open(Paths.LogsDir); });

            startCard = new Card();
            Controls.Add(startCard);
            CardCap(startCard, "Запуск");
            tAutostart = new Toggle();
            tAutostart.BackColor = Theme.Card;
            tAutostart.Toggled += delegate { ApplyAutostart(); };
            startCard.Controls.Add(tAutostart);
            lAutostart = CardLabel(startCard, "Поднимать сеть при входе в Windows (autostart.vbs)", 50);
            tKeepalive = new Toggle();
            tKeepalive.BackColor = Theme.Card;
            tKeepalive.Toggled += delegate { ApplyKeepalive(); };
            startCard.Controls.Add(tKeepalive);
            lKeepalive = CardLabel(startCard, "Проверять туннель каждые 10 минут (задача)", 92);

            serviceCard = new Card();
            Controls.Add(serviceCard);
            CardCap(serviceCard, "Обслуживание");
            bUpdate = MakeBtn(serviceCard, "Обновить из GitHub", delegate { UpdateRepo(); });
            bUpdate.Style = BtnStyle.Ghost;
            bRebuild = MakeBtn(serviceCard, "Пересобрать GUI", delegate { Rebuild(); });
            bGithub = MakeBtn(serviceCard, "Репозиторий", delegate { Open("https://github.com/BobLoTiK/DYTgN"); });

            aboutCard = new Card();
            Controls.Add(aboutCard);
            CardCap(aboutCard, "О программе");
            lAbout = CardLabel(aboutCard, "", 44);
            lAbout.Font = Theme.Small;
            lAbout.Height = 60;
            lAbout.Text = "net-suite GUI — оболочка над zapret, TgWsProxy и notion-туннелем.\n"
                + "Версия " + Application.ProductVersion + " · собрано csc из .NET Framework, без установки.\n"
                + "Данные пишутся в logs\\gui-YYYYMMDD.log рядом с netsuite.json.";
        }

        Btn MakeBtn(Control parent, string text, EventHandler click)
        {
            Btn b = new Btn();
            b.Text = text;
            b.Style = BtnStyle.Ghost;
            b.Click += click;
            parent.Controls.Add(b);
            return b;
        }

        static void CardCap(Card c, string text)
        {
            Label l = new Label();
            l.Text = text.ToUpperInvariant();
            l.Font = Theme.Small;
            l.ForeColor = Theme.Dim;
            l.BackColor = Theme.Card;
            l.AutoSize = true;
            l.Location = new Point(16, 12);
            c.Controls.Add(l);
        }

        static Label CardLabel(Control parent, string text, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Theme.Base;
            l.ForeColor = Theme.Text;
            l.BackColor = Theme.Card;
            l.AutoSize = false;
            l.Height = 22;
            l.Location = new Point(16, y);
            parent.Controls.Add(l);
            return l;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (pathsCard == null) return;
            int w = Width, h = Height;
            pathsCard.SetBounds(0, 60, w, 222);
            int y = 56;
            Row(pathsCard, "Репозиторий", Paths.RepoRoot, bOpenRepo, y);
            Row(pathsCard, "Конфиг", Paths.ConfigPath, bOpenCfg, y + 36);
            Row(pathsCard, "Профили zapret", Host.Cfg.ZapretFolder, bOpenZapret, y + 72);
            Row(pathsCard, "Журналы", Paths.LogsDir, bOpenLogs, y + 108);

            startCard.SetBounds(0, 296, w, 134);
            lAutostart.SetBounds(68, 46, w - 90, 24);
            tAutostart.Location = new Point(16, 48);
            lKeepalive.SetBounds(68, 88, w - 90, 24);
            tKeepalive.Location = new Point(16, 90);

            serviceCard.SetBounds(0, 444, w, 110);
            bUpdate.SetBounds(16, 44, 180, 38);
            bRebuild.SetBounds(206, 44, 170, 38);
            bGithub.SetBounds(386, 44, 130, 38);

            aboutCard.SetBounds(0, 568, w, Math.Max(90, h - 568));
            lAbout.SetBounds(16, 42, w - 32, 70);
        }

        void Row(Card c, string caption, string value, Btn open, int y)
        {
            Label l = (Label)c.Controls["row" + y];
            if (l == null)
            {
                l = new Label();
                l.Name = "row" + y;
                l.BackColor = Theme.Card;
                l.AutoSize = false;
                l.Height = 32;
                l.Location = new Point(16, y);
                c.Controls.Add(l);
            }
            l.Width = c.Width - 150;
            l.Font = Theme.Small;
            l.ForeColor = Theme.Muted;
            l.Text = caption + ":  " + value;
            l.TextAlign = ContentAlignment.MiddleLeft;
            open.SetBounds(c.Width - 106, y, 90, 30);
        }

        static void Open(string path)
        {
            try
            {
                if (Directory.Exists(path) || File.Exists(path))
                    Process.Start("explorer.exe", "\"" + path + "\"");
                else Process.Start(path);
            }
            catch { }
        }

        public override void OnShow()
        {
            loading = true;
            tAutostart.Checked = File.Exists(StartupLink());
            tKeepalive.Checked = TaskExists();
            loading = false;
            lAbout.Text = "net-suite GUI — оболочка над zapret, TgWsProxy и notion-туннелем.\n"
                + "Версия " + Application.ProductVersion + " · один exe, без установщика.\n"
                + "Журнал: logs\\gui-" + DateTime.Now.ToString("yyyyMMdd") + ".log";
        }

        static string StartupLink()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "net-suite.lnk");
        }

        static bool TaskExists()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("schtasks.exe", "/query /tn NotionTunnelKeepalive");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(8000);
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        void ApplyAutostart()
        {
            if (loading) return;
            string link = StartupLink();
            try
            {
                if (tAutostart.Checked)
                {
                    object sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                    object lnk = sh.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { link });
                    Type lt = lnk.GetType();
                    lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wscript.exe") });
                    lt.InvokeMember("Arguments", BindingFlags.SetProperty, null, lnk, new object[] { "\"" + Paths.InRepo("autostart.vbs") + "\"" });
                    lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Paths.RepoRoot });
                    lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "net-suite: поднять сеть при входе" });
                    lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                    Log.Good("автозапуск включён: " + link);
                }
                else
                {
                    if (File.Exists(link)) File.Delete(link);
                    Log.Info("автозапуск выключен");
                }
            }
            catch (Exception ex)
            {
                Log.Bad("автозапуск не переключился: " + ex.Message);
                tAutostart.Checked = File.Exists(link);
            }
        }

        void ApplyKeepalive()
        {
            if (loading) return;
            string vbs = Paths.InRepo("notion\\hidden-watch.vbs");
            try
            {
                ProcessStartInfo psi;
                if (tKeepalive.Checked)
                {
                    if (!File.Exists(vbs)) { Log.Bad("не нашёл notion\\hidden-watch.vbs"); tKeepalive.Checked = false; return; }
                    psi = new ProcessStartInfo("schtasks.exe",
                        "/create /tn NotionTunnelKeepalive /tr \"wscript.exe \\\"" + vbs + "\\\"\" /sc minute /mo 10 /f");
                }
                else
                {
                    psi = new ProcessStartInfo("schtasks.exe", "/delete /tn NotionTunnelKeepalive /f");
                }
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);
                    if (p.ExitCode == 0) Log.Good(tKeepalive.Checked ? "задача keepalive создана" : "задача keepalive удалена");
                    else { Log.Bad("schtasks: " + outp.Trim()); tKeepalive.Checked = TaskExists(); }
                }
            }
            catch (Exception ex)
            {
                Log.Bad("задача не переключилась: " + ex.Message);
                tKeepalive.Checked = TaskExists();
            }
        }

        void UpdateRepo()
        {
            Log.Step("▶ git pull");
            Run.Start("git.exe", "pull --ff-only",
                delegate (string line) { try { BeginInvoke(new Action(delegate { Log.Write("  " + line, Theme.Muted); })); } catch { } },
                delegate (int code)
                {
                    try
                    {
                        BeginInvoke(new Action(delegate
                        {
                            if (code == 0) Log.Good("репозиторий обновлён (для новых файлов GUI пересоберите его)");
                            else Log.Bad("git pull вышел с кодом " + code);
                        }));
                    }
                    catch { }
                });
        }

        // The running exe is locked, so the rebuild has to happen after this window exits:
        // hand a tiny waiter to cmd, close, let it build, and it re-opens the fresh exe.
        void Rebuild()
        {
            Log.Step("пересборка GUI: окно закроется, соберётся заново и откроется");
            string exe = System.Windows.Forms.Application.ExecutablePath;
            string build = Paths.InRepo("gui\\build.ps1");
            string log = Path.Combine(Paths.RepoRoot, "logs", "gui-build.log");
            string cmd = "/c timeout /t 2 >nul & powershell -NoProfile -ExecutionPolicy Bypass -File \"" + build +
                         "\" > \"" + log + "\" 2>&1 & start \"\" \"" + exe + "\"";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", cmd);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Paths.RepoRoot;
                Process.Start(psi);
            }
            catch (Exception ex) { Log.Bad("не смог запустить пересборку: " + ex.Message); return; }
            Host.HardExit();
        }
    }
}

