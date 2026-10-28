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
        int counted;  // the hook's count when last picked up
        bool asking, forgotten;

        public bool IsLocked { get; private set; }

        public event Action Changed;         // locked or unlocked, the word, or the counts
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
            // the hook gets installed afresh when the PC wakes up or someone signs back in
            SystemEvents.PowerModeChanged += PowerModeChanged;
            SystemEvents.SessionSwitch += SessionSwitched;
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
            if (IsLocked)
            {
                Log.Write("unlock from the " + from);
                hook.Unlock();
                return;
            }
            if (!Settings.HasWord && !AskForWord(owner)) return;
            hook.Lock();
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
            Log.Write(locked ? "locked" : "unlocked");
            if (locked) poll.Start();
            else
            {
                CountBlocked(true);
                poll.Stop();
                Save();
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
            if (e.Mode == PowerModes.Resume) hook.Rehook();
        }

        void SessionSwitched(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect) hook.Rehook();
        }

        public void Dispose()
        {
            SystemEvents.PowerModeChanged -= PowerModeChanged;
            SystemEvents.SessionSwitch -= SessionSwitched;
            poll.Stop();
            poll.Dispose();
            CountBlocked(false);
            Save();
            hook.Dispose();
            marshal.Dispose();
        }
    }
}
