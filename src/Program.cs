// LoafLock: locks the keyboard so a cat lying on it can't type. The mouse keeps working, unless
// it's set to lock too.
//
//   LoafLock.exe            sits in the taskbar corner as Rukus and opens its window; opened
//                           again while running, it brings up the running one's window
//   LoafLock.exe --startup  the same without the window (how Windows starts it at sign-in)
//   LoafLock.exe lock       locks the keyboard (starting LoafLock quietly if it isn't running),
//   LoafLock.exe unlock     or unlocks it: for a desktop shortcut or a Stream Deck button
//   LoafLock.exe --version  print the version
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LoafLock
{
    static class Program
    {
        public const string Version = "0.6";

        // "LoafLock v0.6 · Build: abc1234" ("dev" for a build made off GitHub)
        public static string VersionText { get { return "LoafLock v" + Version + " · Build: " + BuildInfo.Commit; } }

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int processId);

        public enum Command { Open, Startup, Lock, Unlock, Version, Unknown }

        // the first argument, if any; "lock", "--lock" and "/lock" all do
        public static Command Parse(string[] args)
        {
            if (args.Length == 0) return Command.Open;
            switch (args[0].ToLowerInvariant().TrimStart('-', '/'))
            {
                case "startup": return Command.Startup;
                case "lock": return Command.Lock;
                case "unlock": return Command.Unlock;
                case "version": case "v": return Command.Version;
            }
            return Command.Unknown;
        }

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

            Command cmd = Parse(args);
            if (cmd == Command.Version || cmd == Command.Unknown)
            {
                AttachConsole(-1); // print into the window it was started from
                if (cmd == Command.Unknown) Console.WriteLine("LoafLock doesn't know \"" + args[0] + "\". It takes: lock, unlock, --startup, --version.");
                Console.WriteLine(VersionText);
                return cmd == Command.Unknown ? 2 : 0;
            }

            // one LoafLock per sign-in; opening it again brings up the running one's window, and
            // lock / unlock go to the running one
            bool first;
            using (var one = new Mutex(true, Instance.MutexName, out first))
            {
                if (!first)
                {
                    Instance.Ask ask = cmd == Command.Lock ? Instance.Ask.Lock : cmd == Command.Unlock ? Instance.Ask.Unlock : Instance.Ask.Show;
                    if (Instance.Send(ask)) return 0;
                    AttachConsole(-1);
                    Console.WriteLine("LoafLock is running but didn't answer. If it's older than 0.4, quit it and start this one.");
                    return 1;
                }
                if (cmd == Command.Unlock) return 0;  // nothing running, so nothing's locked
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool quiet = cmd != Command.Open;
                Log.Write("started " + VersionText + (cmd == Command.Startup ? " with Windows" : cmd == Command.Lock ? " to lock" : ""));
                using (new Tray(Settings.Load(), quiet, cmd == Command.Lock)) Application.Run();
                Log.Write("quit");
            }
            return 0;
        }
    }
}
