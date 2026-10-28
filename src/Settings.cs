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

        public bool Popup = true;  // the "keyboard's locked" note in the corner when a locked key is pressed

        // paws blocked: all told, and on BlockedDay (yyyy-MM-dd)
        public int BlockedTotal, BlockedToday;
        public string BlockedDay = "";

        public bool HasWord { get { return UnlockWord.Length > 0; } }

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
                    case "popup": s.Popup = !IsNo(value); break;
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
                "popup=" + (Popup ? "yes" : "no") + "\r\n" +
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
