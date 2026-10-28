using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

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
        readonly ToolStripMenuItem lockItem, countItem;
        readonly Popup popup = new Popup();
        readonly EventWaitHandle showRequests = Instance.ShowRequests();
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer { Interval = 500 };
        MainForm window;
        bool? shownLocked;

        // quiet: started with Windows, so no window unless there's no unlock word yet
        public Tray(Settings settings, bool quiet)
        {
            var menu = new ContextMenuStrip { ShowImageMargin = false };
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
            menu.Items.Add("Quit LoafLock", null, delegate { Log.Write("quit from the tray"); Application.ExitThread(); });
            icon.ContextMenuStrip = menu;
            icon.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenWindow(); };

            locker.Changed += Show;
            locker.KeysBlocked += delegate
            {
                if (settings.Popup) popup.Nudge("Type your unlock word to get it back.\nPaws blocked today: " + locker.Today.ToString("N0"));
            };
            locker.Failed += message => MessageBox.Show(message, "LoafLock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            // LoafLock.exe opened again: show this copy's window
            tick.Tick += delegate { if (showRequests.WaitOne(0)) OpenWindow(); };
            tick.Start();

            Show();
            icon.Visible = true;
            if (!quiet || !settings.HasWord)
            {
                locker.Post(delegate  // once LoafLock is up and running
                {
                    OpenWindow();
                    if (!settings.HasWord) locker.AskForWord(window);  // the first run
                });
            }
        }

        void Show()
        {
            bool isLocked = locker.IsLocked;
            if (shownLocked != isLocked)
            {
                shownLocked = isLocked;
                icon.Icon = isLocked ? locked : unlocked;
                lockItem.Text = isLocked ? "Unlock keyboard" : "Lock keyboard";
            }
            string tip = isLocked ? "LoafLock: keyboard locked · " + locker.Today.ToString("N0") + " paws blocked today" : "LoafLock: keyboard unlocked";
            if (tip.Length > 63) tip = tip.Substring(0, 63);  // the most Windows takes
            if (icon.Text != tip) icon.Text = tip;
            countItem.Text = "Paws blocked: " + locker.Today.ToString("N0") + " today · " + locker.Total.ToString("N0") + " all time";
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
