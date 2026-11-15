using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LoafLock
{
    static class AppIcons
    {
        // one of the icons built into the exe, at the size given
        public static Icon Load(string name, Size size)
        {
            using (Stream res = Assembly.GetExecutingAssembly().GetManifestResourceStream("LoafLock." + name))
                return res == null ? SystemIcons.Application : new Icon(res, size);
        }
    }

    // Ruckus in the taskbar corner, with a gold padlock while the keyboard is locked. A left
    // click opens the LoafLock window; a right click has Lock / Unlock and the paws blocked.
    sealed class Tray : IDisposable
    {
        readonly Locker locker;
        readonly NotifyIcon icon = new NotifyIcon();
        readonly Icon unlocked = AppIcons.Load("loaflock.ico", SystemInformation.SmallIconSize);
        readonly Icon locked = AppIcons.Load("loaflock-locked.ico", SystemInformation.SmallIconSize);
        readonly ContextMenuStrip menu = new ContextMenuStrip { ShowImageMargin = false };
        readonly ToolStripMenuItem lockItem, countItem;
        readonly Popup popup = new Popup();
        readonly EventWaitHandle showRequests = Instance.Requests(Instance.Ask.Show);
        readonly EventWaitHandle lockRequests = Instance.Requests(Instance.Ask.Lock);
        readonly EventWaitHandle unlockRequests = Instance.Requests(Instance.Ask.Unlock);
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer { Interval = 500 };
        MainForm window;
        bool? shownLocked, shownMouse;

        // quiet: started with Windows or from the command line, so no window (see
        // OpensWindowAtStart); lockNow: started by "LoafLock.exe lock"
        public Tray(Settings settings, bool quiet, bool lockNow)
        {
            Theme.Set(settings.Look);
            locker = new Locker(settings, new KeyHook(new KeyGate(), false));

            menu.Items.Add(new ToolStripMenuItem(Program.VersionText) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            var open = new ToolStripMenuItem("Open LoafLock", null, delegate { OpenWindow(); });
            open.Font = new Font(menu.Font, FontStyle.Bold);
            menu.Items.Add(open);
            lockItem = new ToolStripMenuItem("Lock keyboard", null, delegate { locker.LockOrUnlock(OpenWindowOrNull(), "tray menu"); });
            menu.Items.Add(lockItem);
            countItem = new ToolStripMenuItem("") { Enabled = false };
            menu.Items.Add(countItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Options…", null, delegate
            {
                OpenWindow();
                window.ShowOptions();
            });
            menu.Items.Add("Quit LoafLock", null, delegate { Log.Write("quit from the tray"); Application.ExitThread(); });
            icon.ContextMenuStrip = menu;
            icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenWindow(); };

            locker.Changed += Show;
            locker.LockedChanged += nowLocked =>
            {
                if (!settings.NoteOnLock) return;
                string[] note = LockNote(settings, nowLocked, nowLocked ? locker.LockedBy : locker.UnlockedBy, locker.MouseLocked, locker.Today);
                popup.Nudge(note[0], note[1], nowLocked);
            };
            locker.KeysBlocked += delegate
            {
                if (!settings.NoteOnKeys) return;
                if (locker.Loafing && settings.NoteOnLock) LoafNote();  // the loaf's note stays up while it goes on
                else popup.Nudge(locker.MouseLocked ? "Keyboard and mouse are locked" : "Keyboard's locked", KeyNoteText(settings, locker.MouseLocked, locker.Today), true);
            };
            // a long loaf: Ruckus nods off in the note (with the lock and unlock notes)
            locker.LongLoaf += delegate
            {
                if (settings.NoteOnLock) LoafNote();
            };
            locker.Failed += message => MessageBox.Show(message, "LoafLock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            // LoafLock.exe opened again: show this copy's window; or "LoafLock.exe lock" / "unlock"
            tick.Tick += delegate
            {
                if (showRequests.WaitOne(0)) OpenWindow();
                if (lockRequests.WaitOne(0)) locker.Lock(OpenWindowOrNull(), "command line");
                if (unlockRequests.WaitOne(0)) locker.Unlock("command line");
            };
            tick.Start();
            Theme.Changed += Restyle;
            SystemEvents.UserPreferenceChanged += WindowsLookChanged;

            Restyle();
            Show();
            icon.Visible = true;
            bool opens = OpensWindowAtStart(quiet, settings.StartMinimized, settings.HasWord);
            if (opens || lockNow)
            {
                locker.Post(delegate  // once LoafLock is up and running
                {
                    if (opens) OpenWindow();
                    if (!settings.HasWord) locker.AskForWord(window);  // the first run
                    if (lockNow && settings.HasWord) locker.Lock(OpenWindowOrNull(), "command line");
                });
            }
        }

        // Windows switched between light and dark apps (or high contrast)
        void WindowsLookChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
                locker.Post(Theme.Update);
        }

        void Restyle()
        {
            Theme.Apply(menu);
        }

        // the note when cat detection locks: the cat's name goes in the line under it, which wraps,
        // since a long one wouldn't fit in the heading
        internal const string CatNoteTitle = "Cat on the keyboard?";

        internal static string CatNoteText(Settings s)
        {
            string name = s.CatName.Length > 0 ? s.CatName : "a cat";
            // with the hotkey to name as well, the note says it in fewer words, so it still fits
            return s.HotkeyUnlocksNow ? "Looks like " + name + ". " + HowBack(s, false) : "Looks like " + name + ", so the keyboard's locked. " + HowBack(s, false);
        }

        // how to get the keyboard (and the mouse: them) back, naming the hotkey when it unlocks too
        internal static string HowBack(Settings s, bool them)
        {
            return s.HotkeyUnlocksNow ? "Type your unlock word or press " + s.Hotkey + "." : "Type your unlock word to get " + (them ? "them" : "it") + " back.";
        }

        // the note on every blocked key (an option): it has the paw count to fit in as well, so it
        // keeps to the unlock word (a long name and a long hotkey together don't fit)
        internal static string KeyNoteText(Settings s, bool mouse, int today)
        {
            return "Type your unlock word to get " + (mouse ? "them" : "it") + " back.\n" + s.PawsBlocked + " today: " + today.ToString("N0");
        }

        // the note when the keyboard locks or unlocks: { heading, line }. by: what locked it, or
        // what unlocked it (Locker's LockedBy / UnlockedBy)
        internal static string[] LockNote(Settings s, bool locked, string by, bool mouse, int today)
        {
            string count = s.PawsBlocked + " today: " + today.ToString("N0");
            if (!locked && by == "itself") return new[] { "Keyboard unlocked by itself", "No paws for " + Settings.WaitText(s.AutoUnlockSeconds) + ".\n" + count };
            if (!locked) return new[] { "Keyboard unlocked", count };
            if (by == "idle") return new[] { "Keyboard locked by itself", "Nobody used the PC for " + s.IdleMinutes + " minutes. " + HowBack(s, false) };
            if (by == "cat") return new[] { CatNoteTitle, CatNoteText(s) };
            if (mouse) return new[] { "Keyboard and mouse locked", HowBack(s, true) };
            return new[] { "Keyboard locked", HowBack(s, false) };
        }

        void LoafNote()
        {
            Settings s = locker.Settings;
            popup.Nudge((s.CatName.Length > 0 ? s.CatName : "A cat") + " is loafing", "Nothing gets typed: the keyboard's locked.", true, true);
        }

        // the window opens when LoafLock starts, unless it started with Windows or minimized; the
        // first run always opens it, to ask for the unlock word
        public static bool OpensWindowAtStart(bool quiet, bool startMinimized, bool hasWord)
        {
            return !hasWord || (!quiet && !startMinimized);
        }

        void Show()
        {
            bool isLocked = locker.IsLocked, mouse = locker.MouseLocked;
            if (shownLocked != isLocked || shownMouse != mouse)
            {
                shownLocked = isLocked;
                shownMouse = mouse;
                icon.Icon = isLocked ? locked : unlocked;
                lockItem.Text = isLocked ? "Unlock keyboard" + (mouse ? " and mouse" : "") : "Lock keyboard";
            }
            string tip = isLocked ? "LoafLock: keyboard" + (mouse ? " and mouse" : "") + " locked · " + locker.Today.ToString("N0") + " paws blocked today" : "LoafLock: keyboard unlocked";
            if (tip.Length > 63) tip = tip.Substring(0, 63);  // the most Windows takes
            if (icon.Text != tip) icon.Text = tip;
            countItem.Text = locker.Settings.PawsBlocked + ": " + locker.Today.ToString("N0") + " today · " + locker.Total.ToString("N0") + " all time";
        }

        // the window, when it's up, for a word prompt to go over
        IWin32Window OpenWindowOrNull()
        {
            return window != null && !window.IsDisposed && window.Visible ? window : null;
        }

        void OpenWindow()
        {
            if (window == null || window.IsDisposed) window = new MainForm(locker);
            window.Show();
            if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
            window.Activate();
        }

        public void Dispose()
        {
            tick.Stop();
            tick.Dispose();
            showRequests.Dispose();
            lockRequests.Dispose();
            unlockRequests.Dispose();
            Theme.Changed -= Restyle;
            SystemEvents.UserPreferenceChanged -= WindowsLookChanged;
            locker.Changed -= Show;
            if (window != null) window.Dispose();
            popup.Dispose();
            locker.Dispose();
            icon.Visible = false;
            icon.Dispose();
            unlocked.Dispose();
            locked.Dispose();
        }
    }
}
