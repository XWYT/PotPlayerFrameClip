using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace PotPlayerFrameClip
{
    internal sealed class CornerTarget : Button
    {
        private readonly UiTheme theme;
        private readonly int corner;
        private bool hot, pressed;
        internal bool Selected;
        internal void NotifySelection() { Invalidate(); AccessibilityNotifyClients(AccessibleEvents.StateChange, -1); }
        internal CornerTarget(UiTheme theme, int corner, string label)
        {
            this.theme = theme; this.corner = corner; Text = String.Empty; AccessibleName = label;
            AccessibleRole = AccessibleRole.RadioButton; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override bool IsInputKey(Keys keyData) { Keys key = keyData & Keys.KeyCode; return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || base.IsInputKey(keyData); }
        protected override AccessibleObject CreateAccessibilityInstance() { return new CornerAccessible(this); }
        private sealed class CornerAccessible : ControlAccessibleObject
        {
            private readonly CornerTarget target;
            internal CornerAccessible(CornerTarget target) : base(target) { this.target = target; }
            public override AccessibleStates State { get { return base.State | (target.Selected ? AccessibleStates.Checked : AccessibleStates.None); } }
            public override string DefaultAction { get { return target.AccessibleName; } }
            public override void DoDefaultAction() { target.PerformClick(); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Height / 34f;
            using (GraphicsPath area = UiShape.Round(new RectangleF(.75f, .75f, Width - 1.5f, Height - 1.5f), 4 * s))
            {
                using (Brush fill = new SolidBrush(pressed ? theme.Line : hot ? theme.Hover : Selected ? theme.Track : theme.Surface)) e.Graphics.FillPath(fill, area);
                if (Selected || (Focused && ShowFocusCues))
                    using (Pen pen = new Pen(theme.AccentColor, Math.Max(1, s))) e.Graphics.DrawPath(pen, area);
            }
            // The miniature banner previews the chosen corner, not a textual direction.
            float width = 32 * s, height = 13 * s;
            RectangleF banner = new RectangleF(corner % 2 == 0 ? 8 * s : Width - width - 8 * s, corner < 2 ? 6 * s : Height - height - 6 * s, width, height);
            Color ink = Selected ? theme.AccentColor : theme.Muted;
            using (GraphicsPath path = UiShape.Round(banner, 3 * s))
            using (Pen pen = new Pen(ink, Math.Max(1, s))) e.Graphics.DrawPath(pen, path);
            using (Pen pen = new Pen(ink, Math.Max(1, s)))
            {
                e.Graphics.DrawLine(pen, banner.Left + 5 * s, banner.Top + 5 * s, banner.Right - 5 * s, banner.Top + 5 * s);
                e.Graphics.DrawLine(pen, banner.Left + 5 * s, banner.Top + 9 * s, banner.Right - 12 * s, banner.Top + 9 * s);
            }
        }
    }

    internal sealed class CornerPicker : UserControl
    {
        private readonly UiTheme theme;
        private readonly CornerTarget[] corners;
        private readonly ToolTip tips = new ToolTip();
        private readonly string[] values = { "top-left", "top-right", "bottom-left", "bottom-right" };
        private string selected;
        internal event EventHandler ValueChanged;
        internal string Value
        {
            get { return selected; }
            set { if (selected == value) return; selected = value; for (int i = 0; i < 4; i++) { corners[i].Selected = values[i] == value; corners[i].NotifySelection(); } if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
        }
        internal CornerPicker(UiTheme theme, string language, string value)
        {
            SuspendLayout(); this.theme = theme; Size = new Size(224, 116); DoubleBuffered = true;
            string[] labels = language == UiText.English ? new[] { "Top left", "Top right", "Bottom left", "Bottom right" } : new[] { "左上", "右上", "左下", "右下" };
            corners = new CornerTarget[4];
            for (int i = 0; i < 4; i++)
            {
                int index = i; CornerTarget button = new CornerTarget(theme, i, labels[i]);
                button.Click += delegate { Value = values[index]; };
                button.KeyDown += delegate(object sender, KeyEventArgs e) {
                    int next = index;
                    if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) next = index ^ 1;
                    else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) next = index ^ 2;
                    else return;
                    Value = values[next]; corners[next].Focus(); e.Handled = true; e.SuppressKeyPress = true;
                };
                corners[i] = button; Controls.Add(button); tips.SetToolTip(button, labels[i]);
            }
            Value = value; ResumeLayout(true);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (corners == null || corners.Any(c => c == null)) return; float s = Height / 116f;
            for (int i = 0; i < 4; i++) corners[i].Bounds = new Rectangle((int)((i % 2 == 0 ? 12 : 116) * s), (int)((i < 2 ? 12 : 60) * s), (int)(96 * s), (int)(34 * s));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); float s = Height / 116f; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = UiShape.Round(new RectangleF(.5f, .5f, Width - 1, Height - 9 * s), 6 * s))
            using (Pen pen = new Pen(theme.Line)) { e.Graphics.DrawPath(pen, path); e.Graphics.DrawLine(pen, Width / 2, Height - 9 * s, Width / 2, Height - s); e.Graphics.DrawLine(pen, Width / 2 - 18 * s, Height - s, Width / 2 + 18 * s, Height - s); }
        }
        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class NotificationPositionView : Panel
    {
        private readonly AppConfig draft;
        private readonly UiTheme theme;
        private readonly string language;
        private readonly Label title, status;
        private readonly ChoiceBox monitors;
        private readonly CornerPicker corners;
        private readonly ToolButton custom;
        internal event EventHandler ValueChanged;
        internal event EventHandler PlacementConfirmed;
        internal NotificationPositionView(AppConfig config, UiTheme theme)
        {
            SuspendLayout(); draft = config.Copy(); this.theme = theme; language = config.Language; Height = 210; Margin = new Padding(0);
            title = new Label { Text = T("提醒位置", "Notification position"), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            FormatChoice[] choices = new[] { new FormatChoice("", T("跟随播放器所在屏幕", "Player's display")) }.Concat(Screen.AllScreens.Select((s, i) => new FormatChoice(s.DeviceName, T("显示器 ", "Display ") + (i + 1) + (s.Primary ? T(" · 主屏", " · Primary") : "") + " · " + s.Bounds.Width + "×" + s.Bounds.Height))).ToArray();
            if (!choices.Any(c => c.Value == draft.NotificationMonitor)) choices = choices.Concat(new[] { new FormatChoice(draft.NotificationMonitor, T("已断开的显示器", "Disconnected display")) }).ToArray();
            monitors = new ChoiceBox(theme, choices, draft.NotificationMonitor, T("提醒显示器", "Notification display"));
            corners = new CornerPicker(theme, language, draft.NotificationPosition);
            custom = new ToolButton(theme) { Text = T("自定义摆放…", "Custom position…"), Bordered = true, Size = new Size(156, 32), AccessibleName = T("自定义摆放提醒", "Place the notification") };
            status = new Label { AutoEllipsis = true, Tag = "muted", ForeColor = theme.Muted };
            Controls.AddRange(new Control[] { title, monitors, corners, custom, status });
            monitors.ValueChanged += delegate { draft.NotificationMonitor = monitors.Value; Changed(); };
            corners.ValueChanged += delegate { draft.NotificationPosition = corners.Value; Changed(); };
            custom.Click += delegate { ChooseCustom(); }; UpdateStatus(); ResumeLayout(true);
        }
        private string T(string zh, string en) { return UiText.Choose(language, zh, en); }
        private void Changed() { UpdateStatus(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); }
        private void UpdateStatus() { status.Text = draft.NotificationPosition == "custom" ? T("自定义位置", "Custom position") : String.Empty; }
        internal void CopyTo(AppConfig target)
        {
            target.NotificationPosition = draft.NotificationPosition; target.NotificationMonitor = draft.NotificationMonitor;
            target.NotificationX = draft.NotificationX; target.NotificationY = draft.NotificationY;
        }
        internal void PreviewTheme(string mode) { draft.Theme = mode; }
        internal void ChooseCustom()
        {
            AppConfig next = draft.Copy();
            using (StatusStripForm preview = new StatusStripForm(next, null, null, true))
            {
                Form owner = FindForm(); Rectangle area = owner == null ? Screen.PrimaryScreen.WorkingArea : Screen.FromControl(owner).WorkingArea;
                preview.PreparePlacement(area);
                if (preview.ShowDialog(owner) != DialogResult.OK) return;
                Screen screen = Screen.FromRectangle(new Rectangle(preview.Location, preview.BannerSize));
                NotificationPlacement.Capture(draft, preview.Location, preview.BannerSize, screen.WorkingArea, screen.DeviceName);
                monitors.Value = screen.DeviceName; corners.Value = "custom"; Changed();
                if (PlacementConfirmed != null) PlacementConfirmed(this, EventArgs.Empty);
            }
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (status == null) return; float s = Height / 210f;
            int right = (int)(Math.Min(320, Width / s * .6f) * s);
            title.Bounds = new Rectangle(0, (int)(13 * s), Math.Max(20, Width - right - (int)(16 * s)), (int)(32 * s));
            monitors.Bounds = new Rectangle(Width - right, (int)(12 * s), right, (int)(34 * s));
            corners.Bounds = new Rectangle(Width - (int)(224 * s), (int)(68 * s), (int)(224 * s), (int)(116 * s));
            custom.Bounds = new Rectangle(0, (int)(82 * s), (int)(156 * s), (int)(32 * s));
            status.Bounds = new Rectangle(0, (int)(126 * s), Math.Max(20, corners.Left - (int)(20 * s)), (int)(44 * s));
        }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (Pen pen = new Pen(theme.Line)) e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1); }
    }
}
