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
    //                - a key down when the cat detector locked (LockWatched): it watched the key
    //                  go down, so it knows the press reached the app.
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
    //   the hotkey:  when it unlocks too (an option), a fresh press of its key with exactly its
    //                Ctrl / Shift / Alt down and nothing else at all, then that key let go with
    //                nothing else gone down meanwhile. A cat lying across the keyboard holds
    //                other keys too, and a paw landing on the combo and its neighbours puts
    //                them down before the key comes up, so it can't. Never a key on its own
    //                (Pause): one paw could press that.
    //   gone:        for the auto unlock (an option), Gone says when nothing's down and nothing
    //                has happened for a while, once this lock has blocked something (or the
    //                cat detector made it). A lock set before the cat arrives holds until it's
    //                been and gone.
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
        int comboKey;                              // the hotkey's key, while the hotkey unlocks too; else 0
        bool comboCtrl, comboShift, comboAlt;      // ...and which of Ctrl, Shift and Alt go with it
        int armed;                                 // the hotkey's key went down just so: it unlocks as it comes up, unless another key goes down first
        bool pawed;                                // this lock has blocked something, or the cat detector made it
        int lastStir;                              // when a key, a button or the locked mouse last did anything (ms)

        public bool Locked { get; private set; }

        // how the gate unlocked itself last: "word", "hotkey" or "itself"; null when it was told
        // to (from the window, the menu or the command line)
        public string UnlockedBy { get; private set; }

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

        // the hotkey's key while it unlocks too, else 0 (the tests read it)
        internal int UnlockComboKey { get { return comboKey; } }

        // The hotkey unlocks too: its key, and which of Ctrl, Shift and Alt go with it. A key of 0,
        // or one with none of them (Pause on its own), and it doesn't.
        public void SetUnlockHotkey(int key, bool ctrl, bool shift, bool alt)
        {
            bool held = ctrl || shift || alt;
            comboKey = held && key > 0 && key < 256 && !IsModifier(key) && !IsMouse(key) ? key : 0;
            comboCtrl = ctrl;
            comboShift = shift;
            comboAlt = alt;
        }

        // now: Windows' clock (ms)
        public void Lock(int now)
        {
            // keys LoafLock isn't dropping went by while it wasn't looking, so it can't know
            // they're down: a key the cat was already lying on just counts as a fresh press
            for (int vk = 0; vk < 256; vk++) down[vk] = dropping[vk];
            watchedSince = now;
            matched = 0;
            armed = 0;
            pawed = false;
            lastStir = now;
            UnlockedBy = null;
            Locked = true;
        }

        // Locked by the cat detector, which watched every key go by while unlocked. The keys down
        // now reached the app, so their repeats are dropped and their releases go through; every
        // other key is a fresh press, dropped along with its release. (A key first seen just
        // after the hook went in or was renewed may still have been held from before:
        // watchedSince stays where that put it.)
        public void LockWatched()
        {
            for (int vk = 0; vk < 256; vk++)
            {
                if (!down[vk] || dropping[vk]) continue;
                dropping[vk] = true;
                paired[vk] = repeated[vk] = false;
                pending++;
            }
            matched = 0;
            armed = 0;
            // the cat's paw made this lock: the auto unlock waits for it to go (lastStir is the
            // paw's last key, since the gate sees every key while watching)
            pawed = true;
            UnlockedBy = null;
            Locked = true;
        }

        // the hook went in to watch for a cat: keys went by while it wasn't looking, so it can't
        // know which are down
        public void StartWatching(int now)
        {
            for (int vk = 0; vk < 256; vk++) down[vk] = dropping[vk];
            watchedSince = now;
        }

        // the hook was installed afresh: another program's hook may have been ahead of it until
        // now, and seen presses LoafLock didn't
        public void Rehooked(int now)
        {
            watchedSince = now;
        }

        // told to unlock: from the window, the menu or the command line
        public void Unlock()
        {
            Unlock(null);
        }

        // by: "word", "hotkey", "itself", or null when told to
        public void Unlock(string by)
        {
            Locked = false;
            matched = 0;
            armed = 0;
            UnlockedBy = by;
        }

        // when a key, a button or the locked mouse last did anything (ms, Windows' clock)
        public int LastStir { get { return lastStir; } }

        // The auto unlock's question: locked, something was blocked (or the cat detector made the
        // lock), nothing's down, and nothing has happened for quietMs. now: Windows' clock (ms).
        public bool Gone(int now, int quietMs)
        {
            if (!Locked || !pawed || now - lastStir < quietMs) return false;
            for (int vk = 0; vk < 256; vk++)
                if (down[vk]) return false;
            return true;
        }

        // the mouse moved, or its wheel turned, while it's locked too: someone's still there
        public void Stir(int time)
        {
            lastStir = time;
        }

        // One key event; true means drop it. time: when it happened (ms, Windows' clock); late:
        // how long it took to reach the hook (ms).
        public bool OnKey(int vk, bool pressed, int time, int late = 0)
        {
            if (vk < 0 || vk > 255) return false;
            // let through: not dropped, not counted, and the word doesn't see them (one held from
            // before the option was ticked finishes the old way)
            if (PassVolume && IsVolume(vk) && !dropping[vk]) return false;
            lastStir = time;  // a press, a repeat or a release: something's still there
            bool repeat = pressed && down[vk];
            int gap = time - seen[vk];
            // "still down", but silent too long (or a mouse button, which never repeats, pressed
            // again): its release got lost
            if (repeat && (IsMouse(vk) || gap > LostReleaseMs))
            {
                Forget(vk);
                repeat = false;
            }
            seen[vk] = pressed ? time : 0;  // a key that's up keeps no time, so nothing here says what was typed when
            down[vk] = pressed;

            if (!pressed)
            {
                bool drop = dropping[vk] && paired[vk];
                if (dropping[vk])
                {
                    dropping[vk] = paired[vk] = false;
                    pending--;
                }
                // the hotkey, let go with nothing else gone down since it went down
                if (Locked && armed != 0 && vk == armed) Unlock("hotkey");
                return drop;
            }
            if (dropping[vk])
            {
                // its first repeat: one that comes this quickly means it was repeating already
                if (!repeated[vk] && gap < RepeatSplitMs) paired[vk] = false;
                repeated[vk] = true;
                if (Locked)
                {
                    Blocked++;  // held since it was pressed while locked
                    pawed = true;
                }
                return true;
            }
            if (!Locked) return false;
            dropping[vk] = true;
            repeated[vk] = false;
            // a mouse button never repeats, so a press seen is a new press
            paired[vk] = late < LateMs && !IsModifier(vk) && (IsMouse(vk) || time - watchedSince > HeldGraceMs);
            pending++;
            Blocked++;
            pawed = true;
            if (!repeat)
            {
                if (vk != armed) armed = 0;  // something else went down: not the hotkey on its own after all
                if (Combo(vk)) armed = vk;   // it unlocks as the key comes up
                else Letter(vk);
            }
            return true;
        }

        // the hotkey that unlocks too: its key, pressed with exactly its Ctrl / Shift / Alt down
        // (left or right) and nothing else down at all
        bool Combo(int vk)
        {
            if (comboKey == 0 || vk != comboKey) return false;
            bool ctrl = false, shift = false, alt = false;
            for (int k = 0; k < 256; k++)
            {
                if (!down[k] || k == vk) continue;
                switch (k)
                {
                    case 0x11: case 0xA2: case 0xA3: ctrl = true; break;   // Ctrl, left, right
                    case 0x10: case 0xA0: case 0xA1: shift = true; break;  // Shift
                    case 0x12: case 0xA4: case 0xA5: alt = true; break;    // Alt
                    default: return false;  // another key, Windows, Caps Lock or a mouse button
                }
            }
            return ctrl == comboCtrl && shift == comboShift && alt == comboAlt;
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
            if (matched == word.Length) Unlock("word");
        }

        // Shift, Ctrl, Alt, Caps Lock and the Windows keys
        public static bool IsModifier(int vk)
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
