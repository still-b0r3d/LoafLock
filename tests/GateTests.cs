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

        static KeyGate Locked(string word)
        {
            var g = new KeyGate();
            g.SetWord(word);
            g.Lock();
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
            Check(!Up(g, 'A'), "a release was dropped");
            Check(Down(g, 'B'), "a press got through");
            for (int i = 0; i < 5; i++) Check(Down(g, 'B'), "a repeat got through");
            Up(g, 'B');
            Check(Down(g, Shift), "Shift got through");
            Up(g, Shift);
            Check(g.Blocked == 8, "blocked " + g.Blocked + " presses, expected 8 (a held key keeps counting)");
            Check(g.Locked, "it unlocked by itself");
        }

        // A press LoafLock never saw went through: the cat was already lying on J, or the hook was
        // a moment late, or J went to an admin window. Windows has J down, so J's release has to
        // reach it, or J stays stuck down.
        static void GateNeverDropsARelease()
        {
            KeyGate g = Locked("rukus");
            Check(Down(g, 'J') && Down(g, 'J') && Down(g, 'J'), "repeats of a key LoafLock saw only as repeats got through");
            Check(!Up(g, 'J'), "the release of a press that slipped past was dropped (J would stay stuck down)");
            Check(g.Blocked == 3, "blocked " + g.Blocked + ", expected 3");
            Check(Down(g, Ctrl), "Ctrl got through");
            g.Unlock();
            Check(!Up(g, Ctrl), "a release after unlocking was dropped");
            Check(!g.Busy, "still busy with every key up");
        }

        static void GateHeldAcrossUnlock()
        {
            KeyGate g = Locked("rukus");
            Check(Down(g, 'J'), "a press got through");
            g.Unlock();
            Check(g.Busy, "not busy with J still held");
            Check(Down(g, 'J') && Down(g, 'J'), "repeats of a key pressed while locked got through after unlocking");
            Check(!Up(g, 'J'), "its release was dropped");
            Check(!g.Busy, "still busy with every key up");
            Check(!Down(g, 'J') && !Up(g, 'J'), "a new press after unlocking was dropped");
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
            Check(!Up(g, 'S'), "the last letter's release was dropped");
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
