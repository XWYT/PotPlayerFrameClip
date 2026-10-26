using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PotPlayerFrameClip
{
    internal sealed class UiTheme
    {
        internal readonly bool Dark;
        internal readonly Color Background, Surface, Text, Muted, Line, Hover, AccentColor, ErrorColor, WarningColor, Track;
        internal static readonly Color Accent = Color.FromArgb(31, 151, 113);
        internal static readonly Color Error = Color.FromArgb(212, 79, 84);
        internal static readonly Color Warning = Color.FromArgb(189, 139, 43);
        private static readonly string FontName = ChooseFont();
        private static readonly Dictionary<string, Image> Icons = new Dictionary<string, Image>();
        internal UiTheme(string mode)
        {
            Dark = mode == "dark" || (mode == "system" && SystemDark());
            Background = Dark ? Color.FromArgb(30, 34, 35) : Color.White;
            Surface = Dark ? Color.FromArgb(36, 41, 42) : Color.FromArgb(246, 248, 247);
            Text = Dark ? Color.FromArgb(238, 242, 239) : Color.FromArgb(32, 39, 37);
            Muted = Dark ? Color.FromArgb(164, 178, 172) : Color.FromArgb(89, 103, 98);
            Line = Dark ? Color.FromArgb(53, 61, 58) : Color.FromArgb(220, 226, 223);
            Hover = Dark ? Color.FromArgb(48, 56, 54) : Color.FromArgb(238, 243, 240);
            AccentColor = Dark ? Color.FromArgb(132, 221, 177) : Color.FromArgb(23, 106, 77);
            ErrorColor = Dark ? Color.FromArgb(255, 156, 168) : Color.FromArgb(172, 51, 65);
            WarningColor = Dark ? Color.FromArgb(236, 196, 135) : Color.FromArgb(136, 89, 12);
            Track = Dark ? Color.FromArgb(55, 66, 60) : Color.FromArgb(225, 232, 228);
        }
        private static string ChooseFont()
        {
            using (InstalledFontCollection fonts = new InstalledFontCollection())
                    foreach (string name in new[] { "PingFang SC", "苹方 中等", "Microsoft YaHei UI", "Segoe UI" })
                    if (fonts.Families.Any(f => f.Name == name)) return name;
            return "Segoe UI";
        }
        internal static Font Font(float size = 9.5f, FontStyle style = FontStyle.Regular) { return new Font(FontName, size, style); }
        private static bool SystemDark()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0;
            }
            catch { return false; }
        }
        internal void Apply(Control control)
        {
            control.ForeColor = Object.Equals(control.Tag, "muted") ? Muted : Text;
            control.BackColor = control is TextBoxBase || Object.Equals(control.Tag, "input") ? Surface : Background;
            Button button = control as Button;
            if (button != null && !(button is ToolButton) && !(button is CaptionButton) && !(button is CornerTarget))
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Line;
                button.FlatAppearance.MouseOverBackColor = Surface;
                button.BackColor = Surface;
            }
            foreach (Control child in control.Controls) Apply(child);
        }
        internal static Image Icon(string name, bool dark, int size)
        {
            int pixels = size <= 14 ? 14 : size <= 20 ? 20 : size <= 21 ? 21 : size <= 28 ? 28 : size <= 30 ? 30 : 40;
            if (name == "square" || name == "copy") pixels = size <= 10 ? 10 : size <= 15 ? 15 : pixels;
            string resource = "FrameClip.Icons." + name + "-" + pixels + (dark ? "-dark.png" : "-light.png");
            lock (Icons)
            {
                Image cached;
                if (Icons.TryGetValue(resource, out cached)) return cached;
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                {
                    if (stream == null) return null;
                    using (Bitmap original = new Bitmap(stream)) cached = new Bitmap(original);
                    Icons.Add(resource, cached); return cached;
                }
            }
        }
        internal Button IconButton(string name, string label, Action action, ToolTip tips)
        {
            Button button = new ToolButton(this, name, label) { Margin = new Padding(2) };
            float scale;
            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero)) scale = graphics.DpiX / 96f;
            button.Image = Icon(name, Dark, (int)(20 * scale));
            button.Click += delegate { action(); };
            tips.SetToolTip(button, label);
            return button;
        }
        internal static bool ReduceMotion(AppConfig config)
        {
            bool enabled;
            return config.ReducedMotion || (SystemParametersInfo(0x1042, 0, out enabled, 0) && !enabled);
        }
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint parameter, out bool value, uint flags);
    }

    internal static class PlayerQuery
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
            uint flags, uint timeout, out UIntPtr result);
    }

    internal sealed class ViewTabs : TabControl
    {
        private readonly UiTheme theme;
        internal ViewTabs(UiTheme theme)
        {
            this.theme = theme;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(theme.Background);
            for (int i = 0; i < TabCount; i++)
            {
                Rectangle rect = GetTabRect(i);
                TextRenderer.DrawText(e.Graphics, TabPages[i].Text, Font, rect, i == SelectedIndex ? theme.Text : theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                if (i == SelectedIndex)
                    using (Brush brush = new SolidBrush(theme.AccentColor)) e.Graphics.FillRectangle(brush, rect.Left + 10, rect.Bottom - 3, Math.Max(1, rect.Width - 20), 2);
            }
        }
    }
}
