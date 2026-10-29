using System;
using System.Globalization;
using System.IO;

namespace LoafLock
{
    // Settings live in %APPDATA%\LoafLock\settings.ini as key=value lines.
    class Settings
    {
        // the tests point this at a folder of their own
        public static string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LoafLock");

        public static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

        public const int MinWordLength = 4, MaxWordLength = 20;

        // typed while the keyboard is locked, it unlocks it: lowercase a-z, or "" until it's picked.
        // It's a cat filter, not a password, so it's kept as plain text.
        public string UnlockWord = "";

        // the note in the corner: when the keyboard locks or unlocks, and on every blocked key
        public bool NoteOnLock = true, NoteOnKeys;

        public bool StartMinimized;  // start as just Rukus in the taskbar corner, no window

        public bool LockMouse;  // lock the mouse too: the pointer, the buttons and the wheel

        public bool HotkeyOn;                     // lock with a hotkey (it never unlocks)
        public Hotkey Hotkey = Hotkey.Default;

        public bool VolumeThrough;  // Mute, Volume Down and Volume Up still work while locked

        // lock by itself after IdleMinutes with nobody at the keyboard or mouse
        public bool IdleLock;
        public int IdleMinutes = Idle.DefaultMinutes;

        // "Paws blocked by Biscuit": the cat's name, or "" for none
        public string CatName = "";
        public const int MaxNameLength = 24;

        public Look Look = Look.Windows;  // light or dark windows

        // paws blocked: all told, and on BlockedDay (yyyy-MM-dd)
        public int BlockedTotal, BlockedToday;
        public string BlockedDay = "";

        public bool HasWord { get { return UnlockWord.Length > 0; } }

        // "Paws blocked", or "Paws blocked by Biscuit"
        public string PawsBlocked { get { return CatName.Length > 0 ? "Paws blocked by " + CatName : "Paws blocked"; } }

        // a cat's name as LoafLock keeps it: one line, no spaces at the ends, not too long
        public static string CleanName(string name)
        {
            var kept = new System.Text.StringBuilder();
            foreach (char c in name ?? "")
                if (!char.IsControl(c)) kept.Append(c);
            string clean = kept.ToString().Trim();
            return clean.Length > MaxNameLength ? clean.Substring(0, MaxNameLength).TrimEnd() : clean;
        }

        public int TodayCount(DateTime now)
        {
            return BlockedDay == Day(now) ? BlockedToday : 0;
        }

        public void AddBlocked(int count, DateTime now)
        {
            string day = Day(now);
            if (BlockedDay != day)
            {
                BlockedDay = day;
                BlockedToday = 0;
            }
            BlockedToday += count;
            BlockedTotal += count;
        }

        static string Day(DateTime time)
        {
            return time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // what's wrong with a word as an unlock word, or null if it'll do
        public static string WordProblem(string word)
        {
            word = word ?? "";
            if (word.Length == 0) return "Type a word.";
            foreach (char c in word)
                if ((c < 'a' || c > 'z') && (c < 'A' || c > 'Z')) return "Letters A to Z only: no spaces, numbers or accents.";
            if (word.Length < MinWordLength) return "At least " + MinWordLength + " letters, so a cat can't hit it by luck.";
            if (word.Length > MaxWordLength) return "At most " + MaxWordLength + " letters.";
            return null;
        }

        public static Settings Load()
        {
            var s = new Settings();
            string[] lines;
            try
            {
                if (!File.Exists(FilePath)) return s;
                lines = File.ReadAllLines(FilePath);
            }
            catch (Exception e)
            {
                Log.Write("couldn't read settings.ini: " + e.Message);
                return s;
            }
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (line.StartsWith("#") || eq < 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();
                int number;
                DateTime day;
                switch (key)
                {
                    case "unlock_word":
                        if (WordProblem(value) == null) s.UnlockWord = value.ToLowerInvariant();
                        else if (value.Length > 0) Log.Write("settings: the unlock word in settings.ini isn't letters A to Z (4 to 20), so it was ignored");
                        break;
                    case "note_on_lock": s.NoteOnLock = !IsNo(value); break;
                    case "note_on_keys": s.NoteOnKeys = !IsNo(value); break;
                    case "start_minimized": s.StartMinimized = !IsNo(value); break;
                    case "lock_mouse": s.LockMouse = !IsNo(value); break;
                    case "hotkey_on": s.HotkeyOn = !IsNo(value); break;
                    case "hotkey":
                        Hotkey hotkey;
                        if (Hotkey.TryParse(value, out hotkey)) s.Hotkey = hotkey;
                        else Log.Write("settings: the hotkey in settings.ini isn't one LoafLock takes, so it was ignored");
                        break;
                    case "volume_through": s.VolumeThrough = !IsNo(value); break;
                    case "idle_lock": s.IdleLock = !IsNo(value); break;
                    case "idle_minutes": if (int.TryParse(value, out number) && Array.IndexOf(Idle.Choices, number) >= 0) s.IdleMinutes = number; break;
                    case "cat_name": s.CatName = CleanName(value); break;
                    case "look":
                        switch (value.ToLowerInvariant())
                        {
                            case "light": s.Look = Look.Light; break;
                            case "dark": s.Look = Look.Dark; break;
                            default: s.Look = Look.Windows; break;
                        }
                        break;
                    case "blocked_total": if (int.TryParse(value, out number) && number >= 0) s.BlockedTotal = number; break;
                    case "blocked_today": if (int.TryParse(value, out number) && number >= 0) s.BlockedToday = number; break;
                    case "blocked_day":
                        if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)) s.BlockedDay = value;
                        break;
                }
            }
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            string text =
                "# LoafLock settings. LoafLock writes this file.\r\n" +
                "unlock_word=" + UnlockWord + "\r\n" +
                "note_on_lock=" + (NoteOnLock ? "yes" : "no") + "\r\n" +
                "note_on_keys=" + (NoteOnKeys ? "yes" : "no") + "\r\n" +
                "start_minimized=" + (StartMinimized ? "yes" : "no") + "\r\n" +
                "lock_mouse=" + (LockMouse ? "yes" : "no") + "\r\n" +
                "hotkey_on=" + (HotkeyOn ? "yes" : "no") + "\r\n" +
                "hotkey=" + Hotkey + "\r\n" +
                "volume_through=" + (VolumeThrough ? "yes" : "no") + "\r\n" +
                "idle_lock=" + (IdleLock ? "yes" : "no") + "\r\n" +
                "idle_minutes=" + IdleMinutes + "\r\n" +
                "cat_name=" + CatName + "\r\n" +
                "look=" + Look.ToString().ToLowerInvariant() + "\r\n" +
                "blocked_total=" + BlockedTotal + "\r\n" +
                "blocked_today=" + BlockedToday + "\r\n" +
                "blocked_day=" + BlockedDay + "\r\n";
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
        }

        static bool IsNo(string value)
        {
            value = value.ToLowerInvariant();
            return value == "no" || value == "false" || value == "0" || value == "off";
        }
    }
}
