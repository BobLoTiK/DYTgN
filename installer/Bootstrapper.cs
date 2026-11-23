using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;

// net-suite bootstrapper.
// 1) gets the repository into %LOCALAPPDATA%\net-suite\app - with git when git is there,
//    otherwise by downloading the GitHub archive (no git, no manual steps)
// 2) asks what to create (the control-panel shortcut, autostart) and whether to start now
// 3) runs setup.ps1 with those answers and shows the result
//
// debug switch: net-suite-setup.exe --fetch-only  - only download the repo, run nothing

class Program
{
    const string RepoUrl = "https://github.com/BobLoTiK/DYTgN.git";
    const string ZipUrl = "https://github.com/BobLoTiK/DYTgN/archive/refs/heads/main.zip";

    // files that stayed locked while the archive was copied over a running install
    static List<string> skipped = new List<string>();
    // the panel is closed for the update and started again at the end
    static bool panelWasRunning;

    static int Main(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
        try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { }

        bool fetchOnly = false;
        foreach (string a in args)
        {
            string s = a.Trim().ToLowerInvariant();
            if (s == "--fetch-only" || s == "/fetch-only") fetchOnly = true;
        }

        try
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "net-suite");
            string target = Path.Combine(root, "app");
            string setup = Path.Combine(target, "setup.ps1");

            Console.WriteLine();
            Console.WriteLine("  net-suite installer");
            Console.WriteLine("  -------------------");
            Console.WriteLine("  sets up zapret DPI bypass, TgWsProxy and the Notion tunnel,");
            Console.WriteLine("  then puts a window over all of it: gui\\NetSuite.exe");
            Console.WriteLine("  folder: " + target);
            Console.WriteLine();

            bool existing = File.Exists(setup);
            if (!Fetch(target, setup, existing))
            {
                Pause();
                return 1;
            }

            if (fetchOnly)
            {
                Console.WriteLine("  fetch only: repository is in place, nothing else was run");
                Pause();
                return 0;
            }

            Console.WriteLine();
            Console.WriteLine("[2/3] a few questions, Enter = yes");
            Console.WriteLine();
            bool desk = Ask("  Ярлык «Сеть (пульт)» на рабочем столе?", true);
            bool auto = Ask("  Добавить в автозапуск при входе в Windows?", true);
            bool task = Ask("  Проверять туннель каждые 10 минут (задача планировщика)?", true);
            bool start = Ask("  Запустить всё сразу?", true);

            string psArgs = "-NoProfile -ExecutionPolicy Bypass -File \"" + setup + "\"";
            if (!desk) psArgs += " -NoDesktop";
            if (!auto) psArgs += " -NoAutostart";
            if (!task) psArgs += " -NoTask";
            if (!start) psArgs += " -NoStart";

            Console.WriteLine();
            Console.WriteLine("[3/3] installing");
            Console.WriteLine();
            int rc2 = Run("powershell.exe", psArgs, target);

