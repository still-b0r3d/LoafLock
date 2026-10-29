using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LoafLock
{
    // A small note in the corner above the taskbar: "Keyboard locked" / "Keyboard unlocked", or
    // on a blocked key. It never takes focus (a fullscreen video stays fullscreen), clicks go
    // through it, and it fades out a few seconds after the last nudge.
    sealed class Popup : Form
    {
        // where it goes, given its size: the main screen's bottom-right corner, above the
        // taskbar. The tests put it off screen.
        public static Func<Size, Point> Place = size =>
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            return new Point(area.Right - size.Width - 16, area.Bottom - size.Height - 16);
        };

        public int HoldMs = 3000;  // how long it stays after the last blocked key

        const double Full = 0.96;  // never quite opaque: only a see-through window lets clicks pass
        readonly Timer fade = new Timer { Interval = 30 };
        readonly Icon lockedRukus, rukus;
        readonly Font titleFont = new Font("Segoe UI Semibold", 11.5f), textFont = new Font("Segoe UI", 9f);
        readonly Font[] zFonts = { new Font("Segoe UI Black", 8f), new Font("Segoe UI Black", 10f), new Font("Segoe UI Black", 12.5f) };
        readonly int pad;
        string title = "", text = "";
        bool showsLocked = true, sleepy;
        int lastNudge;
        bool fadingIn;

        public Popup()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(32, 32, 36);
            DoubleBuffered = true;
            Opacity = 0;
            float scale;
            using (Graphics g = CreateGraphics()) scale = g.DpiX / 96f;
            pad = (int)(14 * scale);
            var size = new Size((int)(48 * scale), (int)(48 * scale));
            lockedRukus = AppIcons.Load("loaflock-locked.ico", size);
            rukus = AppIcons.Load("loaflock.ico", size);
            ClientSize = new Size((int)(340 * scale), (int)(80 * scale));
            fade.Tick += delegate { Step(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams;
                p.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
                return p;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2;  // DWMWCP_ROUND: rounded corners on Windows 11; older Windows keeps them square
            DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
        }

        // what the note says, and which Rukus: with the padlock (locked), nodding off (sleepy).
        // The screenshot tool sets it without showing it.
        internal void Set(string heading, string line, bool locked, bool sleepy = false)
        {
            title = heading ?? "";
            text = line ?? "";
            showsLocked = locked;
            this.sleepy = sleepy;
            Invalidate();
        }

        // show the note, or keep it up a while longer
        public void Nudge(string heading, string line, bool locked, bool sleepy = false)
        {
            Set(heading, line, locked, sleepy);
            lastNudge = Environment.TickCount;
            if (!Visible)
            {
                Location = Place(Size);
                Opacity = 0;
                Show();
            }
            // back on top of everything, a fullscreen video included, still without focus
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            fadingIn = Opacity < Full;
            fade.Start();
        }

        void Step()
        {
            if (fadingIn)
            {
                Opacity = Math.Min(Full, Opacity + 0.24);
                fadingIn = Opacity < Full;
                return;
            }
            if (Environment.TickCount - lastNudge < HoldMs) return;
            Opacity = Math.Max(0, Opacity - 0.08);
            if (Opacity > 0) return;
            fade.Stop();
            Hide();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Icon picture = showsLocked ? lockedRukus : rukus;
            int top0 = (ClientSize.Height - picture.Height) / 2;
            g.DrawIcon(picture, pad, top0);
            if (sleepy) Zzz(g, pad, top0, picture.Width);
            int x = pad * 2 + picture.Width, width = ClientSize.Width - x - pad;
            Size head = TextRenderer.MeasureText(g, title, titleFont);
            Size body = TextRenderer.MeasureText(g, text, textFont, new Size(width, ClientSize.Height), TextFormatFlags.WordBreak);
            int top = (ClientSize.Height - head.Height - body.Height) / 2;
            TextRenderer.DrawText(g, title, titleFont, new Point(x, top), Color.White);
            TextRenderer.DrawText(g, text, textFont, new Rectangle(x, top + head.Height, width, body.Height), Color.FromArgb(205, 205, 210), TextFormatFlags.WordBreak);
        }

        // "z Z Z" drifting up from the top right of Rukus's head, small to big
        void Zzz(Graphics g, int x, int y, int size)
        {
            float s = size / 48f;
            var color = Color.FromArgb(160, 195, 255);
            float[][] at = { new[] { 0.62f, 0.16f }, new[] { 0.80f, -0.04f }, new[] { 1.00f, -0.27f } };  // as parts of the picture's size
            for (int i = 0; i < zFonts.Length; i++)
                TextRenderer.DrawText(g, i == 0 ? "z" : "Z", zFonts[i], new Point(x + (int)(at[i][0] * 48 * s), y + (int)(at[i][1] * 48 * s)), color, TextFormatFlags.NoPadding);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)MA_NOACTIVATE;  // a click never makes it the active window
                return;
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fade.Dispose();
                lockedRukus.Dispose();
                rukus.Dispose();
                titleFont.Dispose();
                textFont.Dispose();
                foreach (Font z in zFonts) z.Dispose();
            }
            base.Dispose(disposing);
        }

        // ---- Windows ----

        const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
