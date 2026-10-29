using System;
using System.Runtime.InteropServices;

namespace LoafLock
{
    // "Lock by itself": how long nobody's used the keyboard or the mouse, from Windows' own
    // count (what screen savers go by), so an unlocked LoafLock still watches no keys. The
    // mouse counts too: telling typing apart from the mouse would need the hook on all the
    // time. A game controller doesn't count for Windows, so games in exclusive fullscreen are
    // left alone; a fullscreen video in a browser isn't one of those, and does get locked.
    static class Idle
    {
        public static readonly int[] Choices = { 5, 10, 15, 30, 60 };  // minutes
        public const int DefaultMinutes = 10;

        // time to lock? idleMs: nobody at the keyboard or mouse for this long; sinceMs: since
        // LoafLock last unlocked, started or the PC woke (whichever was last), since typing the
        // unlock word doesn't count as using the keyboard for Windows
        public static bool Due(int minutes, long idleMs, long sinceMs, bool game)
        {
            return !game && Math.Min(idleMs, sinceMs) >= minutes * 60000L;
        }

        public static long IdleMs()
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref info)) return 0;
            return unchecked((uint)Environment.TickCount - info.dwTime);  // both wrap after 49.7 days
        }

        // a game (or a presentation) has the whole screen to itself
        public static bool GameFullscreen()
        {
            int state;
            if (SHQueryUserNotificationState(out state) != 0) return false;
            return state == QUNS_RUNNING_D3D_FULL_SCREEN || state == QUNS_PRESENTATION_MODE;
        }

        const int QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;

        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO
        {
            public uint cbSize, dwTime;
        }

        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("shell32.dll")]
        static extern int SHQueryUserNotificationState(out int state);
    }
}
