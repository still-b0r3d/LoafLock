using System;
using System.IO;
using System.Windows.Forms;

namespace LoafLock.Tests
{
    // 0.5 on: the hotkey and the volume keys. Hooks are in test mode, and the live hotkey is a
    // lone F24, which no app uses.
    static partial class TestMain
    {
        static void HotkeyText()
        {
            Hotkey h;
            Check(Hotkey.Default.ToString() == "Ctrl+Alt+L" && Hotkey.Default.Problem == null, "the default hotkey is " + Hotkey.Default);
            Check(Hotkey.TryParse("ctrl+alt+L", out h) && h.Key == Keys.L && h.Modifiers == (Keys.Control | Keys.Alt), "ctrl+alt+L wasn't read");
            Check(Hotkey.TryParse(" Ctrl + Shift + 5 ", out h) && h.Key == Keys.D5 && h.ToString() == "Ctrl+Shift+5", "Ctrl+Shift+5 came back as " + h);
            Check(Hotkey.TryParse("Pause", out h) && Hotkey.TryParse("ScrollLock", out h) && Hotkey.TryParse("F24", out h), "a lone Pause, ScrollLock or F24 was turned down");
            Check(Hotkey.TryParse(new Hotkey(Keys.Next, Keys.Control).ToString(), out h) && h.Key == Keys.Next, "Ctrl+PageDown doesn't come back");
            Check(!Hotkey.TryParse("L", out h), "a lone L was taken (it's typing)");
            Check(!Hotkey.TryParse("Alt+L", out h), "Alt+L was taken (a lone Alt tap after the lock opens menus)");
            Check(!Hotkey.TryParse("Shift+L", out h), "Shift+L was taken (it's typing)");
            Check(!Hotkey.TryParse("Ctrl+Alt", out h) && !Hotkey.TryParse("Ctrl+L+K", out h) && !Hotkey.TryParse("Ctrl+Banana", out h) && !Hotkey.TryParse("", out h),
                "nonsense was taken as a hotkey");

            using (var pick = new HotkeyForm(Hotkey.Default))
            {
                pick.Press(Keys.K | Keys.Control | Keys.Alt);
                Check(pick.Picked.ToString() == "Ctrl+Alt+K", "pressing Ctrl+Alt+K picked " + pick.Picked);
                pick.Press(Keys.J | Keys.Alt);
                Check(pick.Picked.ToString() == "Ctrl+Alt+K", "Alt+J replaced a good pick");
                pick.Press(Keys.ControlKey | Keys.Control);
                Check(pick.Picked.ToString() == "Ctrl+Alt+K", "Ctrl on its own replaced the pick");
            }
        }

        static void GateVolumeThrough()
        {
            const int Mute = 0xAD, VolumeUp = 0xAF;
            KeyGate g = Locked("rukus");
            Check(Down(g, VolumeUp) && Up(g, VolumeUp), "without the option, Volume Up got through");
            g.PassVolume = true;
            Check(!Down(g, Mute) && !Up(g, Mute) && !Down(g, VolumeUp) && !Down(g, VolumeUp) && !Up(g, VolumeUp), "with the option, a volume key was dropped");
            int blocked = g.Blocked;
            Type(g, "ruk");
            Down(g, Mute);
            Up(g, Mute);
            Type(g, "us");
            Check(!g.Locked, "a volume key in the middle of the word started it over");
            Check(g.Blocked == blocked + 5, "volume keys were counted as paws");

            // held from a lock without the option: it finishes the old way
            g = Locked("rukus");
            Check(Down(g, VolumeUp), "Volume Up got through");
            g.PassVolume = true;
            Check(Down(g, VolumeUp) && Up(g, VolumeUp), "a volume key held from before the option didn't finish the old way");
            g.Unlock();
            Check(!g.Busy, "still busy with every key up");
        }

        // a lone F24 as the hotkey: sent here, it locks (a test-mode hook, so nothing real is locked)
        static void HotkeyLocks()
        {
            var settings = new Settings { UnlockWord = "rukus", HotkeyOn = true, Hotkey = new Hotkey(Keys.F24, Keys.None) };
            var hook = new KeyHook(new KeyGate(), true);
            using (var locker = new Locker(settings, hook))
            {
                if (!locker.ApplyHotkey()) throw new SkipException("F24 is taken as a hotkey here: " + locker.HotkeyProblem);
                if (IntPtr.Size != 8 || !Send(F24, false)) throw new SkipException("Windows won't take sent keys here");
                Send(F24, true);
                Pump(() => locker.IsLocked, "the hotkey didn't lock");
                locker.Unlock("test");
                Pump(() => !locker.IsLocked, "it didn't unlock");

                settings.HotkeyOn = false;
                Check(locker.ApplyHotkey() && locker.HotkeyProblem == null, "turning the hotkey off didn't let it go");
                using (var other = new HotkeyListener())
                {
                    Check(other.Set(new Hotkey(Keys.F24, Keys.None)), "F24 wasn't free again after turning the hotkey off");
                    settings.HotkeyOn = true;
                    Check(!locker.ApplyHotkey() && locker.HotkeyProblem != null && locker.HotkeyProblem.StartsWith("F24 is already taken"),
                        "a hotkey another program holds didn't say so");
                }
            }
        }

