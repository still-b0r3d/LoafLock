using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LoafLock.Tests
{
    // The counts, the start-with-Windows entry, the pop-up and the window. Windows stay off
    // screen or hidden and never take focus from what's in use; hooks are in test mode.
    static partial class TestMain
    {
        static void CountsByDay()
        {
            var s = new Settings();
            var monday = new DateTime(2026, 10, 26, 10, 0, 0);
            s.AddBlocked(3, monday);
            s.AddBlocked(2, monday.AddHours(13));
            Check(s.TodayCount(monday) == 5 && s.BlockedTotal == 5, "Monday's count is " + s.TodayCount(monday) + ", all time " + s.BlockedTotal + ", expected 5 and 5");
            DateTime tuesday = monday.AddDays(1);
            Check(s.TodayCount(tuesday) == 0, "Monday's paws counted for Tuesday");
            s.AddBlocked(1, tuesday);
            Check(s.TodayCount(tuesday) == 1 && s.BlockedTotal == 6, "Tuesday's count is " + s.TodayCount(tuesday) + ", all time " + s.BlockedTotal + ", expected 1 and 6");

            s.Popup = false;
            s.Save();
            Settings back = Settings.Load();
            Check(!back.Popup && back.BlockedTotal == 6 && back.TodayCount(tuesday) == 1, "the counts or the pop-up setting didn't come back");
            File.WriteAllText(Settings.FilePath, "blocked_total=-4\r\nblocked_today=lots\r\nblocked_day=yesterday\r\n");
            back = Settings.Load();
            Check(back.BlockedTotal == 0 && back.BlockedToday == 0 && back.BlockedDay == "", "nonsense counts were read");
        }

        static void StartupEntry()
        {
            string real = StartWithWindows.RunKeyPath;
            StartWithWindows.RunKeyPath = @"Software\LoafLockTests-" + Guid.NewGuid().ToString("N");
            try
            {
                Check(!StartWithWindows.IsOn, "on before it was set");
                StartWithWindows.Set(true, @"C:\Some Folder\LoafLock.exe");
                Check(StartWithWindows.IsOn, "not on after turning it on");
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartWithWindows.RunKeyPath))
                    Check((string)key.GetValue(StartWithWindows.ValueName) == "\"C:\\Some Folder\\LoafLock.exe\" --startup", "the entry doesn't start this exe quietly");
                StartWithWindows.Set(false, @"C:\Some Folder\LoafLock.exe");
                Check(!StartWithWindows.IsOn, "still on after turning it off");
                StartWithWindows.Set(false, @"C:\Some Folder\LoafLock.exe");  // off twice is fine
            }
            finally
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(StartWithWindows.RunKeyPath, false); } catch { }
                StartWithWindows.RunKeyPath = real;
            }
        }

        static void PopupNeverTakesFocus()
        {
            Popup.Place = size => new Point(-32000, -32000);  // off every screen
            using (var popup = new Popup { HoldMs = 300 })
            {
                IntPtr before = GetForegroundWindow();
                popup.Nudge("Type your unlock word to get it back.\nPaws blocked today: 1");
                Pump(() => popup.Opacity >= 0.9, "the note didn't fade in");
                Check(popup.Visible, "the note isn't showing");
                Check(GetForegroundWindow() == before, "the note took focus");
                int style = GetWindowLong(popup.Handle, GWL_EXSTYLE);
                Check((style & WS_EX_NOACTIVATE) != 0 && (style & WS_EX_TOPMOST) != 0, "the note can take focus, or isn't on top");
                Check((style & WS_EX_TRANSPARENT) != 0 && (style & WS_EX_LAYERED) != 0, "clicks don't go through the note");
                Pump(() => !popup.Visible, "the note didn't fade out");
            }
        }

        static void WindowFollowsTheLock()
        {
            var settings = new Settings { UnlockWord = "rukus" };
            // test mode: the hook judges only this test's own keys, never the keyboard in use
            var hook = new KeyHook(new KeyGate(), true);
            using (var locker = new Locker(settings, hook))
            using (var window = new MainForm(locker))
            {
                Check(window.StateText.Contains("unlocked") && window.ButtonText == "Lock keyboard", "the window doesn't show the keyboard unlocked");
                Check(window.WordText == "rukus", "the window doesn't show the unlock word");
                int windowThread = Thread.CurrentThread.ManagedThreadId, otherThread = 0;
                locker.Changed += delegate { if (Thread.CurrentThread.ManagedThreadId != windowThread) otherThread = Thread.CurrentThread.ManagedThreadId; };
                window.LockClicked();
                Pump(() => locker.IsLocked, "the window's Lock button didn't lock");
                Check(otherThread == 0, "the lock was reported on another thread (" + otherThread + "), not the window's");
                Check(window.ButtonText == "Unlock keyboard" && window.StateText.StartsWith("Keyboard locked"), "the window doesn't show the keyboard locked");
                if (IntPtr.Size == 8 && Send(F24, false))
                {
                    Send(F24, true);
                    Pump(() => locker.Today == 1, "a blocked key didn't reach the counts (hook installed: " + hook.Installed +
                        ", blocked by the hook: " + hook.Blocked + ", counted today: " + locker.Today + ", locked: " + locker.IsLocked + ")");
                    Check(window.CountsText.Contains("1 today"), "the window's count didn't move: " + window.CountsText);
                }
                window.LockClicked();
                Pump(() => !locker.IsLocked, "the window's Unlock button didn't unlock");
                Check(window.ButtonText == "Lock keyboard", "the button didn't turn back into Lock");
                Check(File.Exists(Settings.FilePath) && File.ReadAllText(Settings.FilePath).Contains("blocked_total="), "the counts weren't saved on unlocking");
            }
        }

        static void RemoveSettingsForgetsAll()
        {
            string real = StartWithWindows.RunKeyPath;
            StartWithWindows.RunKeyPath = @"Software\LoafLockTests-" + Guid.NewGuid().ToString("N");
            try
            {
                var settings = new Settings { UnlockWord = "rukus" };
                settings.Save();
                Log.Write("something for the log");
                StartWithWindows.Set(true, @"C:\Some Folder\LoafLock.exe");
                using (var locker = new Locker(settings, new KeyHook(new KeyGate(), true)))
                    locker.ForgetEverything();  // and closing it after saves nothing
                Check(!Directory.Exists(Settings.Dir), "the settings folder is still there");
                Check(!StartWithWindows.IsOn, "the start-with-Windows entry is still there");
            }
            finally
            {
                Log.Off = false;
                Directory.CreateDirectory(Settings.Dir);
                try { Registry.CurrentUser.DeleteSubKeyTree(StartWithWindows.RunKeyPath, false); } catch { }
                StartWithWindows.RunKeyPath = real;
            }
        }

        // lets the window thread run (posted calls, timers) until done, or 3 seconds
        static void Pump(Func<bool> done, string what)
        {
            var waited = Stopwatch.StartNew();
            while (!done())
            {
                if (waited.ElapsedMilliseconds > 3000) throw new Exception(what);
                Application.DoEvents();
                Thread.Sleep(10);
            }
        }

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hwnd, int index);
    }
}
