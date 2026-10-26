using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PotPlayerFrameClip
{
    internal sealed class StatusStripForm : Form
    {
        internal const int LogicalWidth = 380, LogicalHeight = 76;
        private readonly ToolTip tips = new ToolTip();
        private readonly Timer transition = new Timer();
        private readonly AppConfig config;
        private readonly UiTheme theme;
        private readonly ToolButton expand, pinButton, confirm, cancel;
        private readonly bool placement;
        private float scale = 1;
        private string heading = String.Empty, detail = String.Empty;
        private double? progress;
        private bool failed;
        private Font headingFont, detailFont, buttonFont;
        internal float SurfaceScale { get { return scale; } }
        internal Rectangle HeadingBounds { get; private set; }
        internal Rectangle DetailBounds { get; private set; }
        internal Size BannerSize { get { return new Size(Width, Px(LogicalHeight)); } }
        internal StatusStripForm(AppConfig config, Action open, Action pin, bool placement = false)
        {
            SuspendLayout(); this.config = config; this.placement = placement;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false; TopMost = true; DoubleBuffered = true;
            theme = new UiTheme(config.Theme); BackColor = theme.Background; ForeColor = theme.Text;
            expand = Button("chevron-up", T("任务中心", "Task center"), open);
            pinButton = Button("pin", T("固定或取消固定横幅", "Pin or unpin banner"), pin);
            confirm = Button(null, T("确定", "Confirm"), delegate { DialogResult = DialogResult.OK; Close(); });
            cancel = Button(null, T("取消", "Cancel"), delegate { DialogResult = DialogResult.Cancel; Close(); });
            confirm.Text = confirm.AccessibleName; confirm.Primary = true; confirm.Bordered = true;
            cancel.Text = cancel.AccessibleName; cancel.Bordered = true;
            expand.Visible = pinButton.Visible = !placement; confirm.Visible = cancel.Visible = placement;
            AcceptButton = confirm; CancelButton = cancel;
            transition.Interval = 16;
            transition.Tick += delegate { Opacity = Math.Min(1, Opacity + .12); if (Opacity >= 1) transition.Stop(); };
            SetSurfaceScale(NotificationPlacement.ScaleFor(Screen.PrimaryScreen.WorkingArea));
            ResumeLayout(true);
        }
        private string T(string zh, string en) { return UiText.Choose(config.Language, zh, en); }
        private ToolButton Button(string icon, string label, Action action)
        {
            ToolButton button = new ToolButton(theme, icon, label) { LogicalHeight = 28, IconSize = 14 };
            button.Click += delegate { if (action != null) action(); }; Controls.Add(button); tips.SetToolTip(button, label); return button;
        }
        private int Px(float value) { return (int)Math.Round(value * scale); }
        internal void SetSurfaceScale(float value)
        {
            scale = Math.Max(.75f, Math.Min(4, value));
            if (headingFont != null) headingFont.Dispose(); if (detailFont != null) detailFont.Dispose(); if (buttonFont != null) buttonFont.Dispose();
            // Pixel fonts and explicit geometry share one monitor-DPI scale.
            using (Font family = UiTheme.Font())
            {
                headingFont = new Font(family.FontFamily, 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
                detailFont = new Font(family.FontFamily, 11 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
                buttonFont = new Font(family.FontFamily, 12 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            }
            foreach (Control control in Controls) control.Font = buttonFont;
            ClientSize = new Size(Px(LogicalWidth), Px(LogicalHeight + (placement ? 46 : 0)));
            LayoutSurface(); Invalidate();
        }
        private void LayoutSurface()
        {
            if (expand == null) return;
            int actionLeft = Width - Px(76);
            pinButton.Bounds = new Rectangle(actionLeft, Px(9), Px(28), Px(28));
            expand.Bounds = new Rectangle(Width - Px(42), Px(9), Px(28), Px(28));
            int textRight = placement ? Width - Px(18) : actionLeft - Px(12);
            HeadingBounds = new Rectangle(Px(18), Px(12), Math.Max(0, textRight - Px(18)), Px(22));
            DetailBounds = new Rectangle(Px(18), Px(40), Math.Max(0, Width - Px(36)), Px(18));
            confirm.Bounds = new Rectangle(Width - Px(80), Px(84), Px(64), Px(28));
            cancel.Bounds = new Rectangle(Width - Px(152), Px(84), Px(64), Px(28));
        }
        internal void ApplyAppearance()
        {
            pinButton.IconName = config.NotificationMode == "pinned" ? "pin-off" : "pin"; pinButton.Invalidate();
        }
        protected override bool ShowWithoutActivation { get { return !placement; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams p = base.CreateParams; p.ExStyle |= 0x80; if (!placement) p.ExStyle |= 0x08000000; return p; }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x21 && !placement) { message.Result = new IntPtr(3); return; }
            if (message.Msg == 0x02E0)
            {
                NativeMethods.RECT suggested = (NativeMethods.RECT)Marshal.PtrToStructure(message.LParam, typeof(NativeMethods.RECT));
                SetSurfaceScale((message.WParam.ToInt64() & 0xffff) / 96f);
                Location = NotificationPlacement.Clamp(new Point(suggested.Left, suggested.Top), Size, Screen.FromRectangle(new Rectangle(suggested.Left, suggested.Top, Width, Height)).WorkingArea);
                message.Result = IntPtr.Zero; return;
            }
            base.WndProc(ref message);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (placement && e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); }
        }
        internal void PreparePlacement(Rectangle fallback)
        {
            heading = T("将横幅拖到合适的位置", "Drag the banner into position");
            detail = T("2 项进行中 · 导出中 61%", "2 active · Exporting 61%"); progress = .61;
            Place(fallback);
        }
        private void Place(Rectangle fallback)
        {
            Rectangle area = NotificationPlacement.WorkArea(config, fallback);
            float target = NotificationPlacement.ScaleFor(area); if (Math.Abs(target - scale) > .01f) SetSurfaceScale(target);
            Location = NotificationPlacement.Location(config, area, BannerSize, Px(18));
            if (placement) Location = NotificationPlacement.Clamp(Location, Size, area);
        }
        internal void Present(string title, string subtitle, double? fraction, bool hasFailed, Rectangle area)
        {
            heading = title ?? String.Empty; detail = subtitle ?? String.Empty; progress = fraction; failed = hasFailed;
            string tooltip = heading + Environment.NewLine + detail;
            if (tips.GetToolTip(this) != tooltip) tips.SetToolTip(this, tooltip);
            ApplyAppearance(); Place(area);
            if (!Visible)
            {
                Opacity = UiTheme.ReduceMotion(config) ? 1 : .08; Show(); if (Opacity < 1) transition.Start();
            }
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (headingFont == null) return;
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath border = UiShape.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), Px(6)))
            using (Pen pen = new Pen(theme.Line)) g.DrawPath(pen, border);
            TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(g, heading, headingFont, HeadingBounds, failed ? theme.ErrorColor : theme.Text, flags);
            TextRenderer.DrawText(g, detail, detailFont, DetailBounds, theme.Muted, flags);
            Rectangle track = new Rectangle(Px(18), Px(65), Math.Max(0, Width - Px(36)), Math.Max(2, Px(2)));
            using (Brush brush = new SolidBrush(theme.Track)) g.FillRectangle(brush, track);
            if (progress.HasValue)
                using (Brush brush = new SolidBrush(failed ? theme.ErrorColor : theme.AccentColor))
                    g.FillRectangle(brush, track.Left, track.Top, (int)(track.Width * Math.Max(0, Math.Min(1, progress.Value))), track.Height);
        }
        protected override void OnVisibleChanged(EventArgs e) { if (!Visible) transition.Stop(); base.OnVisibleChanged(e); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { transition.Dispose(); tips.Dispose(); if (headingFont != null) headingFont.Dispose(); if (detailFont != null) detailFont.Dispose(); if (buttonFont != null) buttonFont.Dispose(); }
            base.Dispose(disposing);
        }
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }
}
