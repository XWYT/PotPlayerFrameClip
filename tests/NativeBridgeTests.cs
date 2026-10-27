using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PotPlayerFrameClip
{
    // Separate real processes exercise Windows loader, hook ownership and the queue.
    internal static class NativeBridgeTestProgram
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, IntPtr proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetProp(IntPtr window, string name);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        [StructLayout(LayoutKind.Sequential)] private struct QueuedMessage { internal IntPtr Window; internal uint Message; internal UIntPtr WParam; internal IntPtr LParam; internal uint Time; internal int X, Y; internal uint Private; }
        [DllImport("user32.dll")] private static extern bool PeekMessage(out QueuedMessage message, IntPtr window, uint first, uint last, uint remove);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern bool SetProp(IntPtr window, string name, IntPtr value);
        [StructLayout(LayoutKind.Sequential)] private struct MouseData { internal int X, Y; internal IntPtr Window; internal uint HitTest; internal UIntPtr Extra; }
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);

        private sealed class TargetWindow : NativeWindow, IDisposable
        {
            internal int AboutCount;
            internal int ActionCount;
            internal int LastAction = -1;
            internal bool Closed;
            internal TargetWindow(string caption = "FrameClip isolated bridge target", int width = 600, int height = 400)
            {
                CreateHandle(new CreateParams { Caption = caption, X = -30000, Y = -30000, Width = width, Height = height, Style = unchecked((int)0x80000000) });
            }
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x111 && message.WParam.ToInt64() == 0xE140) AboutCount++;
                if (message.Msg == 0x84C0) { ActionCount++; LastAction = message.WParam.ToInt32(); }
                if (message.Msg == 0x10) { Closed = true; Application.ExitThread(); return; }
                base.WndProc(ref message);
            }
            public void Dispose() { DestroyHandle(); }
        }

        private static void InvokeMouse(MouseHook hook, TargetWindow window, int message, int y, int code = 0)
        {
            MouseData data = new MouseData { X = -29990, Y = -30000 + y, Window = window.Handle };
            IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MouseData)));
            try { Marshal.StructureToPtr(data, pointer, false); hook(code, new IntPtr(message), pointer); }
            finally { Marshal.FreeHGlobal(pointer); }
        }

        private static void VerifyMenuActions(IntPtr module)
        {
            MouseHook hook = (MouseHook)Marshal.GetDelegateForFunctionPointer(GetProcAddress(module, "FrameClipMouseProc"), typeof(MouseHook));
            using (TargetWindow main = new TargetWindow())
            using (TargetWindow root = new TargetWindow(width: 350, height: 600))
            using (TargetWindow menu = new TargetWindow(width: 350, height: 226))
            using (TargetWindow receiver = new TargetWindow("PotPlayerFrameClip.NativeBridge"))
            {
                // Offscreen windows, no real input or interaction with the installed player.
                ShowWindow(root.Handle, 4); ShowWindow(menu.Handle, 4);
                int[] centers = { 12, 41, 65, 89, 113, 142, 166, 190, 214 };
                int checks = 0;
                foreach (int height in new[] { 226, 339, 452 })
                {
                    SetWindowPos(menu.Handle, IntPtr.Zero, -30000, -30000, 350, height, 0x14);
                    for (int action = 0; action < centers.Length; action++)
                    {
                        InvokeMouse(hook, main, 0x204, 10);
                        SetProp(main.Handle, "PotPlayerFrameClip.NativeBridge.Owner.v1", new IntPtr(Process.GetCurrentProcess().Id));
                        InvokeMouse(hook, root, 0x200, 10);
                        InvokeMouse(hook, menu, 0x200, 10);
                        int y = centers[action] * height / 226;
                        // HC_NOREMOVE must not duplicate state transitions.
                        InvokeMouse(hook, menu, 0x201, y, 3);
                        InvokeMouse(hook, menu, 0x201, y);
                        SendMessage(main.Handle, 0x111, new IntPtr(0x1234), IntPtr.Zero);
                        int about = main.AboutCount, received = receiver.ActionCount;
                        SendMessage(main.Handle, 0x111, new IntPtr(0xE140), IntPtr.Zero);
                        Application.DoEvents();
                        if (main.AboutCount != about || receiver.ActionCount != received + 1 || receiver.LastAction != action)
                            throw new InvalidOperationException("Menu action failed height=" + height + " action=" + action + " about=" + main.AboutCount + "/" + about + " received=" + receiver.ActionCount + "/" + received + " last=" + receiver.LastAction);
                        // A second unarmed About command must still belong to PotPlayer.
                        SendMessage(main.Handle, 0x111, new IntPtr(0xE140), IntPtr.Zero);
                        if (main.AboutCount != about + 1) throw new InvalidOperationException("Ordinary About command swallowed");
                        checks++;
                    }
                }
                foreach (int y in new[] { 26, 127 })
                {
                    SetWindowPos(menu.Handle, IntPtr.Zero, -30000, -30000, 350, 226, 0x14);
                    InvokeMouse(hook, main, 0x204, 10);
                    InvokeMouse(hook, root, 0x200, 10);
                    InvokeMouse(hook, menu, 0x200, 10);
                    InvokeMouse(hook, menu, 0x201, y);
                    int before = receiver.ActionCount, about = main.AboutCount;
                    SendMessage(main.Handle, 0x111, new IntPtr(0xE140), IntPtr.Zero);
                    Application.DoEvents();
                    if (receiver.ActionCount != before || main.AboutCount != about + 1)
                        throw new InvalidOperationException("Separator click armed an action");
                }
                InvokeMouse(hook, main, 0x204, 10);
                InvokeMouse(hook, root, 0x200, 10); InvokeMouse(hook, menu, 0x200, 10);
                InvokeMouse(hook, menu, 0x201, 166);
                Thread.Sleep(1100);
                int expired = receiver.ActionCount;
                SendMessage(main.Handle, 0x111, new IntPtr(0xE140), IntPtr.Zero); Application.DoEvents();
                if (receiver.ActionCount != expired) throw new InvalidOperationException("Expired selection dispatched");
                InvokeMouse(hook, main, 0x204, 10);
                InvokeMouse(hook, root, 0x200, 10); InvokeMouse(hook, menu, 0x200, 10);
                Thread.Sleep(10500);
                int idleBefore = receiver.ActionCount;
                InvokeMouse(hook, menu, 0x201, 166);
                SendMessage(main.Handle, 0x111, new IntPtr(0xE140), IntPtr.Zero); Application.DoEvents();
                if (receiver.ActionCount != idleBefore + 1 || receiver.LastAction != 6)
                    throw new InvalidOperationException("Open menu expired while user was deciding");
                Console.WriteLine("Open menu survives10.5 seconds idle");
                Stopwatch idle = Stopwatch.StartNew();
                for (int i = 0; i < 100000; i++) InvokeMouse(hook, main, 0x202, 10);
                Console.WriteLine("Ignored100000 mouse events in " + idle.ElapsedMilliseconds + "ms including test marshalling");
                Console.WriteLine("Native menu actions passed: " + checks + " mappings at100/150/200 percent");
            }
        }

        [STAThread] public static int Main(string[] args)
        {
            TestDesktop.Initialize();
            if (args.Length == 2 && (args[0] == "--bridge-target" || args[0] == "--bridge-target-filtered"))
            {
                using (TargetWindow window = new TargetWindow())
                using (System.Windows.Forms.Timer deadline = new System.Windows.Forms.Timer { Interval = 25000 })
                {
                    File.WriteAllText(args[1], window.Handle.ToInt64().ToString(CultureInfo.InvariantCulture));
                    deadline.Tick += delegate { Application.ExitThread(); };
                    if (args[0] == "--bridge-target-filtered")
                    {
                        // Model a player loop that services sent messages but never
                        // retrieves our registered queued initialization message.
                        Stopwatch limit = Stopwatch.StartNew();
                        while (!window.Closed && limit.ElapsedMilliseconds < 25000)
                        { QueuedMessage message; PeekMessage(out message, IntPtr.Zero, 0x113, 0x113, 1); Thread.Sleep(5); }
                    }
                    else { deadline.Start(); Application.Run(); }
                }
                return 0;
            }
            if (args.Length != 2) return 2;
            int normal = VerifyInjection(args, false);
            return normal == 0 ? VerifyInjection(args, true) : normal;
        }

        private static int VerifyInjection(string[] args, bool filtered)
        {
            string signal = Path.Combine(args[1], Guid.NewGuid().ToString("N") + ".hwnd");
            Process child = null; IntPtr module = IntPtr.Zero, mouse = IntPtr.Zero, bootstrap = IntPtr.Zero;
            try
            {
                child = Process.Start(new ProcessStartInfo(Application.ExecutablePath, (filtered ? "--bridge-target-filtered" : "--bridge-target") + " \"" + signal + "\"") { UseShellExecute = false, CreateNoWindow = true });
                Stopwatch timer = Stopwatch.StartNew();
                while (!File.Exists(signal) && timer.ElapsedMilliseconds < 10000) { Application.DoEvents(); Thread.Sleep(10); }
                if (!File.Exists(signal)) throw new InvalidOperationException("Target did not start");
                IntPtr window = new IntPtr(Int64.Parse(File.ReadAllText(signal), CultureInfo.InvariantCulture));
                uint pid; uint thread = GetWindowThreadProcessId(window, out pid);
                if (pid != child.Id) throw new InvalidOperationException("Target identity mismatch");
                module = LoadLibrary(args[0]);
                if (module == IntPtr.Zero) throw new InvalidOperationException("DLL load failed");
                uint initialize = RegisterWindowMessage("PotPlayerFrameClip.NativeBridge.Initialize.v1");
                IntPtr owner = new IntPtr(Process.GetCurrentProcess().Id);
                if (filtered)
                {
                    IntPtr queued = SetWindowsHookEx(3, GetProcAddress(module, "FrameClipGetMessageProc"), module, thread);
                    if (queued == IntPtr.Zero) throw new InvalidOperationException("Queued baseline hook failed");
                    try
                    {
                        PostMessage(window, initialize, owner, IntPtr.Zero);
                        Stopwatch queuedWait = Stopwatch.StartNew();
                        while (queuedWait.ElapsedMilliseconds < 300) { Application.DoEvents(); Thread.Sleep(10); }
                        if (GetProp(window, "PotPlayerFrameClip.NativeBridge.Owner.v1") != IntPtr.Zero)
                            throw new InvalidOperationException("Filtered target unexpectedly retrieved queued initialization");
                        Console.WriteLine("Filtered loop reproduces queued initialization failure");
                    }
                    finally { UnhookWindowsHookEx(queued); }
                }
                mouse = SetWindowsHookEx(7, GetProcAddress(module, "FrameClipMouseProc"), module, thread);
                bootstrap = SetWindowsHookEx(4, GetProcAddress(module, "FrameClipCallWndProc"), module, thread);
                if (mouse == IntPtr.Zero || bootstrap == IntPtr.Zero) throw new InvalidOperationException("Hook setup failed: " + Marshal.GetLastWin32Error());
                IntPtr result;
                if (SendMessageTimeout(window, initialize, owner, IntPtr.Zero, 3, 150, out result) == IntPtr.Zero)
                    throw new InvalidOperationException("Sent initialization failed: " + Marshal.GetLastWin32Error());
                timer.Restart();
                while (GetProp(window, "PotPlayerFrameClip.NativeBridge.Owner.v1") != owner && timer.ElapsedMilliseconds < 6000)
                { Application.DoEvents(); Thread.Sleep(10); }
                if (GetProp(window, "PotPlayerFrameClip.NativeBridge.Owner.v1") != owner)
                    throw new InvalidOperationException("Hook returned a handle but target handshake failed");
                IntPtr rebound = new IntPtr(owner.ToInt64() + 1);
                SendMessageTimeout(window, initialize, rebound, IntPtr.Zero, 3, 150, out result);
                timer.Restart();
                while (GetProp(window, "PotPlayerFrameClip.NativeBridge.Owner.v1") != rebound && timer.ElapsedMilliseconds < 3000)
                { Application.DoEvents(); Thread.Sleep(10); }
                if (GetProp(window, "PotPlayerFrameClip.NativeBridge.Owner.v1") != rebound)
                    throw new InvalidOperationException("Existing subclass did not rebind to restarted owner");
                SendMessageTimeout(window, initialize, owner, IntPtr.Zero, 3, 150, out result);
                timer.Restart();
                while (GetProp(window, "PotPlayerFrameClip.NativeBridge.Owner.v1") != owner && timer.ElapsedMilliseconds < 3000)
                { Application.DoEvents(); Thread.Sleep(10); }
                if (GetProp(window, "PotPlayerFrameClip.NativeBridge.Owner.v1") != owner)
                    throw new InvalidOperationException("Owner rebind restore failed");
                UnhookWindowsHookEx(bootstrap); bootstrap = IntPtr.Zero;
                Console.WriteLine((Environment.Is64BitProcess ? "x64" : "x86") + (filtered ? " filtered-loop" : " normal-loop") + " injected handshake passed");
                SendMessageTimeout(window, 0x10, IntPtr.Zero, IntPtr.Zero, 3, 1000, out result);
                if (!child.WaitForExit(5000) || child.ExitCode != 0) throw new InvalidOperationException("Target shutdown failed");
                VerifyMenuActions(module);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                string log = Path.Combine(args[1], "bridge-debug.log");
                if (File.Exists(log)) Console.Error.WriteLine(File.ReadAllText(log, System.Text.Encoding.Unicode));
                return 1;
            }
            finally
            {
                if (bootstrap != IntPtr.Zero) UnhookWindowsHookEx(bootstrap);
                if (mouse != IntPtr.Zero) UnhookWindowsHookEx(mouse);
                if (module != IntPtr.Zero) FreeLibrary(module);
                if (child != null) { if (!child.HasExited) child.Kill(); child.Dispose(); }
                if (File.Exists(signal)) File.Delete(signal);
            }
        }
    }
}
