using System;
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

        public bool HasWord { get { return UnlockWord.Length > 0; } }

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
                switch (key)
                {
                    case "unlock_word":
                        if (WordProblem(value) == null) s.UnlockWord = value.ToLowerInvariant();
                        else if (value.Length > 0) Log.Write("settings: the unlock word in settings.ini isn't letters A to Z (4 to 20), so it was ignored");
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
                "unlock_word=" + UnlockWord + "\r\n";
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
        }
    }
}
