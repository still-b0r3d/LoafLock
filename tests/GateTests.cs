using System.IO;

namespace LoafLock.Tests
{
    // KeyGate, one key event at a time, on a pretend clock. True from Down/Up means dropped.
    static partial class TestMain
    {
        const int Shift = 0xA0, Ctrl = 0xA2, Space = 0x20, Digit1 = 0x31;
        static int clock;

        static bool Down(KeyGate g, char key) { return Down(g, (int)key); }
        static bool Up(KeyGate g, char key) { return Up(g, (int)key); }
        static bool Down(KeyGate g, int vk) { clock += 40; return g.OnKey(vk, true, clock); }
        static bool Up(KeyGate g, int vk) { clock += 40; return g.OnKey(vk, false, clock); }

        static void Tap(KeyGate g, char key)
        {
            Down(g, key);
            Up(g, key);
        }

        static void Type(KeyGate g, string letters)
        {
            foreach (char c in letters) Tap(g, char.ToUpperInvariant(c));
        }

        // locked a while ago: every key pressed from now on is a new press
        static KeyGate Locked(string word)
        {
            KeyGate g = LockedJustNow(word);
            clock += g.HeldGraceMs + 100;
            return g;
        }

        // locked this moment: a key seen now may have been held since before
        static KeyGate LockedJustNow(string word)
        {
            var g = new KeyGate();
            g.SetWord(word);
            g.Lock(clock);
            return g;
        }

        static void GateUnlockedPasses()
        {
            var g = new KeyGate();
            g.SetWord("rukus");
            Check(!Down(g, 'A') && !Up(g, 'A'), "a key was dropped while unlocked");
            Type(g, "rukus");
            Check(!g.Locked && !g.Busy && g.Blocked == 0, "typing the word while unlocked did something");
        }

        static void GateDropsWhileLocked()
        {
            KeyGate g = Locked("rukus");
            Check(Down(g, 'A'), "a press got through");
            Check(Up(g, 'A'), "the release of a dropped press got through");
            Check(Down(g, 'B'), "a press got through");
            for (int i = 0; i < 5; i++) Check(Down(g, 'B'), "a repeat got through");
            Check(Up(g, 'B'), "the release of a held key got through");
            Check(Down(g, Shift), "Shift got through");
            Check(!Up(g, Shift), "Shift's release was dropped (stuck, it would change every click)");
            Check(Down(g, Ctrl) && !Up(g, Ctrl), "Ctrl got through, or its release was dropped");
            Check(g.Blocked == 9, "blocked " + g.Blocked + " presses, expected 9 (a held key keeps counting)");
            Check(g.Locked, "it unlocked by itself");
            g.Unlock();
            Check(!g.Busy, "still busy with every key up");
        }

        // YouTube plays and pauses on Space's release, not its press (holding Space plays at 2x),
        // so a lone release is a tap to it. Checked 2026-10-28 on a real YouTube page.
        static void GateDropsTheReleaseWithItsPress()
        {
            KeyGate g = Locked("rukus");
            Check(Down(g, Space) && Up(g, Space), "Space's press or release got through (YouTube would pause)");
            g.Unlock();
            Check(!g.Busy, "still busy with every key up");
            Check(!Down(g, Space) && !Up(g, Space), "Space was dropped after unlocking");
        }

        // A press LoafLock never saw went through: the cat was already lying on J when it locked.
        // What it sees is J's repeats, as if J were a new press. Windows has J down, so J's
        // release has to reach it, or J stays stuck down. A held key repeats within HeldGraceMs,
        // so a key first seen after that is a new press, whose release goes with it.
        static void GateKeyHeldFromBefore()
        {
            KeyGate g = LockedJustNow("rukus");
            Check(Down(g, 'J') && Down(g, 'J') && Down(g, 'J'), "repeats of a key LoafLock saw only as repeats got through");
            Check(!Up(g, 'J'), "the release of a key held since before the lock was dropped (J would stay stuck down)");
            Check(g.Blocked == 3, "blocked " + g.Blocked + ", expected 3");
            Check(Down(g, Ctrl), "Ctrl got through");
            g.Unlock();
            Check(!Up(g, Ctrl), "the release of a key seen just after locking was dropped");
            Check(!g.Busy, "still busy with every key up");

            g = LockedJustNow("rukus");
            clock += g.HeldGraceMs + 100;
            Check(Down(g, 'K') && Up(g, 'K'), "a key pressed well after locking: its press or release got through");
        }

