using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace LoafLock
{
    static class AppIcons
    {
        // one of the icons built into the exe, at the size given
        public static Icon Load(string name, Size size)
        {
            using (Stream res = Assembly.GetExecutingAssembly().GetManifestResourceStream("LoafLock." + name))
                return res == null ? SystemIcons.Application : new Icon(res, size);
        }
    }

    // Rukus in the taskbar corner. So far it only shows the version and quits; locking comes next.
    sealed class Tray : IDisposable
    {
        readonly NotifyIcon icon = new NotifyIcon();
        readonly Icon unlocked = AppIcons.Load("loaflock.ico", SystemInformation.SmallIconSize);

        public Tray()
        {
            var menu = new ContextMenuStrip { ShowImageMargin = false };
            menu.Items.Add(new ToolStripMenuItem(Program.VersionText) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit", null, delegate { Application.ExitThread(); });
            icon.ContextMenuStrip = menu;
            icon.Icon = unlocked;
            icon.Text = "LoafLock";
            icon.Visible = true;
        }

        public void Dispose()
        {
            icon.Visible = false;
            icon.Dispose();
            unlocked.Dispose();
        }
    }
}
