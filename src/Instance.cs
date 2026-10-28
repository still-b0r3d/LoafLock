using System.Runtime.InteropServices;
using System.Threading;

namespace LoafLock
{
    // One LoafLock per sign-in. Opening LoafLock.exe again brings up the running one's window.
    static class Instance
    {
        public const string MutexName = @"Local\LoafLock";
        const string ShowName = @"Local\LoafLock.Show";

        // the running copy watches this
        public static EventWaitHandle ShowRequests()
        {
            return new EventWaitHandle(false, EventResetMode.AutoReset, ShowName);
        }

        public static void AskToShow()
        {
            AllowSetForegroundWindow(-1);  // ASFW_ANY: so its window can come to the front
            using (var e = new EventWaitHandle(false, EventResetMode.AutoReset, ShowName)) e.Set();
        }

        [DllImport("user32.dll")]
        static extern bool AllowSetForegroundWindow(int processId);
    }
}
