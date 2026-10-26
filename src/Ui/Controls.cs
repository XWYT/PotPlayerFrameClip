using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PotPlayerFrameClip
{
    internal sealed class BrandMark : Control
    {
        private readonly UiTheme theme;
        internal BrandMark(UiTheme theme) { this.theme = theme; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(theme.Background);
            int size = (int)(18 * Height / 52f); Image image = UiTheme.Icon("scan-line", theme.Dark, size);
            if (image != null) e.Graphics.DrawImage(image, new Rectangle((Width - size) / 2, (Height - size) / 2, size, size));
        }
    }

    internal static class UiScale
    {
        internal static float For(Control control) { WorkspaceForm form = control.FindForm() as WorkspaceForm; return form == null ? 1 : form.UiScale; }
    }

    internal static class UiShape
    {
        internal static GraphicsPath Round(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
    }

    internal sealed class ToolButton : Button
    {
        private readonly UiTheme theme;
        private bool hot, pressed;
        internal bool Bordered, Primary;
        internal string IconName;
        internal int LogicalHeight = 32, IconSize = 18;
        internal ToolButton(UiTheme theme, string icon = null, string label = null)
        {
            this.theme = theme; IconName = icon;
            Size = new Size(32, 32); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            AccessibleName = label; Tag = icon; Text = String.Empty; BackColor = theme.Background;
            if (icon != null) Image = UiTheme.Icon(icon, theme.Dark, 18);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Height / (float)LogicalHeight;
            using (GraphicsPath path = UiShape.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 4 * scale))
            {
                if ((Primary && Enabled) || (Enabled && (hot || pressed || (Focused && ShowFocusCues))))
                    using (Brush brush = new SolidBrush(Primary ? (pressed ? ControlPaint.Dark(theme.AccentColor, .12f) : hot ? ControlPaint.Light(theme.AccentColor, .08f) : theme.AccentColor) : pressed ? theme.Line : theme.Hover)) e.Graphics.FillPath(brush, path);
                if (Bordered || (Focused && ShowFocusCues))
                    using (Pen pen = new Pen(Focused && ShowFocusCues ? theme.AccentColor : theme.Line)) e.Graphics.DrawPath(pen, path);
            }
            Color ink = !Enabled ? theme.Muted : Primary ? (theme.Dark ? theme.Background : Color.White) : theme.Text;
            int size = (int)Math.Round(IconSize * scale);
            Image icon = IconName == null ? null : UiTheme.Icon(IconName, theme.Dark, size);
            int textWidth = Text.Length == 0 ? 0 : TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            int gap = icon != null && textWidth > 0 ? (int)(7 * scale) : 0;
            int left = (Width - (icon != null ? size : 0) - textWidth - gap) / 2;
            if (icon != null) e.Graphics.DrawImage(icon, new Rectangle(left, (Height - size) / 2, size, size));
            if (textWidth > 0) TextRenderer.DrawText(e.Graphics, Text, Font, icon == null ? ClientRectangle : new Rectangle(left + size + gap, 0, textWidth + 1, Height), ink,
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    internal sealed class ToggleSwitch : CheckBox
    {
        private readonly UiTheme theme;
        internal ToggleSwitch(UiTheme theme, string label)
        {
            this.theme = theme; AccessibleName = label; AutoSize = false; Size = new Size(34, 20);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Height / 20f;
            using (GraphicsPath track = UiShape.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), Height / 2f))
            {
                using (Brush brush = new SolidBrush(Checked ? theme.AccentColor : theme.Track)) e.Graphics.FillPath(brush, track);
                using (Pen pen = new Pen(Checked ? theme.AccentColor : theme.Muted)) e.Graphics.DrawPath(pen, track);
            }
            using (Brush brush = new SolidBrush(Checked ? theme.Background : theme.Muted))
                e.Graphics.FillEllipse(brush, (Checked ? 18 : 4) * scale, 4 * scale, 12 * scale, 12 * scale);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, theme.AccentColor, BackColor);
        }
    }

    internal sealed class NavigationStrip : Control
    {
        private readonly UiTheme theme;
        private readonly string[] items;
        private readonly bool segmented;
        private int selected;
        internal event EventHandler SelectedIndexChanged;
        internal int SelectedIndex
        {
            get { return selected; }
            set { if (value == selected || value < 0 || value >= items.Length) return; selected = value; Invalidate(); if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty); }
        }
        internal NavigationStrip(UiTheme theme, bool segmented, params string[] items)
        {
            this.theme = theme; this.items = items; this.segmented = segmented;
            Height = segmented ? 44 : 46; TabStop = true; AccessibleRole = AccessibleRole.PageTabList;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        }
        private Rectangle Item(int index)
        {
            float scale = Height / (segmented ? 44f : 46f);
            int left = (int)(24 * scale), gap = (int)((segmented ? 6 : 26) * scale);
            for (int i = 0; i < index; i++) left += TextRenderer.MeasureText(items[i], Font, Size.Empty, TextFormatFlags.NoPadding).Width + (segmented ? (int)(24 * scale) : 0) + gap;
            int width = TextRenderer.MeasureText(items[index], Font, Size.Empty, TextFormatFlags.NoPadding).Width + (segmented ? (int)(24 * scale) : 0);
            return new Rectangle(left, segmented ? (int)(6 * scale) : 0, width, segmented ? Height - (int)(12 * scale) : Height);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
            Focus(); for (int i = 0; i < items.Length; i++) if (Item(i).Contains(e.Location)) SelectedIndex = i;
        }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Home || keyData == Keys.End || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) { SelectedIndex = (selected + (e.KeyCode == Keys.Right ? 1 : items.Length - 1)) % items.Length; e.Handled = true; }
            else if (e.KeyCode == Keys.Home || e.KeyCode == Keys.End) { SelectedIndex = e.KeyCode == Keys.Home ? 0 : items.Length - 1; e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(theme.Background); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < items.Length; i++)
            {
                Rectangle rect = Item(i);
                if (i == selected && segmented) using (GraphicsPath path = UiShape.Round(rect, 4)) using (Brush brush = new SolidBrush(theme.Hover)) e.Graphics.FillPath(brush, path);
                TextRenderer.DrawText(e.Graphics, items[i], Font, rect, i == selected ? theme.Text : theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                if (i == selected && !segmented) using (Brush brush = new SolidBrush(theme.AccentColor)) e.Graphics.FillRectangle(brush, rect.Left, Height - 2, rect.Width, 2);
                if (i == selected && Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(rect, -1, -3), theme.AccentColor, theme.Background);
            }
            if (!segmented) using (Pen pen = new Pen(theme.Line)) e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
        }
    }

    internal sealed class ChoiceBox : Control
    {
        private readonly UiTheme theme;
        private readonly FormatChoice[] choices;
        private ContextMenuStrip menu;
        private int index;
        internal int PreferredWidth
        {
            get
            {
                int textWidth = 0;
                foreach (FormatChoice choice in choices)
                    textWidth = Math.Max(textWidth, TextRenderer.MeasureText(choice.ToString(), Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width);
                return textWidth + (int)Math.Ceiling(44 * Height / 34f);
            }
        }
        internal event EventHandler ValueChanged;
        internal string Value
        {
            get { return choices[index].Value; }
            set { for (int i = 0; i < choices.Length; i++) if (choices[i].Value == value) { SetIndex(i); return; } }
        }
        internal ChoiceBox(UiTheme theme, FormatChoice[] choices, string value, string label)
        {
            this.theme = theme; this.choices = choices; AccessibleName = label; AccessibleRole = AccessibleRole.ComboBox;
            Size = new Size(245, 34); TabStop = true;
            for (int i = 0; i < choices.Length; i++) if (choices[i].Value == value) index = i;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
        }
        private void SetIndex(int value)
        {
            if (value == index) return; index = value; Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if ((e.Alt && e.KeyCode == Keys.Down) || e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Popup(); e.Handled = true; }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) { SetIndex(Math.Max(0, Math.Min(choices.Length - 1, index + (e.KeyCode == Keys.Down ? 1 : -1)))); e.Handled = true; }
            base.OnKeyDown(e);
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { Focus(); Popup(); } }
        private void Popup()
        {
            if (menu == null)
            {
                menu = new ContextMenuStrip { ShowImageMargin = false };
                for (int i = 0; i < choices.Length; i++)
                {
                    int selected = i;
                    ToolStripMenuItem item = new ToolStripMenuItem(choices[i].ToString());
                    item.Click += delegate { SetIndex(selected); }; menu.Items.Add(item);
                }
            }
            menu.Font = Font; menu.BackColor = theme.Background; menu.ForeColor = theme.Text;
            for (int i = 0; i < choices.Length; i++) ((ToolStripMenuItem)menu.Items[i]).Checked = i == index;
            menu.Show(this, new Point(0, Height));
        }
        protected override void Dispose(bool disposing) { if (disposing && menu != null) menu.Dispose(); base.Dispose(disposing); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(theme.Background); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = Height / 34f;
            using (GraphicsPath path = UiShape.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 4 * scale))
            using (Pen pen = new Pen(Focused && ShowFocusCues ? theme.AccentColor : theme.Line)) e.Graphics.DrawPath(pen, path);
            int iconSize = (int)(14 * scale), inset = (int)(10 * scale);
            Image icon = UiTheme.Icon("chevron-down", theme.Dark, iconSize);
            if (icon != null) e.Graphics.DrawImage(icon, new Rectangle(Width - inset - iconSize, (Height - iconSize) / 2, iconSize, iconSize));
            TextRenderer.DrawText(e.Graphics, choices[index].ToString(), Font, new Rectangle(inset, 0, Width - inset * 3 - iconSize, Height), theme.Text,
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class NumericStepper : UserControl
    {
        private readonly ToolButton less, more;
        private readonly TextBox number;
        private readonly UiTheme theme;
        private int value = 2;
        internal event EventHandler ValueChanged;
        internal int Value
        {
            get { return value; }
            set
            {
                int next = Math.Max(1, Math.Min(4, value));
                if (this.value == next) return; this.value = next; number.Text = next.ToString();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }
        internal NumericStepper(UiTheme theme, int initial, string label)
        {
            this.theme = theme; AccessibleName = label; Size = new Size(103, 34); BackColor = theme.Background;
            less = new ToolButton(theme, "minus", "-1") { LogicalHeight = 34, IconSize = 13 };
            more = new ToolButton(theme, "plus", "+1") { LogicalHeight = 34, IconSize = 13 };
            number = new TextBox { BorderStyle = BorderStyle.None, TextAlign = HorizontalAlignment.Center, Text = initial.ToString(), BackColor = theme.Background, ForeColor = theme.Text, AccessibleName = label, MaxLength = 1 };
            value = initial; less.Click += delegate { Value--; }; more.Click += delegate { Value++; };
            number.Validated += delegate { int parsed; if (Int32.TryParse(number.Text, out parsed)) Value = parsed; number.Text = Value.ToString(); };
            number.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Up) { Value++; e.Handled = true; } else if (e.KeyCode == Keys.Down) { Value--; e.Handled = true; } };
            Controls.AddRange(new Control[] { less, number, more });
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (number == null) return;
            int width = Height - 2; less.Bounds = new Rectangle(1, 1, width, Height - 2); more.Bounds = new Rectangle(Width - width - 1, 1, width, Height - 2);
            number.Bounds = new Rectangle(width + 1, (Height - number.PreferredHeight) / 2, Math.Max(10, Width - width * 2 - 2), number.PreferredHeight);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = UiShape.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 4)) using (Pen pen = new Pen(theme.Line)) e.Graphics.DrawPath(pen, path);
        }
    }
}
