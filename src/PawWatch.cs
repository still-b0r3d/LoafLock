namespace LoafLock
{
    // Cat detection (experimental, an option): whether the keys down right now look like a cat
    // rather than someone typing or playing. No Windows calls, so the tests can drive it one key
    // at a time; in the app only the hook thread touches it. All it keeps is which ordinary keys
    // are down and how long they've been down, and it says "cat" for:
    //   a pile:    PileKeys down together HoldMs after the last of them (a cat lying on it),
    //   a paw:     PawKeys that went down within PawMs of each other, still down HoldMs on,
    //   a settle:  PawKeys down together SettleMs after the last of them (a cat lying down a key
    //              at a time, on a keyboard that senses only 3 keys at once),
    // where the keys of a pile or a settle went down within BurstMs of each other. A cat puts
    // its keys down in a burst, so a gamer holding W+A who adds push-to-talk seconds later isn't
    // one. Three game keys pressed within a second and held (W, then Space and D) still are, and
    // so is a standing diagonal jump (W, A and Space at once): Options and the README say so.
    // Shift, Ctrl, Alt, the Windows keys and Caps Lock never count, so a shortcut can't. Typing
    // overlaps 2 or 3 keys for a split second, and a fast roll of keys is let go within HoldMs.
    // A keyboard that senses only 2 keys at once (some cheap ones and laptops) can never show it
    // a cat.
    sealed class PawWatch
    {
        public int PileKeys = 4, PawKeys = 3;
        public int PawMs = 40, HoldMs = 100, SettleMs = 1500, BurstMs = 1000;
        // a key silent this long stops counting: its release may never have come (Ctrl+Alt+Del's
        // screen, locking the PC). A key under a resting paw goes quiet too, since Windows repeats
        // only the last key pressed, but every rule has fired long before then.
        public int StaleMs = 5000;

        readonly bool[] down = new bool[256];
        readonly bool[] left = new bool[256];  // held since before it could count (across an unlock): left out until let go
        readonly int[] since = new int[256];   // when it went down (ms, Windows' clock)
        readonly int[] heard = new int[256];   // when it last did anything: went down or repeated
        readonly int[] ages = new int[256];    // scratch: how long each key that counts has been down, longest first

        // how the last cat was spotted, for the log: "5 keys down at once"
        public string Why { get; private set; }

        // One key event; time: when it happened (ms, Windows' clock). True for a fresh press of a
        // key that counts, the only thing that can bring a cat sooner.
        public bool OnKey(int vk, bool pressed, int time)
        {
            if (vk < 0 || vk > 255 || KeyGate.IsModifier(vk) || KeyGate.IsMouse(vk)) return false;
            if (!pressed)
            {
                // forgotten as it comes up: no times kept that could say what was typed in what order
                down[vk] = left[vk] = false;
                since[vk] = heard[vk] = 0;
                return false;
            }
            // a repeat, unless the key was silent so long that its release must have got lost
            if (down[vk] && time - heard[vk] <= StaleMs)
            {
                heard[vk] = time;
                return false;
            }
            down[vk] = true;
            left[vk] = false;
            since[vk] = heard[vk] = time;
            return true;
        }

        // the keys down now don't count until they're let go: after an unlock the cat may still be
        // lying on them, and it takes fresh paws to lock again
        public void LeaveHeld()
        {
            for (int vk = 0; vk < 256; vk++)
                if (down[vk]) left[vk] = true;
        }

        // every key forgotten: after sleep or signing back in, when releases may have gone missing
        public void Clear()
        {
            for (int vk = 0; vk < 256; vk++) down[vk] = left[vk] = false;
        }

        // true when the keys down at now look like a cat; Why then says how
        public bool IsCat(int now)
        {
            int n = Count(now);
            for (int i = 0; i + PileKeys <= n; i++)
                if (ages[i] - ages[i + PileKeys - 1] <= BurstMs && ages[i + PileKeys - 1] >= HoldMs)
                    return Spotted(n + " keys down at once");
            for (int i = 0; i + PawKeys <= n; i++)
            {
                int spread = ages[i] - ages[i + PawKeys - 1];
                if (spread <= PawMs && ages[i + PawKeys - 1] >= HoldMs)
                    return Spotted(PawKeys + " keys down within " + spread + " ms of each other");
            }
            for (int i = 0; i + PawKeys <= n; i++)
                if (ages[i] - ages[i + PawKeys - 1] <= BurstMs && ages[i + PawKeys - 1] >= SettleMs)
                {
                    int tenths = (ages[i + PawKeys - 1] + 50) / 100;
                    return Spotted(PawKeys + " keys held down for " + tenths / 10 + "." + tenths % 10 + " s");
                }
            return false;
        }

        // how long until IsCat could say yes if no key goes up or down meanwhile (ms), or -1 when
        // it can't with the keys down now
        public int NextLook(int now)
        {
            int n = Count(now), wait = -1;
            for (int i = 0; i + PileKeys <= n; i++)
                if (ages[i] - ages[i + PileKeys - 1] <= BurstMs) wait = Sooner(wait, HoldMs - ages[i + PileKeys - 1]);
            for (int i = 0; i + PawKeys <= n; i++)
            {
                int spread = ages[i] - ages[i + PawKeys - 1];
                if (spread <= PawMs) wait = Sooner(wait, HoldMs - ages[i + PawKeys - 1]);
                if (spread <= BurstMs) wait = Sooner(wait, SettleMs - ages[i + PawKeys - 1]);
            }
            return wait;
        }

        // fills ages with how long each key that counts has been down, longest first (keys sorted
        // by age, not by time, so Windows' clock wrapping round after 49 days doesn't matter);
        // returns how many there are
        int Count(int now)
        {
            int n = 0;
            for (int vk = 0; vk < 256; vk++)
            {
                if (!down[vk] || left[vk] || now - heard[vk] > StaleMs) continue;
                int age = now - since[vk], at = n++;
                while (at > 0 && ages[at - 1] < age)
                {
                    ages[at] = ages[at - 1];
                    at--;
                }
                ages[at] = age;
            }
            return n;
        }

        bool Spotted(string why)
        {
            Why = why;
            return true;
        }

        static int Sooner(int wait, int ms)
        {
            if (ms < 0) ms = 0;
            return wait < 0 || ms < wait ? ms : wait;
        }
    }
}
