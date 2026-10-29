using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace LoafLock
{
    // The keyboard hook, and the mouse hook when the mouse is locked too. Windows hands them every
    // key press and mouse event before any app gets one, and they can drop them. They run on a
    // thread of their own with its own message loop, and they're installed only while LoafLock
    // has the keyboard: locked, or still dropping the repeats of keys pressed while locked.
    // Unlocked, LoafLock doesn't see the keyboard or the mouse at all.
    //
    // Windows skips a hook that takes longer than about 300 ms on a key, and after a few of those
    // it removes the hook without a word. So the callbacks do as little as they can and allocate
    // nothing, their thread runs at high priority and never waits on a file (the log is written
    // by the window side, from Note), and the hooks are installed afresh every minute and after
    // sleep or signing back in. Whatever goes wrong in a callback, the key goes through: a bug
    // in LoafLock never traps the keyboard.
    sealed class KeyHook : IDisposable
    {
        // keys and mouse events the tests send carry this, and in test mode only they are judged
        public static readonly IntPtr TestTag = new IntPtr(0x4C6F6166);  // "Loaf"

        // a key that reaches LoafLock this late gets counted in the log (never which key)
        public const int SlowMs = 50;

        // Windows waits about 300 ms for the hook; a step of this thread's own longer than this
        // may have let keys go by
        const int StallMs = 200;

        public int RehookMs = 60000;  // how often the hooks are installed afresh
        public int TickMs = 500;      // how often lost releases are looked for

        // raised on the hook's thread
        public event Action<bool> LockChanged;  // true: locked; false: unlocked
        public event Action<string> Failed;     // the keyboard couldn't be locked; says why
        public event Action<string> Note;       // a line for the log (never which key)

        readonly KeyGate gate;
        readonly bool testMode;  // judge only keys sent with TestTag; every real key goes through
        readonly HookProc proc, mouseProc;  // kept here: Windows holds on to them, so the garbage collector mustn't take them
        readonly Thread thread;
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        volatile uint threadId;
        volatile string word = "";
        volatile bool installed, mouseInstalled, mouseToo, volumeThrough;
        volatile int rehooks;
        IntPtr hook, mouseHook;
        UIntPtr timer;
        int lastRehook;
        bool reportedLocked, checkPosted;
        int slowKeys, slowest;

        public KeyHook(KeyGate gate, bool testMode)
        {
            this.gate = gate;
            this.testMode = testMode;
            proc = Proc;
            mouseProc = MouseProc;
            // compile the callbacks and the gate now, so the first key isn't spent compiling them
            foreach (string name in new[] { "Proc", "MouseProc", "Judge", "Button" })
                RuntimeHelpers.PrepareMethod(typeof(KeyHook).GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic).MethodHandle);
            foreach (MethodInfo m in typeof(KeyGate).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                RuntimeHelpers.PrepareMethod(m.MethodHandle);
            thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "LoafLock keyboard hook" };
            thread.Start();
            ready.WaitOne();
        }

        public string Word { set { word = value ?? ""; } }
        public bool MouseToo { set { mouseToo = value; } }  // lock the mouse too, from the next Lock on
        public bool VolumeThrough { set { volumeThrough = value; } }  // let the volume keys through, from the next Lock on
        public bool Installed { get { return installed; } }
        public bool MouseInstalled { get { return mouseInstalled; } }
        public int Rehooks { get { return rehooks; } }
        public int Blocked { get { return Volatile.Read(ref gate.Blocked); } }  // fresh presses dropped, all told

        public void Lock() { Post(CmdLock); }
        public void Unlock() { Post(CmdUnlock); }
        public void Rehook() { Post(CmdRehook); }
        public void FreeMouse() { Post(CmdFreeMouse); }  // the mouse works again; the keyboard stays as it is

        public void Dispose()
        {
            Post(WM_QUIT);
            thread.Join(2000);
        }

        void Post(uint message)
        {
            PostThreadMessage(threadId, message, IntPtr.Zero, IntPtr.Zero);
        }

        void Run()
        {
            MSG msg;
            PeekMessage(out msg, IntPtr.Zero, 0, 0, 0);  // gives this thread its message queue
            threadId = GetCurrentThreadId();
            ready.Set();
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.hwnd != IntPtr.Zero) continue;  // no windows live on this thread
                int began = Environment.TickCount;
                try
                {
                    switch (msg.message)
                    {
                        case CmdLock: DoLock(); break;
                        case CmdUnlock: DoUnlock(); break;
                        case CmdRehook: Reinstall(); break;
                        case CmdFreeMouse: DoFreeMouse(); break;
                        case CmdCheck: checkPosted = false; Check(); break;
                        case WM_TIMER:
                            gate.Expire(Environment.TickCount);
                            if (Environment.TickCount - lastRehook >= RehookMs) Reinstall();
                            Check();
                            break;
                    }
                }
                catch (Exception e) { Say("keyboard hook: " + e); }  // the thread, and with it Unlock, has to keep going
                // held up that long, keys pressed meanwhile may have gone past unjudged: treat
                // what's seen next as maybe held from before, so its release goes through
                if (Environment.TickCount - began > StallMs) gate.Rehooked(Environment.TickCount);
            }
            Remove();
        }

        void DoLock()
        {
            if (hook == IntPtr.Zero && !Install()) return;
            gate.SetWord(word);
            int gap = LongestRepeatGapMs();
            gate.LostReleaseMs = Math.Max(1500, gap + 500);
            gate.HeldGraceMs = gap + 100;
            gate.RepeatSplitMs = RepeatSplitMs();
            gate.PassVolume = volumeThrough;
            if (mouseToo) { if (mouseHook == IntPtr.Zero) InstallMouse(); }
            else if (mouseHook != IntPtr.Zero) RemoveMouse();
            gate.Lock(Environment.TickCount);
            Report(true);
        }

        // The longest a held key goes quiet between repeats, from Windows' keyboard settings: the
        // repeat delay (250 to 1000 ms), or with Filter Keys on, its own delay and repeat rate.
        static int LongestRepeatGapMs()
        {
            int gap = 1000, delay;
            if (SystemParametersInfo(SPI_GETKEYBOARDDELAY, 0, out delay, 0)) gap = (delay + 1) * 250;
            var filter = new FILTERKEYS { cbSize = (uint)Marshal.SizeOf(typeof(FILTERKEYS)) };
            if (SystemParametersInfo(SPI_GETFILTERKEYS, filter.cbSize, ref filter, 0) && (filter.dwFlags & FKF_FILTERKEYSON) != 0)
                gap = Math.Max(gap, (int)Math.Max(filter.iDelayMSec, filter.iRepeatMSec));
            return gap;
        }

        // A new press waits the repeat delay before it repeats; a key already repeating repeats
        // every 1/rate. Halfway between tells a key LoafLock saw start from one that was going
        // already. 0 when they're too close to tell (a slow rate with a short delay), or with
        // Filter Keys on.
        static int RepeatSplitMs()
        {
            int delay, speed;
            if (!SystemParametersInfo(SPI_GETKEYBOARDDELAY, 0, out delay, 0) || !SystemParametersInfo(SPI_GETKEYBOARDSPEED, 0, out speed, 0)) return 0;
            var filter = new FILTERKEYS { cbSize = (uint)Marshal.SizeOf(typeof(FILTERKEYS)) };
            if (SystemParametersInfo(SPI_GETFILTERKEYS, filter.cbSize, ref filter, 0) && (filter.dwFlags & FKF_FILTERKEYSON) != 0) return 0;
            double wait = (delay + 1) * 250, every = 1000 / (2.5 + Math.Max(0, Math.Min(31, speed)) * 27.5 / 31);  // speed 0 to 31: about 2.5 to 30 a second
            return every * 1.5 < wait - 50 ? (int)((wait + every) / 2) : 0;
        }

        void DoUnlock()
        {
            gate.Unlock();
            Check();
        }

        void DoFreeMouse()
        {
            if (mouseHook == IntPtr.Zero) return;
            RemoveMouse();
            gate.ForgetMouse();
            Say("let go of the mouse");
            Check();
        }

        bool Install()
        {
            hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                Say("couldn't start the keyboard hook (Windows error " + error + ")");
                Action<string> failed = Failed;
                if (failed != null)
                {
                    try { failed("LoafLock couldn't lock the keyboard (Windows error " + error + "). It's still unlocked."); }
                    catch (Exception e) { Say("couldn't say so: " + e.Message); }
                }
                return false;
            }
            installed = true;
            lastRehook = Environment.TickCount;
            timer = SetTimer(IntPtr.Zero, UIntPtr.Zero, (uint)Math.Min(TickMs, RehookMs), IntPtr.Zero);
            return true;
        }

        void InstallMouse()
        {
            mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, GetModuleHandle(null), 0);
            if (mouseHook == IntPtr.Zero) Say("couldn't lock the mouse (Windows error " + Marshal.GetLastWin32Error() + "); the keyboard locks anyway");
            mouseInstalled = mouseHook != IntPtr.Zero;
        }

        // Fresh hooks first, then the old ones out. Keys reach the hooks only while this thread
        // waits for messages, so none can slip past in between.
        void Reinstall()
        {
            if (hook == IntPtr.Zero) return;
            lastRehook = Environment.TickCount;
            IntPtr fresh = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
            if (fresh == IntPtr.Zero)
            {
                Say("couldn't renew the keyboard hook (Windows error " + Marshal.GetLastWin32Error() + "); keeping the old one");
                return;
            }
            UnhookWindowsHookEx(hook);
            hook = fresh;
            if (mouseHook != IntPtr.Zero)
            {
                fresh = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, GetModuleHandle(null), 0);
                if (fresh == IntPtr.Zero) Say("couldn't renew the mouse hook (Windows error " + Marshal.GetLastWin32Error() + "); keeping the old one");
                else
                {
                    UnhookWindowsHookEx(mouseHook);
                    mouseHook = fresh;
                }
            }
            gate.Rehooked(Environment.TickCount);
            rehooks++;
        }

        void RemoveMouse()
        {
            if (mouseHook == IntPtr.Zero) return;
            UnhookWindowsHookEx(mouseHook);
            mouseHook = IntPtr.Zero;
            mouseInstalled = false;
        }

        void Remove()
        {
            if (timer != UIntPtr.Zero)
            {
                KillTimer(IntPtr.Zero, timer);
                timer = UIntPtr.Zero;
            }
            RemoveMouse();
            if (hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
            installed = false;
        }

        // after a key, a command or a tick: say when the gate has unlocked, and let the hooks go
        // once nothing is held from the locked time
        void Check()
        {
            if (reportedLocked && !gate.Locked) Report(false);
            if (!gate.Busy) Remove();
            if (slowKeys > 0)
            {
                Say(slowKeys + " key press(es) took over " + SlowMs + " ms to reach LoafLock (the slowest " + slowest + " ms)");
                slowKeys = slowest = 0;
            }
        }

        void Report(bool locked)
        {
            reportedLocked = locked;
            Action<bool> changed = LockChanged;
            if (changed == null) return;
            try { changed(locked); }
            catch (Exception e) { Say("couldn't pass on " + (locked ? "locked" : "unlocked") + ": " + e.Message); }  // e.g. the app is closing
        }

        // hands a line for the log to whoever listens; this thread never waits on a file
        void Say(string text)
        {
            Action<string> note = Note;
            if (note == null) return;
            try { note(text); }
            catch { }
        }

        // Every key press and release, before any app sees it. Fast, and allocates nothing.
        IntPtr Proc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    // KBDLLHOOKSTRUCT: vkCode at 0, flags at 8, time at 12, dwExtraInfo at 16
                    bool sent = (Marshal.ReadInt32(lParam, 8) & LLKHF_INJECTED) != 0;  // by a program, not a keyboard
                    if (testMode ? sent && Marshal.ReadIntPtr(lParam, 16) == TestTag : !sent)
                    {
                        int message = wParam.ToInt32();
                        if (Judge(Marshal.ReadInt32(lParam, 0), message == WM_KEYDOWN || message == WM_SYSKEYDOWN, Marshal.ReadInt32(lParam, 12)))
                            return (IntPtr)1;
                    }
                }
            }
            catch { }
            return CallNextHookEx(hook, nCode, wParam, lParam);
        }

        // Every mouse move, click and wheel turn, while the mouse is locked too. Moves and the
        // wheel are dropped while locked; the buttons go through the gate like keys, so a
        // button's release goes with its press. Fast, and allocates nothing.
        IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    // MSLLHOOKSTRUCT: pt at 0, mouseData at 8, flags at 12, time at 16, dwExtraInfo
                    // after it (at 24 in a 64-bit process, 20 in a 32-bit one)
                    bool sent = (Marshal.ReadInt32(lParam, 12) & LLMHF_INJECTED) != 0;
                    if (testMode ? sent && Marshal.ReadIntPtr(lParam, IntPtr.Size == 8 ? 24 : 20) == TestTag : !sent)
                    {
                        int message = wParam.ToInt32();
                        int vk = Button(message, Marshal.ReadInt32(lParam, 8));
                        bool drop = vk == 0
                            ? gate.Locked
                            : Judge(vk, message == WM_LBUTTONDOWN || message == WM_RBUTTONDOWN || message == WM_MBUTTONDOWN || message == WM_XBUTTONDOWN, Marshal.ReadInt32(lParam, 16));
                        if (drop) return (IntPtr)1;
                    }
                }
            }
            catch { }
            return CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        // one key or mouse button, pressed or let go, at time (Windows' clock): true drops it
        bool Judge(int vk, bool pressed, int time)
        {
            int late = Environment.TickCount - time;
            if (late > SlowMs)
            {
                slowKeys++;
                if (late > slowest) slowest = late;
            }
            bool wasLocked = gate.Locked;
            bool drop = gate.OnKey(vk, pressed, time, late);
            // slow by the time it's judged (this thread was held up partway): Windows may not
            // have waited for the answer
            if (pressed && drop && Environment.TickCount - time >= KeyGate.LateMs) gate.Unpair(vk);
            if (wasLocked != gate.Locked || (!gate.Busy && !checkPosted))
            {
                checkPosted = true;
                PostThreadMessage(threadId, CmdCheck, IntPtr.Zero, IntPtr.Zero);
            }
            return drop;
        }

        // a mouse button's key code, or 0 for a move or the wheel
        static int Button(int message, int mouseData)
        {
            switch (message)
            {
                case WM_LBUTTONDOWN: case WM_LBUTTONUP: return 1;  // VK_LBUTTON
                case WM_RBUTTONDOWN: case WM_RBUTTONUP: return 2;  // VK_RBUTTON
                case WM_MBUTTONDOWN: case WM_MBUTTONUP: return 4;  // VK_MBUTTON
                case WM_XBUTTONDOWN: case WM_XBUTTONUP: return ((mouseData >> 16) & 0xFFFF) == 2 ? 6 : 5;  // VK_XBUTTON2 / VK_XBUTTON1
            }
            return 0;
        }

        // ---- Windows ----

        const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
        const int LLKHF_INJECTED = 0x10, LLMHF_INJECTED = 0x1;
        const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205,
            WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_XBUTTONDOWN = 0x020B, WM_XBUTTONUP = 0x020C;
        const uint WM_QUIT = 0x0012, WM_TIMER = 0x0113, WM_APP = 0x8000;
        const uint CmdLock = WM_APP + 1, CmdUnlock = WM_APP + 2, CmdRehook = WM_APP + 3, CmdCheck = WM_APP + 4, CmdFreeMouse = WM_APP + 5;

        delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam, lParam;
            public uint time;
            public int x, y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr module, uint threadId);
        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")]
        static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")]
        static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")]
        static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint ms, IntPtr callback);
        [DllImport("user32.dll")]
        static extern bool KillTimer(IntPtr hwnd, UIntPtr id);

        const uint SPI_GETKEYBOARDSPEED = 0x000A, SPI_GETKEYBOARDDELAY = 0x0016, SPI_GETFILTERKEYS = 0x0032, FKF_FILTERKEYSON = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        struct FILTERKEYS
        {
            public uint cbSize, dwFlags, iWaitMSec, iDelayMSec, iRepeatMSec, iBounceMSec;
        }

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
        static extern bool SystemParametersInfo(uint action, uint param, out int value, uint ini);
        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
        static extern bool SystemParametersInfo(uint action, uint param, ref FILTERKEYS value, uint ini);
    }
}
