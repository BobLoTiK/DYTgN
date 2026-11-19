using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace NetSuite
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)12288;
                ServicePointManager.DefaultConnectionLimit = 24;
            }
            catch { }

            // Privileged helper mode: the same exe, started with UAC, does one job.
            if (args.Length >= 4 && args[0] == "--elevated")
            {
                int code = ElevTasks.Run(args[1], args[2], args[3]);
                Environment.Exit(code);
                return;
            }

            // Open straight on a given page (debug aid): --page configs
            if (args.Length >= 2 && args[0] == "--page")
            {
                Paths.Init();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm pf = new MainForm();
                pf.Show();
                pf.ShowPage(args[1]);
                Application.Run(pf);
                return;
            }

            // Scripted action test: press the button from the command line.
            if (args.Length >= 2 && args[0] == "--act")
            {
                Paths.Init();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm f = new MainForm();
                f.ShowInTaskbar = false;
                f.Opacity = 0.02;
                f.Show();
                Application.DoEvents();
                bool ok = f.RunActionAndWait(args[1], "cli " + args[1], 180000);
                Log.Write("cli action " + args[1] + ": " + (ok ? "finished" : "timeout"), System.Drawing.Color.Gray);
                f.HardExit();
                return;
            }

            // Self-test mode: render every page to PNG and exit. No clicks needed.
            if (args.Length >= 2 && args[0] == "--shot")
            {
                Paths.Init();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm f = new MainForm();
                f.ShowInTaskbar = false;
                f.Opacity = 0.02;
                f.Show();
                f.ShootPages(args[1]);
                f.HardExit();
                return;
            }

            bool created;
            using (Mutex mtx = new Mutex(true, "net-suite-gui-1", out created))
            {
                if (!created)
                {
                    MessageBox.Show("net-suite уже запущен — окно найдёте в панели задач или в трее.", "net-suite",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Paths.Init();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }
}
