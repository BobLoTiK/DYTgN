using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace NetSuite
{
    // Everything the dashboard shows, gathered in one pass on a worker thread.
    public class Snapshot
    {
        public bool ZapretRunning;
        public string ZapretService = "?";
        public string Youtube = "-";
        public string Discord = "-";
        public string Telegram = "-";
        public bool TgwsProcess;
        public bool TgwsPort;
        public bool SocksPort;
        public string Notion = "-";
        public bool PacOk;
        public string PacUrl = "";
        public DateTime Stamp = DateTime.Now;
        public bool Busy;

        public string ZapretState
        {
            get
            {
                if (Youtube == "000" && Discord == "000") return "no-internet";
                if (Youtube == "200" && Discord == "200") return "ok";
                if (ZapretRunning) return "dead";
                return "off";
            }
        }

        public string ZapretDetail
        {
            get
            {
                string h = "youtube " + Youtube + " · discord " + Discord;
                if (ZapretState == "off") h += " · служба " + ZapretService;
                if (ZapretState == "dead") h += " · профиль не тянет";
                if (ZapretState == "no-internet") h += " · интернета нет совсем";
                return h;
            }
        }

        public int ProblemCount
        {
            get
            {
                int n = 0;
                if (ZapretState != "ok") n++;
                if (!TgwsPort) n++;
                if (Notion != "200") n++;
                if (!PacOk) n++;
                return n;
            }
        }
    }

    static class Probe
    {
        public static string Http(string url, int ms)
        {
            try
            {
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
                r.Proxy = null;
                r.Timeout = ms;
                r.ReadWriteTimeout = ms;
                r.AllowAutoRedirect = true;
                r.UserAgent = "net-suite-gui/1.0";
                r.KeepAlive = false;
                using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
                    return ((int)resp.StatusCode).ToString();
            }
            catch (WebException we)
            {
                HttpWebResponse resp = we.Response as HttpWebResponse;
                if (resp != null) return ((int)resp.StatusCode).ToString();
                return "000";
            }
            catch { return "000"; }
        }

        public static bool Port(int port, int timeoutMs)
        {
            try
            {
                using (TcpClient c = new TcpClient())
                {
                    IAsyncResult ar = c.BeginConnect("127.0.0.1", port, null, null);
                    bool ok = ar.AsyncWaitHandle.WaitOne(timeoutMs);
                    if (!ok) return false;
                    c.EndConnect(ar);
                    return c.Connected;
                }
            }
            catch { return false; }
        }

        public static int TcpMs(string host, int port, int timeoutMs)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                using (TcpClient c = new TcpClient())
                {
                    IAsyncResult ar = c.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) return -1;
                    c.EndConnect(ar);
                    return (int)sw.ElapsedMilliseconds;
                }
            }
            catch { return -1; }
        }

        public static int PingMs(string host, int timeoutMs)
        {
            try
            {
                using (Ping p = new Ping())
                {
                    PingReply r = p.Send(host, timeoutMs);
                    if (r != null && r.Status == IPStatus.Success) return (int)r.RoundtripTime;
                }
            }
            catch { }
            return -1;
        }

        // Notion only answers through its local SOCKS tunnel, so this has to go
        // through curl.exe exactly like the scripts do.
        public static string HttpViaSocks(string url, int socksPort, int timeoutSec)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("curl.exe",
                    "-s -L -o NUL -w \"%{http_code}\" --max-time " + timeoutSec +
                    " --socks5-hostname 127.0.0.1:" + socksPort + " " + url);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(timeoutSec * 1000 + 2000);
                    return outp.Length == 0 ? "000" : outp;
                }
            }
            catch { return "000"; }
        }

        public static bool ProcessRunning(string name)
        {
            try { return Process.GetProcessesByName(name).Length > 0; }
            catch { return false; }
        }

        public static string ServiceState(string name)
        {
            try
            {
                using (ServiceController sc = new ServiceController(name))
                    return sc.Status.ToString().ToLowerInvariant();
            }
            catch { return "?"; }
        }

        public static Snapshot Take(Config cfg)
        {
            Snapshot s = new Snapshot();
            s.Busy = true;
            s.PacUrl = cfg.NotionPacUrl;
            s.ZapretRunning = ProcessRunning(cfg.ZapretProcess);
            s.ZapretService = ServiceState(cfg.ZapretService);
            s.TgwsProcess = ProcessRunning(cfg.TgwsProcess);
            s.TgwsPort = Port(cfg.TgwsPort, 600);
            s.PacOk = Http(cfg.NotionPacUrl, 2500) == "200";

            Thread t1 = new Thread(delegate () { s.Youtube = Http("https://www.youtube.com/", 5000); });
            Thread t2 = new Thread(delegate () { s.Discord = Http("https://discord.com/", 5000); });
            t1.IsBackground = true; t2.IsBackground = true;
            t1.Start(); t2.Start();

            s.SocksPort = Port(cfg.NotionSocksPort, 600);
            if (s.SocksPort)
            {
                s.Notion = HttpViaSocks(cfg.NotionCheckUrl, cfg.NotionSocksPort, 8);
            }

            t1.Join(6000);
            t2.Join(6000);
            if (s.Youtube == "-") s.Youtube = "000";
            if (s.Discord == "-") s.Discord = "000";
            s.Stamp = DateTime.Now;
            s.Busy = false;
            return s;
        }

        // ---- numbers for the diagnostics page ----

        public class Speed
        {
            public double DownMbps = -1;
            public double UpMbps = -1;
            public string Error = "";
        }

        // Cloudflare answers 403 to .NET's HttpWebRequest fingerprint but happily
        // serves curl, so the speed test goes through curl.exe like everything else.
        static string Curl(string args, int timeoutMs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("curl.exe", args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(timeoutMs);
                    return outp;
                }
            }
            catch { return ""; }
        }

        static double ParseRate(string s)
        {
            // takes "size time" from curl -w and returns Mbps
            string[] parts = s.Split(' ');
            if (parts.Length < 2) return -1;
            double size, time;
            if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out size)) return -1;
            if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out time)) return -1;
            if (size <= 0 || time <= 0.15) return -1;
            return size * 8.0 / time / 1000000.0;
        }

        public static double DownloadMbps(int seconds)
        {
            // 50 MB is the sweet spot: Cloudflare rejects 200 MB with a 1-byte body.
            string r = Curl("-s -o NUL -A \"net-suite-gui/1.0\" --max-time " + seconds +
                " -w \"%{size_download} %{time_total}\" \"https://speed.cloudflare.com/__down?bytes=52428800\"",
                (seconds + 5) * 1000);
            double mbps = ParseRate(r);
            if (mbps > 0) return mbps;
            string r2 = Curl("-s -o NUL -A \"net-suite-gui/1.0\" --max-time " + seconds +
                " -w \"%{size_download} %{time_total}\" \"https://cachefly.cachefly.net/100mb.test\"",
                (seconds + 5) * 1000);
            return ParseRate(r2);
        }

        public static double UploadMbps(int seconds)
        {
            string file = Path.Combine(Path.GetTempPath(), "net-suite-upload.bin");
            try
            {
                if (!File.Exists(file) || new FileInfo(file).Length < 4 * 1024 * 1024)
                {
                    byte[] chunk = new byte[1024 * 1024];
                    new Random(7).NextBytes(chunk);
                    using (FileStream fs = File.Create(file))
                        for (int i = 0; i < 6; i++) fs.Write(chunk, 0, chunk.Length);
                }
            }
            catch { return -1; }
            string r = Curl("-s -o NUL -A \"net-suite-gui/1.0\" --max-time " + seconds +
                " -X POST --data-binary \"@" + file + "\" -w \"%{size_upload} %{time_total}\" https://speed.cloudflare.com/__up",
                (seconds + 5) * 1000);
            return ParseRate(r);
        }

        public static readonly string[][] TelegramDc = new string[][]
        {
            new string[] { "DC1", "149.154.175.50" },
            new string[] { "DC2", "149.154.167.51" },
            new string[] { "DC3", "149.154.175.100" },
            new string[] { "DC4", "149.154.167.91" },
            new string[] { "DC5", "91.108.56.130" }
        };
    }
}

