using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PotPlayerFrameClip
{
    // Tests may maximize windows or open popups. Offscreen coordinates alone do
    // not isolate those operations: USER32 can move them back onto a monitor.
    internal static class TestDesktop
    {
        private static bool initialized;
        internal static void Initialize()
        {
            if (initialized) return;
            const string key = "FRAMECLIP_TEST_DESKTOP";
            string name = Environment.GetEnvironmentVariable(key);
            if (!String.IsNullOrEmpty(name))
            {
                AssertIsolated(); initialized = true; return;
            }
            name = "FrameClipTests-" + Guid.NewGuid().ToString("N");
            IntPtr handle = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, 0x01ff, IntPtr.Zero);
            if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot create isolated test desktop");
            ProcessInfo process = new ProcessInfo();
            int exitCode;
            try
            {
                // STA/CLR startup may already own hidden windows. Choose the
                // desktop at process creation, before CLR initializes USER32.
                StartupInfo startup = new StartupInfo();
                startup.Size = Marshal.SizeOf(typeof(StartupInfo)); startup.Desktop = name;
                Environment.SetEnvironmentVariable(key, name);
                if (!CreateProcess(null, new StringBuilder(Environment.CommandLine), IntPtr.Zero, IntPtr.Zero, true, 0, IntPtr.Zero, null, ref startup, out process))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot start isolated UI tests; visible fallback is forbidden");
                if (WaitForSingleObject(process.Process, 1800000) != 0)
                {
                    TerminateProcess(process.Process, 124); WaitForSingleObject(process.Process, 10000);
                    throw new TimeoutException("Isolated UI test process timed out");
                }
                uint result;
                if (!GetExitCodeProcess(process.Process, out result)) throw new Win32Exception(Marshal.GetLastWin32Error());
                exitCode = unchecked((int)result);
            }
            finally
            {
                Environment.SetEnvironmentVariable(key, null);
                if (process.Thread != IntPtr.Zero) CloseHandle(process.Thread);
                if (process.Process != IntPtr.Zero) CloseHandle(process.Process);
                CloseDesktop(handle);
            }
            Environment.Exit(exitCode);
        }
        internal static void AssertIsolated()
        {
            IntPtr input = OpenInputDesktop(0, false, 1);
            if (input == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot verify UI test isolation");
            try
            {
                string current = Name(GetThreadDesktop(GetCurrentThreadId()));
                if (current == Name(input) || current != Environment.GetEnvironmentVariable("FRAMECLIP_TEST_DESKTOP") || !current.StartsWith("FrameClipTests-", StringComparison.Ordinal))
                    throw new InvalidOperationException("UI tests cannot run on the visible input desktop");
                Console.WriteLine("UI tests isolated on non-input desktop: " + current);
            }
            finally { CloseDesktop(input); }
        }
        private static string Name(IntPtr handle)
        {
            StringBuilder buffer = new StringBuilder(256); int needed;
            if (!GetUserObjectInformation(handle, 2, buffer, buffer.Capacity * 2, out needed))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot read test desktop identity");
            return buffer.ToString();
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr attributes);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            internal int Size; internal string Reserved, Desktop, Title;
            internal int X, Y, Width, Height, CharsX, CharsY, Fill, Flags;
            internal short Show, ReservedSize; internal IntPtr ReservedData, Input, Output, Error;
        }
        [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo
        { internal IntPtr Process, Thread; internal uint Id, ThreadId; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcess(string app, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo process);
        [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint timeout);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
        [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr process, uint code);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseDesktop(IntPtr desktop);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint thread);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, int length, out int needed);
    }
}
