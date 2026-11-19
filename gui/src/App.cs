using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace NetSuite
{
    static class Paths
    {
        public static string ExeDir;
        public static string RepoRoot;
        public static string ConfigPath;
        public static string LogsDir;

        public static void Init()
        {
            ExeDir = Path.GetDirectoryName(Application.ExecutablePath);
            DirectoryInfo d = new DirectoryInfo(ExeDir);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "netsuite.json"))) d = d.Parent;
            RepoRoot = d != null ? d.FullName : ExeDir;
            ConfigPath = Path.Combine(RepoRoot, "netsuite.json");
            LogsDir = Path.Combine(RepoRoot, "logs");
            try { Directory.CreateDirectory(LogsDir); } catch { }
        }

        public static string InRepo(string rel) { return Path.Combine(RepoRoot, rel); }
    }

    // Read-only view of netsuite.json: the GUI and the scripts share one config.
    public class Config
    {
        public string ZapretProcess = "winws";
        public string ZapretService = "zapret";
        public string ZapretFolder;
        public string TornetFolder;
        public string TgwsProcess = "TgWsProxy_windows";
        public string TgwsExe;
        public int TgwsPort = 1443;
        public string NotionFolder;
        public int NotionSocksPort = 10808;
        public string NotionPacUrl = "http://127.0.0.1:10810/proxy.pac";
        public string NotionCheckUrl = "https://www.notion.com/";
        public string LoadError = "";

        public static Config Load()
        {
            Config c = new Config();
            try
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                ser.MaxJsonLength = 4 * 1024 * 1024;
                Dictionary<string, object> root = ser.Deserialize<Dictionary<string, object>>(File.ReadAllText(Paths.ConfigPath));
                object o;
                if (root.TryGetValue("components", out o))
                {
                    Dictionary<string, object> comps = o as Dictionary<string, object>;
                    if (comps != null)
                    {
                        Dictionary<string, object> z = Dict(comps, "zapret");
                        if (z != null)
                        {
                            c.ZapretProcess = Str(z, "process", c.ZapretProcess);
                            c.ZapretService = Str(z, "service", c.ZapretService);
                            c.ZapretFolder = Expand(Str(z, "folder", ""));
                        }
                        Dictionary<string, object> t = Dict(comps, "tgws");
                        if (t != null)
                        {
                            c.TgwsProcess = Str(t, "process", c.TgwsProcess);
                            c.TgwsExe = Expand(Str(t, "exe", ""));
                            c.TgwsPort = Int(t, "port", c.TgwsPort);
                        }
                        Dictionary<string, object> n = Dict(comps, "notion");
                        if (n != null)
                        {
                            c.NotionFolder = Expand(Str(n, "folder", ""));
                            c.NotionSocksPort = Int(n, "socksPort", c.NotionSocksPort);
                            c.NotionPacUrl = Str(n, "pacUrl", c.NotionPacUrl);
                            c.NotionCheckUrl = Str(n, "checkUrl", c.NotionCheckUrl);
                        }
                    }
                }
            }
            catch (Exception ex) { c.LoadError = ex.Message; }
            return c;
        }

        static Dictionary<string, object> Dict(Dictionary<string, object> parent, string key)
        {
            object o;
            if (!parent.TryGetValue(key, out o)) return null;
            return o as Dictionary<string, object>;
        }

        static string Str(Dictionary<string, object> d, string key, string fallback)
        {
            object o;
            if (!d.TryGetValue(key, out o) || o == null) return fallback;
            return Convert.ToString(o);
        }

        static int Int(Dictionary<string, object> d, string key, int fallback)
        {
            object o;
            if (!d.TryGetValue(key, out o) || o == null) return fallback;
            try { return Convert.ToInt32(o); } catch { return fallback; }
        }

        public static string Expand(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            return p.Replace("%REPO%", Paths.RepoRoot)
                    .Replace("%LOCALAPPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
                    .Replace("%APPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))
                    .Replace("%USERPROFILE%", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
                    .Replace("%DOCUMENTS%", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        }
    }

    // One log for the user (window) and for the record (logs\gui-*.log).
    static class Log
    {
        public static event Action<string, Color> Line;
        static readonly object sync = new object();

        public static void Write(string text, Color c)
        {
            string stamp = DateTime.Now.ToString("HH:mm:ss");
            string line = stamp + "  " + text;
            Action<string, Color> h = Line;
            if (h != null) { try { h(line, c); } catch { } }
            try
            {
                string f = Path.Combine(Paths.LogsDir, "gui-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                lock (sync) File.AppendAllText(f, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        public static void Info(string s) { Write(s, Theme.Muted); }
        public static void Good(string s) { Write(s, Theme.Ok); }
        public static void Warn(string s) { Write(s, Theme.Warn); }
        public static void Bad(string s) { Write(s, Theme.Err); }
        public static void Step(string s) { Write(s, Theme.Accent); }
    }

    // Wraps powershell.exe / bat files so their output lands in the log window.
    static class Run
    {
        public static Process Start(string file, string args, Action<string> onLine, Action<int> onExit)
        {
            ProcessStartInfo psi = new ProcessStartInfo(file, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            try { psi.WorkingDirectory = Paths.RepoRoot; } catch { }
            Process p = new Process();
            p.StartInfo = psi;
            p.EnableRaisingEvents = true;
            if (onLine != null)
            {
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) onLine(e.Data); };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) onLine(e.Data); };
            }
            if (onExit != null) p.Exited += delegate { try { onExit(p.ExitCode); } catch { } };
            p.Start();
            if (onLine != null) { p.BeginOutputReadLine(); p.BeginErrorReadLine(); }
            return p;
        }

        // powershell -File, with UTF-8 forced on the pipeline so Cyrillic survives.
        public static Process Script(string script, string args, Action<string> onLine, Action<int> onExit)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("-NoProfile -ExecutionPolicy Bypass -Command \"");
            sb.Append("[Console]::OutputEncoding=[Text.Encoding]::UTF8; $OutputEncoding=[Text.Encoding]::UTF8; ");
            sb.Append("& '").Append(script).Append("' ").Append(args).Append("\"");
            return Start("powershell.exe", sb.ToString(), onLine, onExit);
        }

        public static void Bat(string batPath, Action<string> onLine, Action<int> onExit)
        {
            Start("cmd.exe", "/c \"" + batPath + "\"", onLine, onExit);
        }
    }

    // Runs a privileged job in a copy of this exe started with UAC (Verb=runas).
    // Requests and answers travel through json files in %TEMP%.
    static class Elev
    {
        public static bool Ask(string task, Dictionary<string, object> payload, int timeoutMs, out Dictionary<string, object> result, out string error)
        {
            result = null;
            error = "";
            string inFile = Path.Combine(Path.GetTempPath(), "netnsgui-" + Guid.NewGuid().ToString("N") + ".in.json");
            string outFile = Path.Combine(Path.GetTempPath(), "netnsgui-" + Guid.NewGuid().ToString("N") + ".out.json");
            JavaScriptSerializer ser = new JavaScriptSerializer();
            try
            {
                if (payload == null) payload = new Dictionary<string, object>();
                File.WriteAllText(inFile, ser.Serialize(payload), Encoding.UTF8);
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath,
                    "--elevated " + task + " \"" + inFile + "\" \"" + outFile + "\"");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.WorkingDirectory = Paths.ExeDir;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(timeoutMs);
                    if (!p.HasExited) { try { p.Kill(); } catch { } error = "задача не уложилась в таймаут"; return false; }
                }
                if (!File.Exists(outFile)) { error = "привилегированная часть не вернула результат"; return false; }
                string json = File.ReadAllText(outFile, Encoding.UTF8);
                Dictionary<string, object> dict = ser.Deserialize<Dictionary<string, object>>(json);
                if (dict == null) { error = "пустой ответ"; return false; }
                object okObj;
                dict.TryGetValue("ok", out okObj);
                bool ok = okObj != null && Convert.ToBoolean(okObj);
                object errObj;
                if (dict.TryGetValue("error", out errObj) && errObj != null) error = Convert.ToString(errObj);
                result = dict;
                return ok;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                error = ex.NativeErrorCode == 1223 ? "UAC отклонён" : ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                try { File.Delete(inFile); } catch { }
                try { File.Delete(outFile); } catch { }
            }
        }

        public static string Json(Dictionary<string, object> d, string key, string fallback)
        {
            if (d == null) return fallback;
            object o;
            if (!d.TryGetValue(key, out o) || o == null) return fallback;
            return Convert.ToString(o);
        }

        public static bool IsAdmin()
        {
            try
            {
                System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent();
                System.Security.Principal.WindowsPrincipal p = new System.Security.Principal.WindowsPrincipal(id);
                return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}

