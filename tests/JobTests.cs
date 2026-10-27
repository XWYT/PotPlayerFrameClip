using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace PotPlayerFrameClip
{
    internal static class JobTestProgram
    {
        [STAThread] public static int Main(string[] args)
        {
            if (args.Contains("-hwaccels") || args.Contains("-decoders")) return GpuTests.CapabilityFixture(args);
            if (Array.IndexOf(args, "--frameclip-gpu-fixture") >= 0) return GpuTests.ProcessFixture(args);
            TestDesktop.Initialize();
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); return JobTests.Run() == 0 ? 0 : 1;
        }
    }
    internal static class JobTests
    {
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
        [StructLayout(LayoutKind.Sequential)] private struct TrackBounds
        { internal Point Reserved; internal Size MaxSize; internal Point MaxPosition; internal Size MinTrack, MaxTrack; }
        private static int failures, checks;
        private static void Check(bool value, string message)
        {
            checks++; if (value) return; failures++; Console.Error.WriteLine("Jobs: " + message);
        }
        private static void MaintenanceUi(string root)
        {
            Check(MaintenanceProgram.IsSilent(new[] { "/verysilent" }), "maintenance silent switch lost");
            Check(!MaintenanceProgram.IsSilent(new[] { "/POTPLAYERDIR=C:\\Apps" }), "maintenance unexpectedly silent");
            Check(MaintenanceProgram.Quote(@"C:\").Length == 6, "maintenance trailing-slash quoting corrupts root");
            bool invalid = false; try { MaintenanceProgram.Quote("bad\"path"); } catch (ArgumentException) { invalid = true; }
            Check(invalid, "maintenance allowed embedded command quote");
            string renders = Environment.GetEnvironmentVariable("FRAMECLIP_UI_PREVIEW");
            foreach (string language in new[] { UiText.Chinese, UiText.English })
            foreach (string mode in new[] { "light", "dark" })
            foreach (bool uninstall in new[] { false, true })
            foreach (bool bundle in new[] { false, true })
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                AppConfig config = new AppConfig { Theme = mode, Language = language };
                TaskCompletionSource<int> pending = new TaskCompletionSource<int>();
                int attempts = 0;
                using (MaintenanceForm form = new MaintenanceForm(config, uninstall, (p,f,m,o) => ++attempts == 1 ? pending.Task : Task.FromResult(0), root, bundle))
                {
                    form.AutoScaleMode = AutoScaleMode.None; form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-24000, -24000); form.ShowInTaskbar = false;
                    Realize(form);
                    Control[] controls = new[] { (Control)form }.Concat(Children(form)).ToArray();
                    Font[] fonts = controls.Select(c => UiTheme.Font(c.Font.SizeInPoints * scale, c.Font.Style)).ToArray();
                    form.Scale(new SizeF(scale,scale));
                    for (int i=0; i<controls.Length; i++) controls[i].Font = fonts[i];
                    form.PerformLayout(); foreach (Control c in Children(form).ToArray()) c.PerformLayout();
                    ToolButton action = (ToolButton)typeof(MaintenanceForm).GetField("action",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
                    Label status = (Label)typeof(MaintenanceForm).GetField("status",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
                    ToggleSwitch option = Children(form).OfType<ToggleSwitch>().Single();
                    Check(!uninstall || !option.Checked, "uninstall removes settings by default");
                    if (!uninstall) Check(option.AccessibleName == UiText.Choose(language, "开机自启动", "Start with Windows"), "installer startup label is ambiguous");
                    Check(action.Height <= 34 * form.UiScale && action.Width >= 86 * form.UiScale, "maintenance primary button is stretched");
                    Check(status.Bottom < status.Parent.ClientSize.Height - 10 * form.UiScale, "maintenance status is clipped by footer");
                    foreach (TextBox box in Children(form).OfType<TextBox>()) Check(box.Parent.Height >= box.Height, "maintenance input clips text");
                    if (!uninstall)
                    {
                        ChoiceBox dependency = Children(form).OfType<ChoiceBox>().Single();
                        Check(dependency.Value == (bundle ? "bundled" : "later"), "missing FFmpeg has wrong installer default");
                        dependency.Value = "bundled";
                        Check(bundle || dependency.Value == "later", "lightweight installer offers absent bundle");
                    }
                    string name = "Maintenance-" + language + "-" + mode + "-" + uninstall + "-" + bundle + "-" + scale;
                    if (!String.IsNullOrEmpty(renders)) using (Bitmap b = new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(b,form.ClientRectangle); b.Save(Path.Combine(renders,name+".png")); }
                    MethodInfo execute = typeof(MaintenanceForm).GetMethod("Execute",BindingFlags.NonPublic|BindingFlags.Instance);
                    Task task = (Task)execute.Invoke(form,new object[] {config});
                    Check(!action.Enabled && !option.Enabled, "maintenance allows double-submit while busy");
                    pending.SetResult(1);
                    Stopwatch wait = Stopwatch.StartNew(); while (!task.IsCompleted && wait.ElapsedMilliseconds < 3000) { Application.DoEvents(); Thread.Sleep(5); }
                    Check(task.IsCompleted && action.Enabled && form.ResultCode != 0 && status.ForeColor == new UiTheme(mode).ErrorColor, "maintenance failure loses retry/error state");
                    task = (Task)execute.Invoke(form,new object[] {config});
                    while (!task.IsCompleted && wait.ElapsedMilliseconds < 3000) { Application.DoEvents(); Thread.Sleep(5); }
                    Check(task.IsCompleted && form.ResultCode == 0 && !option.Enabled, "maintenance success not retained");
                    foreach (Font font in fonts) font.Dispose();
                }
            }
            using (MaintenanceForm compact = new MaintenanceForm(new AppConfig { ReducedMotion = true }, false, (p,f,m,o) => Task.FromResult(0), root))
            {
                compact.AutoScaleMode = AutoScaleMode.None; compact.StartPosition = FormStartPosition.Manual;
                compact.Location = new Point(-20000, -20000); compact.ShowInTaskbar = false;
                compact.MinimumSize = new Size(580, 360); compact.ClientSize = new Size(620, 400); Realize(compact);
                Label status = (Label)typeof(MaintenanceForm).GetField("status", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(compact);
                Panel viewport = (Panel)status.Parent.Parent;
                Check(viewport.AutoScroll && viewport.DisplayRectangle.Height > viewport.ClientSize.Height, "compact maintenance content is not scrollable");
                Check(status.Bottom < status.Parent.Height, "compact maintenance status escapes scroll content");
                viewport.ScrollControlIntoView(status);
                Check(status.Parent.Top + status.Bottom <= viewport.ClientSize.Height, "compact maintenance status cannot scroll into view");
            }
            using (JobCoordinator jobs = new JobCoordinator(new AppConfig(), delegate { }, Path.Combine(root,"maintenance-jobs")))
            {
                Check(jobs.TryBeginMaintenance(), "idle maintenance rejected");
                bool rejected = false; try { jobs.Submit(new JobRequest(CaptureAction.ExportPrecise, Path.Combine(root,"FrameClip Fixture 2026.mkv"),0,1000,new AppConfig())); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected,"maintenance still accepts new work");
            }
        }
        private static bool Until(Func<bool> condition, int timeout = 6000)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (!condition()) { if (watch.ElapsedMilliseconds >= timeout) return false; Thread.Sleep(20); }
            return true;
        }
        internal static int Run()
        {
            TestDesktop.AssertIsolated();
            string root = Path.Combine(Path.GetTempPath(), "FrameClipJobs-" + Guid.NewGuid().ToString("N"));
            string previous = Environment.GetEnvironmentVariable("FRAMECLIP_DATA_DIRECTORY");
            Directory.CreateDirectory(root); Environment.SetEnvironmentVariable("FRAMECLIP_DATA_DIRECTORY", root);
            try
            {
                string source = Path.Combine(root, "FrameClip Fixture 2026.mkv"); File.WriteAllBytes(source, new byte[] { 1 });
                AppConfig config = new AppConfig { LibraryRootDirectory = Path.Combine(root, "outputs") };
                SnapshotAndScheduling(root, source, config);
                Reservations(root);
                Progress();
                GpuTests.Contracts(root, source, Check);
                CancellationAndRecovery(root, source, config);
                Ipc();
                PartialOutputActions(root, source, config);
                Ui(root, source, config);
                CaptionUi();
                SaveUi(root);
                NotificationUi(root);
                MaintenanceUi(root);
                TestDesktop.AssertIsolated();
                string diagnostic = RuntimeDiagnostics.Write(new InvalidOperationException("diagnostic-fixture"), "test");
                Check(diagnostic != null && diagnostic.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "runtime diagnostics escaped isolated data");
                Check(diagnostic != null && File.ReadAllText(diagnostic).Contains("System.InvalidOperationException: diagnostic-fixture"), "runtime diagnostic lost exception type/message");
                string ffmpeg = Environment.GetEnvironmentVariable("FRAMECLIP_TEST_FFMPEG");
                if (!String.IsNullOrEmpty(ffmpeg)) { Integration(root, config, ffmpeg); GpuTests.Integration(root, ffmpeg, Check); }
            }
            catch (Exception error) { failures++; Console.Error.WriteLine(error); }
            finally
            {
                Environment.SetEnvironmentVariable("FRAMECLIP_DATA_DIRECTORY", previous);
                try { Directory.Delete(root, true); } catch (Exception error) { Console.Error.WriteLine("Test cleanup: " + error.Message); }
            }
            Console.WriteLine("Job checks: " + checks + ", failures: " + failures);
            return failures;
        }
        private static void SnapshotAndScheduling(string root, string source, AppConfig config)
        {
            JobRequest a = new JobRequest(CaptureAction.ExportPrecise, source, 1000, 5000, config);
            JobRequest b = new JobRequest(CaptureAction.ExportPrecise, source, 6000, 9000, config);
            JobRequest c = new JobRequest(CaptureAction.ExportPrecise, source, 10000, 11000, config);
            AppConfig copy = a.Settings; copy.VideoPreset = "dnxhr444";
            Check(a.Settings.VideoPreset == "prores422hq" && a.Start == 1000 && a.End == 5000, "request settings are not immutable");
            using (ManualResetEventSlim releaseA = new ManualResetEventSlim())
            using (ManualResetEventSlim releaseB = new ManualResetEventSlim())
            using (CountdownEvent started = new CountdownEvent(2))
            using (ManualResetEventSlim image = new ManualResetEventSlim())
            {
                int third = 0;
                using (JobCoordinator jobs = new JobCoordinator(config, delegate(MediaJob job) {
                    if (job.Request.Id == a.Id) { started.Signal(); releaseA.Wait(job.Cancellation.Token); }
                    else if (job.Request.Id == b.Id) { started.Signal(); releaseB.Wait(job.Cancellation.Token); }
                    else if (job.Request.IsImage) image.Set();
                    else Interlocked.Increment(ref third);
                }, Path.Combine(root, "scheduling")))
                {
                    jobs.Submit(a); jobs.Submit(b); jobs.Submit(c);
                    Check(started.Wait(4000), "two video workers did not overlap");
                    Check(jobs.Snapshot().Single(s => s.Id == c.Id).State == JobState.Queued, "third video was not queued");
                    jobs.Submit(new JobRequest(CaptureAction.CaptureFrame, source, 2000, 2000, config));
                    Check(image.Wait(3000) && !releaseA.IsSet && !releaseB.IsSet, "image lane was blocked by video work");
                    config.MaxVideoJobs = 1; jobs.Reconfigure(); releaseA.Set();
                    Check(Until(() => jobs.Snapshot().Single(s => s.Id == a.Id).Terminal), "first video did not finish");
                    Thread.Sleep(150); Check(third == 0, "lowering concurrency admitted a third worker too early");
                    releaseB.Set(); Check(Until(() => !jobs.Busy) && third == 1, "queued video did not resume");
                    jobs.Retry(a.Id); Check(Until(() => !jobs.Busy), "retry did not finish");
                    Check(jobs.Snapshot().Count(s => s.Start == 1000 && s.End == 5000) == 2, "retry changed the range");
                    Check(jobs.Resources.Capped, "Windows shared CPU hard cap was not applied on this host");
                }
            }
            config.MaxVideoJobs = 2;
        }
        private static void Reservations(string root)
        {
            string folder = Path.Combine(root, "reservations");
            string[] results = new string[12];
            Parallel.For(0, results.Length, i => {
                using (OutputReservation reservation = OutputReservation.Create(folder, "same", ".mov"))
                { File.WriteAllBytes(reservation.StagingPath, new byte[] { (byte)i }); reservation.Commit(); results[i] = reservation.FinalPath; }
            });
            Check(results.Distinct(StringComparer.OrdinalIgnoreCase).Count() == results.Length, "concurrent duplicate outputs collided");
            Check(!Directory.GetFiles(folder, "*.fc-lock").Any() && !Directory.GetFiles(folder, ".fc-*").Any(), "successful reservations leaked");
            using (OutputReservation reservation = OutputReservation.Create(folder, "canceled", ".mov")) { File.WriteAllText(reservation.StagingPath, "partial"); }
            Check(!Directory.GetFiles(folder, "canceled*").Any() && !Directory.GetFiles(folder, ".fc-*").Any(), "canceled output leaked");
            using (OutputReservation reservation = OutputReservation.Create(folder, "external", ".mov"))
            {
                File.WriteAllText(reservation.StagingPath, "new"); File.WriteAllText(reservation.FinalPath, "original");
                bool protectedFile = false; try { reservation.Commit(); } catch (IOException) { protectedFile = true; }
                Check(protectedFile && File.ReadAllText(reservation.FinalPath) == "original", "publication overwrote an existing file");
            }
        }
        private static void Progress()
        {
            List<long> values = new List<long>(); ProgressParser parser = new ProgressParser(values.Add);
            foreach (string line in new[] { "frame=1", "out_time=00:00:01.250000", "out_time_us=1500000", "progress=continue", "out_time_us=-4", "progress=end", "out_time_us=N/A", "progress=continue", "out_time=00:00:02.000000", "progress=end" }) parser.Read(line);
            Check(values.SequenceEqual(new long[] { 1500000, 2000000 }), "progress parser did not prefer valid microseconds or reject invalid samples");
        }
        private static void CancellationAndRecovery(string root, string source, AppConfig config)
        {
            string store = Path.Combine(root, "cancel");
            using (ManualResetEventSlim entered = new ManualResetEventSlim())
            using (JobCoordinator jobs = new JobCoordinator(config, delegate(MediaJob job) { entered.Set(); job.Cancellation.Token.WaitHandle.WaitOne(); job.Cancellation.Token.ThrowIfCancellationRequested(); }, store))
            {
                JobRequest request = new JobRequest(CaptureAction.ExportPrecise, source, 0, 1000, config); jobs.Submit(request);
                Check(entered.Wait(3000), "cancel worker never started");
                string recovery = Path.Combine(root, "recovery"); Directory.CreateDirectory(recovery); File.Copy(Path.Combine(store, "jobs.xml"), Path.Combine(recovery, "jobs.xml"));
                using (JobCoordinator recovered = new JobCoordinator(config, delegate { throw new Exception("must not replay"); }, recovery))
                    Check(recovered.Snapshot().Single().State == JobState.Interrupted && !recovered.Busy, "recovery replayed unfinished work");
                jobs.Cancel(request.Id); Check(Until(() => !jobs.Busy) && jobs.Snapshot().Single().State == JobState.Canceled, "active cancellation was not terminal");
            }
            using (JobCoordinator jobs = new JobCoordinator(config, delegate(MediaJob job) {
                string path = job.Reserve(Path.Combine(root, "committed"), "done", ".png"); File.WriteAllText(path, "finished"); job.CommitOutput(path);
                job.Cancellation.Cancel(); job.Cancellation.Token.ThrowIfCancellationRequested();
            }, Path.Combine(root, "late-cancel")))
            {
                jobs.Submit(new JobRequest(CaptureAction.CaptureFrame, source, 0, 0, config));
                Check(Until(() => !jobs.Busy) && jobs.Snapshot().Single().State == JobState.Succeeded && jobs.Snapshot().Single().Outputs.Length == 1, "late cancellation lost a committed output");
            }
            File.AppendAllText(source, "changed");
            using (JobCoordinator jobs = new JobCoordinator(config, delegate { throw new Exception("must not run"); }, Path.Combine(root, "changed")))
            {
                JobRequest original = new JobRequest(CaptureAction.ExportPrecise, source, 0, 1000, config); File.AppendAllText(source, "again"); jobs.Submit(original);
                Check(Until(() => !jobs.Busy) && jobs.Snapshot().Single().Error.Contains("源文件"), "source identity change was not rejected");
            }
        }
        private static void Ipc()
        {
            using (ResidentIpc server = new ResidentIpc(command => command == "v1 --capture" ? "job:test" : "error:protocol"))
            {
                string result = null;
                Check(Until(() => ResidentIpc.TrySend("--capture", 150, out result)) && result == "job:test", "resident IPC acknowledgement failed");
                Check(ResidentIpc.TrySend("--unknown", 500, out result) && result.StartsWith("error:"), "IPC command rejection failed");
            }
        }
        private static void PartialOutputActions(string root, string source, AppConfig config)
        {
            using (JobCoordinator jobs = new JobCoordinator(config, delegate { }, Path.Combine(root, "partial-output-ui")))
            using (ToolTip tips = new ToolTip())
            using (WorkspaceForm host = new WorkspaceForm(config))
            using (JobRow row = new JobRow(config, new UiTheme("dark"), jobs, tips))
            {
                host.AutoScaleMode = AutoScaleMode.None; host.Content.Controls.Add(row); host.Show(); Realize(host);
                float previousScale = 1;
                foreach (float scale in new[] { 1f, 1.5f, 2f })
                {
                    host.Scale(new SizeF(scale / previousScale, scale / previousScale)); previousScale = scale;
                    Check(Math.Abs(UiScale.For(row) - scale) < .03f, "partial-output host scale mismatch");
                    row.Size = new Size((int)(650 * scale), (int)(124 * scale));
                    foreach (JobState state in new[] { JobState.Succeeded, JobState.Failed, JobState.Canceled, JobState.Running, JobState.Succeeded })
                    {
                        row.UpdateJob(new JobSnapshot { Id = Guid.NewGuid().ToString("N"), Source = source, Title = "Partial output", State = state,
                            Outputs = new[] { source }, ProcessingBackend = "cpu", Error = state == JobState.Failed ? "Companion output failed" : "" });
                        row.PerformLayout();
                        ToolButton[] actions = Children(row).OfType<ToolButton>().Where(b => b.Visible).ToArray();
                        Check(actions.Length == (state == JobState.Succeeded ? 2 : 3), "partial-output actions missing");
                        Check(!actions.Any(a => actions.Any(b => a != b && a.Bounds.IntersectsWith(b.Bounds))), "partial-output actions overlap: " + state + " scale=" + scale);
                    }
                }
            }
        }
        private static IEnumerable<Control> Children(Control control)
        {
            foreach (Control child in control.Controls) { yield return child; foreach (Control descendant in Children(child)) yield return descendant; }
        }
        private static void Realize(Control control)
        {
            IntPtr handle = control.Handle;
            foreach (Control child in control.Controls) Realize(child);
            control.PerformLayout();
        }
        private static void Ui(string root, string source, AppConfig config)
        {
            string renders = Environment.GetEnvironmentVariable("FRAMECLIP_UI_PREVIEW");
            using (JobCoordinator jobs = new JobCoordinator(config, delegate(MediaJob job) { job.Cancellation.Token.WaitHandle.WaitOne(); job.Cancellation.Token.ThrowIfCancellationRequested(); }, Path.Combine(root, "ui-jobs")))
            {
                jobs.Submit(new JobRequest(CaptureAction.ExportPrecise, source, 1025, 59999, config));
                foreach (string language in new[] { UiText.Chinese, UiText.English })
                foreach (string theme in new[] { "light", "dark" })
                foreach (float scale in new[] { 1f, 1.5f, 2f })
                {
                    config.Language = language; config.Theme = theme;
                    using (SettingsForm settings = new SettingsForm(config))
                    using (JobCenterForm center = new JobCenterForm(config, jobs, delegate { }))
                    {
                        foreach (Form form in new Form[] { settings, center })
                        {
                            form.AutoScaleMode = AutoScaleMode.None; form.StartPosition = FormStartPosition.Manual;
                            form.Location = new Point(-30000, -30000); form.ShowInTaskbar = false;
                            Realize(form);
                            Control[] controls = new[] { (Control)form }.Concat(Children(form)).ToArray();
                            Font[] fonts = controls.Select(c => UiTheme.Font(c.Font.SizeInPoints * scale, c.Font.Style)).ToArray();
                            form.Scale(new SizeF(scale, scale));
                            for (int i = 0; i < controls.Length; i++) controls[i].Font = fonts[i];
                            foreach (Button button in controls.OfType<Button>().Where(b => b.Tag is string)) button.Image = UiTheme.Icon((string)button.Tag, theme == "dark", (int)(20 * scale));
                            form.PerformLayout();
                            ShowWindow(form.Handle, 4);
                            for (int tab = 0; tab < (form is JobCenterForm ? 8 : 3); tab++)
                            {
                                JobCenterForm workspace = form as JobCenterForm;
                                SettingsView settingsView = Children(form).OfType<SettingsView>().Single();
                                if (workspace != null) workspace.View = tab >= 5 ? 1 : 0;
                                settingsView.Section = workspace == null ? tab : Math.Max(0, tab - 5);
                                if (workspace != null && tab < 5)
                                {
                                    JobSnapshot[] scene = tab == 4 ? new JobSnapshot[0] : new[] {
                                        new JobSnapshot { Id = "preview-a", Title = "夜景参考 · 片段 A", Source = source, Format = "ProRes 422 HQ", State = tab == 1 ? JobState.Succeeded : tab == 2 ? JobState.Failed : tab == 3 ? JobState.Canceled : JobState.Running,
                                            Fraction = tab == 1 ? 1 : .61, Start = 728000, End = 758000, ElapsedSeconds = 42, RemainingSeconds = 80, Outputs = new string[0], Error = tab == 2 ? "输出目录不可写，未生成完成文件。" : "" },
                                        new JobSnapshot { Id = "preview-b", Title = "室内自然光 · 片段 B", Source = source, Format = "ProRes 422 HQ", State = tab == 0 ? JobState.Running : JobState.Succeeded, Fraction = .28, Start = 1696000, End = 1721000, Outputs = new string[0] },
                                        new JobSnapshot { Id = "preview-c", Title = new string('W', 150), Source = source, Format = "MKV / COPY", State = tab == 0 ? JobState.Queued : JobState.Succeeded, Start = 2525000, End = 2543000, Outputs = new string[0] },
                                        new JobSnapshot { Id = "preview-image", Title = "夜景参考 · 当前帧", Source = source, Format = "PNG · 16-bit", State = JobState.Succeeded, IsImage = true, Start = 846240, End = 846240, Outputs = new string[0] } };
                                    workspace.UpdateJobs(scene);
                                }
                                form.PerformLayout();
                                foreach (Control child in Children(form).ToArray()) child.PerformLayout();
                                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                                    {
                                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                                        int contrast = 0;
                                        Color background = new UiTheme(theme).Background;
                                        for (int y = 50; y < bitmap.Height - 30; y += 3)
                                        for (int x = 20; x < bitmap.Width - 20; x += 3)
                                        { Color p = bitmap.GetPixel(x, y); if (Math.Abs(p.R - background.R) > 50) contrast++; }
                                        Check(contrast > 60, "offscreen native UI rendered blank");
                                        if (!String.IsNullOrEmpty(renders))
                                        {
                                            Directory.CreateDirectory(renders); bitmap.Save(Path.Combine(renders, form.GetType().Name + "-" + language + "-" + theme + "-" + scale + "-" + tab + ".png"));
                                            if (form is JobCenterForm && language == UiText.Chinese && tab == 0 && scale == 1)
                                                using (Bitmap caption = bitmap.Clone(new Rectangle(0, 0, bitmap.Width, 52), bitmap.PixelFormat)) caption.Save(Path.Combine(renders, "FrameClip-caption-" + theme + ".png"));
                                        }
                                    }
                            }
                            Check(Children(form).OfType<Button>().Where(b => b.Tag is string).All(b => b.Image != null), "native icon resource is missing");
                            Check(form.Width >= Math.Min(550 * scale, SystemInformation.MaxWindowTrackSize.Width), "DPI scaling collapsed the form bounds");
                            foreach (ChoiceBox choice in Children(form).OfType<ChoiceBox>().Where(c => c.Visible))
                                Check(choice.Width >= choice.PreferredWidth, "settings choice is truncated: " + choice.AccessibleName);
                            ShowWindow(form.Handle, 0);
                            foreach (Font font in fonts) font.Dispose();
                        }
                    }
                }
                jobs.CancelAll(); Check(Until(() => !jobs.Busy), "UI test worker leaked");
                NativeChrome(config, jobs);
            }
            config.Language = UiText.Chinese; config.Theme = "system";
            using (StatusStripForm strip = new StatusStripForm(config, delegate { }, delegate { }))
            {
                // DrawToBitmap does not show or activate a native window.
                strip.CreateControl();
                using (Bitmap bitmap = new Bitmap(strip.Width, strip.Height)) strip.DrawToBitmap(bitmap, new Rectangle(Point.Empty, strip.Size));
                Check(!strip.Visible, "offscreen status render activated a window");
            }
        }
        private static void NativeChrome(AppConfig config, JobCoordinator jobs)
        {
            using (WorkspaceForm oversized = new WorkspaceForm(config))
            {
                oversized.MinimumSize = new Size(1440, 1220);
                IntPtr data = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TrackBounds)));
                try
                {
                    Marshal.StructureToPtr(new TrackBounds { MaxTrack = new Size(1040, 800) }, data, false);
                    SendMessage(oversized.Handle, 0x24, IntPtr.Zero, data);
                    TrackBounds limits = (TrackBounds)Marshal.PtrToStructure(data, typeof(TrackBounds));
                    Check(limits.MinTrack == oversized.MinimumSize, "scaled minimum track size was lost");
                    Check(limits.MaxTrack.Width >= limits.MinTrack.Width && limits.MaxTrack.Height >= limits.MinTrack.Height, "desktop maximum track size contradicts scaled minimum");
                }
                finally { Marshal.FreeHGlobal(data); }
            }
            foreach (string mode in new[] { "light", "dark" })
            {
                config.Theme = mode; config.Language = UiText.Chinese;
                using (JobCenterForm form = new JobCenterForm(config, jobs, delegate { }))
                {
                    // Exercise the production DPI path; manual Scale/DrawToBitmap
                    // alone cannot detect USER32 overwriting the custom caption.
                    form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-12000, -12000);
                    form.ShowInTaskbar = false; form.ShowSettings(1); form.Show();
                    Application.DoEvents();
                    Check(form.AutoScaleMode == AutoScaleMode.Dpi, "native DPI path was not tested");
                    Check(form.ClientSize == form.Size, "native nonclient frame reduced client area");
                    Check(Math.Abs(form.Width - 720 * form.UiScale) <= 3, "DPI scaled fonts without scaling the initial window");
                    Check(SendMessage(form.Handle, 0x86, IntPtr.Zero, IntPtr.Zero) == new IntPtr(1), "deactivation may repaint a classic caption");
                    Check(SendMessage(form.Handle, 0x86, new IntPtr(1), IntPtr.Zero) == new IntPtr(1), "activation may repaint a classic caption");
                    SendMessage(form.Handle, 0x85, new IntPtr(1), IntPtr.Zero);
                    form.Invalidate(true); form.Update();
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                    {
                        using (Graphics graphics = Graphics.FromImage(bitmap))
                        {
                            IntPtr dc = graphics.GetHdc();
                            try { Check(PrintWindow(form.Handle, dc, 0), "native window capture failed"); }
                            finally { graphics.ReleaseHdc(dc); }
                        }
                        Color expected = new UiTheme(mode).Background;
                        int y = Math.Max(3, (int)(form.UiScale * 7));
                        Check(bitmap.GetPixel(bitmap.Width / 2, y).ToArgb() == expected.ToArgb(), "system caption overwrote the custom title bar");
                        string renders = Environment.GetEnvironmentVariable("FRAMECLIP_UI_PREVIEW");
                        if (!String.IsNullOrEmpty(renders)) bitmap.Save(Path.Combine(renders, "NativeWindow-" + mode + ".png"));
                    }
                    Control caption = Children(form).OfType<CaptionButton>().First().Parent.Parent;
                    Check(caption.Visible && caption.Top == form.Padding.Top && caption.Height > 25, "custom title bar is hidden or collapsed");
                    Check(form.Content.Top >= caption.Bottom, "content overlaps custom title bar");
                    foreach (ChoiceBox choice in Children(form).OfType<ChoiceBox>().Where(c => c.Visible))
                        Check(choice.Width >= choice.PreferredWidth, "production DPI truncated settings choice");
                    foreach (ChoiceBox choice in Children(form).OfType<ChoiceBox>().Where(c => c.Visible))
                    {
                        MethodInfo popup = typeof(ChoiceBox).GetMethod("Popup", BindingFlags.NonPublic | BindingFlags.Instance);
                        for (int pass = 0; pass < 3; pass++)
                        {
                            popup.Invoke(choice, null); Application.DoEvents();
                            ContextMenuStrip menu = (ContextMenuStrip)typeof(ChoiceBox).GetField("menu", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(choice);
                            ToolStripItem item = menu.Items[pass % menu.Items.Count];
                            item.PerformClick();
                            typeof(ToolStripDropDown).GetMethod("OnItemClicked", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, new object[] { new ToolStripItemClickedEventArgs(item) });
                            Application.DoEvents();
                            Check(!menu.IsDisposed && !menu.Visible, "choice menu disposed inside item-click teardown");
                        }
                    }
                    form.View = 0;
                    for (int pass = 0; pass < 3; pass++)
                    {
                        typeof(JobCenterForm).GetMethod("ShowFilter", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, null);
                        ContextMenuStrip menu = (ContextMenuStrip)typeof(JobCenterForm).GetField("filterMenu", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(form);
                        ToolStripItem item = menu.Items[pass]; item.PerformClick();
                        typeof(ToolStripDropDown).GetMethod("OnItemClicked", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, new object[] { new ToolStripItemClickedEventArgs(item) });
                        Application.DoEvents();
                        Check(!menu.IsDisposed && !menu.Visible && form.Filter == pass, "filter menu lifetime or selection failed");
                    }
                    Size normal = form.Size;
                    form.Size = new Size(normal.Width + 40, normal.Height + 20);
                    Check(form.ClientSize == form.Size && form.Content.Top >= caption.Bottom, "resize restored native frame or overlapped title bar");
                    CaptionButton maximize = Children(form).OfType<CaptionButton>().Single(c => c.IconName == "square");
                    maximize.PerformClick(); Application.DoEvents();
                    Check(form.WindowState == FormWindowState.Maximized && maximize.IconName == "copy", "custom maximize action failed");
                    maximize.PerformClick(); Application.DoEvents();
                    Check(form.WindowState == FormWindowState.Normal && maximize.IconName == "square", "custom restore action failed");
                    form.Hide();
                }
            }
        }
        private static void SaveUi(string root)
        {
            foreach (string language in new[] { UiText.Chinese, UiText.English })
            foreach (string mode in new[] { "light", "dark" })
            using (SettingsForm form = new SettingsForm(new AppConfig { Language = language, Theme = mode, LibraryRootDirectory = Path.Combine(root, "save-output") }))
            {
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-12000, -12000); form.ShowInTaskbar = false;
                form.Show(); Application.DoEvents();
                SettingsView view = Children(form).OfType<SettingsView>().Single();
                ToolButton save = (ToolButton)typeof(SettingsView).GetField("save", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
                TextBox path = (TextBox)typeof(SettingsView).GetField("root", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
                Label note = (Label)typeof(SettingsView).GetField("note", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
                Check(!save.Enabled && !view.HasChanges && !save.Primary, "unchanged settings show an active save action");
                Check(Math.Abs(save.Width - 64 * form.UiScale) <= 2 && Math.Abs(save.Height - 28 * form.UiScale) <= 2, "save action stretched with footer docking");
                Check(Math.Abs(save.Top + save.Height / 2f - save.Parent.Height / 2f) <= 1, "save action is not vertically centered in footer");
                path.Text = "relative-output";
                Check(save.Enabled && save.Primary && view.HasChanges && note.Text.Length > 0, "pending save state missing");
                Check(!view.Save() && save.Enabled && view.HasChanges && note.ForeColor == new UiTheme(mode).ErrorColor, "save failure removed retry or error feedback");
                path.Text = Path.Combine(root, "save-output");
                int events = 0; SettingsSavedEventArgs result = null;
                // The wrapper closes on save; test the hosted view without its dialog handler.
                typeof(SettingsView).GetField("Saved", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, new EventHandler<SettingsSavedEventArgs>(delegate(object sender, SettingsSavedEventArgs e) { events++; result = e; }));
                Check(view.Save() && !view.HasChanges && !save.Enabled && !save.Primary && note.Text.Length > 0, "saved feedback missing or action still enabled");
                Check(events == 1 && result != null && !result.AppearanceChanged, "ordinary save requested unnecessary window rebuild");
                string renders = Environment.GetEnvironmentVariable("FRAMECLIP_UI_PREVIEW");
                if (!String.IsNullOrEmpty(renders))
                    using (Bitmap bitmap = new Bitmap(save.Parent.Width, save.Parent.Height)) { save.Parent.DrawToBitmap(bitmap, save.Parent.ClientRectangle); bitmap.Save(Path.Combine(renders, "SaveFooter-" + language + "-" + mode + ".png")); }
                path.Text += "-changed";
                Check(save.Enabled && save.Primary && view.HasChanges, "new edits did not reset saved state");
                view.ShowSaved(); form.Hide();
            }
            using (Control dispatcher = new Control())
            {
                dispatcher.CreateControl();
                AppConfig config = new AppConfig { LibraryRootDirectory = Path.Combine(root, "save-hosted") };
                using (CaptureEngine engine = new CaptureEngine(config, dispatcher))
                {
                    engine.OpenSettings(); Application.DoEvents();
                    QuietDesktop desktop = (QuietDesktop)typeof(CaptureEngine).GetField("desktop", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(engine);
                    FieldInfo centerField = typeof(QuietDesktop).GetField("center", BindingFlags.Instance | BindingFlags.NonPublic);
                    JobCenterForm before = (JobCenterForm)centerField.GetValue(desktop);
                    before.Settings.Section = 1;
                    Check(before.Settings.Save(), "hosted settings save failed"); Application.DoEvents();
                    Check(Object.ReferenceEquals(before, centerField.GetValue(desktop)) && !before.IsDisposed && before.Settings.Section == 1, "ordinary save destroyed page and success feedback");
                    before.Settings.ShowSaved(); before.Hide();
                    NotifyIcon tray = (NotifyIcon)typeof(QuietDesktop).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(desktop);
                    ContextMenuStrip menu = tray.ContextMenuStrip;
                    menu.Items[0].Click += delegate { desktop.RefreshTheme(); };
                    for (int pass = 0; pass < 3; pass++)
                    {
                        config.Theme = pass % 2 == 0 ? "dark" : "light";
                        config.Language = pass % 2 == 0 ? UiText.English : UiText.Chinese;
                        menu.Show(new Point(20, 20));
                        Exception clickFailure = null;
                        try
                        {
                            menu.Items[0].PerformClick();
                            typeof(ToolStripDropDown).GetMethod("OnItemClicked", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(menu, new object[] { new ToolStripItemClickedEventArgs(menu.Items[0]) });
                        }
                        catch (Exception error) { clickFailure = error; }
                        Application.DoEvents();
                        Check(clickFailure == null, "tray appearance refresh during click threw: " + clickFailure);
                        Check(!menu.IsDisposed && Object.ReferenceEquals(menu, tray.ContextMenuStrip), "appearance refresh disposed an active tray menu");
                        Check(!menu.Visible && menu.Items.Count == 5, "tray menu teardown failed or duplicated actions");
                        Check(menu.Items[0].Text == UiText.Choose(config.Language, "任务中心", "Task center"), "reused tray menu did not refresh language");
                    }
                }
            }
        }
        private static void NotificationUi(string root)
        {
            AppConfig config = new AppConfig();
            Check(config.NotificationPosition == "bottom-right" && config.NotificationMonitor == "", "legacy notification defaults changed");
            Rectangle area = new Rectangle(-2560, -200, 2560, 1400); Size size = new Size(570, 114);
            string[] positions = { "top-left", "top-right", "bottom-left", "bottom-right" };
            foreach (string position in positions)
            {
                config.NotificationPosition = position; Point point = NotificationPlacement.Location(config, area, size, 27);
                Check(area.Contains(new Rectangle(point, size)), "corner escaped negative-coordinate monitor");
                Check(point.X == (position.EndsWith("left") ? area.Left + 27 : area.Right - size.Width - 27), "horizontal corner mismatch");
                Check(point.Y == (position.StartsWith("top") ? area.Top + 27 : area.Bottom - size.Height - 27), "vertical corner mismatch");
            }
            NotificationPlacement.Capture(config, new Point(-2100, 150), size, area, "test-monitor");
            Check(NotificationPlacement.Location(config, area, size, 27) == new Point(-2100, 150), "custom position drifted after capture");
            config.Save(); AppConfig loaded = AppConfig.Load();
            Check(loaded.NotificationPosition == "custom" && loaded.NotificationMonitor == "test-monitor" && loaded.NotificationX == config.NotificationX && loaded.NotificationY == config.NotificationY, "notification configuration failed roundtrip");
            Check(NotificationPlacement.WorkArea(config, area) == Screen.PrimaryScreen.WorkingArea, "disconnected monitor did not fall back safely");
            Rectangle smaller = new Rectangle(0, 0, 1280, 720);
            Check(smaller.Contains(new Rectangle(NotificationPlacement.Location(config, smaller, size, 27), size)), "custom position escaped changed resolution");
            config.NotificationPosition = "invalid"; config.NotificationX = Double.NaN; config.NotificationY = Double.PositiveInfinity; config.NormalizeJobs();
            Check(config.NotificationPosition == "bottom-right" && config.NotificationX == 1 && config.NotificationY == 1, "invalid notification values not normalized");
            foreach (string language in new[] { UiText.Chinese, UiText.English })
            foreach (string mode in new[] { "light", "dark" })
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                config = new AppConfig { Language = language, Theme = mode, ReducedMotion = true };
                using (StatusStripForm strip = new StatusStripForm(config, delegate { }, delegate { }))
                {
                    strip.Present(language == UiText.Chinese ? "2 项进行中 · 导出中 61%" : "2 active · Exporting 61%", new string('W', 130), .61, false, Screen.PrimaryScreen.WorkingArea);
                    strip.SetSurfaceScale(scale); strip.Location = new Point(-12000, -12000); Application.DoEvents();
                    Check(strip.Size == new Size((int)(380 * scale), (int)(76 * scale)), "banner DPI size mismatch");
                    Check(strip.HeadingBounds.Bottom + 2 * scale <= strip.DetailBounds.Top && strip.DetailBounds.Bottom < 65 * scale, "banner text/progress overlap");
                    Check(strip.DetailBounds.Width > strip.HeadingBounds.Width, "filename cannot use full second row");
                    foreach (ToolButton button in Children(strip).OfType<ToolButton>().Where(b => b.Visible))
                        Check(!button.Bounds.IntersectsWith(strip.HeadingBounds) && !button.Bounds.IntersectsWith(strip.DetailBounds), "banner action overlaps text");
                    Check((GetWindowLong(strip.Handle, -20) & 0x08000000) != 0, "passive banner can activate");
                    using (Bitmap bitmap = new Bitmap(strip.Width, strip.Height))
                    {
                        strip.DrawToBitmap(bitmap, strip.ClientRectangle);
                        Check(bitmap.GetPixel(strip.Width / 2, 3).ToArgb() == new UiTheme(mode).Background.ToArgb(), "banner theme differs from settings");
                        string renders = Environment.GetEnvironmentVariable("FRAMECLIP_UI_PREVIEW");
                        if (!String.IsNullOrEmpty(renders)) bitmap.Save(Path.Combine(renders, "Banner-" + language + "-" + mode + "-" + scale + ".png"));
                    }
                    strip.Hide();
                }
                using (StatusStripForm preview = new StatusStripForm(config, null, null, true))
                {
                    preview.PreparePlacement(Screen.PrimaryScreen.WorkingArea); preview.SetSurfaceScale(scale); preview.Location = new Point(-12000, -12000); preview.Show(); Application.DoEvents();
                    Check((GetWindowLong(preview.Handle, -20) & 0x08000000) == 0, "placement preview cannot accept input");
                    Check(preview.BannerSize.Height == (int)(76 * scale), "placement persists confirmation footer height");
                    ToolButton cancel = Children(preview).OfType<ToolButton>().Single(b => b.Text == (language == UiText.Chinese ? "取消" : "Cancel")); cancel.PerformClick();
                    Check(preview.DialogResult == DialogResult.Cancel && config.NotificationPosition == "bottom-right", "cancel mutated placement");
                }
                using (CornerPicker picker = new CornerPicker(new UiTheme(mode), language, "bottom-right"))
                {
                    int events = 0; picker.ValueChanged += delegate { events++; };
                    picker.Controls.OfType<CornerTarget>().First().PerformClick();
                    Check(picker.Value == "top-left" && events == 1 && picker.Controls.OfType<CornerTarget>().Count(b => b.Selected) == 1, "visual corner picker selection failed");
                    Check(picker.Controls.OfType<CornerTarget>().All(b => b.Text.Length == 0 && !String.IsNullOrEmpty(b.AccessibleName)), "corner picker contains visible direction text or lost accessible labels");
                    CornerTarget first = picker.Controls.OfType<CornerTarget>().First();
                    typeof(CornerTarget).GetMethod("OnKeyDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(first, new object[] { new KeyEventArgs(Keys.Right) });
                    Check(picker.Value == "top-right" && picker.Controls.OfType<CornerTarget>().Count(b => b.Selected) == 1, "corner picker arrow navigation failed");
                    Check((picker.Controls.OfType<CornerTarget>().ElementAt(1).AccessibilityObject.State & AccessibleStates.Checked) != 0, "corner selection not exposed to accessibility");
                }
            }
            using (Control dispatcher = new Control())
            {
                dispatcher.CreateControl();
                config = new AppConfig { Theme = "light", LibraryRootDirectory = Path.Combine(root, "notification-hosted") };
                using (CaptureEngine engine = new CaptureEngine(config, dispatcher))
                {
                    engine.OpenSettings(); Application.DoEvents();
                    QuietDesktop desktop = (QuietDesktop)typeof(CaptureEngine).GetField("desktop", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(engine);
                    FieldInfo centerField = typeof(QuietDesktop).GetField("center", BindingFlags.Instance | BindingFlags.NonPublic);
                    FieldInfo stripField = typeof(QuietDesktop).GetField("strip", BindingFlags.Instance | BindingFlags.NonPublic);
                    JobCenterForm center = (JobCenterForm)centerField.GetValue(desktop); center.Settings.Section = 2;
                    ChoiceBox themeChoice = (ChoiceBox)typeof(SettingsView).GetField("themeChoice", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(center.Settings);
                    themeChoice.Value = "dark"; Check(center.Settings.Save(), "theme save failed"); Application.DoEvents();
                    Check(((StatusStripForm)stripField.GetValue(desktop)).BackColor == new UiTheme("dark").Background, "settings save failed to sync banner theme");
                    center = (JobCenterForm)centerField.GetValue(desktop);
                    NotificationPositionView position = Children(center).OfType<NotificationPositionView>().Single();
                    string ini = File.ReadAllText(AppConfig.ConfigPath);
                    using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 50 })
                    {
                        timer.Tick += delegate {
                            StatusStripForm preview = Application.OpenForms.OfType<StatusStripForm>().FirstOrDefault(s => s.Visible);
                            if (preview == null) return; timer.Stop();
                            Children(preview).OfType<ToolButton>().Single(b => b.Text == "取消").PerformClick();
                        };
                        timer.Start(); position.ChooseCustom();
                    }
                    Check(File.ReadAllText(AppConfig.ConfigPath) == ini, "canceling custom preview changed persisted settings");
                    Rectangle liveArea = Screen.PrimaryScreen.WorkingArea; Point expected = new Point(liveArea.Left + 80, liveArea.Top + 90);
                    using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 50 })
                    {
                        timer.Tick += delegate {
                            StatusStripForm preview = Application.OpenForms.OfType<StatusStripForm>().FirstOrDefault(s => s.Visible);
                            if (preview == null) return; timer.Stop(); preview.Location = expected;
                            Children(preview).OfType<ToolButton>().Single(b => b.Text == "确定").PerformClick();
                        };
                        timer.Start(); position.ChooseCustom();
                    }
                    AppConfig restored = AppConfig.Load();
                    Check(restored.NotificationPosition == "custom" && restored.NotificationMonitor == Screen.PrimaryScreen.DeviceName, "custom confirmation did not persist selected monitor");
                    StatusStripForm strip = (StatusStripForm)stripField.GetValue(desktop);
                    Check(NotificationPlacement.Location(restored, liveArea, strip.BannerSize, 27) == expected, "custom confirmed position differs from next banner");
                    Check(!center.Settings.HasChanges && Object.ReferenceEquals(center, centerField.GetValue(desktop)), "custom confirmation lost save feedback");
                    center.Hide();
                }
            }
        }
        private static void CaptionUi()
        {
            string renders = Environment.GetEnvironmentVariable("FRAMECLIP_UI_PREVIEW");
            foreach (string mode in new[] { "light", "dark" })
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                UiTheme theme = new UiTheme(mode);
                int width = (int)Math.Round(34 * scale), height = (int)Math.Round(28 * scale);
                using (Bitmap sheet = new Bitmap(width * 4, height * 3))
                using (Graphics graphics = Graphics.FromImage(sheet))
                {
                    graphics.Clear(theme.Background);
                    string[] icons = { "minus", "square", "copy", "x" };
                    for (int i = 0; i < icons.Length; i++)
                    using (CaptionButton button = new CaptionButton(theme, icons[i], icons[i], icons[i] == "x", delegate { }))
                    {
                        button.Size = new Size(width, height); button.CreateControl();
                        Check(button.Width == width && button.Height == height && button.TabStop, "caption target or keyboard access changed");
                        int glyphSize = (int)Math.Round((i == 1 || i == 2 ? 10 : 14) * scale);
                        Check(UiTheme.Icon(icons[i], theme.Dark, glyphSize) != null, "caption DPI icon is missing");
                        using (Bitmap normal = new Bitmap(width, height))
                        using (Bitmap hover = new Bitmap(width, height))
                        using (Bitmap pressed = new Bitmap(width, height))
                        {
                            button.DrawToBitmap(normal, button.ClientRectangle);
                            typeof(CaptionButton).GetMethod("OnMouseEnter", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(button, new object[] { EventArgs.Empty });
                            button.DrawToBitmap(hover, button.ClientRectangle);
                            typeof(CaptionButton).GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(button, new object[] { new MouseEventArgs(MouseButtons.Left, 1, width / 2, height / 2, 0) });
                            button.DrawToBitmap(pressed, button.ClientRectangle);
                            Check(normal.GetPixel(width / 2, 2).ToArgb() != hover.GetPixel(width / 2, 2).ToArgb(), "caption hover feedback is missing");
                            Check(hover.GetPixel(width / 2, 2).ToArgb() != pressed.GetPixel(width / 2, 2).ToArgb(), "caption press feedback is missing");
                            Check(normal.GetPixel(0, 0).ToArgb() == hover.GetPixel(0, 0).ToArgb(), "caption hover corners are not rounded");
                            if (i == 1 || i == 2)
                            {
                                int left = width, right = -1;
                                for (int x = 0; x < width; x++)
                                for (int y = 0; y < height; y++)
                                    if (normal.GetPixel(x, y).ToArgb() != theme.Background.ToArgb()) { left = Math.Min(left, x); right = Math.Max(right, x); }
                                Check(right >= left && right - left + 1 <= Math.Ceiling(10 * scale), "maximize glyph is oversized");
                            }
                            graphics.DrawImageUnscaled(normal, i * width, 0);
                            graphics.DrawImageUnscaled(hover, i * width, height);
                            graphics.DrawImageUnscaled(pressed, i * width, height * 2);
                        }
                    }
                    if (!String.IsNullOrEmpty(renders)) { Directory.CreateDirectory(renders); sheet.Save(Path.Combine(renders, "CaptionButtons-" + mode + "-" + scale + ".png")); }
                }
            }
        }
        private static void Integration(string root, AppConfig config, string ffmpeg)
        {
            config.FfmpegPath = ffmpeg; config.FfprobePath = Path.Combine(Path.GetDirectoryName(ffmpeg), "ffprobe.exe");
            string source = Path.Combine(root, "Synthetic-2026.mkv");
            using (CaptureEngine helper = new CaptureEngine(config, null))
                helper.RunProcess(ffmpeg, "-hide_banner -loglevel error -y -f lavfi -i testsrc2=size=1280x720:rate=24 -f lavfi -i sine=frequency=440:sample_rate=48000 -t 6 -c:v ffv1 -pix_fmt yuv422p10le -color_primaries bt709 -color_trc bt709 -colorspace bt709 -c:a pcm_s24le \"" + source + "\"", "fixture", 30000);
            using (Control dispatcher = new Control())
            {
                dispatcher.CreateControl();
                using (CaptureEngine engine = new CaptureEngine(config, dispatcher))
                {
                    string a = engine.Jobs.Submit(new JobRequest(CaptureAction.ExportPrecise, source, 0, 4000, config));
                    string b = engine.Jobs.Submit(new JobRequest(CaptureAction.ExportPrecise, source, 1000, 5000, config));
                    string image = engine.Jobs.Submit(new JobRequest(CaptureAction.CaptureFrame, source, 2000, 2000, config));
                    Stopwatch latency = Stopwatch.StartNew(); bool independentImage = false;
                    while (engine.Jobs.Busy && latency.ElapsedMilliseconds < 90000)
                    {
                        JobSnapshot[] live = engine.Jobs.Snapshot();
                        if (live.Any(s => s.Id == image && s.State == JobState.Succeeded) && live.Count(s => (s.Id == a || s.Id == b) && !s.Terminal) > 0) independentImage = true;
                        Thread.Sleep(10);
                    }
                    Check(independentImage, "real screenshot did not finish while video work was active");
                    Check(Until(() => !engine.Jobs.Busy, 90000), "real concurrent FFmpeg jobs timed out");
                    foreach (JobSnapshot s in engine.Jobs.Snapshot().Where(s => s.Id == a || s.Id == b || s.Id == image))
                    {
                        Check(s.State == JobState.Succeeded && s.Outputs.Length == 1, "real export failed: " + s.Error);
                        if (s.Outputs.Length > 0)
                        {
                            string metadata = new CaptureEngine(config, null).RunProcess(config.FfprobePath, "-v error -show_entries stream=codec_name,pix_fmt,color_transfer,color_primaries -of compact \"" + s.Outputs[0] + "\"", "fidelity probe", 15000);
                            Check(metadata.Contains(s.IsImage ? "rgb48be" : "prores") && metadata.Contains("bt709"), "real output format/color tags changed");
                        }
                    }
                    config.ImageFormat = "tiff16";
                    string tiff = engine.Jobs.Submit(new JobRequest(CaptureAction.CaptureFrame, source, 1000, 1000, config));
                    string original = engine.Jobs.Submit(new JobRequest(CaptureAction.ExportOriginal, source, 1000, 3000, config));
                    Check(Until(() => !engine.Jobs.Busy, 30000), "TIFF/source-copy jobs timed out");
                    foreach (JobSnapshot s in engine.Jobs.Snapshot().Where(s => s.Id == tiff || s.Id == original)) Check(s.State == JobState.Succeeded, "real TIFF/source-copy failed: " + s.Error);
                    Check(!Directory.GetFiles(config.LibraryRootDirectory, ".fc-*", SearchOption.AllDirectories).Any(), "real jobs leaked staging files");
                }
            }
            config.ImageFormat = "png16";
        }
    }
}