        // a renewed hook may have had another program's hook ahead of it (a key remapper that
        // swallows J and sends its own key): it gives keys seen just after a renewal the same
        // benefit of the doubt
        static void GateAfterARehook()
        {
            KeyGate g = Locked("rukus");
            g.Rehooked(clock);
            Check(Down(g, 'J') && !Up(g, 'J'), "a key seen just after a renewal: its release was dropped");
            clock += g.HeldGraceMs + 100;
            Check(Down(g, 'J') && Up(g, 'J'), "a key pressed well after the renewal: its press or release got through");
        }

        // Windows passes a key on if the hook takes too long, so a press that reached LoafLock
        // late may have got through: its release goes through too
        static void GateLatePress()
        {
            KeyGate g = Locked("rukus");
            clock += 40;
            Check(g.OnKey('J', true, clock, KeyGate.LateMs + 50), "a late press got through");
            Check(!Up(g, 'J'), "a late press's release was dropped (J could stay stuck down)");
            clock += 40;
            Check(g.OnKey('K', true, clock, 20) && Up(g, 'K'), "an on-time press: its press or release got through");
        }

        // Windows passed a press on without the hook (it waited too long, or the key went to an
        // admin window), and LoafLock only sees the repeats: they come quicker than a new press
        // would start repeating, so the release goes through. Found by the 0.4 review with a
        // stalled hook.
        static void GateAlreadyRepeating()
        {
            KeyGate g = Locked("rukus");
            g.RepeatSplitMs = 266;  // a 500 ms wait, then 30 a second
            Check(Down(g, 'J') && Down(g, 'J') && Down(g, 'J'), "repeats of a key whose press got past got through");
            Check(!Up(g, 'J'), "the release of a key that was repeating already was dropped (J would stay stuck down)");
            Check(Down(g, 'K'), "a press got through");
            clock += 460;  // a new press: the wait, then the repeats
            Check(Down(g, 'K') && Down(g, 'K'), "a repeat got through");
            Check(Up(g, 'K'), "a new press's release got through");

            g = Locked("rukus");
            Check(Down(g, 'J'), "a press got through");
            g.Unpair('J');  // the hook was held up while judging it
            Check(!Up(g, 'J'), "the release of a press Windows may have passed on was dropped");
        }

        // Windows repeats only the last key pressed: with the cat on Space and then F, Space goes
        // quiet, but it's still held, and its release mustn't reach YouTube alone
        static void GateTwoPaws()
        {
            KeyGate g = Locked("rukus");
            Check(Down(g, Space), "Space got through");
            for (int i = 0; i < 70; i++)
            {
                Check(Down(g, 'F'), "F got through");
                if (i % 10 == 0) g.Expire(clock);
            }
            Check(Up(g, 'F'), "F's release got through");
            Check(Up(g, Space), "Space's release got through after a long quiet under the paw (YouTube would pause)");
            g.Unlock();
            Check(!g.Busy, "still busy with every key up");
        }

        static void GateHeldAcrossUnlock()
        {
            KeyGate g = Locked("rukus");
            Check(Down(g, 'J'), "a press got through");
            g.Unlock();
            Check(g.Busy, "not busy with J still held");
            Check(Down(g, 'J') && Down(g, 'J'), "repeats of a key pressed while locked got through after unlocking");
            Check(Up(g, 'J'), "its release got through, though its press never did");
            Check(!g.Busy, "still busy with every key up");
            Check(!Down(g, 'J') && !Up(g, 'J'), "a new press after unlocking was dropped");
        }