            Console.WriteLine();
            if (rc2 != 0)
            {
                Console.WriteLine("  setup finished with code " + rc2);
                Console.WriteLine("  log: " + Path.Combine(target, "logs"));
            }
            else
            {
                Console.WriteLine("  done");
                Console.WriteLine("  folder:  " + target);
                Console.WriteLine("  window:  gui\\NetSuite.exe   (or the shortcut on the desktop)");
                Console.WriteLine("  no window needed: on.bat / off.bat / status.bat");
                if (auto) Console.WriteLine("  autostart: on (remove net-suite.lnk from Startup to undo)");
                if (!desk) Console.WriteLine("  desktop shortcut: not created");
                if (panelWasRunning) ReopenPanel(target);
            }
            Pause();
            return rc2;
        }
        catch (Exception ex)
        {
            Console.WriteLine("  install failed: " + ex.Message);
            Pause();
            return 1;
        }
    }

    // the panel was open before the update - bring it back, so an update is not a way to
    // lose the window you were looking at
    static void ReopenPanel(string target)
    {
        try
        {
            string exe = Path.Combine(target, "gui\\NetSuite.exe");
            if (!File.Exists(exe)) return;
            ProcessStartInfo psi = new ProcessStartInfo(exe);
            psi.WorkingDirectory = target;
            psi.UseShellExecute = true;
            Process.Start(psi);
            Console.WriteLine("  window:  opened again");
        }
        catch (Exception ex)
        {
            Console.WriteLine("  window:  could not reopen (" + ex.Message + ")");
        }
    }

    // Gets the repo into target. git first (cheap updates), archive as the fallback that
    // needs nothing installed. Returns false only when both ways are gone.
    static bool Fetch(string target, string setup, bool existing)
    {
        bool git = HaveGit();

        if (existing)
        {
            if (git)
            {
                Console.WriteLine("[1/3] repo already here, updating");
                int upd = Run("git", "-C \"" + target + "\" pull --ff-only", null);
                if (upd == 0) return true;
                Console.WriteLine("      git pull did not work, refreshing from the archive");
            }
            else
            {
                Console.WriteLine("[1/3] repo already here, no git - refreshing from the archive");
            }
            return FromArchive(target, setup);
        }

        Directory.CreateDirectory(target);

        if (git)
        {
            Console.WriteLine("[1/3] downloading repo with git, ~130 MB, once");
            int rc = Run("git", "clone --depth 1 \"" + RepoUrl + "\" \"" + target + "\"", null);
            if (rc == 0 && File.Exists(setup)) return true;
            Console.WriteLine("      git clone did not work, switching to the archive");
        }
        else
        {
            Console.WriteLine("[1/3] git not found on this PC - that is fine, downloading the archive");
        }

        // a half-done clone would make later git calls fail; the archive does not need it
        if (!File.Exists(setup)) DropBrokenGit(target);
        return FromArchive(target, setup);
    }

    static bool FromArchive(string target, string setup)
    {
        string zip = Path.Combine(Path.GetTempPath(), "net-suite-repo.zip");
        Console.WriteLine("      source: " + ZipUrl);
        if (!DownloadZip(zip)) return false;

        // an update lands on top of a running install: our own window and the engines hold
        // their exe files open, so they have to let go before the files can be replaced
        CloseRunningPanel();

        Console.WriteLine("      unpacking");
        skipped.Clear();
        bool ok = ExtractZip(zip, target);
        try { File.Delete(zip); } catch { }

        if (!ok || !File.Exists(setup))
        {
            Console.WriteLine();
            Console.WriteLine("  The archive could not be unpacked automatically.");
            Console.WriteLine("  Fallback: download " + ZipUrl);
            Console.WriteLine("  unpack anywhere and run install.bat from that folder.");
            return false;
        }
        if (skipped.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  these files were busy and stayed as they were:");
            int shown = 0;
            foreach (string f in skipped)
            {
                Console.WriteLine("      " + f);
                if (++shown >= 5) { Console.WriteLine("      ..."); break; }
            }
            Console.WriteLine("  that is normal while the engines run; close net-suite and run the installer again");
        }
        Console.WriteLine("      repo is in place");
        return true;
    }

    // The panel is our own window and it lives in the tray: closing it politely does nothing,
    // so it is ended. The engines are left alone - their files are simply skipped above.
    static void CloseRunningPanel()
    {
        try
        {
            Process[] running = Process.GetProcessesByName("NetSuite");
            if (running.Length == 0) return;
            panelWasRunning = true;
            foreach (Process p in running)
            {
                try { p.CloseMainWindow(); } catch { }
            }
            Thread.Sleep(1500);
            foreach (Process p in running)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill();
                        p.WaitForExit(5000);
                    }
                }
                catch { }
            }
            Console.WriteLine("      closed the running panel (it holds gui\\NetSuite.exe open)");
        }
        catch { }
    }

    static bool DownloadZip(string zipPath)
    {
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "net-suite-installer");
                    wc.DownloadProgressChanged += delegate (object s, DownloadProgressChangedEventArgs e)
                    {
                        double got = e.BytesReceived / 1048576.0;
                        double all = e.TotalBytesToReceive / 1048576.0;
                        if (all > 0)
                            Console.Write("\r      {0,3}%  {1:0.0} / {2:0.0} MB   ", e.ProgressPercentage, got, all);
                    };
                    wc.DownloadFileAsync(new Uri(ZipUrl), zipPath);
                    while (wc.IsBusy) Thread.Sleep(200);
                }
                if (File.Exists(zipPath) && new FileInfo(zipPath).Length > 1000000)
                {
                    Console.WriteLine();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("      download failed: " + ex.Message);
            }
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            if (attempt == 1) Console.WriteLine("      trying once more");
            Thread.Sleep(1500);
        }
        return false;
    }

    static bool ExtractZip(string zip, string target)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "ns_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            bool unpacked = false;
            try
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(zip, tmp);
                unpacked = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("      .NET unzip failed (" + ex.GetType().Name + "), trying tar");
            }
            if (!unpacked)
            {
                if (Run("tar", "-xf \"" + zip + "\" -C \"" + tmp + "\"", null) != 0) return false;
            }

            string src = FindRepoRoot(tmp);
            if (src == null)
            {
                Console.WriteLine("      setup.ps1 is not inside the archive");
                return false;
            }
            CopyDir(src, target);
            return true;
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    // the archive has one top folder (DYTgN-main); find the level that holds setup.ps1
    static string FindRepoRoot(string dir)
    {
        if (File.Exists(Path.Combine(dir, "setup.ps1"))) return dir;
        string[] subs = Directory.GetDirectories(dir);
        foreach (string d in subs)
        {
            if (File.Exists(Path.Combine(d, "setup.ps1"))) return d;
        }
        foreach (string d in subs)
        {
            string inner = FindRepoRoot(d);
            if (inner != null) return inner;
        }
        return null;
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (string file in Directory.GetFiles(src))
        {
            string name = Path.GetFileName(file);
            if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Copy(file, Path.Combine(dst, name), true); }
            catch (IOException) { skipped.Add(name); }
            catch (UnauthorizedAccessException) { skipped.Add(name); }
        }
        foreach (string dir in Directory.GetDirectories(src))
        {
            string name = Path.GetFileName(dir);
            if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase)) continue;
            CopyDir(dir, Path.Combine(dst, name));
        }
    }

    static void DropBrokenGit(string target)
    {
        try
        {
            string g = Path.Combine(target, ".git");
            if (Directory.Exists(g))
            {
                Directory.Delete(g, true);
                Console.WriteLine("      removed a half-finished .git folder");
            }
        }
        catch { }
    }

    static bool HaveGit()
    {
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo("git", "--version");
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode == 0;
            }
        }
        catch
        {
            return false;
        }
    }

    static bool Ask(string question, bool def)
    {
        Console.Write(question + (def ? " [Y/n] " : " [y/N] "));
        string s = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(s)) return def;
        s = s.Trim().ToLowerInvariant();
        if (s == "y" || s == "yes" || s == "д" || s == "да" || s == "1" || s == "+" || s == "l") return true;
        if (s == "n" || s == "no" || s == "н" || s == "нет" || s == "0" || s == "-") return false;
        return def;
    }

    static int Run(string exe, string arguments, string workdir)
    {
        ProcessStartInfo psi = new ProcessStartInfo(exe, arguments);
        psi.UseShellExecute = false;
        if (!string.IsNullOrEmpty(workdir)) psi.WorkingDirectory = workdir;
        try
        {
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("      cannot run " + exe + ": " + ex.Message);
            return -1;
        }
    }

    static void Pause()
    {
        Console.WriteLine();
        Console.Write("  press Enter to close... ");
        Console.ReadLine();
    }
}
