using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LoafLock
{
    // LoafLock's options: from Options… in the window's LoafLock menu, or Ruckus's menu. Every
    // change applies and saves right away. Since 0.83 they're in pages, the way TVFollow's
    // Settings window has them: the pages listed down the left, one page at a time with its title
    // and a line saying what it's for, Close at the bottom. In a row of choices, the one LoafLock
    // starts with says "(default)", so a change that didn't work out is easy to undo.
    class OptionsForm : Form
    {
        public const string LockingPage = "locking", ByItselfPage = "by itself", CatPage = "your cat",
            NotesPage = "notes", LookPage = "look", StartingPage = "starting";

        readonly Locker locker;
        readonly NavList nav = new NavList();
        readonly Panel side = new Panel { Dock = DockStyle.Left, Width = 190, Padding = new Padding(8, 12, 8, 8) };   // the list's strip
        readonly Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 52 };
        readonly Panel pages = new Panel { Dock = DockStyle.Fill };
        readonly Dictionary<string, Column> pageOf = new Dictionary<string, Column>();
        readonly List<NavList.Page> allPages = new List<NavList.Page>();
        readonly Font titleFont, boldFont;
        Column building;   // the page the helpers below add to
        string shown;      // the page on show

        readonly CheckBox hotkeyOn, hotkeyUnlocks, volume, noteOnLock, noteOnKeys, mouse, idle, autoUnlock, catDetection, minimized, startup;
        readonly RadioButton[] idleChoices, autoUnlockChoices, catKeyChoices, catHoldChoices;
        readonly Label hotkeyText, hotkeyProblem, hotkeyLine, headsUp, catRules, catKeyboards;
        readonly LinkLabel changeHotkey;
        readonly TextBox catName;
        readonly RadioButton lookWindows, lookLight, lookDark;
        readonly Button remove;
        bool loading, headsUpOn;

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
            ClientSize = new Size(740, 520);
            try { Icon = AppIcons.Load("loaflock.ico", SystemInformation.IconSize); } catch { }
            titleFont = TitleFont(Font);
            boldFont = new Font(Font, FontStyle.Bold);

            // ---- Locking: the hotkey, the volume keys, the mouse ----
            Page(LockingPage, "Locking", (char)0xE72E, "How you lock the keyboard, and what stays locked.");
            Heading("Hotkey");
            FlowLayoutPanel hotkeyRow = Row();
            hotkeyOn = new CheckBox { Text = "Lock with a hotkey:", AutoSize = true, Margin = new Padding(0, 2, 6, 0) };
            hotkeyOn.CheckedChanged += delegate { if (!loading) HotkeyChanged(hotkeyOn.Checked, locker.Settings.Hotkey); };
            hotkeyRow.Controls.Add(hotkeyOn);
            hotkeyText = new Label { AutoSize = true, Font = boldFont, UseMnemonic = false, Margin = new Padding(0, 3, 10, 0) };
            hotkeyRow.Controls.Add(hotkeyText);
            changeHotkey = new LinkLabel { Text = "Change…", AutoSize = true, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 3, 0, 0) };
            changeHotkey.LinkClicked += delegate { PickHotkey(); };
            hotkeyRow.Controls.Add(changeHotkey);
            hotkeyProblem = Line("", Theme.BadTag);
            hotkeyUnlocks = Check("It unlocks too", on =>
            {
                locker.Settings.HotkeyUnlocks = on;
                Log.Write(on ? "the hotkey unlocks too" : "the hotkey only locks");
                locker.ApplyRules();
                locker.SettingsChanged();  // the window and the notes name it, or stop naming it
            });
            hotkeyUnlocks.Margin = new Padding(18, 4, 0, 2);
            hotkeyLine = Line("");
            hotkeyLine.Margin = new Padding(18, 2, 0, 4);

            Heading("While locked");
            volume = Check("Let the volume keys through", on => { locker.Settings.VolumeThrough = on; locker.VolumeChanged(); });
            Line("From the next lock, mute, volume up and down, and a volume knob still work. Play/pause stays locked, or a paw on it would pause the video.");

            Heading("The mouse");
            mouse = Check("Lock the mouse too", on => { locker.Settings.LockMouse = on; locker.Save(); });
            Line("From the next lock, the pointer, the buttons and the wheel stop too, so clicking Unlock won't work: your unlock word gets things back " +
                "(or the hotkey, if it unlocks too). If you're stuck: Ctrl+Alt+Del, Lock, then sign back in, and the mouse works again.");

            // ---- By itself: locking when nobody's there, unlocking when the cat's gone ----
            Page(ByItselfPage, "By itself", (char)0xE916, "Locking and unlocking without you.");
            Heading("Locking by itself");
            idle = Check("Lock when nobody's used the keyboard or mouse for:", on =>
            {
                locker.Settings.IdleLock = on;
                locker.RestartIdle();
                locker.Save();
                ShowHeadsUp();
            });
            idleChoices = Choices(Idle.Choices, Idle.DefaultMinutes, m => m.ToString(), m =>
            {
                locker.Settings.IdleMinutes = m;
                locker.RestartIdle();
                locker.Save();
                ShowHeadsUp();
            }, null);
            Line("Minutes. Watching a video without touching anything counts; a game that has the whole screen to itself doesn't (one in a borderless window still does). " +
                "It locks only the keyboard, so you can click Unlock when you're back.");

            Heading("Unlocking by itself");
            autoUnlock = Check("Unlock once the cat's been gone for:", on =>
            {
                locker.Settings.AutoUnlock = on;
                Log.Write(on ? "unlocks by itself after " + Settings.WaitText(locker.Settings.AutoUnlockSeconds) + " with nothing pressed" : "doesn't unlock by itself");
                locker.ApplyRules();
                locker.Save();
                ShowHeadsUp();
            });
            autoUnlockChoices = Choices(Settings.AutoUnlockChoices, Settings.DefaultAutoUnlockSeconds,
                s => s == 60 ? "1 minute" : s % 60 == 0 ? s / 60 + " minutes" : s + " seconds", s =>
            {
                locker.Settings.AutoUnlockSeconds = s;
                locker.ApplyRules();
                locker.Save();
            }, null);
            Line("Gone means nothing pressed or held down for that long, once the lock has blocked a paw. A lock you set before the cat comes stays on until " +
                "it's been and gone. Your unlock word still works straight away.");
            headsUp = Line("", Theme.GoldTag);
            headsUp.Margin = new Padding(0, 6, 0, 4);

            // ---- Your cat: the name, and cat detection with its numbers ----
            Page(CatPage, "Your cat", (char)0xEB51, "Your cat's name, and spotting your cat on the keyboard.");
            FlowLayoutPanel nameRow = Row();
            nameRow.Controls.Add(new Label { Text = "Name:", AutoSize = true, Margin = new Padding(0, 4, 6, 0) });
            catName = new TextBox { Width = 220, MaxLength = Settings.MaxNameLength, Margin = new Padding(0) };
            catName.TextChanged += delegate { if (!loading) NameChanged(); };
            nameRow.Controls.Add(catName);
            Line("Shown with the paw counts: \"Paws blocked by Ruckus\". Leave it empty for none.");

            Heading("Cat detection (experimental)");
            catDetection = Check("Lock when it looks like a cat is on the keyboard", on =>
            {
                locker.Settings.CatDetection = on;
                Log.Write(on ? "cat detection on" : "cat detection off");
                locker.ApplyWatch();
                locker.Save();
            });
            catKeyChoices = Choices(PawWatch.KeyChoices, PawWatch.DefaultKeys, k => k.ToString(), k =>
            {
                locker.Settings.CatKeys = k;
                CatRulesChanged();
            }, "Keys:");
            catHoldChoices = Choices(PawWatch.HoldChoices, PawWatch.DefaultHoldMs, Settings.SecondsText, ms =>
            {
                locker.Settings.CatHoldMs = ms;
                CatRulesChanged();
            }, "Held for:");
            catRules = Line("");
            catKeyboards = Line("");

            // ---- Notes ----
            Page(NotesPage, "Notes", (char)0xE8BD, "The notes in the corner of the screen.");
            noteOnLock = Check("When the keyboard locks or unlocks", on => { locker.Settings.NoteOnLock = on; locker.Save(); });
            noteOnKeys = Check("On every blocked key", on => { locker.Settings.NoteOnKeys = on; locker.Save(); });
            Line("A note never takes focus, so a fullscreen video stays fullscreen.");

            // ---- Look ----
            Page(LookPage, "Look", (char)0xE790, "Light or dark windows.");
            FlowLayoutPanel looks = Row();
            lookWindows = LookChoice(looks, "Like Windows (default)", Look.Windows);
            lookLight = LookChoice(looks, "Light", Look.Light);
            lookDark = LookChoice(looks, "Dark", Look.Dark);
            Line("Like Windows follows the light or dark setting Windows has for apps.");

            // ---- Starting, and your settings ----
            Page(StartingPage, "Starting", (char)0xE7E8, "How LoafLock starts, and your settings.");
            minimized = Check("Start minimized (just Ruckus in the taskbar corner)", on => { locker.Settings.StartMinimized = on; locker.Save(); });
            startup = Check("Start LoafLock when you sign in to Windows", StartupChanged);
            Heading("Your settings");
            remove = new Button { Text = "Remove my settings…", AutoSize = true, MinimumSize = new Size(0, 28), Padding = new Padding(8, 0, 8, 0), UseVisualStyleBackColor = true, Margin = new Padding(0, 0, 0, 4) };
            remove.Click += delegate { RemoveSettings(); };
            building.Controls.Add(remove);
            Line("Forgets your unlock word, your cat's name and the paw counts, removes the sign-in entry, then quits LoafLock.");

            // ---- the list, the footer ----
            foreach (NavList.Page page in allPages) nav.Items.Add(page);
            nav.Dock = DockStyle.Fill;
            nav.Font = Font;
            nav.SelectedIndexChanged += delegate { if (nav.Selected != null) Reveal(nav.Selected.Key); };
            side.Controls.Add(nav);
            var close = new Button { Text = "Close", AutoSize = true, MinimumSize = new Size(88, 28), UseVisualStyleBackColor = true, Anchor = AnchorStyles.Right | AnchorStyles.Top };
            close.Location = new Point(footer.Width - 112, 12);  // anchored right: it keeps this gap from the edge
            close.Click += delegate { Close(); };
            CancelButton = close;
            footer.Controls.Add(close);

            // docked in reverse: the footer across the bottom, then the list down the left, then the pages.
            // Tab goes the way the window reads: the list, the page that's open, the footer.
            Controls.Add(pages);
            Controls.Add(side);
            Controls.Add(footer);
            side.TabIndex = 0;
            pages.TabIndex = 1;
            footer.TabIndex = 2;

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
            Restyle();
            Fill();
            ShowPage(LockingPage);
            locker.Changed += Fill;
            Theme.Changed += Restyle;
            Disposed += delegate { locker.Changed -= Fill; Theme.Changed -= Restyle; };  // closed, or just disposed (the tests)
            // the list has the focus, so the arrow keys move between pages straight away
            Shown += delegate { nav.Focus(); };
        }

        // light or dark: the window, then the parts with colours of their own
        void Restyle()
        {
            Theme.Apply(this);
            nav.Recolour();
            side.BackColor = NavList.Back;
            footer.BackColor = Theme.Footer;
        }

        protected override void OnSystemColorsChanged(EventArgs e)
        {
            base.OnSystemColorsChanged(e);
            if (footer != null) Restyle();  // a high contrast theme went on or off
        }

        // the pages' titles: Segoe UI Semibold where Windows has it
        static Font TitleFont(Font body)
        {
            var font = new Font("Segoe UI Semibold", 15f);
            if (font.Name == "Segoe UI Semibold") return font;
            font.Dispose();
            return new Font(body.FontFamily, 15f, FontStyle.Bold);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                titleFont.Dispose();
                boldFont.Dispose();
            }
            base.Dispose(disposing);
        }

        // the screenshot tool shows it off screen without taking focus
        internal bool Quiet = false;
        protected override bool ShowWithoutActivation { get { return Quiet; } }

        // what the tests click and read
        internal CheckBox NoteOnLockBox { get { return noteOnLock; } }
        internal CheckBox NoteOnKeysBox { get { return noteOnKeys; } }
        internal CheckBox MinimizedBox { get { return minimized; } }
        internal CheckBox MouseBox { get { return mouse; } }
        internal CheckBox HotkeyBox { get { return hotkeyOn; } }
        internal CheckBox HotkeyUnlocksBox { get { return hotkeyUnlocks; } }
        internal CheckBox VolumeBox { get { return volume; } }
        internal string HotkeyProblemText { get { return hotkeyProblem.Text; } }
        internal string HotkeyLineText { get { return hotkeyLine.Text; } }
        internal CheckBox IdleBox { get { return idle; } }
        internal CheckBox AutoUnlockBox { get { return autoUnlock; } }
        internal CheckBox CatDetectionBox { get { return catDetection; } }
        internal RadioButton IdleChoice(int minutes) { return idleChoices[Array.IndexOf(Idle.Choices, minutes)]; }
        internal RadioButton AutoUnlockChoice(int seconds) { return autoUnlockChoices[Array.IndexOf(Settings.AutoUnlockChoices, seconds)]; }
        internal RadioButton CatKeysChoice(int keys) { return catKeyChoices[Array.IndexOf(PawWatch.KeyChoices, keys)]; }
        internal RadioButton CatHoldChoice(int ms) { return catHoldChoices[Array.IndexOf(PawWatch.HoldChoices, ms)]; }
        internal RadioButton[][] ChoiceRows { get { return new[] { idleChoices, autoUnlockChoices, catKeyChoices, catHoldChoices, new[] { lookWindows, lookLight, lookDark } }; } }
        internal bool HeadsUpShown { get { return headsUpOn; } }
        internal string HeadsUpText { get { return headsUp.Text; } }
        internal string CatRulesText { get { return catRules.Text + " " + catKeyboards.Text; } }
        internal TextBox CatNameBox { get { return catName; } }
        internal RadioButton LookBox(Look look) { return look == Look.Light ? lookLight : look == Look.Dark ? lookDark : lookWindows; }
        internal string CurrentPage { get { return shown; } }
        internal List<string> PageKeys { get { return nav.Pages.ConvertAll(p => p.Key); } }
        internal NavList Nav { get { return nav; } }
        internal Control Side { get { return side; } }
        internal Control Pages { get { return pages; } }
        internal Control Footer { get { return footer; } }

        // the page a control is on, or null
        internal string PageHolding(Control control)
        {
            for (Control c = control; c != null; c = c.Parent)
                foreach (KeyValuePair<string, Column> pair in pageOf)
                    if (pair.Value == c) return pair.Key;
            return null;
        }

        void Fill()
        {
            loading = true;
            Settings s = locker.Settings;
            hotkeyOn.Checked = s.HotkeyOn;
            hotkeyText.Text = s.Hotkey.ToString();
            hotkeyProblem.Text = locker.HotkeyProblem ?? "";
            hotkeyProblem.Visible = locker.HotkeyProblem != null;
            hotkeyUnlocks.Checked = s.HotkeyUnlocks;
            volume.Checked = s.VolumeThrough;  // it applies from the next lock
            noteOnLock.Checked = s.NoteOnLock;
            noteOnKeys.Checked = s.NoteOnKeys;
            mouse.Checked = s.LockMouse;  // it applies from the next lock
            idle.Checked = s.IdleLock;
            Pick(idleChoices, Idle.Choices, s.IdleMinutes);
            autoUnlock.Checked = s.AutoUnlock;
            Pick(autoUnlockChoices, Settings.AutoUnlockChoices, s.AutoUnlockSeconds);
            catDetection.Checked = s.CatDetection;
            Pick(catKeyChoices, PawWatch.KeyChoices, s.CatKeys);
            Pick(catHoldChoices, PawWatch.HoldChoices, s.CatHoldMs);
            if (Settings.CleanName(catName.Text) != s.CatName) catName.Text = s.CatName;  // not while it's being typed
            LookBox(s.Look).Checked = true;
            minimized.Checked = s.StartMinimized;
            ShowHotkeyLine();
            ShowHeadsUp();
            ShowCatRules();
            // nothing here is greyed out while locked: greyed-out text is hard to read in dark
            // mode, so "Remove my settings…" says why it won't instead
            loading = false;
        }

        static void Pick(RadioButton[] row, int[] values, int value)
        {
            for (int i = 0; i < values.Length; i++) row[i].Checked = values[i] == value;
        }

        // what the hotkey does: locks only, unlocks too, or can't unlock (a key on its own)
        void ShowHotkeyLine()
        {
            Settings s = locker.Settings;
            if (!s.HotkeyUnlocks) hotkeyLine.Text = "It only locks. It can't unlock, so a cat can't either.";
            else if (!s.HotkeyOn) hotkeyLine.Text = "It unlocks too once Lock with a hotkey is ticked.";
            else if (!s.Hotkey.CanUnlock) hotkeyLine.Text = s.Hotkey + " on its own only locks: one paw could press it. Pick a combo with Ctrl to unlock with it too.";
            else hotkeyLine.Text = "Press it again to unlock: it unlocks as you let go. It only counts with just those keys down, so a cat lying across the " +
                "keyboard can't, but one that lands on exactly " + s.Hotkey + " could.";
        }

        // Locking by itself and unlocking by itself, both on: say what that means
        void ShowHeadsUp()
        {
            Settings s = locker.Settings;
            headsUpOn = s.IdleLock && s.AutoUnlock;
            headsUp.Text = "Both are on: once the cat's gone the keyboard unlocks, and it locks by itself again only after nobody's used the PC for another " +
                s.IdleMinutes + " minutes.";
            headsUp.Visible = headsUpOn;
        }

        void CatRulesChanged()
        {
            locker.ApplyRules();
            locker.Save();
            ShowCatRules();
        }

        // what cat detection goes by, in the numbers picked
        void ShowCatRules()
        {
            Settings s = locker.Settings;
            int keys = s.CatKeys, fewer = keys - 1;
            catRules.Text = "Locks when " + keys + " keys stay down together for " + Settings.SecondsText(s.CatHoldMs) + ", or " + fewer +
                " that land at once stay down that long, or " + fewer + " stay down for a second and a half. Fewer keys or a shorter time spots more cats, and gives more false alarms.";
            catKeyboards.Text = (keys <= 3
                ? "Any keyboard can show it a cat, but holding two keys together in a game (W and A) sets it off."
                : "It can't spot a cat on a keyboard that senses only " + (keys - 2) + " keys at once, and holding " + Number(fewer) + " keys together in a game could set it off.") +
                " While it's on, LoafLock watches the keyboard all the time: how many keys are down, never what's typed.";
        }

        static string Number(int n)
        {
            string[] words = { "no", "one", "two", "three", "four", "five", "six" };
            return n >= 0 && n < words.Length ? words[n] : n.ToString();
        }

        // on or off, or a new combo: Windows is asked for it straight away, and if something
        // else has it, the old setting comes back and it says why
        internal void HotkeyChanged(bool on, Hotkey hotkey)
        {
            Settings s = locker.Settings;
            bool wasOn = s.HotkeyOn;
            Hotkey was = s.Hotkey;
            s.HotkeyOn = on;
            s.Hotkey = hotkey;
            if (!locker.ApplyHotkey())
            {
                string why = locker.HotkeyProblem;
                s.HotkeyOn = wasOn;
                s.Hotkey = was;
                locker.ApplyHotkey();
                locker.SettingsChanged();  // Fill shows the old setting again...
                hotkeyProblem.Text = why;  // ...and why
                hotkeyProblem.Visible = true;
                return;
            }
            Log.Write(on ? "hotkey " + hotkey + " locks" + (s.HotkeyUnlocksNow ? " and unlocks" : "") : "no hotkey");
            locker.ApplyRules();  // the gate's combo, when it unlocks too
            locker.SettingsChanged();
        }

        void PickHotkey()
        {
            locker.HoldHotkey();  // or pressing the current one in the picker would lock
            bool picked = false;
            try
            {
                using (var pick = new HotkeyForm(locker.Settings.Hotkey))
                {
                    if (pick.ShowDialog(this) != DialogResult.OK) return;
                    picked = true;
                    HotkeyChanged(true, pick.Picked);  // picking one turns it on
                }
            }
            finally
            {
                if (!picked) locker.ApplyHotkey();  // the old one back
            }
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

        // ---- pages ----

        public void ShowPage(string key)
        {
            for (int i = 0; i < nav.Items.Count; i++)
                if (((NavList.Page)nav.Items[i]).Key == key && nav.SelectedIndex != i) nav.SelectedIndex = i;
            Reveal(key);
        }

        void Reveal(string key)
        {
            if (key == shown || !pageOf.ContainsKey(key)) return;
            shown = key;
            SuspendLayout();
            foreach (KeyValuePair<string, Column> pair in pageOf) pair.Value.Visible = pair.Key == key;
            ResumeLayout(true);
        }

        // ---- building ----

        // a new page, with its title and a line about it, listed down the left; the helpers
        // below add to it from here on
        Column Page(string key, string name, char icon, string about)
        {
            var page = new Column { Padding = new Padding(28, 18, 28, 12), Visible = false };
            pageOf[key] = page;
            allPages.Add(new NavList.Page(key, name, icon));
            pages.Controls.Add(page);
            building = page;
            page.Controls.Add(new Label { Text = name, AutoSize = true, Font = titleFont, UseMnemonic = false, Margin = new Padding(0, 0, 0, 2) });
            Label line = Line(about);
            line.Margin = new Padding(0, 0, 0, 10);
            return page;
        }

        void Heading(string text)
        {
            building.Controls.Add(new Label { Text = text, AutoSize = true, Font = boldFont, UseMnemonic = false, Margin = new Padding(0, 10, 0, 6) });
        }

        // a line of text, grey unless tagged otherwise; it wraps to the page's width
        Label Line(string text, string tag = Theme.DimTag)
        {
            var label = new Label { Text = text, AutoSize = true, Tag = tag, UseMnemonic = false, Margin = new Padding(0, 2, 0, 4) };
            building.Controls.Add(label);
            return label;
        }

        FlowLayoutPanel Row()
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
            building.Controls.Add(row);
            return row;
        }

        CheckBox Check(string text, Action<bool> changed)
        {
            var box = new CheckBox { Text = text, AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
            box.CheckedChanged += delegate { if (!loading) changed(box.Checked); };
            building.Controls.Add(box);
            return box;
        }

        // A row of choices, indented under its tick, the one LoafLock starts with marked
        // "(default)"; picked runs when one is picked. label, if any, goes in front ("Keys:").
        RadioButton[] Choices(int[] values, int byDefault, Func<int, string> text, Action<int> picked, string label)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(18, 0, 0, 4) };
            if (label != null) row.Controls.Add(new Label { Text = label, AutoSize = true, MinimumSize = new Size(64, 0), Margin = new Padding(0, 4, 4, 0) });
            var made = new RadioButton[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                int value = values[i];
                var choice = new RadioButton { Text = text(value) + (value == byDefault ? " (default)" : ""), AutoSize = true, Margin = new Padding(0, 2, 14, 0) };
                choice.CheckedChanged += delegate { if (!loading && choice.Checked) picked(value); };
                row.Controls.Add(choice);
                made[i] = choice;
            }
            building.Controls.Add(row);
            return made;
        }

        // one page: top to bottom, its lines wrapped to the page's width (TVFollow's Column)
        sealed class Column : FlowLayoutPanel
        {
            public Column()
            {
                FlowDirection = FlowDirection.TopDown;
                WrapContents = false;
                AutoScroll = true;
                Dock = DockStyle.Fill;
            }

            protected override void OnLayout(LayoutEventArgs levent)
            {
                int width = ClientSize.Width - Padding.Horizontal;
                foreach (Control c in Controls)
                {
                    int w = Math.Max(60, width - c.Margin.Horizontal);
                    if (c is Label && c.AutoSize && c.MaximumSize.Width != w) c.MaximumSize = new Size(w, 0);
                }
                base.OnLayout(levent);
            }
        }
    }
}
