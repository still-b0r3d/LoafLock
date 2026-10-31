using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace LoafLock.Tests
{
    // LoafLockTests.exe --shots <folder>: draws the window (unlocked and locked), the pop-up and
    // the unlock-word dialog into PNG files, straight from the controls. Nothing shows on screen
    // and nothing takes focus; locking uses a test-mode hook.
    //
    // LoafLockTests.exe --screenshots docs\images: the README's pictures. The window, Rukus's
    // menu, Options and the notes, shown on the main screen in front of a plain backdrop and
    // captured as Windows draws them (title bar, shadow and all). It takes about 15 seconds; Rukus
    // shows up in the taskbar corner meanwhile, and locking uses a test-mode hook, so the keyboard
    // keeps working.
    static partial class TestMain
    {
        static int TakeShots(string folder)
        {
            Settings.Dir = Path.Combine(Path.GetTempPath(), "LoafLockShots-" + Guid.NewGuid().ToString("N"));
            Instance.Name = @"Local\LoafLockShots-" + Guid.NewGuid().ToString("N");
            try
            {
                foreach (Look look in new[] { Look.Light, Look.Dark })
                {
                    Theme.Set(look);
                    string to = Path.Combine(folder, look.ToString().ToLowerInvariant());
                    Directory.CreateDirectory(to);
                    var settings = new Settings { UnlockWord = "rukus", CatName = "Biscuit", Look = look };
                    settings.AddBlocked(212, DateTime.Now);
                    settings.BlockedTotal = 4051;
                    using (var locker = new Locker(settings, new KeyHook(new KeyGate(), true)))
                    using (var window = new MainForm(locker) { Quiet = true })
                    {
                        ShowOffScreen(window);
                        Shoot(window, Path.Combine(to, "window-unlocked.png"));
                        window.LockClicked();
                        Pump(() => locker.IsLocked, "it didn't lock");
                        Shoot(window, Path.Combine(to, "window-locked.png"));
                        window.LockClicked();
                        Pump(() => !locker.IsLocked, "it didn't unlock");
                        using (var options = new OptionsForm(locker) { Quiet = true })
                        {
                            ShowOffScreen(options);
                            Shoot(options, Path.Combine(to, "options.png"));
                        }
                    }
                    using (var word = new WordForm("") { Quiet = true })
                    {
                        ShowOffScreen(word);
                        Shoot(word, Path.Combine(to, "word-first-run.png"));
                    }
                }
                using (var popup = new Popup())
                {
                    popup.Set("Keyboard locked", "Type your unlock word to get it back.", true);
                    Shoot(popup, Path.Combine(folder, "note-locked.png"));
                    popup.Set("Keyboard unlocked", "Paws blocked today: 212", false);
                    Shoot(popup, Path.Combine(folder, "note-unlocked.png"));
                    popup.Set("Keyboard's locked", "Type your unlock word to get it back.\nPaws blocked by Biscuit today: 212", true);
                    Shoot(popup, Path.Combine(folder, "note-on-a-key.png"));
                    popup.Set("Keyboard and mouse locked", "Type your unlock word to get them back.", true);
                    Shoot(popup, Path.Combine(folder, "note-mouse-locked.png"));
                    popup.Set("Biscuit is loafing", "Nothing gets typed: the keyboard's locked.", true, true);
                    Shoot(popup, Path.Combine(folder, "note-loafing.png"));
                    popup.Set("Keyboard locked by itself", "Nobody used the PC for 10 minutes. Type your unlock word to get it back.", true);
                    Shoot(popup, Path.Combine(folder, "note-idle.png"));
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

        static readonly Color Backdrop = Color.FromArgb(232, 234, 237);

        // what the pictures show: an unlock word, Biscuit, and a busy day's paws
        static Settings Example(Look look)
        {
            var settings = new Settings { UnlockWord = "rukus", CatName = "Biscuit", Look = look };
            settings.AddBlocked(212, DateTime.Now);
            settings.BlockedTotal = 4051;
            return settings;
        }

        // the README's pictures, named as it has them in docs\images
        static int TakeScreenshots(string folder)
        {
            Settings.Dir = Path.Combine(Path.GetTempPath(), "LoafLockShots-" + Guid.NewGuid().ToString("N"));
            Instance.Name = @"Local\LoafLockShots-" + Guid.NewGuid().ToString("N");
            // a Run key of its own, so the Options window never reads or changes the real one
            StartWithWindows.RunKeyPath = @"Software\LoafLockShots-" + Guid.NewGuid().ToString("N");
            Application.EnableVisualStyles();  // as LoafLock.exe does, or the buttons come out old-style
            Application.SetCompatibleTextRenderingDefault(false);
            Directory.CreateDirectory(folder);
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            var spot = new Point(area.Left + 80, area.Top + 80);
            var backdrop = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                BackColor = Backdrop,
                ShowInTaskbar = false,
                TopMost = true,
                StartPosition = FormStartPosition.Manual,
                Bounds = area
            };
            try
            {
                backdrop.Show();
                Theme.Set(Look.Light);
                // Rukus's menu comes first: after a menu from a window's menu bar (Help, below),
                // WinForms closes a menu opened like this one straight away. It's from a Tray of
                // its own, started quietly, and never locked.
                using (var tray = new Tray(Example(Look.Light), true, false))
                {
                    var menu = (ContextMenuStrip)typeof(Tray).GetField("menu", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tray);
                    menu.Show(backdrop, new Point(200, 140));
                    Pump(() => menu.Visible, "Rukus's menu didn't open");
                    Settle();
                    Check(menu.Visible, "Rukus's menu closed before it was captured");
                    Capture(Rectangle.Inflate(menu.Bounds, 18, 18), Path.Combine(folder, "4-menu.png"));
                    menu.Close();
                }
                using (var locker = new Locker(Example(Look.Light), new KeyHook(new KeyGate(), true)))
                {
                    using (var window = new MainForm(locker))
                    {
                        ShowOnBackdrop(window, spot);
                        Screenshot(window, Path.Combine(folder, "7-window-light.png"));
                        window.LockClicked();
                        Pump(() => locker.IsLocked, "it didn't lock");
                        Screenshot(window, Path.Combine(folder, "1-window-locked.png"));
                        window.LockClicked();
                        Pump(() => !locker.IsLocked, "it didn't unlock");
                        var help = (ToolStripMenuItem)window.MainMenuStrip.Items[2];  // LoafLock, Options, Help
                        help.ShowDropDown();
                        Pump(() => help.DropDown.Visible, "the Help menu didn't open");
                        Settle();
                        Check(help.DropDown.Visible, "the Help menu closed before it was captured");
                        Capture(Rectangle.Union(Rectangle.Inflate(VisibleBounds(window), 24, 24), Rectangle.Inflate(help.DropDown.Bounds, 24, 24)),
                            Path.Combine(folder, "9-help-menu.png"));
                        help.HideDropDown();
                    }
                    using (var options = new OptionsForm(locker))
                    {
                        ShowOnBackdrop(options, spot);
                        Screenshot(options, Path.Combine(folder, "5-options.png"));
                    }
                }
                using (var word = new WordForm(""))
                {
                    ShowOnBackdrop(word, spot);
                    Screenshot(word, Path.Combine(folder, "2-unlock-word.png"));
                }
                Theme.Set(Look.Dark);
                using (var locker = new Locker(Example(Look.Dark), new KeyHook(new KeyGate(), true)))
                using (var window = new MainForm(locker))
                {
                    ShowOnBackdrop(window, spot);
                    Screenshot(window, Path.Combine(folder, "8-window-dark.png"));
                }
                // the note in the corner, shown where the windows were
                Popup.Place = size => spot;
                using (var popup = new Popup { HoldMs = 60000 })
                {
                    Note(popup, Path.Combine(folder, "3-note-locked.png"), "Keyboard locked", "Type your unlock word to get it back.", true, false);
                    Note(popup, Path.Combine(folder, "6-note-loafing.png"), "Biscuit is loafing", "Nothing gets typed: the keyboard's locked.", true, true);
                }
                Console.WriteLine("screenshots in " + folder);
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine("FAIL  screenshots: " + e.Message);
                return 1;
            }
            finally
            {
                backdrop.Dispose();
                try { Directory.Delete(Settings.Dir, true); } catch { }
            }
        }

        // in front, over the backdrop, the way it looks to someone using it
        static void ShowOnBackdrop(Form form, Point at)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = at;
            form.TopMost = true;
            form.Show();
            form.Activate();
            Settle();
        }

        // lets everything lay itself out and paint, then the desktop compositor catch up
        static void Settle()
        {
            for (int i = 0; i < 10; i++)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }
            Thread.Sleep(250);
        }

        static void Screenshot(Form form, string path)
        {
            Settle();
            Capture(Rectangle.Inflate(VisibleBounds(form), 24, 24), path);
        }

        // one note, faded in where Popup.Place puts it, with some backdrop round it
        static void Note(Popup popup, string path, string heading, string line, bool locked, bool sleepy)
        {
            popup.Nudge(heading, line, locked, sleepy);
            Pump(() => popup.Opacity >= 0.95, "the note didn't show");
            Settle();
            Capture(Rectangle.Inflate(popup.Bounds, 16, 16), path);
        }

        // that part of the screen, as it is now
        static void Capture(Rectangle r, string path)
        {
            using (var picture = new Bitmap(r.Width, r.Height))
            {
                using (Graphics g = Graphics.FromImage(picture)) g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                picture.Save(path, ImageFormat.Png);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT rect, int size);

        // the window as drawn, without the invisible resize border Windows 10 and 11 add round it
        static Rectangle VisibleBounds(Form form)
        {
            RECT r;
            const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
            if (DwmGetWindowAttribute(form.Handle, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT))) == 0)
                return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            return form.Bounds;
        }
    }
}
