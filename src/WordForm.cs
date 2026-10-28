using System;
using System.Drawing;
using System.Windows.Forms;

namespace LoafLock
{
    // Asks for the unlock word: on the first run, and from the tray's "Change unlock word…".
    class WordForm : Form
    {
        readonly TextBox box;
        readonly Label problem;

        // what was typed, as LoafLock keeps it
        public string Word { get { return box.Text.Trim().ToLowerInvariant(); } }

        // the screenshot tool shows it off screen without taking focus
        internal bool Quiet = false;
        protected override bool ShowWithoutActivation { get { return Quiet; } }

        public WordForm(string current)
        {
            SuspendLayout();
            Text = "LoafLock";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            try { Icon = AppIcons.Load("loaflock.ico", SystemInformation.IconSize); } catch { }

            var body = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(20, 16, 20, 12)
            };

            var top = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
            var rukus = new PictureBox { Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(0, 0, 12, 0) };
            try
            {
                using (Icon big = AppIcons.Load("loaflock.ico", new Size(64, 64))) rukus.Image = big.ToBitmap();
            }
            catch { }
            top.Controls.Add(rukus);
            top.Controls.Add(new Label
            {
                Text = current.Length > 0 ? "Change your unlock word" : "Pick an unlock word",
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 13F),
                Margin = new Padding(0, 10, 0, 0)
            });
            body.Controls.Add(top);

            body.Controls.Add(Note(
                "While the keyboard is locked, typing this word gives it back. Letters only, at least " +
                Settings.MinWordLength + ". It's not a password, just something a cat won't type.", null));

            box = new TextBox { Width = 260, MaxLength = Settings.MaxWordLength, CharacterCasing = CharacterCasing.Lower, Text = current, Margin = new Padding(0, 4, 0, 4) };
            box.TextChanged += delegate { problem.Text = ""; };
            body.Controls.Add(box);

            problem = Note("", Theme.BadTag);
            body.Controls.Add(problem);

            body.Controls.Add(Note(
                "Rukus's menu in the taskbar corner locks and unlocks too. If you're ever stuck: " +
                "Ctrl+Alt+Del, Task Manager, end LoafLock.", Theme.DimTag));

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0, 8, 0, 0) };
            var ok = MakeButton("OK");
            var cancel = MakeButton("Cancel");
            ok.Click += delegate { Done(); };
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            body.Controls.Add(buttons);

            Controls.Add(body);
            AcceptButton = ok;
            CancelButton = cancel;

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();
            Theme.Apply(this);
            Theme.Changed += Restyle;
            Disposed += delegate { Theme.Changed -= Restyle; };
            Shown += delegate { box.Focus(); box.SelectAll(); };
        }

        void Restyle()
        {
            Theme.Apply(this);
        }

        void Done()
        {
            string why = Settings.WordProblem(box.Text.Trim());
            if (why != null)
            {
                problem.Text = why;
                box.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
        }

        // tag: Theme.DimTag, Theme.BadTag, or null for the window's text colour
        static Label Note(string text, string tag)
        {
            return new Label { Text = text, AutoSize = true, MaximumSize = new Size(360, 0), Tag = tag, Margin = new Padding(0, 0, 0, 6) };
        }

        static Button MakeButton(string text)
        {
            return new Button { Text = text, AutoSize = true, MinimumSize = new Size(80, 28), UseVisualStyleBackColor = true, Margin = new Padding(8, 0, 0, 0) };
        }
    }
}
