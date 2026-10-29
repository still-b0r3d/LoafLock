namespace LoafLock
{
    // A long loaf: while locked, keys kept being blocked, with no pause longer than GapMs, for
    // LoafMs. A cat lying on the keyboard keeps a key repeating (Windows repeats the last one
    // pressed), so the paws never stop coming. Says so once per loaf. No figures are kept:
    // stats stay to a minimum.
    sealed class LoafWatch
    {
        public int LoafMs = 30000;
        public int GapMs = 2500;  // longer than the slowest repeat (Filter Keys can space them 2 s)

        bool loafing, told;
        int started, last;

        // now: the clock (ms); blocked: keys were blocked since the last look. True when this
        // loaf has just gone on long enough.
        public bool Tick(int now, bool blocked)
        {
            if (blocked)
            {
                if (!loafing || now - last > GapMs)
                {
                    loafing = true;
                    told = false;
                    started = now;
                }
                last = now;
            }
            else if (loafing && now - last > GapMs) loafing = false;
            if (!loafing || told || now - started < LoafMs) return false;
            told = true;
            return true;
        }

        // a loaf long enough to have been told, still going
        public bool Told { get { return loafing && told; } }

        public void Reset()
        {
            loafing = told = false;
        }
    }
}
