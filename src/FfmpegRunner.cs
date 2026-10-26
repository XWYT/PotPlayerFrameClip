using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PotPlayerFrameClip
{
    internal sealed class WorkerResources : IDisposable
    {
        private readonly object gate = new object();
        private IntPtr videos, images;
        private string mode = "quiet";
        private bool capped;
        private bool lowerPriority = true;
        private bool disposed;
        private readonly Dictionary<Process, bool> processes = new Dictionary<Process, bool>();
        private readonly Dictionary<string, string> probes = new Dictionary<string, string>();
        private double completedCpu;
        internal readonly GpuAdmission GpuVideos = new GpuAdmission();
        internal readonly HardwareFailureCache HardwareFailures = new HardwareFailureCache();
        private readonly HardwareCapabilityCache hardwareCapabilities = new HardwareCapabilityCache();
        internal HardwareDecodePlan[] HardwarePlans(string executable, VideoInfo info, CancellationToken token, Func<string, string> probe)
        {
            return hardwareCapabilities.Get(executable, token, probe).Plans(info, GpuAdapter.All,
                File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvcuda.dll")));
        }
        internal string Mode { get { lock (gate) return mode; } }
        internal bool Capped { get { lock (gate) return capped; } }
        internal WorkerResources() { videos = CreateGroup(); images = CreateGroup(); }
        internal void Configure(string value, bool lower)
        {
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException("WorkerResources");
                mode = value;
                lowerPriority = lower;
                CpuLimit cpu = new CpuLimit();
                if (value != "fast") { cpu.Flags = 1 | 4; cpu.Rate = value == "balanced" ? 6000u : 3500u; }
                bool applied = videos != IntPtr.Zero && SetCpu(videos, 15, ref cpu, (uint)Marshal.SizeOf(typeof(CpuLimit)));
                capped = applied && value != "fast";
                foreach (KeyValuePair<Process, bool> pair in processes)
                    try { if (!pair.Value && !pair.Key.HasExited) pair.Key.PriorityClass = lowerPriority ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal; } catch { }
            }
        }
        internal bool Attach(Process process, bool image)
        {
            lock (gate)
            {
                if (disposed) { try { process.Kill(); } catch { } throw new ObjectDisposedException("WorkerResources"); }
                bool assigned = AssignProcessToJobObject(image ? images : videos, process.Handle);
                if (!assigned && !image) capped = false;
                processes.Add(process, image);
                try { process.PriorityClass = image || !lowerPriority ? ProcessPriorityClass.Normal : ProcessPriorityClass.BelowNormal; }
                catch { }
                return assigned;
            }
        }
        internal void Detach(Process process)
        {
            lock (gate)
            {
                if (!processes.Remove(process)) return;
                try { completedCpu += process.TotalProcessorTime.TotalSeconds; } catch { }
            }
        }
        internal void Usage(out double cpuSeconds, out long workingSet)
        {
            lock (gate)
            {
                cpuSeconds = completedCpu; workingSet = 0;
                foreach (Process process in processes.Keys)
                    try { process.Refresh(); cpuSeconds += process.TotalProcessorTime.TotalSeconds; if (!process.HasExited) workingSet += process.WorkingSet64; } catch { }
            }
        }
        internal string ProbeKey(string executable, string arguments, JobRequest request)
        {
            FileInfo tool = new FileInfo(executable);
            return executable + "|" + tool.Length + "|" + tool.LastWriteTimeUtc.Ticks + "|" +
                request.SourceLength + "|" + request.SourceModified + "|" + arguments;
        }
        internal string CachedProbe(string key) { lock (gate) { string value; return probes.TryGetValue(key, out value) ? value : null; } }
        internal void CacheProbe(string key, string value) { lock (gate) { if (probes.Count >= 64) probes.Clear(); probes[key] = value; } }
        internal int Threads(bool image)
        {
            if (image || Mode == "quiet") return 2;
            return Mode == "balanced" ? 4 : Math.Max(2, Math.Min(12, Environment.ProcessorCount / 2));
        }
        private static IntPtr CreateGroup()
        {
            IntPtr job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return job;
            ExtendedLimit limit = new ExtendedLimit(); limit.Basic.Flags = 0x2000;
            if (!SetLimits(job, 9, ref limit, (uint)Marshal.SizeOf(typeof(ExtendedLimit)))) { CloseHandle(job); return IntPtr.Zero; }
            return job;
        }
        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                GpuVideos.Dispose();
                foreach (Process process in processes.Keys) { try { if (!process.HasExited) process.Kill(); } catch { } }
                if (videos != IntPtr.Zero) CloseHandle(videos);
                if (images != IntPtr.Zero) CloseHandle(images);
                videos = images = IntPtr.Zero;
            }
        }
        internal static bool HasMemoryHeadroom()
        {
            MemoryStatus status = new MemoryStatus(); status.Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            return !GlobalMemoryStatusEx(ref status) || status.AvailablePhysical >= 768UL * 1024 * 1024;
        }
        [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
        {
            internal uint Length, Load;
            internal ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended;
        }
        [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [StructLayout(LayoutKind.Sequential)] private struct CpuLimit { internal uint Flags, Rate; }
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimit
        {
            internal long ProcessTime, JobTime; internal uint Flags;
            internal UIntPtr MinimumWorkingSet, MaximumWorkingSet;
            internal uint ActiveProcesses; internal UIntPtr Affinity; internal uint Priority, Scheduling;
        }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters { internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimit
        {
            internal BasicLimit Basic; internal IoCounters Io;
            internal UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", EntryPoint = "SetInformationJobObject")] private static extern bool SetCpu(IntPtr job, int kind, ref CpuLimit data, uint size);
        [DllImport("kernel32.dll", EntryPoint = "SetInformationJobObject")] private static extern bool SetLimits(IntPtr job, int kind, ref ExtendedLimit data, uint size);
        [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }

    internal sealed class ProgressParser
    {
        private long? time;
        private readonly Action<long> report;
        internal ProgressParser(Action<long> report) { this.report = report; }
        internal void Read(string line)
        {
            int separator = line.IndexOf('=');
            if (separator < 1) return;
            string key = line.Substring(0, separator), value = line.Substring(separator + 1);
            long parsed;
            if (key == "out_time_us" && Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed >= 0) time = parsed;
            if (key == "out_time" && !time.HasValue)
            {
                TimeSpan span;
                if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out span) && span.Ticks >= 0) time = span.Ticks / 10;
            }
            if (key == "progress") { if (time.HasValue) report(time.Value); time = null; }
        }
    }

    internal static class FfmpegRunner
    {
        internal static string Run(string executable, string arguments, string operation, int timeout, MediaJob job, WorkerResources resources)
        {
            bool probe = operation.IndexOf("probe", StringComparison.OrdinalIgnoreCase) >= 0;
            CancellationToken token = job.Cancellation.Token;
            token.ThrowIfCancellationRequested();
            string probeKey = probe && operation != "validation probe" ? resources.ProbeKey(executable, arguments, job.Request) : null;
            string cached = probeKey == null ? null : resources.CachedProbe(probeKey);
            if (cached != null) return cached;
            string staging = null;
            lock (job.Gate)
                foreach (OutputReservation reservation in job.Reservations)
                    if (arguments.EndsWith("\"" + reservation.StagingPath + "\"", StringComparison.Ordinal)) staging = reservation.StagingPath;
            if (!probe)
            {
                int threads = resources.Threads(job.Request.IsImage);
                arguments = "-progress pipe:1 -nostats -stats_period 0.5 -filter_threads 1 -filter_complex_threads 1 " + arguments;
                int input = FfmpegArguments.FindOption(arguments, "-i");
                if (input >= 0) arguments = arguments.Insert(input, "-threads " + threads + " ");
                int output = arguments.LastIndexOf(" \"", StringComparison.Ordinal);
                if (output >= 0) arguments = arguments.Insert(output, " -threads:v " + threads);
                job.SetState(JobState.Running);
            }
            ProcessStartInfo start = new ProcessStartInfo(executable, arguments) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                RedirectStandardInput = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            StringBuilder stdout = new StringBuilder(), stderr = new StringBuilder();
            string logDirectory = Path.Combine(AppPaths.DataDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            string logPath = Path.Combine(logDirectory, "job-" + job.Request.Id + ".log");
            using (StreamWriter log = new StreamWriter(logPath, true, new UTF8Encoding(false)))
            using (Process process = Process.Start(start))
            {
                if (process == null) throw new IOException("Unable to start " + operation);
                Task stdoutTask = null, stderrTask = null;
                Stopwatch processWatch = Stopwatch.StartNew();
                double previousCpu = 0, previousWall = 0, peakCpu = 0;
                long peakMemory = 0;
                try
                {
                bool assigned = resources.Attach(process, job.Request.IsImage);
                log.WriteLine(DateTime.UtcNow.ToString("o") + " " + operation + " pid=" + process.Id + " group=" + assigned + " cap=" + resources.Capped);
                JobSnapshot current = job.Snapshot();
                log.WriteLine("processing=" + current.ProcessingBackend + " " + current.ProcessingNote);
                log.WriteLine(executable + " " + arguments);
                log.Flush();
                bool hadProgress = false;
                ProgressParser parser = new ProgressParser(delegate(long time) { if (time > 0) { hadProgress = true; job.ConfirmHardwareProcessing(); } job.Progress(time); });
                stdoutTask = Task.Run(delegate {
                    string line;
                    while ((line = process.StandardOutput.ReadLine()) != null)
                    {
                        if (probe) { if (stdout.Length < 1024 * 1024) stdout.AppendLine(line); }
                        else parser.Read(line);
                    }
                });
                stderrTask = Task.Run(delegate {
                    string line;
                    while ((line = process.StandardError.ReadLine()) != null)
                    {
                        stderr.AppendLine(line);
                        if (stderr.Length > 16384) stderr.Remove(0, stderr.Length - 8192);
                        if (log.BaseStream.Position < 2 * 1024 * 1024) log.WriteLine(line);
                    }
                });
                Stopwatch elapsed = Stopwatch.StartNew();
                bool canceled = false, timedOut = false;
                while (!process.WaitForExit(100))
                {
                    try
                    {
                        double wall = processWatch.Elapsed.TotalSeconds;
                        if (wall - previousWall >= .45)
                        {
                            process.Refresh(); double cpu = process.TotalProcessorTime.TotalSeconds;
                            peakCpu = Math.Max(peakCpu, (cpu - previousCpu) / (wall - previousWall) / Environment.ProcessorCount * 100);
                            peakMemory = Math.Max(peakMemory, process.PeakWorkingSet64); previousCpu = cpu; previousWall = wall;
                        }
                    }
                    catch (InvalidOperationException) { }
                    if (token.IsCancellationRequested || elapsed.ElapsedMilliseconds > timeout)
                    {
                        canceled = token.IsCancellationRequested; timedOut = !canceled;
                        try { process.StandardInput.WriteLine("q"); process.StandardInput.Flush(); } catch { }
                        if (!process.WaitForExit(500)) { try { process.Kill(); } catch { } }
                        process.WaitForExit(5000);
                        break;
                    }
                }
                if (!Task.WaitAll(new[] { stdoutTask, stderrTask }, 5000)) throw new IOException("Process output did not close.");
                if (canceled) throw new OperationCanceledException(token);
                if (timedOut) throw new TimeoutException(operation + " timed out.");
                token.ThrowIfCancellationRequested();
                if (process.ExitCode != 0) throw new FfmpegProcessException(operation, stderr.ToString(), hadProgress);
                }
                finally
                {
                    try { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } } catch { }
                    try { Task.WaitAll(new[] { stdoutTask, stderrTask }.Where(t => t != null).ToArray(), 5000); } catch { }
                    try
                    {
                        double cpu = process.TotalProcessorTime.TotalSeconds;
                        lock (job.Gate) { job.ProcessCpuSeconds += cpu; job.ProcessWallSeconds += processWatch.Elapsed.TotalSeconds; job.PeakMachineCpu = Math.Max(job.PeakMachineCpu, peakCpu); job.PeakWorkingSet = Math.Max(job.PeakWorkingSet, peakMemory); }
                        log.WriteLine(String.Format(CultureInfo.InvariantCulture, "metrics cpu_seconds={0:0.000} wall_seconds={1:0.000} peak_machine_cpu={2:0.00} peak_working_set={3}", cpu, processWatch.Elapsed.TotalSeconds, peakCpu, peakMemory));
                    }
                    catch { }
                    resources.Detach(process);
                }
            }
            if (!probe && staging != null)
            {
                job.SetState(JobState.Finalizing);
                ValidateOutput(staging, job, resources);
                job.CommitOutput(staging);
            }
            if (probeKey != null) resources.CacheProbe(probeKey, stdout.ToString());
            return stdout.ToString();
        }
        private static void ValidateOutput(string path, MediaJob job, WorkerResources resources)
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new IOException("Empty output.");
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tif", StringComparison.OrdinalIgnoreCase)) return;
            string output = Run(job.Request.Settings.FfprobePath,
                "-v error -select_streams v:0 -show_entries stream=codec_type -of csv=p=0 \"" + path + "\"",
                "validation probe", 15000, job, resources);
            if (output.IndexOf("video", StringComparison.Ordinal) < 0) throw new IOException("Output container validation failed.");
        }
    }
}
