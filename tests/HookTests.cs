using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace LoafLock.Tests
{
    // The real hook on its real thread, fed keys this test sends itself. They carry
    // KeyHook.TestTag, and in test mode only tagged keys are judged, so the keyboard in use is
    // never locked. F24 and F23: keys no app uses, in case one ever slips past.
    static partial class TestMain
    {
        const ushort F23 = 0x86, F24 = 0x87;

        static void LiveHook()
        {
            if (IntPtr.Size != 8) throw new SkipException("needs a 64-bit run");
            var gate = new KeyGate();
            var changes = new List<bool>();
            using (var hook = new KeyHook(gate, true))
            {
                hook.RehookMs = 150;
                hook.LockChanged += locked => { lock (changes) changes.Add(locked); };
                hook.Lock();
                WaitFor(() => hook.Installed && Changes(changes) == "L", "the hook didn't start");

                if (!Send(F24, false)) throw new SkipException("Windows won't take sent keys here (no desktop to type on?)");
                WaitFor(() => Volatile.Read(ref gate.Blocked) == 1, "a sent F24 wasn't blocked");
                Check((GetAsyncKeyState(F24) & 0x8000) == 0, "Windows saw the blocked F24 go down anyway");
                Send(F24, true);

                Thread.Sleep(500);  // a few renewals
                Check(hook.Rehooks >= 2, "the hook was renewed " + hook.Rehooks + " times in half a second, expected 2 or more");
                Send(F24, false);
                Send(F24, true);
                WaitFor(() => Volatile.Read(ref gate.Blocked) == 2, "F24 got through after the hook was renewed");

                Send(F23, false);  // held across the unlock
                WaitFor(() => Volatile.Read(ref gate.Blocked) == 3, "a sent F23 wasn't blocked");
                hook.Unlock();
                WaitFor(() => Changes(changes) == "LU", "no unlock was reported");
                Thread.Sleep(100);
                Check(hook.Installed, "the hook went while F23 was still held");
                Send(F23, true);
                WaitFor(() => !hook.Installed, "the hook stayed after the last key was let go");
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

        [DllImport("user32.dll")]
        static extern uint SendInput(uint count, ref INPUT input, int size);
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vk);
    }
}
