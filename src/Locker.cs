using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LoafLock
{
    // LoafLock's state, on the window thread: the hook, the unlock word and the paws-blocked
    // counts. The tray menu, the window and the pop-up all show it and drive it.
    sealed class Locker : IDisposable
    {
        public readonly Settings Settings;
        readonly KeyHook hook;
        // made on the window thread, so calls from the hook's thread can be handed over to it.
        // (Not SynchronizationContext.Current: depending on what ran before, that can be the
        // plain one, which runs the call on some other thread.)
        readonly Control marshal = new Control();
        // while locked, picks up the keys the hook blocked (its thread only counts them)
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 150 };
        readonly HotkeyListener hotkey = new HotkeyListener();
        // "Lock by itself": looks every few seconds at how long nobody's been at the PC
        readonly System.Windows.Forms.Timer idleCheck = new System.Windows.Forms.Timer { Interval = 5000 };
        int idleFrom = Environment.TickCount;  // idle time counts from here at the earliest
        int counted;  // the hook's count when last picked up
        bool asking, forgotten;

        public bool IsLocked { get; private set; }
        public bool MouseLocked { get; private set; }  // locked, with the mouse too
        public string LockedBy { get; private set; }   // what asked for the lock in force: "window", "idle", ...

        // why the hotkey isn't working (another app has it), or null
        public string HotkeyProblem { get; private set; }

        public event Action Changed;         // locked or unlocked, the word, the counts, or a setting
        public event Action<bool> LockedChanged;  // true: just locked; false: just unlocked
        public event Action KeysBlocked;     // keys were blocked just now
        public event Action<string> Failed;  // the keyboard couldn't be locked; says why

        public Locker(Settings settings, KeyHook hook)
        {
            Settings = settings;
            this.hook = hook;
            IntPtr made = marshal.Handle;  // its window, on this thread, now
            hook.Word = settings.UnlockWord;
            hook.LockChanged += locked => Post(delegate { SetLocked(locked); });
            hook.Failed += message => Post(delegate { if (Failed != null) Failed(message); });
            hook.Note += line => Post(delegate { Log.Write(line); });  // the hook's thread never writes files
            poll.Tick += delegate { CountBlocked(true); };
            hotkey.Pressed += delegate { Lock(null, "hotkey"); };
            ApplyHotkey();
            idleCheck.Tick += delegate
            {
                if (Settings.IdleLock && !IsLocked) CheckIdle(Idle.IdleMs(), Environment.TickCount - idleFrom, Idle.GameFullscreen());
            };
            idleCheck.Start();
            // the hook gets installed afresh when the PC wakes up or someone signs back in
            SystemEvents.PowerModeChanged += PowerModeChanged;
            SystemEvents.SessionSwitch += SessionSwitched;
        }

        // takes the hotkey from Windows, or gives it back, as the settings say. False (and
        // HotkeyProblem says why) when something else already has it.
        public bool ApplyHotkey()
        {
            HotkeyProblem = null;
            if (!Settings.HotkeyOn)
            {
                hotkey.Clear();
                return true;
            }
            if (hotkey.Set(Settings.Hotkey)) return true;
            HotkeyProblem = Settings.Hotkey + " is already taken, by another app or by Windows. Pick another one.";
            Log.Write("couldn't take the hotkey " + Settings.Hotkey + ": something else has it");
            return false;
        }

        public int Today { get { return Settings.TodayCount(DateTime.Now); } }
        public int Total { get { return Settings.BlockedTotal; } }

        // runs call on the window thread, once it gets to it; from any thread
        public void Post(MethodInvoker call)
        {
            try { marshal.BeginInvoke(call); }
            catch (InvalidOperationException) { }  // closing: the window thread has moved on
        }

        // from the tray menu or the window; owner: the window a word prompt goes over
        public void LockOrUnlock(IWin32Window owner, string from)
        {
            if (IsLocked) Unlock(from);
            else Lock(owner, from);
        }

        // from: where it was asked for, for the log ("window", "command line")
        public void Lock(IWin32Window owner, string from)
        {
            Lock(owner, from, true);
        }

        // mouse: the mouse locks too, if Options says so (an idle lock never locks it, so
        // whoever comes back can click Unlock)
        void Lock(IWin32Window owner, string from, bool mouse)
        {
            if (IsLocked) return;
            if (asking)
            {
                // the word is being picked: locking now would lock with the old one
                Log.Write("lock from the " + from + " left alone: the unlock word is being changed");
                return;
            }
            if (!Settings.HasWord && !AskForWord(owner)) return;
            Log.Write("lock from the " + from);
            LockedBy = from;
            hook.MouseToo = mouse && Settings.LockMouse;
            hook.VolumeThrough = Settings.VolumeThrough;
            hook.Lock();
        }

        // "Lock by itself", every few seconds: idleMs from Windows, sinceMs since idleFrom
        internal void CheckIdle(long idleMs, long sinceMs, bool game)
        {
            if (!Settings.IdleLock || IsLocked || asking || !Settings.HasWord) return;
            if (!Idle.Due(Settings.IdleMinutes, idleMs, sinceMs, game)) return;
            idleFrom = Environment.TickCount;  // if the lock fails, the next try is a while off
            Lock(null, "idle", false);
        }

        // idle time starts over: at an unlock (typing the unlock word doesn't count as using the
        // keyboard for Windows), a wake-up, a sign-in, and when the option is turned on
        public void RestartIdle()
        {
            idleFrom = Environment.TickCount;
        }

        // always goes to the hook, even if the lock it undoes hasn't been reported yet ("lock"
        // and "unlock" from the command line in quick succession): unlocking twice does no harm
        public void Unlock(string from)
        {
            Log.Write("unlock from the " + from);
            hook.Unlock();
        }

        // a setting changed (the cat's name, say): the window and the tray show it
        public void SettingsChanged()
        {
            Save();
            if (Changed != null) Changed();
        }

        // the first run, and "Change…": true once there's a word
        public bool AskForWord(IWin32Window owner)
        {
            if (asking || IsLocked) return Settings.HasWord;
            asking = true;
            try
            {
                using (var form = new WordForm(Settings.UnlockWord))
                {
                    if (form.ShowDialog(owner) != DialogResult.OK) return Settings.HasWord;
                    Settings.UnlockWord = form.Word;
                }
                hook.Word = Settings.UnlockWord;
                Save();
                Log.Write("unlock word set");  // never the word itself
                if (Changed != null) Changed();
                return true;
            }
            finally { asking = false; }
        }

        void SetLocked(bool locked)
        {
            if (IsLocked == locked) return;
            IsLocked = locked;
            MouseLocked = locked && hook.MouseInstalled;
            Log.Write(locked ? (MouseLocked ? "locked, the mouse too" : "locked") : "unlocked");
            if (locked) poll.Start();
            else
            {
                CountBlocked(true);
                poll.Stop();
                Save();
                RestartIdle();
            }
            if (LockedChanged != null) LockedChanged(locked);
            if (Changed != null) Changed();
        }

        // tell: let the window and the pop-up know (not while closing)
        void CountBlocked(bool tell)
        {
            int now = hook.Blocked;
            int fresh = now - counted;
            if (fresh <= 0) return;
            counted = now;
            Settings.AddBlocked(fresh, DateTime.Now);
            if (!tell) return;
            if (KeysBlocked != null) KeysBlocked();
            if (Changed != null) Changed();
        }

        public void Save()
        {
            if (forgotten) return;
            try { Settings.Save(); }
            catch (Exception e) { Log.Write("couldn't save settings.ini: " + e.Message); }
        }

        // "Remove my settings": the settings, the log and the sign-in entry go. LoafLock quits
        // right after, so nothing writes them again.
        public void ForgetEverything()
        {
            forgotten = true;
            Log.Off = true;
            try { StartWithWindows.Set(false, Application.ExecutablePath); } catch { }
            // only ever LoafLock's own folder
            if (Path.GetFileName(Settings.Dir.TrimEnd('\\')).StartsWith("LoafLock", StringComparison.OrdinalIgnoreCase))
            {
                try { if (Directory.Exists(Settings.Dir)) Directory.Delete(Settings.Dir, true); } catch { }
            }
        }

        void PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            hook.Rehook();
            Post(RestartIdle);
        }

        void SessionSwitched(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect)
            {
                hook.Rehook();
                Post(RestartIdle);
            }
            // the way out with the mouse locked too: Ctrl+Alt+Del, Lock, sign back in. The
            // mouse works again (and can click Unlock); the keyboard stays locked.
            if (e.Reason == SessionSwitchReason.SessionLock) Post(LetGoOfTheMouse);
        }

        internal void LetGoOfTheMouse()
        {
            if (!MouseLocked) return;
            hook.FreeMouse();
            MouseLocked = false;
            Log.Write("Windows was locked, so the mouse works again (the keyboard stays locked)");
            if (Changed != null) Changed();
        }

        public void Dispose()
        {
            SystemEvents.PowerModeChanged -= PowerModeChanged;
            SystemEvents.SessionSwitch -= SessionSwitched;
            poll.Stop();
            poll.Dispose();
            idleCheck.Stop();
            idleCheck.Dispose();
            hotkey.Dispose();
            CountBlocked(false);
            Save();
            hook.Dispose();
            marshal.Dispose();
        }
    }
}
