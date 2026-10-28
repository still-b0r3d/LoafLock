using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LoafLock
{
    // The LoafLock window, from a left click on Rukus or "Open LoafLock". Kept small: Rukus, the
    // Lock / Unlock button, the paws blocked and the unlock word. Everything else lives in the
    // menu bar (LoafLock, Options, Help). Closing it leaves LoafLock in the taskbar corner.
    class MainForm : Form
    {
        public const string MakerUrl = "https://b0r3d.org";

        const int ColumnWidth = 340;  // the column everything lines up in

        readonly Locker locker;
        readonly PictureBox rukus;
        readonly Image unlockedPicture, lockedPicture;
        readonly Label state, counts, word;
        readonly LinkLabel change;
        readonly Button lockButton;
        readonly ToolStripMenuItem lockItem, wordItem, noteOnLock, noteOnKeys, minimized, startup, remove;
        bool loading;

        public MainForm(Locker locker)
        {
            this.locker = locker;
            SuspendLayout();
            Text = "LoafLock";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = SystemColors.Window;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            try { Icon = AppIcons.Load("loaflock.ico", SystemInformation.IconSize); } catch { }
            unlockedPicture = Picture("loaflock.ico");
            lockedPicture = Picture("loaflock-locked.ico");

            // ---- the menu bar ----
            var menu = new MenuStrip { BackColor = SystemColors.Window };
            var app = new ToolStripMenuItem("&LoafLock");
            lockItem = Item(app, "&Lock keyboard", delegate { LockClicked(); });
            wordItem = Item(app, "&Change unlock word…", delegate { locker.AskForWord(this); });
            app.DropDownItems.Add(new ToolStripSeparator());
            Item(app, "Close this &window", delegate { Close(); });
            Item(app, "&Quit LoafLock", delegate { Log.Write("quit from the window"); Application.ExitThread(); });

            var options = new ToolStripMenuItem("&Options");
            noteOnLock = Toggle(options, "Show a &note when the keyboard locks or unlocks", on => { locker.Settings.NoteOnLock = on; locker.Save(); });
            noteOnKeys = Toggle(options, "Show a note on every &blocked key", on => { locker.Settings.NoteOnKeys = on; locker.Save(); });
            options.DropDownItems.Add(new ToolStripSeparator());
            minimized = Toggle(options, "Start &minimized (just Rukus in the taskbar corner)", on => { locker.Settings.StartMinimized = on; locker.Save(); });
            startup = Toggle(options, "Start LoafLock when you &sign in to Windows", StartupChanged);
            options.DropDownItems.Add(new ToolStripSeparator());
            remove = Item(options, "&Remove my settings…", delegate { RemoveSettings(); });

            var help = new ToolStripMenuItem("&Help");
            Item(help, "If the keyboard's &stuck…", delegate { Stuck(); });
            Item(help, "Open the &log", delegate { OpenLog(); });
            help.DropDownItems.Add(new ToolStripSeparator());
            Item(help, "&About LoafLock", delegate { About(); });
            Item(help, "Visit &b0r3d.org", delegate { Open(MakerUrl); });

            menu.Items.AddRange(new ToolStripItem[] { app, options, help });
            MainMenuStrip = menu;

            // ---- the window itself ----
            var body = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(24, 14, 24, 18)
            };

            // Rukus, the name, and where things stand
            var top = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 12) };
            rukus = new PictureBox { Size = new Size(64, 64), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 14, 0) };
            top.Controls.Add(rukus);
            var heading = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            heading.Controls.Add(new Label { Text = "LoafLock", AutoSize = true, Font = new Font("Segoe UI Semibold", 16F), Margin = new Padding(0) });
            state = new Label { AutoSize = true, MaximumSize = new Size(ColumnWidth - 80, 0), Margin = new Padding(2, 2, 0, 0) };
            heading.Controls.Add(state);
            top.Controls.Add(heading);
            body.Controls.Add(top);

            lockButton = new Button { Size = new Size(ColumnWidth, 40), Font = new Font(Font.FontFamily, 11F, FontStyle.Bold), UseVisualStyleBackColor = true, Margin = new Padding(0, 0, 0, 10) };
            lockButton.Click += delegate { LockClicked(); };
            body.Controls.Add(lockButton);

            counts = new Label { AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
            body.Controls.Add(counts);

            var wordRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            wordRow.Controls.Add(new Label { Text = "Unlock word:", AutoSize = true, Margin = new Padding(0, 0, 4, 0) });
            word = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 10, 0) };
            wordRow.Controls.Add(word);
            change = new LinkLabel { AutoSize = true, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0) };
            change.LinkClicked += delegate { locker.AskForWord(this); };
            wordRow.Controls.Add(change);
            body.Controls.Add(wordRow);

            Controls.Add(body);
            Controls.Add(menu);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();
            body.Top = menu.Bottom;  // under the menu bar

            try
            {
                loading = true;
                startup.Checked = StartWithWindows.IsOn;
            }
            catch (Exception e) { Log.Write("couldn't read the start-with-Windows entry: " + e.Message); }
            Fill();
            locker.Changed += Fill;
            FormClosed += delegate { locker.Changed -= Fill; };
        }

        // the screenshot tool shows it off screen without taking focus
        internal bool Quiet = false;
        protected override bool ShowWithoutActivation { get { return Quiet; } }

        // what the tests look at
        internal string StateText { get { return state.Text; } }
        internal string ButtonText { get { return lockButton.Text; } }
        internal string CountsText { get { return counts.Text; } }
        internal string WordText { get { return word.Text; } }

        internal void LockClicked()
        {
            locker.LockOrUnlock(this, "window");
        }

        void Fill()
        {
            loading = true;
            bool locked = locker.IsLocked;
            Settings s = locker.Settings;
            rukus.Image = locked ? lockedPicture : unlockedPicture;
            state.Text = locked ? "Keyboard locked. Type your unlock word, or click Unlock." : "Keyboard unlocked.";
            state.ForeColor = locked ? Color.FromArgb(160, 110, 0) : SystemColors.GrayText;
            lockButton.Text = locked ? "Unlock keyboard" : "Lock keyboard";
            lockItem.Text = locked ? "&Unlock keyboard" : "&Lock keyboard";
            counts.Text = "Paws blocked: " + locker.Today.ToString("N0") + " today · " + locker.Total.ToString("N0") + " all time";
            word.Text = s.HasWord ? s.UnlockWord : "none yet";
            change.Text = s.HasWord ? "Change…" : "Pick one…";
            change.Enabled = wordItem.Enabled = remove.Enabled = !locked;
            noteOnLock.Checked = s.NoteOnLock;
            noteOnKeys.Checked = s.NoteOnKeys;
            minimized.Checked = s.StartMinimized;
            loading = false;
        }

        void StartupChanged(bool on)
        {
            try
            {
                StartWithWindows.Set(on, Application.ExecutablePath);
                Log.Write(on ? "starts with Windows from now on" : "no longer starts with Windows");
            }
            catch (Exception e)
            {
                Log.Write("couldn't change the start-with-Windows entry: " + e.Message);
                loading = true;
                startup.Checked = !on;
                loading = false;
                MessageBox.Show(this, "LoafLock couldn't change that: " + e.Message, "LoafLock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void Stuck()
        {
            MessageBox.Show(this,
                "If the keyboard stays locked:\n\n" +
                "•  Type your unlock word" + (locker.Settings.HasWord ? " (" + locker.Settings.UnlockWord + ")" : "") + ".\n" +
                "•  Or click Unlock keyboard, here or in Rukus's menu in the taskbar corner.\n" +
                "•  Still stuck? Press Ctrl+Alt+Del, open Task Manager and end LoafLock. The keyboard comes back at once.",
                "LoafLock", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void OpenLog()
        {
            if (File.Exists(Log.FilePath)) Open(Log.FilePath);
            else MessageBox.Show(this, "Nothing in the log yet.", "LoafLock", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void About()
        {
            MessageBox.Show(this,
                Program.VersionText + "\n\n" +
                "Locks the keyboard so a cat lying on it can't type. The mouse keeps working. It never connects to the internet and never keeps what you type.\n\n" +
                "Made by b0r3d.org.",
                "About LoafLock", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void RemoveSettings()
        {
            if (locker.IsLocked) return;
            DialogResult answer = MessageBox.Show(this,
                "Remove LoafLock's settings, its log and its start-with-Windows entry, then quit?\n\n" +
                "Your unlock word and paw counts will be gone. LoafLock.exe stays where it is: delete it too to remove LoafLock completely.",
                "LoafLock", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            locker.ForgetEverything();
            Application.ExitThread();
        }

        static void Open(string target)
        {
            try { Process.Start(target); } catch { }
        }

        static Image Picture(string icon)
        {
            // 64 px at most: the .ico's bigger picture is PNG-packed, the kind .NET Framework
            // turns into static
            using (Icon i = AppIcons.Load(icon, new Size(64, 64))) return i.ToBitmap();
        }

        static ToolStripMenuItem Item(ToolStripMenuItem parent, string text, EventHandler click)
        {
            var item = new ToolStripMenuItem(text, null, click);
            parent.DropDownItems.Add(item);
            return item;
        }

        // a menu item with a tick, for an option
        ToolStripMenuItem Toggle(ToolStripMenuItem parent, string text, Action<bool> changed)
        {
            var item = new ToolStripMenuItem(text) { CheckOnClick = true };
            item.CheckedChanged += delegate { if (!loading) changed(item.Checked); };
            parent.DropDownItems.Add(item);
            return item;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                unlockedPicture.Dispose();
                lockedPicture.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
