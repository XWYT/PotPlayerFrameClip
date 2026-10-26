using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace PotPlayerFrameClip
{
    internal static class FfmpegArguments
    {
        internal static int FindOption(string arguments, string option, bool last = false)
        {
            bool quoted = false; int found = -1;
            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i] == '"') { quoted = !quoted; continue; }
                if (quoted || (i > 0 && !Char.IsWhiteSpace(arguments[i - 1]))) continue;
                if (i + option.Length <= arguments.Length && String.CompareOrdinal(arguments, i, option, 0, option.Length) == 0 &&
                    (i + option.Length == arguments.Length || Char.IsWhiteSpace(arguments[i + option.Length])))
                { found = i; if (!last) return i; }
            }
            return found;
        }
    }

    internal sealed class HardwareDecodePlan
    {
        internal string Backend, PixelFormat, TransferFormat, Device;
        internal GpuAdapter Adapter;
        internal string Identity { get { return Backend + "@" + (Adapter == null ? Device ?? "default" : Adapter.Identity); } }
        internal long VideoBudget { get { return Adapter == null ? GpuAdapter.Current.VideoBudget : Adapter.VideoBudget; } }
        internal static bool Supports(VideoInfo info)
        {
            if (info == null || info.Width <= 0 || info.Height <= 0 || info.IsDolbyVision) return false;
            // Hardware film-grain synthesis is not pixel-identical across drivers.
            // Older probes that cannot establish its absence retain reference decoding.
            if (info.Codec == "av1" && info.HasFilmGrain != false) return false;
            if (info.Codec == "h264") return info.PixelFormat == "yuv420p";
            return (info.Codec == "hevc" || info.Codec == "vp9" || info.Codec == "av1") &&
                (info.PixelFormat == "yuv420p" || info.PixelFormat == "yuv420p10le");
        }
        internal static HardwareDecodePlan Create(string backend, VideoInfo info, string device = null)
        {
            if (!Supports(info) || (backend != "cuda" && backend != "d3d11va" && backend != "qsv" && backend != "dxva2")) throw new ArgumentException("Unsupported hardware decode plan.");
            return new HardwareDecodePlan { Backend = backend, PixelFormat = info.PixelFormat, Device = device, Codec = info.Codec,
                TransferFormat = info.PixelFormat == "yuv420p10le" ? "p010le" : "nv12" };
        }
        internal string Apply(string arguments)
        {
            int input = FfmpegArguments.FindOption(arguments, "-i");
            if (input < 0 || FfmpegArguments.FindOption(arguments, "-i", true) != input) throw new ArgumentException("Hardware decoding requires one input.");
            int filter = FfmpegArguments.FindOption(arguments, "-vf");
            if (filter < input || !arguments.Substring(filter).StartsWith("-vf \"", StringComparison.Ordinal)) throw new ArgumentException("Hardware decoding requires the reference filter chain.");
            // Download exactly once, then keep the existing reference color and encoder path.
            arguments = arguments.Insert(filter + 5, "hwdownload,format=" + TransferFormat + ",format=" + PixelFormat + ",");
            string prefix = String.Empty;
            string device = Device;
            if (Backend == "qsv")
            {
                // Derive QSV from the exact DXGI adapter rather than assuming Intel is adapter 0.
                prefix = "-init_hw_device d3d11va=fcbase:" + Device + " -init_hw_device qsv=fcqsv@fcbase ";
                device = "fcqsv";
            }
            string outputFormat = Backend == "d3d11va" ? "d3d11" : Backend == "dxva2" ? "dxva2_vld" : Backend;
            return prefix + arguments.Insert(input, "-hwaccel " + Backend + " -hwaccel_output_format " + outputFormat +
                (String.IsNullOrEmpty(device) ? "" : " -hwaccel_device " + device) +
                (Backend == "qsv" ? " -c:v " + Codec + "_qsv" : "") + " ");
        }
        private string Codec;
        internal static HardwareDecodePlan ForAdapter(string backend, VideoInfo info, GpuAdapter adapter)
        {
            HardwareDecodePlan plan = Create(backend, info, backend == "cuda" ? "0" : backend == "dxva2" ? null : adapter.D3DDevice);
            plan.Adapter = adapter; plan.Codec = info.Codec; return plan;
        }
    }

    internal sealed class HardwareCapabilities
    {
        private readonly HashSet<string> accelerators = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> decoders = new HashSet<string>(StringComparer.Ordinal);
        internal HardwareCapabilities(string hardware, string codecs)
        {
            foreach (string line in (hardware ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string value = line.Trim();
                if (new[] { "cuda", "d3d11va", "qsv", "dxva2" }.Contains(value)) accelerators.Add(value);
            }
            foreach (string line in (codecs ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] fields = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length > 1 && fields[0].Length == 6 && fields[0][0] == 'V') decoders.Add(fields[1]);
            }
        }
        internal HardwareDecodePlan[] Plans(VideoInfo info, IEnumerable<GpuAdapter> adapters, bool cudaAvailable)
        {
            if (!HardwareDecodePlan.Supports(info)) return new HardwareDecodePlan[0];
            GpuAdapter[] devices = adapters.ToArray();
            int nvidia = devices.Count(a => a.Vendor == 0x10de);
            List<HardwareDecodePlan> plans = new List<HardwareDecodePlan>();
            foreach (GpuAdapter adapter in devices.Take(4))
            {
                // DXGI and CUDA ordinals are different namespaces; device 0 is safe only for one NVIDIA.
                if (nvidia == 1 && adapter.Vendor == 0x10de && cudaAvailable && accelerators.Contains("cuda"))
                    plans.Add(HardwareDecodePlan.ForAdapter("cuda", info, adapter));
                if (accelerators.Contains("d3d11va")) plans.Add(HardwareDecodePlan.ForAdapter("d3d11va", info, adapter));
                if (adapter.Vendor == 0x8086 && adapter.D3DDevice != null && accelerators.Contains("qsv") && decoders.Contains(info.Codec + "_qsv"))
                    plans.Add(HardwareDecodePlan.ForAdapter("qsv", info, adapter));
            }
            // D3D9 ordinals need not match DXGI. Only a single adapter gives its default
            // device an unambiguous budget; hybrid systems retain explicitly bound backends.
            if (devices.Length == 1 && accelerators.Contains("dxva2"))
                plans.Add(HardwareDecodePlan.ForAdapter("dxva2", info, devices[0]));
            return plans.Where(p => GpuAdmission.Estimate(info) <= p.VideoBudget).ToArray();
        }
    }

    internal sealed class FfmpegProcessException : IOException
    {
        internal readonly string Diagnostics;
        internal readonly bool HadProgress;
        internal FfmpegProcessException(string operation, string diagnostics, bool hadProgress)
            : base(operation + ": " + diagnostics.Trim()) { Diagnostics = diagnostics; HadProgress = hadProgress; }
        internal bool HardwareInitializationFailure
        {
            get
            {
                if (HadProgress) return false;
                string text = Diagnostics.ToLowerInvariant();
                if (text.Contains("no space left") || text.Contains("permission denied") || text.Contains("disk full")) return false;
                foreach (string marker in new[] { "cannot load nvcuda", "could not dynamically load cuda", "no cuda capable device", "cuda_error_",
                    "device creation failed", "no device available for decoder", "hardware device setup failed", "failed setup for format",
                    "hwaccel initialisation returned error", "failed to create d3d11", "failed to create a d3d11", "does not support the required nvdec",
                    "unrecognized hwaccel", "unrecognized option 'hwaccel", "invalid hwaccel", "device type cuda needed", "device type d3d11va needed",
                    "no such filter: 'hwdownload'", "error initializing an internal frame pool", "unsupported bit depth",
                    "error initializing an mfx session", "error initializing the mfx video decoder", "error creating a mfx session",
                    "failed to create dxva2 video decoder", "failed to create idirectxvideodecoderservice",
                    "failed to create direct3d device", "failed to create idirect3d object", "failed to get the decoder guids",
                    "failed to create video decoder", "no decoder device for codec found",
                    "unable to retrieve decoder configurations", "could not create the decoder output view",
                    "failed to retrieve decoder device guids", "your platform doesn't support hardware accelerated av1 decoding" })
                    if (text.Contains(marker)) return true;
                return false;
            }
        }
    }

    internal sealed class GpuAdmission : IDisposable
    {
        private sealed class Lease : IDisposable
        {
            private GpuAdmission owner;
            private readonly long cost;
            private readonly string device;
            internal Lease(GpuAdmission owner, long cost, string device) { this.owner = owner; this.cost = cost; this.device = device; }
            public void Dispose() { GpuAdmission value = Interlocked.Exchange(ref owner, null); if (value != null) value.Release(cost, device); }
        }
        private readonly object gate = new object();
        private int active;
        private readonly LinkedList<object> queue = new LinkedList<object>();
        private readonly Dictionary<string, long> allocated = new Dictionary<string, long>();
        private bool disposed;
        // Soft admission estimate, not a driver-enforced VRAM cap. Leave playback headroom.
        internal readonly long VideoBudget;
        internal GpuAdmission() : this(GpuAdapter.Current.VideoBudget) { }
        internal GpuAdmission(long budget) { VideoBudget = budget; }
        internal static long Estimate(VideoInfo info)
        {
            long pixels = checked((long)info.Width * info.Height);
            return checked(pixels * (info.PixelFormat == "yuv420p10le" ? 3 : 2) * 24);
        }
        internal IDisposable Acquire(VideoInfo info, CancellationToken token, Action waiting)
        { return Acquire(info, "default", VideoBudget, token, waiting); }
        internal IDisposable Acquire(VideoInfo info, HardwareDecodePlan plan, CancellationToken token, Action waiting)
        { return Acquire(info, plan.Adapter == null ? "default" : plan.Adapter.Identity, plan.VideoBudget, token, waiting); }
        private IDisposable Acquire(VideoInfo info, string device, long budget, CancellationToken token, Action waiting)
        {
            long cost = Estimate(info); bool notified = false;
            lock (gate)
            {
                LinkedListNode<object> ticket = queue.AddLast(new object());
                try
                {
                    long used;
                    while (!disposed && (queue.First != ticket || active >= 2 ||
                        (allocated.TryGetValue(device, out used) && used > 0 && used + cost > budget)))
                    {
                        token.ThrowIfCancellationRequested();
                        if (!notified) { notified = true; waiting(); }
                        Monitor.Wait(gate, 100);
                    }
                    token.ThrowIfCancellationRequested();
                    if (disposed) throw new ObjectDisposedException("GpuAdmission");
                    allocated.TryGetValue(device, out used);
                    active++; allocated[device] = used + cost; return new Lease(this, cost, device);
                }
                finally { queue.Remove(ticket); Monitor.PulseAll(gate); }
            }
        }
        private void Release(long cost, string device) { lock (gate) { active--; allocated[device] -= cost; Monitor.PulseAll(gate); } }
        public void Dispose() { lock (gate) { disposed = true; Monitor.PulseAll(gate); } }
    }

    internal sealed class HardwareFailureCache
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, DateTime> failures = new Dictionary<string, DateTime>();
        internal static string Key(string executable, string backend, VideoInfo info)
        {
            FileInfo tool = new FileInfo(executable);
            return tool.FullName + "|" + tool.Length + "|" + tool.LastWriteTimeUtc.Ticks + "|" + backend + "|" +
                info.Codec + "|" + info.PixelFormat + "|" + info.Width + "x" + info.Height;
        }
        internal bool Blocked(string key)
        {
            lock (gate) { DateTime until; if (!failures.TryGetValue(key, out until)) return false; if (until > DateTime.UtcNow) return true; failures.Remove(key); return false; }
        }
        internal void Failed(string key) { lock (gate) { if (failures.Count >= 64) failures.Clear(); failures[key] = DateTime.UtcNow.AddMinutes(5); } }
    }

    internal sealed class HardwareCapabilityCache
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, HardwareCapabilities> cache = new Dictionary<string, HardwareCapabilities>();
        private readonly HashSet<string> pending = new HashSet<string>();
        internal HardwareCapabilities Get(string executable, CancellationToken token, Func<string, string> probe)
        {
            FileInfo file = new FileInfo(executable);
            string key = file.FullName + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks;
            lock (gate)
            {
                while (pending.Contains(key)) { token.ThrowIfCancellationRequested(); Monitor.Wait(gate, 100); }
                token.ThrowIfCancellationRequested();
                HardwareCapabilities found; if (cache.TryGetValue(key, out found)) return found;
                pending.Add(key);
            }
            try
            {
                HardwareCapabilities result = new HardwareCapabilities(probe("-hide_banner -hwaccels"), probe("-hide_banner -decoders"));
                lock (gate) { if (cache.Count >= 8) cache.Clear(); cache[key] = result; }
                return result;
            }
            finally { lock (gate) { pending.Remove(key); Monitor.PulseAll(gate); } }
        }
    }

    internal sealed class GpuAdapter
    {
        internal static readonly GpuAdapter[] All = Discover();
        internal static readonly GpuAdapter Current = All[0];
        internal string Name = "Automatic hardware adapter", D3DDevice;
        internal ulong DedicatedMemory, SharedMemory;
        internal uint Vendor;
        internal string Identity { get { return Vendor.ToString("x") + ":" + (D3DDevice ?? "auto") + ":" + Name; } }
        internal long VideoBudget { get { return BudgetFor(DedicatedMemory, SharedMemory); } }
        internal static long BudgetFor(ulong dedicated, ulong shared)
        {
            // UMA adapters often report only a tiny dedicated reservation, not their usable capacity.
            if (dedicated <= 256UL * 1024 * 1024 && shared > dedicated)
                return (long)Math.Max(256UL * 1024 * 1024, Math.Min(1024UL * 1024 * 1024, shared / 8));
            return BudgetFor(dedicated);
        }
        internal static long BudgetFor(ulong dedicated)
        {
            // Reserve most VRAM for the player, desktop and other apps, plus one image lane.
            return dedicated == 0 ? 512L * 1024 * 1024 : (long)Math.Max(256UL * 1024 * 1024, Math.Min(1536UL * 1024 * 1024, dedicated / 4));
        }
        private static GpuAdapter[] Discover()
        {
            List<GpuAdapter> found = new List<GpuAdapter>(); IntPtr factory = IntPtr.Zero;
            try
            {
                Guid iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
                if (CreateDXGIFactory1(ref iid, out factory) < 0 || factory == IntPtr.Zero) return new[] { new GpuAdapter() };
                EnumAdapters enumerate = (EnumAdapters)Marshal.GetDelegateForFunctionPointer(Method(factory, 12), typeof(EnumAdapters));
                for (uint index = 0; index < 32; index++)
                {
                    IntPtr adapter; if (enumerate(factory, index, out adapter) < 0) break;
                    try
                    {
                        AdapterDescription desc;
                        GetDescription get = (GetDescription)Marshal.GetDelegateForFunctionPointer(Method(adapter, 10), typeof(GetDescription));
                        if (get(adapter, out desc) < 0 || (desc.Flags & 2) != 0) continue;
                        found.Add(new GpuAdapter { Name = desc.Description, Vendor = desc.Vendor,
                            DedicatedMemory = desc.DedicatedVideoMemory.ToUInt64(), SharedMemory = desc.SharedSystemMemory.ToUInt64(),
                            D3DDevice = index.ToString(System.Globalization.CultureInfo.InvariantCulture) });
                    }
                    finally { Marshal.Release(adapter); }
                }
            }
            catch (Exception error) { System.Diagnostics.Trace.WriteLine("GPU adapter discovery: " + error.Message); }
            finally { if (factory != IntPtr.Zero) Marshal.Release(factory); }
            GpuAdapter[] result = found.OrderByDescending(a => a.DedicatedMemory).ThenBy(a => a.D3DDevice).ToArray();
            if (result.Length == 0) result = new[] { new GpuAdapter() };
            return result;
        }
        private static IntPtr Method(IntPtr instance, int index) { return Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), index * IntPtr.Size); }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct AdapterDescription
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Description;
            internal uint Vendor, Device, Subsystem, Revision;
            internal UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
            internal uint LuidLow; internal int LuidHigh; internal uint Flags;
        }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters(IntPtr self, uint index, out IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDescription(IntPtr self, out AdapterDescription description);
        [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
    }
}
