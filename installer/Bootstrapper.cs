using System;
using System.Diagnostics;
using System.IO;

// net-suite bootstrapper.
// Downloads the repository (git clone, shallow) into %LOCALAPPDATA%\net-suite\app
// and runs its setup.ps1. Re-running updates the existing copy and re-runs setup.

class Program
{
    const string RepoUrl = "https://github.com/BobLoTiK/DYTgN.git";
    const string ZipUrl = "https://github.com/BobLoTiK/DYTgN/archive/refs/heads/main.zip";

    static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try
        {
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "net-suite");
            string target = Path.Combine(root, "app");

            Console.WriteLine();
            Console.WriteLine("  net-suite installer");
            Console.WriteLine("  -------------------");
            Console.WriteLine("  installing to: " + target);
            Console.WriteLine();

            if (File.Exists(Path.Combine(target, "setup.ps1")))
            {
                Console.WriteLine("[1/3] existing copy found, updating");
                int upd = Run("git", "-C \"" + target + "\" pull --ff-only", null);
                if (upd != 0)
                {
                    Console.WriteLine("      update skipped (needs git credentials or no changes)");
                }
            }
            else
            {
                Console.WriteLine("[1/3] downloading repository (about 130 MB, once)");
                Directory.CreateDirectory(target);
                int rc = Run("git", "clone --depth 1 \"" + RepoUrl + "\" \"" + target + "\"", null);
                if (rc != 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("  Clone failed. Either git is not installed, or this account");
                    Console.WriteLine("  has no access to the repository.");
                    Console.WriteLine();
                    Console.WriteLine("  Fallback: download " + ZipUrl);
                    Console.WriteLine("  unpack it anywhere and run install.bat from there.");
                    Pause();
                    return 1;
                }
            }

            string setup = Path.Combine(target, "setup.ps1");
            if (!File.Exists(setup))
            {
                Console.WriteLine("  setup.ps1 is missing in " + target);
                Pause();
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("[2/3] running setup");
            Console.WriteLine();
            int rc2 = Run("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + setup + "\"", target);
            if (rc2 != 0)
            {
                Console.WriteLine();
                Console.WriteLine("  setup finished with code " + rc2 + ", check the log in");
                Console.WriteLine("  " + Path.Combine(target, "logs"));
                Pause();
                return rc2;
            }

            Console.WriteLine();
            Console.WriteLine("[3/3] done");
            Console.WriteLine("  desktop: Net ON / Net OFF / Net STATUS / Notion");
            Console.WriteLine("  logs:    " + Path.Combine(target, "logs"));
            Pause();
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("  install failed: " + ex.Message);
            Pause();
            return 1;
        }
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
