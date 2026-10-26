using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PotPlayerFrameClip
{
    internal static class PlayerProcesses
    {
        internal static Process[] Get(bool only64)
        {
            List<Process> result = new List<Process>();
            IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
            if (snapshot == new IntPtr(-1))
            {
                foreach (Process process in Process.GetProcesses())
                {
                    try { if (Matches(process.ProcessName, only64)) { result.Add(process); continue; } } catch { }
                    process.Dispose();
                }
                return result.ToArray();
            }
            try
            {
                ProcessEntry entry = new ProcessEntry { Size = (uint)Marshal.SizeOf(typeof(ProcessEntry)) };
                if (Process32First(snapshot, ref entry))
                    do
                    {
                        if (!Matches(entry.Executable, only64)) continue;
                        try { result.Add(Process.GetProcessById((int)entry.ProcessId)); } catch (ArgumentException) { }
                    } while (Process32Next(snapshot, ref entry));
            }
            finally { CloseHandle(snapshot); }
            return result.ToArray();
        }
        private static bool Matches(string name, bool only64)
        {
            return name.IndexOf("PotPlayer", StringComparison.OrdinalIgnoreCase) >= 0 &&
                name.IndexOf("FrameClip", StringComparison.OrdinalIgnoreCase) < 0 && (!only64 || name.IndexOf("64", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct ProcessEntry
        {
            internal uint Size, Usage, ProcessId;
            internal UIntPtr DefaultHeap;
            internal uint ModuleId, Threads, ParentId;
            internal int Priority;
            internal uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Executable;
        }
        [DllImport("kernel32.dll")] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint id);
        [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
