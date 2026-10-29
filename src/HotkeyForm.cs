using System;
using System.Drawing;
using System.Windows.Forms;

namespace LoafLock
{
    // "Change…" next to the hotkey in Options: press the keys, see them, OK.
    class HotkeyForm : Form
    {
        readonly Label combo, problem;
        readonly Button ok;
        Hotkey picked;
        bool showingGood = true;  // what's shown is a hotkey LoafLock takes (OK isn't greyed out: in dark mode that's unreadable)

        public Hotkey Picked { get { return picked; } }

        // the screenshot tool shows it off screen without taking focus
        internal bool Quiet = false;
        protected override bool ShowWithoutActivation { get { return Quiet; } }

        public HotkeyForm(Hotkey current)
        {
            picked = current;
            SuspendLayout();
            Text = "LoafLock hotkey";
            Font = SystemFonts.MessageBoxFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            KeyPreview = true;
            try { Icon = AppIcons.Load("loaflock.ico", SystemInformation.IconSize); } catch { }

            var body = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(20, 16, 20, 12)
            };
            body.Controls.Add(new Label { Text = "Press the keys for the hotkey", AutoSize = true, Font = new Font("Segoe UI Semibold", 12F), Margin = new Padding(0, 0, 0, 8) });
            body.Controls.Add(new Label
            {
                Text = "Ctrl with a key, like Ctrl+Alt+L, or Pause, Scroll Lock or F13 to F24 on their own. It only locks the keyboard.",
                AutoSize = true, MaximumSize = new Size(340, 0), Tag = Theme.DimTag, Margin = new Padding(0, 0, 0, 10)
            });
            combo = new Label { Text = current.ToString(), AutoSize = true, Font = new Font("Segoe UI Semibold", 16F), Margin = new Padding(0, 0, 0, 4) };
            body.Controls.Add(combo);
            problem = new Label { AutoSize = true, MaximumSize = new Size(340, 0), Tag = Theme.BadTag, Margin = new Padding(0, 0, 0, 6) };
            body.Controls.Add(problem);

            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0, 8, 0, 0) };
            ok = new Button { Text = "OK", AutoSize = true, MinimumSize = new Size(80, 28), UseVisualStyleBackColor = true, Margin = new Padding(8, 0, 0, 0), TabStop = false };
            ok.Click += delegate { if (showingGood) DialogResult = DialogResult.OK; };
            var cancel = new Button { Text = "Cancel", AutoSize = true, MinimumSize = new Size(80, 28), UseVisualStyleBackColor = true, DialogResult = DialogResult.Cancel, Margin = new Padding(8, 0, 0, 0), TabStop = false };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            body.Controls.Add(buttons);
            Controls.Add(body);

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();
            Theme.Apply(this);
        }

        // every key comes here first, Tab and the arrows included, so any of them can be part
        // of the combo; Esc on its own cancels, Enter on its own is OK
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode, mods = keyData & Keys.Modifiers;
            if (key == Keys.Escape && mods == Keys.None) { DialogResult = DialogResult.Cancel; return true; }
            if (key == Keys.Return && mods == Keys.None) { if (showingGood) DialogResult = DialogResult.OK; return true; }
            if (IsModifierKey(key)) return true;  // waits for the real key
            Try(new Hotkey(key, mods));
            return true;
        }

        void Try(Hotkey hotkey)
        {
            combo.Text = hotkey.ToString();
            string why = hotkey.Problem;
            problem.Text = why ?? "";
            showingGood = why == null;
            if (showingGood) picked = hotkey;
        }

        internal void Press(Keys keyData)  // for the tests
        {
            var none = new Message();
            ProcessCmdKey(ref none, keyData);
        }

        static bool IsModifierKey(Keys key)
        {
            return key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin;
        }
    }
}