        // with the mouse locked too, its buttons go through the gate like keys
        static void GateMouseButtons()
        {
            const int Left = 1, Right = 2;
            KeyGate g = LockedJustNow("rukus");
            // a button never repeats, so even one seen just after locking is a new press
            Check(Down(g, Right) && Up(g, Right), "a right click got through (a lone release opens a menu)");
            Check(Down(g, Left), "a left click got through");
            clock += g.LostReleaseMs * 3;  // a cat lying on the button: it never repeats
            g.Expire(clock);
            Check(Up(g, Left), "the release of a button held down a long while got through");
            Type(g, "ruk");
            Check(Down(g, Left) && Up(g, Left), "a click got through");
            Type(g, "us");
            Check(!g.Locked, "a click in the middle of the word started it over");

            g = Locked("rukus");
            Check(Down(g, Left), "a left click got through");
            Check(Down(g, Left), "a second press, its release lost, got through while locked");
            g.Unlock();
            Check(!Down(g, Left), "after unlocking, a button whose release got lost is still dropped");
            Check(!Up(g, Left) && !g.Busy, "still busy after that");

            g = Locked("rukus");
            Check(Down(g, Left), "a left click got through");
            g.Unlock();
            clock += g.LostReleaseMs + 100;
            g.Expire(clock);
            Check(!g.Busy, "a button held across the unlock keeps the hook for good");

            g = Locked("rukus");
            Check(Down(g, Left), "a left click got through");
            g.ForgetMouse();
            Check(!Up(g, Left), "after letting go of the mouse, a button's release was dropped");
        }

        static void GateLostRelease()
        {
            KeyGate g = Locked("rukus");
            Check(Down(g, Ctrl), "Ctrl got through");  // Ctrl+Alt+Del: its screen swallows the release
            g.Unlock();
            Check(g.Busy, "not busy while Ctrl looks held");
            clock += g.LostReleaseMs + 100;
            Check(!Down(g, Ctrl), "Ctrl pressed again after its release got lost was dropped");
            Check(!Up(g, Ctrl) && !g.Busy, "still busy after that");

            g = Locked("rukus");
            Check(Down(g, 'X'), "X got through");
            g.Unlock();
            clock += g.LostReleaseMs + 100;
            g.Expire(clock);
            Check(!g.Busy, "a key silent past the limit still keeps the hook");
        }

        // Filter Keys can space repeats out to 2 seconds; LoafLock sets the limit from Windows'
        // settings, and a slow repeat mustn't pass for a new press
        static void GateSlowRepeats()
        {
            KeyGate g = Locked("aaaa");
            g.LostReleaseMs = 2500;
            Down(g, 'A');  // a cat lying on A, repeating every 2 seconds
            for (int i = 0; i < 5; i++)
            {
                clock += 1960;
                Check(Down(g, 'A'), "a slow repeat got through");
            }
            Check(g.Locked, "slow repeats were taken for new presses and typed the word");
            Check(g.Blocked == 6, "blocked " + g.Blocked + ", expected 6 (each repeat counts)");
            g.Unlock();
            clock += 1960;
            Check(Down(g, 'A'), "a slow repeat got through after unlocking");
        }

        static void WordUnlocks()
        {
            KeyGate g = Locked("rukus");
            Type(g, "ruku");
            Check(g.Locked, "unlocked before the last letter");
            Check(Down(g, 'S'), "the last letter of the word got through");
            Check(!g.Locked, "the word didn't unlock");
            Check(g.Busy, "not busy before the last letter is let go");
            Check(Up(g, 'S'), "the last letter's release got through, though its press never did");
            Check(!g.Busy, "still busy after the word");
            Check(!Down(g, 'A') && !Up(g, 'A'), "typing after the word was dropped");
        }

