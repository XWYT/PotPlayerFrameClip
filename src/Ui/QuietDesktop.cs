using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace PotPlayerFrameClip
{
    internal sealed class QuietDesktop : IDisposable
    {
        private readonly AppConfig config;
        private readonly CaptureEngine engine;
        private readonly Control dispatcher;
        private readonly NotifyIcon tray;
        private StatusStripForm strip;
        private readonly System.Windows.Forms.Timer tick;
        private JobCenterForm center;
        private bool disposed;
        private int pending;
        private string signature = String.Empty, message, messageTitle;
        private DateTime changedAt = DateTime.MinValue, messageUntil;
        private bool dark;
        internal QuietDesktop(AppConfig config, CaptureEngine engine, Control dispatcher)
        {
            this.config = config; this.engine = engine; this.dispatcher = dispatcher;
            tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "FrameClip", Visible = true };
            tray.DoubleClick += delegate { OpenTasks(); };
            tick = new System.Windows.Forms.Timer { Interval = 500 }; tick.Tick += delegate { Refresh(); };
            engine.Jobs.Changed += QueueRefresh;
            signature = Signature(engine.Jobs.Snapshot()); RefreshTheme();
        }
        private string T(string zh, string en) { return UiText.Choose(config.Language, zh, en); }
        private static string Signature(JobSnapshot[] jobs) { return String.Join(";", jobs.Select(s => s.Id + ":" + s.State)); }
        private void QueueRefresh()
        {
            if (disposed || Interlocked.Exchange(ref pending, 1) != 0) return;
            try { dispatcher.BeginInvoke(new Action(delegate { Interlocked.Exchange(ref pending, 0); if (!disposed && !tick.Enabled) Refresh(); })); }
            catch (InvalidOperationException) { Interlocked.Exchange(ref pending, 0); }
        }
        internal void RefreshTheme()
        {
            if (disposed) return;
            dark = new UiTheme(config.Theme).Dark;
            if (strip != null) strip.Dispose();
            strip = new StatusStripForm(config, OpenTasks, delegate { config.NotificationMode = config.NotificationMode == "pinned" ? "compact" : "pinned"; config.Save(); Refresh(); });
            ContextMenuStrip menu = tray.ContextMenuStrip;
            if (menu == null)
            {
                menu = new ContextMenuStrip();
                menu.Items.Add(String.Empty, null, delegate { OpenTasks(); });
                menu.Items.Add(String.Empty, null, delegate { engine.OpenSettings(); });
                menu.Items.Add(String.Empty, null, delegate { JobText.Open(config.LibraryRootDirectory, false); });
                menu.Items.Add(new ToolStripSeparator()); menu.Items.Add(String.Empty, null, delegate { engine.Quit(); });
                tray.ContextMenuStrip = menu;
            }
            // Item-click teardown still uses this popup after callbacks return.
            menu.Items[0].Text = T("任务中心", "Task center");
            menu.Items[1].Text = T("设置", "Settings");
            menu.Items[2].Text = T("打开输出目录", "Open output folder");
            menu.Items[4].Text = T("退出", "Quit");
            Refresh();
        }
        private void EnsureCenter()
        {
            if (center == null || center.IsDisposed) center = new JobCenterForm(config, engine.Jobs, delegate { engine.Execute(CaptureAction.CaptureFrame); }, SettingsSaved);
        }
        private void SettingsSaved(object sender, SettingsSavedEventArgs args)
        {
            engine.Jobs.Reconfigure();
            if (!args.AppearanceChanged) { center.UpdateJobs(); Refresh(); return; }
            RefreshTheme();
            Rectangle bounds = center.Bounds; FormWindowState state = center.WindowState; int section = center.Settings.Section;
            dispatcher.BeginInvoke(new Action(delegate {
                if (disposed) return;
                center.Dispose(); center = null; EnsureCenter(); center.Bounds = bounds; center.ShowSettings(section); center.Settings.ShowSaved(); center.Show(); center.WindowState = state;
                if (args.LanguageChanged) Message(T("语言已保存", "Language saved"), args.MenuLanguageApplied ? T("重新打开播放器可刷新菜单", "Restart the player to refresh menus") : T("播放器菜单未更新，请检查权限", "Player menus were not updated; check permissions"), 5000);
                Refresh();
            }));
        }
        internal void OpenSettings()
        {
            EnsureCenter(); center.ShowSettings(); ShowCenter();
        }
        internal void OpenTasks()
        {
            EnsureCenter(); center.View = 0; ShowCenter();
        }
        private void ShowCenter()
        {
            center.UpdateJobs(); center.Show(); if (center.WindowState == FormWindowState.Minimized) center.WindowState = FormWindowState.Normal; center.Activate();
        }
        internal void Message(string title, string text, int milliseconds)
        {
            messageTitle = title; message = text; messageUntil = DateTime.UtcNow.AddMilliseconds(milliseconds); Refresh();
        }
        private void Refresh()
        {
            if (disposed) return;
            if (config.Theme == "system" && new UiTheme(config.Theme).Dark != dark) { RefreshTheme(); return; }
            JobSnapshot[] jobs = engine.Jobs.Snapshot();
            string next = Signature(jobs); if (signature != next) { signature = next; changedAt = DateTime.UtcNow; }
            JobSnapshot[] active = jobs.Where(s => !s.Terminal).ToArray();
            int queued = active.Count(s => s.State == JobState.Queued), failed = jobs.Count(s => s.State == JobState.Failed || s.State == JobState.Interrupted);
            tray.Text = "FrameClip | " + active.Length + T(" 进行中", " active") + (failed > 0 ? " | " + failed + T(" 失败", " failed") : "");
            if (center != null && !center.IsDisposed && center.Visible) center.UpdateJobs(jobs);
            bool recent = (DateTime.UtcNow - changedAt).TotalSeconds < 5;
            bool note = DateTime.UtcNow < messageUntil;
            if (active.Length > 0 || recent || note || (center != null && !center.IsDisposed && center.Visible)) tick.Start(); else tick.Stop();
            Rectangle area; bool fullScreen;
            bool foreground = PlayerArea(out area, out fullScreen);
            bool pinned = config.NotificationMode == "pinned";
            bool show = config.NotificationMode != "tray" && (active.Length > 0 || recent || note) &&
                (pinned || foreground) && (pinned || !fullScreen || recent || note);
            if (!show) { strip.Hide(); return; }
            JobSnapshot current = active.FirstOrDefault(s => s.State != JobState.Queued);
            string title = note ? messageTitle : active.Length > 0 ? active.Length + T(" 项进行中", " active") + (queued > 0 ? " · " + queued + T(" 项等待", " queued") : "") : failed > 0 ? failed + T(" 项需要检查", " need attention") : T("任务已完成", "Tasks completed");
            string detail = note ? message : current != null ? current.Title : T("FrameClip", "FrameClip");
            if (!note && current != null) title += " · " + JobText.State(current, config.Language) + (current.Fraction.HasValue ? " " + (current.Fraction.Value * 100).ToString("0") + "%" : "");
            strip.Present(title, detail, current == null ? (active.Length == 0 && !note ? (double?)1 : null) : current.Fraction, active.Length == 0 && failed > 0, area);
        }
        private bool PlayerArea(out Rectangle area, out bool fullScreen)
        {
            IntPtr window = NativeMethods.GetForegroundWindow(); area = Screen.FromHandle(window).WorkingArea; fullScreen = false;
            uint id; NativeMethods.GetWindowThreadProcessId(window, out id);
            try
            {
                using (Process process = Process.GetProcessById((int)id))
                {
                    if (!process.ProcessName.StartsWith("PotPlayer", StringComparison.OrdinalIgnoreCase)) return false;
                }
                NativeMethods.RECT rect;
                if (NativeMethods.GetWindowRect(window, out rect))
                {
                    Rectangle bounds = Screen.FromHandle(window).Bounds;
                    fullScreen = rect.Left <= bounds.Left && rect.Top <= bounds.Top && rect.Right >= bounds.Right && rect.Bottom >= bounds.Bottom;
                }
                return true;
            }
            catch { return false; }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; engine.Jobs.Changed -= QueueRefresh;
            tick.Dispose(); strip.Dispose(); if (center != null) center.Dispose();
            tray.Visible = false; if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose(); tray.Dispose();
        }
    }
}
