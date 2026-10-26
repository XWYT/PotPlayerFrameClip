using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace PotPlayerFrameClip
{
    internal sealed class CaptionButton : Button
    {
        private readonly UiTheme theme;
        private readonly bool close;
        private readonly ImageAttributes muted = new ImageAttributes();
        private GraphicsPath shape;
        private bool hot, pressed;
        internal string IconName;
        internal CaptionButton(UiTheme theme, string icon, string label, bool close, Action action)
        {
            this.theme = theme; this.close = close; IconName = icon;
            AccessibleName = label; AccessibleRole = AccessibleRole.PushButton;
            Size = new Size(34, 28); Margin = new Padding(2, 0, 2, 0); TabStop = true;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            BackColor = theme.Background;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            muted.SetColorMatrix(new ColorMatrix { Matrix33 = .67f });
            Click += delegate { action(); };
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnSizeChanged(EventArgs e)
        {
            if (shape != null) shape.Dispose();
            float radius = Math.Min(Height / 4f, 6 * Height / 28f), diameter = radius * 2;
            shape = new GraphicsPath();
            shape.AddArc(0, 0, diameter, diameter, 180, 90); shape.AddArc(Width - diameter - 1, 0, diameter, diameter, 270, 90);
            shape.AddArc(Width - diameter - 1, Height - diameter - 1, diameter, diameter, 0, 90); shape.AddArc(0, Height - diameter - 1, diameter, diameter, 90, 90); shape.CloseFigure();
            base.OnSizeChanged(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(theme.Background); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (shape != null && (hot || pressed || (Focused && ShowFocusCues)))
            {
                Color fill = pressed ? (close ? (theme.Dark ? Color.FromArgb(78, 44, 47) : Color.FromArgb(249, 224, 225)) :
                    (theme.Dark ? Color.FromArgb(48, 52, 50) : Color.FromArgb(232, 237, 234))) : theme.Hover;
                using (Brush brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, shape);
                if (Focused && ShowFocusCues) using (Pen pen = new Pen(theme.Muted)) e.Graphics.DrawPath(pen, shape);
            }
            // Closed shapes look heavier than open strokes at the same nominal size.
            int logicalSize = IconName == "square" || IconName == "copy" ? 10 : 14;
            int size = Math.Max(8, (int)Math.Round(logicalSize * Height / 28.0));
            Image icon = UiTheme.Icon(IconName, theme.Dark, size);
            if (icon != null)
            {
                Rectangle target = new Rectangle((Width - size) / 2, (Height - size) / 2, size, size);
                e.Graphics.DrawImage(icon, target, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, hot || Focused ? null : muted);
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) { muted.Dispose(); if (shape != null) shape.Dispose(); } base.Dispose(disposing); }
    }
}
