using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace LoafLock.Tests
{
    // 0.81: cat detection. PawWatch on its own, on a pretend clock; the gate's lock from it; then
    // the real hook in test mode (only this test's own keys count), Locker, Options and the note.
    static partial class TestMain
    {
        const int Backspace = 0x08, Escape = 0x1B, Alt = 0xA4, Win = 0x5B, CapsLock = 0x14;
        const ushort F20 = 0x83, F21 = 0x84, F22 = 0x85;

        // a key pressed at `at` and let go holdMs later, repeating while it's held the way Windows
        // repeats a key (half a second, then every 33 ms)
        static void Press(List<int[]> keys, int at, int vk, int holdMs)
        {
            keys.Add(new[] { at, vk, 1 });
            for (int t = at + 500; t < at + holdMs; t += 33) keys.Add(new[] { t, vk, 1 });
            keys.Add(new[] { at + holdMs, vk, 0 });
        }

        // plays the keys into a fresh PawWatch in time order, looking for a cat every 10 ms: when
        // one was first seen, or -1
        static int FirstCat(List<int[]> keys, int until)
        {
            return FirstCat(new PawWatch(), keys, until);
        }

        static int FirstCat(PawWatch w, List<int[]> keys, int until)
        {
            List<int[]> inOrder = keys.OrderBy(k => k[0]).ToList();  // a stable sort: keys at the same moment keep their order
            int next = 0;
            for (int t = 0; t <= until; t += 10)
            {
                for (; next < inOrder.Count && inOrder[next][0] <= t; next++) w.OnKey(inOrder[next][1], inOrder[next][2] == 1, inOrder[next][0]);
                if (w.IsCat(t)) return t;
            }
            return -1;
        }

        // the same, looking only when KeyHook looks: after each fresh press, and when NextLook
        // said to come back (never sooner than 10 ms, like its timer)
        static int FirstCatLikeTheHook(List<int[]> keys, int until)
        {
            var w = new PawWatch();
            int look = -1;  // when the hook's timer is due, or -1 for no timer
            foreach (int[] k in keys.OrderBy(e => e[0]).ToList())
            {
                for (; look >= 0 && look <= k[0]; look = Later(w, look)) if (w.IsCat(look)) return look;
                if (!w.OnKey(k[1], k[2] == 1, k[0])) continue;
                if (w.IsCat(k[0])) return k[0];
                int wait = w.NextLook(k[0]);
                if (wait >= 0) look = k[0] + Math.Max(10, wait);
            }
            for (; look >= 0 && look <= until; look = Later(w, look)) if (w.IsCat(look)) return look;
            return -1;
        }

        // when the hook's timer goes off next, after a look at `now` that found no cat
        static int Later(PawWatch w, int now)
        {
            int wait = w.NextLook(now);
            return wait < 0 ? -1 : now + Math.Max(10, wait);
        }

        // no cat, whether it looks every 10 ms or only where the hook looks
        static bool NoCat(List<int[]> keys, int until)
        {
            return FirstCat(keys, until) < 0 && FirstCatLikeTheHook(keys, until) < 0;
        }

        static void CatNotTypingOrGames()
        {
            // fast typing: the next key goes down before the last one comes up
            string text = "THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG ";
            var keys = new List<int[]>();
            for (int i = 0; i < 400; i++) Press(keys, 1000 + i * 60, text[i % text.Length], 110);
            Check(NoCat(keys, 30000), "fast typing looked like a cat");
            keys = new List<int[]>();
            for (int i = 0; i < 300; i++) Press(keys, 1000 + i * 30, text[i % text.Length], 90);
            Check(NoCat(keys, 12000), "a burst of very fast typing (three keys down at times) looked like a cat");

            // a fast roll across Q, W, E and R (a game's spells), over and over
            keys = new List<int[]>();
            for (int r = 0; r < 10; r++)
                for (int i = 0; i < 4; i++) Press(keys, 1000 + r * 700 + i * 15, "QWER"[i], 60);
            Check(NoCat(keys, 9000), "a fast roll across Q, W, E and R looked like a cat");

            // a game: W and A held together for a minute, Space tapped, push-to-talk on V held for
            // ten seconds and a lean on Q for four, both pressed long after W and A
            keys = new List<int[]>();
            Press(keys, 1000, 'W', 60000);
            Press(keys, 1010, 'A', 60000);
            for (int t = 2000; t < 60000; t += 700) Press(keys, t, Space, 120);
            Press(keys, 10000, 'V', 10000);
            Press(keys, 30000, 'Q', 4000);
            Check(NoCat(keys, 62000), "a game (W+A held, Space taps, push-to-talk, a lean) looked like a cat");

            // Backspace held down; Shift, Ctrl, Alt, Windows and Caps Lock held with one key
            keys = new List<int[]>();
            Press(keys, 1000, Backspace, 8000);
            foreach (int modifier in new[] { Shift, Ctrl, Alt, Win, CapsLock }) Press(keys, 10000, modifier, 3000);
            Press(keys, 10005, Escape, 3000);
            Check(NoCat(keys, 14000), "a held Backspace, or the modifiers with one key, looked like a cat");
        }

        static void CatSpotted()
        {
            // a pile: five keys down within a quarter of a second
            var keys = new List<int[]>();
            for (int i = 0; i < 5; i++) Press(keys, 1000 + i * 60, "ASDFG"[i], 3000);
            var w = new PawWatch();
            int at = FirstCat(w, keys, 5000);
            Check(at == 1280 && w.Why == "5 keys down at once",
                "a pile was spotted at " + at + " ms (\"" + w.Why + "\"), expected 1280 ms (the fourth key + 0.1 s), five keys down at once");
            Check(FirstCatLikeTheHook(keys, 5000) == 1280, "looking only where the hook looks, the pile was spotted at " + FirstCatLikeTheHook(keys, 5000) + " ms, not 1280");

            // a paw: three keys within 30 ms
            keys = new List<int[]>();
            for (int i = 0; i < 3; i++) Press(keys, 2000 + i * 15, "JKL"[i], 400);
            w = new PawWatch();
            at = FirstCat(w, keys, 5000);
            Check(at == 2130 && w.Why == "3 keys down within 30 ms of each other", "a paw was spotted at " + at + " ms (\"" + w.Why + "\"), expected 2130 ms");
            Check(FirstCatLikeTheHook(keys, 5000) == 2130, "looking only where the hook looks, the paw was spotted at " + FirstCatLikeTheHook(keys, 5000) + " ms, not 2130");

            // a cat settling onto a keyboard that senses 3 keys at once: one key, then another, then
            // another, all within a second
            keys = new List<int[]>();
            Press(keys, 1000, 'Z', 5000);
            Press(keys, 1400, 'X', 5000);
            Press(keys, 1800, 'C', 5000);
            w = new PawWatch();
            at = FirstCat(w, keys, 6000);
            Check(at == 3300 && w.Why == "3 keys held down for 1.5 s", "settling was spotted at " + at + " ms (\"" + w.Why + "\"), expected 3300 ms");
            Check(FirstCatLikeTheHook(keys, 6000) == 3300, "looking only where the hook looks, settling was spotted at " + FirstCatLikeTheHook(keys, 6000) + " ms, not 3300");
            w = new PawWatch();
            Check(w.NextLook(1000) == -1, "with nothing down, it still wants another look");
            w.OnKey('Z', true, 1000);
            w.OnKey('X', true, 1400);
            w.OnKey('C', true, 1800);
            Check(!w.IsCat(1900) && w.NextLook(1900) == 1400, "settling: the next look is in " + w.NextLook(1900) + " ms, expected 1400");

            // but a third key held seconds after the other two is a gamer adding one, not a cat
            keys = new List<int[]>();
            Press(keys, 1000, 'Z', 8000);
            Press(keys, 1200, 'X', 8000);
            Press(keys, 3500, 'C', 5000);
            Check(NoCat(keys, 9000), "a third key held seconds after the other two was taken for a cat settling");

            // the known limit, kept on purpose (Options and the README say so): three game keys
            // pressed within a second and held, W, then Space and D, look like a cat settling in
            keys = new List<int[]>();
            Press(keys, 1000, 'W', 5000);
            Press(keys, 1300, Space, 5000);
            Press(keys, 1700, 'D', 5000);
            Check(FirstCat(keys, 6000) == 3200 && FirstCatLikeTheHook(keys, 6000) == 3200,
                "three held game keys pressed within a second weren't taken for a cat at 3200 ms; if that changed on purpose, change Options and the README too");

            // Windows' clock wraps round after 49.7 days: a pile across the wrap is still a pile
            w = new PawWatch();
            int start = int.MaxValue - 40;
            w.OnKey('A', true, start);
            w.OnKey('S', true, unchecked(start + 20));
            w.OnKey('D', true, unchecked(start + 50));
            w.OnKey('F', true, unchecked(start + 70));
            Check(w.NextLook(unchecked(start + 100)) == 70 && !w.IsCat(unchecked(start + 160)) && w.IsCat(unchecked(start + 170)),
                "a pile across the clock's wrap round wasn't spotted on time");
        }

        static void CatHeldAndStale()
        {
            // still lying on four keys at the unlock: they don't count until they're let go
            var w = new PawWatch();
            foreach (char k in "QWER") w.OnKey(k, true, 1000);
            Check(w.IsCat(1200), "four keys down weren't a cat");
            w.LeaveHeld();
            Check(!w.IsCat(1300) && w.NextLook(1300) == -1, "keys held across the unlock still counted");
            w.OnKey('T', true, 1400);
            Check(!w.IsCat(1600), "one fresh key with four held ones was taken for a cat");
            foreach (char k in "QWER") w.OnKey(k, false, 1700);
            foreach (char k in "QWER") w.OnKey(k, true, 1800);
            Check(w.IsCat(1900), "fresh presses of keys let go after the unlock didn't count");

            // a key whose release never came, pressed again much later, is a fresh press
            w = new PawWatch();
            w.OnKey('A', true, 1000);
            w.OnKey('A', true, 7000);
            w.OnKey('S', true, 7010);
            w.OnKey('D', true, 7020);
            Check(w.IsCat(7130) && w.Why == "3 keys down within 20 ms of each other",
                "a key pressed again after its release went missing wasn't a fresh press (\"" + w.Why + "\")");

            // Shift, Ctrl, Alt, Windows, Caps Lock and the mouse buttons never count
            w = new PawWatch();
            foreach (int vk in new[] { Shift, Ctrl, Alt, Win, CapsLock, 1, 2 }) w.OnKey(vk, true, 1000);
            Check(!w.IsCat(3000) && w.NextLook(3000) == -1, "modifiers or mouse buttons counted as paws");
        }

        static void GateCatLock()
        {
            var g = new KeyGate();
            g.SetWord("rukus");
            g.StartWatching(clock);
            clock += g.HeldGraceMs + 100;  // a good while after the hook went in
            Check(!Down(g, 'A') && !Down(g, 'D'), "a key was dropped while unlocked");
            g.LockWatched();
            Check(g.Locked, "the cat lock didn't lock");
            Check(Down(g, 'A'), "a held key's repeat got through after the cat lock");
            Check(!Up(g, 'A'), "a held key's release was dropped: its press reached the app, so the key would stick");
            Check(Down(g, 'F') && Up(g, 'F'), "a fresh key, or its release, got through after the cat lock");
            Type(g, "rukus");
            Check(!g.Locked, "the unlock word didn't unlock a cat lock");
            Check(!Up(g, 'D'), "a key held through the cat lock had its release dropped after the unlock");
            Check(!g.Busy, "still busy with every key up");
        }

        // A live test that stops halfway can leave its keys down as far as Windows knows, so they're
        // let go whatever happens. The hook has gone by then, so nothing stops the releases.
        static void LiveCatWatch()
        {
            try { LiveCatWatchSteps(); }
            finally { LetGoOfTheTestKeys(); }
        }

        static void CatLocksTheKeyboardOnly()
        {
            try { CatLockSteps(); }
            finally { LetGoOfTheTestKeys(); }
        }

        static void LetGoOfTheTestKeys()
        {
            foreach (ushort vk in new[] { F20, F21, F22, F23, F24 }) Send(vk, true);
        }

        static void LiveCatWatchSteps()
        {
            if (IntPtr.Size != 8) throw new SkipException("needs a 64-bit run");
            var gate = new KeyGate();
            var changes = new List<bool>();
            string why = null;
            using (var behind = new Observer())
            using (var hook = new KeyHook(gate, true))
            {
                hook.Word = "rukus";
                hook.LockChanged += locked => { lock (changes) changes.Add(locked); };
                hook.Cat += reason => why = reason;
                hook.Watch(true);
                WaitFor(() => hook.Installed && hook.Watching, "watching for a cat didn't put the hook in");
                if (!Send(F24, false)) throw new SkipException("Windows won't take sent keys here (no desktop to type on?)");
                Send(F24, true);
                WaitFor(() => behind.Downs(F24) == 1 && behind.Ups(F24) == 1, "a key didn't get past while watching");

                Thread.Sleep(gate.HeldGraceMs + 100);  // so a key after the lock is plainly a fresh press
                var landed = Stopwatch.StartNew();
                foreach (ushort vk in new[] { F20, F21, F22, F23 }) Send(vk, false);  // a paw: four keys at once
                WaitFor(() => Changes(changes) == "L", "a paw of four keys didn't lock");
                // a pile or a paw locks a tenth of a second on, not at the settle's 1.5 s
                Check(landed.ElapsedMilliseconds < 700, "the paw took " + landed.ElapsedMilliseconds + " ms to lock");
                // Windows stamps keys in ticks of about 16 ms: with the fourth key a tick behind,
                // the first three make a paw before the four make a pile
                Check(why == "4 keys down at once" || (why != null && why.StartsWith("3 keys down within ")), "it locked, saying \"" + why + "\"");
                Check(behind.Downs(F20) == 1 && behind.Downs(F23) == 1, "the paw's own presses didn't reach the apps (they come before the lock)");
                Send(F24, false);
                Send(F24, true);
                WaitFor(() => Volatile.Read(ref gate.Blocked) >= 1, "a fresh key wasn't blocked after the cat lock");
                Thread.Sleep(100);
                Check(behind.Downs(F24) == 1 && behind.Ups(F24) == 1, "a fresh key, or its release, got past the cat lock");

                // unlocked with the cat still on the keys: they don't count, so one fresh key
                // next to them isn't a cat
                hook.Unlock();
                WaitFor(() => Changes(changes) == "LU", "it didn't unlock");
                Check(hook.Installed, "the hook went at the unlock, though it's still watching");
                Send(F24, false);
                Thread.Sleep(300);
                Check(Changes(changes) == "LU", "one fresh key, with the cat still lying on four from before the unlock, was taken for a cat");
                Send(F24, true);
                WaitFor(() => behind.Downs(F24) == 2 && behind.Ups(F24) == 2, "a key didn't get past after the unlock");
                foreach (ushort vk in new[] { F20, F21, F22, F23 }) Send(vk, true);
                WaitFor(() => behind.Ups(F20) == 1 && behind.Ups(F21) == 1 && behind.Ups(F22) == 1 && behind.Ups(F23) == 1,
                    "the paw's releases didn't get through (the keys would stick)");

                hook.Watch(false);
                WaitFor(() => !hook.Installed && !hook.Watching, "the hook stayed after watching was turned off");
            }
        }

        static void CatLockSteps()
        {
            if (IntPtr.Size != 8) throw new SkipException("needs a 64-bit run");
            var settings = new Settings { UnlockWord = "rukus", CatDetection = true, LockMouse = true, VolumeThrough = true };
            var gate = new KeyGate();
            var hook = new KeyHook(gate, true);
            using (var locker = new Locker(settings, hook))
            {
                Pump(() => hook.Watching, "cat detection didn't start watching");
                if (!Send(F20, false)) throw new SkipException("Windows won't take sent keys here");
                foreach (ushort vk in new[] { F21, F22, F23 }) Send(vk, false);
                Pump(() => locker.IsLocked, "a paw didn't lock");
                Check(locker.LockedBy == "cat" && !locker.MouseLocked && !hook.MouseInstalled, "the cat lock locked the mouse too, or wasn't put down to the cat");
                Check(gate.PassVolume, "the cat lock didn't let the volume keys through, with that option ticked");
                foreach (ushort vk in new[] { F20, F21, F22, F23 }) Send(vk, true);
                Pump(() => File.Exists(Log.FilePath) && File.ReadAllText(Log.FilePath).Contains("lock from cat detection: looks like a cat (") , "the log doesn't say why it locked");
                locker.Unlock("test");
                Pump(() => !locker.IsLocked, "it didn't unlock");
                Check(hook.Installed && hook.Watching, "it stopped watching at the unlock");
            }
        }

        static void OptionsCatDetection()
        {
            var settings = new Settings { UnlockWord = "rukus" };
            var hook = new KeyHook(new KeyGate(), true);
            using (var locker = new Locker(settings, hook))
            using (var options = new OptionsForm(locker))
            {
                Check(!options.CatDetectionBox.Checked && !new Settings().CatDetection, "cat detection is on to start with");
                Pump(() => hook.Installed, "", true);
                Check(!hook.Installed, "the hook is in with cat detection off");
                options.CatDetectionBox.Checked = true;
                Check(settings.CatDetection && Settings.Load().CatDetection, "ticking cat detection didn't change the setting, or didn't save it");
                Pump(() => hook.Installed && hook.Watching, "ticking cat detection didn't start watching");
                options.CatDetectionBox.Checked = false;
                Pump(() => !hook.Installed, "unticking cat detection didn't stop watching");
            }

            // no unlock word yet, so nothing to get out of a cat lock with: it doesn't watch
            var noWord = new KeyHook(new KeyGate(), true);
            using (new Locker(new Settings { CatDetection = true }, noWord))
            {
                Pump(() => noWord.Installed, "", true);
                Check(!noWord.Installed, "it watched for a cat with no unlock word");
            }
            File.WriteAllText(Settings.FilePath, "cat_detection=yes\r\n");
            Check(Settings.Load().CatDetection, "cat_detection=yes in settings.ini wasn't read");
        }

        static void CatNoteFits()
        {
            using (var popup = new Popup())
                foreach (string name in new[] { "", "Biscuit", "Sir Whiskers McFluffton" })
                    Check(popup.Fits(Tray.CatNoteTitle, Tray.CatNoteText(name)), "the cat note doesn't fit with " + (name == "" ? "no name" : "the name " + name));
        }
    }
}
