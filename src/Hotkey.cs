using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LoafLock
{
    // A key combo that locks the keyboard: "Ctrl+Alt+L". Windows' own hotkey feature
    // (RegisterHotKey), not a hook, so an unlocked LoafLock still watches nothing. It only ever
    // locks: while locked, the keyboard hook drops its keys before Windows sees a hotkey, so it
    // can't unlock, and a cat can't either.
    struct Hotkey
    {
        public readonly Keys Key;        // the key itself, e.g. Keys.L
        public readonly Keys Modifiers;  // Keys.Control, Keys.Alt, Keys.Shift together

        public Hotkey(Keys key, Keys modifiers)
        {
            Key = key;
            Modifiers = modifiers & (Keys.Control | Keys.Alt | Keys.Shift);
        }

        public static readonly Hotkey Default = new Hotkey(Keys.L, Keys.Control | Keys.Shift);

        // What's wrong with it as a hotkey, or null if it'll do:
        // - a key nothing else uses, on its own (Pause, Scroll Lock, F13 to F24), or
        // - Ctrl+Shift with a key: Ctrl with one key is a shortcut apps use (Ctrl+C), Alt or
        //   Shift with one key gets in the way of typing and menus (after the lock, Alt's
        //   release reaches the app as a lone Alt tap, which opens its menu bar), or
        // - Ctrl+Alt with an F key: with a letter it's AltGr on many keyboards, which types a
        //   character (Ctrl+Alt+L is "ł" in Polish).
        // Never Space or Enter: Windows takes the hotkey's press, but its release still reaches
        // the app, and a lone Space release plays or pauses YouTube.
        public string Problem
        {
            get
            {
                bool ctrl = (Modifiers & Keys.Control) != 0, alt = (Modifiers & Keys.Alt) != 0, shift = (Modifiers & Keys.Shift) != 0;
                if (Key == Keys.None || IsModifier(Key)) return "Press a key along with Ctrl and Shift.";
                if (Key == Keys.Space || Key == Keys.Return) return "Not Space or Enter: when you let go, the video would still get it.";
                if (Modifiers == Keys.None) return Alone(Key) ? null : "On its own, only Pause, Scroll Lock or F13 to F24. Hold Ctrl and Shift with anything else.";
                if (!ctrl) return "Hold Ctrl and Shift with the key: Alt or Shift alone gets in the way of typing and menus.";
                if (alt) return Key >= Keys.F1 && Key <= Keys.F24 ? null : "Ctrl+Alt with a letter types a character on some keyboards. Use Ctrl+Shift, or Ctrl+Alt with an F key.";
                if (!shift) return "Ctrl with one key is a shortcut apps use (Ctrl+C copies). Hold Shift too.";
                return null;
            }
        }

        static bool Alone(Keys key)
        {
            return key == Keys.Pause || key == Keys.Scroll || (key >= Keys.F13 && key <= Keys.F24);
        }

        static bool IsModifier(Keys key)
        {
            switch (key)
            {
                case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey:
                case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey:
                case Keys.Menu: case Keys.LMenu: case Keys.RMenu:
                case Keys.LWin: case Keys.RWin: case Keys.Capital:
                    return true;
            }
            return false;
        }

        // "Ctrl+Alt+L", the way settings.ini and the window show it
        public override string ToString()
        {
            string text = "";
            if ((Modifiers & Keys.Control) != 0) text += "Ctrl+";
            if ((Modifiers & Keys.Alt) != 0) text += "Alt+";
            if ((Modifiers & Keys.Shift) != 0) text += "Shift+";
            return text + KeyName(Key);
        }

        static string KeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9) return ((char)('0' + (key - Keys.D0))).ToString();
            switch (key)
            {
                case Keys.Scroll: return "ScrollLock";
                case Keys.Next: return "PageDown";
                case Keys.Prior: return "PageUp";
                case Keys.Return: return "Enter";
                case Keys.Back: return "Backspace";
                case Keys.Oemcomma: return "Comma";
                case Keys.OemPeriod: return "Period";
            }
            return key.ToString();
        }

        // "ctrl+alt+L" back again; false if it isn't a hotkey LoafLock takes
        public static bool TryParse(string text, out Hotkey hotkey)
        {
            hotkey = default(Hotkey);
            Keys modifiers = Keys.None, key = Keys.None;
            foreach (string raw in (text ?? "").Split('+'))
            {
                string part = raw.Trim();
                if (part.Length == 0) return false;
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": modifiers |= Keys.Control; continue;
                    case "alt": modifiers |= Keys.Alt; continue;
                    case "shift": modifiers |= Keys.Shift; continue;
                }
                if (key != Keys.None) return false;  // two keys
                key = ParseKey(part);
                if (key == Keys.None) return false;
            }
            hotkey = new Hotkey(key, modifiers);
            return hotkey.Problem == null;
        }

        static Keys ParseKey(string part)
        {
            if (part.Length == 1 && part[0] >= '0' && part[0] <= '9') return Keys.D0 + (part[0] - '0');
            switch (part.ToLowerInvariant())
            {
                case "scrolllock": return Keys.Scroll;
                case "pagedown": return Keys.Next;
                case "pageup": return Keys.Prior;
                case "enter": return Keys.Return;
                case "backspace": return Keys.Back;
                case "comma": return Keys.Oemcomma;
                case "period": return Keys.OemPeriod;
            }
            Keys key;
            if (!Enum.TryParse(part, true, out key) || (key & Keys.Modifiers) != 0 || (int)key <= 0 || (int)key > 0xFE) return Keys.None;
            return key;
        }
    }

    // Holds one hotkey with Windows and says when it's pressed. Lives on the window thread.
    sealed class HotkeyListener : NativeWindow, IDisposable
    {
        const int Id = 1;
        bool registered;

        public event Action Pressed;

        public HotkeyListener()
        {
            // a message-only window: never shown, only there to be told about the hotkey
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) });  // HWND_MESSAGE
        }

        public bool Registered { get { return registered; } }

        // false: another app already has it (or Windows keeps it for itself)
        public bool Set(Hotkey hotkey)
        {
            Clear();
            uint mods = MOD_NOREPEAT;
            if ((hotkey.Modifiers & Keys.Control) != 0) mods |= MOD_CONTROL;
            if ((hotkey.Modifiers & Keys.Alt) != 0) mods |= MOD_ALT;
            if ((hotkey.Modifiers & Keys.Shift) != 0) mods |= MOD_SHIFT;
            registered = RegisterHotKey(Handle, Id, mods, (uint)hotkey.Key);
            return registered;
        }

        public void Clear()
        {
            if (!registered) return;
            UnregisterHotKey(Handle, Id);
            registered = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == Id)
            {
                Action pressed = Pressed;
                if (pressed != null) pressed();
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Clear();
            DestroyHandle();
        }

        const int WM_HOTKEY = 0x0312;
        const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    }
}
