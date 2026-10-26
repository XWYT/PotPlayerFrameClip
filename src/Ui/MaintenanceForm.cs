using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PotPlayerFrameClip
{
    internal sealed class MaintenanceForm : WorkspaceForm
    {
        private readonly UiTheme theme;
        private readonly bool uninstall;
        private readonly Func<string, string, string, bool, Task<int>> run;
        private readonly Label heading, description, status;
        private readonly TextBox player, ffmpeg;
        private readonly ChoiceBox ffmpegMode;
        private readonly Control ffmpegRow;
        private readonly ToggleSwitch option;
        private readonly ToolButton action, cancel;
        private readonly ToolButton logs;
        private readonly Panel progress;
        private readonly ToolTip tips = new ToolTip();
        private readonly Panel body, footer;
        private bool busy, completed;
        internal int ResultCode = 1;
        internal MaintenanceForm(AppConfig config, bool uninstall, Func<string, string, string, bool, Task<int>> run, string logDirectory = null, bool bundledFFmpeg = false) : base(config)
        {
            SuspendLayout(); this.uninstall = uninstall; this.run = run; theme = new UiTheme(config.Theme);
            Text = "FrameClip"; Font = UiTheme.Font(); ClientSize = new Size(620, uninstall ? 388 : 610); MinimumSize = new Size(580, uninstall ? 388 : 610);
            StartPosition = FormStartPosition.CenterScreen; MaximizeBox = false;
            bool upgrade = File.Exists(Path.Combine(AppPaths.DataDirectory, "install-state.json"));
            body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 16, 28, 20) };
            heading = new Label { Text = T(config, uninstall ? "卸载 FrameClip" : upgrade ? "更新 FrameClip" : "安装 FrameClip", uninstall ? "Uninstall FrameClip" : upgrade ? "Update FrameClip" : "Install FrameClip"), Font = UiTheme.Font(16f, FontStyle.Bold), AutoEllipsis = true };
            description = new Label { Text = T(config, uninstall ? "移除程序与菜单集成。导出的素材始终保留。" : upgrade ? "更新程序，保留设置、提醒位置与任务记录。" : "连接 PotPlayer 与 FFmpeg，保留已有设置。", uninstall ? "Remove the app and menu integration. Exported media is always kept." : upgrade ? "Update while keeping settings, notification position and history." : "Connect PotPlayer and FFmpeg. Existing settings are kept."), Tag = "muted" };
            player = new TextBox { Text = DetectPlayer(config), BorderStyle = BorderStyle.None };
            ffmpeg = new TextBox { Text = config.FfmpegPath, BorderStyle = BorderStyle.None };
            Panel fields = new Panel();
            string initialMode = File.Exists(config.FfmpegPath) && File.Exists(config.FfprobePath) ? "existing" : bundledFFmpeg ? "bundled" : "later";
            FormatChoice[] modes = new[] {
                new FormatChoice("existing", T(config, "使用已有 FFmpeg", "Use existing FFmpeg")),
                new FormatChoice("bundled", T(config, "未安装，配置内置 FFmpeg", "Not installed: use bundled FFmpeg")),
                new FormatChoice("later", T(config, "稍后配置", "Configure later")) };
            ffmpegMode = new ChoiceBox(theme, modes.Where(c => bundledFFmpeg || c.Value != "bundled").ToArray(), initialMode, T(config, "FFmpeg 安装方式", "FFmpeg setup"));
            ffmpegRow = PathInput(config, "FFmpeg", ffmpeg, false, 164);
            Label dependencyLabel = new Label { Text = T(config, "FFmpeg 安装方式", "FFmpeg setup"), AutoSize = true, Top = 82 };
            Label dependencyHint = new Label { Text = bundledFFmpeg ? T(config, "内置版本仅供 FrameClip 使用，不修改系统环境。", "The bundled copy is private to FrameClip. No system changes.") : T(config, "没有 FFmpeg？请选择离线完整版，或安装后在设置中配置。", "No FFmpeg? Use the full offline installer, or configure it later."), Tag = "muted", AutoEllipsis = true };
            ffmpegMode.ValueChanged += delegate { ffmpegRow.Enabled = ffmpegMode.Value == "existing"; };
            ffmpegRow.Enabled = initialMode == "existing";
            fields.Layout += delegate { float s = UiScale; ffmpegMode.Bounds = new Rectangle(0, (int)(106*s), fields.Width, (int)(34*s)); dependencyHint.Bounds = new Rectangle(0,(int)(144*s),fields.Width,(int)(20*s)); };
            if (!uninstall)
            {
                fields.Controls.Add(PathInput(config, T(config, "PotPlayer 目录", "PotPlayer folder"), player, true, 0));
                fields.Controls.AddRange(new Control[] { dependencyLabel, ffmpegMode, dependencyHint, ffmpegRow });
            }
            option = new ToggleSwitch(theme, T(config, uninstall ? "同时删除设置与任务记录" : "开机自启动", uninstall ? "Also remove settings and task history" : "Start with Windows")) { Checked = !uninstall && (!upgrade || MaintenanceProgram.StartupEnabled()) };
            Label optionLabel = new Label { Text = option.AccessibleName, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            Panel optionRow = new Panel(); optionRow.Controls.AddRange(new Control[] { optionLabel, option });
            optionRow.Layout += delegate { option.Bounds = new Rectangle(optionRow.Width - option.Width, (optionRow.Height - option.Height) / 2, option.Width, option.Height); optionLabel.Bounds = new Rectangle(0, 0, Math.Max(20, option.Left - 16), optionRow.Height); };
            status = new Label { Text = T(config, "请先结束导出并关闭 PotPlayer。", "Finish exports and close PotPlayer first."), Tag = "muted", AutoEllipsis = true };
            progress = new Panel { Visible = false, BackColor = theme.Track };
            progress.Paint += delegate(object sender, PaintEventArgs e) { using (Brush brush = new SolidBrush(theme.AccentColor)) e.Graphics.FillRectangle(brush, progress.ClientRectangle); };
            body.Controls.AddRange(new Control[] { heading, description, fields, optionRow, status, progress });
            footer = new Panel { Dock = DockStyle.Bottom, Height = 64 };
            action = new ToolButton(theme) { Text = T(config, uninstall ? "卸载" : upgrade ? "更新" : "安装", uninstall ? "Uninstall" : upgrade ? "Update" : "Install"), Primary = true, Bordered = true, LogicalHeight = 32 };
            cancel = new ToolButton(theme) { Text = T(config, "取消", "Cancel"), Bordered = true, LogicalHeight = 32 };
            logs = new ToolButton(theme, "folder-open", T(config, "打开维护日志", "Open maintenance logs")) { Visible = false, Bordered = true, LogicalHeight = 32 };
            tips.SetToolTip(logs, logs.AccessibleName);
            logs.Click += delegate { if (!String.IsNullOrEmpty(logDirectory) && Directory.Exists(logDirectory)) Process.Start(new ProcessStartInfo(logDirectory) { UseShellExecute = true }); };
            action.Click += async delegate { await Execute(config); }; cancel.Click += delegate { Close(); };
            footer.Controls.AddRange(new Control[] { action, cancel, logs });
            footer.Paint += delegate(object sender, PaintEventArgs e) { using (Pen pen = new Pen(theme.Line)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            body.Layout += delegate {
                float s = UiScale; int width = Math.Max(1, body.Width - (int)(56 * s)); int left = (int)(28 * s);
                heading.Bounds = new Rectangle(left, (int)(18 * s), width, (int)(36 * s));
                description.Bounds = new Rectangle(left, (int)(62 * s), width, (int)(45 * s));
                fields.Bounds = new Rectangle(left, (int)(121 * s), width, (int)(246 * s));
                optionRow.Bounds = new Rectangle(left, (int)((uninstall ? 135 : 378) * s), width, (int)(32 * s));
                status.Bounds = new Rectangle(left, (int)((uninstall ? 194 : 426) * s), width, (int)(42 * s));
                progress.Bounds = new Rectangle(left, body.Height - (int)(12 * s), width, (int)(3 * s));
            };
            footer.Layout += delegate { float s = UiScale; action.Bounds = new Rectangle(footer.Width - (int)(116 * s), (int)(16 * s), (int)(88 * s), (int)(32 * s)); cancel.Bounds = new Rectangle(action.Left - (int)(100 * s), action.Top, (int)(88 * s), action.Height); logs.Bounds = new Rectangle((int)(28 * s), action.Top, action.Height, action.Height); };
            Content.Controls.Add(body); Content.Controls.Add(footer); theme.Apply(Content); progress.BackColor = theme.Track;
            AcceptButton = action; CancelButton = cancel;
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi; ResumeLayout(true);
        }
        private static string T(AppConfig config, string zh, string en) { return UiText.Choose(config.Language, zh, en); }
        private static string DetectPlayer(AppConfig config)
        {
            try { if (!String.IsNullOrEmpty(config.PotPlayerMenuPath)) return Path.GetDirectoryName(Path.GetDirectoryName(config.PotPlayerMenuPath)); } catch (ArgumentException) { }
            foreach (string path in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\PotPlayer"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"DAUM\PotPlayer"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"DAUM\PotPlayer") }) if (Directory.Exists(path)) return path;
            return String.Empty;
        }
        private Control PathInput(AppConfig config, string text, TextBox box, bool directory, int top)
        {
            Panel row = new Panel { Top = top, Height = 76, Dock = DockStyle.None };
            Label label = new Label { Text = text, AutoSize = true, Top = 0 };
            Panel field = new Panel { Top = 25, Height = 34, Padding = new Padding(10, 8, 10, 6), Tag = "input" };
            box.Dock = DockStyle.Fill; field.Controls.Add(box);
            ToolButton browse = new ToolButton(theme, "folder-open", T(config, "浏览", "Browse")) { Bordered = true, LogicalHeight = 34 };
            tips.SetToolTip(browse, browse.AccessibleName);
            browse.Click += delegate { if (directory) using (FolderBrowserDialog dialog = new FolderBrowserDialog()) { if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath; } else using (OpenFileDialog dialog = new OpenFileDialog { Filter = "FFmpeg|ffmpeg.exe" }) { if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName; } };
            row.Controls.AddRange(new Control[] { label, field, browse });
            row.ParentChanged += delegate { if (row.Parent != null) row.Parent.SizeChanged += delegate { row.Width = row.Parent.ClientSize.Width; }; };
            row.Layout += delegate { float s = UiScale; row.Width = row.Parent == null ? row.Width : row.Parent.ClientSize.Width; field.Bounds = new Rectangle(0, (int)(25 * s), Math.Max(20, row.Width - (int)(44 * s)), (int)(34 * s)); browse.Bounds = new Rectangle(row.Width - (int)(34 * s), field.Top, (int)(34 * s), field.Height); };
            return row;
        }
        private async Task Execute(AppConfig config)
        {
            if (completed) { Close(); return; } if (busy) return;
            busy = true; action.Enabled = cancel.Enabled = false; player.Enabled = ffmpeg.Enabled = ffmpegMode.Enabled = option.Enabled = false;
            status.ForeColor = theme.Muted;
            progress.Visible = true;
            status.Text = T(config, uninstall ? "正在安全卸载…" : "正在备份并安装…", uninstall ? "Uninstalling safely…" : "Backing up and installing…");
            try
            {
                if (!uninstall && ffmpegMode.Value == "existing" && (!File.Exists(ffmpeg.Text.Trim()) || !File.Exists(Path.Combine(Path.GetDirectoryName(ffmpeg.Text.Trim()) ?? String.Empty, "ffprobe.exe"))))
                    throw new IOException(T(config, "请选择同一目录内的 ffmpeg.exe 和 ffprobe.exe。", "Select ffmpeg.exe with ffprobe.exe in the same folder."));
                ResultCode = await run(player.Text.Trim(), ffmpeg.Text.Trim(), ffmpegMode.Value, option.Checked);
                if (ResultCode != 0) throw new IOException(T(config, "操作未完成，请检查维护日志后重试。", "Operation did not complete. Check the maintenance log and retry."));
                completed = true; status.ForeColor = theme.AccentColor;
                status.Text = T(config, uninstall ? "卸载完成。导出的素材未删除。" : ffmpegMode.Value == "later" ? "安装完成。截图与导出前，请先在设置中配置 FFmpeg。" : "安装完成。设置与历史记录已保留。", uninstall ? "Uninstalled. Exported media was not deleted." : ffmpegMode.Value == "later" ? "Installed. Configure FFmpeg in Settings before capturing or exporting." : "Installed. Settings and history were preserved.");
                action.Text = T(config, "完成", "Done"); cancel.Visible = false;
            }
            catch (Exception error) { ResultCode = 1; status.ForeColor = theme.ErrorColor; status.Text = error.Message; action.Text = T(config, "重试", "Retry"); logs.Visible = true; }
            finally { busy = false; player.Enabled = ffmpeg.Enabled = ffmpegMode.Enabled = option.Enabled = !completed; ffmpegRow.Enabled = !completed && ffmpegMode.Value == "existing"; action.Enabled = cancel.Enabled = true; progress.Visible = false; }
        }
        protected override void Dispose(bool disposing) { if (disposing) { tips.Dispose(); heading.Font.Dispose(); Font.Dispose(); } base.Dispose(disposing); }
    }

    internal static class MaintenanceProgram
    {
        internal static bool HasBundledFFmpeg
        {
            get {
#if FRAMECLIP_BUNDLED_FFMPEG
                return true;
#else
                return false;
#endif
            }
        }
        [STAThread] public static int Main(string[] args)
        {
            try
            {
                using (Mutex mutex = new Mutex(false, @"Local\FrameClipMaintenance-7C39C686"))
                {
                    bool owned;
                    try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
                    if (!owned) throw new IOException("Another FrameClip installation or uninstall is running.");
                    try {
                        // The staged child must acquire ownership, not create a new named object.
                        if (args.Contains("--uninstall") && !args.Contains("--staged")) { mutex.ReleaseMutex(); owned = false; }
                        return Run(args);
                    } finally { if (owned) mutex.ReleaseMutex(); }
                }
            }
            catch (Exception error)
            {
                string directory = LogDirectory();
                string path = Path.Combine(directory, "failure-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".log");
                File.WriteAllText(path, error.ToString());
                if (!IsSilent(args)) MessageBox.Show(error.Message + Environment.NewLine + path, "FrameClip", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
        private static string LogDirectory()
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FrameClipMaintenance", "logs");
            Directory.CreateDirectory(directory); return directory;
        }
        internal static bool IsSilent(string[] args) { return args.Any(a => a.Equals("/SILENT", StringComparison.OrdinalIgnoreCase) || a.Equals("/VERYSILENT", StringComparison.OrdinalIgnoreCase)); }
        internal static bool StartupEnabled()
        {
            using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                return key != null && key.GetValue("PotPlayerFrameClip") != null;
        }
        private static int Run(string[] args)
        {
            NativeMethods.SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            bool uninstall = args.Contains("--uninstall");
            string root = Path.GetDirectoryName(Application.ExecutablePath);
            if (uninstall && !args.Contains("--staged"))
            {
                string temporary = Path.Combine(Path.GetTempPath(), "FrameClipMaintenance-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
                string copy = Path.Combine(temporary, "FrameClipMaintenance.exe"); File.Copy(Application.ExecutablePath, copy);
                string forwarded = String.Join(" ", args.Where(a => a != "--uninstall").Select(Quote));
                using (Process child = Process.Start(new ProcessStartInfo(copy, "--uninstall --staged --root " + Quote(root) + " " + forwarded) { UseShellExecute = false }))
                { if (IsSilent(args)) { child.WaitForExit(); if (child.ExitCode == 0) QueueSelfCleanup(Application.ExecutablePath); return child.ExitCode; } }
                return 0;
            }
            int index = Array.IndexOf(args, "--root"); if (index >= 0 && index + 1 < args.Length) root = Path.GetFullPath(args[index + 1]);
            string logDirectory = LogDirectory();
            string log = Path.Combine(logDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            string installRoot = root;
            Func<string, string, string, bool, Task<int>> operation = (player, ffmpeg, mode, option) => Task.Run(() => {
                string engine, temporary = null;
                if (uninstall) engine = Path.Combine(installRoot, "unins000.exe");
                else
                {
                    temporary = Path.Combine(Path.GetTempPath(), "FrameClipSetup-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary); engine = Path.Combine(temporary, "FrameClipSetupEngine.exe");
                    using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("FrameClip.SetupEngine"))
                    { if (resource == null) throw new IOException("Setup payload missing."); using (FileStream output = File.Create(engine)) resource.CopyTo(output); }
                }
                try
                {
                    string command = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=" + Quote(log);
                    if (IsSilent(args)) command += " " + String.Join(" ", args.Where(a => a.StartsWith("/", StringComparison.Ordinal)).Select(Quote));
                    else if (uninstall) { if (option) command += " /REMOVEUSERDATA=1"; }
                    else command += " /POTPLAYERDIR=" + Quote(player) + " /FFMPEGMODE=" + mode + " /FFMPEGPATH=" + Quote(mode == "existing" ? ffmpeg : String.Empty) + (option ? "" : " /NOSTARTUP=1");
                    ProcessStartInfo start = new ProcessStartInfo(engine, command) { UseShellExecute = false, CreateNoWindow = true };
                    start.EnvironmentVariables["FRAMECLIP_MAINTENANCE_LOG"] = log + ".scripts.log";
                    using (Process child = Process.Start(start)) { child.WaitForExit(); return child.ExitCode; }
                }
                finally { if (temporary != null) try { Directory.Delete(temporary, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            });
            if (IsSilent(args)) return operation(String.Empty, String.Empty, "existing", false).GetAwaiter().GetResult();
            using (MaintenanceForm form = new MaintenanceForm(AppConfig.Load(false), uninstall, operation, logDirectory, HasBundledFFmpeg)) { Application.Run(form); return form.ResultCode; }
        }
        private static void QueueSelfCleanup(string executable)
        {
            // A silent caller must wait for the real uninstall result, keeping its image
            // mapped. Remove only that unchanged image after this parent has exited.
            string hash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(executable)) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            string literal = "'" + executable.Replace("'", "''") + "'";
            string script = "Wait-Process -Id " + Process.GetCurrentProcess().Id + " -ErrorAction SilentlyContinue; $p=" + literal +
                "; for($i=0;$i -lt 30;$i++){if(-not (Test-Path -LiteralPath $p)){break}; if((Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash -ne '" + hash +
                "'){break}; try{Remove-Item -LiteralPath $p -Force -ErrorAction Stop;break}catch{Start-Sleep -Milliseconds 100}}";
            string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            using (Process cleanup = Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"), "-NoProfile -WindowStyle Hidden -EncodedCommand " + encoded) { UseShellExecute = false, CreateNoWindow = true })) { }
        }
        internal static string Quote(string value)
        {
            if (value == null || value.IndexOf('"') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0) throw new ArgumentException("Invalid path.");
            int trailing = value.Length - value.TrimEnd('\\').Length;
            return "\"" + value + new string('\\', trailing) + "\"";
        }
    }
}