        static void WordWithAPawOnTheKeys()
        {
            KeyGate g = Locked("rukus");
            Down(g, Space);  // a paw resting on the space bar, repeating all the while
            foreach (char c in "RUKUS")
            {
                Down(g, Space);
                Tap(g, c);
                Down(g, Space);
            }
            Check(!g.Locked, "the word didn't unlock with a paw on the space bar");
        }

        static void WordWrongKeysAndShift()
        {
            KeyGate g = Locked("rukus");
            Type(g, "ruxkus");
            Check(g.Locked, "a wrong letter in the middle still unlocked");
            Type(g, "ru");
            Tap(g, (char)Digit1);
            Type(g, "kus");
            Check(g.Locked, "a number in the middle still unlocked");
            Down(g, Shift);
            Type(g, "RuKuS");
            Up(g, Shift);
            Check(!g.Locked, "Shift held while typing the word got in the way");
        }

        static void WordNearMisses()
        {
            KeyGate g = Locked("aab");
            Type(g, "aaab");
            Check(!g.Locked, "'aaab' didn't unlock 'aab'");
            g = Locked("nanas");
            Type(g, "nananas");
            Check(!g.Locked, "'nananas' didn't unlock 'nanas'");
        }

        static void WordNeedsFreshPresses()
        {
            KeyGate g = Locked("aaaa");
            Down(g, 'A');
            for (int i = 0; i < 20; i++) Down(g, 'A');  // a cat lying on A
            Up(g, 'A');
            Check(g.Locked, "a held key's repeats typed the word");
            g = Locked("");
            Type(g, "rukus");
            Check(g.Locked, "with no word set, something unlocked it");
        }

        static void SettingsRoundTrip()
        {
            var s = new Settings();
            Check(!s.HasWord, "a fresh settings has a word");
            Check(!s.LockMouse && s.CatName == "" && s.Look == Look.Windows, "the mouse, cat's name or look defaults are wrong");
            s.LockMouse = true;
            s.CatName = "Biscuit";
            s.Look = Look.Dark;
            s.Save();
            Settings back = Settings.Load();
            Check(back.LockMouse && back.CatName == "Biscuit" && back.Look == Look.Dark, "the mouse, cat's name or look didn't come back");
            Check(back.PawsBlocked == "Paws blocked by Biscuit" && new Settings().PawsBlocked == "Paws blocked", "the paws line is " + back.PawsBlocked);
            Check(Settings.CleanName("  Sir\tWhiskers  ") == "SirWhiskers" && Settings.CleanName(new string('x', 40)).Length == Settings.MaxNameLength,
                "a name with a tab or too many letters wasn't tidied");
            File.WriteAllText(Settings.FilePath, "look=purple\r\ncat_name= Rukus = loaf \r\n");
            back = Settings.Load();
            Check(back.Look == Look.Windows && back.CatName == "Rukus = loaf", "a hand-edited look or name was read wrong");
            s = new Settings();
            s.UnlockWord = "rukus";
            s.Save();
            Check(Settings.Load().UnlockWord == "rukus", "the word didn't come back");
            Check(Settings.WordProblem("Rukus") == null, "a good word was turned down");
            Check(Settings.WordProblem("cat") != null, "a 3-letter word was accepted");
            Check(Settings.WordProblem("ru kus") != null && Settings.WordProblem("ruku5") != null && Settings.WordProblem("rükus") != null,
                "a word with a space, a number or an accent was accepted");
            Check(Settings.WordProblem(new string('a', Settings.MaxWordLength + 1)) != null, "a too-long word was accepted");
            File.WriteAllText(Settings.FilePath, "unlock_word=ab1\r\n");
            Check(!Settings.Load().HasWord, "a bad word in settings.ini was used");
            File.WriteAllText(Settings.FilePath, "# a note\r\nUNLOCK_WORD = Loaf \r\n");
            Check(Settings.Load().UnlockWord == "loaf", "a hand-edited word wasn't read");
        }
    }
}
