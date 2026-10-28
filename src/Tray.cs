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

    // Rukus in the taskbar corner, with a gold padlock while the keyboard is locked. Right-click
    // for Lock / Unlock and the unlock word.
    sealed class Tray : IDisposable
    {
        readonly Settings settings;
        readonly KeyHook hook = new KeyHook(new KeyGate(), false);
        readonly NotifyIcon icon = new NotifyIcon();
        readonly Icon unlocked = AppIcons.Load("loaflock.ico", SystemInformation.SmallIconSize);
        readonly Icon locked = AppIcons.Load("loaflock-locked.ico", SystemInformation.SmallIconSize);
        readonly ToolStripMenuItem lockItem, wordItem;
        readonly SynchronizationContext ui;
        bool isLocked, asking;

        public Tray(Settings settings)
        {
            this.settings = settings;
            var menu = new ContextMenuStrip { ShowImageMargin = false };
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();  // the menu, a control, sets it up
            menu.Items.Add(new ToolStripMenuItem(Program.VersionText) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            lockItem = new ToolStripMenuItem("Lock keyboard", null, delegate { LockOrUnlock(); });
            lockItem.Font = new Font(menu.Font, FontStyle.Bold);
            menu.Items.Add(lockItem);
            wordItem = new ToolStripMenuItem("Change unlock word…", null, delegate { AskForWord(); });
            menu.Items.Add(wordItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit LoafLock", null, delegate { Log.Write("quit from the tray"); Application.ExitThread(); });
            icon.ContextMenuStrip = menu;

            hook.Word = settings.UnlockWord;
            hook.LockChanged += nowLocked => ui.Post(delegate { Show(nowLocked); }, null);
            hook.Failed += message => ui.Post(delegate { MessageBox.Show(message, "LoafLock", MessageBoxButtons.OK, MessageBoxIcon.Warning); }, null);
            hook.Note += line => ui.Post(delegate { Log.Write(line); }, null);  // the hook's thread never writes files
            // the hook gets installed afresh when the PC wakes up or someone signs back in
            SystemEvents.PowerModeChanged += PowerModeChanged;
            SystemEvents.SessionSwitch += SessionSwitched;

            Show(false);
            icon.Visible = true;
            if (!settings.HasWord) ui.Post(delegate { AskForWord(); }, null);  // first run, once the app is up
        }

        void Show(bool nowLocked)
        {
            if (isLocked != nowLocked) Log.Write(nowLocked ? "locked" : "unlocked");
            isLocked = nowLocked;
            icon.Icon = nowLocked ? locked : unlocked;
            icon.Text = nowLocked ? "LoafLock: keyboard locked" : "LoafLock: keyboard unlocked";
            lockItem.Text = nowLocked ? "Unlock keyboard" : "Lock keyboard";
            wordItem.Enabled = !nowLocked;  // nobody can type a new one while locked anyway
        }

        void LockOrUnlock()
        {
            if (isLocked)
            {
                Log.Write("unlock from the tray menu");
                hook.Unlock();
                return;
            }
            if (!settings.HasWord && !AskForWord()) return;
            hook.Lock();
        }

        // first run, and "Change unlock word…": true once there's a word
        bool AskForWord()
        {
            if (asking) return false;
            asking = true;
            try
            {
                using (var form = new WordForm(settings.UnlockWord))
                {
                    if (form.ShowDialog() != DialogResult.OK) return settings.HasWord;
                    settings.UnlockWord = form.Word;
                }
                try { settings.Save(); }
                catch (Exception e) { Log.Write("couldn't save settings.ini: " + e.Message); }
                hook.Word = settings.UnlockWord;
                Log.Write("unlock word set");  // never the word itself
                return true;
            }
            finally { asking = false; }
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
            hook.Dispose();
            icon.Visible = false;
            icon.Dispose();
            unlocked.Dispose();
            locked.Dispose();
        }
    }
}
