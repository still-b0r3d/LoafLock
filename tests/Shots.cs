using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace LoafLock.Tests
{
    // LoafLockTests.exe --shots <folder>: draws the window (unlocked and locked), the pop-up and
    // the unlock-word dialog into PNG files, straight from the controls. Nothing shows on screen
    // and nothing takes focus; locking uses a test-mode hook.
    static partial class TestMain
    {
        static int TakeShots(string folder)
        {
            Directory.CreateDirectory(folder);
            Settings.Dir = Path.Combine(Path.GetTempPath(), "LoafLockShots-" + Guid.NewGuid().ToString("N"));
            try
            {
                var settings = new Settings { UnlockWord = "rukus" };
                settings.AddBlocked(212, DateTime.Now);
                settings.BlockedTotal = 4051;
                using (var locker = new Locker(settings, new KeyHook(new KeyGate(), true)))
                using (var window = new MainForm(locker) { Quiet = true })
                {
                    ShowOffScreen(window);
                    Shoot(window, Path.Combine(folder, "window-unlocked.png"));
                    window.LockClicked();
                    Pump(() => locker.IsLocked, "it didn't lock");
                    Shoot(window, Path.Combine(folder, "window-locked.png"));
                    window.LockClicked();
                    Pump(() => !locker.IsLocked, "it didn't unlock");
                    using (var options = new OptionsForm(locker) { Quiet = true })
                    {
                        ShowOffScreen(options);
                        Shoot(options, Path.Combine(folder, "options.png"));
                    }
                }
                using (var popup = new Popup())
                {
                    popup.Set("Keyboard locked", "Type your unlock word to get it back.", true);
                    Shoot(popup, Path.Combine(folder, "note-locked.png"));
                    popup.Set("Keyboard unlocked", "Paws blocked today: 212", false);
                    Shoot(popup, Path.Combine(folder, "note-unlocked.png"));
                    popup.Set("Keyboard's locked", "Type your unlock word to get it back.\nPaws blocked today: 212", true);
                    Shoot(popup, Path.Combine(folder, "note-on-a-key.png"));
                }
                using (var word = new WordForm("") { Quiet = true })
                {
                    ShowOffScreen(word);
                    Shoot(word, Path.Combine(folder, "word-first-run.png"));
                }
                Console.WriteLine("shots in " + folder);
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine("FAIL  shots: " + e.Message);
                return 1;
            }
            finally
            {
                try { Directory.Delete(Settings.Dir, true); } catch { }
            }
        }

        // a window draws its buttons and labels only once it's been shown: shown here far off every
        // screen, out of the taskbar, without taking focus
        static void ShowOffScreen(Form form)
        {
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.Show();
            Application.DoEvents();
        }

        static void Shoot(Control control, string path)
        {
            IntPtr made = control.Handle;
            control.PerformLayout();
            using (var picture = new Bitmap(control.Width, control.Height))
            {
                control.DrawToBitmap(picture, new Rectangle(Point.Empty, control.Size));
                picture.Save(path, ImageFormat.Png);
            }
        }
    }
}
