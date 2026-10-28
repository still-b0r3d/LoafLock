using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace LoafLock.Tests
{
    // The real hooks on their real thread, fed keys and mouse events this test sends itself.
    // They carry KeyHook.TestTag, and in test mode only tagged ones are judged, so the keyboard
    // and mouse in use are never locked. F24 and F23: keys no app uses, in case one ever slips
    // past; the mouse gets nudged one pixel and back (Windows drops mouse input that does nothing).
    //
    // An Observer hook, installed before LoafLock's, sits behind it: what it sees is what got
    // past LoafLock, on its way to the apps.
    static partial class TestMain
    {
        const ushort F23 = 0x86, F24 = 0x87;

        static void LiveHook()
        {
            if (IntPtr.Size != 8) throw new SkipException("needs a 64-bit run");
            var gate = new KeyGate();
            var changes = new List<bool>();
            using (var behind = new Observer())
            using (var hook = new KeyHook(gate, true))
            {
                hook.RehookMs = 150;
                hook.LockChanged += locked => { lock (changes) changes.Add(locked); };
                hook.Lock();
                WaitFor(() => hook.Installed && Changes(changes) == "L", "the hook didn't start");
                Check(!hook.MouseInstalled, "the mouse hook is in, though the mouse isn't to be locked");

                if (!Send(F24, false)) throw new SkipException("Windows won't take sent keys here (no desktop to type on?)");
                WaitFor(() => Volatile.Read(ref gate.Blocked) == 1, "a sent F24 wasn't blocked");
                Check((GetAsyncKeyState(F24) & 0x8000) == 0, "Windows saw the blocked F24 go down anyway");
                Send(F24, true);

                Thread.Sleep(500);  // a few renewals
                Check(hook.Rehooks >= 2, "the hook was renewed " + hook.Rehooks + " times in half a second, expected 2 or more");
                Send(F24, false);
                Send(F24, true);
                WaitFor(() => Volatile.Read(ref gate.Blocked) == 2, "F24 got through after the hook was renewed");
                Check(behind.Downs(F24) == 0, "an F24 press got past LoafLock");

                // no renewals now, and long enough after the last one that a key is a new press:
                // its release goes with it (YouTube pauses on a lone Space release)
                hook.RehookMs = 60000;
                Thread.Sleep(gate.HeldGraceMs + 200);
                int ups = behind.Ups(F24);
                Send(F24, false);
                Send(F24, true);
                WaitFor(() => Volatile.Read(ref gate.Blocked) == 3, "F24 wasn't blocked");
                Thread.Sleep(100);
                Check(behind.Downs(F24) == 0 && behind.Ups(F24) == ups, "the release of a dropped F24 press got past LoafLock");

                Send(F23, false);  // held across the unlock
                WaitFor(() => Volatile.Read(ref gate.Blocked) == 4, "a sent F23 wasn't blocked");
                hook.Unlock();
                WaitFor(() => Changes(changes) == "LU", "no unlock was reported");
                Thread.Sleep(100);
                Check(hook.Installed, "the hook went while F23 was still held");
                Send(F23, true);
                WaitFor(() => !hook.Installed, "the hook stayed after the last key was let go");
                Thread.Sleep(100);
                Check(behind.Downs(F23) == 0 && behind.Ups(F23) == 0, "F23's press or its release got past LoafLock");
                Send(F24, false);
                Send(F24, true);
                WaitFor(() => behind.Downs(F24) == 1 && behind.Ups(F24) == ups + 1, "F24 didn't get through after unlocking");
            }
        }

        static void LiveMouseHook()
        {
            if (IntPtr.Size != 8) throw new SkipException("needs a 64-bit run");
            var gate = new KeyGate();
            using (var behind = new Observer())
            using (var hook = new KeyHook(gate, true))
            {
                hook.MouseToo = true;
                hook.Lock();
                WaitFor(() => hook.Installed && hook.MouseInstalled, "the mouse hook didn't start");
                if (!Nudge()) throw new SkipException("Windows won't take sent mouse input here (no desktop?)");
                Thread.Sleep(150);
                Check(behind.Moves == 0, "a move got past the locked mouse");

                hook.FreeMouse();  // the PC was locked
                WaitFor(() => !hook.MouseInstalled, "the mouse hook stayed after letting go of the mouse");
                Check(hook.Installed, "letting go of the mouse let go of the keyboard too");
                Nudge();
                WaitFor(() => behind.Moves == 2, "a move didn't get through after letting go of the mouse");

                hook.Unlock();
                WaitFor(() => !hook.Installed, "the hook stayed after unlocking");
                hook.MouseToo = false;
                hook.Lock();
                WaitFor(() => hook.Installed, "the hook didn't start again");
                Thread.Sleep(50);
                Check(!hook.MouseInstalled, "the mouse was locked, though it's not to be any more");
                Nudge();
                WaitFor(() => behind.Moves == 4, "a move didn't get through with only the keyboard locked");
                hook.Unlock();
                WaitFor(() => !hook.Installed, "the hook stayed after unlocking");
            }
        }

        // A hook that drops nothing and counts the tagged keys and mouse moves that reach it.
        sealed class Observer : IDisposable
        {
            readonly int[] downs = new int[256], ups = new int[256];
            int moves;
            readonly KeyProc keys, mouse;
            readonly Thread thread;
            readonly ManualResetEvent ready = new ManualResetEvent(false);
            uint threadId;
            IntPtr keyHook, mouseHook;

            public Observer()
            {
                keys = KeySeen;
                mouse = MouseSeen;
                thread = new Thread(Run) { IsBackground = true, Name = "test observer" };
                thread.Start();
                ready.WaitOne();
                if (keyHook == IntPtr.Zero || mouseHook == IntPtr.Zero) throw new Exception("the observer hooks didn't start");
            }

            public int Downs(int vk) { return Volatile.Read(ref downs[vk]); }
            public int Ups(int vk) { return Volatile.Read(ref ups[vk]); }
            public int Moves { get { return Volatile.Read(ref moves); } }

            void Run()
            {
                MSG msg;
                PeekMessage(out msg, IntPtr.Zero, 0, 0, 0);
                threadId = GetCurrentThreadId();
                keyHook = SetWindowsHookEx(13, keys, GetModuleHandle(null), 0);
                mouseHook = SetWindowsHookEx(14, mouse, GetModuleHandle(null), 0);
                ready.Set();
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0) { }
                if (keyHook != IntPtr.Zero) UnhookWindowsHookEx(keyHook);
                if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
            }

            IntPtr KeySeen(int nCode, IntPtr wParam, IntPtr lParam)
            {
                if (nCode >= 0 && Marshal.ReadIntPtr(lParam, 16) == KeyHook.TestTag)
                {
                    int vk = Marshal.ReadInt32(lParam, 0) & 0xFF, message = wParam.ToInt32();
                    if (message == 0x100 || message == 0x104) Interlocked.Increment(ref downs[vk]);
                    else Interlocked.Increment(ref ups[vk]);
                }
                return CallNextHookEx(keyHook, nCode, wParam, lParam);
            }

            IntPtr MouseSeen(int nCode, IntPtr wParam, IntPtr lParam)
            {
                if (nCode >= 0 && Marshal.ReadIntPtr(lParam, 24) == KeyHook.TestTag && wParam.ToInt32() == 0x200)
                    Interlocked.Increment(ref moves);
                return CallNextHookEx(mouseHook, nCode, wParam, lParam);
            }

            public void Dispose()
            {
                PostThreadMessage(threadId, 0x12, IntPtr.Zero, IntPtr.Zero);  // WM_QUIT
                thread.Join(2000);
            }

            delegate IntPtr KeyProc(int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll")]
            static extern IntPtr SetWindowsHookEx(int idHook, KeyProc fn, IntPtr module, uint threadId);
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

            [StructLayout(LayoutKind.Sequential)]
            struct MSG
            {
                public IntPtr hwnd;
                public uint message;
                public IntPtr wParam, lParam;
                public uint time;
                public int x, y;
            }
        }

        static string Changes(List<bool> changes)
        {
            lock (changes)
            {
                string text = "";
                foreach (bool locked in changes) text += locked ? "L" : "U";
                return text;
            }
        }

        static void WaitFor(Func<bool> done, string what)
        {
            var waited = Stopwatch.StartNew();
            while (!done())
            {
                if (waited.ElapsedMilliseconds > 3000) throw new Exception(what);
                Thread.Sleep(10);
            }
        }

        static bool Send(ushort vk, bool release)
        {
            var input = new INPUT { type = 1, wVk = vk, dwFlags = release ? 2u : 0u, dwExtraInfo = KeyHook.TestTag };
            return SendInput(1, ref input, Marshal.SizeOf(typeof(INPUT))) == 1;
        }

        // the pointer one pixel right and back: if it ever slipped past, nothing would notice.
        // (A move of nothing never reaches a hook: Windows drops it.)
        static bool Nudge()
        {
            var input = new MOUSE_INPUT { type = 0, dx = 1, dwFlags = 0x0001, dwExtraInfo = KeyHook.TestTag };  // MOUSEEVENTF_MOVE
            bool sent = SendInput(1, ref input, Marshal.SizeOf(typeof(MOUSE_INPUT))) == 1;
            input.dx = -1;
            return SendInput(1, ref input, Marshal.SizeOf(typeof(MOUSE_INPUT))) == 1 && sent;
        }

        // INPUT with a KEYBDINPUT inside, as 64-bit Windows lays it out: 40 bytes, or SendInput
        // quietly does nothing
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        struct INPUT
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public ushort wVk;
            [FieldOffset(10)] public ushort wScan;
            [FieldOffset(12)] public uint dwFlags;
            [FieldOffset(16)] public uint time;
            [FieldOffset(24)] public IntPtr dwExtraInfo;
        }

        // INPUT with a MOUSEINPUT inside, 64-bit: 40 bytes too
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        struct MOUSE_INPUT
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public int dx;
            [FieldOffset(12)] public int dy;
            [FieldOffset(16)] public uint mouseData;
            [FieldOffset(20)] public uint dwFlags;
            [FieldOffset(24)] public uint time;
            [FieldOffset(32)] public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll")]
        static extern uint SendInput(uint count, ref INPUT input, int size);
        [DllImport("user32.dll")]
        static extern uint SendInput(uint count, ref MOUSE_INPUT input, int size);
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vk);
    }
}
