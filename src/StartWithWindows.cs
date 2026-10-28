using Microsoft.Win32;

namespace LoafLock
{
    // "Start LoafLock when you sign in to Windows": one value under HKCU\...\Run pointing at this
    // exe, wherever it is. Written only when someone ticks the box; unticking removes it.
    static class StartWithWindows
    {
        public const string ValueName = "LoafLock";

        // the tests use a key of their own
        public static string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static string CommandFor(string exe)
        {
            return "\"" + exe + "\" --startup";
        }

        public static bool IsOn
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                    return key != null && key.GetValue(ValueName) is string;
            }
        }

        public static void Set(bool on, string exe)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (on) key.SetValue(ValueName, CommandFor(exe));
                else key.DeleteValue(ValueName, false);
            }
        }
    }
}
