// LoafLock: locks the keyboard so a cat lying on it can't type. The mouse keeps working.
//
//   LoafLock.exe            sits in the taskbar corner as Rukus and opens its window; opened
//                           again while running, it brings up the running one's window
//   LoafLock.exe --startup  the same without the window (how Windows starts it at sign-in)
//   LoafLock.exe --version  print the version
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LoafLock
{
    static class Program
    {
        public const string Version = "0.1";

        // "LoafLock v0.1 · Build: abc1234" ("dev" for a build made off GitHub)
        public static string VersionText { get { return "LoafLock v" + Version + " · Build: " + BuildInfo.Commit; } }

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int processId);

        [STAThread]
        static int Main(string[] args)
        {
            // an error in a window would otherwise only show a dialog: the log keeps a trace of
            // it, and of anything that ends the process from another thread
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                Log.Write("unhandled: " + e.Exception);
                MessageBox.Show("LoafLock hit an error: " + e.Exception.Message + "\n\nThe details are in LoafLock.log in " + Settings.Dir + ".",
                    "LoafLock", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e) { Log.Write("crashed: " + e.ExceptionObject); };

            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (cmd == "--version" || cmd == "-v")
            {
                AttachConsole(-1); // print into the window it was started from
                Console.WriteLine(VersionText);
                return 0;
            }

            // one LoafLock per sign-in; opening it again brings up the running one's window
            bool quiet = cmd == "--startup";
            bool first;
            using (var one = new Mutex(true, Instance.MutexName, out first))
            {
                if (!first)
                {
                    if (!quiet) Instance.AskToShow();
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Log.Write("started " + VersionText + (quiet ? " with Windows" : ""));
                using (new Tray(Settings.Load(), quiet)) Application.Run();
                Log.Write("quit");
            }
            return 0;
        }
    }
}
