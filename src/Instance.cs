using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace LoafLock
{
    // One LoafLock per sign-in. Opening LoafLock.exe again brings up the running one's window,
    // and "LoafLock.exe lock" / "unlock" ask the running one to lock or unlock.
    static class Instance
    {
        // the tests use names of their own, so they never reach a LoafLock that's really running
        public static string Name = @"Local\LoafLock";

        public static string MutexName { get { return Name; } }

        public enum Ask { Show, Lock, Unlock }

        // the running copy watches these
        public static EventWaitHandle Requests(Ask what)
        {
            return new EventWaitHandle(false, EventResetMode.AutoReset, Name + "." + what);
        }

        // false: the running copy never answered (still starting up for too long, or one older
        // than 0.4, which doesn't take lock / unlock)
        public static bool Send(Ask what, int waitMs = 3000)
        {
            if (what == Ask.Show) AllowSetForegroundWindow(-1);  // ASFW_ANY: so its window can come to the front
            int start = Environment.TickCount;
            while (true)
            {
                EventWaitHandle e;
                if (EventWaitHandle.TryOpenExisting(Name + "." + what, out e))
                {
                    using (e) e.Set();
                    return true;
                }
                if (Environment.TickCount - start >= waitMs) return false;
                Thread.Sleep(100);  // it holds the one-per-sign-in lock, but isn't listening yet
            }
        }

        [DllImport("user32.dll")]
        static extern bool AllowSetForegroundWindow(int processId);
    }
}
