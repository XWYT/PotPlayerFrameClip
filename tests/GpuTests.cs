using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PotPlayerFrameClip
{
    internal static class GpuTests
    {
        internal static void Contracts(string root, string source, Action<bool, string> check)
        {
            AppConfig config = new AppConfig { LibraryRootDirectory = Path.Combine(root, "gpu-output") };
            check(config.ProcessingDevice == "gpu", "GPU preference is not the default");
            config.ProcessingDevice = "invalid"; config.NormalizeJobs(); check(config.ProcessingDevice == "gpu", "invalid preference did not normalize");
            config.ProcessingDevice = "cpu"; config.Save(); check(AppConfig.Load().ProcessingDevice == "cpu", "CPU preference did not persist");
            config.ProcessingDevice = "gpu";
            using (SettingsView view = new SettingsView(config))
            {
                ChoiceBox device = Descendants(view).OfType<ChoiceBox>().Single(c => c.AccessibleName == "视频与截图处理");
                check(device.Value == "gpu" && !view.HasChanges, "GPU UI default is not clean");
                typeof(ChoiceBox).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(device, new object[] { new KeyEventArgs(Keys.Down) });
                check(device.Value == "cpu" && view.HasChanges, "GPU selector keyboard navigation did not update dirty state");
                check(view.Save() && config.ProcessingDevice == "cpu" && !view.HasChanges, "GPU selector save failed");
            }
            using (SettingsView view = new SettingsView(AppConfig.Load()))
                check(Descendants(view).OfType<ChoiceBox>().Single(c => c.AccessibleName == "视频与截图处理").Value == "cpu", "GPU selector reopen lost CPU setting");
            config.ProcessingDevice = "gpu";
            JobRequest request = new JobRequest(CaptureAction.CaptureFrame, source, 0, 0, config);
            config.ProcessingDevice = "cpu"; check(request.Settings.ProcessingDevice == "gpu", "accepted GPU preference was mutable");
            MediaJob projection = new MediaJob(request); projection.SetProcessing("cuda", "test");
            check(projection.Snapshot().ProcessingBackend == "cuda" && projection.Snapshot().ProcessingNote == "test", "processing projection was lost");
            check(JobText.Processing(projection.Snapshot(), UiText.English) == "GPU · CUDA", "GPU display is not truthful");
            projection.SetProcessing("cuda-pending", ""); check(JobText.Processing(projection.Snapshot(), UiText.English) == "Trying GPU", "pending hardware attempt claims success");
            projection.ConfirmHardwareProcessing(); check(projection.Snapshot().ProcessingBackend == "cuda", "running hardware backend was not confirmed");
            VideoInfo sdr = new VideoInfo { Codec = "h264", PixelFormat = "yuv420p", Width = 1920, Height = 1080 };
            VideoInfo hdr = new VideoInfo { Codec = "hevc", PixelFormat = "yuv420p10le", Width = 3840, Height = 2160 };
            check(HardwareDecodePlan.Supports(sdr) && HardwareDecodePlan.Supports(hdr), "common hardware formats rejected");
            check(!HardwareDecodePlan.Supports(new VideoInfo { Codec = "h264", PixelFormat = "yuv422p10le", Width = 1920, Height = 1080 }), "unsupported H264 high bit-depth admitted");
            sdr.IsDolbyVision = true; check(!HardwareDecodePlan.Supports(sdr), "Dolby Vision reference safety bypassed"); sdr.IsDolbyVision = false;
            string arguments = "-ss 1 -i \"source.mp4\" -frames:v 1 -vf \"scale=in_range=limited,format=rgb48be\" -c:v png \"output.png\"";
            string cuda = HardwareDecodePlan.Create("cuda", sdr).Apply(arguments);
            check(cuda.Contains("-hwaccel cuda -hwaccel_output_format cuda -i") && cuda.Contains("hwdownload,format=nv12,format=yuv420p,scale=in_range=limited"), "CUDA path changed reference filter order");
            string d3d = HardwareDecodePlan.Create("d3d11va", hdr).Apply(arguments);
            check(d3d.Contains("-hwaccel_output_format d3d11") && d3d.Contains("hwdownload,format=p010le,format=yuv420p10le"), "10-bit download loses precision");
            check(HardwareDecodePlan.Create("d3d11va", hdr, "0").Apply(arguments).Contains("-hwaccel_device 0 -i"), "selected adapter was not bound to decoding");
            check(!cuda.Contains("nvenc") && cuda.EndsWith("-c:v png \"output.png\"", StringComparison.Ordinal), "GPU preference changed output codec");
            string unusual = arguments.Replace("source.mp4", "source -i test -vf test.mp4").Replace("output.png", "output -i test.png");
            check(HardwareDecodePlan.Create("cuda", sdr).Apply(unusual).Contains("source -i test -vf test.mp4"), "filename option text broke GPU argument parsing");
            check(new FfmpegProcessException("test", "Cannot load nvcuda.dll", false).HardwareInitializationFailure, "missing driver did not allow fallback");
            check(new FfmpegProcessException("test", "Device creation failed: -22. Error opening output files: Invalid argument", false).HardwareInitializationFailure, "generic output summary hid hardware initialization failure");
            check(!new FfmpegProcessException("test", "CUDA_ERROR_OUT_OF_MEMORY", true).HardwareInitializationFailure, "mid-export failure would replay a job");
            check(!new FfmpegProcessException("test", "CUDA_ERROR_UNKNOWN: No space left on device", false).HardwareInitializationFailure, "disk-full failure would retry GPU");
            HardwareFailureCache cache = new HardwareFailureCache(); cache.Failed("test"); check(cache.Blocked("test") && !cache.Blocked("other"), "failure cache key isolation failed");
            check(GpuAdmission.Estimate(hdr) > GpuAdmission.Estimate(sdr), "GPU estimate ignores depth/resolution");
            check(GpuAdapter.BudgetFor(8UL * 1024 * 1024 * 1024) == 1536L * 1024 * 1024 && GpuAdapter.BudgetFor(2UL * 1024 * 1024 * 1024) == 512L * 1024 * 1024, "adapter capacity budget did not scale");
            check(GpuAdapter.Current != null && GpuAdapter.Current.VideoBudget > 0, "DXGI fallback policy is not usable without dedicated hardware");
            using (GpuAdmission admission = new GpuAdmission(1024L * 1024 * 1024))
            using (ManualResetEventSlim waiting = new ManualResetEventSlim())
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                IDisposable first = admission.Acquire(sdr, CancellationToken.None, delegate { });
                IDisposable second = admission.Acquire(sdr, CancellationToken.None, delegate { });
                Task third = Task.Run(delegate { using (admission.Acquire(sdr, CancellationToken.None, waiting.Set)) { } });
                check(waiting.Wait(2000) && !third.IsCompleted, "GPU video limit did not block a third decoder");
                first.Dispose(); check(third.Wait(2000), "GPU admission did not resume");
                IDisposable large = null;
                VideoInfo oversized = new VideoInfo { Codec = "hevc", PixelFormat = "yuv420p10le", Width = 7680, Height = 4320 };
                Task blocked = Task.Run(delegate { large = admission.Acquire(oversized, cancel.Token, waiting.Set); });
                Thread.Sleep(100); cancel.Cancel();
                try { blocked.Wait(2000); } catch (AggregateException error) { check(error.InnerException is OperationCanceledException, "GPU wait cancellation error type"); }
                check(blocked.IsCanceled || blocked.IsFaulted, "GPU wait ignored cancellation");
                if (large != null) large.Dispose(); second.Dispose();
            }
            string store = Path.Combine(root, "gpu-history");
            using (JobCoordinator jobs = new JobCoordinator(config, delegate(MediaJob job) { job.SetProcessing("d3d11va", "saved"); }, store))
            {
                jobs.Submit(request); Wait(jobs, 5000);
            }
            using (JobCoordinator jobs = new JobCoordinator(config, delegate { throw new Exception("no replay"); }, store))
            {
                JobSnapshot saved = jobs.Snapshot().Single();
                check(saved.ProcessingBackend == "d3d11va" && saved.ProcessingNote == "saved", "actual backend was lost from history");
                jobs.Retry(saved.Id); Wait(jobs, 5000);
                System.Xml.XmlDocument xml = new System.Xml.XmlDocument(); xml.Load(Path.Combine(store, "jobs.xml"));
                check(xml.SelectNodes("/jobs/job/ProcessingDevice").Cast<System.Xml.XmlNode>().All(n => n.InnerText == "gpu"), "retry lost original GPU preference");
            }
            ProcessFailures(root, check);
            VendorContracts(root, check);
        }
        internal static int CapabilityFixture(string[] args)
        {
            if (Environment.GetEnvironmentVariable("FRAMECLIP_FIXTURE_CAPABILITY_FAIL") == "1")
            { Console.Error.WriteLine("Capability enumeration unavailable"); return 1; }
            Console.WriteLine(args.Contains("-hwaccels") ? "Hardware acceleration methods:\ncuda\nd3d11va\nqsv\ndxva2" : " V..... h264_qsv Intel H264\n V..... hevc_qsv Intel HEVC");
            return 0;
        }
        private static HardwareDecodePlan[] FixturePlans(WorkerResources resources, string executable, VideoInfo info)
        {
            return resources.HardwarePlans(executable, info, CancellationToken.None, delegate(string options) {
                return options.Contains("-hwaccels") ? "cuda\nd3d11va\nqsv\ndxva2" : " V..... h264_qsv Intel H264\n V..... hevc_qsv Intel HEVC";
            });
        }
        private static void VendorContracts(string root, Action<bool, string> check)
        {
            HardwareCapabilities caps = new HardwareCapabilities("cuda\nd3d11va\nqsv\ndxva2", " V..... h264_qsv Intel\n V..... hevc_qsv Intel");
            VideoInfo info = new VideoInfo { Codec = "hevc", PixelFormat = "yuv420p10le", Width = 3840, Height = 2160 };
            GpuAdapter intel = new GpuAdapter { Name = "Intel UMA", Vendor = 0x8086, D3DDevice = "1", DedicatedMemory = 128UL * 1024 * 1024, SharedMemory = 8UL * 1024 * 1024 * 1024 };
            GpuAdapter amd = new GpuAdapter { Name = "AMD Radeon", Vendor = 0x1002, D3DDevice = "0", DedicatedMemory = 8UL * 1024 * 1024 * 1024 };
            GpuAdapter nv = new GpuAdapter { Name = "NVIDIA", Vendor = 0x10de, D3DDevice = "2", DedicatedMemory = 8UL * 1024 * 1024 * 1024 };
            check(intel.VideoBudget == 1024L * 1024 * 1024 && intel.VideoBudget >= GpuAdmission.Estimate(info), "UMA GPU rejected UHD based only on tiny dedicated reservation");
            check(caps.Plans(info, new[] { intel }, false).Select(p => p.Backend).SequenceEqual(new[] { "d3d11va", "qsv", "dxva2" }), "Intel fallback policy incorrect");
            check(caps.Plans(info, new[] { amd }, false).Select(p => p.Backend).SequenceEqual(new[] { "d3d11va", "dxva2" }), "AMD requires a vendor runtime or tries Intel/CUDA");
            HardwareDecodePlan[] mixed = caps.Plans(info, new[] { nv, intel }, true);
            check(mixed[0].Backend == "cuda" && mixed[0].Device == "0" && mixed.Any(p => p.Backend == "qsv" && p.Device == "1"), "mixed adapter device binding incorrect");
            check(!mixed.Any(p => p.Backend == "dxva2"), "ambiguous default DXVA2 device bypassed per-adapter budgets");
            check(caps.Plans(info, new[] { intel }, false).Last().Adapter == intel, "single-adapter DXVA2 budget did not use the actual UMA adapter");
            VideoInfo av1 = new VideoInfo { Codec = "av1", PixelFormat = "yuv420p10le", Width = 640, Height = 360 };
            check(!HardwareDecodePlan.Supports(av1), "AV1 unknown film-grain state bypassed reference decoding");
            av1.HasFilmGrain = true; check(!HardwareDecodePlan.Supports(av1), "AV1 film grain admitted without fidelity acceptance");
            av1.HasFilmGrain = false; check(HardwareDecodePlan.Supports(av1), "AV1 without grain rejected");
            check(!caps.Plans(info, new[] { nv, new GpuAdapter { Vendor = 0x10de, Name = "NVIDIA 2", D3DDevice = "3" } }, true).Any(p => p.Backend == "cuda"), "ambiguous CUDA/DXGI ordinal used for multiple NVIDIA GPUs");
            check(!new HardwareCapabilities("qsv", " V..... h264_qsv Intel").Plans(info, new[] { intel }, false).Any(), "QSV used missing HEVC decoder");
            check(!new HardwareCapabilities("Hardware acceleration methods:", "").Plans(info, new[] { nv }, true).Any(), "CPU-only FFmpeg attempted unavailable GPU backends");
            string args = "-ss 1 -i \"source.mp4\" -vf \"format=yuv422p10le\" -c:v prores_ks \"result.mov\"";
            string qsv = HardwareDecodePlan.ForAdapter("qsv", info, intel).Apply(args);
            check(qsv.StartsWith("-init_hw_device d3d11va=fcbase:1 -init_hw_device qsv=fcqsv@fcbase", StringComparison.Ordinal) &&
                qsv.Contains("-hwaccel_device fcqsv -c:v hevc_qsv -i") && qsv.Contains("hwdownload,format=p010le,format=yuv420p10le,format=yuv422p10le"), "QSV changed bit depth or selected wrong adapter");
            string dxva = HardwareDecodePlan.ForAdapter("dxva2", info, null).Apply(args);
            check(dxva.Contains("-hwaccel dxva2 -hwaccel_output_format dxva2_vld -i") && !dxva.Contains("-hwaccel_device"), "DXVA2 incorrectly reused DXGI ordinal");
            string executable = System.Reflection.Assembly.GetExecutingAssembly().Location;
            HardwareFailureCache failures = new HardwareFailureCache();
            HardwareDecodePlan a = HardwareDecodePlan.ForAdapter("d3d11va", info, amd), b = HardwareDecodePlan.ForAdapter("d3d11va", info, intel);
            failures.Failed(HardwareFailureCache.Key(executable, a.Identity, info));
            check(!failures.Blocked(HardwareFailureCache.Key(executable, b.Identity, info)), "AMD failure poisoned Intel fallback");
            foreach (string marker in new[] { "Error initializing an MFX session", "Failed to create DXVA2 video decoder", "Failed to create video decoder",
                "Failed to create D3D11VA video decoder", "Unable to retrieve decoder configurations", "Could not create the decoder output view", "Failed to retrieve decoder device GUIDs" })
                check(new FfmpegProcessException("test", marker, false).HardwareInitializationFailure && !new FfmpegProcessException("test", marker, true).HardwareInitializationFailure, "vendor failure classification incorrect: " + marker);
            foreach (string backend in new[] { "qsv", "dxva2" })
                check(JobText.Processing(new JobSnapshot { ProcessingBackend = backend }, UiText.English).Contains(backend.ToUpperInvariant()), "new hardware backend missing from UI");
            HardwareCapabilityCache cache = new HardwareCapabilityCache(); int probes = 0;
            Parallel.For(0, 8, i => cache.Get(executable, CancellationToken.None, delegate(string options) {
                Interlocked.Increment(ref probes); Thread.Sleep(10); return options.Contains("-hwaccels") ? "d3d11va" : ""; }));
            check(probes == 2, "concurrent jobs duplicated FFmpeg capability subprocesses");
            int attempts = 0; HardwareCapabilityCache retry = new HardwareCapabilityCache();
            try { retry.Get(executable, CancellationToken.None, delegate { throw new IOException("probe interrupted"); }); } catch (IOException) { }
            retry.Get(executable, CancellationToken.None, delegate { attempts++; return ""; });
            check(attempts == 2, "failed capability probe stuck pending or cached a fake success");
            using (GpuAdmission admission = new GpuAdmission())
            using (IDisposable lease = admission.Acquire(info, a, CancellationToken.None, delegate { }))
            {
                Task second = Task.Run(delegate { using (admission.Acquire(info, b, CancellationToken.None, delegate { })) { } });
                check(second.Wait(2000), "different adapters incorrectly shared one VRAM estimate");
            }
        }
        // A child of this test executable exercises the real process runner without GPU hardware.
        internal static int ProcessFixture(string[] args)
        {
            string scenario = args[Array.IndexOf(args, "--frameclip-gpu-fixture") + 1];
            int hardware = Array.IndexOf(args, "-hwaccel");
            string source = args[Array.IndexOf(args, "-i") + 1];
            File.AppendAllText(Path.Combine(Path.GetDirectoryName(source), "attempts.log"),
                (hardware < 0 ? "cpu" : args[hardware + 1]) + Environment.NewLine);
            if (hardware >= 0)
            {
                if (scenario == "progress")
                { Console.WriteLine("out_time_us=500000"); Console.WriteLine("progress=continue"); Console.Out.Flush(); }
                Console.Error.WriteLine(scenario == "disk" ? "CUDA_ERROR_UNKNOWN: No space left on device" : "Device creation failed: -22");
                return 1;
            }
            File.WriteAllBytes(args[args.Length - 1], new byte[] { 1 });
            return 0;
        }
        private static void ProcessFailures(string root, Action<bool, string> check)
        {
            foreach (string scenario in new[] { "initialize", "disk", "progress", "cached", "probe" })
            {
                string directory = Path.Combine(root, "gpu-process-" + scenario); Directory.CreateDirectory(directory);
                string source = Path.Combine(directory, "source.mp4"); File.WriteAllBytes(source, new byte[] { 1 });
                AppConfig config = new AppConfig { FfmpegPath = System.Reflection.Assembly.GetExecutingAssembly().Location, ProcessingDevice = "gpu" };
                MediaJob job = new MediaJob(new JobRequest(scenario == "cached" ? CaptureAction.ExportPrecise : CaptureAction.CaptureFrame, source, 500, 1000, config));
                VideoInfo info = new VideoInfo { Codec = "h264", PixelFormat = "yuv420p", Width = 640, Height = 360 };
                using (WorkerResources resources = new WorkerResources())
                {
                    HardwareDecodePlan[] plans = scenario == "probe" ? new HardwareDecodePlan[0] : FixturePlans(resources, config.FfmpegPath, info);
                    CaptureEngine worker = (CaptureEngine)Activator.CreateInstance(typeof(CaptureEngine),
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                        new object[] { config, null, job, resources }, null);
                    string output = job.Reserve(directory, "result", ".png");
                    Exception failure = null;
                    Action invoke = delegate
                    {
                        try
                        {
                        typeof(CaptureEngine).GetMethod("RunDecodedMedia", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                            .Invoke(worker, new object[] { "--frameclip-gpu-fixture " + scenario + " -i \"" + source + "\" -vf \"format=rgb24\" \"" + output + "\"", "frame fixture", info });
                        }
                        catch (System.Reflection.TargetInvocationException error) { failure = error.InnerException; }
                    };
                    try
                    {
                        if (scenario == "cached")
                        {
                            foreach (HardwareDecodePlan plan in plans)
                                resources.HardwareFailures.Failed(HardwareFailureCache.Key(config.FfmpegPath, plan.Identity, info));
                            using (IDisposable first = resources.GpuVideos.Acquire(info, CancellationToken.None, delegate { }))
                            using (IDisposable second = resources.GpuVideos.Acquire(info, CancellationToken.None, delegate { }))
                            {
                                Task fallback = Task.Run(invoke);
                                bool bypassed = fallback.Wait(3000);
                                first.Dispose(); second.Dispose();
                                check(fallback.Wait(10000) && bypassed, "cached CPU fallback waited for occupied GPU slots");
                            }
                        }
                        else if (scenario == "probe")
                        {
                            string previous = Environment.GetEnvironmentVariable("FRAMECLIP_FIXTURE_CAPABILITY_FAIL");
                            try { Environment.SetEnvironmentVariable("FRAMECLIP_FIXTURE_CAPABILITY_FAIL", "1"); invoke(); }
                            finally { Environment.SetEnvironmentVariable("FRAMECLIP_FIXTURE_CAPABILITY_FAIL", previous); }
                        }
                        else invoke();
                    }
                    finally { foreach (OutputReservation reservation in job.Reservations) reservation.Dispose(); job.Cancellation.Dispose(); }
                    string[] attempts = File.ReadAllLines(Path.Combine(directory, "attempts.log"));
                    if (scenario == "initialize" || scenario == "cached" || scenario == "probe")
                    {
                        check(failure == null && attempts.SequenceEqual(scenario == "cached" || scenario == "probe" ? new[] { "cpu" } : plans.Select(p => p.Backend).Concat(new[] { "cpu" })), "initialization failure did not follow bounded backend/CPU fallback");
                        check(job.Outputs.Count == 1 && File.Exists(job.Outputs[0]) && job.ProcessingBackend == "cpu" && job.ProcessingNote.Length > 0, "fallback did not publish one truthful CPU result");
                        check(plans.All(p => resources.HardwareFailures.Blocked(HardwareFailureCache.Key(config.FfmpegPath, p.Identity, info))), "process initialization failures were not cached");
                    }
                    else
                    {
                        FfmpegProcessException processError = failure as FfmpegProcessException;
                        check(processError != null && !processError.HardwareInitializationFailure && attempts.Length == 1 && job.Outputs.Count == 0, "process failure silently retried: " + scenario);
                        check(processError != null && processError.HadProgress == (scenario == "progress"), "process progress evidence was not retained: " + scenario);
                        check(!resources.HardwareFailures.Blocked(HardwareFailureCache.Key(config.FfmpegPath, plans[0].Identity, info)), "non-initialization failure poisoned the hardware cache");
                    }
                    check(!Directory.GetFiles(directory, "*.fc-lock").Any() && !Directory.GetFiles(directory, ".fc-*").Any(), "process failure fixture leaked temporary outputs");
                }
            }
        }
        private static void Wait(JobCoordinator jobs, int timeout)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (jobs.Busy && watch.ElapsedMilliseconds < timeout) Thread.Sleep(20);
            if (jobs.Busy) throw new TimeoutException("GPU integration timed out.");
            // The coordinator persists in the worker cleanup phase.
            Thread.Sleep(150);
        }
        private static System.Collections.Generic.IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls) { yield return child; foreach (Control nested in Descendants(child)) yield return nested; }
        }
        internal static void Integration(string root, string ffmpeg, Action<bool, string> check)
        {
            check(GpuAdapter.Current.D3DDevice != null && GpuAdapter.Current.DedicatedMemory > 0, "DXGI did not discover this validation host GPU");
            AppConfig config = new AppConfig { FfmpegPath = ffmpeg, FfprobePath = Path.Combine(Path.GetDirectoryName(ffmpeg), "ffprobe.exe"),
                LibraryRootDirectory = Path.Combine(root, "gpu-fidelity"), ResourceMode = "fast", LowerBackgroundPriority = false };
            using (CaptureEngine helper = new CaptureEngine(config, null))
            {
                // Fixture encoders are test-only; LGPL bundles intentionally omit x264/x265.
                string fixtureFfmpeg = Environment.GetEnvironmentVariable("FRAMECLIP_TEST_FIXTURE_FFMPEG") ?? ffmpeg;
                foreach (bool hdr in new[] { false, true })
                {
                    string source = Path.Combine(root, hdr ? "GPU-PQ.mp4" : "GPU-SDR.mp4");
                    helper.RunProcess(fixtureFfmpeg, "-hide_banner -loglevel error -y -f lavfi -i testsrc2=size=640x360:rate=24 -f lavfi -i sine=frequency=440:sample_rate=48000 -t 3 " +
                        (hdr ? "-vf \"format=yuv420p10le,setparams=range=limited:color_primaries=bt2020:color_trc=smpte2084:colorspace=bt2020nc\" -c:v libx265 -preset fast -x265-params pools=2:frame-threads=2:log-level=error:colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc -color_primaries bt2020 -color_trc smpte2084 -colorspace bt2020nc" :
                        "-vf \"format=yuv420p,setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709\" -c:v libx264 -preset fast -crf 16 -pix_fmt yuv420p -color_primaries bt709 -color_trc bt709 -colorspace bt709") + " -color_range tv -c:a aac \"" + source + "\"", "GPU fixture", 30000);
                    string fixtureTags = helper.RunProcess(config.FfprobePath, "-v error -select_streams v:0 -show_entries stream=color_transfer,color_primaries -of compact \"" + source + "\"", "fixture metadata", 15000);
                    check(fixtureTags.Contains(hdr ? "smpte2084" : "bt709") && fixtureTags.Contains(hdr ? "bt2020" : "bt709"), "synthetic source color metadata was not signaled: " + fixtureTags);
                    foreach (string backend in new[] { "cuda", "d3d11va", "dxva2" })
                    {
                        VideoInfo info = new VideoInfo { Codec = hdr ? "hevc" : "h264", PixelFormat = hdr ? "yuv420p10le" : "yuv420p", Width = 640, Height = 360 };
                        string raw = "-hide_banner -loglevel error -ss 0.75 -i \"" + source + "\" -frames:v 24 -vf \"format=" + info.PixelFormat + "\" -f framemd5 -";
                        string cpu = helper.RunProcess(ffmpeg, raw, "CPU fidelity", 15000);
                        string gpu = helper.RunProcess(ffmpeg, HardwareDecodePlan.ForAdapter(backend, info, GpuAdapter.Current).Apply(raw), "GPU fidelity", 15000);
                        check(cpu == gpu, "hardware decoded pixels differ: " + backend + (hdr ? " HEVC10" : " H264"));
                    }
                    using (Control dispatcher = new Control())
                    {
                        dispatcher.CreateControl();
                        using (CaptureEngine engine = new CaptureEngine(config, dispatcher))
                        {
                            foreach (string format in new[] { "png16", "tiff16" })
                            foreach (CaptureAction action in new[] { CaptureAction.CaptureFrame, CaptureAction.ExportPrecise })
                            {
                                if (format == "tiff16" && action != CaptureAction.CaptureFrame) continue;
                                config.ImageFormat = format; config.ExportRec709ForHdr = hdr; config.ProcessingDevice = "cpu";
                                string a = engine.Jobs.Submit(new JobRequest(action, source, 750, 1750, config));
                                config.ProcessingDevice = "gpu";
                                string b = engine.Jobs.Submit(new JobRequest(action, source, 750, 1750, config));
                                Wait(engine.Jobs, 60000);
                                JobSnapshot cpu = engine.Jobs.Snapshot().Single(s => s.Id == a), gpu = engine.Jobs.Snapshot().Single(s => s.Id == b);
                                check(cpu.State == JobState.Succeeded && gpu.State == JobState.Succeeded, "GPU/CPU production export failed: " + cpu.Error + "; " + gpu.Error);
                                check(cpu.ProcessingBackend == "cpu" && (gpu.ProcessingBackend == "cuda" || gpu.ProcessingBackend == "d3d11va"), "GPU preference did not run hardware decoder: " + gpu.ProcessingNote);
                                check(cpu.Outputs.Length == gpu.Outputs.Length && gpu.Outputs.Length == (hdr && action == CaptureAction.CaptureFrame ? 2 : 1),
                                    "GPU changed expected outputs: " + format + " " + action + " hdr=" + hdr + " cpu=" + cpu.Outputs.Length + " gpu=" + gpu.Outputs.Length + " warnings=" + cpu.Warning + "; " + gpu.Warning);
                                for (int i = 0; i < Math.Min(cpu.Outputs.Length, gpu.Outputs.Length); i++)
                                {
                                    string options = "-hide_banner -loglevel error -i \"{0}\" -map 0:v:0 -frames:v 24 -f framemd5 -";
                                    check(helper.RunProcess(ffmpeg, String.Format(options, cpu.Outputs[i]), "CPU pixels", 15000) == helper.RunProcess(ffmpeg, String.Format(options, gpu.Outputs[i]), "GPU pixels", 15000), "GPU production pixels changed: " + format + " " + action + " hdr=" + hdr);
                                    string probe = "-v error -show_entries stream=codec_name,pix_fmt,color_transfer,color_primaries,color_space,profile,sample_rate -of compact \"{0}\"";
                                    check(helper.RunProcess(config.FfprobePath, String.Format(probe, cpu.Outputs[i]), "CPU tags", 15000) == helper.RunProcess(config.FfprobePath, String.Format(probe, gpu.Outputs[i]), "GPU tags", 15000), "GPU production tags changed");
                                }
                            }
                            config.ProcessingDevice = "gpu";
                            string copy = engine.Jobs.Submit(new JobRequest(CaptureAction.ExportOriginal, source, 0, 1500, config)); Wait(engine.Jobs, 15000);
                            check(engine.Jobs.Snapshot().Single(s => s.Id == copy).ProcessingBackend == "copy", "stream copy was mislabeled GPU");
                            foreach (string preset in new[] { "dnxhrhqx", "dnxhr444", "prores4444", "prores4444xq" })
                            {
                                config.VideoPreset = preset; config.ProcessingDevice = "cpu";
                                string a = engine.Jobs.Submit(new JobRequest(CaptureAction.ExportPrecise, source, 500, 1000, config)); config.ProcessingDevice = "gpu";
                                string b = engine.Jobs.Submit(new JobRequest(CaptureAction.ExportPrecise, source, 500, 1000, config)); Wait(engine.Jobs, 30000);
                                JobSnapshot cpu = engine.Jobs.Snapshot().Single(s => s.Id == a), gpu = engine.Jobs.Snapshot().Single(s => s.Id == b);
                                check(cpu.State == JobState.Succeeded && gpu.State == JobState.Succeeded, "editing codec failed with hardware decode: " + preset + " " + cpu.Error + " " + gpu.Error);
                                if (cpu.Outputs.Length == 1 && gpu.Outputs.Length == 1)
                                {
                                    string hash = "-hide_banner -loglevel error -i \"{0}\" -map 0:v:0 -f framemd5 -";
                                    check(helper.RunProcess(ffmpeg, String.Format(hash, cpu.Outputs[0]), "CPU editing pixels", 15000) == helper.RunProcess(ffmpeg, String.Format(hash, gpu.Outputs[0]), "GPU editing pixels", 15000), "GPU editing codec pixels changed: " + preset);
                                }
                            }
                            config.VideoPreset = "prores422hq";
                            VideoInfo blockedInfo = new VideoInfo { Codec = hdr ? "hevc" : "h264", PixelFormat = hdr ? "yuv420p10le" : "yuv420p", Width = 640, Height = 360 };
                            foreach (HardwareDecodePlan plan in engine.Jobs.Resources.HardwarePlans(ffmpeg, blockedInfo, CancellationToken.None,
                                delegate(string options) { return helper.RunProcess(ffmpeg, options, "hardware capabilities probe", 10000); }))
                                engine.Jobs.Resources.HardwareFailures.Failed(HardwareFailureCache.Key(ffmpeg, plan.Identity, blockedInfo));
                            string fallback = engine.Jobs.Submit(new JobRequest(CaptureAction.CaptureFrame, source, 500, 500, config)); Wait(engine.Jobs, 15000);
                            JobSnapshot fallbackResult = engine.Jobs.Snapshot().Single(s => s.Id == fallback);
                            check(fallbackResult.State == JobState.Succeeded && fallbackResult.ProcessingBackend == "cpu" && !String.IsNullOrEmpty(fallbackResult.ProcessingNote), "cached hardware failure did not use truthful CPU fallback");
                        }
                    }
                }
                ExtendedCodecs(root, ffmpeg, helper, check);
            }
        }
        private static void ExtendedCodecs(string root, string ffmpeg, CaptureEngine helper, Action<bool, string> check)
        {
            foreach (string codec in new[] { "vp9", "av1" })
            foreach (bool tenBit in new[] { false, true })
            {
                string pixel = tenBit ? "yuv420p10le" : "yuv420p";
                string source = Path.Combine(root, codec + (tenBit ? "10" : "8") + ".mkv");
                helper.RunProcess(ffmpeg, "-hide_banner -loglevel error -y -f lavfi -i testsrc2=size=320x180:rate=12 -frames:v 12 -pix_fmt " + pixel +
                    (codec == "av1" ? " -c:v libaom-av1 -cpu-used 8 -crf 30" : " -c:v libvpx-vp9 -deadline realtime -cpu-used 8 -crf 30 -b:v 0") +
                    " -threads 2 \"" + source + "\"", "extended GPU fixture", 30000);
                VideoInfo info = new VideoInfo { Codec = codec, PixelFormat = pixel, Width = 320, Height = 180, HasFilmGrain = false };
                string raw = "-hide_banner -loglevel error -i \"" + source + "\" -frames:v 12 -vf \"format=" + pixel + "\" -f framemd5 -";
                string cpu = helper.RunProcess(ffmpeg, raw, "extended CPU fidelity", 15000);
                foreach (string backend in new[] { "cuda", "d3d11va", "dxva2" })
                    check(cpu == helper.RunProcess(ffmpeg, HardwareDecodePlan.ForAdapter(backend, info, GpuAdapter.Current).Apply(raw), "extended GPU fidelity", 15000),
                        "hardware pixels differ: " + codec + " " + pixel + " " + backend);
                string tags = helper.RunProcess(Path.Combine(Path.GetDirectoryName(ffmpeg), "ffprobe.exe"),
                    "-v error -select_streams v:0 -show_entries stream=film_grain -of default=noprint_wrappers=1 \"" + source + "\"", "film grain probe", 15000);
                if (codec == "av1")
                {
                    VideoInfo detected = (VideoInfo)typeof(CaptureEngine).GetMethod("ProbeVideo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(helper, new object[] { source });
                    check(tags.Contains("film_grain=0") ? detected.HasFilmGrain == false : detected.HasFilmGrain == null && !HardwareDecodePlan.Supports(detected),
                        "AV1 missing film-grain metadata must retain safe CPU decoding");
                }
            }
            string grain = Path.Combine(root, "av1-grain.mkv");
            helper.RunProcess(ffmpeg, "-hide_banner -loglevel error -y -f lavfi -i testsrc2=size=320x180:rate=12 -frames:v 12 -c:v libaom-av1 -cpu-used 8 -crf 30 -threads 2 -aom-params film-grain-test=1 \"" + grain + "\"", "grain fixture", 30000);
            VideoInfo probed = (VideoInfo)typeof(CaptureEngine).GetMethod("ProbeVideo", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(helper, new object[] { grain });
            check(probed.HasFilmGrain != false && !HardwareDecodePlan.Supports(probed), "real AV1 grain or unknown state did not protect reference decoding");
        }
    }
}
