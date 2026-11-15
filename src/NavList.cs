using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LoafLock
{
    // The list of pages down the Options window's left side, drawn like Windows' own Settings (and
    // TVFollow's): an icon and a name for each page, the open one on a darker patch with a short
    // gold bar, the padlock's gold. It's a list box underneath, so the arrow keys and screen
    // readers work as with any list. Light, dark and high contrast come from Theme.
    class NavList : ListBox
    {
        public class Page
        {
            public readonly string Key, Name;
            public readonly char Icon;   // a character of Windows' icon font

            public Page(string key, string name, char icon)
            {
                Key = key;
                Name = name;
                Icon = icon;
            }

            public override string ToString() { return Name; }   // what a screen reader says
        }

        // the strip, a patch under the open page and a fainter one under the mouse; in Windows'
        // high contrast themes the theme's own: its window colour, its highlight under the open
        // page with the words in the colour that goes on it, and an outline under the mouse
        static bool Contrast { get { return SystemInformation.HighContrast; } }
        public static Color Back { get { return Theme.Strip; } }
        static Color Open { get { return Theme.Dark ? Color.FromArgb(61, 61, 61) : Color.FromArgb(232, 232, 232); } }
        static Color Hover { get { return Theme.Dark ? Color.FromArgb(52, 52, 52) : Color.FromArgb(237, 237, 237); } }

        readonly Font iconFont;   // null on a Windows without an icon font: names only then
        int hot = -1;             // the page under the mouse

        public NavList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            BorderStyle = BorderStyle.None;
            IntegralHeight = false;
            DoubleBuffered = true;
            iconFont = IconFont(12f);
            AccessibleName = "Options pages";
            Recolour();
        }

        // Windows 11's icon font, else Windows 10's (the same characters in both)
        static Font IconFont(float points)
        {
            foreach (string name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
            {
                // a font Windows doesn't have comes back as another one, under that one's name
                var font = new Font(name, points);
                if (font.Name == name) return font;
                font.Dispose();
            }
            return null;
        }

        public bool HasIcons { get { return iconFont != null; } }

        public Page Selected { get { return SelectedItem as Page; } }

        public List<Page> Pages
        {
            get
            {
                var list = new List<Page>();
                foreach (object item in Items) list.Add((Page)item);
                return list;
            }
        }

        // light or dark (Theme.Changed), or a high contrast theme going on or off
        public void Recolour()
        {
            BackColor = Back;
            ForeColor = Contrast ? SystemColors.WindowText : Theme.Text;
            Invalidate();
        }

        // the rows grow with the text, so they fit at any display scaling
        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            ItemHeight = Math.Min(255, Font.Height * 2 + 4);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ItemHeight = Math.Min(255, Font.Height * 2 + 4);
        }

        protected override void OnSystemColorsChanged(EventArgs e)
        {
            base.OnSystemColorsChanged(e);
            Recolour();
        }

        int Scaled(Graphics g, int px) { return (int)Math.Round(px * g.DpiX / 96f); }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = e.Bounds;
            using (var back = new SolidBrush(BackColor)) g.FillRectangle(back, r);
            if (e.Index < 0 || e.Index >= Items.Count) return;
            var page = (Page)Items[e.Index];
            bool open = (e.State & DrawItemState.Selected) != 0;
            bool contrast = Contrast;
            Rectangle patch = new Rectangle(r.X + Scaled(g, 4), r.Y + Scaled(g, 2), r.Width - Scaled(g, 8), r.Height - Scaled(g, 4));
            if (open || e.Index == hot)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath path = Rounded(patch, Scaled(g, 4)))
                {
                    if (contrast && !open)
                    {
                        using (var line = new Pen(SystemColors.Highlight)) g.DrawPath(line, path);
                    }
                    else
                    {
                        using (var fill = new SolidBrush(!open ? Hover : contrast ? SystemColors.Highlight : Open)) g.FillPath(fill, path);
                    }
                }
                if (open)
                {
                    int h = Scaled(g, 16), w = Math.Max(2, Scaled(g, 3));
                    var bar = new Rectangle(patch.X, patch.Y + (patch.Height - h) / 2, w, h);
                    using (GraphicsPath path = Rounded(bar, w / 2))
                    using (var fill = new SolidBrush(contrast ? SystemColors.HighlightText : Theme.Gold))
                        g.FillPath(fill, path);
                }
                g.SmoothingMode = SmoothingMode.Default;
            }
            Color ink = !Enabled ? SystemColors.GrayText : contrast && open ? SystemColors.HighlightText : ForeColor;
            int x = patch.X + Scaled(g, 14);
            const TextFormatFlags Middle = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            if (iconFont != null)
            {
                int box = Scaled(g, 20);
                TextRenderer.DrawText(g, page.Icon.ToString(), iconFont, new Rectangle(x, patch.Y, box, patch.Height), ink, Middle | TextFormatFlags.HorizontalCenter);
                x += box + Scaled(g, 12);
            }
            TextRenderer.DrawText(g, page.Name, Font, new Rectangle(x, patch.Y, Math.Max(1, patch.Right - x), patch.Height), ink, Middle | TextFormatFlags.EndEllipsis);
            // moving through the list with the keyboard shows where the focus is
            if ((e.State & DrawItemState.Focus) != 0 && (e.State & DrawItemState.NoFocusRect) == 0)
                ControlPaint.DrawFocusRectangle(g, patch, ink, open ? (contrast ? SystemColors.Highlight : Open) : BackColor);
        }

        static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexFromPoint(e.Location);
            if (index == ListBox.NoMatches) index = -1;
            if (index == hot) return;
            Repaint(hot);
            hot = index;
            Repaint(hot);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Repaint(hot);
            hot = -1;
        }

        void Repaint(int index)
        {
            if (index >= 0 && index < Items.Count) Invalidate(GetItemRectangle(index));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && iconFont != null) iconFont.Dispose();
            base.Dispose(disposing);
        }
    }
}
