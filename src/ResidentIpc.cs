using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PotPlayerFrameClip
{
    internal sealed class ResidentIpc : IDisposable
    {
        private readonly Func<string, string> dispatch;
        private readonly object gate = new object();
        private NamedPipeServerStream server;
        private volatile bool stopping;
        private readonly Task listener;
        internal static string PipeName
        {
            get
            {
                string identity = WindowsIdentity.GetCurrent().User.Value + "|" + Path.GetFullPath(AppPaths.DataDirectory).TrimEnd('\\').ToUpperInvariant();
                using (SHA256 hash = SHA256.Create())
                    return "FrameClip-v1-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity))).Replace("-", "").Substring(0, 24);
            }
        }
        internal static string MutexName
        {
            get { return String.IsNullOrEmpty(Environment.GetEnvironmentVariable("FRAMECLIP_DATA_DIRECTORY")) ? "Local\\PotPlayerFrameClip-8D04C93A" : "Local\\" + PipeName; }
        }
        internal ResidentIpc(Func<string, string> dispatch)
        {
            this.dispatch = dispatch; listener = Task.Run((Action)Listen);
        }
        private void Listen()
        {
            while (!stopping)
            {
                try
                {
                    PipeSecurity access = new PipeSecurity();
                    access.SetAccessRuleProtection(true, false);
                    access.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    using (NamedPipeServerStream pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, access))
                    {
                        lock (gate) { if (stopping) return; server = pipe; }
                        pipe.WaitForConnection();
                        string command = ReadPacket(pipe, 2000);
                        WritePacket(pipe, dispatch(command), 2000);
                        lock (gate) server = null;
                    }
                }
                catch (Exception error) { if (!stopping) { Trace.WriteLine("IPC: " + error.Message); Thread.Sleep(100); } }
            }
        }
        internal static string Handle(string command, CaptureEngine engine, Control dispatcher)
        {
            CaptureAction action;
            if (command == "v1 --tasks" || command == "v1 --settings" || command == "v1 --quit")
            {
                dispatcher.BeginInvoke(new Action(delegate {
                    if (command == "v1 --tasks") engine.OpenTasks();
                    else if (command == "v1 --settings") engine.OpenSettings(); else engine.Quit();
                }));
                return "ok";
            }
            switch (command)
            {
                case "v1 --maintenance-stop": action = default(CaptureAction); break;
                case "v1 --capture": action = CaptureAction.CaptureFrame; break;
                case "v1 --mark-in": action = CaptureAction.MarkIn; break;
                case "v1 --mark-out": action = CaptureAction.MarkOut; break;
                case "v1 --original": action = CaptureAction.ExportOriginal; break;
                case "v1 --precise": action = CaptureAction.ExportPrecise; break;
                case "v1 --clear": action = CaptureAction.ClearRange; break;
                case "v1 --open-images": action = CaptureAction.OpenImageOutput; break;
                case "v1 --open-videos": action = CaptureAction.OpenVideoOutput; break;
                default: return "error:unsupported command or protocol";
            }
            // Expire a queued UI dispatch instead of accepting work after the client timed out.
            int phase = 0;
            string response = "error:resident UI is unavailable";
            using (ManualResetEvent done = new ManualResetEvent(false))
            {
                dispatcher.BeginInvoke(new Action(delegate {
                    if (Interlocked.CompareExchange(ref phase, 1, 0) != 0) return;
                    try { if (command == "v1 --maintenance-stop") response = engine.StopForMaintenance() ? "ok" : "error:jobs active"; else { engine.Execute(action); response = engine.LastSubmissionResult; } }
                    catch (Exception error) { response = "error:" + error.Message; }
                    finally { Interlocked.Exchange(ref phase, 2); try { done.Set(); } catch (ObjectDisposedException) { } }
                }));
                if (!done.WaitOne(4000))
                {
                    if (Interlocked.CompareExchange(ref phase, 3, 0) == 0) return response;
                    // A bounded player query may already be executing. Never resend this action.
                    if (!done.WaitOne(4000)) return "error:request status unknown; inspect task center before retrying";
                }
            }
            return response ?? "error:no acknowledgement";
        }
        internal static bool TrySend(string command, int connectTimeout, out string response)
        {
            response = null;
            try
            {
                using (NamedPipeClientStream pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    pipe.Connect(connectTimeout); WritePacket(pipe, "v1 " + command, 2000); response = ReadPacket(pipe, 10000); return true;
                }
            }
            catch (TimeoutException) { return false; }
            catch (IOException error) { response = "error:" + error.Message; return false; }
        }
        internal static int Forward(string command)
        {
            string response;
            if (command == "--maintenance-stop")
            {
                if (!TrySend(command, 350, out response)) return 3;
                return response == "ok" ? 0 : response == "error:jobs active" ? 4 : 2;
            }
            if (!TrySend(command, 350, out response))
            {
                if (response != null) return 3;
                using (Process process = Process.Start(new ProcessStartInfo(Application.ExecutablePath) { UseShellExecute = false, CreateNoWindow = true })) { }
                Stopwatch wait = Stopwatch.StartNew();
                while (wait.ElapsedMilliseconds < 12000)
                {
                    if (TrySend(command, 250, out response)) break;
                    if (response != null) return 3;
                    Thread.Sleep(100);
                }
            }
            return response != null && (response == "ok" || response.StartsWith("job:", StringComparison.Ordinal)) ? 0 : 2;
        }
        private static void WritePacket(Stream stream, string value, int timeout)
        {
            byte[] data = Encoding.UTF8.GetBytes(value.Length > 2000 ? value.Substring(0, 2000) : value);
            if (data.Length > 8192) throw new IOException("IPC packet exceeds limit.");
            byte[] length = BitConverter.GetBytes(data.Length);
            byte[] packet = new byte[4 + data.Length]; Buffer.BlockCopy(length, 0, packet, 0, 4); Buffer.BlockCopy(data, 0, packet, 4, data.Length);
            IAsyncResult result = stream.BeginWrite(packet, 0, packet.Length, null, null);
            using (result.AsyncWaitHandle) { if (!result.AsyncWaitHandle.WaitOne(timeout)) throw new IOException("IPC write timed out."); stream.EndWrite(result); }
        }
        private static string ReadPacket(Stream stream, int timeout)
        {
            byte[] header = ReadExactly(stream, 4, timeout); int length = BitConverter.ToInt32(header, 0);
            if (length < 0 || length > 8192) throw new IOException("Invalid IPC packet size.");
            return new UTF8Encoding(false, true).GetString(ReadExactly(stream, length, timeout));
        }
        private static byte[] ReadExactly(Stream stream, int length, int timeout)
        {
            byte[] data = new byte[length]; int offset = 0; Stopwatch watch = Stopwatch.StartNew();
            while (offset < length)
            {
                IAsyncResult result = stream.BeginRead(data, offset, length - offset, null, null);
                using (result.AsyncWaitHandle)
                {
                    int remaining = timeout - (int)watch.ElapsedMilliseconds;
                    if (remaining <= 0 || !result.AsyncWaitHandle.WaitOne(remaining)) throw new IOException("IPC read timed out.");
                    int read = stream.EndRead(result); if (read == 0) throw new EndOfStreamException(); offset += read;
                }
            }
            return data;
        }
        public void Dispose()
        {
            stopping = true; lock (gate) { if (server != null) server.Dispose(); }
            try { listener.Wait(2500); } catch { }
        }
    }
}
