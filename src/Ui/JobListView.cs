using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PotPlayerFrameClip
{
    internal sealed class JobListView : Panel
    {
        private readonly AppConfig config;
        private readonly UiTheme theme;
        private readonly JobCoordinator coordinator;
        private readonly ToolTip tips;
        private readonly Dictionary<int, JobRow> rows = new Dictionary<int, JobRow>();
        private JobSnapshot[] jobs = new JobSnapshot[0];
        private int[] tops = new int[0], heights = new int[0];
        internal int MaterializedRows { get { return rows.Count; } }
        internal JobListView(AppConfig config, UiTheme theme, JobCoordinator coordinator, ToolTip tips)
        {
            this.config = config; this.theme = theme; this.coordinator = coordinator; this.tips = tips;
            AutoScroll = true; BackColor = theme.Background; DoubleBuffered = true;
        }
        internal void SetJobs(JobSnapshot[] value) { jobs = value; ArrangeRows(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if (rows != null) ArrangeRows(); }
        protected override void OnScroll(ScrollEventArgs e) { base.OnScroll(e); ArrangeRows(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (rows != null) ArrangeRows(); }
        private void ArrangeRows()
        {
            if (IsDisposed) return;
            float scale = UiScale.For(this); int total = 0, scroll = -AutoScrollPosition.Y;
            tops = new int[jobs.Length]; heights = new int[jobs.Length];
            for (int i = 0; i < jobs.Length; i++)
            {
                tops[i] = total; heights[i] = (int)Math.Round(JobRow.LogicalHeight(jobs[i]) * scale); total += heights[i];
            }
            if (AutoScrollMinSize.Height != total) AutoScrollMinSize = new Size(0, total);
            int width = Math.Max(200, ClientSize.Width - (VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
            HashSet<int> visible = new HashSet<int>();
            for (int i = 0; i < jobs.Length; i++) if (tops[i] + heights[i] >= scroll && tops[i] <= scroll + ClientSize.Height) visible.Add(i);
            SuspendLayout();
            foreach (int index in rows.Keys.Where(i => !visible.Contains(i)).ToArray()) { rows[index].Dispose(); rows.Remove(index); }
            foreach (int index in visible)
            {
                JobRow row;
                if (!rows.TryGetValue(index, out row)) { row = new JobRow(config, theme, coordinator, tips); rows.Add(index, row); Controls.Add(row); }
                row.Bounds = new Rectangle(0, tops[index] + AutoScrollPosition.Y, width, heights[index]); row.UpdateJob(jobs[index]);
            }
            ResumeLayout(false); Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (jobs.Length > 0) return;
            Image icon = UiTheme.Icon("check-check", theme.Dark, 28);
            int y = Math.Max(28, ClientSize.Height / 2 - 28);
            if (icon != null) e.Graphics.DrawImage(icon, new Rectangle((ClientSize.Width - 28) / 2, y, 28, 28));
            TextRenderer.DrawText(e.Graphics, UiText.Choose(config.Language, "没有待处理任务", "No pending jobs"), Font, new Rectangle(20, y + 40, Math.Max(50, ClientSize.Width - 40), 24), theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class JobRow : Panel
    {
        private JobSnapshot job;
        private readonly AppConfig config;
        private readonly UiTheme theme;
        private readonly ToolTip tips;
        private readonly ToolButton cancel, retry, output, detail;
        private Font small, strong;
        internal static int LogicalHeight(JobSnapshot job) { return job.State == JobState.Running && job.Fraction.HasValue ? 124 : !String.IsNullOrEmpty(job.Error) || !String.IsNullOrEmpty(job.Warning) ? 110 : 88; }
        internal JobRow(AppConfig config, UiTheme theme, JobCoordinator jobs, ToolTip tips)
        {
            this.config = config; this.theme = theme; this.tips = tips;
            DoubleBuffered = true; BackColor = theme.Background; Margin = new Padding(0);
            cancel = Action("x", T("取消此任务", "Cancel job"), delegate { jobs.Cancel(job.Id); });
            retry = Action("rotate-ccw", T("重试此任务", "Retry job"), delegate { try { jobs.Retry(job.Id); } catch (Exception e) { MessageBox.Show(this, e.Message, "FrameClip"); } });
            output = Action("folder-open", T("打开输出文件夹", "Open output folder"), delegate { if (job.Outputs.Length > 0) JobText.Open(job.Outputs[0], true); });
            detail = Action("file-text", T("详情与日志", "Details and log"), ShowDetails);
            Controls.AddRange(new Control[] { cancel, retry, output, detail }); MakeFonts();
        }
        private ToolButton Action(string icon, string label, Action action)
        {
            ToolButton button = new ToolButton(theme, icon, label) { LogicalHeight = 28, IconSize = 15 }; button.Click += delegate { action(); }; tips.SetToolTip(button, label); return button;
        }
        private string T(string zh, string en) { return UiText.Choose(config.Language, zh, en); }
        private void MakeFonts()
        {
            if (small != null) small.Dispose(); if (strong != null) strong.Dispose();
            small = new Font(Font.FontFamily, Font.SizeInPoints * .846f); strong = new Font(Font.FontFamily, Font.SizeInPoints, FontStyle.Bold);
        }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); MakeFonts(); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (detail == null) return; float scale = UiScale.For(this);
            int size = (int)(28 * scale), right = Width - (int)(24 * scale), top = (int)(15 * scale);
            detail.Bounds = new Rectangle(right - size, top, size, size);
            output.Bounds = new Rectangle(right - size * 2 - (int)(2 * scale), top, size, size);
            cancel.Bounds = retry.Bounds = new Rectangle(right - size * 3 - (int)(4 * scale), top, size, size);
        }
        internal void UpdateJob(JobSnapshot value)
        {
            if (job != null && job.Id == value.Id && job.State == value.State && job.Fraction == value.Fraction && job.Detail == value.Detail && job.Warning == value.Warning && job.Error == value.Error && job.ProcessingBackend == value.ProcessingBackend && job.ProcessingNote == value.ProcessingNote &&
                (job.Terminal || (int)job.ElapsedSeconds == (int)value.ElapsedSeconds) && job.Outputs.Length == value.Outputs.Length) return;
            job = value; AccessibleName = job.Title + ", " + JobText.State(job, config.Language);
            cancel.Visible = !job.Terminal; cancel.Enabled = job.State != JobState.Canceling;
            retry.Visible = job.Terminal && job.State != JobState.Succeeded;
            output.Visible = job.Outputs.Length > 0; output.Enabled = job.Outputs.Length > 0;
            if (job.Terminal) { output.Location = cancel.Location; retry.Location = cancel.Location; }
            tips.SetToolTip(this, job.Source + Environment.NewLine + job.Detail + Environment.NewLine + job.ProcessingNote); Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (job == null) return;
            float s = UiScale.For(this); int left = (int)(24 * s), textLeft = (int)(62 * s), right = Width - (int)(24 * s);
            using (Pen pen = new Pen(theme.Line)) e.Graphics.DrawLine(pen, left, 0, right, 0);
            Image symbol = UiTheme.Icon(job.IsImage ? "image" : "film", theme.Dark, (int)(18 * s));
            if (symbol != null) e.Graphics.DrawImage(symbol, new Rectangle(left, (int)(19 * s), (int)(18 * s), (int)(18 * s)));
            string state = JobText.State(job, config.Language);
            int stateWidth = TextRenderer.MeasureText(state, small, Size.Empty, TextFormatFlags.NoPadding).Width;
            int actionsLeft = !job.Terminal ? cancel.Left : retry.Visible ? retry.Left : output.Visible ? output.Left : detail.Left;
            int available = Math.Max((int)(30 * s), actionsLeft - textLeft - stateWidth - (int)(18 * s));
            TextFormatFlags flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter;
            int titleWidth = Math.Min(available, TextRenderer.MeasureText(job.Title, strong, Size.Empty, TextFormatFlags.NoPadding).Width);
            TextRenderer.DrawText(e.Graphics, job.Title, strong, new Rectangle(textLeft, (int)(14 * s), titleWidth, (int)(25 * s)), theme.Text, flags);
            Color stateColor = job.State == JobState.Failed || job.State == JobState.Interrupted ? theme.ErrorColor : !String.IsNullOrEmpty(job.Warning) ? theme.WarningColor : job.State == JobState.Queued || job.State == JobState.Canceled ? theme.Muted : theme.AccentColor;
            TextRenderer.DrawText(e.Graphics, state, small, new Rectangle(textLeft + titleWidth + (int)(8 * s), (int)(16 * s), stateWidth + 2, (int)(21 * s)), stateColor, flags);
            string range = JobText.Time(job.Start) + (job.IsImage ? "" : "  -  " + JobText.Time(job.End));
            string processing = JobText.Processing(job, config.Language);
            TextRenderer.DrawText(e.Graphics, range + "    " + job.Format + (processing.Length == 0 ? "" : "    " + processing), small, new Rectangle(textLeft, (int)(42 * s), right - textLeft, (int)(20 * s)), theme.Muted, flags);
            if (job.State == JobState.Running && job.Fraction.HasValue)
            {
                int trackTop = (int)(77 * s), trackWidth = right - textLeft;
                using (Brush brush = new SolidBrush(theme.Track)) e.Graphics.FillRectangle(brush, textLeft, trackTop, trackWidth, Math.Max(2, 3 * s));
                using (Brush brush = new SolidBrush(theme.AccentColor)) e.Graphics.FillRectangle(brush, textLeft, trackTop, (int)(trackWidth * job.Fraction.Value), Math.Max(2, 3 * s));
                string percent = (job.Fraction.Value * 100).ToString("0") + "%";
                TextRenderer.DrawText(e.Graphics, percent, small, new Rectangle(textLeft, (int)(88 * s), trackWidth / 3, (int)(20 * s)), theme.Muted, flags);
                string timing = job.RemainingSeconds.HasValue ? T("剩余约 ", "About ") + JobText.Duration(job.RemainingSeconds.Value) : T("已用 ", "Elapsed ") + JobText.Duration(job.ElapsedSeconds);
                TextRenderer.DrawText(e.Graphics, timing, small, new Rectangle(textLeft + trackWidth / 3, (int)(88 * s), trackWidth * 2 / 3, (int)(20 * s)), theme.Muted, flags | TextFormatFlags.Right);
            }
            else if (!String.IsNullOrEmpty(job.Error) || !String.IsNullOrEmpty(job.Warning))
                TextRenderer.DrawText(e.Graphics, String.IsNullOrEmpty(job.Error) ? job.Warning : job.Error, small, new Rectangle(textLeft, (int)(72 * s), right - textLeft, (int)(24 * s)), stateColor, flags);
        }
        private void ShowDetails()
        {
            using (WorkspaceForm dialog = new WorkspaceForm(config) { Text = T("任务详情", "Job details"), ClientSize = new Size(660, 440), MinimumSize = new Size(480, 320), StartPosition = FormStartPosition.CenterParent, Font = UiTheme.Font() })
            {
                string log = Path.Combine(AppPaths.DataDirectory, "logs", "job-" + job.Id + ".log");
                TextBox text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Both, WordWrap = false,
                    Text = job.Source + Environment.NewLine + JobText.State(job, config.Language) + Environment.NewLine + job.Detail + Environment.NewLine +
                        JobText.Processing(job, config.Language) + (new[] { "cuda", "d3d11va", "qsv", "dxva2" }.Contains(job.ProcessingBackend) ? T("：GPU 解码；CPU 色彩处理和编码", ": GPU decoding; CPU color processing and encoding") : "") + Environment.NewLine +
                        job.ProcessingNote + Environment.NewLine + job.Error + Environment.NewLine + job.Warning + Environment.NewLine + String.Join(Environment.NewLine, job.Outputs) + Environment.NewLine + log };
                FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
                ToolButton copy = new ToolButton(theme, "copy", T("复制详情", "Copy details")); copy.Click += delegate { try { Clipboard.SetText(text.Text); } catch { } }; tips.SetToolTip(copy, copy.AccessibleName);
                ToolButton open = new ToolButton(theme, "file-text", T("打开日志", "Open log")) { Enabled = File.Exists(log) }; open.Click += delegate { Process.Start("notepad.exe", "\"" + log + "\""); }; tips.SetToolTip(open, open.AccessibleName);
                actions.Controls.Add(copy); actions.Controls.Add(open); dialog.Content.Padding = new Padding(20); dialog.Content.Controls.Add(text); dialog.Content.Controls.Add(actions); theme.Apply(dialog.Content); dialog.ShowDialog(FindForm()); dialog.Font.Dispose();
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) { if (small != null) small.Dispose(); if (strong != null) strong.Dispose(); } base.Dispose(disposing); }
    }
}
