using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace LoafLock
{
    // The LoafLock window, from a left click on Rukus or "Open LoafLock": lock and unlock, the
    // paws blocked, the unlock word and the options. Closing it leaves LoafLock in the taskbar
    // corner.
    class MainForm : Form
    {
        public const string MakerUrl = "https://b0r3d.org";

        const int ColumnWidth = 400;  // the column everything lines up in

        readonly Locker locker;
        readonly PictureBox rukus;
        readonly Image unlockedPicture, lockedPicture;
        readonly Label state, counts, word;
        readonly Button lockButton, change;
        readonly CheckBox popup, startup;
        readonly LinkLabel footer;
        readonly FlowLayoutPanel body;
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

            body = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(24, 18, 24, 14)
            };

            // Rukus, the name, and where things stand
            var top = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 12) };
            rukus = new PictureBox { Size = new Size(64, 64), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 14, 0) };
            top.Controls.Add(rukus);
            var heading = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            heading.Controls.Add(new Label { Text = "LoafLock", AutoSize = true, Font = new Font("Segoe UI Semibold", 16F), Margin = new Padding(0) });
            state = new Label { AutoSize = true, MaximumSize = new Size(300, 0), Margin = new Padding(2, 2, 0, 0) };
            heading.Controls.Add(state);
            top.Controls.Add(heading);
            body.Controls.Add(top);

            lockButton = new Button { Size = new Size(ColumnWidth, 40), Font = new Font(Font.FontFamily, 11F, FontStyle.Bold), UseVisualStyleBackColor = true, Margin = new Padding(0, 0, 0, 10) };
            lockButton.Click += delegate { LockClicked(); };
            body.Controls.Add(lockButton);
            counts = Line("", SystemColors.ControlText);

            Heading("Unlock word");
            var wordRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            word = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 6, 12, 0) };
            wordRow.Controls.Add(word);
            change = new Button { AutoSize = true, MinimumSize = new Size(0, 28), Padding = new Padding(8, 0, 8, 0), UseVisualStyleBackColor = true, Margin = new Padding(0) };
            change.Click += delegate { locker.AskForWord(this); };
            wordRow.Controls.Add(change);
            body.Controls.Add(wordRow);
            Line("Type it while the keyboard is locked to get the keyboard back. It's not a password, just something a cat won't type.", SystemColors.GrayText);

            Heading("Options");
            popup = Check("Show a note in the corner when a locked key is pressed");
            popup.CheckedChanged += delegate
            {
                if (loading) return;
                locker.Settings.Popup = popup.Checked;
                locker.Save();
            };
            startup = Check("Start LoafLock when you sign in to Windows");
            startup.CheckedChanged += delegate { StartupChanged(); };
            Line("Stuck? Ctrl+Alt+Del, Task Manager, end LoafLock: the keyboard comes back at once.", SystemColors.GrayText).Margin = new Padding(0, 10, 0, 10);

            footer = new LinkLabel
            {
                AutoSize = true,
                Text = "v" + Program.Version + " · Build: " + BuildInfo.Commit + "  ·  Open log  ·  Remove my settings…  ·  made by b0r3d.org",
                LinkColor = SystemColors.GrayText,
                ActiveLinkColor = SystemColors.HotTrack,
                LinkBehavior = LinkBehavior.HoverUnderline,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(0, 4, 0, 0)
            };
            string text = footer.Text;
            footer.Links.Clear();
            footer.Links.Add(text.IndexOf("Open log"), 8, "log");
            footer.Links.Add(text.IndexOf("Remove my settings"), "Remove my settings…".Length, "remove");
            footer.Links.Add(text.LastIndexOf("b0r3d.org"), 9, MakerUrl);
            footer.LinkClicked += (s, e) => FooterClicked((string)e.Link.LinkData);
            body.Controls.Add(footer);

            Controls.Add(body);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();

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
            rukus.Image = locked ? lockedPicture : unlockedPicture;
            state.Text = locked ? "Keyboard locked. Type your unlock word, or click Unlock." : "Keyboard unlocked.";
            state.ForeColor = locked ? Color.FromArgb(160, 110, 0) : SystemColors.GrayText;
            lockButton.Text = locked ? "Unlock keyboard" : "Lock keyboard";
            counts.Text = "Paws blocked: " + locker.Today.ToString("N0") + " today · " + locker.Total.ToString("N0") + " all time";
            word.Text = locker.Settings.HasWord ? locker.Settings.UnlockWord : "None yet";
            change.Text = locker.Settings.HasWord ? "Change…" : "Pick one…";
            change.Enabled = !locked;
            popup.Checked = locker.Settings.Popup;
            footer.Links[1].Enabled = !locked;
            loading = false;
        }

        void StartupChanged()
        {
            if (loading) return;
            try
            {
                StartWithWindows.Set(startup.Checked, Application.ExecutablePath);
                Log.Write(startup.Checked ? "starts with Windows from now on" : "no longer starts with Windows");
            }
            catch (Exception e)
            {
                Log.Write("couldn't change the start-with-Windows entry: " + e.Message);
                loading = true;
                startup.Checked = !startup.Checked;
                loading = false;
                MessageBox.Show(this, "LoafLock couldn't change that: " + e.Message, "LoafLock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        void FooterClicked(string what)
        {
            if (what == "log")
            {
                if (File.Exists(Log.FilePath)) Open(Log.FilePath);
                else MessageBox.Show(this, "Nothing in the log yet.", "LoafLock", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (what == "remove") RemoveSettings();
            else Open(what);
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

        void Heading(string text)
        {
            body.Controls.Add(new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 10, 0, 4) });
        }

        Label Line(string text, Color color)
        {
            var label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(ColumnWidth, 0), ForeColor = color, Margin = new Padding(0, 0, 0, 6) };
            body.Controls.Add(label);
            return label;
        }

        CheckBox Check(string text)
        {
            var box = new CheckBox { Text = text, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            body.Controls.Add(box);
            return box;
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
