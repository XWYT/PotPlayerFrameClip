using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace PotPlayerFrameClip
{
    internal static class JobBenchmarkProgram
    {
        private sealed class Measurement
        {
            internal string Fixture, Mode, Metadata;
            internal double Wall, Cpu, Peak;
            internal long Memory;
            internal bool EqualPixels;
        }
        [STAThread] public static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            string ffmpeg = args[0], evidence = Path.GetFullPath(args[1]);
            string scratch = Path.Combine(Path.GetTempPath(), "FrameClipBenchmark-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch); Directory.CreateDirectory(evidence);
            Environment.SetEnvironmentVariable("FRAMECLIP_DATA_DIRECTORY", Path.Combine(scratch, "data"));
            try
            {
                List<Measurement> records = new List<Measurement>();
                foreach (bool hdr in new[] { false, true })
                {
                    string tags = hdr ? "-color_primaries bt2020 -color_trc smpte2084 -colorspace bt2020nc" : "-color_primaries bt709 -color_trc bt709 -colorspace bt709";
                    string source = Path.Combine(scratch, hdr ? "HDR.mkv" : "SDR.mkv");
                    Raw(ffmpeg, "-hide_banner -loglevel error -y -f lavfi -i testsrc2=size=1280x720:rate=24 -f lavfi -i sine=frequency=440:sample_rate=48000 -t 10 -vf \"" + ColorFilter(hdr) + "\" -c:v ffv1 -pix_fmt yuv422p10le " + tags + " -color_range tv -c:a pcm_s24le \"" + source + "\"", null);
                    string baselineHash = null, baselineMetadata = null;
                    foreach (string mode in new[] { "baseline", "quiet", "balanced", "fast" })
                    {
                        string folder = Path.Combine(scratch, (hdr ? "hdr-" : "sdr-") + mode); Directory.CreateDirectory(folder);
                        AppConfig config = new AppConfig { FfmpegPath = ffmpeg, FfprobePath = Path.Combine(Path.GetDirectoryName(ffmpeg), "ffprobe.exe"), ResourceMode = mode == "baseline" ? "fast" : mode, LowerBackgroundPriority = mode != "baseline" && mode != "fast" };
                        Measurement m = new Measurement { Fixture = hdr ? "synthetic PQ tags" : "synthetic SDR", Mode = mode };
                        List<string> outputs = new List<string>();
                        WorkerResources jobCoordinatorResources = null;
                        using (JobCoordinator jobs = new JobCoordinator(config, delegate(MediaJob job) {
                            string staging = job.Reserve(folder, "ProRes-HQ", ".mov");
                            FfmpegRunner.Run(ffmpeg, Arguments(source, staging, tags, hdr), "precise benchmark", 120000, job, jobCoordinatorResources);
                        }, Path.Combine(folder, "state")))
                        {
                            jobCoordinatorResources = jobs.Resources;
                            Task baseline = null;
                            if (mode == "baseline") baseline = Task.Run(delegate {
                                for (int i = 0; i < 2; i++) { string output = Path.Combine(folder, i + ".mov"); Raw(ffmpeg, Arguments(source, output, tags, hdr), jobs.Resources); outputs.Add(output); }
                            });
                            else for (int i = 0; i < 2; i++) jobs.Submit(new JobRequest(CaptureAction.ExportPrecise, source, 0, 8000, config));
                            Measure(jobs.Resources, mode == "baseline" ? (Func<bool>)(() => baseline.IsCompleted) : (() => !jobs.Busy), m);
                            if (baseline != null) baseline.Wait();
                            else
                            {
                                JobSnapshot[] states = jobs.Snapshot();
                                if (states.Any(s => s.State != JobState.Succeeded)) throw new IOException(String.Join(";", states.Select(s => s.Error)));
                                outputs.AddRange(states.SelectMany(s => s.Outputs));
                            }
                        }
                        string metadata = Raw(config.FfprobePath, "-v error -show_entries stream=codec_name,profile,pix_fmt,color_range,color_space,color_transfer,color_primaries,sample_rate -of compact \"" + outputs[0] + "\"", null);
                        if (!metadata.Contains("prores") || !metadata.Contains("yuv422p10le") || !metadata.Contains("pcm_s24le") ||
                            !metadata.Contains("color_transfer=" + (hdr ? "smpte2084" : "bt709")) ||
                            !metadata.Contains("color_primaries=" + (hdr ? "bt2020" : "bt709")) ||
                            !metadata.Contains("color_space=" + (hdr ? "bt2020nc" : "bt709")))
                            throw new IOException("Output fidelity tags differ (" + m.Fixture + ", " + mode + "): " + metadata);
                        if (baselineMetadata == null) baselineMetadata = metadata;
                        else if (metadata != baselineMetadata) throw new IOException("Output metadata differs from the uncapped baseline: " + metadata);
                        m.Metadata = metadata.Trim();
                        string hash = Raw(ffmpeg, "-hide_banner -loglevel error -i \"" + outputs[0] + "\" -map 0:v:0 -frames:v 24 -f framemd5 -", null);
                        if (baselineHash == null) baselineHash = hash;
                        m.EqualPixels = hash == baselineHash;
                        records.Add(m); Console.WriteLine(m.Fixture + " " + mode + ": " + m.Wall.ToString("0.00") + "s, " + m.Cpu.ToString("0.00") + " CPU-s, peak " + m.Peak.ToString("0.0") + "%, " + (m.Memory / 1048576.0).ToString("0.0") + " MiB, pixels=" + m.EqualPixels);
                        foreach (string output in outputs) File.Delete(output);
                    }
                }
                double[] idle = Idle();
                Save(evidence, records, idle);
                return records.All(r => r.EqualPixels) ? 0 : 2;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { try { Directory.Delete(scratch, true); } catch { } }
        }
        private static string ColorFilter(bool hdr)
        {
            return "format=yuv422p10le,setparams=range=limited:color_primaries=" + (hdr ? "bt2020" : "bt709") +
                ":color_trc=" + (hdr ? "smpte2084" : "bt709") + ":colorspace=" + (hdr ? "bt2020nc" : "bt709");
        }
        private static string Arguments(string source, string output, string tags, bool hdr)
        {
            return "-hide_banner -loglevel error -y -ss 0 -i \"" + source + "\" -t 8 -map 0:v:0 -map 0:a? -map_metadata 0 -vf \"" + ColorFilter(hdr) + "\" -c:v prores_ks -profile:v 3 -vendor apl0 -pix_fmt yuv422p10le " + tags + " -color_range tv -c:a pcm_s24le -ar 48000 -movflags +write_colr \"" + output + "\"";
        }
        private static void Measure(WorkerResources resources, Func<bool> done, Measurement result)
        {
            Stopwatch watch = Stopwatch.StartNew(); double previousCpu = 0, previousWall = 0;
            do
            {
                Thread.Sleep(100); double cpu; long memory; resources.Usage(out cpu, out memory);
                double wall = watch.Elapsed.TotalSeconds;
                if (wall - previousWall >= .45)
                {
                    result.Peak = Math.Max(result.Peak, (cpu - previousCpu) / (wall - previousWall) / Environment.ProcessorCount * 100);
                    previousCpu = cpu; previousWall = wall;
                }
                result.Memory = Math.Max(result.Memory, memory); result.Cpu = cpu;
                if (wall > 120) throw new TimeoutException("Benchmark timeout.");
            } while (!done());
            result.Wall = watch.Elapsed.TotalSeconds;
        }
        private static string Raw(string executable, string arguments, WorkerResources resources)
        {
            using (Process process = Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                try
                {
                    if (resources != null) resources.Attach(process, false);
                    Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(120000)) throw new TimeoutException();
                    Task.WaitAll(output, error); if (process.ExitCode != 0) throw new IOException(error.Result); return output.Result;
                }
                finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } if (resources != null) resources.Detach(process); }
            }
        }
        private static double[] Idle()
        {
            using (Control dispatcher = new Control())
            {
                dispatcher.CreateControl();
                using (CaptureEngine engine = new CaptureEngine(new AppConfig(), dispatcher))
                using (Process process = Process.GetCurrentProcess())
                {
                    Stopwatch settle = Stopwatch.StartNew(); while (settle.ElapsedMilliseconds < 1000) { Application.DoEvents(); Thread.Sleep(25); }
                    double cpu = process.TotalProcessorTime.TotalSeconds; Stopwatch watch = Stopwatch.StartNew();
                    while (watch.ElapsedMilliseconds < 3000) { Application.DoEvents(); Thread.Sleep(50); }
                    process.Refresh(); return new[] { process.TotalProcessorTime.TotalSeconds - cpu, process.WorkingSet64 / 1048576.0, process.PrivateMemorySize64 / 1048576.0 };
                }
            }
        }
        private static void Save(string folder, List<Measurement> records, double[] idle)
        {
            using (XmlWriter writer = XmlWriter.Create(Path.Combine(folder, "benchmark.xml"), new XmlWriterSettings { Indent = true }))
            {
                writer.WriteStartElement("benchmark"); writer.WriteAttributeString("logicalProcessors", Environment.ProcessorCount.ToString());
                foreach (Measurement r in records)
                {
                    writer.WriteStartElement("case"); writer.WriteAttributeString("fixture", r.Fixture); writer.WriteAttributeString("mode", r.Mode);
                    writer.WriteAttributeString("wallSeconds", r.Wall.ToString("R", CultureInfo.InvariantCulture)); writer.WriteAttributeString("cpuSeconds", r.Cpu.ToString("R", CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("peakCpuPercent", r.Peak.ToString("R", CultureInfo.InvariantCulture)); writer.WriteAttributeString("peakWorkingSetBytes", r.Memory.ToString());
                    writer.WriteAttributeString("equalFirst24DecodedFrames", r.EqualPixels.ToString()); writer.WriteElementString("metadata", r.Metadata); writer.WriteEndElement();
                }
                writer.WriteStartElement("managedUiShellAfterBenchmarks"); writer.WriteAttributeString("cpuSecondsOver3Seconds", idle[0].ToString("R", CultureInfo.InvariantCulture));
                writer.WriteAttributeString("workingSetMiB", idle[1].ToString("R", CultureInfo.InvariantCulture)); writer.WriteAttributeString("privateMiB", idle[2].ToString("R", CultureInfo.InvariantCulture)); writer.WriteEndElement(); writer.WriteEndElement();
            }
        }
    }
}
