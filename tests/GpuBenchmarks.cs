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
    internal static class GpuBenchmarkProgram
    {
        private sealed class Sample
        {
            internal string Fixture, Backend;
            internal double Wall, Cpu, ImageLatency, PeakCpu;
            internal long Memory;
            internal bool Equal;
        }
        public static int Main(string[] args)
        {
            string ffmpeg = args[0], evidence = Path.GetFullPath(args[1]);
            string root = Path.Combine(Path.GetTempPath(), "FrameClipGpuBenchmark-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); Directory.CreateDirectory(evidence);
            Environment.SetEnvironmentVariable("FRAMECLIP_DATA_DIRECTORY", Path.Combine(root, "data"));
            List<Sample> samples = new List<Sample>();
            try
            {
                foreach (bool uhd in new[] { false, true })
                {
                    int width = uhd ? 3840 : 1920, height = uhd ? 2160 : 1080;
                    string fixture = uhd ? "UHD-HEVC10-PQ" : "HD-H264-SDR";
                    string source = Path.Combine(root, fixture + ".mp4");
                    string primaries = uhd ? "bt2020" : "bt709", transfer = uhd ? "smpte2084" : "bt709", matrix = uhd ? "bt2020nc" : "bt709";
                    string color = "setparams=range=limited:color_primaries=" + primaries + ":color_trc=" + transfer + ":colorspace=" + matrix;
                    Raw(ffmpeg, "-hide_banner -loglevel error -y -f lavfi -i testsrc2=size=" + width + "x" + height + ":rate=24 -t 4 -vf \"format=" + (uhd ? "yuv420p10le" : "yuv420p") + "," + color + "\" " +
                        (uhd ? "-c:v libx265 -preset ultrafast -x265-params pools=4:frame-threads=2:log-level=error:colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc" : "-c:v libx264 -preset fast -crf 16 -threads 4") + " \"" + source + "\"");
                    VideoInfo info = new VideoInfo { Width = width, Height = height, Codec = uhd ? "hevc" : "h264", PixelFormat = uhd ? "yuv420p10le" : "yuv420p" };
                    string baselineVideo = null, baselineImage = null;
                    for (int repeat = 0; repeat < 2; repeat++)
                    foreach (string backend in repeat == 0 ? new[] { "cpu", "cuda", "d3d11va" } : new[] { "d3d11va", "cuda", "cpu" })
                    {
                        string folder = Path.Combine(root, fixture + "-" + backend + "-" + repeat); Directory.CreateDirectory(folder);
                        AppConfig config = new AppConfig { FfmpegPath = ffmpeg, FfprobePath = Path.Combine(Path.GetDirectoryName(ffmpeg), "ffprobe.exe") };
                        WorkerResources resources = null;
                        Sample sample = new Sample { Fixture = fixture, Backend = backend };
                        using (JobCoordinator jobs = new JobCoordinator(config, delegate(MediaJob job) {
                            IDisposable lease = null;
                            try
                            {
                                if (backend != "cpu" && !job.Request.IsImage) lease = resources.GpuVideos.Acquire(info, job.Cancellation.Token, delegate { });
                                string output = job.Reserve(folder, job.Request.IsImage ? "image" : "clip", job.Request.IsImage ? ".png" : ".mov");
                                string arguments = "-hide_banner -loglevel error -y -ss 0.5 -i \"" + source + "\" " +
                                    (job.Request.IsImage ? "-frames:v 1 -map 0:v:0 -vf \"scale=in_range=limited:out_range=full:in_color_matrix=" + (uhd ? "bt2020" : matrix) + ",format=rgb48be,setparams=range=full:color_primaries=" + primaries + ":color_trc=" + transfer + ":colorspace=gbr\" -c:v png -pred mixed -update 1" :
                                    "-t 3 -map 0:v:0 -vf \"format=yuv422p10le," + color + "\" -c:v prores_ks -profile:v 3 -vendor apl0 -pix_fmt yuv422p10le -color_primaries " + primaries + " -color_trc " + transfer + " -colorspace " + matrix + " -movflags +write_colr") + " \"" + output + "\"";
                                if (backend != "cpu") arguments = HardwareDecodePlan.Create(backend, info).Apply(arguments);
                                FfmpegRunner.Run(ffmpeg, arguments, "GPU benchmark", 120000, job, resources);
                            }
                            finally { if (lease != null) lease.Dispose(); }
                        }, Path.Combine(folder, "state")))
                        {
                            resources = jobs.Resources; Stopwatch watch = Stopwatch.StartNew();
                            jobs.Submit(new JobRequest(CaptureAction.ExportPrecise, source, 500, 3500, config));
                            jobs.Submit(new JobRequest(CaptureAction.ExportPrecise, source, 500, 3500, config));
                            string image = jobs.Submit(new JobRequest(CaptureAction.CaptureFrame, source, 500, 500, config));
                            double previousCpu = 0, previousWall = 0;
                            do
                            {
                                Thread.Sleep(50); double cpu; long memory; resources.Usage(out cpu, out memory);
                                double wall = watch.Elapsed.TotalSeconds; sample.Memory = Math.Max(sample.Memory, memory); sample.Cpu = cpu;
                                if (wall - previousWall >= .45)
                                { sample.PeakCpu = Math.Max(sample.PeakCpu, (cpu - previousCpu) / (wall - previousWall) / Environment.ProcessorCount * 100); previousCpu = cpu; previousWall = wall; }
                                if (sample.ImageLatency == 0 && jobs.Snapshot().Single(j => j.Id == image).Terminal) sample.ImageLatency = wall;
                                if (wall > 120) throw new TimeoutException("GPU benchmark timed out.");
                            } while (jobs.Busy);
                            sample.Wall = watch.Elapsed.TotalSeconds;
                            JobSnapshot[] result = jobs.Snapshot();
                            if (result.Any(j => j.State != JobState.Succeeded)) throw new IOException(String.Join("; ", result.Select(j => j.Error)));
                            string videoHash = Raw(ffmpeg, "-hide_banner -loglevel error -i \"" + result.First(j => !j.IsImage).Outputs[0] + "\" -frames:v 24 -f framemd5 -");
                            string imageHash = Raw(ffmpeg, "-hide_banner -loglevel error -i \"" + result.Single(j => j.IsImage).Outputs[0] + "\" -f framemd5 -");
                            if (baselineVideo == null) { baselineVideo = videoHash; baselineImage = imageHash; }
                            sample.Equal = videoHash == baselineVideo && imageHash == baselineImage;
                            foreach (string output in result.SelectMany(j => j.Outputs)) File.Delete(output);
                        }
                        samples.Add(sample);
                        Console.WriteLine(fixture + " " + backend + " repeat=" + repeat + " wall=" + sample.Wall.ToString("0.00") + "s cpu=" + sample.Cpu.ToString("0.00") + "s image=" + sample.ImageLatency.ToString("0.00") + "s memory=" + (sample.Memory / 1048576.0).ToString("0.0") + "MiB equal=" + sample.Equal);
                    }
                }
                using (XmlWriter writer = XmlWriter.Create(Path.Combine(evidence, "gpu-benchmark.xml"), new XmlWriterSettings { Indent = true }))
                {
                    writer.WriteStartElement("gpuBenchmark"); writer.WriteAttributeString("utc", DateTime.UtcNow.ToString("o"));
                    writer.WriteAttributeString("policy", "playback-first shared 35% CPU; 2 video slots + independent image; software color and encoding");
                    foreach (Sample s in samples)
                    {
                        writer.WriteStartElement("sample"); writer.WriteAttributeString("fixture", s.Fixture); writer.WriteAttributeString("backend", s.Backend);
                        writer.WriteAttributeString("wallSeconds", s.Wall.ToString("R", CultureInfo.InvariantCulture)); writer.WriteAttributeString("cpuSeconds", s.Cpu.ToString("R", CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("imageLatencySeconds", s.ImageLatency.ToString("R", CultureInfo.InvariantCulture)); writer.WriteAttributeString("peakMachineCpu", s.PeakCpu.ToString("R", CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("peakWorkingSet", s.Memory.ToString(CultureInfo.InvariantCulture)); writer.WriteAttributeString("equalPixels", s.Equal.ToString()); writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
                return samples.All(s => s.Equal) ? 0 : 2;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { try { Directory.Delete(root, true); } catch { } }
        }
        private static string Raw(string executable, string arguments)
        {
            using (Process process = Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                try
                {
                    Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(120000)) throw new TimeoutException("GPU benchmark command timed out.");
                    Task.WaitAll(output, error); if (process.ExitCode != 0) throw new IOException(error.Result); return output.Result;
                }
                finally { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } }
            }
        }
    }
}
