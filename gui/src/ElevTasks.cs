using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Web.Script.Serialization;

namespace NetSuite
{
    // The half of the app that needs administrator rights. It runs in a copy of
    // NetSuite.exe started with UAC, takes a json request, writes a json answer.
    static class ElevTasks
    {
        public static int Run(string task, string inFile, string outFile)
        {
            Paths.Init();
            Dictionary<string, object> res = new Dictionary<string, object>();
            bool ok = false;
            string err = "";
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                Dictionary<string, object> req = new Dictionary<string, object>();
                if (File.Exists(inFile))
                {
                    Dictionary<string, object> parsed = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(inFile, Encoding.UTF8));
                    if (parsed != null) req = parsed;
                }
                switch (task)
                {
                    case "scan": ok = Scan(req, res, out err); break;
                    case "apply": ok = Apply(req, res, out err); break;
                    case "dns": ok = SetDns(req, res, out err); break;
                    case "doh": ok = SetDoh(req, res, out err); break;
                    default: err = "unknown task: " + task; break;
                }
            }
            catch (Exception ex) { err = ex.Message; }
            res["ok"] = ok;
            res["error"] = err;
            try { File.WriteAllText(outFile, new JavaScriptSerializer().Serialize(res), Encoding.UTF8); }
            catch { }
            return ok ? 0 : 1;
        }

        // ---- zapret profile scan / apply ----

        static bool Scan(Dictionary<string, object> req, Dictionary<string, object> res, out string err)
        {
            err = "";
            string ps = Path.Combine(Paths.RepoRoot, "zapret\\scan.ps1");
            if (!File.Exists(ps)) { err = "zapret\\scan.ps1 не найден"; return false; }
            string outFile = Path.Combine(Paths.LogsDir, "scan-results.json");
            string progFile = Path.Combine(Paths.LogsDir, "scan-progress.json");
            int settle = 5;
            object o;
            if (req.TryGetValue("settle", out o) && o != null) { try { settle = Convert.ToInt32(o); } catch { } }
            string outp;
            int code = RunPs("-File '" + ps + "' -SettleSeconds " + settle + " -OutFile '" + outFile + "' -ProgressFile '" + progFile + "'", out outp);
            res["code"] = code;
            res["output"] = outp;
            if (code != 0)
            {
                err = "скан не завершился (код " + code + ")";
                return false;
            }
            return true;
        }

        static bool Apply(Dictionary<string, object> req, Dictionary<string, object> res, out string err)
        {
            err = "";
            string profile = "";
            object o;
            if (req.TryGetValue("profile", out o) && o != null) profile = Convert.ToString(o);
            if (profile.Length == 0) { err = "не указан профиль"; return false; }
            string ps = Path.Combine(Paths.RepoRoot, "zapret\\scan.ps1");
            if (!File.Exists(ps)) { err = "zapret\\scan.ps1 не найден"; return false; }
            string outp;
            int code = RunPs("-File '" + ps + "' -Apply '" + profile.Replace("'", "''") + "'", out outp);
            res["code"] = code;
            res["output"] = outp;
            if (code != 0) { err = "не удалось применить профиль (код " + code + ")"; return false; }
            return true;
        }

        static int RunPs(string args, out string output)
        {
            output = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; " +
                    "& powershell -NoProfile -ExecutionPolicy Bypass " + args + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.WorkingDirectory = Paths.RepoRoot;
                using (Process p = Process.Start(psi))
                {
                    string a = p.StandardOutput.ReadToEnd();
                    string b = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    output = (a + "\n" + b).Trim();
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { output = ex.Message; return -1; }
        }

        // ---- DNS / DoH ----

        static bool SetDns(Dictionary<string, object> req, Dictionary<string, object> res, out string err)
        {
            err = "";
            string iface = ActiveAdapter();
            if (iface == null) { err = "не нашёл активный сетевой адаптер с шлюзом"; return false; }
            res["adapter"] = iface;
            object dhcpObj, serversObj;
            req.TryGetValue("dhcp", out dhcpObj);
            bool dhcp = dhcpObj != null && Convert.ToBoolean(dhcpObj);
            List<string> servers = new List<string>();
            if (!dhcp && req.TryGetValue("servers", out serversObj))
            {
                object[] arr = serversObj as object[];
                if (arr != null) foreach (object s in arr) if (s != null) servers.Add(Convert.ToString(s));
            }
            string log = "";
            if (dhcp || servers.Count == 0)
            {
                log += Netsh("interface ipv4 set dnsservers name=\"" + iface + "\" source=dhcp");
            }
            else
            {
                log += Netsh("interface ipv4 set dnsservers name=\"" + iface + "\" source=static address=" + servers[0] + " register=primary validate=no");
                for (int i = 1; i < servers.Count; i++)
                    log += Netsh("interface ipv4 add dnsservers name=\"" + iface + "\" address=" + servers[i] + " index=" + (i + 1) + " validate=no");
            }
            log += Netsh("interface ipv4 show dnsservers name=\"" + iface + "\"");
            res["log"] = log;
            return true;
        }

        static bool SetDoh(Dictionary<string, object> req, Dictionary<string, object> res, out string err)
        {
            err = "";
            object serverObj, tplObj;
            req.TryGetValue("server", out serverObj);
            req.TryGetValue("template", out tplObj);
            string server = serverObj == null ? "" : Convert.ToString(serverObj);
            string tpl = tplObj == null ? "" : Convert.ToString(tplObj);
            if (server.Length == 0 || tpl.Length == 0) { err = "нужен сервер и шаблон DoH"; return false; }
            string outp = Netsh("dns add encryption server=" + server + " dohtemplate=" + tpl + " autoupgrade=yes udpfallback=no");
            res["log"] = outp;
            bool ok = outp.IndexOf("Ok", StringComparison.OrdinalIgnoreCase) >= 0 || outp.IndexOf("ОК", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!ok) { err = outp.Trim(); return false; }
            return true;
        }

        static string Netsh(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("netsh.exe", args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    string a = p.StandardOutput.ReadToEnd();
                    string b = p.StandardError.ReadToEnd();
                    p.WaitForExit(15000);
                    return (a + b).Trim() + Environment.NewLine;
                }
            }
            catch (Exception ex) { return "netsh: " + ex.Message + Environment.NewLine; }
        }

        // The adapter that actually carries the default route, not "first Up one".
        // Otherwise a Radmin VPN or Hyper-V switch gets its DNS rewritten.
        public static string ActiveAdapter()
        {
            try
            {
                int bestIndex = -1;
                int bestMetric = int.MaxValue;
                using (System.Management.ManagementObjectSearcher searcher =
                    new System.Management.ManagementObjectSearcher(
                        "SELECT InterfaceIndex, Metric1 FROM Win32_IP4RouteTable " +
                        "WHERE Destination='0.0.0.0' AND Mask='0.0.0.0'"))
                {
                    foreach (System.Management.ManagementObject mo in searcher.Get())
                    {
                        int idx = Convert.ToInt32(mo["InterfaceIndex"]);
                        int metric = 9999;
                        try { metric = Convert.ToInt32(mo["Metric1"]); } catch { }
                        System.Net.NetworkInformation.NetworkInterface ni = ByIndex(idx);
                        if (ni == null) continue;
                        bool has4 = false;
                        foreach (System.Net.NetworkInformation.UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                            if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) has4 = true;
                        if (!has4) continue;
                        if (metric < bestMetric) { bestMetric = metric; bestIndex = idx; }
                    }
                }
                System.Net.NetworkInformation.NetworkInterface best = ByIndex(bestIndex);
                if (best != null) return best.Name;
            }
            catch { }

            // fallback: any up physical-looking adapter with a gateway
            try
            {
                foreach (System.Net.NetworkInformation.NetworkInterface ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Ethernet &&
                        ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211) continue;
                    string n = ni.Name.ToLowerInvariant();
                    if (n.Contains("vpn") || n.Contains("radmin") || n.Contains("vethernet") ||
                        n.Contains("tailscale") || n.Contains("zerotier") || n.Contains("hamachi")) continue;
                    if (ni.GetIPProperties().GatewayAddresses.Count == 0) continue;
                    return ni.Name;
                }
            }
            catch { }
            return null;
        }

        static System.Net.NetworkInformation.NetworkInterface ByIndex(int index)
        {
            if (index < 0) return null;
            try
            {
                foreach (System.Net.NetworkInformation.NetworkInterface ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    System.Net.NetworkInformation.IPv4InterfaceProperties p4 = ni.GetIPProperties().GetIPv4Properties();
                    if (p4 != null && p4.Index == index) return ni;
                }
            }
            catch { }
            return null;
        }
    }
}
