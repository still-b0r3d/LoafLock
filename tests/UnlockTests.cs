using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace LoafLock.Tests
{
    // 0.83: unlocking by itself once the cat's gone, the hotkey unlocking too, cat detection's
    // numbers, and Options in pages. The gate and PawWatch on a pretend clock first; then the real
    // hook in test mode (only this test's own keys count), Locker, Options and the notes.
    static partial class TestMain
    {
        const int KeyL = 'L', RCtrl = 0xA3, RShift = 0xA1, Pause = 0x13, VolUpKey = 0xAF;

        static void Settings083RoundTrip()
        {
            var s = new Settings();
            Check(!s.AutoUnlock && s.AutoUnlockSeconds == 60 && !s.HotkeyUnlocks && s.CatKeys == 4 && s.CatHoldMs == 250,
                "the 0.83 options don't start at their defaults");
            s.AutoUnlock = true;
            s.AutoUnlockSeconds = 120;
            s.HotkeyUnlocks = true;
            s.CatKeys = 5;
            s.CatHoldMs = 500;
            s.Save();
            Settings back = Settings.Load();
            Check(back.AutoUnlock && back.AutoUnlockSeconds == 120 && back.HotkeyUnlocks && back.CatKeys == 5 && back.CatHoldMs == 500, "the 0.83 options didn't come back");
            File.WriteAllText(Settings.FilePath, "auto_unlock_seconds=45\r\ncat_keys=2\r\ncat_hold_ms=1000\r\n");
            back = Settings.Load();
            Check(back.AutoUnlockSeconds == 60 && back.CatKeys == 4 && back.CatHoldMs == 250,
                "numbers not on offer were read (" + back.AutoUnlockSeconds + " s, " + back.CatKeys + " keys, " + back.CatHoldMs + " ms)");
            Check(Settings.WaitText(30) == "30 seconds" && Settings.WaitText(60) == "a minute" && Settings.WaitText(300) == "5 minutes", "the wait reads wrong");
            Check(Settings.SecondsText(250) == "0.25 s" && Settings.SecondsText(100) == "0.1 s" && Settings.SecondsText(500) == "0.5 s", "a time reads wrong");

            // the hotkey unlocks only when it's on, ticked, and a combo
            var h = new Settings { HotkeyOn = true, HotkeyUnlocks = true };
            Check(h.HotkeyUnlocksNow, "Ctrl+Shift+L, on and ticked, doesn't unlock");
            h.Hotkey = new Hotkey(Keys.Pause, Keys.None);
            Check(!h.HotkeyUnlocksNow, "Pause on its own unlocks");
            h.Hotkey = Hotkey.Default;
            h.HotkeyOn = false;
            Check(!h.HotkeyUnlocksNow, "a hotkey that's off unlocks");
        }

        static void GateAutoUnlock()
        {
            const int Quiet = 60000;
            // a lock nothing touched never unlocks by itself: it was set before the cat came
            KeyGate g = Locked("rukus");
            clock += Quiet * 3;
            Check(!g.Gone(clock, Quiet), "a lock nothing touched unlocked by itself");

            // the cat lies down (D repeating, A and S silent under it), gets up and goes
            g = Locked("rukus");
            Down(g, 'A');
            Down(g, 'S');
            for (int i = 0; i < 10; i++) Down(g, 'D');
            Up(g, 'D');
            clock += Quiet * 2;
            Check(!g.Gone(clock, Quiet), "it would unlock with keys still held down");
            Up(g, 'A');
            Up(g, 'S');
            int left = clock;
            Check(!g.Gone(left + Quiet - 1, Quiet), "it would unlock before the wait was over");
            Check(g.Gone(left + Quiet, Quiet), "the cat's gone and nothing's been pressed for the whole wait, but it wouldn't unlock");

            // anything at all starts the wait over: a key, the locked mouse moving
            clock = left + Quiet - 1000;
            Tap(g, 'J');
            Check(!g.Gone(left + Quiet, Quiet), "a key pressed during the wait didn't start it over");
            Check(g.Gone(clock + Quiet, Quiet), "the wait from the last key didn't count");
            clock += Quiet - 1000;
            g.Stir(clock);
            Check(!g.Gone(clock + 1000, Quiet), "the locked mouse moving didn't start the wait over");

            // the volume keys going through (an option) aren't the cat
            g = Locked("rukus");
            g.PassVolume = true;
            Tap(g, 'A');
            left = clock;
            clock = left + Quiet - 500;
            Check(!Down(g, VolUpKey) && !Up(g, VolUpKey), "the volume key was dropped");
            Check(g.Gone(left + Quiet, Quiet), "turning the volume up counted as the cat");

            // a cat lock: the paw went through (watched while unlocked), so nothing was blocked,
            // but its going still counts
            g = new KeyGate();
            g.SetWord("rukus");
            g.StartWatching(clock);
            clock += 1000;
            foreach (char k in "ASD") Check(!Down(g, k), "a watched key was dropped while unlocked");
            g.LockWatched();
            foreach (char k in "ASD") Check(!Up(g, k), "a paw key's release was dropped after a cat lock");
            Check(g.Gone(clock + Quiet, Quiet), "after a cat lock, the cat going didn't count");

            // a key whose release never came while cat detection watched (Alt+Tab into an admin
            // window takes Alt's and Tab's releases): once it's been silent StaleMs it's let go
            // of, so the next cat lock can still unlock by itself and with the hotkey
            KeyGate stale = new KeyGate();
            stale.SetWord("rukus");
            stale.SetUnlockHotkey(KeyL, true, true, false);
            stale.StartWatching(clock);
            clock += 1000;
            Down(stale, Alt);
            Down(stale, 0x09);  // Tab
            clock += stale.StaleMs + 100;
            stale.Expire(clock);
            foreach (char k in "ASDF") Down(stale, k);
            stale.LockWatched();
            foreach (char k in "ASDF") Up(stale, k);
            Check(stale.Gone(clock + Quiet, Quiet), "a key whose release never came kept the auto unlock from ever undoing a cat lock");
            clock += Quiet;
            Down(stale, Ctrl);
            Down(stale, Shift);
            Down(stale, KeyL);
            Up(stale, KeyL);
            Check(!stale.Locked && stale.UnlockedBy == "hotkey", "a key whose release never came kept the hotkey from undoing a cat lock");

            // ...but a paw's key is never that old: one put down 2.5 s before the cat lock (a slow
            // settle) still counts as held, so the lock waits for it to come up
            KeyGate settle = new KeyGate();
            settle.SetWord("rukus");
            settle.StartWatching(clock);
            clock += 1000;
            Down(settle, 'A');
            clock += 2500;
            settle.Expire(clock);
            Down(settle, 'S');
            Down(settle, 'D');
            settle.LockWatched();
            Up(settle, 'S');
            Up(settle, 'D');
            Check(!settle.Gone(clock + Quiet, Quiet), "a paw key put down 2.5 s before the cat lock was forgotten: it would unlock with that key still held");
            Up(settle, 'A');
            Check(settle.Gone(clock + Quiet, Quiet), "the paw's last key came up, but it wouldn't unlock");

            // how it unlocked: by itself, the word, or told to
            g.Unlock("itself");
            Check(!g.Locked && g.UnlockedBy == "itself" && !g.Gone(clock + Quiet * 2, Quiet), "unlocking by itself left it locked, or unnamed, or still 'gone'");
            g = Locked("rukus");
            Type(g, "rukus");
            Check(!g.Locked && g.UnlockedBy == "word", "the word unlocked as \"" + g.UnlockedBy + "\"");
            g = Locked("rukus");
            g.Unlock();
            Check(g.UnlockedBy == null, "an unlock from the window was put down to \"" + g.UnlockedBy + "\"");
        }

        // the auto unlock trusts the quiet only once a hook put in after the whole wait has heard
        // ConfirmMs of it (a review found the regular renewal inside the wait wasn't enough)
        static void TrustQuietRule()
        {
            // a minute's wait, the cat's last key at 5 s, the regular renewal at 60 s (inside the
            // wait). At 65 s the wait is over, but that hook may have been taken out since
            Check(!KeyHook.TrustQuiet(65000, 5000, 60000, 60000, 1500), "a hook renewed during the wait was trusted");
            // a hook put in once the wait was over, after 1.5 s of quiet
            Check(!KeyHook.TrustQuiet(66499, 5000, 65000, 60000, 1500), "a fresh hook was trusted before it had listened 1.5 s");
            Check(KeyHook.TrustQuiet(66500, 5000, 65000, 60000, 1500), "a fresh hook that heard 1.5 s of quiet wasn't trusted");
            // Windows' clock wraps after 49.7 days
            int stir = int.MaxValue - 1000;
            int fresh = unchecked(stir + 60000), now = unchecked(fresh + 1500);
            Check(KeyHook.TrustQuiet(now, stir, fresh, 60000, 1500), "the rule broke where Windows' clock wraps");
        }

        static void GateHotkeyUnlocks()
        {
            // Ctrl+Shift+L pressed fresh, nothing else down: it unlocks as L comes up. Its presses
            // are still dropped, L's release too (its press never got through), Ctrl's and
            // Shift's go through.
            KeyGate g = Locked("rukus");
            g.SetUnlockHotkey(KeyL, true, true, false);
            Check(Down(g, Ctrl) && Down(g, Shift) && Down(g, KeyL), "the hotkey's presses got through");
            Check(g.Locked, "it unlocked as L went down, before any key landing just after it could be seen");
            Check(Up(g, KeyL), "L's release got through, though its press never did");
            Check(!g.Locked && g.UnlockedBy == "hotkey", "Ctrl+Shift+L didn't unlock as L came up");
            Check(!Up(g, Shift) && !Up(g, Ctrl), "Shift's or Ctrl's release was dropped: they'd stick");

            g = Locked("rukus");
            g.SetUnlockHotkey(KeyL, true, true, false);
            Down(g, RCtrl);
            Down(g, RShift);
            Down(g, KeyL);
            Up(g, KeyL);
            Check(!g.Locked, "the right-hand Ctrl and Shift with L didn't unlock");

            // letting go of Ctrl and Shift before L is fine
            g = Locked("rukus");
            g.SetUnlockHotkey(KeyL, true, true, false);
            Down(g, Ctrl);
            Down(g, Shift);
            Down(g, KeyL);
            Up(g, Shift);
            Up(g, Ctrl);
            Up(g, KeyL);
            Check(!g.Locked, "letting go of Ctrl and Shift before L kept it locked");

            // a paw landing on the combo and its neighbours: the others go down after L, before
            // it comes up (a keyboard reports Ctrl and Shift first)
            g = Locked("rukus");
            g.SetUnlockHotkey(KeyL, true, true, false);
            Down(g, RCtrl);
            Down(g, RShift);
            Down(g, KeyL);
            Down(g, 0xBA);  // ;
            Down(g, 0xBE);  // .
            Up(g, KeyL);
            Check(g.Locked, "a paw on Ctrl, Shift, L, ; and . unlocked (; and . landed just after L)");

            // a cat across the keyboard holds other keys too
            g = Locked("rukus");
            g.SetUnlockHotkey(KeyL, true, true, false);
            Down(g, 'A');
            Down(g, Ctrl);
            Down(g, Shift);
            Down(g, KeyL);
            Check(g.Locked, "Ctrl+Shift+L with A held down too unlocked");

            // only its own modifiers: not Ctrl+L, not with Alt or Windows as well
            foreach (int[] keys in new[] { new[] { Ctrl, KeyL }, new[] { Ctrl, Shift, Alt, KeyL }, new[] { Win, Ctrl, Shift, KeyL }, new[] { Shift, KeyL } })
            {
                g = Locked("rukus");
                g.SetUnlockHotkey(KeyL, true, true, false);
                foreach (int vk in keys) Down(g, vk);
                Up(g, KeyL);
                Check(g.Locked, "the keys " + string.Join(", ", keys.Select(k => k.ToString("X")).ToArray()) + " unlocked");
            }

            // holding the hotkey that locked it: its Ctrl and Shift went down before the lock (not
            // seen), and L's repeats aren't fresh presses
            g = LockedJustNow("rukus");
            g.SetUnlockHotkey(KeyL, true, true, false);
            for (int i = 0; i < 6; i++) Down(g, KeyL);
            Check(g.Locked, "holding the hotkey that locked it unlocked it");
            Up(g, KeyL);
            Check(g.Locked, "letting go of the hotkey that locked it unlocked it");

            // a repeat of L with Ctrl and Shift pressed fresh: still not a fresh L
            g = Locked("rukus");
            g.SetUnlockHotkey(KeyL, true, true, false);
            Down(g, KeyL);
            Down(g, Ctrl);
            Down(g, Shift);
            Down(g, KeyL);
            Up(g, KeyL);
            Check(g.Locked, "a repeat of L, with Ctrl and Shift pressed after it, unlocked");

            // a key on its own never unlocks (one paw could press it); and off is off
            g = Locked("rukus");
            g.SetUnlockHotkey(Pause, false, false, false);
            Down(g, Pause);
            Up(g, Pause);
            Check(g.Locked && g.UnlockComboKey == 0, "Pause on its own unlocked");
            g = Locked("rukus");
            g.SetUnlockHotkey(0, true, true, false);
            Down(g, Ctrl);
            Down(g, Shift);
            Down(g, KeyL);
            Up(g, KeyL);
            Check(g.Locked, "with the option off, the hotkey unlocked");

            // the word still works alongside
            g = Locked("rukus");
            g.SetUnlockHotkey(KeyL, true, true, false);
            Type(g, "rukus");
            Check(!g.Locked && g.UnlockedBy == "word", "the word didn't unlock with the hotkey set to unlock too");
        }

        static void CatNumbers()
        {
            // 5 keys: four put down over 0.3 s and held a second aren't a cat (a pile needs five,
            // a slow settle four held 1.5 s); five are, a quarter of a second after the fifth
            var four = new List<int[]>();
            int t = 1000;
            foreach (int vk in new[] { 'A', 'S', 'D', 'F' }) { Press(four, t, vk, 1000); t += 100; }
            int at = FirstCat(new PawWatch(), four, 4000);
            Check(at >= 1550 && at < 1560, "with the default numbers, four keys put down over 0.3 s locked at " + at + " ms, not at 1550");
            var w = new PawWatch();
            w.SetRules(5, 250);
            Check(FirstCat(w, four, 4000) < 0, "set to 5 keys, four put down over 0.3 s locked");
            var five = new List<int[]>();
            t = 1000;
            foreach (int vk in new[] { 'A', 'S', 'D', 'F', 'G' }) { Press(five, t, vk, 1000); t += 100; }
            w = new PawWatch();
            w.SetRules(5, 250);
            at = FirstCat(w, five, 4000);
            Check(at >= 1650 && at < 1660, "set to 5 keys, five put down over 0.4 s locked at " + at + " ms, not at 1650");
            // ...and a paw needs four: three landing at once are a cat only with the defaults
            var three = new List<int[]>();
            foreach (int vk in new[] { 'A', 'S', 'D' }) Press(three, 1000, vk, 2000);
            w = new PawWatch();
            w.SetRules(5, 250);
            Check(FirstCat(w, three, 4000) < 0 && FirstCat(new PawWatch(), three, 4000) >= 0, "a paw of three is a cat at 5 keys, or isn't at the defaults");

            // half a second: four let go after 0.3 s aren't a cat; held 0.7 s they are, at 0.5 s
            var quick = new List<int[]>();
            foreach (int vk in new[] { 'A', 'S', 'D', 'F' }) Press(quick, 1000, vk, 300);
            w = new PawWatch();
            w.SetRules(4, 500);
            Check(FirstCat(w, quick, 3000) < 0, "set to 0.5 s, four keys let go at 0.3 s locked");
            Check(FirstCat(new PawWatch(), quick, 3000) >= 0, "four keys held 0.3 s aren't a cat with the default numbers");
            var held = new List<int[]>();
            foreach (int vk in new[] { 'A', 'S', 'D', 'F' }) Press(held, 1000, vk, 700);
            w = new PawWatch();
            w.SetRules(4, 500);
            at = FirstCat(w, held, 3000);
            Check(at >= 1500 && at < 1550, "set to 0.5 s, four keys held 0.7 s locked at " + at + " ms, not at 1500");

            // 3 keys: two game keys held together (W, then A) do look like a cat, as Options warns
            var game = new List<int[]>();
            Press(game, 1000, 'W', 3000);
            Press(game, 1200, 'A', 3000);
            w = new PawWatch();
            w.SetRules(3, 250);
            Check(FirstCat(w, game, 5000) >= 0, "set to 3 keys, W and A held together didn't lock");
            Check(NoCat(game, 5000), "W and A held together lock with the default numbers");

            // numbers not on offer: the defaults
            w = new PawWatch();
            w.SetRules(2, 1000);
            Check(w.PileKeys == 4 && w.PawKeys == 3 && w.HoldMs == 250, "numbers not on offer were taken");
        }

        static void LiveAutoUnlock()
        {
            try { LiveAutoUnlockSteps(); }
            finally { LetGoOfTheTestKeys(); }
        }

        static void LiveAutoUnlockSteps()
        {
            if (IntPtr.Size != 8) throw new SkipException("needs a 64-bit run");
            var gate = new KeyGate();
            var changes = new List<bool>();
            string by = null;
            using (var behind = new Observer())
            using (var hook = new KeyHook(gate, true))
            {
                hook.TickMs = 50;
                hook.ConfirmMs = 300;
                hook.Word = "rukus";
                hook.LockChanged += locked => { lock (changes) changes.Add(locked); };
                hook.Unlocking += how => by = how;
                hook.Rules(400, 0, false, false, false, PawWatch.DefaultKeys, PawWatch.DefaultHoldMs);
                hook.Lock();
                WaitFor(() => Changes(changes) == "L", "it didn't lock");
                // nothing touches it: it stays locked well past the wait
                Thread.Sleep(Math.Max(900, gate.HeldGraceMs + 100));
                Check(Changes(changes) == "L", "a lock nothing touched unlocked by itself");
                // the cat lands on F24 and lies there (a sent key doesn't repeat: down, and silent)
                if (!Send(F24, false)) throw new SkipException("Windows won't take sent keys here (no desktop to type on?)");
                WaitFor(() => Volatile.Read(ref gate.Blocked) >= 1, "F24 wasn't blocked");
                Thread.Sleep(900);
                Check(Changes(changes) == "L", "it unlocked with a key still held down");
                // ...and goes
                int renewed = hook.Rehooks;
                var gone = Stopwatch.StartNew();
                Send(F24, true);
                WaitFor(() => Changes(changes) == "LU", "it didn't unlock by itself once the key was let go");
                // the 0.4 s wait, then a fresh hook that hears 0.3 s more of nothing
                Check(gone.ElapsedMilliseconds >= 650, "it unlocked " + gone.ElapsedMilliseconds + " ms after the key went up, before the wait and a fresh hook's listen were over");
                Check(hook.Rehooks > renewed, "it trusted the silence without putting a fresh hook in (a hook Windows took out hears nothing either)");
                Check(by == "itself", "the unlock was put down to \"" + by + "\"");
                Thread.Sleep(100);
                Check(behind.Downs(F24) == 0 && behind.Ups(F24) == 0, "the held key, or its release, got past");
                WaitFor(() => !hook.Installed, "the hook stayed in after unlocking by itself");
                Check(hook.QuietMsInForce == 400, "the hook has the wait as " + hook.QuietMsInForce + " ms");
            }
        }

        // Locker: the unlock note and the log say it unlocked by itself; the window names the
        // hotkey while it unlocks too
        static void LockerUnlocksByItself()
        {
            try { LockerUnlockSteps(); }
            finally { LetGoOfTheTestKeys(); }
        }

        static void LockerUnlockSteps()
        {
            if (IntPtr.Size != 8) throw new SkipException("needs a 64-bit run");
            // a hotkey of the tests' own (no keyboard has F22), so nothing real is taken
            var settings = new Settings { UnlockWord = "rukus", AutoUnlock = true, AutoUnlockSeconds = 30, HotkeyOn = true, HotkeyUnlocks = true, Hotkey = new Hotkey(Keys.F22, Keys.Control | Keys.Shift) };
            var gate = new KeyGate();
            var hook = new KeyHook(gate, true) { TickMs = 50, ConfirmMs = 300 };
            using (var locker = new Locker(settings, hook))
            using (var window = new MainForm(locker))
            {
                Pump(() => gate.UnlockComboKey == (int)Keys.F22 && hook.QuietMsInForce == 30000, "the hook didn't take the hotkey or the wait");
                hook.Rules(300, (int)Keys.F22, true, true, false, PawWatch.DefaultKeys, PawWatch.DefaultHoldMs);  // 0.3 s, not 30 s, for the test
                Pump(() => hook.QuietMsInForce == 300, "the hook didn't take the test's wait");
                locker.Lock(null, "tray menu");
                Pump(() => locker.IsLocked, "it didn't lock");
                Check(window.StateText.Contains("press Ctrl+Shift+F22"), "the window doesn't name the hotkey that unlocks: " + window.StateText);
                Check(locker.UnlockedBy == null, "a fresh lock still says how the last one ended");
                Thread.Sleep(gate.HeldGraceMs + 100);
                if (!Send(F24, false)) throw new SkipException("Windows won't take sent keys here");
                Send(F24, true);
                Pump(() => !locker.IsLocked, "it didn't unlock by itself");
                Check(locker.UnlockedBy == "itself", "the unlock was put down to \"" + locker.UnlockedBy + "\"");
                Pump(() => File.Exists(Log.FilePath) && File.ReadAllText(Log.FilePath).Contains("unlock by itself: nothing pressed for 30 seconds"),
                    "the log doesn't say it unlocked by itself");
                string[] note = Tray.LockNote(settings, false, locker.UnlockedBy, false, locker.Today);
                Check(note[0] == "Keyboard unlocked by itself" && note[1].StartsWith("No paws for 30 seconds.\n"), "the note says \"" + note[0] + " / " + note[1] + "\"");

                locker.Lock(null, "tray menu");
                Pump(() => locker.IsLocked, "it didn't lock again");
                Check(locker.UnlockedBy == null, "the new lock still says it unlocked by itself");
                locker.Unlock("tray menu");
                Pump(() => !locker.IsLocked, "it didn't unlock from the menu");
                Check(locker.UnlockedBy == "tray menu", "the menu's unlock was put down to \"" + locker.UnlockedBy + "\"");
                Check(Tray.LockNote(settings, false, locker.UnlockedBy, false, 0)[0] == "Keyboard unlocked", "the menu's unlock got the by-itself note");
            }
        }

        static void OptionsPages()
        {
            var settings = new Settings { UnlockWord = "rukus" };
            using (var locker = new Locker(settings, new KeyHook(new KeyGate(), true)))
            using (var options = new OptionsForm(locker))
            {
                string pages = string.Join(", ", options.PageKeys.ToArray());
                Check(pages == "locking, by itself, your cat, notes, look, starting", "the pages are " + pages);
                Check(options.Nav.Items.Count == 6 && options.CurrentPage == OptionsForm.LockingPage, "it doesn't open on Locking");
                // every option on its page
                var where = new Dictionary<Control, string>
                {
                    { options.HotkeyBox, "locking" }, { options.HotkeyUnlocksBox, "locking" }, { options.VolumeBox, "locking" }, { options.MouseBox, "locking" },
                    { options.IdleBox, "by itself" }, { options.IdleChoice(10), "by itself" }, { options.AutoUnlockBox, "by itself" }, { options.AutoUnlockChoice(60), "by itself" },
                    { options.CatNameBox, "your cat" }, { options.CatDetectionBox, "your cat" }, { options.CatKeysChoice(4), "your cat" }, { options.CatHoldChoice(250), "your cat" },
                    { options.NoteOnLockBox, "notes" }, { options.NoteOnKeysBox, "notes" }, { options.LookBox(Look.Dark), "look" }, { options.MinimizedBox, "starting" }
                };
                foreach (KeyValuePair<Control, string> pair in where)
                    Check(options.PageHolding(pair.Key) == pair.Value, "\"" + pair.Key.Text + "\" is on the page \"" + options.PageHolding(pair.Key) + "\", not \"" + pair.Value + "\"");
                // the list picks the page, and a page picked by name picks it in the list
                IntPtr made = options.Nav.Handle;
                options.Nav.SelectedIndex = 2;
                Check(options.CurrentPage == OptionsForm.CatPage, "picking Your cat in the list showed \"" + options.CurrentPage + "\"");
                options.ShowPage(OptionsForm.StartingPage);
                Check(options.CurrentPage == OptionsForm.StartingPage && options.Nav.SelectedIndex == 5, "showing Starting didn't pick it in the list");
                // Tab goes the way the window reads: the list, the page, the footer
                Check(options.Side.TabIndex < options.Pages.TabIndex && options.Pages.TabIndex < options.Footer.TabIndex, "Tab doesn't go list, page, footer");
                // each row of choices marks the one LoafLock starts with, and only that one, and starts on it
                foreach (RadioButton[] row in options.ChoiceRows)
                {
                    RadioButton[] marked = row.Where(r => r.Text.EndsWith(" (default)")).ToArray();
                    Check(marked.Length == 1 && marked[0].Checked, "the row " + string.Join(" · ", row.Select(r => r.Text).ToArray()) + " doesn't mark its default once, or doesn't start on it");
                }
                Check(options.IdleChoice(Idle.DefaultMinutes).Text.EndsWith("(default)") && options.AutoUnlockChoice(Settings.DefaultAutoUnlockSeconds).Text.EndsWith("(default)") &&
                    options.CatKeysChoice(PawWatch.DefaultKeys).Text.EndsWith("(default)") && options.CatHoldChoice(PawWatch.DefaultHoldMs).Text.EndsWith("(default)") &&
                    options.LookBox(Look.Windows).Text.EndsWith("(default)"), "a choice marked \"(default)\" isn't the default");
            }
        }

        static void OptionsUnlocking()
        {
            // a hotkey of the tests' own (no keyboard has F22), so nothing real is taken
            var settings = new Settings { UnlockWord = "rukus", HotkeyOn = true, Hotkey = new Hotkey(Keys.F22, Keys.Control | Keys.Shift) };
            var gate = new KeyGate();
            var hook = new KeyHook(gate, true);
            using (var locker = new Locker(settings, hook))
            using (var options = new OptionsForm(locker))
            {
                // It unlocks too: saved, said, and in the gate
                Check(!options.HotkeyUnlocksBox.Checked && options.HotkeyLineText.StartsWith("It only locks."), "It unlocks too starts ticked, or the line says " + options.HotkeyLineText);
                options.HotkeyUnlocksBox.Checked = true;
                Check(settings.HotkeyUnlocks && Settings.Load().HotkeyUnlocks, "ticking It unlocks too wasn't saved");
                Check(options.HotkeyLineText.StartsWith("Press it again to unlock:") && options.HotkeyLineText.Contains("Ctrl+Shift+F22"), "the line says " + options.HotkeyLineText);
                Pump(() => gate.UnlockComboKey == (int)Keys.F22, "the gate didn't get the hotkey to unlock with");
                options.HotkeyUnlocksBox.Checked = false;
                Pump(() => gate.UnlockComboKey == 0, "the gate kept the hotkey after untick");
                options.HotkeyUnlocksBox.Checked = true;
                Pump(() => gate.UnlockComboKey == (int)Keys.F22, "the gate didn't get the hotkey back");
                // a key on its own only locks
                options.HotkeyChanged(true, new Hotkey(Keys.F23, Keys.None));
                Check(options.HotkeyLineText.StartsWith("F23 on its own only locks"), "for F23 on its own the line says " + options.HotkeyLineText);
                Pump(() => gate.UnlockComboKey == 0, "a key on its own reached the gate to unlock with");
                options.HotkeyChanged(false, new Hotkey(Keys.F23, Keys.None));
                Check(options.HotkeyLineText == "It unlocks too once Lock with a hotkey is ticked.", "with the hotkey off, the line says " + options.HotkeyLineText);
                // the hotkey picker no longer says it only ever locks
                using (var picker = new HotkeyForm(settings.Hotkey))
                    Check(!TextOf(picker).Contains("It only locks"), "the hotkey picker still says the hotkey only locks the keyboard");

                // the heads-up: locking by itself and unlocking by itself, both on
                Check(!options.HeadsUpShown, "the heads-up shows with neither on");
                options.IdleBox.Checked = true;
                Check(!options.HeadsUpShown, "the heads-up shows with only locking by itself on");
                options.AutoUnlockBox.Checked = true;
                Check(options.HeadsUpShown && options.HeadsUpText.Contains("another 10 minutes"), "the heads-up didn't show with both on, or says: " + options.HeadsUpText);
                options.IdleChoice(30).Checked = true;
                Check(options.HeadsUpText.Contains("another 30 minutes"), "the heads-up didn't follow the minutes: " + options.HeadsUpText);
                options.IdleBox.Checked = false;
                Check(!options.HeadsUpShown, "the heads-up stayed with locking by itself turned off");
                options.CatDetectionBox.Checked = true;
                locker.SettingsChanged();  // Options fills itself in again, heads-up and all
                Check(!options.HeadsUpShown, "cat detection with the auto unlock got the heads-up (they go together)");
                options.CatDetectionBox.Checked = false;

                // the auto unlock's wait and cat detection's numbers: the settings, the file, the hook
                Check(settings.AutoUnlock && Settings.Load().AutoUnlock, "ticking the auto unlock wasn't saved");
                options.AutoUnlockChoice(120).Checked = true;
                Check(settings.AutoUnlockSeconds == 120 && Settings.Load().AutoUnlockSeconds == 120, "the wait wasn't saved");
                Pump(() => hook.QuietMsInForce == 120000, "the hook didn't take the 2-minute wait");
                options.AutoUnlockBox.Checked = false;
                Pump(() => hook.QuietMsInForce == 0, "the hook kept the wait with the auto unlock off");
                options.CatKeysChoice(5).Checked = true;
                options.CatHoldChoice(500).Checked = true;
                Settings saved = Settings.Load();
                Check(settings.CatKeys == 5 && settings.CatHoldMs == 500 && saved.CatKeys == 5 && saved.CatHoldMs == 500, "cat detection's numbers weren't saved");
                Pump(() => hook.CatKeysInForce == 5 && hook.CatHoldMsInForce == 500, "the hook didn't take cat detection's numbers");
                Check(options.CatRulesText.StartsWith("Locks when 5 keys stay down together for 0.5 s, or 4 that land at once"), "the line says: " + options.CatRulesText);
                Check(options.CatRulesText.Contains("senses only 3 keys") && options.CatRulesText.Contains("holding four keys"), "the keyboard line didn't follow the keys: " + options.CatRulesText);
                Pump(() => File.Exists(Log.FilePath) && File.ReadAllText(Log.FilePath).Contains("cat detection: 5 keys, held 0.5 s"), "the log doesn't give cat detection's numbers");
                options.CatKeysChoice(3).Checked = true;
                Check(options.CatRulesText.Contains("W and A"), "3 keys doesn't warn about two game keys: " + options.CatRulesText);
                options.CatKeysChoice(4).Checked = true;
                options.CatHoldChoice(250).Checked = true;
                Pump(() => File.ReadAllText(Log.FilePath).Contains("cat detection: 4 keys, held 0.25 s (the defaults)"), "the log doesn't say the numbers are back to the defaults");
            }
        }

        // every bit of text a window shows, its children's too
        static string TextOf(Control control)
        {
            var all = new System.Text.StringBuilder(control.Text);
            foreach (Control child in control.Controls) all.Append(' ').Append(TextOf(child));
            return all.ToString();
        }

        // every note fits in the corner, with a long name and a long hotkey; the hotkey is named
        // only while it unlocks
        static void NotesFit()
        {
            using (var popup = new Popup())
                foreach (string name in new[] { "", "Biscuit", "Sir Whiskers McFluffton" })
                    foreach (bool hotkey in new[] { false, true })
                    {
                        var s = new Settings { CatName = name, HotkeyOn = hotkey, HotkeyUnlocks = hotkey, Hotkey = new Hotkey(Keys.PageDown, Keys.Control | Keys.Shift), IdleMinutes = 60, AutoUnlockSeconds = 120 };
                        string what = (name == "" ? "no name" : name) + (hotkey ? ", with the hotkey" : "");
                        foreach (string by in new[] { "tray menu", "idle", "cat" })
                            foreach (bool mouse in new[] { false, true })
                            {
                                string[] note = Tray.LockNote(s, true, by, mouse, 12345);
                                Check(popup.Fits(note[0], note[1]), "the note \"" + note[0] + " / " + note[1] + "\" doesn't fit (" + what + ")");
                            }
                        foreach (string by in new[] { null, "itself" })
                        {
                            string[] note = Tray.LockNote(s, false, by, false, 12345);
                            Check(popup.Fits(note[0], note[1]), "the note \"" + note[0] + " / " + note[1] + "\" doesn't fit (" + what + ")");
                        }
                        Check(popup.Fits("Keyboard and mouse are locked", Tray.KeyNoteText(s, true, 12345)), "the note on a blocked key doesn't fit (" + what + ")");
                    }
            Check(Tray.HowBack(new Settings(), false) == "Type your unlock word to get it back.", "the plain note changed: " + Tray.HowBack(new Settings(), false));
            var keyed = new Settings { HotkeyOn = true, HotkeyUnlocks = true };
            Check(Tray.HowBack(keyed, true) == "Type your unlock word or press Ctrl+Shift+L.", "the note doesn't name the hotkey: " + Tray.HowBack(keyed, true));
            keyed.Hotkey = new Hotkey(Keys.Pause, Keys.None);
            Check(Tray.HowBack(keyed, false) == "Type your unlock word to get it back.", "the note names Pause, which only locks");
        }
    }
}
