using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace LoafLock
{
    // The keyboard hook. Windows hands it every key press before any app gets one, and it can
    // drop them. It runs on a thread of its own with its own message loop, and it's installed
    // only while LoafLock has the keyboard: locked, or still dropping the repeats of keys
    // pressed while locked. Unlocked, LoafLock doesn't see the keyboard at all.
    //
    // Windows skips a hook that takes longer than about 300 ms on a key, and after a few of those
    // it removes the hook without a word. So the callback does as little as it can and allocates
    // nothing, its thread runs at high priority and never waits on a file (the log is written
    // by the window side, from Note), and the hook is installed afresh every minute and after
    // sleep or signing back in. Whatever goes wrong in the callback, the key goes through: a bug
    // in LoafLock never traps the keyboard.
    sealed class KeyHook : IDisposable
    {
        // keys the tests send carry this, and in test mode only they are judged
        public static readonly IntPtr TestTag = new IntPtr(0x4C6F6166);  // "Loaf"

        // a key that reaches LoafLock this late gets counted in the log (never which key)
        public const int SlowMs = 50;

        public int RehookMs = 60000;  // how often the hook is installed afresh
        public int TickMs = 500;      // how often lost releases are looked for

        // raised on the hook's thread
        public event Action<bool> LockChanged;  // true: locked; false: unlocked
        public event Action<string> Failed;     // the keyboard couldn't be locked; says why
        public event Action<string> Note;       // a line for the log (never which key)

        readonly KeyGate gate;
        readonly bool testMode;  // judge only keys sent with TestTag; every real key goes through
        readonly HookProc proc;  // kept here: Windows holds on to it, so the garbage collector mustn't take it
        readonly Thread thread;
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        volatile uint threadId;
        volatile string word = "";
        volatile bool installed;
        volatile int rehooks;
        IntPtr hook;
        UIntPtr timer;
        int lastRehook;
        bool reportedLocked, checkPosted;
        int slowKeys, slowest;

        public KeyHook(KeyGate gate, bool testMode)
        {
            this.gate = gate;
            this.testMode = testMode;
            proc = Proc;
            // compile the callback and the gate now, so the first key isn't spent compiling them
            RuntimeHelpers.PrepareMethod(typeof(KeyHook).GetMethod("Proc", BindingFlags.Instance | BindingFlags.NonPublic).MethodHandle);
            foreach (MethodInfo m in typeof(KeyGate).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                RuntimeHelpers.PrepareMethod(m.MethodHandle);
            thread = new Thread(Run) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "LoafLock keyboard hook" };
            thread.Start();
            ready.WaitOne();
        }

        public string Word { set { word = value ?? ""; } }
        public bool Installed { get { return installed; } }
        public int Rehooks { get { return rehooks; } }
        public int Blocked { get { return Volatile.Read(ref gate.Blocked); } }  // fresh presses dropped, all told

        public void Lock() { Post(CmdLock); }
        public void Unlock() { Post(CmdUnlock); }
        public void Rehook() { Post(CmdRehook); }

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
                try
                {
                    switch (msg.message)
                    {
                        case CmdLock: DoLock(); break;
                        case CmdUnlock: DoUnlock(); break;
                        case CmdRehook: Reinstall(); break;
                        case CmdCheck: checkPosted = false; Check(); break;
                        case WM_TIMER:
                            gate.Expire(Environment.TickCount);
                            if (Environment.TickCount - lastRehook >= RehookMs) Reinstall();
                            Check();
                            break;
                    }
                }
                catch (Exception e) { Say("keyboard hook: " + e); }  // the thread, and with it Unlock, has to keep going
            }
            Remove();
        }

        void DoLock()
        {
            if (hook == IntPtr.Zero && !Install()) return;
            gate.SetWord(word);
            gate.LostReleaseMs = Math.Max(1500, LongestRepeatGapMs() + 500);
            gate.Lock();
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

        void DoUnlock()
        {
            gate.Unlock();
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

        // A fresh hook first, then the old one out. Keys reach the hook only while this thread
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
            rehooks++;
        }

        void Remove()
        {
            if (timer != UIntPtr.Zero)
            {
                KillTimer(IntPtr.Zero, timer);
                timer = UIntPtr.Zero;
            }
            if (hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
            installed = false;
        }

        // after a key, a command or a tick: say when the gate has unlocked, and let the hook go
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
                        int time = Marshal.ReadInt32(lParam, 12);
                        int late = Environment.TickCount - time;
                        if (late > SlowMs)
                        {
                            slowKeys++;
                            if (late > slowest) slowest = late;
                        }
                        int message = wParam.ToInt32();
                        bool wasLocked = gate.Locked;
                        bool drop = gate.OnKey(Marshal.ReadInt32(lParam, 0), message == WM_KEYDOWN || message == WM_SYSKEYDOWN, time);
                        if (wasLocked != gate.Locked || (!gate.Busy && !checkPosted))
                        {
                            checkPosted = true;
                            PostThreadMessage(threadId, CmdCheck, IntPtr.Zero, IntPtr.Zero);
                        }
                        if (drop) return (IntPtr)1;
                    }
                }
            }
            catch { }
            return CallNextHookEx(hook, nCode, wParam, lParam);
        }

        // ---- Windows ----

        const int WH_KEYBOARD_LL = 13;
        const int LLKHF_INJECTED = 0x10;
        const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        const uint WM_QUIT = 0x0012, WM_TIMER = 0x0113, WM_APP = 0x8000;
        const uint CmdLock = WM_APP + 1, CmdUnlock = WM_APP + 2, CmdRehook = WM_APP + 3, CmdCheck = WM_APP + 4;

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

        const uint SPI_GETKEYBOARDDELAY = 0x0016, SPI_GETFILTERKEYS = 0x0032, FKF_FILTERKEYSON = 0x1;

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