        // 0.6: "Lock by itself"
        static void IdleLocks()
        {
            const long Minute = 60000;
            Check(Idle.Due(10, 10 * Minute, 11 * Minute, false) && !Idle.Due(10, 9 * Minute, 11 * Minute, false), "10 minutes idle isn't the line");
            Check(!Idle.Due(10, 30 * Minute, 2 * Minute, false), "it counted from before the last unlock (typing the word isn't input for Windows)");
            Check(!Idle.Due(10, 30 * Minute, 30 * Minute, true), "it locked during a fullscreen game");
            Check(Idle.IdleMs() >= 0, "Windows' idle time came back below zero");

            var settings = new Settings { UnlockWord = "rukus", IdleLock = true, IdleMinutes = 5, LockMouse = true };
            var hook = new KeyHook(new KeyGate(), true);
            using (var locker = new Locker(settings, hook))
            {
                locker.CheckIdle(4 * Minute, 4 * Minute, false);
                locker.CheckIdle(6 * Minute, 6 * Minute, true);
                Pump(() => hook.Installed, "", true);
                Check(!locker.IsLocked && !hook.Installed, "it locked before 5 minutes, or during a game");
                locker.CheckIdle(6 * Minute, 6 * Minute, false);
                Pump(() => locker.IsLocked, "5 minutes idle didn't lock");
                Check(locker.LockedBy == "idle" && !locker.MouseLocked && !hook.MouseInstalled, "an idle lock locked the mouse too");
                locker.Unlock("test");
                Pump(() => !locker.IsLocked, "it didn't unlock");

                settings.IdleLock = false;
                locker.CheckIdle(60 * Minute, 60 * Minute, false);
                Pump(() => hook.Installed, "", true);
                Check(!locker.IsLocked && !hook.Installed, "it locked by itself with the option off");
            }

            File.WriteAllText(Settings.FilePath, "idle_lock=yes\r\nidle_minutes=7\r\n");
            Settings back = Settings.Load();
            Check(back.IdleLock && back.IdleMinutes == Idle.DefaultMinutes, "a made-up number of minutes was taken");
        }

        static void OptionsHotkeyAndVolume()
        {
            var settings = new Settings { UnlockWord = "rukus", Hotkey = new Hotkey(Keys.F23, Keys.None) };
            using (var locker = new Locker(settings, new KeyHook(new KeyGate(), true)))
            using (var options = new OptionsForm(locker))
            {
                Check(!options.HotkeyBox.Checked && !options.VolumeBox.Checked, "the hotkey or volume box is ticked to start with");
                options.HotkeyBox.Checked = true;
                Check(settings.HotkeyOn && locker.HotkeyProblem == null, "ticking the hotkey didn't take it (" + locker.HotkeyProblem + ")");
                options.VolumeBox.Checked = true;
                Check(!options.IdleBox.Checked && options.IdleChoice(Idle.DefaultMinutes).Checked, "the idle lock doesn't show its defaults");
                options.IdleBox.Checked = true;
                options.IdleChoice(30).Checked = true;
                Settings saved = Settings.Load();
                Check(saved.HotkeyOn && saved.Hotkey.ToString() == "F23" && saved.VolumeThrough, "the hotkey or the volume option wasn't saved");
                Check(saved.IdleLock && saved.IdleMinutes == 30, "the idle lock wasn't saved");

                using (var other = new HotkeyListener())
                {
                    options.HotkeyBox.Checked = false;
                    Check(other.Set(new Hotkey(Keys.F22, Keys.None)), "F22 is taken here");
                    options.HotkeyChanged(true, new Hotkey(Keys.F22, Keys.None));
                    Check(!settings.HotkeyOn && settings.Hotkey.ToString() == "F23" && options.HotkeyProblemText.StartsWith("F22 is already taken"),
                        "a taken hotkey didn't put the old one back and say why");
                }
            }
        }
    }
}
