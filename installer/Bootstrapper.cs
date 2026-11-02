using System;
using System.Diagnostics;
using System.IO;

// net-suite bootstrapper.
// 1) clones/updates the repository into %LOCALAPPDATA%\net-suite\app
// 2) asks what to create (desktop shortcuts, autostart) and whether to start now
// 3) runs setup.ps1 with those answers and shows the result

class Program
{
    const string RepoUrl = "https://github.com/BobLoTiK/DYTgN.git";
    const string ZipUrl = "https://github.com/BobLoTiK/DYTgN/archive/refs/heads/main.zip";

    static int Main(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

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
            Console.WriteLine("  folder: " + target);
            Console.WriteLine();

            bool existing = File.Exists(setup);
            if (existing)
            {
                Console.WriteLine("[1/3] repo already here, updating");
                int upd = Run("git", "-C \"" + target + "\" pull --ff-only", null);
                if (upd != 0) Console.WriteLine("      update skipped (offline or git needs access)");
            }
            else
            {
                Console.WriteLine("[1/3] downloading repo, ~130 MB, once");
                Directory.CreateDirectory(target);
                int rc = Run("git", "clone --depth 1 \"" + RepoUrl + "\" \"" + target + "\"", null);
                if (rc != 0 || !File.Exists(setup))
                {
                    Console.WriteLine();
                    Console.WriteLine("  Clone failed: git is missing or this account has no access.");
                    Console.WriteLine("  Fallback: download " + ZipUrl);
                    Console.WriteLine("  unpack anywhere and run install.bat from that folder.");
                    Pause();
                    return 1;
                }
            }

            Console.WriteLine();
            Console.WriteLine("[2/3] a few questions, Enter = yes");
            Console.WriteLine();
            bool desk = Ask("  Ярлыки на рабочем столе (Сеть вкл / выкл / статус / Notion)?", true);
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
                Console.WriteLine("  start:   on.bat      stop: off.bat     state: status.bat");
                if (auto) Console.WriteLine("  autostart: on (remove net-suite.lnk from Startup to undo)");
                if (!desk) Console.WriteLine("  shortcuts: not created");
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

    static bool Ask(string question, bool def)
    {
        Console.Write(question + (def ? " [Y/n] " : " [y/N] "));
        string s = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(s)) return def;
        s = s.Trim().ToLowerInvariant();
        if (s == "y" || s == "yes" || s == "д" || s == "да" || s == "1" || s == "+") return true;
        if (s == "n" || s == "no" || s == "н" || s == "нет" || s == "0" || s == "-") return false;
        return def;
    }

    static int Run(string exe, string arguments, string workdir)
    {
        var psi = new ProcessStartInfo(exe, arguments);
        psi.UseShellExecute = false;
        if (!string.IsNullOrEmpty(workdir)) psi.WorkingDirectory = workdir;
        try
        {
            using (var p = Process.Start(psi))
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
