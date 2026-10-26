using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PotPlayerFrameClip
{
    internal static class NotificationPlacement
    {
        internal static Rectangle WorkArea(AppConfig config, Rectangle fallback)
        {
            if (String.IsNullOrEmpty(config.NotificationMonitor)) return fallback;
            foreach (Screen screen in Screen.AllScreens)
                if (String.Equals(screen.DeviceName, config.NotificationMonitor, StringComparison.OrdinalIgnoreCase)) return screen.WorkingArea;
            return Screen.PrimaryScreen.WorkingArea;
        }
        internal static Point Location(AppConfig config, Rectangle area, Size size, int margin)
        {
            int left = area.Left + margin, right = Math.Max(area.Left, area.Right - size.Width - margin);
            int top = area.Top + margin, bottom = Math.Max(area.Top, area.Bottom - size.Height - margin);
            if (config.NotificationPosition == "custom")
                return Clamp(new Point(area.Left + (int)Math.Round(Math.Max(0, area.Width - size.Width) * config.NotificationX), area.Top + (int)Math.Round(Math.Max(0, area.Height - size.Height) * config.NotificationY)), size, area);
            return Clamp(new Point(config.NotificationPosition.EndsWith("left", StringComparison.Ordinal) ? left : right, config.NotificationPosition.StartsWith("top", StringComparison.Ordinal) ? top : bottom), size, area);
        }
        internal static Point Clamp(Point point, Size size, Rectangle area)
        {
            return new Point(Math.Max(area.Left, Math.Min(point.X, area.Right - size.Width)), Math.Max(area.Top, Math.Min(point.Y, area.Bottom - size.Height)));
        }
        internal static void Capture(AppConfig config, Point point, Size banner, Rectangle area, string monitor)
        {
            point = Clamp(point, banner, area); config.NotificationPosition = "custom"; config.NotificationMonitor = monitor;
            config.NotificationX = area.Width <= banner.Width ? 0 : (point.X - area.Left) / (double)(area.Width - banner.Width);
            config.NotificationY = area.Height <= banner.Height ? 0 : (point.Y - area.Top) / (double)(area.Height - banner.Height);
        }
        internal static float ScaleFor(Rectangle area)
        {
            try
            {
                NativeMethods.RECT rect = new NativeMethods.RECT { Left = area.Left, Top = area.Top, Right = area.Right, Bottom = area.Bottom };
                uint x, y; if (GetDpiForMonitor(MonitorFromRect(ref rect, 2), 0, out x, out y) == 0) return x / 96f;
            }
            catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
        }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref NativeMethods.RECT rect, uint flags);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    }
}
