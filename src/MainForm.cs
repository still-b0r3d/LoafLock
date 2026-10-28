using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LoafLock
{
    // The LoafLock window, from a left click on Rukus or "Open LoafLock". Kept small: Rukus, the
    // Lock / Unlock button, the paws blocked, the unlock word and the version. Everything else
    // lives in the menu bar: LoafLock, Options (its own window) and Help. Closing it leaves
    // LoafLock in the taskbar corner.
    class MainForm : Form
    {
        public const string MakerUrl = "https://b0r3d.org";

        const int ColumnWidth = 340;  // the column everything lines up in

        readonly Locker locker;
        readonly PictureBox rukus;
        readonly Image unlockedPicture, lockedPicture;
        readonly Label state, counts, word, version;
        readonly LinkLabel change;
        readonly Button lockButton;
        readonly ToolStripMenuItem lockItem, wordItem;
        bool optionsOpen;

        public MainForm(Locker locker)
        {
            this.locker = locker;
            SuspendLayout();
            Text = "LoafLock";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            try { Icon = AppIcons.Load("loaflock.ico", SystemInformation.IconSize); } catch { }
            unlockedPicture = Picture("loaflock.ico");
            lockedPicture = Picture("loaflock-locked.ico");

            // ---- the menu bar ----
            var menu = new MenuStrip();
            var app = new ToolStripMenuItem("&LoafLock");
            lockItem = Item(app, "&Lock keyboard", delegate { LockClicked(); });
            wordItem = Item(app, "&Change unlock word…", delegate { locker.AskForWord(this); });
            app.DropDownItems.Add(new ToolStripSeparator());
            Item(app, "Close this &window", delegate { Close(); });
            Item(app, "&Quit LoafLock", delegate { Log.Write("quit from the window"); Application.ExitThread(); });

            // opens the Options window straight away: it has room for whatever comes next
            var options = new ToolStripMenuItem("&Options", null, delegate { ShowOptions(); });

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

            version = new Label { Text = "v" + Program.Version + " · Build: " + BuildInfo.Commit, AutoSize = true, Tag = Theme.DimTag, Margin = new Padding(0, 14, 0, 0) };
            body.Controls.Add(version);

            Controls.Add(body);
            Controls.Add(menu);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();
            body.Top = menu.Bottom;  // under the menu bar

            Restyle();
            locker.Changed += Fill;
            Theme.Changed += Restyle;
            Disposed += delegate { locker.Changed -= Fill; Theme.Changed -= Restyle; };  // closed, or just disposed (the tests)
        }

        // light or dark, then everything that's coloured by what it says
        void Restyle()
        {
            Theme.Apply(this);
            Fill();
        }

        // the screenshot tool shows it off screen without taking focus
        internal bool Quiet = false;
        protected override bool ShowWithoutActivation { get { return Quiet; } }

        // what the tests look at
        internal string StateText { get { return state.Text; } }
        internal string ButtonText { get { return lockButton.Text; } }
        internal string CountsText { get { return counts.Text; } }
        internal string WordText { get { return word.Text; } }
        internal string VersionLine { get { return version.Text; } }

        internal void LockClicked()
        {
            locker.LockOrUnlock(this, "window");
        }

        // the Options window, over this one (from the menu bar, or Rukus's "Options…")
        internal void ShowOptions()
        {
            if (optionsOpen) return;
            optionsOpen = true;
            try
            {
                using (var options = new OptionsForm(locker))
                {
                    options.ShowDialog(this);
                    if (options.Forgot) Application.ExitThread();  // "Remove my settings": LoafLock goes too
                }
            }
            finally { optionsOpen = false; }
        }

        void Fill()
        {
            bool locked = locker.IsLocked;
            Settings s = locker.Settings;
            rukus.Image = locked ? lockedPicture : unlockedPicture;
            state.Text = !locked ? "Keyboard unlocked."
                : locker.MouseLocked ? "Keyboard and mouse locked. Type your unlock word."
                : "Keyboard locked. Type your unlock word, or click Unlock.";
            state.ForeColor = locked ? Theme.Gold : Theme.Dim;
            lockButton.Text = locked ? "Unlock keyboard" : "Lock keyboard";
            lockItem.Text = locked ? "&Unlock keyboard" : "&Lock keyboard";
            counts.Text = s.PawsBlocked + ": " + locker.Today.ToString("N0") + " today · " + locker.Total.ToString("N0") + " all time";
            word.Text = s.HasWord ? s.UnlockWord : "none yet";
            change.Text = s.HasWord ? "Change…" : "Pick one…";
            change.Enabled = wordItem.Enabled = !locked;
        }

        void Stuck()
        {
            MessageBox.Show(this,
                "If the keyboard stays locked:\n\n" +
                "•  Type your unlock word" + (locker.Settings.HasWord ? " (" + locker.Settings.UnlockWord + ")" : "") + ".\n" +
                "•  Or click Unlock keyboard, here or in Rukus's menu in the taskbar corner.\n" +
                "•  Mouse locked too? Press Ctrl+Alt+Del and pick Lock, then sign back in: the mouse works again, so you can click Unlock.\n" +
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
                "Locks the keyboard so a cat lying on it can't type. The mouse keeps working, unless you lock it too (in Options). It never connects to the internet and never keeps what you type.\n\n" +
                "Made by b0r3d.org.",
                "About LoafLock", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
