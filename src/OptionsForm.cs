using System;
using System.Drawing;
using System.Windows.Forms;

namespace LoafLock
{
    // LoafLock's options: from Options in the window's menu bar, or Rukus's menu. Every change
    // applies and saves right away. New options get a heading of their own here.
    class OptionsForm : Form
    {
        const int ColumnWidth = 360;

        readonly Locker locker;
        readonly FlowLayoutPanel body;
        readonly CheckBox noteOnLock, noteOnKeys, mouse, minimized, startup;
        readonly TextBox catName;
        readonly RadioButton lookWindows, lookLight, lookDark;
        readonly Button remove;
        bool loading;

        // "Remove my settings" ran: LoafLock quits once this closes
        public bool Forgot { get; private set; }

        public OptionsForm(Locker locker)
        {
            this.locker = locker;
            SuspendLayout();
            Text = "LoafLock options";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            try { Icon = AppIcons.Load("loaflock.ico", SystemInformation.IconSize); } catch { }

            body = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(24, 12, 24, 16)
            };

            Heading("Notes in the corner");
            noteOnLock = Check("When the keyboard locks or unlocks", on => { locker.Settings.NoteOnLock = on; locker.Save(); });
            noteOnKeys = Check("On every blocked key", on => { locker.Settings.NoteOnKeys = on; locker.Save(); });
            Line("A note never takes focus, so a fullscreen video stays fullscreen.");

            Heading("Your cat");
            var nameRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 2) };
            nameRow.Controls.Add(new Label { Text = "Name:", AutoSize = true, Margin = new Padding(0, 4, 6, 0) });
            catName = new TextBox { Width = 200, MaxLength = Settings.MaxNameLength, Margin = new Padding(0) };
            catName.TextChanged += delegate { if (!loading) NameChanged(); };
            nameRow.Controls.Add(catName);
            body.Controls.Add(nameRow);
            Line("Shown with the paw counts: \"Paws blocked by Rukus\". Leave it empty for none.");

            Heading("The mouse");
            mouse = Check("Lock the mouse too", on => { locker.Settings.LockMouse = on; locker.Save(); });
            Line("From the next lock, the pointer, the buttons and the wheel stop too, so only your unlock word gets things back. " +
                "If you're stuck: Ctrl+Alt+Del, Lock, then sign back in, and the mouse works again.");

            Heading("Look");
            var looks = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            lookWindows = LookChoice(looks, "Like Windows", Look.Windows);
            lookLight = LookChoice(looks, "Light", Look.Light);
            lookDark = LookChoice(looks, "Dark", Look.Dark);
            body.Controls.Add(looks);

            Heading("Starting");
            minimized = Check("Start minimized (just Rukus in the taskbar corner)", on => { locker.Settings.StartMinimized = on; locker.Save(); });
            startup = Check("Start LoafLock when you sign in to Windows", StartupChanged);

            Heading("Your settings");
            remove = new Button { Text = "Remove my settings…", AutoSize = true, MinimumSize = new Size(0, 28), Padding = new Padding(8, 0, 8, 0), UseVisualStyleBackColor = true, Margin = new Padding(0, 0, 0, 4) };
            remove.Click += delegate { RemoveSettings(); };
            body.Controls.Add(remove);
            Line("Forgets your unlock word, your cat's name and the paw counts, removes the sign-in entry, then quits LoafLock.");

            var close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(88, 28), UseVisualStyleBackColor = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 14, 0, 0) };
            close.Click += delegate { Close(); };
            body.Controls.Add(close);
            CancelButton = close;

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
            Theme.Apply(this);
            Fill();
            locker.Changed += Fill;
            Theme.Changed += Restyle;
            Disposed += delegate { locker.Changed -= Fill; Theme.Changed -= Restyle; };  // closed, or just disposed (the tests)
        }

        void Restyle()
        {
            Theme.Apply(this);
        }

        // the screenshot tool shows it off screen without taking focus
        internal bool Quiet = false;
        protected override bool ShowWithoutActivation { get { return Quiet; } }

        // what the tests click
        internal CheckBox NoteOnLockBox { get { return noteOnLock; } }
        internal CheckBox NoteOnKeysBox { get { return noteOnKeys; } }
        internal CheckBox MinimizedBox { get { return minimized; } }
        internal CheckBox MouseBox { get { return mouse; } }
        internal TextBox CatNameBox { get { return catName; } }
        internal RadioButton LookBox(Look look) { return look == Look.Light ? lookLight : look == Look.Dark ? lookDark : lookWindows; }

        void Fill()
        {
            loading = true;
            Settings s = locker.Settings;
            noteOnLock.Checked = s.NoteOnLock;
            noteOnKeys.Checked = s.NoteOnKeys;
            mouse.Checked = s.LockMouse;  // it applies from the next lock
            if (Settings.CleanName(catName.Text) != s.CatName) catName.Text = s.CatName;  // not while it's being typed
            LookBox(s.Look).Checked = true;
            minimized.Checked = s.StartMinimized;
            // nothing here is greyed out while locked: greyed-out text is hard to read in dark
            // mode, so "Remove my settings…" says why it won't instead
            loading = false;
        }

        void NameChanged()
        {
            string name = Settings.CleanName(catName.Text);
            if (name == locker.Settings.CatName) return;
            locker.Settings.CatName = name;
            locker.SettingsChanged();
        }

        RadioButton LookChoice(FlowLayoutPanel row, string text, Look look)
        {
            var choice = new RadioButton { Text = text, AutoSize = true, Margin = new Padding(0, 0, 16, 0) };
            choice.CheckedChanged += delegate
            {
                if (loading || !choice.Checked) return;
                locker.Settings.Look = look;
                locker.Save();
                Theme.Set(look);
            };
            row.Controls.Add(choice);
            return choice;
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

        void RemoveSettings()
        {
            if (locker.IsLocked)
            {
                MessageBox.Show(this, "Unlock the keyboard first, then remove the settings.", "LoafLock", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult answer = MessageBox.Show(this,
                "Remove LoafLock's settings, its log and its start-with-Windows entry, then quit?\n\n" +
                "Your unlock word, your cat's name and the paw counts will be gone. LoafLock.exe stays where it is: delete it too to remove LoafLock completely.",
                "LoafLock", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
            locker.ForgetEverything();
            Forgot = true;
            Close();
        }

        void Heading(string text)
        {
            body.Controls.Add(new Label { Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 10, 0, 6) });
        }

        void Line(string text)
        {
            body.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(ColumnWidth, 0), Tag = Theme.DimTag, Margin = new Padding(0, 2, 0, 4) });
        }

        CheckBox Check(string text, Action<bool> changed)
        {
            var box = new CheckBox { Text = text, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            box.CheckedChanged += delegate { if (!loading) changed(box.Checked); };
            body.Controls.Add(box);
            return box;
        }
    }
}
