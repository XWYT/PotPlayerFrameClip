using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PotPlayerFrameClip
{
    internal class WorkspaceForm : Form
    {
        internal readonly Panel Content;
        private readonly UiTheme theme;
        private readonly Panel titleBar;
        private readonly Label title;
        private readonly CaptionButton minimize, maximize, close;
        private readonly ToolTip tips = new ToolTip();
        private readonly AppConfig config;
        private readonly System.Windows.Forms.Timer appearance = new System.Windows.Forms.Timer();
        internal float UiScale { get { return titleBar == null ? 1 : titleBar.Height / 52f; } }
        internal WorkspaceForm(AppConfig config)
        {
            this.config = config;
            theme = new UiTheme(config.Theme);
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(1);
            DoubleBuffered = true;
            Content = new Panel { Dock = DockStyle.Fill, BackColor = theme.Background };
            titleBar = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = theme.Background };
            Control brand = new BrandMark(theme) { Dock = DockStyle.Left, Width = 48 };
            title = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, ForeColor = theme.Text, Font = UiTheme.Font(10.5f, FontStyle.Bold) };
            minimize = CreateCaption("minus", UiText.Choose(config.Language, "最小化", "Minimize"), delegate { WindowState = FormWindowState.Minimized; });
            maximize = CreateCaption("square", UiText.Choose(config.Language, "最大化或还原", "Maximize or restore"), ToggleMaximize);
            close = CreateCaption("x", UiText.Choose(config.Language, "关闭", "Close"), Close);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 126, Padding = new Padding(2, 12, 8, 12), FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = theme.Background };
            actions.Controls.AddRange(new Control[] { close, maximize, minimize });
            minimize.TabIndex = 0; maximize.TabIndex = 1; close.TabIndex = 2;
            titleBar.Controls.Add(title); titleBar.Controls.Add(brand);
            titleBar.Controls.Add(actions);
            titleBar.Paint += delegate(object sender, PaintEventArgs args) { using (Pen pen = new Pen(theme.Line)) args.Graphics.DrawLine(pen, 0, titleBar.Height - 1, titleBar.Width, titleBar.Height - 1); };
            Controls.Add(Content); Controls.Add(titleBar);
            foreach (Control control in new Control[] { titleBar, title, brand }) control.MouseDown += Drag;
            BackColor = theme.Background;
            appearance.Interval = 16;
            appearance.Tick += delegate { Opacity = Math.Min(1, Opacity + .12); if (Opacity >= 1) appearance.Stop(); };
        }
        private CaptionButton CreateCaption(string icon, string label, Action action)
        {
            CaptionButton button = new CaptionButton(theme, icon, label, icon == "x", action);
            tips.SetToolTip(button, label);
            return button;
        }
        private void ToggleMaximize()
        {
            if (MaximizeBox) WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        }
        private void Drag(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (e.Clicks > 1) { ToggleMaximize(); return; }
            ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
        }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (title != null) title.Text = Text; }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (maximize != null)
            {
                string icon = WindowState == FormWindowState.Maximized ? "copy" : "square";
                if (maximize.IconName != icon) { maximize.IconName = icon; maximize.Invalidate(); }
            }
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (minimize != null) { minimize.Visible = MinimizeBox; maximize.Visible = MaximizeBox; }
            base.OnLayout(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen pen = new Pen(theme.Line)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        protected override void WndProc(ref Message message)
        {
            // The entire HWND is client area, including both NCCALCSIZE forms.
            // Retain resize/system-menu styles without letting USER32 paint a
            // fallback caption over our own title bar on activation or resize.
            if (message.Msg == 0x83 || message.Msg == 0x85) { message.Result = IntPtr.Zero; return; }
            if (message.Msg == 0x86) { message.Result = new IntPtr(1); return; }
            if (message.Msg == 0x84 && WindowState == FormWindowState.Normal)
            {
                Point point = PointToClient(new Point((short)(message.LParam.ToInt64() & 0xffff), (short)((message.LParam.ToInt64() >> 16) & 0xffff)));
                int grip = Math.Max(6, titleBar.Height / 7);
                bool left = point.X < grip, right = point.X >= ClientSize.Width - grip;
                bool top = point.Y < grip, bottom = point.Y >= ClientSize.Height - grip;
                int hit = top && left ? 13 : top && right ? 14 : bottom && left ? 16 : bottom && right ? 17 : left ? 10 : right ? 11 : top ? 12 : bottom ? 15 : 0;
                if (hit != 0) { message.Result = new IntPtr(hit); return; }
            }
            if (message.Msg == 0x24)
            {
                MinMax info = (MinMax)Marshal.PtrToStructure(message.LParam, typeof(MinMax));
                Screen screen = Screen.FromHandle(Handle); Rectangle work = screen.WorkingArea, bounds = screen.Bounds;
                info.MaxPosition = new Point(work.Left - bounds.Left, work.Top - bounds.Top); info.MaxSize = work.Size;
                info.MinTrack = MinimumSize;
                // USER32's desktop-sized default can be smaller than a DPI-scaled
                // minimum, clamping the window before its content finishes scaling.
                info.MaxTrack = new Size(Math.Max(info.MaxTrack.Width, info.MinTrack.Width), Math.Max(info.MaxTrack.Height, info.MinTrack.Height));
                Marshal.StructureToPtr(info, message.LParam, false); message.Result = IntPtr.Zero; return;
            }
            base.WndProc(ref message);
        }
        protected override CreateParams CreateParams
        {
            get { CreateParams value = base.CreateParams; value.Style |= 0x00040000 | 0x00080000; return value; }
        }
        protected override void OnShown(EventArgs e)
        {
            if (!UiTheme.ReduceMotion(config)) { Opacity = .08; appearance.Start(); }
            base.OnShown(e);
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int disabled = 1; DwmSetWindowAttribute(Handle, 2, ref disabled, sizeof(int));
                int preference = 2; DwmSetWindowAttribute(Handle, 33, ref preference, sizeof(int));
            }
            catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        }
        protected override void Dispose(bool disposing) { if (disposing) { appearance.Dispose(); tips.Dispose(); title.Font.Dispose(); } base.Dispose(disposing); }
        [StructLayout(LayoutKind.Sequential)] private struct MinMax { internal Point Reserved; internal Size MaxSize; internal Point MaxPosition; internal Size MinTrack, MaxTrack; }
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }
}
