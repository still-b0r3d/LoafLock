using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace LoafLock.Tests
{
    // a test that can't run here (and says why) rather than one that failed
    sealed class SkipException : Exception
    {
        public SkipException(string why) : base(why) { }
    }

    // Runs every test. No keyboard is ever locked by them.
    static partial class TestMain
    {
        static int passed, failed, skipped;

        // both of LoafLock's icons, at the taskbar corner's sizes for 100, 125, 150, 175, 200 and
        // 250 % scaling, and the sizes a window uses
        static readonly string[] IconNames = { "loaflock.ico", "loaflock-locked.ico" };
        static readonly int[] IconSizes = { 16, 20, 24, 28, 32, 40, 48, 64 };

        [STAThread]
        static int Main(string[] args)
        {
            // whatever goes wrong, the run ends with a line saying so: never an error dialog
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => { Console.WriteLine("FAIL  a window threw: " + e.Exception); Environment.Exit(3); };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { Console.WriteLine("FAIL  crashed: " + e.ExceptionObject); Environment.Exit(3); };

            string home = Path.Combine(Path.GetTempPath(), "LoafLockTests-" + Guid.NewGuid().ToString("N"));
            Settings.Dir = home;
            Directory.CreateDirectory(home);

            Run("version: the exe carries the version and the build", VersionMatches);
            Run("icons: built in, at every taskbar size", IconsAtEverySize);
            Run("icons: every size comes out as a picture, not static", IconsArePictures);
            Run("icon check: tells static from a picture", StaticIsCaught);
            Run("settings: the unlock word is checked, saved and read back", SettingsRoundTrip);
            Run("gate: unlocked, every key goes through", GateUnlockedPasses);
            Run("gate: locked, presses are dropped and counted", GateDropsWhileLocked);
            Run("gate: never drops a release (a press that slipped past can't stick)", GateNeverDropsARelease);
            Run("gate: a key held across the unlock stays dropped until it's let go", GateHeldAcrossUnlock);
            Run("gate: a release Windows never sent doesn't keep a key dropped", GateLostRelease);
            Run("gate: slow repeats (Filter Keys) aren't new presses", GateSlowRepeats);
            Run("word: typing it unlocks", WordUnlocks);
            Run("word: works with a paw resting on another key", WordWithAPawOnTheKeys);
            Run("word: a wrong key starts it over; Shift doesn't matter", WordWrongKeysAndShift);
            Run("word: near misses still count ('aaab' unlocks 'aab')", WordNearMisses);
            Run("word: a held key's repeats don't type it; no word, no unlock", WordNeedsFreshPresses);
            Run("hook: the real hook blocks, renews itself and lets go", LiveHook);

            try { Directory.Delete(home, true); } catch { }
            Console.WriteLine();
            Console.WriteLine(passed + " passed, " + failed + " failed" + (skipped > 0 ? ", " + skipped + " skipped" : ""));
            return failed == 0 ? 0 : 1;
        }

        static void Run(string name, Action test)
        {
            try
            {
                test();
                passed++;
                Console.WriteLine("PASS  " + name);
            }
            catch (SkipException e)
            {
                skipped++;
                Console.WriteLine("SKIP  " + name + ": " + e.Message);
            }
            catch (Exception e)
            {
                failed++;
                Console.WriteLine("FAIL  " + name + ": " + e.Message);
            }
        }

        static void Check(bool ok, string what)
        {
            if (!ok) throw new Exception(what);
        }

        static void VersionMatches()
        {
            Assembly exe = typeof(Program).Assembly;
            var attr = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(exe, typeof(AssemblyInformationalVersionAttribute));
            string want = Program.Version + "+" + BuildInfo.Commit;
            Check(attr != null && attr.InformationalVersion == want,
                "AssemblyInfo.cs says " + (attr == null ? "nothing" : attr.InformationalVersion) + ", expected " + want);
            Check(exe.GetName().Version.ToString().StartsWith(Program.Version + "."),
                "AssemblyVersion " + exe.GetName().Version + " doesn't start with " + Program.Version);
        }

        static void IconsAtEverySize()
        {
            foreach (string name in IconNames)
            {
                using (Stream res = typeof(Program).Assembly.GetManifestResourceStream("LoafLock." + name))
                    Check(res != null, name + " isn't built into the exe");
                foreach (int size in IconSizes)
                    using (Icon icon = AppIcons.Load(name, new Size(size, size)))
                        Check(icon.Width == size && icon.Height == size, name + " has no " + size + " px picture (got " + icon.Width + ")");
            }
        }

        static void IconsArePictures()
        {
            foreach (string name in IconNames)
                foreach (int size in IconSizes)
                    using (Icon icon = AppIcons.Load(name, new Size(size, size)))
                    using (Bitmap bmp = icon.ToBitmap())
                    {
                        string why = WhyNotAPicture(bmp);
                        Check(why == null, name + " at " + size + " px: " + why);
                    }
        }

        static void StaticIsCaught()
        {
            var rnd = new Random(12345); // the same "random" static on every run
            foreach (int size in new[] { 16, 32 })
                using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                {
                    for (int y = 0; y < size; y++)
                        for (int x = 0; x < size; x++)
                            bmp.SetPixel(x, y, Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256), rnd.Next(256)));
                    Check(WhyNotAPicture(bmp) != null, size + " px of static passed as a picture");
                }
        }

        // .NET Framework draws an .ico's PNG-packed frames under 256 px as random static (seen
        // 2026-10-27 with b0r3d.org's rukus.ico). Static is see-through in random places, mostly
        // half see-through, and no two neighbouring pixels look alike; Rukus is the opposite.
        // (Rukus's 16 px picture has a few nearly-solid and nearly-clear pixels, hence the slack.)
        // Returns why the picture looks like static, or null when it doesn't.
        static string WhyNotAPicture(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            if (bmp.GetPixel(0, 0).A > 8 || bmp.GetPixel(w - 1, 0).A > 8 || bmp.GetPixel(w / 2, 0).A > 8)
                return "the top corners and the gap between the ears aren't see-through";
            int crisp = 0, pairs = 0;
            long diff = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.A <= 8 || c.A >= 247) crisp++;
                    if (x + 1 < w && c.A >= 247)
                    {
                        Color d = bmp.GetPixel(x + 1, y);
                        if (d.A < 247) continue;
                        pairs++;
                        diff += Math.Abs(c.R - d.R) + Math.Abs(c.G - d.G) + Math.Abs(c.B - d.B);
                    }
                }
            if (crisp * 10 < w * h * 6) return "only " + (100 * crisp / (w * h)) + "% of the pixels are solid or clear";
            if (pairs == 0) return "no solid pixels side by side";
            if (diff / pairs > 120) return "side-by-side pixels differ by " + (diff / pairs) + " on average";
            return null;
        }
    }
}
