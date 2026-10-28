using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace LoafLock
{
    // Options → Look: like Windows' apps (the default), or always light or dark.
    enum Look { Windows, Light, Dark }

    // Light or dark windows. A window builds itself, then calls Apply; Apply again when Changed
    // fires (the option, or Windows switching its apps between light and dark). Labels say how
    // they're coloured with their Tag: Theme.DimTag (grey) or Theme.BadTag (a problem, red);
    // everything else takes the window's colours. High contrast always gets Windows' own colours.
    static class Theme
    {
        public const string DimTag = "dim", BadTag = "bad";

        public static bool Dark { get; private set; }
        public static event Action Changed;

        static Look choice = Look.Windows;

        public static void Set(Look look)
        {
            choice = look;
            Update();
        }

        // looks again at the option and at Windows' setting
        public static void Update()
        {
            bool dark = !SystemInformation.HighContrast && (choice == Look.Dark || (choice == Look.Windows && WindowsAppsAreDark()));
            if (dark == Dark) return;
            Dark = dark;
            if (Changed != null) Changed();
        }

        static bool WindowsAppsAreDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object light = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return light is int && (int)light == 0;
                }
            }
            catch { return false; }
        }

        public static Color Back { get { return Dark ? Color.FromArgb(32, 32, 32) : SystemColors.Window; } }
        public static Color Text { get { return Dark ? Color.FromArgb(240, 240, 240) : SystemColors.ControlText; } }
        public static Color Dim { get { return Dark ? Color.FromArgb(160, 160, 160) : SystemColors.GrayText; } }
        public static Color Gold { get { return Dark ? Color.FromArgb(240, 185, 60) : Color.FromArgb(160, 110, 0); } }
        public static Color Bad { get { return Dark ? Color.FromArgb(255, 120, 110) : Color.FromArgb(192, 32, 32); } }
        static Color Field { get { return Color.FromArgb(48, 48, 48); } }   // dark only: text boxes, buttons
        static Color Hover { get { return Color.FromArgb(62, 62, 62); } }
        static Color Edge { get { return Color.FromArgb(96, 96, 96); } }

        public static void Apply(Form form)
        {
            form.BackColor = Back;
            form.ForeColor = Text;
            Paint(form);
            // the title bar: once the window has one, and again whenever it's made afresh
            form.HandleCreated -= TitleBarOnCreate;
            form.HandleCreated += TitleBarOnCreate;
            if (form.IsHandleCreated) TitleBar(form.Handle);
        }

        // a menu on its own (Rukus's menu in the taskbar corner)
        public static void Apply(ToolStrip strip)
        {
            if (Dark) strip.Renderer = DarkMenus;
            else strip.RenderMode = ToolStripRenderMode.ManagerRenderMode;
            if (strip is MenuStrip) strip.BackColor = Dark ? DarkTable.Panel : SystemColors.Window;  // the window's menu bar
            else if (Dark) strip.BackColor = DarkTable.Menu;
            else strip.ResetBackColor();
            foreach (ToolStripItem item in strip.Items) StyleItem(item);
        }

        static void Paint(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                string tag = c.Tag as string;
                if (c is ToolStrip) Apply((ToolStrip)c);
                else if (c is Button) StyleButton((Button)c);
                else if (c is TextBox)
                {
                    var box = (TextBox)c;
                    box.BackColor = Dark ? Field : SystemColors.Window;
                    box.ForeColor = Dark ? Text : SystemColors.WindowText;
                    box.BorderStyle = Dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                }
                else if (c is LinkLabel)
                {
                    // light: a new LinkLabel's own colours (high contrast's, when it's on)
                    var link = (LinkLabel)c;
                    var plain = new LinkLabel();
                    link.LinkColor = Dark ? Color.FromArgb(120, 175, 255) : plain.LinkColor;
                    link.ActiveLinkColor = Dark ? Color.FromArgb(170, 205, 255) : plain.ActiveLinkColor;
                    link.DisabledLinkColor = Dark ? Dim : plain.DisabledLinkColor;
                    plain.Dispose();
                }
                else if (tag == DimTag) c.ForeColor = Dim;
                else if (tag == BadTag) c.ForeColor = Bad;
                Paint(c);
            }
        }

        static void StyleButton(Button b)
        {
            if (Dark)
            {
                b.FlatStyle = FlatStyle.Flat;
                b.UseVisualStyleBackColor = false;
                b.BackColor = Field;
                b.ForeColor = Text;
                b.FlatAppearance.BorderColor = Edge;
                b.FlatAppearance.MouseOverBackColor = Hover;
                b.FlatAppearance.MouseDownBackColor = Edge;
            }
            else
            {
                b.FlatStyle = FlatStyle.Standard;
                b.ResetBackColor();
                b.ResetForeColor();
                b.UseVisualStyleBackColor = true;
            }
        }

        static void StyleItem(ToolStripItem item)
        {
            item.ForeColor = Dark ? Text : SystemColors.ControlText;
            var parent = item as ToolStripDropDownItem;
            if (parent == null || !parent.HasDropDownItems) return;
            if (Dark) parent.DropDown.Renderer = DarkMenus;
            else parent.DropDown.RenderMode = ToolStripRenderMode.ManagerRenderMode;
            foreach (ToolStripItem child in parent.DropDownItems) StyleItem(child);
        }

        static void TitleBarOnCreate(object sender, EventArgs e)
        {
            TitleBar(((Control)sender).Handle);
        }

        // Windows 10 (from 2020) and 11 draw a dark title bar when asked; older ones just don't
        static void TitleBar(IntPtr hwnd)
        {
            int on = Dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));  // Windows 10 before 2020
        }

        // ---- dark menus ----

        static readonly ToolStripRenderer DarkMenus = new DarkRenderer();

        sealed class DarkRenderer : ToolStripProfessionalRenderer
        {
            public DarkRenderer() : base(new DarkTable()) { RoundedEdges = false; }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? Text : Dim;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = Text;
                base.OnRenderArrow(e);
            }
        }

        sealed class DarkTable : ProfessionalColorTable
        {
            public static readonly Color Panel = Color.FromArgb(32, 32, 32), Menu = Color.FromArgb(43, 43, 43), Lit = Color.FromArgb(65, 65, 65), Line = Color.FromArgb(75, 75, 75);

            public DarkTable() { UseSystemColors = false; }

            public override Color MenuStripGradientBegin { get { return Panel; } }
            public override Color MenuStripGradientEnd { get { return Panel; } }
            public override Color ToolStripDropDownBackground { get { return Menu; } }
            public override Color ImageMarginGradientBegin { get { return Menu; } }
            public override Color ImageMarginGradientMiddle { get { return Menu; } }
            public override Color ImageMarginGradientEnd { get { return Menu; } }
            public override Color MenuBorder { get { return Line; } }
            public override Color MenuItemBorder { get { return Lit; } }
            public override Color MenuItemSelected { get { return Lit; } }
            public override Color MenuItemSelectedGradientBegin { get { return Lit; } }
            public override Color MenuItemSelectedGradientEnd { get { return Lit; } }
            public override Color MenuItemPressedGradientBegin { get { return Menu; } }
            public override Color MenuItemPressedGradientMiddle { get { return Menu; } }
            public override Color MenuItemPressedGradientEnd { get { return Menu; } }
            public override Color SeparatorDark { get { return Line; } }
            public override Color SeparatorLight { get { return Menu; } }
        }

        // ---- Windows ----

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
