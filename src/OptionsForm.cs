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
        readonly CheckBox noteOnLock, noteOnKeys, minimized, startup;
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
            BackColor = SystemColors.Window;
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

            Heading("Starting");
            minimized = Check("Start minimized (just Rukus in the taskbar corner)", on => { locker.Settings.StartMinimized = on; locker.Save(); });
            startup = Check("Start LoafLock when you sign in to Windows", StartupChanged);

            Heading("Your settings");
            remove = new Button { Text = "Remove my settings…", AutoSize = true, MinimumSize = new Size(0, 28), Padding = new Padding(8, 0, 8, 0), UseVisualStyleBackColor = true, Margin = new Padding(0, 0, 0, 4) };
            remove.Click += delegate { RemoveSettings(); };
            body.Controls.Add(remove);
            Line("Forgets your unlock word and paw counts, removes the sign-in entry, then quits LoafLock.");

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
            Fill();
            locker.Changed += Fill;
            FormClosed += delegate { locker.Changed -= Fill; };
        }

        // the screenshot tool shows it off screen without taking focus
        internal bool Quiet = false;
        protected override bool ShowWithoutActivation { get { return Quiet; } }

        // what the tests click
        internal CheckBox NoteOnLockBox { get { return noteOnLock; } }
        internal CheckBox NoteOnKeysBox { get { return noteOnKeys; } }
        internal CheckBox MinimizedBox { get { return minimized; } }

        void Fill()
        {
            loading = true;
            Settings s = locker.Settings;
            noteOnLock.Checked = s.NoteOnLock;
            noteOnKeys.Checked = s.NoteOnKeys;
            minimized.Checked = s.StartMinimized;
            remove.Enabled = !locker.IsLocked;
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

        void RemoveSettings()
        {
            if (locker.IsLocked) return;
            DialogResult answer = MessageBox.Show(this,
                "Remove LoafLock's settings, its log and its start-with-Windows entry, then quit?\n\n" +
                "Your unlock word and paw counts will be gone. LoafLock.exe stays where it is: delete it too to remove LoafLock completely.",
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
            body.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(ColumnWidth, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(0, 2, 0, 4) });
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
