using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PotPlayerFrameClip
{
    internal static class JobText
    {
        internal static string Processing(JobSnapshot job, string language)
        {
            string backend = job.ProcessingBackend ?? String.Empty;
            if (backend == "copy") return UiText.Choose(language, "原码复制", "Stream copy");
            if (backend == "cpu") return "CPU";
            if (backend == "cuda" || backend == "d3d11va" || backend == "qsv" || backend == "dxva2")
                return "GPU · " + (backend == "d3d11va" ? "D3D11" : backend.ToUpperInvariant());
            if (backend.EndsWith("-pending", StringComparison.Ordinal)) return UiText.Choose(language, "尝试 GPU", "Trying GPU");
            return String.Empty;
        }
        internal static string State(JobSnapshot job, string language)
        {
            string zh, en;
            switch (job.State)
            {
                case JobState.Queued: zh = "等待中"; en = "Queued"; break;
                case JobState.Preparing: zh = "准备中"; en = "Preparing"; break;
                case JobState.Running: zh = job.IsImage ? "截图中" : "导出中"; en = job.IsImage ? "Capturing" : "Exporting"; break;
                case JobState.Finalizing: zh = "校验与保存"; en = "Finalizing"; break;
                case JobState.Canceling: zh = "正在取消"; en = "Canceling"; break;
                case JobState.Succeeded: zh = String.IsNullOrEmpty(job.Warning) ? "已完成" : "已完成，有提示"; en = String.IsNullOrEmpty(job.Warning) ? "Completed" : "Completed with warning"; break;
                case JobState.Failed: zh = "失败"; en = "Failed"; break;
                case JobState.Canceled: zh = "已取消"; en = "Canceled"; break;
                default: zh = "上次运行中断"; en = "Interrupted"; break;
            }
            return UiText.Choose(language, zh, en);
        }
        internal static string Time(long milliseconds) { return TimeSpan.FromMilliseconds(milliseconds).ToString(@"hh\:mm\:ss\.fff"); }
        internal static string Duration(double seconds)
        {
            TimeSpan span = TimeSpan.FromSeconds(Math.Max(0, Math.Min(seconds, 359999)));
            return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
        }
        internal static void Open(string path, bool select)
        {
            if (select && File.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
            else if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
        }
    }

    internal sealed class JobCenterForm : WorkspaceForm
    {
        private readonly AppConfig config;
        private readonly JobCoordinator jobs;
        private readonly UiTheme theme;
        private readonly ToolTip tips = new ToolTip();
        private readonly List<Font> ownedFonts = new List<Font>();
        private readonly NavigationStrip navigation;
        private readonly Panel tasksPage;
        private readonly JobListView list;
        private readonly Label heading, summary, resource;
        private readonly ToolButton filter;
        internal readonly SettingsView Settings;
        private int filterIndex;
        private ContextMenuStrip filterMenu;
        internal int View { get { return navigation.SelectedIndex; } set { navigation.SelectedIndex = value; } }
        internal int Filter { get { return filterIndex; } set { filterIndex = Math.Max(0, Math.Min(2, value)); UpdateJobs(); } }
        internal JobCenterForm(AppConfig config, JobCoordinator jobs, Action capture, EventHandler<SettingsSavedEventArgs> saved = null) : base(config)
        {
            SuspendLayout();
            this.config = config; this.jobs = jobs; theme = new UiTheme(config.Theme);
            Text = "FrameClip";
            Font = OwnFont(9.75f); ClientSize = new Size(720, 680); MinimumSize = new Size(600, 480); StartPosition = FormStartPosition.CenterScreen;
            navigation = new NavigationStrip(theme, false, T("任务", "Tasks"), T("设置", "Settings")) { Dock = DockStyle.Top, Font = OwnFont(10.5f) };
            Panel body = new Panel { Dock = DockStyle.Fill, BackColor = theme.Background };
            tasksPage = new Panel { Dock = DockStyle.Fill, BackColor = theme.Background };
            Settings = new SettingsView(config) { Visible = false };
            if (saved != null) Settings.Saved += saved;
            body.Controls.Add(Settings); body.Controls.Add(tasksPage);
            navigation.SelectedIndexChanged += delegate { Settings.Visible = View == 1; tasksPage.Visible = View == 0; if (View == 1) Settings.BringToFront(); else { tasksPage.BringToFront(); UpdateJobs(); } };

            Panel header = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = theme.Background };
            heading = new Label { Text = T("后台导出", "Background exports"), Font = OwnFont(14.25f, FontStyle.Bold), ForeColor = theme.Text, AutoEllipsis = true };
            summary = new Label { Font = OwnFont(9f), ForeColor = theme.Muted, AutoEllipsis = true };
            ToolButton screenshot = new ToolButton(theme, "camera", T("截图", "Capture")) { Text = T("截图", "Capture"), Bordered = true, Size = new Size(84, 32), Font = OwnFont(9f) };
            screenshot.Click += delegate { if (capture != null) capture(); }; tips.SetToolTip(screenshot, screenshot.AccessibleName);
            filter = new ToolButton(theme, "list-filter", T("筛选任务", "Filter tasks")) { Size = new Size(28, 28), LogicalHeight = 28, IconSize = 15 };
            filter.Click += delegate { ShowFilter(); }; tips.SetToolTip(filter, filter.AccessibleName);
            header.Controls.AddRange(new Control[] { heading, summary, screenshot, filter });
            header.Layout += delegate {
                float s = header.Height / 84f; int left = (int)(24 * s), reserved = (int)(150 * s);
                heading.Bounds = new Rectangle(left, (int)(18 * s), Math.Max(50, header.Width - left - reserved), (int)(29 * s));
                summary.Bounds = new Rectangle(left, (int)(49 * s), Math.Max(50, header.Width - left - reserved), (int)(20 * s));
                screenshot.Location = new Point(header.Width - (int)(24 * s) - screenshot.Width, (int)(25 * s));
                filter.Location = new Point(screenshot.Left - filter.Width - (int)(7 * s), (int)(27 * s));
            };
            list = new JobListView(config, theme, jobs, tips) { Dock = DockStyle.Fill };
            tasksPage.Controls.Add(list); tasksPage.Controls.Add(header);

            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 42, BackColor = theme.Surface, Padding = new Padding(24, 4, 16, 4) };
            ToolButton performance = new ToolButton(theme, "sliders-horizontal", T("性能设置", "Performance settings")) { Dock = DockStyle.Right, Size = new Size(28, 28), LogicalHeight = 34, IconSize = 14, BackColor = theme.Surface };
            performance.Click += delegate { ShowSettings(0); }; tips.SetToolTip(performance, performance.AccessibleName);
            resource = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = theme.Muted, Font = OwnFont(8.25f) };
            footer.Controls.Add(resource); footer.Controls.Add(performance);
            footer.Paint += delegate(object sender, PaintEventArgs e) { using (Pen pen = new Pen(theme.Line)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            Content.Controls.Add(body); Content.Controls.Add(footer); Content.Controls.Add(navigation); UpdateJobs();
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi; ResumeLayout(true);
        }
        private Font OwnFont(float size, FontStyle style = FontStyle.Regular) { Font font = UiTheme.Font(size, style); ownedFonts.Add(font); return font; }
        private string T(string zh, string en) { return UiText.Choose(config.Language, zh, en); }
        internal void ShowSettings(int section = 0) { View = 1; Settings.Section = section; }
        private void ShowFilter()
        {
            if (filterMenu == null)
            {
                filterMenu = new ContextMenuStrip { ShowImageMargin = false };
                string[] labels = { T("所有任务", "All tasks"), T("进行中", "Active"), T("最近记录", "Recent") };
                for (int i = 0; i < labels.Length; i++) { int index = i; ToolStripMenuItem item = new ToolStripMenuItem(labels[i]); item.Click += delegate { Filter = index; }; filterMenu.Items.Add(item); }
            }
            filterMenu.Font = Font;
            for (int i = 0; i < filterMenu.Items.Count; i++) ((ToolStripMenuItem)filterMenu.Items[i]).Checked = i == filterIndex;
            filterMenu.Show(filter, new Point(0, filter.Height));
        }
        internal void UpdateJobs(JobSnapshot[] snapshots = null)
        {
            snapshots = snapshots ?? jobs.Snapshot();
            string mode = config.ResourceMode == "quiet" ? T("播放优先", "Playback first") : config.ResourceMode == "balanced" ? T("均衡", "Balanced") : T("导出优先", "Export first");
            resource.Text = mode + "  ·  " + (config.ProcessingDevice == "gpu" ? T("GPU 优先", "GPU preferred") : T("仅 CPU", "CPU only")) + "  ·  " + config.MaxVideoJobs + T(" 路视频 / 1 路截图", " video / 1 image") +
                (config.ResourceMode == "fast" ? "" : jobs.Resources.Capped ? T("  ·  共享 CPU 预算", "  ·  Shared CPU budget") : T("  ·  仅线程限制", "  ·  Thread limits only"));
            if (!String.IsNullOrEmpty(jobs.PersistenceWarning)) resource.Text = jobs.PersistenceWarning;
            tips.SetToolTip(resource, resource.Text);
            if (View != 0) return;
            int active = snapshots.Count(s => !s.Terminal), videos = snapshots.Count(s => !s.Terminal && !s.IsImage && s.State != JobState.Queued), queued = snapshots.Count(s => s.State == JobState.Queued);
            int failed = snapshots.Count(s => s.State == JobState.Failed || s.State == JobState.Interrupted);
            heading.Text = filterIndex == 2 ? T("最近任务", "Recent tasks") : active > 0 ? T("后台导出", "Background exports") : T("任务", "Tasks");
            summary.Text = active > 0 ? (videos > 0 ? videos + T(" 个视频正在导出", " videos exporting") : T("正在处理截图", "Capturing images")) + (queued > 0 ? " · " + queued + T(" 个等待", " queued") : "") :
                failed > 0 ? failed + T(" 个任务需要处理", " jobs need attention") : snapshots.Length > 0 ? T("任务已结束", "Tasks finished") : "";
            JobSnapshot[] visible = filterIndex == 1 ? snapshots.Where(s => !s.Terminal).Reverse().ToArray() : filterIndex == 2 ? snapshots.Where(s => s.Terminal).ToArray() : snapshots.Where(s => !s.Terminal).Reverse().Concat(snapshots.Where(s => s.Terminal)).ToArray();
            list.SetJobs(visible);
        }
        protected override void OnShown(EventArgs e)
        {
            Rectangle work = Screen.FromControl(this).WorkingArea;
            Size limit = new Size(Math.Max(320, work.Width - 24), Math.Max(320, work.Height - 24));
            MinimumSize = new Size(Math.Min(MinimumSize.Width, limit.Width), Math.Min(MinimumSize.Height, limit.Height));
            if (Width > limit.Width || Height > limit.Height) Size = new Size(Math.Min(Width, limit.Width), Math.Min(Height, limit.Height));
            base.OnShown(e);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && Settings.HasChanges)
            {
                DialogResult answer = MessageBox.Show(this, T("是否保存设置更改？", "Save changes to settings?"), "FrameClip", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel || (answer == DialogResult.Yes && !Settings.Save())) e.Cancel = true;
            }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing) { if (disposing) { if (filterMenu != null) filterMenu.Dispose(); tips.Dispose(); foreach (Font font in ownedFonts) font.Dispose(); } base.Dispose(disposing); }
    }
}
