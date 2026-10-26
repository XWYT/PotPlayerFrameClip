using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace PotPlayerFrameClip
{
    internal enum JobState { Queued, Preparing, Running, Finalizing, Canceling, Succeeded, Failed, Canceled, Interrupted }

    internal sealed class JobRequest
    {
        internal readonly string Id, Source, ParentId, Format;
        internal readonly CaptureAction Action;
        internal readonly long Start, End, SourceLength, SourceModified;
        internal readonly DateTime Submitted;
        private readonly AppConfig settings;
        internal AppConfig Settings { get { return settings.Copy(); } }
        internal bool IsImage { get { return Action == CaptureAction.CaptureFrame; } }
        internal long Duration { get { return IsImage ? 0 : End - Start; } }

        internal JobRequest(CaptureAction action, string source, long start, long end, AppConfig config,
            string parentId = null, string id = null, long length = -1, long modified = -1, DateTime? submitted = null)
        {
            Id = id ?? Guid.NewGuid().ToString("N");
            Guid parsedId;
            if (!Guid.TryParseExact(Id, "N", out parsedId)) throw new ArgumentException("Invalid job id.");
            ParentId = parentId ?? String.Empty;
            Source = Path.GetFullPath(source);
            Action = action;
            Start = start;
            End = end;
            if (action != CaptureAction.CaptureFrame && action != CaptureAction.ExportOriginal && action != CaptureAction.ExportPrecise)
                throw new ArgumentException("Unsupported media job.");
            if (start < 0 || (action != CaptureAction.CaptureFrame && end <= start)) throw new ArgumentException("Invalid media range.");
            Submitted = submitted ?? DateTime.UtcNow;
            settings = config.Copy();
            Format = IsImage ? settings.ImageFormat.ToUpperInvariant() : action == CaptureAction.ExportOriginal ? "MKV / COPY" : CaptureFormats.GetVideoSpec(settings.VideoPreset).DisplayName;
            FileInfo file = new FileInfo(Source);
            SourceLength = length >= 0 ? length : file.Length;
            SourceModified = modified >= 0 ? modified : file.LastWriteTimeUtc.Ticks;
        }

        internal void ValidateSource()
        {
            FileInfo file = new FileInfo(Source);
            if (!file.Exists || file.Length != SourceLength || file.LastWriteTimeUtc.Ticks != SourceModified)
                throw new IOException(UiText.Choose(settings.Language, "源文件已改变或不可用，请重新提交任务。", "Source changed or is unavailable. Submit a new job."));
        }
    }

    internal sealed class JobSnapshot
    {
        internal string Id, Title, Source, Format, Detail, Warning, Error, ProcessingBackend, ProcessingNote;
        internal string[] Outputs;
        internal JobState State;
        internal bool IsImage;
        internal long Start, End;
        internal double? Fraction, RemainingSeconds;
        internal double ElapsedSeconds;
        internal bool Terminal { get { return MediaJob.IsTerminal(State); } }
    }

    internal sealed class MediaJob
    {
        internal readonly object Gate = new object();
        internal readonly JobRequest Request;
        internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
        internal readonly List<string> Outputs = new List<string>();
        internal readonly List<OutputReservation> Reservations = new List<OutputReservation>();
        internal readonly Stopwatch Clock = new Stopwatch();
        internal JobState State = JobState.Queued;
        internal bool Admitted;
        internal string Detail = String.Empty, Warning = String.Empty, Error = String.Empty;
        internal string ProcessingBackend = String.Empty, ProcessingNote = String.Empty;
        internal double? Fraction, Remaining;
        internal double RestoredElapsed;
        internal double ProcessCpuSeconds, ProcessWallSeconds, PeakMachineCpu;
        internal long PeakWorkingSet;
        internal Action Changed;

        internal MediaJob(JobRequest request) { Request = request; }
        internal JobState CurrentState { get { lock (Gate) return State; } }
        internal bool Terminal { get { lock (Gate) return IsTerminal(State); } }
        internal static bool IsTerminal(JobState state)
        {
            return state == JobState.Succeeded || state == JobState.Failed || state == JobState.Canceled || state == JobState.Interrupted;
        }
        internal void SetState(JobState state)
        {
            lock (Gate) { if (!IsTerminal(State) && State != JobState.Canceling) State = state; }
            Notify();
        }
        internal void Notify() { Action changed = Changed; if (changed != null) changed(); }
        internal void SetProcessing(string backend, string note)
        {
            lock (Gate) { ProcessingBackend = backend; ProcessingNote = note; }
            Notify();
        }
        internal void ConfirmHardwareProcessing()
        {
            lock (Gate)
                if (ProcessingBackend.EndsWith("-pending", StringComparison.Ordinal)) ProcessingBackend = ProcessingBackend.Substring(0, ProcessingBackend.Length - 8);
        }
        internal void Progress(long microseconds)
        {
            lock (Gate)
            {
                if (State == JobState.Canceling || IsTerminal(State) || Request.Duration <= 0) return;
                double fraction = Math.Max(0, Math.Min(.99, microseconds / (Request.Duration * 1000.0)));
                if (Fraction.HasValue && fraction < Fraction.Value) return;
                Fraction = fraction;
                if (Clock.Elapsed.TotalSeconds > 3 && fraction > .02)
                {
                    double estimated = Clock.Elapsed.TotalSeconds * (1 - fraction) / fraction;
                    Remaining = Remaining.HasValue ? Remaining.Value * .7 + estimated * .3 : estimated;
                }
            }
            Notify();
        }
        internal JobSnapshot Snapshot()
        {
            lock (Gate)
            {
                return new JobSnapshot {
                    Id = Request.Id, Source = Request.Source, Title = Path.GetFileNameWithoutExtension(Request.Source),
                    Format = Request.Format,
                    State = State, IsImage = Request.IsImage, Start = Request.Start, End = Request.End,
                    Fraction = State == JobState.Succeeded ? 1 : Fraction, RemainingSeconds = Remaining,
                    ElapsedSeconds = RestoredElapsed + Clock.Elapsed.TotalSeconds, Detail = Detail, Warning = Warning, Error = Error, Outputs = Outputs.ToArray(),
                    ProcessingBackend = ProcessingBackend, ProcessingNote = ProcessingNote
                };
            }
        }
        internal string Reserve(string directory, string stem, string extension)
        {
            Cancellation.Token.ThrowIfCancellationRequested();
            OutputReservation output = OutputReservation.Create(directory, stem, extension);
            lock (Gate) Reservations.Add(output);
            return output.StagingPath;
        }
        internal void CommitOutput(string staging)
        {
            lock (Gate)
            {
                Cancellation.Token.ThrowIfCancellationRequested();
                OutputReservation output = Reservations.FirstOrDefault(item => item.StagingPath == staging);
                if (output == null) return;
                output.Commit();
                Outputs.Add(output.FinalPath);
            }
        }
    }

    internal sealed class OutputReservation : IDisposable
    {
        internal string FinalPath, StagingPath;
        private FileStream reservation;
        private string lockPath;
        private bool committed;
        internal static OutputReservation Create(string directory, string stem, string extension)
        {
            Directory.CreateDirectory(directory);
            int limit = 230 - directory.Length - extension.Length;
            if (limit < 24) throw new PathTooLongException("Choose a shorter output folder.");
            for (int number = 1; number < 10000; number++)
            {
                string suffix = number == 1 ? "" : "_" + number.ToString(CultureInfo.InvariantCulture);
                string name = stem.Length > limit - suffix.Length ? stem.Substring(0, limit - suffix.Length) : stem;
                string candidate = Path.Combine(directory, name + suffix + extension);
                if (File.Exists(candidate)) continue;
                string marker = candidate + ".fc-lock";
                FileStream handle;
                try { handle = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
                catch (IOException error)
                {
                    int code = error.HResult & 0xffff;
                    // The competing owner may remove its marker before this catch executes.
                    if (code == 80 || code == 183 || code == 32 || File.Exists(marker)) continue;
                    throw;
                }
                catch (UnauthorizedAccessException)
                {
                    // A competing CreateNew/Dispose can expose a delete-pending NTFS marker.
                    if (File.Exists(marker) || File.Exists(candidate)) continue;
                    throw;
                }
                if (File.Exists(candidate)) { handle.Dispose(); File.Delete(marker); continue; }
                return new OutputReservation {
                    FinalPath = candidate, lockPath = marker, reservation = handle,
                    StagingPath = Path.Combine(directory, ".fc-" + Guid.NewGuid().ToString("N") + extension)
                };
            }
            throw new IOException("Too many outputs share this timestamp.");
        }
        internal void Commit()
        {
            FileInfo file = new FileInfo(StagingPath);
            if (!file.Exists || file.Length == 0) throw new IOException("The export produced no output.");
            File.Move(StagingPath, FinalPath);
            committed = true;
        }
        public void Dispose()
        {
            if (reservation == null) return;
            reservation.Dispose();
            reservation = null;
            try { File.Delete(lockPath); } catch { }
            if (!committed) { try { File.Delete(StagingPath); } catch { } }
        }
    }

    internal sealed class JobCoordinator : IDisposable
    {
        private readonly object gate = new object();
        private readonly List<MediaJob> jobs = new List<MediaJob>();
        private readonly AppConfig config;
        private readonly Action<MediaJob> execute;
        private readonly string storePath;
        private readonly List<Task> workers = new List<Task>();
        private readonly Timer admissionTimer;
        internal readonly WorkerResources Resources = new WorkerResources();
        internal string PersistenceWarning { get; private set; }
        private bool stopping, drain;
        internal event Action Changed;
        internal event Action Drained;

        internal JobCoordinator(AppConfig config, Action<MediaJob> execute, string storeDirectory)
        {
            this.config = config;
            this.execute = execute;
            storePath = Path.Combine(storeDirectory, "jobs.xml");
            admissionTimer = new Timer(delegate { Pump(); }, null, Timeout.Infinite, Timeout.Infinite);
            Load();
            Resources.Configure(config.ResourceMode, config.LowerBackgroundPriority);
        }
        internal bool Busy { get { lock (gate) return jobs.Any(j => !j.Terminal); } }
        internal bool TryBeginMaintenance()
        {
            lock (gate)
            {
                if (jobs.Any(j => !j.Terminal)) return false;
                drain = true;
                return true;
            }
        }
        internal JobSnapshot[] Snapshot() { lock (gate) return jobs.Select(j => j.Snapshot()).Reverse().ToArray(); }
        internal string Submit(JobRequest request)
        {
            lock (gate)
            {
                if (stopping || drain) throw new InvalidOperationException("FrameClip is shutting down.");
                if (jobs.Count(j => !j.Terminal) >= 100) throw new InvalidOperationException("Queue is full (100 jobs).");
                MediaJob job = new MediaJob(request);
                job.Changed = Signal;
                jobs.Add(job);
                Trim();
                Save();
            }
            Pump(); Signal();
            return request.Id;
        }
        internal void Retry(string id)
        {
            JobRequest request;
            lock (gate)
            {
                MediaJob job = jobs.First(j => j.Request.Id == id);
                if (!job.Terminal) return;
                request = job.Request;
            }
            Submit(new JobRequest(request.Action, request.Source, request.Start, request.End, request.Settings,
                request.Id, null, request.SourceLength, request.SourceModified));
        }
        internal void Cancel(string id)
        {
            lock (gate)
            {
                MediaJob job = jobs.FirstOrDefault(j => j.Request.Id == id);
                if (job == null) return;
                lock (job.Gate)
                {
                    if (MediaJob.IsTerminal(job.State)) return;
                    job.Cancellation.Cancel();
                    job.State = job.Admitted ? JobState.Canceling : JobState.Canceled;
                }
                Save();
            }
            Pump(); Signal();
        }
        internal void Reconfigure() { Resources.Configure(config.ResourceMode, config.LowerBackgroundPriority); Pump(); Signal(); }
        internal void DrainAndExit() { lock (gate) drain = true; Pump(); }
        internal void CancelAll()
        {
            string[] ids;
            lock (gate) { drain = true; ids = jobs.Where(j => !j.Terminal).Select(j => j.Request.Id).ToArray(); }
            foreach (string id in ids) Cancel(id);
            Pump();
        }
        private void Pump()
        {
            bool empty;
            lock (gate)
            {
                if (stopping) return;
                int videos = jobs.Count(j => j.Admitted && !j.Request.IsImage && !j.Terminal);
                int images = jobs.Count(j => j.Admitted && j.Request.IsImage && !j.Terminal);
                foreach (MediaJob job in jobs)
                {
                    if (job.CurrentState != JobState.Queued) continue;
                    if (job.Request.IsImage ? images >= 1 : videos >= config.MaxVideoJobs) continue;
                    if (!job.Request.IsImage && !WorkerResources.HasMemoryHeadroom())
                    { lock (job.Gate) job.Detail = "Waiting for available memory"; continue; }
                    job.Admitted = true;
                    lock (job.Gate) job.State = JobState.Preparing;
                    if (job.Request.IsImage) images++; else videos++;
                    MediaJob admitted = job;
                    workers.RemoveAll(t => t.IsCompleted);
                    workers.Add(Task.Run(delegate { Run(admitted); }));
                }
                empty = !jobs.Any(j => !j.Terminal);
                admissionTimer.Change(jobs.Any(j => j.CurrentState == JobState.Queued) ? 2000 : Timeout.Infinite, Timeout.Infinite);
            }
            if (empty && drain) { Action handler = Drained; if (handler != null) handler(); }
        }
        private void Run(MediaJob job)
        {
            job.Clock.Start();
            try
            {
                job.Cancellation.Token.ThrowIfCancellationRequested();
                job.Request.ValidateSource();
                execute(job);
                lock (job.Gate)
                {
                    if (job.Outputs.Count == 0) job.Cancellation.Token.ThrowIfCancellationRequested();
                    job.State = JobState.Succeeded;
                }
            }
            catch (OperationCanceledException)
            {
                lock (job.Gate)
                {
                    job.State = job.Outputs.Count > 0 ? JobState.Succeeded : JobState.Canceled;
                    if (job.Outputs.Count > 0) job.Warning = "Remaining output canceled; completed output retained.";
                }
            }
            catch (Exception error)
            {
                lock (job.Gate) { job.State = JobState.Failed; job.Error = error.Message; }
            }
            finally
            {
                job.Clock.Stop();
                foreach (OutputReservation output in job.Reservations) output.Dispose();
                lock (gate) { Trim(); Save(); }
                Pump(); Signal();
            }
        }
        private void Signal() { Action handler = Changed; if (handler != null) handler(); }
        private void Trim()
        {
            while (jobs.Count(j => j.Terminal) > 100)
            {
                MediaJob oldest = jobs.First(j => j.Terminal && !j.Clock.IsRunning);
                jobs.Remove(oldest); oldest.Cancellation.Dispose();
                try { File.Delete(Path.Combine(Path.GetDirectoryName(storePath), "logs", "job-" + oldest.Request.Id + ".log")); } catch { }
            }
        }
        private static string Number(long value) { return value.ToString(CultureInfo.InvariantCulture); }
        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(storePath));
                string temp = storePath + ".tmp";
                using (XmlWriter writer = XmlWriter.Create(temp, new XmlWriterSettings { Indent = true }))
                {
                    writer.WriteStartElement("jobs"); writer.WriteAttributeString("version", "1");
                    foreach (MediaJob job in jobs)
                    {
                        JobRequest r = job.Request;
                        JobSnapshot s = job.Snapshot();
                        writer.WriteStartElement("job");
                        writer.WriteAttributeString("id", r.Id);
                        writer.WriteAttributeString("action", Number((int)r.Action));
                        writer.WriteAttributeString("state", s.State.ToString());
                        writer.WriteAttributeString("source", r.Source);
                        writer.WriteAttributeString("start", Number(r.Start)); writer.WriteAttributeString("end", Number(r.End));
                        writer.WriteAttributeString("length", Number(r.SourceLength)); writer.WriteAttributeString("modified", Number(r.SourceModified));
                        writer.WriteAttributeString("parent", r.ParentId);
                        writer.WriteAttributeString("submitted", r.Submitted.ToString("o", CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("elapsed", s.ElapsedSeconds.ToString(CultureInfo.InvariantCulture));
                        writer.WriteElementString("error", s.Error); writer.WriteElementString("warning", s.Warning);
                        writer.WriteElementString("backend", s.ProcessingBackend); writer.WriteElementString("processingNote", s.ProcessingNote);
                        AppConfig saved = r.Settings;
                        foreach (System.Reflection.FieldInfo field in typeof(AppConfig).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
                            writer.WriteElementString(field.Name, Convert.ToString(field.GetValue(saved), CultureInfo.InvariantCulture));
                        foreach (string output in s.Outputs) writer.WriteElementString("output", output);
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
                if (File.Exists(storePath)) File.Replace(temp, storePath, null); else File.Move(temp, storePath);
                PersistenceWarning = null;
            }
            catch (Exception error) { PersistenceWarning = "Job history: " + error.Message; Trace.WriteLine(PersistenceWarning); }
        }
        private void Load()
        {
            if (!File.Exists(storePath)) return;
            try
            {
                XmlDocument doc = new XmlDocument(); doc.XmlResolver = null; doc.Load(storePath);
                if (doc.DocumentElement.GetAttribute("version") != "1") return;
                foreach (XmlElement node in doc.SelectNodes("/jobs/job"))
                {
                    try
                    {
                        AppConfig saved = config.Copy();
                        foreach (System.Reflection.FieldInfo field in typeof(AppConfig).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
                        {
                            XmlNode value = node.SelectSingleNode(field.Name);
                            if (value != null) field.SetValue(saved, Convert.ChangeType(value.InnerText, field.FieldType, CultureInfo.InvariantCulture));
                        }
                        saved.NormalizeJobs();
                        DateTime submitted;
                        if (!DateTime.TryParse(node.GetAttribute("submitted"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out submitted)) submitted = DateTime.UtcNow;
                        JobRequest r = new JobRequest((CaptureAction)Int32.Parse(node.GetAttribute("action")), node.GetAttribute("source"),
                            Int64.Parse(node.GetAttribute("start")), Int64.Parse(node.GetAttribute("end")), saved, node.GetAttribute("parent"),
                            node.GetAttribute("id"), Int64.Parse(node.GetAttribute("length")), Int64.Parse(node.GetAttribute("modified")), submitted);
                        JobState state;
                        if (!Enum.TryParse(node.GetAttribute("state"), out state)) state = JobState.Interrupted;
                        MediaJob job = new MediaJob(r) { State = MediaJob.IsTerminal(state) ? state : JobState.Interrupted, Changed = Signal };
                        double elapsed;
                        if (Double.TryParse(node.GetAttribute("elapsed"), NumberStyles.Float, CultureInfo.InvariantCulture, out elapsed) && elapsed >= 0 && !Double.IsInfinity(elapsed)) job.RestoredElapsed = elapsed;
                        job.Error = node.SelectSingleNode("error") == null ? "" : node.SelectSingleNode("error").InnerText;
                        job.Warning = node.SelectSingleNode("warning") == null ? "" : node.SelectSingleNode("warning").InnerText;
                        job.ProcessingBackend = node.SelectSingleNode("backend") == null ? "" : node.SelectSingleNode("backend").InnerText;
                        job.ProcessingNote = node.SelectSingleNode("processingNote") == null ? "" : node.SelectSingleNode("processingNote").InnerText;
                        foreach (XmlNode output in node.SelectNodes("output")) job.Outputs.Add(output.InnerText);
                        jobs.Add(job);
                    }
                    catch { }
                }
                Trim();
            }
            catch (Exception error) { Trace.WriteLine("Job history: " + error.Message); }
        }
        public void Dispose()
        {
            Task[] pending;
            lock (gate)
            {
                stopping = true;
                admissionTimer.Dispose();
                foreach (MediaJob job in jobs)
                {
                    if (job.Terminal) continue;
                    job.Cancellation.Cancel();
                    job.SetState(job.Admitted ? JobState.Canceling : JobState.Canceled);
                }
                pending = workers.ToArray();
            }
            try { Task.WaitAll(pending, 2000); } catch { }
            Resources.Dispose();
            try { Task.WaitAll(pending, 10000); } catch { }
            lock (gate) Save();
        }
    }
}
