namespace LoafLock
{
    // Decides, key by key, which presses LoafLock drops. No Windows calls, so the tests can drive
    // it one key at a time; in the app only the hook thread touches it.
    //
    //   locked:      every press is dropped, and so are its repeats, even ones that come after
    //                unlocking, until the key is let go. A cat still lying on a key when you
    //                unlock doesn't start it repeating into YouTube.
    //   releases:    always go through. A release can't leave anything stuck down; dropping one
    //                can, whenever a press slipped past LoafLock (the hook was a moment late,
    //                or the key went to an admin window), and Windows would then think the key
    //                is held, Ctrl and Shift included.
    //   the word:    fresh presses of its letters, in order, unlock. Repeats of a held key and
    //                Shift, Ctrl, Alt, Windows and Caps Lock don't count; any other key starts
    //                the word over.
    //   lost keys:   Windows sometimes never sends a release (Ctrl+Alt+Del's screen, locking
    //                the PC). A held key repeats, so one silent for longer than the slowest
    //                repeat Windows is set to (LostReleaseMs) isn't held any more.
    sealed class KeyGate
    {
        public int LostReleaseMs = 1500;

        readonly bool[] down = new bool[256];      // down right now, as far as the gate has seen
        readonly bool[] dropping = new bool[256];  // its press was dropped: so are its repeats
        readonly int[] seen = new int[256];        // when the key last did anything (ms, Windows' clock)
        int pending;                               // keys with dropping set
        int[] word = new int[0], fallback = new int[0];  // the word's letters as key codes, and where a near miss picks up
        int matched;                               // letters of the word typed so far

        public bool Locked { get; private set; }

        // fresh presses dropped while locked, all told. Only the hook thread writes it; others
        // read it with Volatile.Read.
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

        public void Lock()
        {
            // keys LoafLock isn't dropping went by while it wasn't looking, so it can't know
            // they're down: a key the cat was already lying on just counts as a fresh press
            for (int vk = 0; vk < 256; vk++) down[vk] = dropping[vk];
            matched = 0;
            Locked = true;
        }

        public void Unlock()
        {
            Locked = false;
            matched = 0;
        }

        // One key event; true means drop it. time: when it happened (ms, Windows' clock).
        public bool OnKey(int vk, bool pressed, int time)
        {
            if (vk < 0 || vk > 255) return false;
            bool repeat = pressed && down[vk];
            if (repeat && time - seen[vk] > LostReleaseMs)
            {
                Forget(vk);  // "still down", but silent too long: its release got lost
                repeat = false;
            }
            seen[vk] = time;
            down[vk] = pressed;

            if (!pressed)
            {
                if (dropping[vk])
                {
                    dropping[vk] = false;
                    pending--;
                }
                return false;
            }
            if (dropping[vk]) return true;  // held since it was pressed while locked
            if (!Locked) return false;
            dropping[vk] = true;
            pending++;
            if (!repeat)
            {
                Blocked++;
                Letter(vk);
            }
            return true;
        }

        // releases that never came: a key silent this long isn't held any more
        public void Expire(int now)
        {
            for (int vk = 0; vk < 256; vk++)
                if (dropping[vk] && now - seen[vk] > LostReleaseMs) Forget(vk);
        }

        void Forget(int vk)
        {
            if (dropping[vk])
            {
                dropping[vk] = false;
                pending--;
            }
            down[vk] = false;
        }

        void Letter(int vk)
        {
            if (word.Length == 0 || IsModifier(vk)) return;
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
    }
}
