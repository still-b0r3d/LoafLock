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

    // Rukus in the taskbar corner, with a gold padlock while the keyboard is locked. A left
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
                if (!nowLocked) popup.Nudge("Keyboard unlocked", settings.PawsBlocked + " today: " + locker.Today.ToString("N0"), false);
                else if (locker.LockedBy == "idle")
                    popup.Nudge("Keyboard locked by itself", "Nobody used the keyboard or mouse for " + settings.IdleMinutes + " minutes. Type your unlock word to get it back.", true);
                else if (locker.MouseLocked) popup.Nudge("Keyboard and mouse locked", "Type your unlock word to get them back.", true);
                else popup.Nudge("Keyboard locked", "Type your unlock word to get it back.", true);
            };
            locker.KeysBlocked += delegate
            {
                if (settings.NoteOnKeys)
                    popup.Nudge(locker.MouseLocked ? "Keyboard and mouse are locked" : "Keyboard's locked",
                        "Type your unlock word to get " + (locker.MouseLocked ? "them" : "it") + " back.\n" + settings.PawsBlocked + " today: " + locker.Today.ToString("N0"), true);
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
