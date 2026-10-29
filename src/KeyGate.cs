namespace LoafLock
{
    // Decides, key by key, which presses LoafLock drops. No Windows calls, so the tests can drive
    // it one key at a time; in the app only the hook thread touches it. Mouse buttons come through
    // here too (as their key codes, 1 to 6) when the mouse is locked.
    //
    //   locked:      every press is dropped, and so are its repeats, even ones that come after
    //                unlocking, until the key is let go. A cat still lying on a key when you
    //                unlock doesn't start it repeating into YouTube.
    //   releases:    a release goes with its press. When LoafLock dropped the press, it drops the
    //                release too: the app never saw the key go down, and some act when a key
    //                comes up (YouTube plays and pauses on Space's release). Every other release
    //                goes through, because dropping one whose press got past LoafLock would leave
    //                the key stuck down. So releases still go through for:
    //                - Shift, Ctrl, Alt, Windows and Caps Lock, always. Their lone release does
    //                  nothing, and stuck down they'd change every click and key.
    //                - a key first seen just after locking (or after the hook was renewed): it
    //                  may have been held since before, and what LoafLock saw was its first
    //                  repeat. A held key repeats within HeldGraceMs, so later ones are new.
    //                - a key that turns out to have been repeating already: pressed again
    //                  sooner than a new press would start repeating (RepeatSplitMs). Its first
    //                  press got past LoafLock (Windows gave up waiting for the hook, or it
    //                  went to an admin window).
    //                - a press that reached LoafLock late (LateMs, or Unpair): Windows may have
    //                  given up waiting for the hook and passed it on.
    //   volume:      with PassVolume, Mute and Volume Down / Up go straight through, as if
    //                LoafLock weren't there.
    //   the word:    fresh presses of its letters, in order, unlock. Repeats of a held key and
    //                Shift, Ctrl, Alt, Windows and Caps Lock don't count; any other key starts
    //                the word over. Mouse buttons leave it alone.
    //   lost keys:   Windows sometimes never sends a release (Ctrl+Alt+Del's screen, locking
    //                the PC). Once unlocked, a key silent for longer than the slowest repeat
    //                Windows is set to (LostReleaseMs) isn't held any more. Not while locked:
    //                everything's dropped then anyway, and Windows repeats only the last key
    //                pressed, so a key under a paw goes quiet while the cat lies on another.
    //                A key pressed again after a long silence (a mouse button: pressed again at
    //                all) had its release lost, and counts as a new press.
    sealed class KeyGate
    {
        public int LostReleaseMs = 1500;
        public int HeldGraceMs = 600;  // the longest a held key waits before repeating, and a little more
        public int RepeatSplitMs;      // between a held key's repeat gap and its first wait; 0: can't tell them apart
        public const int LateMs = 150;

        // Mute, Volume Down and Volume Up go through while locked (an option, set at each lock)
        public bool PassVolume;

        readonly bool[] down = new bool[256];      // down right now, as far as the gate has seen
        readonly bool[] dropping = new bool[256];  // its press was dropped: so are its repeats
        readonly bool[] paired = new bool[256];    // ...and its release, since its press surely never got through
        readonly bool[] repeated = new bool[256];  // a repeat of it has been seen since it was first dropped
        readonly int[] seen = new int[256];        // when the key last did anything (ms, Windows' clock)
        int pending;                               // keys with dropping set
        int watchedSince;                          // when LoafLock last locked or renewed its hook
        int[] word = new int[0], fallback = new int[0];  // the word's letters as key codes, and where a near miss picks up
        int matched;                               // letters of the word typed so far

        public bool Locked { get; private set; }

        // presses dropped while locked, all told; a key held down keeps counting with every
        // repeat. Only the hook thread writes it; others read it with Volatile.Read.
        public int Blocked;

        // the hook is still needed: locked, or a dropped press isn't released yet
        public bool Busy { get { return Locked || pending > 0; } }

        public void SetWord(string text)
        {
            text = (text ?? "").ToUpperInvariant();
            var letters = new int[text.Length];
            for (int i = 0; i < letters.Length; i++) letters[i] = text[i];  // 'A'..'Z' are their own key codes
            // for each place in the word, how much of what was typed still counts when the next
            // letter is wrong: with the word "nanas", typing "nanan" keeps the last "nan"
            var back = new int[letters.Length];
            for (int i = 1, k = 0; i < letters.Length; i++)
            {
                while (k > 0 && letters[i] != letters[k]) k = back[k - 1];
                if (letters[i] == letters[k]) k++;
                back[i] = k;
            }
            word = letters;
            fallback = back;
            matched = 0;
        }

        // now: Windows' clock (ms)
        public void Lock(int now)
        {
            // keys LoafLock isn't dropping went by while it wasn't looking, so it can't know
            // they're down: a key the cat was already lying on just counts as a fresh press
            for (int vk = 0; vk < 256; vk++) down[vk] = dropping[vk];
            watchedSince = now;
            matched = 0;
            Locked = true;
        }

        // the hook was installed afresh: another program's hook may have been ahead of it until
        // now, and seen presses LoafLock didn't
        public void Rehooked(int now)
        {
            watchedSince = now;
        }

        public void Unlock()
        {
            Locked = false;
            matched = 0;
        }

        // One key event; true means drop it. time: when it happened (ms, Windows' clock); late:
        // how long it took to reach the hook (ms).
        public bool OnKey(int vk, bool pressed, int time, int late = 0)
        {
            if (vk < 0 || vk > 255) return false;
            // let through: not dropped, not counted, and the word doesn't see them (one held from
            // before the option was ticked finishes the old way)
            if (PassVolume && IsVolume(vk) && !dropping[vk]) return false;
            bool repeat = pressed && down[vk];
            int gap = time - seen[vk];
            // "still down", but silent too long (or a mouse button, which never repeats, pressed
            // again): its release got lost
            if (repeat && (IsMouse(vk) || gap > LostReleaseMs))
            {
                Forget(vk);
                repeat = false;
            }
            seen[vk] = time;
            down[vk] = pressed;

            if (!pressed)
            {
                bool drop = dropping[vk] && paired[vk];
                if (dropping[vk])
                {
                    dropping[vk] = paired[vk] = false;
                    pending--;
                }
                return drop;
            }
            if (dropping[vk])
            {
                // its first repeat: one that comes this quickly means it was repeating already
                if (!repeated[vk] && gap < RepeatSplitMs) paired[vk] = false;
                repeated[vk] = true;
                if (Locked) Blocked++;  // held since it was pressed while locked
                return true;
            }
            if (!Locked) return false;
            dropping[vk] = true;
            repeated[vk] = false;
            // a mouse button never repeats, so a press seen is a new press
            paired[vk] = late < LateMs && !IsModifier(vk) && (IsMouse(vk) || time - watchedSince > HeldGraceMs);
            pending++;
            Blocked++;
            if (!repeat) Letter(vk);
            return true;
        }

        // the press just dropped took long enough that Windows may have passed it on anyway: its
        // release goes through
        public void Unpair(int vk)
        {
            if (vk >= 0 && vk < 256) paired[vk] = false;
        }

        // the mouse hook is gone (the PC was locked while the mouse was too): releases of the
        // buttons it dropped won't reach the gate now
        public void ForgetMouse()
        {
            for (int vk = 0; vk < 256; vk++)
                if (IsMouse(vk)) Forget(vk);
        }

        // once unlocked, releases that never came: a key silent this long isn't held any more
        public void Expire(int now)
        {
            if (Locked) return;
            for (int vk = 0; vk < 256; vk++)
                if (dropping[vk] && now - seen[vk] > LostReleaseMs) Forget(vk);
        }

        void Forget(int vk)
        {
            if (dropping[vk])
            {
                dropping[vk] = paired[vk] = false;
                pending--;
            }
            down[vk] = false;
        }

        void Letter(int vk)
        {
            if (word.Length == 0 || IsModifier(vk) || IsMouse(vk)) return;
            if (vk < 'A' || vk > 'Z')
            {
                matched = 0;
                return;
            }
            while (matched > 0 && word[matched] != vk) matched = fallback[matched - 1];
            if (word[matched] == vk) matched++;
            if (matched == word.Length) Unlock();
        }

        static bool IsModifier(int vk)
        {
            switch (vk)
            {
                case 0x10: case 0x11: case 0x12: case 0x14:                         // Shift, Ctrl, Alt, Caps Lock
                case 0x5B: case 0x5C:                                               // the Windows keys
                case 0xA0: case 0xA1: case 0xA2: case 0xA3: case 0xA4: case 0xA5:  // left and right Shift, Ctrl, Alt
                    return true;
            }
            return false;
        }

        // Mute, Volume Down, Volume Up (a volume knob sends these too)
        public static bool IsVolume(int vk)
        {
            return vk == 0xAD || vk == 0xAE || vk == 0xAF;
        }

        // the mouse buttons' key codes: left, right, (3 is Ctrl+Break), middle, back, forward
        public static bool IsMouse(int vk)
        {
            return vk == 1 || vk == 2 || vk == 4 || vk == 5 || vk == 6;
        }
    }
}
