using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace PotPlayerFrameClip
{
    internal sealed class SettingsSavedEventArgs : EventArgs
    {
        internal bool LanguageChanged, MenuLanguageApplied, AppearanceChanged;
    }

    internal sealed class SettingsView : UserControl
    {
        private readonly AppConfig config;
        private readonly string language;
        private readonly UiTheme theme;
        private readonly NavigationStrip sections;
        private readonly FlowLayoutPanel[] pages;
        private readonly TextBox root, ffmpeg;
        private readonly ChoiceBox image, video, mode, device, themeChoice, notifications, languageChoice;
        private readonly NumericStepper concurrency;
        private readonly NotificationPositionView notificationPosition;
        private readonly ToggleSwitch rec709, reduced, priority;
        private readonly Label budget, note;
        private readonly ToolButton save;
        private readonly ToolTip tips = new ToolTip();
        private bool ready, saving;
        internal bool HasChanges { get; private set; }
        internal event EventHandler<SettingsSavedEventArgs> Saved;
        internal int Section { get { return sections.SelectedIndex; } set { sections.SelectedIndex = value; } }
        internal SettingsView(AppConfig config)
        {
            this.config = config; language = config.Language; theme = new UiTheme(config.Theme);
            Dock = DockStyle.Fill; BackColor = theme.Background;
            sections = new NavigationStrip(theme, true, T("性能", "Performance"), T("输出", "Output"), T("外观", "Appearance")) { Dock = DockStyle.Top };
            Panel body = new Panel { Dock = DockStyle.Fill };
            pages = new[] { Page(), Page(), Page() };
            foreach (Panel page in pages) body.Controls.Add(page);
            pages[0].BringToFront();
            sections.SelectedIndexChanged += delegate { foreach (Panel page in pages) page.Visible = false; pages[sections.SelectedIndex].Visible = true; pages[sections.SelectedIndex].BringToFront(); };
            pages[1].Visible = pages[2].Visible = false;

            device = Choice(new[] { new FormatChoice("gpu", T("GPU 优先", "GPU preferred")), new FormatChoice("cpu", T("仅 CPU", "CPU only")) }, config.ProcessingDevice, T("视频与截图处理", "Video and screenshots"));
            Field(pages[0], T("视频与截图处理", "Video and screenshots"), T("优先硬件解码，不支持时使用 CPU", "Prefer hardware decoding; fall back to CPU"), device);
            tips.SetToolTip(device, T("保持原有编码与色彩处理。原码复制无需解码；ProRes、DNxHR 和图片编码仍由 CPU 完成。", "Preserves codecs and color processing. Stream copy needs no decoding; ProRes, DNxHR and image encoding stay on CPU."));
            mode = Choice(new[] { new FormatChoice("quiet", T("播放优先", "Playback first")), new FormatChoice("balanced", T("均衡", "Balanced")), new FormatChoice("fast", T("导出优先", "Export first")) }, config.ResourceMode, T("资源使用", "Resource use"));
            Field(pages[0], T("资源使用", "Resource use"), T("共享后台资源预算", "Shared background resource budget"), mode);
            concurrency = new NumericStepper(theme, config.MaxVideoJobs, T("同时导出视频", "Parallel videos"));
            Field(pages[0], T("同时导出视频", "Parallel videos"), T("截图使用独立通道", "Independent screenshot lane"), concurrency);
            budget = new Label { AutoSize = false, Width = 100, Height = 30, TextAlign = ContentAlignment.MiddleRight };
            Field(pages[0], T("视频 CPU 总预算", "Total video CPU budget"), T("所有视频任务共享", "Shared by all video jobs"), budget);
            priority = new ToggleSwitch(theme, T("降低后台进程优先级", "Lower background priority")) { Checked = config.LowerBackgroundPriority };
            Field(pages[0], T("降低后台进程优先级", "Lower background priority"), null, priority);
            mode.ValueChanged += delegate { priority.Checked = mode.Value != "fast"; Budget(); Dirty(); };
            concurrency.ValueChanged += delegate { Dirty(); }; priority.CheckedChanged += delegate { Dirty(); }; Budget();

            root = new TextBox { Text = config.LibraryRootDirectory, BorderStyle = BorderStyle.None };
            pages[1].Controls.Add(PathField(T("素材库目录", "Library folder"), root, true));
            image = Choice(new[] { new FormatChoice("png16", "PNG · 16-bit"), new FormatChoice("tiff16", "TIFF · 16-bit") }, config.ImageFormat, T("截图格式", "Image format"));
            Field(pages[1], T("截图格式", "Image format"), null, image);
            video = Choice(UiText.VideoChoices(language), config.VideoPreset, T("精确片段编码", "Precise clip codec"));
            Field(pages[1], T("精确片段编码", "Precise clip codec"), null, video);
            rec709 = new ToggleSwitch(theme, T("同时生成 SDR 查看图", "Include SDR viewing image")) { Checked = config.ExportRec709ForHdr };
            Field(pages[1], T("同时生成 SDR 查看图", "Include SDR viewing image"), "Rec.709", rec709);
            ffmpeg = new TextBox { Text = config.FfmpegPath, BorderStyle = BorderStyle.None };
            pages[1].Controls.Add(PathField("FFmpeg", ffmpeg, false));

            themeChoice = Choice(new[] { new FormatChoice("system", T("跟随系统", "System")), new FormatChoice("light", T("浅色", "Light")), new FormatChoice("dark", T("深色", "Dark")) }, config.Theme, T("主题", "Theme"));
            Field(pages[2], T("主题", "Theme"), null, themeChoice);
            notifications = Choice(new[] { new FormatChoice("compact", T("小型状态条", "Compact strip")), new FormatChoice("tray", T("仅系统托盘", "Tray only")), new FormatChoice("pinned", T("固定显示状态条", "Pinned strip")) }, config.NotificationMode, T("导出进度", "Export progress"));
            Field(pages[2], T("导出进度", "Export progress"), null, notifications);
            notificationPosition = new NotificationPositionView(config, theme);
            themeChoice.ValueChanged += delegate { notificationPosition.PreviewTheme(themeChoice.Value); };
            notificationPosition.ValueChanged += delegate { Dirty(); };
            notificationPosition.PlacementConfirmed += delegate { Save(); };
            pages[2].Controls.Add(notificationPosition);
            reduced = new ToggleSwitch(theme, T("减少动态效果", "Reduce motion")) { Checked = config.ReducedMotion };
            Field(pages[2], T("减少动态效果", "Reduce motion"), null, reduced);
            languageChoice = Choice(UiText.LanguageChoices(), language, T("语言", "Language"));
            Field(pages[2], T("语言", "Language"), null, languageChoice);
            foreach (ToggleSwitch toggle in new[] { rec709, reduced }) toggle.CheckedChanged += delegate { Dirty(); };
            root.TextChanged += delegate { Dirty(); }; ffmpeg.TextChanged += delegate { Dirty(); };

            Panel savebar = new Panel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(24, 12, 24, 16) };
            save = new ToolButton(theme) { Text = T("保存", "Save"), Bordered = true, Enabled = false, Size = new Size(64, 28), LogicalHeight = 28, Font = UiTheme.Font(9f), AccessibleName = T("保存设置", "Save settings") };
            save.Click += delegate { Save(); };
            note = new Label { TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
            savebar.Controls.Add(note); savebar.Controls.Add(save);
            savebar.Layout += delegate {
                float scale = savebar.Height / 64f;
                save.Size = new Size((int)Math.Round(64 * scale), (int)Math.Round(28 * scale));
                save.Location = new Point(savebar.Width - savebar.Padding.Right - save.Width, (savebar.Height - save.Height) / 2);
                note.Bounds = new Rectangle(savebar.Padding.Left, save.Top, Math.Max(0, save.Left - savebar.Padding.Left - (int)(16 * scale)), save.Height);
            };
            Controls.Add(body); Controls.Add(savebar); Controls.Add(sections);
            theme.Apply(this); note.ForeColor = theme.AccentColor; ready = true;
        }
        private string T(string zh, string en) { return UiText.Choose(language, zh, en); }
        private void Dirty() { if (!ready || saving) return; HasChanges = true; save.Enabled = true; save.Primary = true; save.Text = T("保存", "Save"); note.ForeColor = theme.Muted; note.Text = T("有未保存的更改", "Unsaved changes"); tips.SetToolTip(note, null); save.Invalidate(); }
        internal void ShowSaved() { HasChanges = false; save.Enabled = false; save.Primary = false; save.Text = T("已保存", "Saved"); note.ForeColor = theme.AccentColor; note.Text = T("设置已保存", "Settings saved"); tips.SetToolTip(note, null); save.Invalidate(); }
        private void Budget() { budget.Text = mode.Value == "fast" ? T("不限", "No cap") : mode.Value == "balanced" ? "60%" : "35%"; }
        private ChoiceBox Choice(FormatChoice[] options, string value, string label)
        {
            ChoiceBox control = new ChoiceBox(theme, options, value, label); control.ValueChanged += delegate { Dirty(); }; return control;
        }
        private FlowLayoutPanel Page()
        {
            FlowLayoutPanel page = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false, FlowDirection = FlowDirection.TopDown, Padding = new Padding(24, 8, 24, 8), Margin = new Padding(0), BackColor = theme.Background };
            page.SizeChanged += delegate { foreach (Control row in page.Controls) row.Width = Math.Max(200, page.ClientSize.Width - page.Padding.Horizontal - (page.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0)); };
            page.ControlAdded += delegate(object sender, ControlEventArgs args) { args.Control.Width = Math.Max(200, page.ClientSize.Width - page.Padding.Horizontal); };
            return page;
        }
        private void Field(FlowLayoutPanel page, string label, string hint, Control input)
        {
            SettingRow row = new SettingRow(theme, label, hint, input); page.Controls.Add(row);
        }
        private Control PathField(string label, TextBox text, bool folder)
        {
            Panel row = new Panel { Height = 94, Margin = new Padding(0) };
            Label title = new Label { Text = label, Location = new Point(0, 10), AutoSize = true };
            Panel input = new Panel { Location = new Point(0, 37), Height = 36, BackColor = theme.Surface, Padding = new Padding(9, 8, 9, 6), Tag = "input" };
            text.Dock = DockStyle.Fill; text.BackColor = theme.Surface; input.Controls.Add(text); text.AccessibleName = label;
            ToolButton browse = new ToolButton(theme, "folder-open", T("浏览", "Browse")) { Bordered = true, Size = new Size(36, 36), LogicalHeight = 36 };
            tips.SetToolTip(browse, browse.AccessibleName);
            browse.Click += delegate {
                if (folder) using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                { if (Directory.Exists(text.Text)) dialog.SelectedPath = text.Text; if (dialog.ShowDialog(FindForm()) == DialogResult.OK) text.Text = dialog.SelectedPath; }
                else using (OpenFileDialog dialog = new OpenFileDialog { Filter = "FFmpeg|ffmpeg.exe" })
                    if (dialog.ShowDialog(FindForm()) == DialogResult.OK) text.Text = dialog.FileName;
            };
            row.Controls.AddRange(new Control[] { title, input, browse });
            row.Layout += delegate {
                float s = row.Height / 94f; input.Width = Math.Max(40, row.Width - (int)(44 * s));
                browse.Location = new Point(row.Width - browse.Width, input.Top);
            };
            row.Paint += delegate(object sender, PaintEventArgs args) { using (Pen pen = new Pen(theme.Line)) args.Graphics.DrawLine(pen, 0, row.Height - 1, row.Width, row.Height - 1); };
            return row;
        }
        internal bool Save()
        {
            if (saving) return false;
            saving = true; save.Enabled = false; save.Text = T("保存中", "Saving"); save.Refresh();
            try
            {
                string path = root.Text.Trim(); if (!Path.IsPathRooted(path)) throw new IOException(T("请选择绝对路径。", "Choose an absolute output path."));
                AppConfig next = config.Copy(); next.LibraryRootDirectory = Path.GetFullPath(path); Directory.CreateDirectory(next.LibraryRootDirectory);
                next.ImageFormat = image.Value; next.VideoPreset = video.Value; next.ExportRec709ForHdr = rec709.Checked;
                next.FfmpegPath = ffmpeg.Text.Trim(); next.FfprobePath = String.IsNullOrWhiteSpace(next.FfmpegPath) ? "" : Path.Combine(Path.GetDirectoryName(next.FfmpegPath), "ffprobe.exe");
                next.Language = languageChoice.Value; next.ResourceMode = mode.Value; next.MaxVideoJobs = concurrency.Value; next.LowerBackgroundPriority = priority.Checked; next.ProcessingDevice = device.Value;
                next.Theme = themeChoice.Value; next.NotificationMode = notifications.Value; next.ReducedMotion = reduced.Checked; next.NormalizeJobs(); next.LocateTools();
                notificationPosition.CopyTo(next); next.NormalizeJobs();
                bool appearanceChanged = next.Theme != config.Theme || next.Language != config.Language;
                next.Save();
                foreach (FieldInfo field in typeof(AppConfig).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)) field.SetValue(config, field.GetValue(next));
                SettingsSavedEventArgs result = new SettingsSavedEventArgs { LanguageChanged = next.Language != language, AppearanceChanged = appearanceChanged };
                result.MenuLanguageApplied = !result.LanguageChanged || MenuLocalization.Apply(config);
                ShowSaved();
                if (Saved != null) Saved(this, result); return true;
            }
            catch (Exception error) { HasChanges = true; save.Enabled = true; save.Primary = true; save.Text = T("重试", "Retry"); note.ForeColor = theme.ErrorColor; note.Text = error.Message; tips.SetToolTip(note, error.Message); return false; }
            finally { saving = false; save.Invalidate(); }
        }
        protected override void Dispose(bool disposing) { if (disposing) { tips.Dispose(); save.Font.Dispose(); } base.Dispose(disposing); }
    }

    internal sealed class SettingRow : Panel
    {
        private readonly Label label, hint;
        private readonly Control input;
        private readonly UiTheme theme;
        internal SettingRow(UiTheme theme, string text, string detail, Control input)
        {
            this.theme = theme; this.input = input; Height = 72; Margin = new Padding(0);
            label = new Label { Text = text, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            hint = new Label { Text = detail ?? "", ForeColor = theme.Muted, Tag = "muted", AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            Controls.AddRange(new Control[] { label, hint, input });
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (input == null || label == null || hint == null) return;
            float scale = Height / 72f;
            ChoiceBox choice = input as ChoiceBox;
            int width = choice != null ? Math.Min(Math.Max((int)(245 * scale), choice.PreferredWidth), (int)(Width * .58)) : input.Width;
            input.Bounds = new Rectangle(Width - width, (Height - input.Height) / 2, width, input.Height);
            int labelWidth = Math.Max(20, Width - width - (int)(20 * scale));
            label.Bounds = new Rectangle(0, (int)((hint.Text.Length > 0 ? 13 : 21) * scale), labelWidth, (int)(27 * scale));
            hint.Visible = hint.Text.Length > 0;
            hint.Bounds = new Rectangle(0, (int)(38 * scale), labelWidth, (int)(21 * scale));
        }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (Pen pen = new Pen(theme.Line)) e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1); }
    }

    internal sealed class SettingsForm : WorkspaceForm
    {
        private readonly SettingsView view;
        internal bool LanguageChanged { get; private set; }
        internal bool MenuLanguageApplied { get; private set; }
        internal SettingsForm(AppConfig config) : base(config)
        {
            SuspendLayout();
            Text = "FrameClip"; Font = UiTheme.Font();
            ClientSize = new Size(720, 660); MinimumSize = new Size(600, 520); StartPosition = FormStartPosition.CenterScreen;
            view = new SettingsView(config); Content.Controls.Add(view);
            view.Saved += delegate(object sender, SettingsSavedEventArgs args) { LanguageChanged = args.LanguageChanged; MenuLanguageApplied = args.MenuLanguageApplied; DialogResult = DialogResult.OK; Close(); };
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi; ResumeLayout(true);
        }
        protected override void Dispose(bool disposing) { if (disposing) Font.Dispose(); base.Dispose(disposing); }
    }
}
