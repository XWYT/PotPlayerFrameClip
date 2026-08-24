using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

[assembly: AssemblyTitle("PotPlayer FrameClip")]
[assembly: AssemblyDescription("Frame and source clip capture extension for PotPlayer")]
[assembly: AssemblyProduct("PotPlayer FrameClip")]
[assembly: AssemblyCopyright("Copyright (c) 2026 PotPlayer FrameClip contributors")]
[assembly: AssemblyVersion("0.3.3.0")]
[assembly: AssemblyFileVersion("0.3.3.0")]

namespace PotPlayerFrameClip
{
    internal static class AppPaths
    {
        internal const string ProductName = "PotPlayer FrameClip";
        internal static string DataDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PotPlayerFrameClip");
            }
        }

        internal static string ConfigPath
        {
            get { return Path.Combine(DataDirectory, "FrameClip.ini"); }
        }

        internal static string PendingMenuRepairPath
        {
            get { return Path.Combine(DataDirectory, "menu-selection.pending"); }
        }

        internal static string DefaultOutputDirectory
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                if (String.IsNullOrWhiteSpace(root)) root = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                return Path.Combine(root, "FrameClip");
            }
        }

        internal static IEnumerable<string> LegacyConfigCandidates()
        {
            string legacyName = "PotPlayer" + "\u0052esolveCapture";
            yield return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, legacyName + ".ini");
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                legacyName,
                legacyName + ".ini");

            string command = null;
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    command = run == null ? null : run.GetValue(legacyName) as string;
                }
            }
            catch { }
            string executable = ExtractExecutablePath(command);
            if (!String.IsNullOrEmpty(executable))
                yield return Path.Combine(Path.GetDirectoryName(executable), legacyName + ".ini");
        }

        internal static void MigrateLegacyData()
        {
            string legacyName = "PotPlayer" + "\u0052esolveCapture";
            string legacyDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                legacyName);
            if (!Directory.Exists(legacyDirectory)) return;

            Directory.CreateDirectory(DataDirectory);
            CopyIfMissing(Path.Combine(legacyDirectory, "range.state"), Path.Combine(DataDirectory, "range.state"));
            CopyIfMissing(Path.Combine(legacyDirectory, "library-aliases.tsv"), Path.Combine(DataDirectory, "library-aliases.tsv"));
        }

        private static void CopyIfMissing(string source, string destination)
        {
            if (File.Exists(source) && !File.Exists(destination)) File.Copy(source, destination, false);
        }

        private static string ExtractExecutablePath(string command)
        {
            if (String.IsNullOrWhiteSpace(command)) return null;
            command = command.Trim();
            if (command.StartsWith("\"", StringComparison.Ordinal))
            {
                int closingQuote = command.IndexOf('"', 1);
                return closingQuote > 1 ? command.Substring(1, closingQuote - 1) : null;
            }
            int separator = command.IndexOf(' ');
            return separator > 0 ? command.Substring(0, separator) : command;
        }
    }

    internal static class ToolLocator
    {
        internal static string Find(string configuredPath, string executableName)
        {
            if (!String.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
                return Path.GetFullPath(configuredPath);

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new[]
            {
                Path.Combine(baseDirectory, executableName),
                Path.Combine(baseDirectory, "tools", executableName),
                Path.Combine(baseDirectory, "tools", "ffmpeg", "bin", executableName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FFmpeg", executableName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FFmpeg", "bin", executableName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "FFmpeg", executableName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "FFmpeg", "bin", executableName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links", executableName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps", "ffmpeg", "current", "bin", executableName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey", "bin", executableName)
            };
            foreach (string candidate in candidates)
            {
                if (!String.IsNullOrWhiteSpace(candidate) && File.Exists(candidate)) return candidate;
            }

            string path = Environment.GetEnvironmentVariable("PATH") ?? String.Empty;
            foreach (string directory in path.Split(Path.PathSeparator))
            {
                string cleanDirectory = directory.Trim().Trim('"');
                if (cleanDirectory.Length == 0) continue;
                string candidate;
                try { candidate = Path.Combine(cleanDirectory, executableName); }
                catch { continue; }
                if (File.Exists(candidate)) return candidate;
            }
            return configuredPath ?? String.Empty;
        }
    }

    internal static class PendingMenuRepair
    {
        // PotPlayer 退出时可能把内存中的旧菜单选择重新写回配置。安装器留下一个很小的
        // 待处理文件，常驻程序确认播放器完全退出后再回写一次，避免为了安装中断播放。
        private static readonly string[] PotPlayerProcessNames =
            new[] { "PotPlayerMini64", "PotPlayer64", "PotPlayerMini", "PotPlayer" };

        internal static bool Exists
        {
            get { return File.Exists(AppPaths.PendingMenuRepairPath); }
        }

        internal static bool TryApplyIfPlayerStopped()
        {
            if (!Exists || IsPotPlayerRunning()) return false;
            try
            {
                Dictionary<string, string> values = ReadPendingValues(AppPaths.PendingMenuRepairPath);
                string mode;
                string encodedPath;
                string menuName;
                if (!values.TryGetValue("Mode", out mode) || !values.TryGetValue("Path", out encodedPath) ||
                    !values.TryGetValue("Value", out menuName)) return false;
                string configurationPath = Encoding.UTF8.GetString(Convert.FromBase64String(encodedPath));
                bool applied = mode.Equals("Ini", StringComparison.OrdinalIgnoreCase)
                    ? SetIniValue(configurationPath, "Settings", "LastMenuName", menuName)
                    : SetRegistryValue(configurationPath, "LastMenuName", menuName);
                if (applied) File.Delete(AppPaths.PendingMenuRepairPath);
                return applied;
            }
            catch
            {
                return false;
            }
        }

        internal static bool SetIniValue(string path, string section, string key, string value)
        {
            if (!File.Exists(path)) return false;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                Encoding encoding = DetectEncoding(bytes);
                List<string> lines = new List<string>(File.ReadAllLines(path, encoding));
                int sectionIndex = -1;
                int nextSectionIndex = lines.Count;
                for (int index = 0; index < lines.Count; index++)
                {
                    if (!lines[index].StartsWith("[", StringComparison.Ordinal)) continue;
                    if (sectionIndex >= 0)
                    {
                        nextSectionIndex = index;
                        break;
                    }
                    if (lines[index].Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase)) sectionIndex = index;
                }
                if (sectionIndex < 0)
                {
                    if (lines.Count > 0 && lines[lines.Count - 1].Length > 0) lines.Add(String.Empty);
                    lines.Add("[" + section + "]");
                    lines.Add(key + "=" + value);
                }
                else
                {
                    bool replaced = false;
                    for (int index = sectionIndex + 1; index < nextSectionIndex; index++)
                    {
                        if (!lines[index].StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) continue;
                        lines[index] = key + "=" + value;
                        replaced = true;
                        break;
                    }
                    if (!replaced) lines.Insert(nextSectionIndex, key + "=" + value);
                }
                File.WriteAllLines(path, lines.ToArray(), encoding);
                return true;
            }
            catch { return false; }
        }

        private static Dictionary<string, string> ReadPendingValues(string path)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int separator = line.IndexOf('=');
                if (separator > 0) values[line.Substring(0, separator)] = line.Substring(separator + 1);
            }
            return values;
        }

        private static bool SetRegistryValue(string path, string name, string value)
        {
            string prefix = "HKCU:" + Path.DirectorySeparatorChar;
            if (String.IsNullOrWhiteSpace(path) || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(path.Substring(prefix.Length)))
                {
                    if (key == null) return false;
                    key.SetValue(name, value, RegistryValueKind.String);
                    return true;
                }
            }
            catch { return false; }
        }

        private static bool IsPotPlayerRunning()
        {
            foreach (string processName in PotPlayerProcessNames)
            {
                try
                {
                    if (Process.GetProcessesByName(processName).Length > 0) return true;
                }
                catch { }
            }
            return false;
        }

        private static Encoding DetectEncoding(byte[] bytes)
        {
            if (bytes != null && bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode;
            if (bytes != null && bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode;
            if (bytes != null && bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(true);
            if (bytes != null && bytes.Length >= 4)
            {
                int sampleLength = Math.Min(bytes.Length, 512);
                int oddNulls = 0;
                for (int index = 1; index < sampleLength; index += 2)
                    if (bytes[index] == 0) oddNulls++;
                if (oddNulls >= Math.Max(2, sampleLength / 8)) return new UnicodeEncoding(false, false);
            }
            try
            {
                new UTF8Encoding(false, true).GetString(bytes ?? new byte[0]);
                return new UTF8Encoding(false);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.Default;
            }
        }
    }

    internal static class NativeMethods
    {
        internal const int WH_MOUSE_LL = 14;
        internal const int WH_MOUSE = 7;
        internal const int WM_MOUSEMOVE = 0x0200;
        internal const int WM_RBUTTONDOWN = 0x0204;
        internal const int WM_RBUTTONUP = 0x0205;
        internal const int WM_LBUTTONDOWN = 0x0201;
        internal const int WM_LBUTTONUP = 0x0202;
        internal const int WM_MBUTTONDOWN = 0x0207;
        internal const int WM_USER = 0x0400;
        internal const int WM_CANCELMODE = 0x001F;
        internal const int MN_GETHMENU = 0x01E1;
        internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        internal const uint EVENT_SYSTEM_MENUSTART = 0x0004;
        internal const uint EVENT_SYSTEM_MENUEND = 0x0005;
        internal const uint EVENT_SYSTEM_MENUPOPUPSTART = 0x0006;
        internal const uint EVENT_SYSTEM_MENUPOPUPEND = 0x0007;
        internal const uint EVENT_OBJECT_SHOW = 0x8002;
        internal const uint EVENT_OBJECT_HIDE = 0x8003;
        internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        internal const uint MIIM_ID = 0x00000002;
        internal const uint MIIM_SUBMENU = 0x00000004;
        internal const uint MIIM_STRING = 0x00000040;
        internal const uint MFT_SEPARATOR = 0x00000800;
        internal const uint MFT_STRING = 0x00000000;
        internal const uint MF_BYCOMMAND = 0x00000000;
        internal const uint MF_BYPOSITION = 0x00000400;
        internal const uint MF_STRING = 0x00000000;
        internal const uint MF_SEPARATOR = 0x00000800;
        internal const uint GA_ROOT = 2;
        internal const uint GW_HWNDNEXT = 2;
        internal const uint MenuMissing = 0xFFFFFFFF;

        internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        internal delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime);
        internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            internal int X;
            internal int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;

            internal RECT(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }

            internal bool Contains(POINT point)
            {
                return point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MSLLHOOKSTRUCT
        {
            internal POINT Point;
            internal uint MouseData;
            internal uint Flags;
            internal uint Time;
            internal UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MENUITEMINFO
        {
            internal uint cbSize;
            internal uint fMask;
            internal uint fType;
            internal uint fState;
            internal uint wID;
            internal IntPtr hSubMenu;
            internal IntPtr hbmpChecked;
            internal IntPtr hbmpUnchecked;
            internal UIntPtr dwItemData;
            [MarshalAs(UnmanagedType.LPWStr)]
            internal string dwTypeData;
            internal uint cch;
            internal IntPtr hbmpItem;
        }

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int hookId, LowLevelMouseProc callback, IntPtr module, uint threadId);

        [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookExNative(int hookId, IntPtr callback, IntPtr module, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventDelegate callback, uint processId, uint threadId, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWinEvent(IntPtr hook);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr hwnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        internal static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool InsertMenuItem(IntPtr menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition, ref MENUITEMINFO info);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr itemId, string text);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetSubMenu(IntPtr menu, int position);

        [DllImport("user32.dll")]
        internal static extern uint GetMenuState(IntPtr menu, uint item, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int maxCount, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMenuItemRect(IntPtr hwnd, IntPtr menu, uint item, out RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DrawMenuBar(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr LoadLibrary(string path);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        internal static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FreeLibrary(IntPtr module);
    }

    internal enum CaptureAction
    {
        CaptureFrame,
        MarkIn,
        MarkOut,
        ExportOriginal,
        ExportPrecise,
        ClearRange,
        Settings,
        OpenImageOutput,
        OpenVideoOutput
    }

    internal static class UiText
    {
        internal const string Chinese = "zh-CN";
        internal const string English = "en-US";

        internal static string NormalizeLanguage(string value)
        {
            if (String.Equals(value, English, StringComparison.OrdinalIgnoreCase) ||
                String.Equals(value, "en", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(value, "English", StringComparison.OrdinalIgnoreCase)) return English;
            return Chinese;
        }

        internal static bool IsEnglish(string language)
        {
            return NormalizeLanguage(language) == English;
        }

        internal static string Choose(string language, string chinese, string english)
        {
            return IsEnglish(language) ? english : chinese;
        }

        internal static string MenuTitle(string language)
        {
            return Choose(language, "参照帧与片段截取", "Reference Frame & Clip Capture");
        }

        internal static string ActionLabel(string language, CaptureAction action)
        {
            bool english = IsEnglish(language);
            if (action == CaptureAction.CaptureFrame) return english
                ? "Capture current frame (automatic color detection · 16-bit)"
                : "截取当前帧（自动识别色彩 · 16-bit）";
            if (action == CaptureAction.MarkIn) return english ? "Set in point" : "设置入点";
            if (action == CaptureAction.MarkOut) return english ? "Set out point" : "设置出点";
            if (action == CaptureAction.ExportOriginal) return english
                ? "Export source clip (source color/DV + original audio)"
                : "导出原码片段（保留源色彩/DV + 原音频）";
            if (action == CaptureAction.ExportPrecise) return english
                ? "Export precise clip (selectable codec + PCM)"
                : "导出精确片段（可选编码 + PCM）";
            if (action == CaptureAction.ClearRange) return english ? "Clear in and out points" : "清除入点和出点";
            if (action == CaptureAction.Settings) return english ? "Settings…" : "设置…";
            if (action == CaptureAction.OpenImageOutput) return english ? "Open current title image folder" : "打开当前作品图片文件夹";
            if (action == CaptureAction.OpenVideoOutput) return english ? "Open current title video folder" : "打开当前作品视频文件夹";
            return action.ToString();
        }

        internal static bool TryMapActionLabel(string title, out CaptureAction action)
        {
            action = CaptureAction.CaptureFrame;
            foreach (CaptureAction candidate in Enum.GetValues(typeof(CaptureAction)))
            {
                if (title.StartsWith(ActionLabel(Chinese, candidate), StringComparison.Ordinal) ||
                    title.StartsWith(ActionLabel(English, candidate), StringComparison.Ordinal))
                {
                    action = candidate;
                    return true;
                }
            }
            if (title.StartsWith("打开截图文件夹", StringComparison.Ordinal)) { action = CaptureAction.OpenImageOutput; return true; }
            if (title.StartsWith("打开视频文件夹", StringComparison.Ordinal)) { action = CaptureAction.OpenVideoOutput; return true; }
            return false;
        }

        internal static FormatChoice[] ImageChoices(string language)
        {
            return new[]
            {
                new FormatChoice("png16", Choose(language,
                    "PNG · 16-bit RGB（推荐，可写入色彩标签）",
                    "PNG · 16-bit RGB (recommended, supports color tags)")),
                new FormatChoice("tiff16", Choose(language,
                    "TIFF · 16-bit RGB（无损，需手动指定输入色彩空间）",
                    "TIFF · 16-bit RGB (lossless, assign the input color space manually)"))
            };
        }

        internal static FormatChoice[] VideoChoices(string language)
        {
            return new[]
            {
                new FormatChoice("prores422hq", Choose(language, "Apple ProRes 422 HQ · 10-bit（推荐）", "Apple ProRes 422 HQ · 10-bit (recommended)")),
                new FormatChoice("prores4444", Choose(language, "Apple ProRes 4444 · 10-bit 4:4:4 输入", "Apple ProRes 4444 · 10-bit 4:4:4 input")),
                new FormatChoice("prores4444xq", Choose(language, "Apple ProRes 4444 XQ · 10-bit 4:4:4 输入", "Apple ProRes 4444 XQ · 10-bit 4:4:4 input")),
                new FormatChoice("dnxhrhqx", "Avid DNxHR HQX · 10-bit 4:2:2"),
                new FormatChoice("dnxhr444", "Avid DNxHR 444 · 10-bit 4:4:4")
            };
        }

        internal static FormatChoice[] LanguageChoices()
        {
            return new[]
            {
                new FormatChoice(Chinese, "简体中文"),
                new FormatChoice(English, "English")
            };
        }
    }

    internal static class MenuLocalization
    {
        private static readonly CaptureAction[] MenuActions = new[]
        {
            CaptureAction.CaptureFrame, CaptureAction.MarkIn, CaptureAction.MarkOut,
            CaptureAction.ExportOriginal, CaptureAction.ExportPrecise, CaptureAction.ClearRange,
            CaptureAction.Settings, CaptureAction.OpenImageOutput, CaptureAction.OpenVideoOutput
        };

        internal static bool Apply(AppConfig config)
        {
            if (config == null || String.IsNullOrWhiteSpace(config.PotPlayerMenuPath) || !File.Exists(config.PotPlayerMenuPath)) return false;
            try
            {
                return ApplyToFile(config.PotPlayerMenuPath, config.Language);
            }
            catch (UnauthorizedAccessException)
            {
                return ApplyElevated(config.PotPlayerMenuPath, config.Language);
            }
            catch { return false; }
        }

        internal static bool ApplyToFile(string path, string language)
        {
            XmlDocument document = new XmlDocument();
            document.PreserveWhitespace = true;
            document.Load(path);
            XmlElement target = null;
            foreach (XmlNode node in document.SelectNodes("/Menu/SubMenu"))
            {
                XmlElement submenu = node as XmlElement;
                if (submenu == null) continue;
                string name = submenu.GetAttribute("Name");
                XmlElement firstCommand = submenu.SelectSingleNode("MenuItem[@CmdID != '']") as XmlElement;
                string firstName = firstCommand == null ? String.Empty : firstCommand.GetAttribute("Name");
                CaptureAction mapped;
                if (name == UiText.MenuTitle(UiText.Chinese) || name == UiText.MenuTitle(UiText.English) ||
                    name == "帧与片段" || UiText.TryMapActionLabel(firstName, out mapped))
                {
                    target = submenu;
                    break;
                }
            }
            if (target == null) return false;

            target.SetAttribute("Name", UiText.MenuTitle(language));
            List<XmlElement> commands = new List<XmlElement>();
            foreach (XmlNode node in target.SelectNodes("MenuItem[@CmdID != '']"))
            {
                XmlElement element = node as XmlElement;
                if (element != null) commands.Add(element);
            }
            if (commands.Count != MenuActions.Length) return false;

            string normalizedLanguage = UiText.NormalizeLanguage(language);
            bool changed = target.GetAttribute("Name") != UiText.MenuTitle(normalizedLanguage);
            for (int index = 0; index < MenuActions.Length; index++)
            {
                string label = UiText.ActionLabel(normalizedLanguage, MenuActions[index]);
                if (commands[index].GetAttribute("Name") != label) changed = true;
            }
            if (!changed) return true;

            target.SetAttribute("Name", UiText.MenuTitle(normalizedLanguage));
            for (int index = 0; index < MenuActions.Length; index++)
                commands[index].SetAttribute("Name", UiText.ActionLabel(normalizedLanguage, MenuActions[index]));

            string temporary = path + ".language.tmp";
            string backup = path + ".language.bak";
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Encoding = new UTF8Encoding(false);
            settings.Indent = true;
            using (XmlWriter writer = XmlWriter.Create(temporary, settings)) document.Save(writer);
            bool completed = false;
            try
            {
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(temporary, path, backup, true);
                    }
                    catch (IOException)
                    {
                        File.Copy(path, backup, true);
                        try { File.Copy(temporary, path, true); }
                        catch
                        {
                            File.Copy(backup, path, true);
                            throw;
                        }
                    }
                }
                else File.Move(temporary, path);
                completed = true;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                if (completed && File.Exists(backup)) File.Delete(backup);
            }
            return true;
        }

        private static bool ApplyElevated(string path, string language)
        {
            try
            {
                ProcessStartInfo start = new ProcessStartInfo();
                start.FileName = Application.ExecutablePath;
                start.Arguments = "--apply-menu-language " + QuoteArgument(language) + " " + QuoteArgument(path);
                start.UseShellExecute = true;
                start.Verb = "runas";
                using (Process process = Process.Start(start))
                {
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? String.Empty).Replace("\"", "\\\"") + "\"";
        }
    }

    internal sealed class AppConfig
    {
        internal string LibraryRootDirectory = AppPaths.DefaultOutputDirectory;
        internal string ImageFormat = "png16";
        internal string VideoPreset = "prores422hq";
        internal string FfmpegPath = String.Empty;
        internal string FfprobePath = String.Empty;
        internal bool ExportRec709ForHdr;
        internal string Language = UiText.Chinese;
        internal string PotPlayerMenuPath = String.Empty;

        internal static string ConfigPath
        {
            get { return AppPaths.ConfigPath; }
        }

        internal static AppConfig Load()
        {
            AppConfig config = new AppConfig();
            string path = ConfigPath;
            bool legacyConfig = false;
            if (!File.Exists(path))
            {
                foreach (string candidate in AppPaths.LegacyConfigCandidates())
                {
                    if (!File.Exists(candidate)) continue;
                    path = candidate;
                    legacyConfig = true;
                    break;
                }
            }
            if (!File.Exists(path))
            {
                config.LocateTools();
                return config;
            }

            foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                int separator = line.IndexOf('=');
                if (separator <= 0) continue;
                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();
                if (key.Equals("OutputDirectory", StringComparison.OrdinalIgnoreCase))
                {
                    config.LibraryRootDirectory = value;
                }
                if (key.Equals("LibraryRootDirectory", StringComparison.OrdinalIgnoreCase)) config.LibraryRootDirectory = value;
                if (key.Equals("ImageOutputDirectory", StringComparison.OrdinalIgnoreCase)) config.LibraryRootDirectory = value;
                if (key.Equals("ImageFormat", StringComparison.OrdinalIgnoreCase)) config.ImageFormat = value;
                if (key.Equals("VideoPreset", StringComparison.OrdinalIgnoreCase)) config.VideoPreset = value;
                if (key.Equals("FFmpeg", StringComparison.OrdinalIgnoreCase)) config.FfmpegPath = value;
                if (key.Equals("FFprobe", StringComparison.OrdinalIgnoreCase)) config.FfprobePath = value;
                if (key.Equals("ExportRec709ForHdr", StringComparison.OrdinalIgnoreCase))
                {
                    bool enabled;
                    if (Boolean.TryParse(value, out enabled)) config.ExportRec709ForHdr = enabled;
                }
                if (key.Equals("Language", StringComparison.OrdinalIgnoreCase)) config.Language = value;
                if (key.Equals("PotPlayerMenuPath", StringComparison.OrdinalIgnoreCase)) config.PotPlayerMenuPath = value;
            }
            config.ImageFormat = CaptureFormats.NormalizeImageFormat(config.ImageFormat);
            config.VideoPreset = CaptureFormats.NormalizeVideoPreset(config.VideoPreset);
            config.Language = UiText.NormalizeLanguage(config.Language);
            config.LocateTools();
            if (legacyConfig)
            {
                try { config.Save(); }
                catch { }
            }
            return config;
        }

        internal void LocateTools()
        {
            FfmpegPath = ToolLocator.Find(FfmpegPath, "ffmpeg.exe");
            if (String.IsNullOrWhiteSpace(FfprobePath) && !String.IsNullOrWhiteSpace(FfmpegPath))
                FfprobePath = Path.Combine(Path.GetDirectoryName(FfmpegPath), "ffprobe.exe");
            FfprobePath = ToolLocator.Find(FfprobePath, "ffprobe.exe");
        }

        internal void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            string[] lines = new[]
            {
                "LibraryRootDirectory=" + LibraryRootDirectory,
                "ImageFormat=" + CaptureFormats.NormalizeImageFormat(ImageFormat),
                "VideoPreset=" + CaptureFormats.NormalizeVideoPreset(VideoPreset),
                "FFmpeg=" + FfmpegPath,
                "FFprobe=" + FfprobePath,
                "ExportRec709ForHdr=" + ExportRec709ForHdr.ToString(),
                "Language=" + UiText.NormalizeLanguage(Language),
                "PotPlayerMenuPath=" + PotPlayerMenuPath
            };
            string temporary = ConfigPath + ".tmp";
            File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
            if (File.Exists(ConfigPath))
            {
                try { File.Replace(temporary, ConfigPath, null); }
                catch
                {
                    File.Delete(ConfigPath);
                    File.Move(temporary, ConfigPath);
                }
            }
            else File.Move(temporary, ConfigPath);
        }
    }

    internal sealed class MediaOutputPaths
    {
        internal string WorkTitle;
        internal string WorkDirectory;
        internal string ImageDirectory;
        internal string VideoDirectory;
    }

    internal static class MediaOrganizer
    {
        private static readonly Regex EpisodeMarker = new Regex(
            @"(?ix)(?:^|[\s._\-\[\(])(?:s[\s._\-]*\d{1,3}(?:[\s._\-]*e[\s._\-]*\d{1,4})+|s[\s._\-]*\d{1,3}|\d{1,3}[\s._\-]*x[\s._\-]*\d{1,4}|season[\s._\-]*\d{1,3}|(?:episode|ep|e)[\s._\-]*\d{1,4}|第[0-9一二三四五六七八九十百]+(?:季|集|话|話|回))",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex TechnicalMarker = new Regex(
            @"(?ix)(?:^|[\s._\-\[\(])(?:(?:atvp|amzn|nf|netflix|dsnp|hmax|max|hulu)[\s._\-]+(?:web[\s._\-]*dl|webrip)|(?:dovi|dv)[\s._\-]+(?:hdr10\+?|hdr|hevc|h[._\-]?265)|4320p|2160p|1440p|1080[pi]|720p|576[pi]|480[pi]|8k|4k|uhd|web[\s._\-]*dl|webrip|bluray|blu[\s._\-]*ray|bdrip|bdremux|remux|hdtv|hdrip|dvdrip|hevc|avc|av1|h[._\-]?26[45]|x26[45]|hdr10\+?|hdr|hlg|dolby[\s._\-]*vision|dovi|sdr|ddp(?:[._\-]?\d(?:[._\-]\d)?)?|eac3|ac3|truehd|atmos|dts(?:[\s._\-]*hd)?(?:[\s._\-]*ma)?|aac|flac|multi|imax|dual[\s._\-]*audio|extended|theatrical|uncut|director(?:s|'s)?[\s._\-]*cut|proper|repack)(?:$|[\s._\-\]\)])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex BroadcastDateMarker = new Regex(
            @"(?ix)(?:^|[\s._\-])(?:(?:19|20)\d{2}[\s._\-](?:0?[1-9]|1[0-2])[\s._\-](?:0?[1-9]|[12]\d|3[01])|(?:19|20)\d{2}(?:0[1-9]|1[0-2])(?:0[1-9]|[12]\d|3[01]))(?=$|[\s._\-\[\(])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex ZeroPaddedEpisodeAtEnd = new Regex(
            @"(?ix)[\s._\-]+0\d{1,2}(?:v\d+)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex BracketedEpisodeAtEnd = new Regex(
            @"(?ix)[\s._\-]*[\[\(]\s*0\d{1,2}(?:v\d+)?\s*[\]\)]$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex GroupedAbsoluteEpisodeAtEnd = new Regex(
            @"(?ix)\s+-\s*(?:(?:episode|ep|e)[\s._\-]*)?\d{1,4}(?:v\d+)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex PotPlayerSuffix = new Regex(
            @"(?ix)\.(?:mkv|mp4|mov|m4v|avi|ts|m2ts|webm)\s*-\s*potplayer\s+\d{4}[_-]\d{1,2}[_-]\d{1,2}.*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex LeadingGroup = new Regex(@"^\s*\[[^\]]{1,48}\]\s*", RegexOptions.Compiled);
        private static readonly Regex TrailingReleaseGroup = new Regex(@"(?ix)[\s._]+-[\s._]*[\p{L}\p{N}][\p{L}\p{N}._-]{1,30}$", RegexOptions.Compiled);
        private static readonly Regex YearAtEnd = new Regex(@"(?<!\d)((?:19|20)\d{2})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex ParenthesizedYear = new Regex(@"\s*[\(\[]((?:19|20)\d{2})[\)\]]\s*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex GenericSeasonDirectory = new Regex(
            @"(?ix)^(?:s(?:eason)?[\s._\-]*\d{1,3}|第[0-9一二三四五六七八九十百]+季)(?:[\s._\-]*(?:4k|8k|uhd|hdr10\+?|hdr|dovi|dv|sdr|2160p|1080p))*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly object AliasFileLock = new object();
        private static readonly string AliasFilePath = Path.Combine(AppPaths.DataDirectory, "library-aliases.tsv");

        internal static MediaOutputPaths GetPaths(string rootDirectory, string sourcePath, string embeddedTitle, bool embeddedTitleIsSeries)
        {
            string root = Path.GetFullPath(rootDirectory);
            Directory.CreateDirectory(root);
            string fileTitle = DeriveFileTitle(sourcePath);
            string derivedTitle = DeriveClassificationTitle(sourcePath, embeddedTitle, embeddedTitleIsSeries);
            string metadataTitle = CleanReleaseTitle(embeddedTitle ?? String.Empty);
            bool episodicSource = IsEpisodicSource(sourcePath);
            bool fileTitleCanIdentifyWork = IsUsableTitle(fileTitle) && !IsWeakFileTitle(fileTitle);
            bool metadataCanIdentifyWork = IsUsableTitle(metadataTitle) && (embeddedTitleIsSeries || !episodicSource);
            bool derivedTitleCanIdentifyWork = IsUsableTitle(derivedTitle) && !IsWeakFileTitle(derivedTitle);
            if (embeddedTitleIsSeries && episodicSource && IsUsableTitle(metadataTitle)) derivedTitle = metadataTitle;
            // 先使用已经确认过的别名映射，再进行目录相似度匹配。弱标题（例如 02、video、
            // 长哈希）不会参与模糊匹配，避免把不同作品因短文件名误归到同一文件夹。
            string existing = (fileTitleCanIdentifyWork ? FindMappedWorkDirectory(root, fileTitle, episodicSource) : null) ??
                (metadataCanIdentifyWork ? FindMappedWorkDirectory(root, metadataTitle, embeddedTitleIsSeries) : null) ??
                (derivedTitleCanIdentifyWork ? FindMappedWorkDirectory(root, derivedTitle, episodicSource) : null) ??
                (fileTitleCanIdentifyWork ? FindExistingWorkDirectory(root, fileTitle, episodicSource) : null) ??
                (metadataCanIdentifyWork ? FindExistingWorkDirectory(root, metadataTitle, embeddedTitleIsSeries) : null) ??
                (derivedTitleCanIdentifyWork ? FindExistingWorkDirectory(root, derivedTitle, episodicSource) : null);
            string workDirectory = existing ?? BuildNewWorkDirectory(root, derivedTitle, episodicSource);
            string imageDirectory = Path.Combine(workDirectory, "图片");
            string videoDirectory = Path.Combine(workDirectory, "视频");
            Directory.CreateDirectory(imageDirectory);
            Directory.CreateDirectory(videoDirectory);
            if (fileTitleCanIdentifyWork) RememberAlias(root, fileTitle, Path.GetFileName(workDirectory), episodicSource);
            if (derivedTitle != fileTitle && derivedTitleCanIdentifyWork)
                RememberAlias(root, derivedTitle, Path.GetFileName(workDirectory), episodicSource);
            if (metadataCanIdentifyWork)
                RememberAlias(root, metadataTitle, Path.GetFileName(workDirectory), embeddedTitleIsSeries);
            return new MediaOutputPaths
            {
                WorkTitle = Path.GetFileName(workDirectory),
                WorkDirectory = workDirectory,
                ImageDirectory = imageDirectory,
                VideoDirectory = videoDirectory
            };
        }

        internal static string DeriveWorkTitle(string sourcePath, string embeddedTitle)
        {
            string fileName = Path.GetFileNameWithoutExtension(sourcePath) ?? String.Empty;
            fileName = PotPlayerSuffix.Replace(fileName, String.Empty);
            string fromFile = CleanReleaseTitle(fileName);
            string fromMetadata = CleanReleaseTitle(embeddedTitle ?? String.Empty);

            if (IsUsableTitle(fromMetadata) && IsWeakFileTitle(fromFile)) return fromMetadata;
            if (IsUsableTitle(fromFile)) return fromFile;
            if (IsUsableTitle(fromMetadata)) return fromMetadata;
            return "未命名作品";
        }

        internal static string DeriveClassificationTitle(string sourcePath, string embeddedTitle, bool embeddedTitleIsSeries)
        {
            string fileTitle = DeriveFileTitle(sourcePath);
            string metadataTitle = CleanReleaseTitle(embeddedTitle ?? String.Empty);
            bool episodic = IsEpisodicSource(sourcePath);
            if (embeddedTitleIsSeries && episodic && IsUsableTitle(metadataTitle)) return metadataTitle;
            if (!IsWeakFileTitle(fileTitle)) return fileTitle;

            string parentTitle = DeriveParentWorkTitle(sourcePath);
            if (IsUsableTitle(parentTitle)) return parentTitle;
            if (!episodic && IsUsableTitle(metadataTitle) && !IsWeakFileTitle(metadataTitle)) return metadataTitle;

            // 作品名在文件、元数据和上级目录中都不存在时，任何自动“猜片名”都会把
            // 素材放错位置。稳定哈希只区分来源目录，不包含盘符固定规则，也不扫描磁盘。
            string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? sourcePath;
            return (episodic ? "待归类剧集" : "待归类作品") + " [" + StableShortHash(sourceDirectory.Normalize(NormalizationForm.FormKC).ToLowerInvariant()) + "]";
        }

        private static string DeriveParentWorkTitle(string sourcePath)
        {
            DirectoryInfo directory = null;
            try { directory = Directory.GetParent(Path.GetFullPath(sourcePath)); }
            catch { }
            for (int depth = 0; directory != null && depth < 4; depth++, directory = directory.Parent)
            {
                string raw = directory.Name;
                if (String.IsNullOrWhiteSpace(raw) || IsGenericSourceDirectory(raw)) continue;
                string candidate = CleanReleaseTitle(raw);
                if (IsUsableTitle(candidate) && !IsWeakFileTitle(candidate) && !IsGenericSourceDirectory(candidate)) return candidate;
            }
            return String.Empty;
        }

        private static bool IsGenericSourceDirectory(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return true;
            string normalized = Whitespace.Replace(value.Replace('_', ' ').Replace('.', ' '), " ").Trim();
            if (GenericSeasonDirectory.IsMatch(normalized)) return true;
            string key = GetIdentityKey(normalized, false);
            return key == "download" || key == "downloads" || key == "baidunetdiskdownload" || key == "video" ||
                key == "videos" || key == "movie" || key == "movies" || key == "series" || key == "tv" || key == "media";
        }

        private static string DeriveFileTitle(string sourcePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(sourcePath) ?? String.Empty;
            fileName = PotPlayerSuffix.Replace(fileName, String.Empty);
            return CleanReleaseTitle(fileName);
        }

        private static string CleanReleaseTitle(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return String.Empty;
            string rawTitle = value.Trim();
            bool hadLeadingGroup = LeadingGroup.IsMatch(rawTitle);
            string title = LeadingGroup.Replace(rawTitle, String.Empty);
            Match broadcastDate = BroadcastDateMarker.Match(title);
            if (broadcastDate.Success && broadcastDate.Index > 0) title = title.Substring(0, broadcastDate.Index);
            Match episode = EpisodeMarker.Match(title);
            if (episode.Success && episode.Index > 0) title = title.Substring(0, episode.Index);
            else
            {
                int technicalIndex = FindTechnicalTailStart(title);
                if (technicalIndex > 0) title = title.Substring(0, technicalIndex);
            }

            title = title.TrimEnd();
            title = Regex.Replace(title, @"(?ix)[\s._\-]+(?:episode|ep)[\s._\-]*\d{1,4}.*$", String.Empty);
            title = BracketedEpisodeAtEnd.Replace(title, String.Empty);
            title = ZeroPaddedEpisodeAtEnd.Replace(title, String.Empty);
            if (hadLeadingGroup) title = GroupedAbsoluteEpisodeAtEnd.Replace(title, String.Empty);
            title = TrailingReleaseGroup.Replace(title, String.Empty);
            title = title.Replace('_', ' ').Replace('.', ' ');
            title = Regex.Replace(title, @"\s*-\s*", " ");
            title = Whitespace.Replace(title, " ").Trim(' ', '.', '-', '_', '[', ']', '(', ')');

            Match year = YearAtEnd.Match(title);
            int parsedYear;
            if (year.Success && year.Index > 2 && Int32.TryParse(year.Groups[1].Value, out parsedYear) && IsPlausibleReleaseYear(parsedYear))
            {
                string name = title.Substring(0, year.Index).Trim();
                if (name.Length > 0) title = name + " (" + year.Groups[1].Value + ")";
            }
            return SanitizeFolderName(title);
        }

        private static bool IsUsableTitle(string title)
        {
            return !String.IsNullOrWhiteSpace(title) && GetIdentityKey(title, false).Length >= 2;
        }

        private static bool IsWeakFileTitle(string title)
        {
            if (!IsUsableTitle(title)) return true;
            string key = GetIdentityKey(title, true);
            if (key == "video" || key == "movie" || key == "episode" || key == "untitled" || key == "未命名") return true;
            if (Regex.IsMatch(key, @"^[a-f0-9]{16,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;
            if (Regex.IsMatch(key, @"^\d+$", RegexOptions.CultureInvariant))
                return key.Length > 4 || key[0] == '0';
            if (Regex.IsMatch(key, @"^(?:tt|imdb|tmdb)\d{5,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;
            if (Regex.IsMatch(key, @"^(?:video|movie|episode|output|capture|download)\d*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;
            if (Regex.IsMatch(title, @"(?ix)^(?:19|20)\d{2}(?:[\s._\-]+(?:4320p|2160p|1440p|1080[pi]|720p|uhd|4k|8k|hdr10\+?|hdr|sdr|dv|dovi|it|web|dl|remux|hevc|avc|av1|h26[45]|x26[45]))+$")) return true;
            return false;
        }

        private static string FindExistingWorkDirectory(string root, string title, bool episodic)
        {
            string targetFullKey = GetIdentityKey(title, false);
            string targetKey = GetIdentityKey(title, true);
            string targetYear = ExtractYear(title);
            string bestPath = null;
            double bestScore = 0.0;
            double secondScore = 0.0;
            List<string> exactBaseMatches = new List<string>();
            foreach (string directory in Directory.GetDirectories(root))
            {
                string name = Path.GetFileName(directory);
                string candidateFullKey = GetIdentityKey(name, false);
                string candidateKey = GetIdentityKey(name, true);
                string candidateYear = ExtractYear(name);
                if (IsFolderBoundToOtherKind(root, name, episodic)) continue;
                if (candidateFullKey == targetFullKey) return directory;
                if (targetYear.Length > 0 && candidateYear.Length > 0 && targetYear != candidateYear) continue;
                if (targetYear.Length > 0 && candidateYear.Length == 0) continue;
                if (targetYear.Length == 0 && candidateYear.Length > 0) continue;
                if (candidateKey == targetKey)
                {
                    exactBaseMatches.Add(directory);
                    continue;
                }
                if (!Directory.Exists(Path.Combine(directory, "图片")) && !Directory.Exists(Path.Combine(directory, "视频"))) continue;
                if (!IsSafeFuzzyCandidate(title, name)) continue;
                double score = Similarity(targetKey, candidateKey);
                if (score > bestScore)
                {
                    secondScore = bestScore;
                    bestScore = score;
                    bestPath = directory;
                }
                else if (score > secondScore) secondScore = score;
            }

            if (exactBaseMatches.Count == 1) return exactBaseMatches[0];
            if (exactBaseMatches.Count > 1) return null;

            int minimumLength = Math.Min(targetKey.Length, bestPath == null ? 0 : GetIdentityKey(Path.GetFileName(bestPath), true).Length);
            if (bestPath != null && minimumLength >= 8 && bestScore >= 0.90 && bestScore - secondScore >= 0.04) return bestPath;
            return null;
        }

        private static bool IsSafeFuzzyCandidate(string target, string candidate)
        {
            List<string> targetTokens = GetFuzzyTokens(target);
            List<string> candidateTokens = GetFuzzyTokens(candidate);
            if (targetTokens.Count == 0 || targetTokens.Count != candidateTokens.Count) return false;
            for (int index = 0; index < targetTokens.Count; index++)
            {
                string left = targetTokens[index];
                string right = candidateTokens[index];
                if (left == right) continue;
                if (left.Length <= 3 || right.Length <= 3) return false;
                if (Regex.IsMatch(left + right, @"\d", RegexOptions.CultureInvariant)) return false;
                if (Similarity(left, right) < 0.80) return false;
            }
            return true;
        }

        private static List<string> GetFuzzyTokens(string value)
        {
            string normalized = (value ?? String.Empty).Normalize(NormalizationForm.FormKC).ToLowerInvariant().Replace("&", " and ");
            Match parenthesized = ParenthesizedYear.Match(normalized);
            int year;
            if (parenthesized.Success && Int32.TryParse(parenthesized.Groups[1].Value, out year) && IsPlausibleReleaseYear(year))
                normalized = normalized.Substring(0, parenthesized.Index);
            List<string> tokens = new List<string>();
            foreach (Match token in Regex.Matches(normalized, @"[\p{L}\p{N}]+", RegexOptions.CultureInvariant))
                tokens.Add(token.Value);
            return tokens;
        }

        private static string ExtractYear(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return String.Empty;
            string trimmed = value.Trim();
            foreach (Match match in Regex.Matches(trimmed, @"(?<!\d)((?:19|20)\d{2})(?!\d)", RegexOptions.CultureInvariant))
            {
                int year;
                if (match.Index == 0 && match.Length == trimmed.Length) continue;
                if (Int32.TryParse(match.Groups[1].Value, out year) && IsPlausibleReleaseYear(year)) return match.Groups[1].Value;
            }
            return String.Empty;
        }

        private static string GetIdentityKey(string value, bool stripYear)
        {
            if (String.IsNullOrWhiteSpace(value)) return String.Empty;
            string normalized = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Replace("&", "and");
            if (stripYear)
            {
                Match parenthesized = ParenthesizedYear.Match(normalized);
                int year;
                if (parenthesized.Success && Int32.TryParse(parenthesized.Groups[1].Value, out year) && IsPlausibleReleaseYear(year))
                    normalized = normalized.Substring(0, parenthesized.Index);
                else
                {
                    Match plain = YearAtEnd.Match(normalized);
                    if (plain.Success && plain.Index > 0 && Int32.TryParse(plain.Groups[1].Value, out year) && IsPlausibleReleaseYear(year))
                        normalized = normalized.Substring(0, plain.Index);
                }
            }
            StringBuilder key = new StringBuilder();
            foreach (char character in normalized)
                if (Char.IsLetterOrDigit(character)) key.Append(character);
            return key.ToString();
        }

        private static double Similarity(string left, string right)
        {
            if (left.Length == 0 || right.Length == 0) return 0.0;
            if (left == right) return 1.0;
            int distance = LevenshteinDistance(left, right);
            return 1.0 - distance / (double)Math.Max(left.Length, right.Length);
        }

        private static int LevenshteinDistance(string left, string right)
        {
            int[] previous = new int[right.Length + 1];
            int[] current = new int[right.Length + 1];
            for (int index = 0; index <= right.Length; index++) previous[index] = index;
            for (int row = 1; row <= left.Length; row++)
            {
                current[0] = row;
                for (int column = 1; column <= right.Length; column++)
                {
                    int cost = left[row - 1] == right[column - 1] ? 0 : 1;
                    current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), previous[column - 1] + cost);
                }
                int[] swap = previous;
                previous = current;
                current = swap;
            }
            return previous[right.Length];
        }

        private static bool IsPlausibleReleaseYear(int year)
        {
            return year >= 1888 && year <= DateTime.Now.Year + 1;
        }

        private static int FindTechnicalTailStart(string title)
        {
            foreach (Match marker in TechnicalMarker.Matches(title))
            {
                if (marker.Index <= 0) continue;
                string tail = title.Substring(marker.Index);
                tail = TrailingReleaseGroup.Replace(tail, String.Empty);
                tail = Regex.Replace(tail, @"(?ix)-[\p{L}\p{N}]{2,24}$", String.Empty);
                tail = Regex.Replace(tail, @"\[[^\]]*\]|\([^\)]*\)", " ");
                for (int pass = 0; pass < 3; pass++) tail = TechnicalMarker.Replace(tail, " ");
                tail = Regex.Replace(tail,
                    @"(?ix)(?:^|[\s._\-])(?:atvp|amzn|nf|netflix|dsnp|hmax|max|hulu|web|dl|rip|bluray|blu|ray|bd|remux|uhd|hdr|sdr|dovi|dv|hevc|avc|av1|x26[45]|h26[45]|ddp|eac3|ac3|truehd|atmos|dts|hd|ma|aac|flac|multi|imax|proper|repack|internal|extended|theatrical|uncut|limited|complete|dual|audio|dubbed|subbed|eng|chs|cht|chi|jpn|kor|fra|ger|ita|spa|rus|10bit|12bit|8bit|sample|readnfo|v\d+|(?:19|20)\d{2}|\d+(?:\.\d+)?)(?=$|[\s._\-])",
                    " ");
                if (!Regex.IsMatch(tail, @"[\p{L}]")) return marker.Index;
            }
            return -1;
        }

        private static bool IsEpisodicSource(string sourcePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(sourcePath) ?? String.Empty;
            if (EpisodeMarker.IsMatch(fileName) || BroadcastDateMarker.IsMatch(fileName)) return true;
            if (Regex.IsMatch(fileName, @"(?ix)\s+-\s*(?:(?:episode|ep|e)[\s._\-]*)?\d{1,4}(?:v\d+)?(?=$|[\s._\-\[\(])")) return true;
            if (Regex.IsMatch(fileName, @"(?ix)(?:^|[\s._\-])0\d{1,2}(?:v\d+)?(?=[\s._\-]+(?:4320p|2160p|1440p|1080[pi]|720p|576[pi]|480[pi]|web|bluray|webrip|hdtv))")) return true;
            string parent = Path.GetDirectoryName(sourcePath);
            string parentName = String.IsNullOrEmpty(parent) ? String.Empty : Path.GetFileName(parent);
            return Regex.IsMatch(fileName, @"^0?\d{1,4}(?:v\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
                GenericSeasonDirectory.IsMatch(Whitespace.Replace((parentName ?? String.Empty).Replace('_', ' ').Replace('.', ' '), " ").Trim());
        }

        private static string FindMappedWorkDirectory(string root, string title, bool episodic)
        {
            List<string> aliases = GetAliasKeys(title, episodic);
            if (aliases.Count == 0 || !File.Exists(AliasFilePath)) return null;
            lock (AliasFileLock)
            {
                try
                {
                    string rootKey = NormalizeRoot(root);
                    foreach (string line in File.ReadAllLines(AliasFilePath, Encoding.UTF8))
                    {
                        string[] fields = line.Split('\t');
                        if (fields.Length != 3) continue;
                        string storedRoot = DecodeAliasField(fields[0]);
                        string storedAlias = DecodeAliasField(fields[1]);
                        string folderName = DecodeAliasField(fields[2]);
                        if (!String.Equals(storedRoot, rootKey, StringComparison.OrdinalIgnoreCase) || !aliases.Contains(storedAlias)) continue;
                        if (String.IsNullOrWhiteSpace(folderName) || Path.GetFileName(folderName) != folderName) continue;
                        string path = Path.Combine(root, folderName);
                        if (Directory.Exists(path)) return path;
                    }
                }
                catch { }
            }
            return null;
        }

        private static bool IsFolderBoundToOtherKind(string root, string folderName, bool episodic)
        {
            if (!File.Exists(AliasFilePath)) return false;
            bool requestedKindFound = false;
            bool otherKindFound = false;
            lock (AliasFileLock)
            {
                try
                {
                    string rootKey = NormalizeRoot(root);
                    foreach (string line in File.ReadAllLines(AliasFilePath, Encoding.UTF8))
                    {
                        string[] fields = line.Split('\t');
                        if (fields.Length != 3 || !String.Equals(DecodeAliasField(fields[0]), rootKey, StringComparison.OrdinalIgnoreCase) ||
                            !String.Equals(DecodeAliasField(fields[2]), folderName, StringComparison.OrdinalIgnoreCase)) continue;
                        string alias = DecodeAliasField(fields[1]);
                        bool isSeriesAlias = alias.StartsWith("series:", StringComparison.Ordinal) || alias.StartsWith("series-base:", StringComparison.Ordinal);
                        bool isFeatureAlias = alias.StartsWith("feature:", StringComparison.Ordinal);
                        if ((episodic && isSeriesAlias) || (!episodic && isFeatureAlias)) requestedKindFound = true;
                        if ((episodic && isFeatureAlias) || (!episodic && isSeriesAlias)) otherKindFound = true;
                    }
                }
                catch { return false; }
            }
            return otherKindFound && !requestedKindFound;
        }

        private static string BuildNewWorkDirectory(string root, string title, bool episodic)
        {
            string folderName = SanitizeFolderName(title);
            string path = Path.Combine(root, folderName);
            if (!Directory.Exists(path)) return path;

            string kindSuffix = episodic ? " [剧集]" : " [电影]";
            folderName = SanitizeFolderName(title + kindSuffix);
            path = Path.Combine(root, folderName);
            if (!Directory.Exists(path)) return path;

            for (int number = 2; number < 1000; number++)
            {
                path = Path.Combine(root, SanitizeFolderName(title + kindSuffix + " " + number.ToString(CultureInfo.InvariantCulture)));
                if (!Directory.Exists(path)) return path;
            }
            throw new IOException("同名作品目录过多，请整理参考素材库根目录后重试。");
        }

        private static void RememberAlias(string root, string title, string folderName, bool episodic)
        {
            List<string> aliases = GetAliasKeys(title, episodic);
            if (aliases.Count == 0 || String.IsNullOrWhiteSpace(folderName) || Path.GetFileName(folderName) != folderName) return;
            lock (AliasFileLock)
            {
                try
                {
                    string stateDirectory = Path.GetDirectoryName(AliasFilePath);
                    Directory.CreateDirectory(stateDirectory);
                    string rootKey = NormalizeRoot(root);
                    List<string> output = new List<string>();
                    Dictionary<string, bool> replaced = new Dictionary<string, bool>();
                    foreach (string alias in aliases) replaced[alias] = false;
                    if (File.Exists(AliasFilePath))
                    {
                        foreach (string line in File.ReadAllLines(AliasFilePath, Encoding.UTF8))
                        {
                            string[] fields = line.Split('\t');
                            string storedAlias = fields.Length == 3 ? DecodeAliasField(fields[1]) : String.Empty;
                            if (fields.Length == 3 && String.Equals(DecodeAliasField(fields[0]), rootKey, StringComparison.OrdinalIgnoreCase) && replaced.ContainsKey(storedAlias))
                            {
                                if (!replaced[storedAlias]) output.Add(EncodeAliasLine(rootKey, storedAlias, folderName));
                                replaced[storedAlias] = true;
                            }
                            else output.Add(line);
                        }
                    }
                    foreach (string alias in aliases)
                        if (!replaced[alias]) output.Add(EncodeAliasLine(rootKey, alias, folderName));
                    string temporary = AliasFilePath + ".tmp";
                    File.WriteAllLines(temporary, output.ToArray(), new UTF8Encoding(false));
                    if (File.Exists(AliasFilePath))
                    {
                        try { File.Replace(temporary, AliasFilePath, null); }
                        catch
                        {
                            File.Delete(AliasFilePath);
                            File.Move(temporary, AliasFilePath);
                        }
                    }
                    else File.Move(temporary, AliasFilePath);
                }
                catch { }
            }
        }

        private static List<string> GetAliasKeys(string title, bool episodic)
        {
            List<string> aliases = new List<string>();
            string fullKey = GetIdentityKey(title, false);
            if (fullKey.Length == 0) return aliases;
            string prefix = episodic ? "series:" : "feature:";
            aliases.Add(prefix + fullKey);
            if (episodic && ExtractYear(title).Length == 0)
            {
                string baseKey = GetIdentityKey(title, true);
                string baseAlias = "series-base:" + baseKey;
                if (baseKey.Length > 0 && !aliases.Contains(baseAlias)) aliases.Add(baseAlias);
            }
            return aliases;
        }

        private static string NormalizeRoot(string root)
        {
            return Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        }

        private static string EncodeAliasLine(string root, string alias, string folderName)
        {
            return EncodeAliasField(root) + "\t" + EncodeAliasField(alias) + "\t" + EncodeAliasField(folderName);
        }

        private static string EncodeAliasField(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? String.Empty));
        }

        private static string DecodeAliasField(string value)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
            catch { return String.Empty; }
        }

        private static string SanitizeFolderName(string value)
        {
            string title = String.IsNullOrWhiteSpace(value) ? "未命名作品" : value.Normalize(NormalizationForm.FormC).Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars()) title = title.Replace(invalid, ' ');
            title = Whitespace.Replace(title, " ").Trim(' ', '.');
            if (title.Length > 96)
            {
                string suffix = " [" + StableShortHash(title) + "]";
                title = title.Substring(0, 96 - suffix.Length).TrimEnd(' ', '.') + suffix;
            }
            string deviceName = title.Split('.')[0].Trim();
            if (Regex.IsMatch(deviceName, @"(?ix)^(?:con|prn|aux|nul|com[1-9]|lpt[1-9])$")) title += "_";
            return title.Length == 0 ? "未命名作品" : title;
        }

        internal static string StableShortHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (byte valueByte in Encoding.UTF8.GetBytes(value))
                {
                    hash ^= valueByte;
                    hash *= 16777619;
                }
                return hash.ToString("x8", CultureInfo.InvariantCulture);
            }
        }
    }

    internal sealed class FormatChoice
    {
        internal readonly string Value;
        internal readonly string Label;

        internal FormatChoice(string value, string label)
        {
            Value = value;
            Label = label;
        }

        public override string ToString()
        {
            return Label;
        }
    }

    internal sealed class VideoEncodingSpec
    {
        internal string Token;
        internal string DisplayName;
        internal string Extension;
        internal string PixelFormat;
        internal string CodecArguments;
    }

    internal static class CaptureFormats
    {
        internal static readonly FormatChoice[] ImageChoices = new[]
        {
            new FormatChoice("png16", "PNG · 16-bit RGB（推荐，可写入色彩标签）"),
            new FormatChoice("tiff16", "TIFF · 16-bit RGB（无损，需手动指定输入色彩空间）")
        };

        internal static readonly FormatChoice[] VideoChoices = new[]
        {
            new FormatChoice("prores422hq", "Apple ProRes 422 HQ · 10-bit（推荐）"),
            new FormatChoice("prores4444", "Apple ProRes 4444 · 10-bit 4:4:4 输入"),
            new FormatChoice("prores4444xq", "Apple ProRes 4444 XQ · 10-bit 4:4:4 输入"),
            new FormatChoice("dnxhrhqx", "Avid DNxHR HQX · 10-bit 4:2:2"),
            new FormatChoice("dnxhr444", "Avid DNxHR 444 · 10-bit 4:4:4")
        };

        internal static string NormalizeImageFormat(string value)
        {
            return String.Equals(value, "tiff16", StringComparison.OrdinalIgnoreCase) ? "tiff16" : "png16";
        }

        internal static string NormalizeVideoPreset(string value)
        {
            foreach (FormatChoice choice in VideoChoices)
                if (String.Equals(choice.Value, value, StringComparison.OrdinalIgnoreCase)) return choice.Value;
            return "prores422hq";
        }

        internal static VideoEncodingSpec GetVideoSpec(string value)
        {
            string preset = NormalizeVideoPreset(value);
            if (preset == "prores4444") return new VideoEncodingSpec
            {
                Token = preset,
                DisplayName = "ProRes 4444",
                Extension = ".mov",
                PixelFormat = "yuv444p10le",
                CodecArguments = "-c:v prores_ks -profile:v 4 -vendor apl0 -alpha_bits 0"
            };
            if (preset == "prores4444xq") return new VideoEncodingSpec
            {
                Token = preset,
                DisplayName = "ProRes 4444 XQ",
                Extension = ".mov",
                PixelFormat = "yuv444p10le",
                CodecArguments = "-c:v prores_ks -profile:v 5 -vendor apl0 -alpha_bits 0"
            };
            if (preset == "dnxhrhqx") return new VideoEncodingSpec
            {
                Token = preset,
                DisplayName = "DNxHR HQX",
                Extension = ".mov",
                PixelFormat = "yuv422p10le",
                CodecArguments = "-c:v dnxhd -profile:v dnxhr_hqx"
            };
            if (preset == "dnxhr444") return new VideoEncodingSpec
            {
                Token = preset,
                DisplayName = "DNxHR 444",
                Extension = ".mov",
                PixelFormat = "yuv444p10le",
                CodecArguments = "-c:v dnxhd -profile:v dnxhr_444"
            };
            return new VideoEncodingSpec
            {
                Token = "prores422hq",
                DisplayName = "ProRes 422 HQ",
                Extension = ".mov",
                PixelFormat = "yuv422p10le",
                CodecArguments = "-c:v prores_ks -profile:v 3 -vendor apl0"
            };
        }
    }

    internal sealed class RangeState
    {
        internal string SourcePath;
        internal long InMilliseconds = -1;
        internal long OutMilliseconds = -1;

        internal static string StateDirectory
        {
            get { return AppPaths.DataDirectory; }
        }

        internal static string StatePath
        {
            get { return Path.Combine(StateDirectory, "range.state"); }
        }

        internal static RangeState Load()
        {
            RangeState state = new RangeState();
            if (!File.Exists(StatePath)) return state;
            try
            {
                foreach (string line in File.ReadAllLines(StatePath, Encoding.UTF8))
                {
                    int separator = line.IndexOf('=');
                    if (separator <= 0) continue;
                    string key = line.Substring(0, separator);
                    string value = line.Substring(separator + 1);
                    if (key == "Path") state.SourcePath = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                    long parsed;
                    if (key == "In" && long.TryParse(value, out parsed)) state.InMilliseconds = parsed;
                    if (key == "Out" && long.TryParse(value, out parsed)) state.OutMilliseconds = parsed;
                }
            }
            catch
            {
                return new RangeState();
            }
            return state;
        }

        internal void Save()
        {
            Directory.CreateDirectory(StateDirectory);
            List<string> lines = new List<string>();
            lines.Add("Path=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(SourcePath ?? String.Empty)));
            lines.Add("In=" + InMilliseconds.ToString(CultureInfo.InvariantCulture));
            lines.Add("Out=" + OutMilliseconds.ToString(CultureInfo.InvariantCulture));
            File.WriteAllLines(StatePath, lines.ToArray(), new UTF8Encoding(false));
        }

        internal void Clear()
        {
            SourcePath = null;
            InMilliseconds = -1;
            OutMilliseconds = -1;
            Save();
        }
    }

    internal static class PotPlayerMediaLocator
    {
        private static readonly Regex CommandLineToken = new Regex("\\\"([^\\\"]*)\\\"|(\\S+)", RegexOptions.Compiled);

        internal static bool TitleMatchesPath(string title, string path)
        {
            if (String.IsNullOrWhiteSpace(title) || String.IsNullOrWhiteSpace(path)) return false;
            string fileName;
            try { fileName = Path.GetFileName(path); }
            catch { return false; }
            if (fileName.Equals(title, StringComparison.OrdinalIgnoreCase)) return true;
            return Path.GetFileNameWithoutExtension(fileName).Equals(title, StringComparison.OrdinalIgnoreCase);
        }

        internal static string ExtractPathFromIniValue(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string candidate = value.Trim().Trim('"');
            if (IsRootedPath(candidate)) return candidate;

            // PotPlayer 的历史列表可能写成“序号=播放位置=完整路径”。从每个等号后
            // 依次寻找盘符路径，可同时兼容普通 BMList 和带播放位置的记录。
            int separator = candidate.IndexOf('=');
            while (separator >= 0 && separator + 1 < candidate.Length)
            {
                string suffix = candidate.Substring(separator + 1).Trim().Trim('"');
                if (IsRootedPath(suffix)) return suffix;
                separator = candidate.IndexOf('=', separator + 1);
            }
            return null;
        }

        internal static IList<string> ExtractExistingMediaArguments(string commandLine)
        {
            List<string> paths = new List<string>();
            if (String.IsNullOrWhiteSpace(commandLine)) return paths;
            bool first = true;
            foreach (Match match in CommandLineToken.Matches(commandLine))
            {
                string token = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                if (first)
                {
                    first = false;
                    continue;
                }
                token = token.Trim();
                if (IsRootedPath(token) && File.Exists(token)) paths.Add(Path.GetFullPath(token));
            }
            return paths;
        }

        internal static string FindExactOrSibling(string title, IEnumerable<string> trustedPaths)
        {
            if (String.IsNullOrWhiteSpace(title) || trustedPaths == null) return null;
            string safeTitle;
            try
            {
                safeTitle = Path.GetFileName(title);
                if (!safeTitle.Equals(title, StringComparison.Ordinal) || safeTitle.Length == 0) return null;
            }
            catch { return null; }

            HashSet<string> directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in trustedPaths)
            {
                if (String.IsNullOrWhiteSpace(path)) continue;
                try
                {
                    if (File.Exists(path) && TitleMatchesPath(title, path)) return Path.GetFullPath(path);
                    string directory = Path.GetDirectoryName(path);
                    if (!String.IsNullOrWhiteSpace(directory)) directories.Add(directory);
                }
                catch { }
            }

            // 只在播放列表、INI 或目标 PotPlayer 进程已经证明可信的目录中拼接文件名，
            // 避免短文件名触发整盘扫描，也避免同名媒体被错误地跨目录匹配。
            foreach (string directory in directories)
            {
                try
                {
                    string candidate = Path.Combine(directory, safeTitle);
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
                catch { }
            }
            return null;
        }

        private static bool IsRootedPath(string value)
        {
            try { return Path.IsPathRooted(value); }
            catch { return false; }
        }
    }

    internal sealed class VideoInfo
    {
        internal int Width;
        internal int Height;
        internal string ColorSpace = "unknown";
        internal string ColorTransfer = "unknown";
        internal string ColorPrimaries = "unknown";
        internal string ColorRange = "unknown";
        internal string PixelFormat = "unknown";
        internal string EmbeddedTitle = String.Empty;
        internal bool EmbeddedTitleIsSeries;
        internal bool MetadataAssumed;
        internal bool IsDolbyVision;
        internal int DolbyVisionProfile;
        internal int DolbyVisionCompatibilityId = -1;

        internal bool IsPq
        {
            get { return ColorTransfer.Equals("smpte2084", StringComparison.OrdinalIgnoreCase); }
        }

        internal bool IsHlg
        {
            get { return ColorTransfer.Equals("arib-std-b67", StringComparison.OrdinalIgnoreCase); }
        }

        internal bool IsRec709
        {
            get { return ColorPrimaries.Equals("bt709", StringComparison.OrdinalIgnoreCase); }
        }

        internal bool IsP3
        {
            get
            {
                return ColorPrimaries.Equals("smpte431", StringComparison.OrdinalIgnoreCase) ||
                    ColorPrimaries.Equals("smpte432", StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly AppConfig config;
        private readonly string displayLanguage;
        private readonly TextBox libraryRootTextBox;
        private readonly TextBox ffmpegPathTextBox;
        private readonly ComboBox imageFormatComboBox;
        private readonly ComboBox videoPresetComboBox;
        private readonly ComboBox languageComboBox;
        private readonly CheckBox rec709CheckBox;

        internal bool LanguageChanged { get; private set; }
        internal bool MenuLanguageApplied { get; private set; }

        internal SettingsForm(AppConfig config)
        {
            this.config = config;
            displayLanguage = UiText.NormalizeLanguage(config.Language);
            MenuLanguageApplied = true;
            Text = UiText.Choose(displayLanguage, "参照帧与片段截取设置", "Reference Frame & Clip Capture Settings");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(720, 590);
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);

            Label heading = CreateLabel(UiText.Choose(displayLanguage, "输出与编码", "Output and Encoding"), 24, 20, 650, 30, 13f, FontStyle.Bold);
            Controls.Add(heading);

            Controls.Add(CreateLabel(UiText.Choose(displayLanguage, "输出根目录", "Output root folder"), 24, 68, 220, 24, 9f, FontStyle.Regular));
            libraryRootTextBox = CreatePathTextBox(config.LibraryRootDirectory, 24, 94);
            Controls.Add(libraryRootTextBox);
            Controls.Add(CreateBrowseButton(libraryRootTextBox, 616, 93));

            Controls.Add(CreateLabel(UiText.Choose(displayLanguage, "图片格式", "Image format"), 24, 158, 160, 24, 9f, FontStyle.Regular));
            imageFormatComboBox = CreateChoiceComboBox(24, 184, 320);
            foreach (FormatChoice choice in UiText.ImageChoices(displayLanguage)) imageFormatComboBox.Items.Add(choice);
            SelectChoice(imageFormatComboBox, CaptureFormats.NormalizeImageFormat(config.ImageFormat));
            Controls.Add(imageFormatComboBox);

            Controls.Add(CreateLabel(UiText.Choose(displayLanguage, "精确片段编码", "Precise clip codec"), 370, 158, 260, 24, 9f, FontStyle.Regular));
            videoPresetComboBox = CreateChoiceComboBox(370, 184, 326);
            foreach (FormatChoice choice in UiText.VideoChoices(displayLanguage)) videoPresetComboBox.Items.Add(choice);
            SelectChoice(videoPresetComboBox, CaptureFormats.NormalizeVideoPreset(config.VideoPreset));
            Controls.Add(videoPresetComboBox);

            Controls.Add(CreateLabel("FFmpeg", 24, 242, 160, 24, 9f, FontStyle.Regular));
            ffmpegPathTextBox = CreatePathTextBox(config.FfmpegPath, 24, 268);
            Controls.Add(ffmpegPathTextBox);
            Controls.Add(CreateFileBrowseButton(ffmpegPathTextBox, 616, 267));

            rec709CheckBox = new CheckBox();
            rec709CheckBox.Text = UiText.Choose(displayLanguage,
                "HDR 截图同时生成 Rec.709 SDR 参照（色调映射）",
                "Also generate a tone-mapped Rec.709 SDR reference for HDR captures");
            rec709CheckBox.Checked = config.ExportRec709ForHdr;
            rec709CheckBox.SetBounds(24, 326, 672, 28);
            Controls.Add(rec709CheckBox);

            Controls.Add(CreateLabel(UiText.Choose(displayLanguage, "界面与菜单语言", "Interface and menu language"), 24, 370, 260, 24, 9f, FontStyle.Regular));
            languageComboBox = CreateChoiceComboBox(24, 396, 260);
            foreach (FormatChoice choice in UiText.LanguageChoices()) languageComboBox.Items.Add(choice);
            SelectChoice(languageComboBox, displayLanguage);
            Controls.Add(languageComboBox);

            Label note = CreateLabel(UiText.Choose(displayLanguage,
                "程序会自动建立“根目录\\作品名\\图片”和“根目录\\作品名\\视频”。HDR 的 Rec.709 副本是用于普通 SDR 监看的色调映射参照，不替代同时保存的 HDR 原图。PNG 可携带色彩标签；TIFF 建议按文件名手动指定输入色彩空间。",
                "FrameClip creates title-specific Images and Videos folders automatically. The Rec.709 copy is a tone-mapped SDR viewing reference and does not replace the HDR original. PNG supports color tags; assign TIFF input color space from the filename when needed."), 24, 438, 672, 72, 8.5f, FontStyle.Regular);
            note.ForeColor = Color.FromArgb(80, 84, 90);
            Controls.Add(note);

            Button cancelButton = new Button();
            cancelButton.Text = UiText.Choose(displayLanguage, "取消", "Cancel");
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.SetBounds(508, 532, 90, 34);
            Controls.Add(cancelButton);

            Button saveButton = new Button();
            saveButton.Text = UiText.Choose(displayLanguage, "保存", "Save");
            saveButton.SetBounds(606, 532, 90, 34);
            saveButton.Click += SaveButtonClick;
            Controls.Add(saveButton);

            AcceptButton = saveButton;
            CancelButton = cancelButton;
        }

        protected override void OnShown(EventArgs eventArgs)
        {
            base.OnShown(eventArgs);
            TopMost = true;
            BringToFront();
            Activate();
        }

        private static Label CreateLabel(string text, int left, int top, int width, int height, float size, FontStyle style)
        {
            Label label = new Label();
            label.Text = text;
            label.SetBounds(left, top, width, height);
            label.Font = new Font("Microsoft YaHei UI", size, style);
            return label;
        }

        private static TextBox CreatePathTextBox(string text, int left, int top)
        {
            TextBox textBox = new TextBox();
            textBox.Text = text;
            textBox.SetBounds(left, top, 584, 30);
            return textBox;
        }

        private Button CreateBrowseButton(TextBox target, int left, int top)
        {
            Button button = new Button();
            button.Text = UiText.Choose(displayLanguage, "浏览…", "Browse…");
            button.SetBounds(left, top, 80, 30);
            button.Click += delegate
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = UiText.Choose(displayLanguage, "选择输出文件夹", "Select the output folder");
                    if (Directory.Exists(target.Text)) dialog.SelectedPath = target.Text;
                    if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
                }
            };
            return button;
        }

        private Button CreateFileBrowseButton(TextBox target, int left, int top)
        {
            Button button = new Button();
            button.Text = UiText.Choose(displayLanguage, "浏览…", "Browse…");
            button.SetBounds(left, top, 80, 30);
            button.Click += delegate
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = UiText.Choose(displayLanguage, "选择 ffmpeg.exe", "Select ffmpeg.exe");
                    dialog.Filter = UiText.Choose(displayLanguage, "FFmpeg|ffmpeg.exe|可执行文件|*.exe", "FFmpeg|ffmpeg.exe|Executable files|*.exe");
                    if (File.Exists(target.Text)) dialog.InitialDirectory = Path.GetDirectoryName(target.Text);
                    if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
                }
            };
            return button;
        }

        private static ComboBox CreateChoiceComboBox(int left, int top, int width)
        {
            ComboBox comboBox = new ComboBox();
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox.SetBounds(left, top, width, 30);
            return comboBox;
        }

        private static void SelectChoice(ComboBox comboBox, string value)
        {
            for (int index = 0; index < comboBox.Items.Count; index++)
            {
                FormatChoice choice = comboBox.Items[index] as FormatChoice;
                if (choice != null && String.Equals(choice.Value, value, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedIndex = index;
                    return;
                }
            }
            if (comboBox.Items.Count > 0) comboBox.SelectedIndex = 0;
        }

        private void SaveButtonClick(object sender, EventArgs eventArgs)
        {
            string libraryRoot = libraryRootTextBox.Text.Trim();
            if (libraryRoot.Length == 0)
            {
                MessageBox.Show(this,
                    UiText.Choose(displayLanguage, "输出根目录不能为空。", "The output root folder cannot be empty."),
                    UiText.Choose(displayLanguage, "无法保存", "Cannot save"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                Directory.CreateDirectory(libraryRoot);
                FormatChoice imageChoice = imageFormatComboBox.SelectedItem as FormatChoice;
                FormatChoice videoChoice = videoPresetComboBox.SelectedItem as FormatChoice;
                FormatChoice languageChoice = languageComboBox.SelectedItem as FormatChoice;
                config.LibraryRootDirectory = Path.GetFullPath(libraryRoot);
                config.ImageFormat = imageChoice == null ? "png16" : imageChoice.Value;
                config.VideoPreset = videoChoice == null ? "prores422hq" : videoChoice.Value;
                config.ExportRec709ForHdr = rec709CheckBox.Checked;
                string selectedLanguage = languageChoice == null ? UiText.Chinese : UiText.NormalizeLanguage(languageChoice.Value);
                LanguageChanged = selectedLanguage != displayLanguage;
                config.Language = selectedLanguage;
                config.FfmpegPath = ffmpegPathTextBox.Text.Trim();
                string ffmpegDirectory = String.IsNullOrWhiteSpace(config.FfmpegPath)
                    ? String.Empty
                    : Path.GetDirectoryName(config.FfmpegPath);
                config.FfprobePath = String.IsNullOrWhiteSpace(ffmpegDirectory)
                    ? String.Empty
                    : Path.Combine(ffmpegDirectory, "ffprobe.exe");
                config.LocateTools();
                config.Save();
                MenuLanguageApplied = MenuLocalization.Apply(config);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception exception)
            {
                MessageBox.Show(this,
                    UiText.Choose(displayLanguage, "无法保存设置：", "Unable to save settings: ") + exception.Message,
                    UiText.Choose(displayLanguage, "保存失败", "Save failed"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal sealed class ToastForm : Form
    {
        private readonly System.Windows.Forms.Timer closeTimer;
        private readonly string titleText;
        private readonly string messageText;
        private readonly Font titleFont;
        private readonly Font messageFont;

        internal ToastForm(string title, string message, int milliseconds, Rectangle workingArea)
        {
            titleText = title ?? String.Empty;
            messageText = message ?? String.Empty;
            Text = title;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(34, 36, 39);
            ForeColor = Color.White;
            Width = 520;
            Height = 92;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            UpdateStyles();
            titleFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
            messageFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

            Left = workingArea.Right - Width - 18;
            Top = workingArea.Bottom - Height - 18;

            closeTimer = new System.Windows.Forms.Timer();
            closeTimer.Interval = Math.Max(1200, milliseconds);
            closeTimer.Tick += delegate
            {
                closeTimer.Stop();
                Close();
            };
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000000;
                parameters.ExStyle |= 0x00000080;
                return parameters;
            }
        }

        protected override void OnShown(EventArgs eventArgs)
        {
            base.OnShown(eventArgs);
            Refresh();
            closeTimer.Start();
        }

        protected override void OnPaintBackground(PaintEventArgs eventArgs)
        {
            eventArgs.Graphics.Clear(BackColor);
        }

        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            Graphics graphics = eventArgs.Graphics;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            using (SolidBrush titleBrush = new SolidBrush(Color.White))
            using (SolidBrush messageBrush = new SolidBrush(Color.FromArgb(215, 219, 224)))
            using (Pen borderPen = new Pen(Color.FromArgb(56, 59, 64)))
            using (StringFormat titleFormat = new StringFormat(StringFormat.GenericTypographic))
            using (StringFormat messageFormat = new StringFormat(StringFormat.GenericTypographic))
            {
                titleFormat.Trimming = StringTrimming.EllipsisCharacter;
                titleFormat.FormatFlags = StringFormatFlags.NoWrap;
                messageFormat.Trimming = StringTrimming.EllipsisCharacter;
                graphics.DrawRectangle(borderPen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                graphics.DrawString(titleText, titleFont, titleBrush, new RectangleF(18, 11, ClientSize.Width - 36, 25), titleFormat);
                graphics.DrawString(messageText, messageFont, messageBrush, new RectangleF(18, 40, ClientSize.Width - 36, 41), messageFormat);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                closeTimer.Stop();
                closeTimer.Dispose();
                titleFont.Dispose();
                messageFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class CaptureEngine
    {
        private readonly AppConfig config;
        private readonly Control dispatcher;
        private int busy;
        private int settingsOpen;
        private ToastForm activeToast;

        internal CaptureEngine(AppConfig config, Control dispatcher)
        {
            this.config = config;
            this.dispatcher = dispatcher;
        }

        internal bool IsBusy
        {
            get { return Thread.VolatileRead(ref busy) != 0; }
        }

        internal bool HasActiveToast
        {
            get { return activeToast != null && !activeToast.IsDisposed; }
        }

        private string T(string chinese, string english)
        {
            return UiText.Choose(config.Language, chinese, english);
        }

        private string MenuTitle
        {
            get { return UiText.MenuTitle(config.Language); }
        }

        internal void Execute(CaptureAction action)
        {
            if (action == CaptureAction.Settings)
            {
                ShowSettings();
                return;
            }

            if (action == CaptureAction.OpenImageOutput || action == CaptureAction.OpenVideoOutput)
            {
                try
                {
                    string directory = GetCurrentMediaDirectory(action == CaptureAction.OpenImageOutput);
                    Directory.CreateDirectory(directory);
                    Process.Start("explorer.exe", Quote(directory));
                }
                catch (Exception exception)
                {
                    WriteErrorLog(exception);
                    ShowToast(T("打开文件夹失败", "Unable to open folder"), Shorten(exception.Message, 100), 5000);
                }
                return;
            }

            if (action == CaptureAction.ClearRange)
            {
                RangeState state = RangeState.Load();
                state.Clear();
                ShowToast(MenuTitle, T("入点和出点已清除。", "In and out points cleared."), 2500);
                return;
            }

            if (action == CaptureAction.MarkIn || action == CaptureAction.MarkOut)
            {
                Mark(action);
                return;
            }

            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                ShowToast(MenuTitle, T("已有导出任务正在运行。", "Another export task is already running."), 3000);
                return;
            }

            Task.Run(delegate
            {
                try
                {
                    if (action == CaptureAction.CaptureFrame) CaptureCurrentFrame();
                    if (action == CaptureAction.ExportOriginal) ExportOriginalRange();
                    if (action == CaptureAction.ExportPrecise) ExportPreciseRange();
                }
                catch (Exception exception)
                {
                    WriteErrorLog(exception);
                    ShowToast(T("导出失败", "Export failed"), Shorten(exception.Message, 100), 6500);
                }
                finally
                {
                    Interlocked.Exchange(ref busy, 0);
                }
            });
        }

        private void ShowSettings()
        {
            if (Interlocked.CompareExchange(ref settingsOpen, 1, 0) != 0)
            {
                ShowToast(MenuTitle, T("设置窗口已经打开。", "The settings window is already open."), 2500);
                return;
            }
            Action show = delegate
            {
                try
                {
                    using (SettingsForm settings = new SettingsForm(config))
                    {
                        if (settings.ShowDialog() == DialogResult.OK)
                        {
                            string message = T(
                                "新的输出位置和格式会从下一次截取或导出开始生效。",
                                "The new output location and formats apply to the next capture or export.");
                            if (settings.LanguageChanged && settings.MenuLanguageApplied)
                                message = T("语言已切换。请重新打开 PotPlayer 以刷新自定义菜单。", "Language changed. Restart PotPlayer to refresh its custom menu.");
                            else if (!settings.MenuLanguageApplied)
                                message = T("程序语言已保存，但无法更新 PotPlayer 菜单文件；请重新安装后再切换语言。", "The application language was saved, but the PotPlayer menu file could not be updated. Reinstall before changing the language again.");
                            ShowToast(T("设置已保存", "Settings saved"), message, settings.LanguageChanged ? 6500 : 4500);
                        }
                    }
                }
                finally { Interlocked.Exchange(ref settingsOpen, 0); }
            };
            if (dispatcher.InvokeRequired) dispatcher.BeginInvoke(show);
            else show();
        }

        private string GetCurrentMediaDirectory(bool imageDirectory)
        {
            IntPtr player = FindPotPlayerWindow();
            string source = GetCurrentSource(player);
            if (String.IsNullOrEmpty(source) || !File.Exists(source)) return config.LibraryRootDirectory;
            string embeddedTitle = String.Empty;
            bool embeddedTitleIsSeries = false;
            if (File.Exists(config.FfprobePath))
            {
                try
                {
                    VideoInfo info = ProbeVideo(source);
                    embeddedTitle = info.EmbeddedTitle;
                    embeddedTitleIsSeries = info.EmbeddedTitleIsSeries;
                }
                catch { }
            }
            MediaOutputPaths paths = MediaOrganizer.GetPaths(config.LibraryRootDirectory, source, embeddedTitle, embeddedTitleIsSeries);
            return imageDirectory ? paths.ImageDirectory : paths.VideoDirectory;
        }

        internal bool Seek(long milliseconds)
        {
            IntPtr player = FindPotPlayerWindow();
            if (player == IntPtr.Zero) return false;
            NativeMethods.PostMessage(player, NativeMethods.WM_USER, new IntPtr(0x5005), new IntPtr(milliseconds));
            return true;
        }

        private void Mark(CaptureAction action)
        {
            IntPtr player = FindPotPlayerWindow();
            string source = GetCurrentSource(player);
            long current = GetCurrentMilliseconds(player);
            string readError = GetPlaybackReadError(player, source, current);
            if (!String.IsNullOrEmpty(readError))
            {
                ShowToast(MenuTitle, readError, 5000);
                return;
            }

            RangeState state = RangeState.Load();
            if (!String.Equals(state.SourcePath, source, StringComparison.OrdinalIgnoreCase))
            {
                state = new RangeState();
                state.SourcePath = source;
            }

            if (action == CaptureAction.MarkIn) state.InMilliseconds = current;
            if (action == CaptureAction.MarkOut) state.OutMilliseconds = current;
            state.Save();

            string title = action == CaptureAction.MarkIn
                ? T("已设置入点", "In point set")
                : T("已设置出点", "Out point set");
            ShowToast(title, FormatTime(current) + "  |  " + Path.GetFileName(source), 3500);
        }

        private void CaptureCurrentFrame()
        {
            EnsureTools();
            IntPtr player = FindPotPlayerWindow();
            string source = GetCurrentSource(player);
            long current = GetCurrentMilliseconds(player);
            string readError = GetPlaybackReadError(player, source, current);
            if (!String.IsNullOrEmpty(readError)) throw new InvalidOperationException(readError);

            VideoInfo info = ProbeVideo(source);
            EnsureDolbyVisionDecodeIsReferenceSafe(info);
            EnsureFrameMatrixIsReferenceSafe(info);
            MediaOutputPaths paths = MediaOrganizer.GetPaths(config.LibraryRootDirectory, source, info.EmbeddedTitle, info.EmbeddedTitleIsSeries);
            string stem = BuildStem(source, current, "Frame");
            bool useTiff = CaptureFormats.NormalizeImageFormat(config.ImageFormat) == "tiff16";
            string extension = useTiff ? ".tif" : ".png";
            string imagePath = BuildAvailableOutputPath(paths.ImageDirectory, stem + "_" + GetFileColorLabel(info) + "_16bit", extension);
            string seek = Seconds(current);
            string matrix = NormalizeMatrix(info.ColorSpace);
            string range = info.ColorRange.Equals("pc", StringComparison.OrdinalIgnoreCase) ? "full" : "limited";
            string rgbPixelFormat = useTiff ? "rgb48le" : "rgb48be";
            // scale 只完成 YCbCr 到 RGB 的矩阵和码值范围变换，不套显示 LUT、不执行 PQ/HLG
            // 到 SDR 的色调映射。输出仍保留源传递函数，供后期软件按文件名与元数据解释。
            string colorTags = ",setparams=range=full:color_primaries=" + info.ColorPrimaries + ":color_trc=" + info.ColorTransfer + ":colorspace=gbr";
            string imageFilter = (IsRgbPixelFormat(info.PixelFormat)
                ? "scale=in_range=" + range + ":out_range=full,format=" + rgbPixelFormat
                : "scale=in_range=" + range + ":out_range=full:in_color_matrix=" + matrix + ":out_color_matrix=" + matrix + ",format=" + rgbPixelFormat) + colorTags;
            string formatDescription = useTiff ? "16-bit TIFF" : "16-bit PNG";

            bool createRec709 = config.ExportRec709ForHdr && (info.IsPq || info.IsHlg);
            string taskSuffix = createRec709 ? T(" · 同时生成 Rec.709 SDR", " · plus Rec.709 SDR") : String.Empty;
            ShowToast(T("正在截取当前帧", "Capturing current frame"), paths.WorkTitle + "  |  " + FormatTime(current) + " · " + GetColorDescription(info) + " · " + formatDescription + taskSuffix, 4000);

            string codecArguments = useTiff ? "-c:v tiff -compression_algo deflate -update 1" : "-c:v png -pred mixed -update 1";
            string imageArguments = "-hide_banner -loglevel error -y -ss " + seek + " -i " + Quote(source) +
                " -map 0:v:0 -frames:v 1 -vf " + Quote(imageFilter) + " " + codecArguments +
                " -color_range pc -color_primaries " + info.ColorPrimaries + " -color_trc " + info.ColorTransfer + " " + Quote(imagePath);
            RunProcess(config.FfmpegPath, imageArguments, useTiff ? "TIFF frame" : "PNG frame");

            string rec709Path = null;
            Exception rec709Failure = null;
            if (createRec709)
            {
                rec709Path = BuildAvailableOutputPath(paths.ImageDirectory, stem + "_Rec709-SDR-TONEMAPPED_16bit", extension);
                string rec709Filter = BuildRec709ToneMapFilter(info, rgbPixelFormat);
                string rec709Arguments = "-hide_banner -loglevel error -y -ss " + seek + " -i " + Quote(source) +
                    " -map 0:v:0 -frames:v 1 -vf " + Quote(rec709Filter) + " " + codecArguments +
                    " -color_range pc -color_primaries bt709 -color_trc bt709 " + Quote(rec709Path);
                try
                {
                    RunProcess(config.FfmpegPath, rec709Arguments, "Rec.709 tone-mapped frame");
                }
                catch (Exception exception)
                {
                    rec709Failure = exception;
                    WriteErrorLog(exception);
                }
            }

            string tiffWarning = useTiff ? T("  |  TIFF 请按文件名手动指定输入色彩空间", "  |  Assign TIFF input color space from the filename") : String.Empty;
            if (rec709Failure != null)
            {
                ShowToast(T("HDR 原图已完成", "HDR original completed"),
                    Path.GetFileName(imagePath) + T("  |  Rec.709 副本失败：", "  |  Rec.709 copy failed: ") + Shorten(rec709Failure.Message, 90) + tiffWarning, 9000);
                return;
            }
            string completedFiles = Path.GetFileName(imagePath);
            if (rec709Path != null) completedFiles += "  +  " + Path.GetFileName(rec709Path);
            ShowToast(T("当前帧已完成", "Frame capture completed"), completedFiles + GetColorWarning(info) + tiffWarning,
                info.IsDolbyVision || info.MetadataAssumed || useTiff || rec709Path != null ? 8000 : 5000);
        }

        internal static string BuildRec709ToneMapFilter(VideoInfo info, string rgbPixelFormat)
        {
            string inputRange = info.ColorRange.Equals("pc", StringComparison.OrdinalIgnoreCase) ? "full" : "limited";
            string inputMatrix = NormalizeZscaleMatrix(info.ColorSpace);
            return "zscale=rin=" + inputRange + ":pin=" + info.ColorPrimaries + ":tin=" + info.ColorTransfer +
                ":min=" + inputMatrix + ":t=linear:npl=100,format=gbrpf32le," +
                "tonemap=mobius:param=0.3:desat=2," +
                "zscale=p=bt709:t=bt709:m=gbr:r=full,format=" + rgbPixelFormat +
                ",setparams=range=full:color_primaries=bt709:color_trc=bt709:colorspace=gbr";
        }

        private void ExportOriginalRange()
        {
            EnsureTools();
            RangeState range = GetValidRange();
            VideoInfo info = ProbeVideo(range.SourcePath);
            MediaOutputPaths paths = MediaOrganizer.GetPaths(config.LibraryRootDirectory, range.SourcePath, info.EmbeddedTitle, info.EmbeddedTitleIsSeries);
            string output = BuildAvailableOutputPath(paths.VideoDirectory,
                BuildRangeStem(range.SourcePath, range.InMilliseconds, range.OutMilliseconds, "Original") + "_" + GetFileColorLabel(info), ".mkv");
            long duration = range.OutMilliseconds - range.InMilliseconds;
            ShowToast(T("正在导出原码片段", "Exporting source clip"), FormatDuration(duration) + "  |  " + GetColorDescription(info) + T(" · 保留源编码与原音频", " · source codec and original audio"), 4000);

            // 流复制可以保留源视频、音频、字幕及容器元数据，但起点受关键帧结构约束。
            // 需要严格切点时由“精确片段”承担重新编码成本。
            string arguments = "-hide_banner -loglevel error -y -ss " + Seconds(range.InMilliseconds) +
                " -i " + Quote(range.SourcePath) + " -t " + Seconds(duration) +
                " -map 0:v:0 -map 0:a? -map 0:s? -map_metadata 0 -map_chapters 0 -c copy -avoid_negative_ts make_zero " + Quote(output);
            RunProcess(config.FfmpegPath, arguments, "original range");
            ShowToast(T("原码片段已完成", "Source clip completed"), Path.GetFileName(output), 5500);
        }

        private void ExportPreciseRange()
        {
            EnsureTools();
            RangeState range = GetValidRange();
            VideoInfo info = ProbeVideo(range.SourcePath);
            EnsureDolbyVisionDecodeIsReferenceSafe(info);
            MediaOutputPaths paths = MediaOrganizer.GetPaths(config.LibraryRootDirectory, range.SourcePath, info.EmbeddedTitle, info.EmbeddedTitleIsSeries);
            VideoEncodingSpec encoding = CaptureFormats.GetVideoSpec(config.VideoPreset);
            string output = BuildAvailableOutputPath(paths.VideoDirectory,
                BuildRangeStem(range.SourcePath, range.InMilliseconds, range.OutMilliseconds, "Precise") + "_" + GetFileColorLabel(info) + "_" + encoding.Token,
                encoding.Extension);
            long duration = range.OutMilliseconds - range.InMilliseconds;
            ShowToast(T("正在导出精确片段", "Exporting precise clip"), FormatDuration(duration) + "  |  " + GetColorDescription(info) + " · " + encoding.DisplayName + " + PCM", 4500);

            string arguments = "-hide_banner -loglevel error -y -ss " + Seconds(range.InMilliseconds) +
                " -i " + Quote(range.SourcePath) + " -t " + Seconds(duration) +
                " -map 0:v:0 -map 0:a? -map_metadata 0 -vf " + Quote("format=" + encoding.PixelFormat +
                    ",setparams=range=" + NormalizeFilterRange(info.ColorRange) + ":color_primaries=" + info.ColorPrimaries +
                    ":color_trc=" + info.ColorTransfer + ":colorspace=" + NormalizeOutputColorSpace(info)) + " " + encoding.CodecArguments +
                " -pix_fmt " + encoding.PixelFormat + " -color_range " + NormalizeRange(info.ColorRange) +
                " -colorspace " + NormalizeOutputColorSpace(info) + " -color_primaries " + info.ColorPrimaries +
                " -color_trc " + info.ColorTransfer + " -c:a pcm_s24le -ar 48000 -movflags +write_colr " + Quote(output);
            RunProcess(config.FfmpegPath, arguments, "precise range");
            ShowToast(T("精确片段已完成", "Precise clip completed"), Path.GetFileName(output) + GetColorWarning(info), info.IsDolbyVision || info.MetadataAssumed ? 8000 : 5500);
        }

        private RangeState GetValidRange()
        {
            RangeState range = RangeState.Load();
            IntPtr player = FindPotPlayerWindow();
            string source = GetCurrentSource(player);
            if (String.IsNullOrEmpty(range.SourcePath) || range.InMilliseconds < 0 || range.OutMilliseconds < 0)
                throw new InvalidOperationException(T("请先在右键菜单中设置入点和出点。", "Set an in point and an out point from the context menu first."));
            if (range.OutMilliseconds <= range.InMilliseconds)
                throw new InvalidOperationException(T("出点必须晚于入点。", "The out point must be later than the in point."));
            if (!String.IsNullOrEmpty(source) && !String.Equals(source, range.SourcePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(T("当前播放文件与已设置入出点的文件不同。", "The current media file is different from the file used for the saved in and out points."));
            if (!File.Exists(range.SourcePath))
                throw new FileNotFoundException(T("找不到入出点对应的源文件。", "The source file for the saved in and out points was not found."), range.SourcePath);
            return range;
        }

        private VideoInfo ProbeVideo(string source)
        {
            string arguments = "-v error -select_streams v:0 -show_entries stream=width,height,color_space,color_transfer,color_primaries,color_range,pix_fmt:stream_side_data=side_data_type,dv_profile,dv_bl_signal_compatibility_id -of default=noprint_wrappers=1 " + Quote(source);
            string output = RunProcess(config.FfprobePath, arguments, "probe");
            VideoInfo info = new VideoInfo();
            foreach (string rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = rawLine.IndexOf('=');
                if (separator <= 0) continue;
                string key = rawLine.Substring(0, separator).Trim();
                string value = rawLine.Substring(separator + 1).Trim();
                if (value.Length == 0 || value.Equals("unknown", StringComparison.OrdinalIgnoreCase)) continue;
                int parsed;
                if (key == "width" && Int32.TryParse(value, out parsed)) info.Width = parsed;
                if (key == "height" && Int32.TryParse(value, out parsed)) info.Height = parsed;
                if (key == "color_space") info.ColorSpace = value;
                if (key == "color_transfer") info.ColorTransfer = value;
                if (key == "color_primaries") info.ColorPrimaries = value;
                if (key == "color_range") info.ColorRange = value;
                if (key == "pix_fmt") info.PixelFormat = value;
                if (key == "side_data_type" && value.IndexOf("DOVI", StringComparison.OrdinalIgnoreCase) >= 0) info.IsDolbyVision = true;
                if (key == "dv_profile" && Int32.TryParse(value, out parsed))
                {
                    info.IsDolbyVision = true;
                    info.DolbyVisionProfile = parsed;
                }
                if (key == "dv_bl_signal_compatibility_id" && Int32.TryParse(value, out parsed)) info.DolbyVisionCompatibilityId = parsed;
            }
            ReadContainerTitle(source, info);
            ApplyColorMetadataFallbacks(info);
            return info;
        }

        private void ReadContainerTitle(string source, VideoInfo info)
        {
            string arguments = "-v error -show_entries format_tags=title,show,series,TVSHOW -of default=noprint_wrappers=1 " + Quote(source);
            string output = RunProcess(config.FfprobePath, arguments, "title probe");
            string fallbackTitle = String.Empty;
            foreach (string rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = rawLine.IndexOf('=');
                if (separator <= 0) continue;
                string key = rawLine.Substring(0, separator).Trim();
                string value = rawLine.Substring(separator + 1).Trim();
                if (value.Length == 0) continue;
                if (key.Equals("TAG:show", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("TAG:series", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("TAG:TVSHOW", StringComparison.OrdinalIgnoreCase))
                {
                    info.EmbeddedTitle = value;
                    info.EmbeddedTitleIsSeries = true;
                    return;
                }
                if (key.Equals("TAG:title", StringComparison.OrdinalIgnoreCase) && fallbackTitle.Length == 0) fallbackTitle = value;
            }
            info.EmbeddedTitle = fallbackTitle;
        }

        private static void ApplyColorMetadataFallbacks(VideoInfo info)
        {
            bool useNtscSdDefaults = info.Height > 0 && info.Height <= 480;
            bool usePalSdDefaults = info.Height > 480 && info.Height <= 576;
            string sdOrHdPrimaries = useNtscSdDefaults ? "smpte170m" : (usePalSdDefaults ? "bt470bg" : "bt709");
            string sdOrHdTransfer = useNtscSdDefaults ? "smpte170m" : (usePalSdDefaults ? "bt470bg" : "bt709");
            string sdOrHdMatrix = useNtscSdDefaults ? "smpte170m" : (usePalSdDefaults ? "bt470bg" : "bt709");

            if (IsUnknown(info.ColorTransfer))
            {
                info.ColorTransfer = sdOrHdTransfer;
                info.MetadataAssumed = true;
            }
            if (IsUnknown(info.ColorPrimaries))
            {
                info.ColorPrimaries = info.IsPq || info.IsHlg ? "bt2020" : sdOrHdPrimaries;
                info.MetadataAssumed = true;
            }
            if (IsUnknown(info.ColorSpace))
            {
                if (info.ColorPrimaries.Equals("bt2020", StringComparison.OrdinalIgnoreCase)) info.ColorSpace = "bt2020nc";
                else info.ColorSpace = sdOrHdMatrix;
                info.MetadataAssumed = true;
            }
            if (IsUnknown(info.ColorRange))
            {
                info.ColorRange = IsRgbPixelFormat(info.PixelFormat) ? "pc" : "tv";
                info.MetadataAssumed = true;
            }
        }

        private void EnsureDolbyVisionDecodeIsReferenceSafe(VideoInfo info)
        {
            if (!info.IsDolbyVision) return;
            // Profile 5 等源通常没有可独立解释的 HDR10/HLG 基础层。直接交给普通 FFmpeg
            // 解码会得到偏色画面，因此宁可拒绝生成参照，也不输出看似正常的错误图片。
            if (info.DolbyVisionProfile == 5 || info.DolbyVisionCompatibilityId == 0)
                throw new InvalidOperationException(T(
                    "该 Dolby Vision 源没有可可靠解码的 HDR10/HLG 兼容基础层，不能生成准确参照。请使用带兼容基础层的 Profile 7/8 版本；原码片段仍可保留。",
                    "This Dolby Vision source has no reliably decodable HDR10/HLG-compatible base layer. Use a Profile 7/8 source with a compatible base layer; source-copy clips can still be preserved."));
        }

        private void EnsureFrameMatrixIsReferenceSafe(VideoInfo info)
        {
            if (IsRgbPixelFormat(info.PixelFormat)) return;
            string matrix = info.ColorSpace.ToLowerInvariant();
            if (matrix == "bt709" || matrix == "bt2020nc" || matrix == "bt2020ncl" || matrix == "bt470bg" ||
                matrix == "smpte170m" || matrix == "fcc" || matrix == "smpte240m") return;
            throw new InvalidOperationException(T(
                "该片源使用插件尚不能无损还原的 YUV 矩阵（" + info.ColorSpace + "），已停止截图以避免生成错误参照。原码片段仍可保留。",
                "This source uses a YUV matrix that FrameClip cannot reconstruct safely (" + info.ColorSpace + "). Frame capture was stopped to avoid an incorrect reference; source-copy clips remain available."));
        }

        private static bool IsUnknown(string value)
        {
            return String.IsNullOrEmpty(value) || value.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("unspecified", StringComparison.OrdinalIgnoreCase) || value.Equals("reserved", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetFileColorLabel(VideoInfo info)
        {
            string label = GetBaseFileColorLabel(info);
            if (info.IsDolbyVision)
                label = "DV" + (info.DolbyVisionProfile > 0 ? "-P" + info.DolbyVisionProfile.ToString(CultureInfo.InvariantCulture) : String.Empty) + "-Base-" + label;
            return label + (info.MetadataAssumed ? "-ASSUMED" : String.Empty);
        }

        private static string GetBaseFileColorLabel(VideoInfo info)
        {
            if (info.IsPq && info.ColorPrimaries.Equals("bt2020", StringComparison.OrdinalIgnoreCase)) return "Rec2100-PQ";
            if (info.IsHlg && info.ColorPrimaries.Equals("bt2020", StringComparison.OrdinalIgnoreCase)) return "Rec2100-HLG";
            if (info.IsP3) return "P3-" + TransferFileToken(info.ColorTransfer);
            if (info.IsRec709) return "Rec709-" + TransferFileToken(info.ColorTransfer);
            return SanitizeToken(info.ColorPrimaries) + "-" + TransferFileToken(info.ColorTransfer);
        }

        private string GetColorDescription(VideoInfo info)
        {
            if (info.IsDolbyVision)
            {
                string baseDescription = info.IsPq ? "Rec.2100 PQ" : (info.IsHlg ? "Rec.2100 HLG" : info.ColorPrimaries + " / " + TransferDisplayName(info.ColorTransfer));
                return "Dolby Vision" + (info.DolbyVisionProfile > 0 ? " P" + info.DolbyVisionProfile.ToString(CultureInfo.InvariantCulture) : String.Empty) + " / " + baseDescription + T(" 基础层", " base layer");
            }
            if (info.IsPq) return "Rec.2100 PQ";
            if (info.IsHlg) return "Rec.2100 HLG";
            if (info.IsP3) return "P3 / " + TransferDisplayName(info.ColorTransfer);
            if (info.IsRec709) return "Rec.709 / " + TransferDisplayName(info.ColorTransfer);
            return info.ColorPrimaries + " / " + TransferDisplayName(info.ColorTransfer);
        }

        private string GetColorWarning(VideoInfo info)
        {
            if (info.IsDolbyVision && info.MetadataAssumed) return T("  |  DV 动态映射未烘焙；部分色彩元数据为推定值", "  |  DV dynamic mapping is not baked in; some color metadata was inferred");
            if (info.IsDolbyVision) return T("  |  DV 动态映射未烘焙，输出为可解码基础层", "  |  DV dynamic mapping is not baked in; output uses the decodable base layer");
            if (info.MetadataAssumed) return T("  |  源文件色彩元数据不完整，文件名已标记 ASSUMED", "  |  Source color metadata is incomplete; the filename is marked ASSUMED");
            return String.Empty;
        }

        private static string TransferFileToken(string transfer)
        {
            if (transfer.Equals("smpte2084", StringComparison.OrdinalIgnoreCase)) return "PQ";
            if (transfer.Equals("arib-std-b67", StringComparison.OrdinalIgnoreCase)) return "HLG";
            if (transfer.Equals("iec61966-2-1", StringComparison.OrdinalIgnoreCase)) return "sRGB";
            if (transfer.Equals("bt709", StringComparison.OrdinalIgnoreCase)) return "BT709";
            if (transfer.Equals("linear", StringComparison.OrdinalIgnoreCase)) return "Linear";
            return SanitizeToken(transfer);
        }

        private static string TransferDisplayName(string transfer)
        {
            if (transfer.Equals("smpte2084", StringComparison.OrdinalIgnoreCase)) return "PQ";
            if (transfer.Equals("arib-std-b67", StringComparison.OrdinalIgnoreCase)) return "HLG";
            if (transfer.Equals("iec61966-2-1", StringComparison.OrdinalIgnoreCase)) return "sRGB";
            if (transfer.Equals("bt709", StringComparison.OrdinalIgnoreCase)) return "BT.709";
            return transfer;
        }

        private static string SanitizeToken(string value)
        {
            StringBuilder token = new StringBuilder();
            foreach (char character in value)
            {
                if (Char.IsLetterOrDigit(character) || character == '-') token.Append(character);
                else token.Append('-');
            }
            return token.Length == 0 ? "Unknown" : token.ToString();
        }

        private static bool IsRgbPixelFormat(string pixelFormat)
        {
            if (String.IsNullOrEmpty(pixelFormat)) return false;
            string format = pixelFormat.ToLowerInvariant();
            return format.StartsWith("rgb", StringComparison.Ordinal) || format.StartsWith("bgr", StringComparison.Ordinal) ||
                format.StartsWith("gbr", StringComparison.Ordinal) || format.StartsWith("rgba", StringComparison.Ordinal) ||
                format.StartsWith("bgra", StringComparison.Ordinal);
        }

        private static int GetProcessTimeoutMilliseconds(string operation)
        {
            string value = operation ?? String.Empty;
            if (value.IndexOf("probe", StringComparison.OrdinalIgnoreCase) >= 0) return 2 * 60 * 1000;
            if (value.IndexOf("frame", StringComparison.OrdinalIgnoreCase) >= 0) return 10 * 60 * 1000;
            return 12 * 60 * 60 * 1000;
        }

        internal string RunProcess(string executable, string arguments, string operation, int timeoutMilliseconds = 0)
        {
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = executable;
            start.Arguments = arguments;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.StandardOutputEncoding = Encoding.UTF8;
            start.StandardErrorEncoding = Encoding.UTF8;

            using (Process process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException(operation + T(" 无法启动。", " could not be started."));
                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();
                int timeout = timeoutMilliseconds > 0 ? timeoutMilliseconds : GetProcessTimeoutMilliseconds(operation);
                if (!process.WaitForExit(timeout))
                {
                    try { process.Kill(); }
                    catch { }
                    try { process.WaitForExit(5000); }
                    catch { }
                    try { Task.WaitAll(new Task[] { stdoutTask, stderrTask }, 5000); }
                    catch { }
                    throw new TimeoutException(operation + T(" 超时，已终止外部进程。", " timed out and the external process was terminated."));
                }
                Task.WaitAll(stdoutTask, stderrTask);
                string stdout = stdoutTask.Result;
                string stderr = stderrTask.Result;
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(operation + T(" 失败：", " failed: ") + Shorten(stderr.Trim(), 240));
                return stdout;
            }
        }

        private void EnsureTools()
        {
            config.LocateTools();
            if (!File.Exists(config.FfmpegPath) || !File.Exists(config.FfprobePath))
                throw new FileNotFoundException(T(
                    "找不到 FFmpeg/FFprobe。请打开“参照帧与片段截取 > 设置”选择 ffmpeg.exe，或将 FFmpeg 加入 PATH。",
                    "FFmpeg/FFprobe was not found. Open Reference Frame & Clip Capture > Settings and select ffmpeg.exe, or add FFmpeg to PATH."), config.FfmpegPath);
        }

        private IntPtr FindPotPlayerWindow()
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (IsPotPlayerWindow(foreground)) return NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOT);

            string[] processNames = new[] { "PotPlayerMini64", "PotPlayer64", "PotPlayerMini", "PotPlayer" };
            foreach (string processName in processNames)
            {
                foreach (Process process in Process.GetProcessesByName(processName))
                {
                    if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;
                }
            }
            return IntPtr.Zero;
        }

        private bool IsPotPlayerWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            uint processId;
            NativeMethods.GetWindowThreadProcessId(NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT), out processId);
            if (processId == 0) return false;
            try
            {
                string name = Process.GetProcessById((int)processId).ProcessName;
                return name.IndexOf("PotPlayer", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private string GetCurrentSource(IntPtr player)
        {
            if (player == IntPtr.Zero) return null;
            string title = GetWindowTitle(player);
            const string suffix = " - PotPlayer";
            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) title = title.Substring(0, title.Length - suffix.Length);
            title = title.Trim();
            if (Path.IsPathRooted(title) && File.Exists(title)) return title;

            // PotPlayer 的窗口标题通常只有文件名。播放器在连续播放、播放列表切换或
            // 退出前尚未落盘时，各数据源可能短暂不同步，因此先收集可信路径，再在
            // 这些路径所在目录中恢复当前标题对应的文件。
            List<string> trustedPaths = new List<string>();

            string playerDirectory = GetPlayerDirectory(player);
            if (!String.IsNullOrEmpty(playerDirectory))
            {
                string playlistDirectory = Path.Combine(playerDirectory, "Playlist");
                string fromPlaylists = FindSourceInPlaylists(playlistDirectory, title, trustedPaths);
                if (!String.IsNullOrEmpty(fromPlaylists)) return fromPlaylists;

                string[] iniNames = new[] { "PotPlayerMini64.ini", "PotPlayerMini.ini", "PotPlayer64.ini", "PotPlayer.ini" };
                foreach (string iniName in iniNames)
                {
                    string fromIni = FindSourceInIni(Path.Combine(playerDirectory, iniName), title, trustedPaths);
                    if (!String.IsNullOrEmpty(fromIni)) return fromIni;
                }
            }

            foreach (string path in PotPlayerMediaLocator.ExtractExistingMediaArguments(GetProcessCommandLine(player)))
                trustedPaths.Add(path);
            return PotPlayerMediaLocator.FindExactOrSibling(title, trustedPaths);
        }

        private string GetPlayerDirectory(IntPtr player)
        {
            uint processId;
            NativeMethods.GetWindowThreadProcessId(player, out processId);
            if (processId == 0) return null;
            try
            {
                return Path.GetDirectoryName(Process.GetProcessById((int)processId).MainModule.FileName);
            }
            catch
            {
                return null;
            }
        }

        private string FindSourceInPlaylists(string directory, string title, ICollection<string> trustedPaths)
        {
            if (!Directory.Exists(directory)) return null;
            foreach (string playlist in Directory.GetFiles(directory, "*.dpl"))
            {
                try
                {
                    using (StreamReader reader = new StreamReader(playlist, true))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            string candidate = null;
                            if (line.StartsWith("playname=", StringComparison.OrdinalIgnoreCase)) candidate = line.Substring(9);
                            else
                            {
                                int fileMarker = line.IndexOf("*file*", StringComparison.OrdinalIgnoreCase);
                                if (fileMarker >= 0) candidate = line.Substring(fileMarker + 6);
                            }
                            if (String.IsNullOrEmpty(candidate)) continue;
                            candidate = candidate.Trim();
                            if (!File.Exists(candidate)) continue;
                            candidate = Path.GetFullPath(candidate);
                            trustedPaths.Add(candidate);
                            if (PotPlayerMediaLocator.TitleMatchesPath(title, candidate)) return candidate;
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        private string FindSourceInIni(string iniPath, string title, ICollection<string> trustedPaths)
        {
            if (!File.Exists(iniPath)) return null;
            try
            {
                bool inMediaList = false;
                foreach (string line in File.ReadAllLines(iniPath, Encoding.UTF8))
                {
                    if (line.StartsWith("[", StringComparison.Ordinal))
                    {
                        inMediaList = line.Equals("[BMList]", StringComparison.OrdinalIgnoreCase) ||
                            line.Equals("[RememberFiles]", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    if (!inMediaList) continue;
                    int separator = line.IndexOf('=');
                    if (separator <= 0) continue;
                    string candidate = PotPlayerMediaLocator.ExtractPathFromIniValue(line.Substring(separator + 1));
                    if (String.IsNullOrEmpty(candidate) || !File.Exists(candidate)) continue;
                    candidate = Path.GetFullPath(candidate);
                    trustedPaths.Add(candidate);
                    if (PotPlayerMediaLocator.TitleMatchesPath(title, candidate)) return candidate;
                }
            }
            catch { }
            return null;
        }

        private string GetProcessCommandLine(IntPtr player)
        {
            uint processId;
            NativeMethods.GetWindowThreadProcessId(player, out processId);
            if (processId == 0) return null;
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT CommandLine FROM Win32_Process WHERE ProcessId=" + processId.ToString(CultureInfo.InvariantCulture)))
                using (ManagementObjectCollection results = searcher.Get())
                {
                    foreach (ManagementObject item in results)
                        return item["CommandLine"] as string;
                }
            }
            catch { }
            return null;
        }

        private string GetPlaybackReadError(IntPtr player, string source, long current)
        {
            if (player == IntPtr.Zero) return T("未找到正在播放的 PotPlayer 窗口。", "No active PotPlayer playback window was found.");
            if (String.IsNullOrEmpty(source))
            {
                string title = GetWindowTitle(player);
                return T("已找到 PotPlayer，但无法定位当前本地媒体文件。窗口标题：", "PotPlayer was found, but the current local media file could not be located. Window title: ") + Shorten(title, 72);
            }
            if (current < 0) return T("已定位当前媒体，但无法读取 PotPlayer 播放时间。", "The current media file was located, but the PotPlayer playback time could not be read.");
            return null;
        }

        private long GetCurrentMilliseconds(IntPtr player)
        {
            if (player == IntPtr.Zero) return -1;
            return NativeMethods.SendMessage(player, NativeMethods.WM_USER, new IntPtr(0x5004), IntPtr.Zero).ToInt64();
        }

        private string GetWindowTitle(IntPtr hwnd)
        {
            StringBuilder text = new StringBuilder(2048);
            NativeMethods.GetWindowText(hwnd, text, text.Capacity);
            return text.ToString();
        }

        private string BuildStem(string source, long milliseconds, string kind)
        {
            string baseName = (Path.GetFileNameWithoutExtension(source) ?? "Media").Normalize(NormalizationForm.FormC);
            foreach (char invalid in Path.GetInvalidFileNameChars()) baseName = baseName.Replace(invalid, '_');
            baseName = baseName.Trim(' ', '.');
            if (baseName.Length > 56)
                baseName = baseName.Substring(0, 47).TrimEnd(' ', '.') + "_" + MediaOrganizer.StableShortHash(baseName);
            if (baseName.Length == 0) baseName = "Media";
            return baseName + "_" + FileTime(milliseconds) + "_" + kind;
        }

        private string BuildRangeStem(string source, long inMilliseconds, long outMilliseconds, string kind)
        {
            string stem = BuildStem(source, inMilliseconds, "IN");
            return stem + "_TO_" + FileTime(outMilliseconds) + "_" + kind;
        }

        private string BuildAvailableOutputPath(string directory, string stem, string extension)
        {
            const int conservativePathLimit = 240;
            int maximumStemLength = conservativePathLimit - directory.Length - extension.Length - 1;
            if (maximumStemLength < 24)
                throw new PathTooLongException(T("输出目录路径过长，请在插件设置中选择更短的参考素材库根目录。", "The output path is too long. Choose a shorter reference-library root folder in Settings."));

            string fittedStem = FitOutputStem(stem, maximumStemLength);
            string candidate = Path.Combine(directory, fittedStem + extension);
            if (!File.Exists(candidate)) return candidate;

            for (int number = 2; number < 10000; number++)
            {
                string suffix = "_" + number.ToString(CultureInfo.InvariantCulture);
                fittedStem = FitOutputStem(stem, maximumStemLength - suffix.Length) + suffix;
                candidate = Path.Combine(directory, fittedStem + extension);
                if (!File.Exists(candidate)) return candidate;
            }
            throw new IOException(T("同一时间码的输出文件过多，请整理该作品目录后重试。", "Too many files exist for the same timecode. Organize the title folder and try again."));
        }

        private static string FitOutputStem(string stem, int maximumLength)
        {
            string normalized = String.IsNullOrWhiteSpace(stem) ? "Media" : stem.Normalize(NormalizationForm.FormC).Trim(' ', '.');
            if (normalized.Length <= maximumLength) return normalized;
            string suffix = "_" + MediaOrganizer.StableShortHash(normalized);
            int prefixLength = Math.Max(1, maximumLength - suffix.Length);
            return normalized.Substring(0, prefixLength).TrimEnd(' ', '.') + suffix;
        }

        private static string FileTime(long milliseconds)
        {
            TimeSpan time = TimeSpan.FromMilliseconds(milliseconds);
            return String.Format(CultureInfo.InvariantCulture, "{0:00}-{1:00}-{2:00}-{3:000}", (int)time.TotalHours, time.Minutes, time.Seconds, time.Milliseconds);
        }

        private static string FormatTime(long milliseconds)
        {
            TimeSpan time = TimeSpan.FromMilliseconds(milliseconds);
            return String.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}.{3:000}", (int)time.TotalHours, time.Minutes, time.Seconds, time.Milliseconds);
        }

        private static string FormatDuration(long milliseconds)
        {
            TimeSpan time = TimeSpan.FromMilliseconds(milliseconds);
            return String.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}.{3:000}", (int)time.TotalHours, time.Minutes, time.Seconds, time.Milliseconds);
        }

        private static string Seconds(long milliseconds)
        {
            return (milliseconds / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
        }

        private static string NormalizeRange(string range)
        {
            return range.Equals("pc", StringComparison.OrdinalIgnoreCase) ? "pc" : "tv";
        }

        private static string NormalizeFilterRange(string range)
        {
            return range.Equals("pc", StringComparison.OrdinalIgnoreCase) ? "full" : "limited";
        }

        private static string NormalizeMatrix(string matrix)
        {
            if (matrix.Equals("bt2020nc", StringComparison.OrdinalIgnoreCase) || matrix.Equals("bt2020ncl", StringComparison.OrdinalIgnoreCase)) return "bt2020";
            if (matrix.Equals("bt470bg", StringComparison.OrdinalIgnoreCase) || matrix.Equals("smpte170m", StringComparison.OrdinalIgnoreCase)) return "bt601";
            if (matrix.Equals("fcc", StringComparison.OrdinalIgnoreCase)) return "fcc";
            if (matrix.Equals("smpte240m", StringComparison.OrdinalIgnoreCase)) return "smpte240m";
            return "bt709";
        }

        private static string NormalizeZscaleMatrix(string matrix)
        {
            string value = (matrix ?? String.Empty).ToLowerInvariant();
            if (value == "bt2020ncl") return "bt2020nc";
            if (value == "bt2020nc" || value == "bt709" || value == "bt470bg" || value == "smpte170m" ||
                value == "fcc" || value == "smpte240m") return value;
            return "bt709";
        }

        private static string NormalizeOutputColorSpace(VideoInfo info)
        {
            string matrix = info.ColorSpace.ToLowerInvariant();
            if (matrix == "bt2020ncl") return "bt2020nc";
            if (matrix == "bt709" || matrix == "fcc" || matrix == "bt470bg" || matrix == "smpte170m" ||
                matrix == "smpte240m" || matrix == "ycgco" || matrix == "bt2020nc" || matrix == "bt2020c" || matrix == "ictcp") return matrix;
            return info.ColorPrimaries.Equals("bt2020", StringComparison.OrdinalIgnoreCase) ? "bt2020nc" : "bt709";
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string Shorten(string value, int maxLength)
        {
            if (String.IsNullOrEmpty(value)) return "未知错误";
            string flat = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return flat.Length <= maxLength ? flat : flat.Substring(0, maxLength - 3) + "...";
        }

        private void ShowToast(string title, string message, int milliseconds)
        {
            Action show = delegate
            {
                ToastForm previousToast = activeToast;
                if (previousToast != null && !previousToast.IsDisposed)
                {
                    // Close 会同步触发 FormClosed，并可能把 activeToast 字段清空；后续释放
                    // 必须使用局部引用，避免成功任务被误记为 NullReferenceException。
                    previousToast.Close();
                    previousToast.Dispose();
                }
                IntPtr player = FindPotPlayerWindow();
                // 导出期间用户可能关闭 PotPlayer，旧窗口句柄在这里会形成一个很窄的竞态。
                // Screen.FromHandle 对刚销毁的句柄可能返回 null，因此逐级回退到主屏幕和
                // 系统工作区，确保任务完成提示本身不会把成功导出记录成错误。
                Screen targetScreen = player == IntPtr.Zero ? null : Screen.FromHandle(player);
                if (targetScreen == null) targetScreen = Screen.PrimaryScreen;
                Rectangle workingArea = targetScreen == null ? SystemInformation.WorkingArea : targetScreen.WorkingArea;
                ToastForm toast = new ToastForm(title, message, milliseconds, workingArea);
                activeToast = toast;
                toast.FormClosed += delegate
                {
                    if (Object.ReferenceEquals(activeToast, toast)) activeToast = null;
                };
                toast.Show();
            };
            if (dispatcher.InvokeRequired) dispatcher.BeginInvoke(show);
            else show();
        }

        private void WriteErrorLog(Exception exception)
        {
            try
            {
                string directory = Path.Combine(RangeState.StateDirectory, "logs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log");
                File.WriteAllText(path, exception.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    internal sealed class NativeBridgeHook
    {
        internal int ProcessId;
        internal uint ThreadId;
        internal IntPtr Handle;
    }

    internal sealed class NativeBridgeMessageWindow : NativeWindow, IDisposable
    {
        internal const int ActionMessage = 0x8000 + 0x4C0;
        internal const string WindowCaption = "PotPlayerFrameClip.NativeBridge";
        private readonly CaptureEngine engine;
        private readonly Control dispatcher;

        internal NativeBridgeMessageWindow(CaptureEngine engine, Control dispatcher)
        {
            this.engine = engine;
            this.dispatcher = dispatcher;
            CreateParams parameters = new CreateParams();
            parameters.Caption = WindowCaption;
            parameters.Style = 0;
            parameters.ExStyle = 0x00000080;
            CreateHandle(parameters);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == ActionMessage)
            {
                int value = message.WParam.ToInt32();
                if (value >= (int)CaptureAction.CaptureFrame && value <= (int)CaptureAction.OpenVideoOutput)
                {
                    CaptureAction action = (CaptureAction)value;
                    dispatcher.BeginInvoke(new Action(delegate { engine.Execute(action); }));
                }
                message.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero) DestroyHandle();
        }
    }

    internal sealed class NativeBridgeManager : IDisposable
    {
        private readonly object syncRoot = new object();
        private readonly Dictionary<int, NativeBridgeHook> hooks = new Dictionary<int, NativeBridgeHook>();
        private readonly Action<string> trace;
        private readonly System.Threading.Timer refreshTimer;
        private IntPtr bridgeModule;
        private IntPtr bridgeProcedure;
        private Process x86Host;
        private int refreshRunning;
        private bool disposed;

        internal NativeBridgeManager(Action<string> trace)
        {
            this.trace = trace;
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            StartX86Host(Path.Combine(directory, "FrameClipBridgeHost32.exe"));
            if (Environment.Is64BitOperatingSystem && Environment.Is64BitProcess)
            {
                string bridgePath = Path.Combine(directory, "FrameClipBridge64.dll");
                if (File.Exists(bridgePath))
                {
                    bridgeModule = NativeMethods.LoadLibrary(bridgePath);
                    if (bridgeModule != IntPtr.Zero)
                        bridgeProcedure = NativeMethods.GetProcAddress(bridgeModule, "FrameClipMouseProc");
                }
                if (bridgeModule == IntPtr.Zero || bridgeProcedure == IntPtr.Zero)
                    Trace("native-bridge64-load-failed " + Marshal.GetLastWin32Error());
            }
            refreshTimer = new System.Threading.Timer(Refresh, null, 0, 1000);
        }

        private void StartX86Host(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo(path, "--parent-pid " + Process.GetCurrentProcess().Id)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                x86Host = Process.Start(startInfo);
                Trace("native-bridge32-host " + (x86Host == null ? 0 : x86Host.Id));
            }
            catch (Exception exception)
            {
                Trace("native-bridge32-host-failed " + exception.GetType().Name);
            }
        }

        private void Refresh(object state)
        {
            if (disposed || bridgeModule == IntPtr.Zero || bridgeProcedure == IntPtr.Zero ||
                Interlocked.CompareExchange(ref refreshRunning, 1, 0) != 0) return;
            try
            {
                HashSet<int> active = new HashSet<int>();
                foreach (Process process in Process.GetProcesses())
                {
                    try
                    {
                        string name = process.ProcessName;
                        if (name.IndexOf("PotPlayer", StringComparison.OrdinalIgnoreCase) < 0 ||
                            name.IndexOf("FrameClip", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("64", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        IntPtr window = process.MainWindowHandle;
                        if (window == IntPtr.Zero) continue;
                        uint processId;
                        uint threadId = NativeMethods.GetWindowThreadProcessId(window, out processId);
                        if (threadId == 0) continue;
                        active.Add(process.Id);
                        EnsureHook(process.Id, threadId);
                    }
                    catch { }
                    finally { process.Dispose(); }
                }
                RemoveInactiveHooks(active);
            }
            finally
            {
                Interlocked.Exchange(ref refreshRunning, 0);
            }
        }

        private void EnsureHook(int processId, uint threadId)
        {
            lock (syncRoot)
            {
                NativeBridgeHook existing;
                if (hooks.TryGetValue(processId, out existing) && existing.ThreadId == threadId && existing.Handle != IntPtr.Zero)
                    return;
                if (existing != null && existing.Handle != IntPtr.Zero)
                    NativeMethods.UnhookWindowsHookEx(existing.Handle);
                IntPtr hook = NativeMethods.SetWindowsHookExNative(NativeMethods.WH_MOUSE, bridgeProcedure, bridgeModule, threadId);
                if (hook == IntPtr.Zero)
                {
                    Trace("native-bridge64-hook-failed pid=" + processId + " error=" + Marshal.GetLastWin32Error());
                    return;
                }
                hooks[processId] = new NativeBridgeHook { ProcessId = processId, ThreadId = threadId, Handle = hook };
                Trace("native-bridge64-hooked pid=" + processId + " thread=" + threadId);
            }
        }

        private void RemoveInactiveHooks(HashSet<int> active)
        {
            lock (syncRoot)
            {
                foreach (int processId in hooks.Keys.Where(delegate(int value) { return !active.Contains(value); }).ToArray())
                {
                    NativeBridgeHook entry = hooks[processId];
                    if (entry.Handle != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(entry.Handle);
                    hooks.Remove(processId);
                    Trace("native-bridge64-unhooked pid=" + processId);
                }
            }
        }

        private void Trace(string message)
        {
            try { if (trace != null) trace(message); }
            catch { }
        }

        public void Dispose()
        {
            disposed = true;
            refreshTimer.Dispose();
            lock (syncRoot)
            {
                foreach (NativeBridgeHook entry in hooks.Values)
                    if (entry.Handle != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(entry.Handle);
                hooks.Clear();
            }
            if (bridgeModule != IntPtr.Zero) NativeMethods.FreeLibrary(bridgeModule);
            bridgeModule = IntPtr.Zero;
            bridgeProcedure = IntPtr.Zero;
        }
    }

    internal sealed class MenuBridgeContext : ApplicationContext
    {
        // PotPlayer 的 XML 菜单不提供第三方命令回调。原生 HMENU 使用独立命令 ID；
        // 皮肤菜单保留 PotPlayer 自己绘制的九个叶子项，FrameClip 只按 UIA 暴露的
        // 精确动作名称接管点击。菜单外观、层级和关闭行为仍由 PotPlayer 管理。
        private const uint RootCommandId = 0xEE00;
        private const uint CaptureCommandId = 0xEE01;
        private const uint MarkInCommandId = 0xEE02;
        private const uint MarkOutCommandId = 0xEE03;
        private const uint ExportOriginalCommandId = 0xEE04;
        private const uint ExportPreciseCommandId = 0xEE05;
        private const uint ClearCommandId = 0xEE06;
        private const uint SettingsCommandId = 0xEE07;
        private const uint OpenImageCommandId = 0xEE08;
        private const uint OpenVideoCommandId = 0xEE09;

        private readonly NativeMethods.LowLevelMouseProc mouseCallback;
        private readonly NativeMethods.WinEventDelegate eventCallback;
        private readonly Control dispatcher;
        private readonly AppConfig config;
        private readonly CaptureEngine engine;
        private readonly NativeBridgeMessageWindow bridgeMessageWindow;
        private readonly NativeBridgeManager nativeBridge;
        private readonly System.Windows.Forms.Timer installRepairTimer;
        private readonly Dictionary<uint, NativeMethods.RECT> actionRects = new Dictionary<uint, NativeMethods.RECT>();
        private IntPtr mouseHook;
        private IntPtr popupEventHook;
        private IntPtr menuEventHook;
        private IntPtr objectEventHook;
        private IntPtr foregroundEventHook;
        private IntPtr activeRootMenu;
        private IntPtr activeSubMenu;
        private IntPtr activeMenuWindow;
        private DateTime pendingRootUntil = DateTime.MinValue;
        private DateTime menuSessionUntil = DateTime.MinValue;
        private bool suppressNextLeftUp;
        private int menuSessionGeneration;

        internal MenuBridgeContext(AppConfig config)
        {
            this.config = config;
            dispatcher = new Control();
            dispatcher.CreateControl();
            engine = new CaptureEngine(config, dispatcher);
            bridgeMessageWindow = new NativeBridgeMessageWindow(engine, dispatcher);
            nativeBridge = new NativeBridgeManager(TraceMenu);
            installRepairTimer = new System.Windows.Forms.Timer();
            installRepairTimer.Interval = 3000;
            installRepairTimer.Tick += delegate
            {
                if (PendingMenuRepair.TryApplyIfPlayerStopped()) installRepairTimer.Stop();
            };
            if (PendingMenuRepair.Exists) installRepairTimer.Start();
            mouseCallback = MouseHookCallback;
            eventCallback = WinEventCallback;
            mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, mouseCallback, NativeMethods.GetModuleHandle(null), 0);
            popupEventHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_MENUPOPUPSTART, NativeMethods.EVENT_SYSTEM_MENUPOPUPEND, IntPtr.Zero, eventCallback, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
            menuEventHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_MENUSTART, NativeMethods.EVENT_SYSTEM_MENUEND, IntPtr.Zero, eventCallback, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
            objectEventHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_OBJECT_SHOW, NativeMethods.EVENT_OBJECT_HIDE, IntPtr.Zero, eventCallback, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
            foregroundEventHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, eventCallback, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);

            if (mouseHook == IntPtr.Zero || popupEventHook == IntPtr.Zero)
                throw new InvalidOperationException(UiText.Choose(config.Language,
                    "无法建立 PotPlayer 菜单扩展钩子。", "Unable to initialize the PotPlayer menu extension hook."));
        }

        internal CaptureEngine Engine
        {
            get { return engine; }
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                NativeMethods.MSLLHOOKSTRUCT data = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
                int message = wParam.ToInt32();
                if (message == NativeMethods.WM_RBUTTONDOWN)
                {
                    suppressNextLeftUp = false;
                    bool targetsPlayer = PointTargetsPotPlayer(data.Point);
                    TraceMenu("right-down " + data.Point.X + "," + data.Point.Y + " player=" + targetsPlayer);
                    if (targetsPlayer) BeginMenuSession(data.Point);
                    else if (IsMenuSessionActive()) ResetMenuTracking();
                }
                else if (message == NativeMethods.WM_LBUTTONDOWN)
                {
                    suppressNextLeftUp = false;
                    if (!IsMenuSessionActive())
                    {
                        ResetMenuTracking();
                    }
                    else if (!IsActivePotPlayerInteraction(data.Point))
                    {
                        TraceMenu("cancel external-left " + data.Point.X + "," + data.Point.Y);
                        ResetMenuTracking();
                    }
                    // 标准菜单仍按 HMENU 的真实命令矩形分发；自绘皮肤菜单由已注入
                    // PotPlayer UI 线程的原生桥接处理，这里不再猜测其窗口或坐标。
                    else if (TryHandleNativeMenuClick(data.Point))
                    {
                        suppressNextLeftUp = true;
                        return new IntPtr(1);
                    }
                    else
                    {
                        TraceMenu("pass unmatched player-left " + data.Point.X + "," + data.Point.Y);
                        ResetMenuTracking();
                    }
                }
                else if ((message == NativeMethods.WM_RBUTTONDOWN || message == NativeMethods.WM_MBUTTONDOWN) && IsMenuSessionActive())
                {
                    TraceMenu("cancel alternate-button");
                    ResetMenuTracking();
                }
                else if (message == NativeMethods.WM_LBUTTONUP && suppressNextLeftUp)
                {
                    suppressNextLeftUp = false;
                    return new IntPtr(1);
                }
            }
            return NativeMethods.CallNextHookEx(mouseHook, nCode, wParam, lParam);
        }

        private void WinEventCallback(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
        {
            if (eventType == NativeMethods.EVENT_SYSTEM_FOREGROUND)
            {
                if (!IsPotPlayerWindow(hwnd) && IsMenuSessionActive())
                {
                    TraceMenu("cancel foreground-change");
                    ResetMenuTracking();
                }
                return;
            }
            if (eventType == NativeMethods.EVENT_SYSTEM_MENUEND)
            {
                ResetMenuTracking();
                return;
            }
            if (eventType != NativeMethods.EVENT_SYSTEM_MENUPOPUPSTART && eventType != NativeMethods.EVENT_OBJECT_SHOW) return;
            if (hwnd == IntPtr.Zero) return;

            if (eventType == NativeMethods.EVENT_OBJECT_SHOW)
            { }
            if (!IsMenuSessionActive()) return;
            if (!IsPotPlayerMenuWindow(hwnd)) return;

            IntPtr menu = NativeMethods.SendMessage(hwnd, NativeMethods.MN_GETHMENU, IntPtr.Zero, IntPtr.Zero);
            if (menu == IntPtr.Zero) return;

            if (IsFrameClipSubMenu(menu))
            {
                activeSubMenu = menu;
                activeMenuWindow = hwnd;
                TouchMenuSession();
                CacheActionRectangles();
                return;
            }

            if (DateTime.UtcNow <= pendingRootUntil && activeRootMenu == IntPtr.Zero)
            {
                InjectRootMenu(menu);
                activeRootMenu = menu;
                pendingRootUntil = DateTime.MinValue;
                TouchMenuSession();
                return;
            }

            if (activeSubMenu != IntPtr.Zero && menu == activeSubMenu)
            {
                activeMenuWindow = hwnd;
                CacheActionRectangles();
            }
        }

        private void InjectRootMenu(IntPtr rootMenu)
        {
            if (NativeMethods.GetMenuState(rootMenu, RootCommandId, NativeMethods.MF_BYCOMMAND) != NativeMethods.MenuMissing)
            {
                activeSubMenu = NativeMethods.GetSubMenu(rootMenu, 0);
                return;
            }

            IntPtr submenu = NativeMethods.CreatePopupMenu();
            if (submenu == IntPtr.Zero) return;
            AppendItem(submenu, CaptureCommandId, UiText.ActionLabel(config.Language, CaptureAction.CaptureFrame));
            NativeMethods.AppendMenu(submenu, NativeMethods.MF_SEPARATOR, UIntPtr.Zero, null);
            AppendItem(submenu, MarkInCommandId, UiText.ActionLabel(config.Language, CaptureAction.MarkIn));
            AppendItem(submenu, MarkOutCommandId, UiText.ActionLabel(config.Language, CaptureAction.MarkOut));
            AppendItem(submenu, ExportOriginalCommandId, UiText.ActionLabel(config.Language, CaptureAction.ExportOriginal));
            AppendItem(submenu, ExportPreciseCommandId, UiText.ActionLabel(config.Language, CaptureAction.ExportPrecise));
            NativeMethods.AppendMenu(submenu, NativeMethods.MF_SEPARATOR, UIntPtr.Zero, null);
            AppendItem(submenu, ClearCommandId, UiText.ActionLabel(config.Language, CaptureAction.ClearRange));
            AppendItem(submenu, SettingsCommandId, UiText.ActionLabel(config.Language, CaptureAction.Settings));
            AppendItem(submenu, OpenImageCommandId, UiText.ActionLabel(config.Language, CaptureAction.OpenImageOutput));
            AppendItem(submenu, OpenVideoCommandId, UiText.ActionLabel(config.Language, CaptureAction.OpenVideoOutput));

            NativeMethods.MENUITEMINFO rootItem = new NativeMethods.MENUITEMINFO();
            rootItem.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.MENUITEMINFO));
            rootItem.fMask = NativeMethods.MIIM_ID | NativeMethods.MIIM_SUBMENU | NativeMethods.MIIM_STRING;
            rootItem.fType = NativeMethods.MFT_STRING;
            rootItem.wID = RootCommandId;
            rootItem.hSubMenu = submenu;
            rootItem.dwTypeData = UiText.MenuTitle(config.Language);
            rootItem.cch = (uint)rootItem.dwTypeData.Length;

            if (!NativeMethods.InsertMenuItem(rootMenu, 0, true, ref rootItem)) return;

            NativeMethods.MENUITEMINFO separator = new NativeMethods.MENUITEMINFO();
            separator.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.MENUITEMINFO));
            separator.fMask = NativeMethods.MIIM_ID;
            separator.fType = NativeMethods.MFT_SEPARATOR;
            separator.wID = RootCommandId + 0x10;
            NativeMethods.InsertMenuItem(rootMenu, 1, true, ref separator);

            activeSubMenu = submenu;
            NativeMethods.DrawMenuBar(NativeMethods.GetForegroundWindow());
        }

        private void AppendItem(IntPtr menu, uint commandId, string text)
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new UIntPtr(commandId), text);
        }

        private void CacheActionRectangles()
        {
            actionRects.Clear();
            uint[] commands = new[]
            {
                CaptureCommandId,
                MarkInCommandId,
                MarkOutCommandId,
                ExportOriginalCommandId,
                ExportPreciseCommandId,
                ClearCommandId,
                SettingsCommandId,
                OpenImageCommandId,
                OpenVideoCommandId
            };
            uint[] positions = new uint[] { 0, 2, 3, 4, 5, 7, 8, 9, 10 };
            for (int index = 0; index < commands.Length; index++)
            {
                NativeMethods.RECT rect;
                if (NativeMethods.GetMenuItemRect(IntPtr.Zero, activeSubMenu, positions[index], out rect))
                {
                    actionRects[commands[index]] = rect;
                }
            }
        }

        private bool IsFrameClipSubMenu(IntPtr menu)
        {
            StringBuilder text = new StringBuilder(160);
            if (NativeMethods.GetMenuString(menu, 0, text, text.Capacity, NativeMethods.MF_BYPOSITION) <= 0) return false;
            CaptureAction action;
            return UiText.TryMapActionLabel(text.ToString(), out action) && action == CaptureAction.CaptureFrame;
        }

        private void BeginMenuSession(NativeMethods.POINT point)
        {
            ResetMenuTracking();
            DateTime now = DateTime.UtcNow;
            pendingRootUntil = now.AddMilliseconds(1500);
            menuSessionUntil = now.AddSeconds(8);
            TraceMenu("session native-bridge");
        }

        private bool IsMenuSessionActive()
        {
            if (DateTime.UtcNow <= menuSessionUntil) return true;
            ResetMenuTracking();
            return false;
        }

        private void TouchMenuSession()
        {
            menuSessionUntil = DateTime.UtcNow.AddSeconds(8);
        }

        private void ResetMenuTracking()
        {
            Interlocked.Increment(ref menuSessionGeneration);
            ResetNativeMenuTracking();
            menuSessionUntil = DateTime.MinValue;
        }

        private void ResetNativeMenuTracking()
        {
            pendingRootUntil = DateTime.MinValue;
            activeRootMenu = IntPtr.Zero;
            activeSubMenu = IntPtr.Zero;
            activeMenuWindow = IntPtr.Zero;
            actionRects.Clear();
        }

        private void TraceMenu(string message)
        {
            try
            {
                Directory.CreateDirectory(RangeState.StateDirectory);
                string path = Path.Combine(RangeState.StateDirectory, "menu-debug.log");
                if (File.Exists(path) && new FileInfo(path).Length > 65536) File.WriteAllText(path, String.Empty);
                File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + message + Environment.NewLine);
            }
            catch { }
        }

        private bool TryHandleNativeMenuClick(NativeMethods.POINT point)
        {
            if (activeSubMenu == IntPtr.Zero || activeMenuWindow == IntPtr.Zero || !NativeMethods.IsWindowVisible(activeMenuWindow) ||
                !IsPotPlayerMenuWindow(activeMenuWindow) || NativeMethods.WindowFromPoint(point) != activeMenuWindow) return false;
            foreach (KeyValuePair<uint, NativeMethods.RECT> item in actionRects)
            {
                if (!item.Value.Contains(point)) continue;
                CaptureAction action;
                if (!TryMapAction(item.Key, out action)) return false;
                DispatchAction(action, activeMenuWindow);
                return true;
            }
            return false;
        }

        private bool TryMapAction(uint command, out CaptureAction action)
        {
            action = CaptureAction.CaptureFrame;
            if (command == CaptureCommandId) action = CaptureAction.CaptureFrame;
            else if (command == MarkInCommandId) action = CaptureAction.MarkIn;
            else if (command == MarkOutCommandId) action = CaptureAction.MarkOut;
            else if (command == ExportOriginalCommandId) action = CaptureAction.ExportOriginal;
            else if (command == ExportPreciseCommandId) action = CaptureAction.ExportPrecise;
            else if (command == ClearCommandId) action = CaptureAction.ClearRange;
            else if (command == SettingsCommandId) action = CaptureAction.Settings;
            else if (command == OpenImageCommandId) action = CaptureAction.OpenImageOutput;
            else if (command == OpenVideoCommandId) action = CaptureAction.OpenVideoOutput;
            else return false;
            return true;
        }

        private void DispatchAction(CaptureAction action, IntPtr menuWindow)
        {
            if (menuWindow != IntPtr.Zero) NativeMethods.PostMessage(menuWindow, NativeMethods.WM_CANCELMODE, IntPtr.Zero, IntPtr.Zero);
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground != menuWindow && IsPotPlayerWindow(foreground))
                NativeMethods.PostMessage(foreground, NativeMethods.WM_CANCELMODE, IntPtr.Zero, IntPtr.Zero);
            ResetMenuTracking();
            dispatcher.BeginInvoke(new Action(delegate { engine.Execute(action); }));
        }

        private bool PointTargetsPotPlayer(NativeMethods.POINT point)
        {
            IntPtr target = NativeMethods.WindowFromPoint(point);
            return IsPotPlayerWindow(target);
        }

        private bool IsActivePotPlayerInteraction(NativeMethods.POINT point)
        {
            return PointTargetsPotPlayer(point) && IsPotPlayerWindow(NativeMethods.GetForegroundWindow());
        }

        private bool IsPotPlayerMenuWindow(IntPtr hwnd)
        {
            uint processId;
            NativeMethods.GetWindowThreadProcessId(hwnd, out processId);
            if (processId == 0) return IsPotPlayerWindow(NativeMethods.GetForegroundWindow());
            try
            {
                return Process.GetProcessById((int)processId).ProcessName.IndexOf("PotPlayer", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return IsPotPlayerWindow(NativeMethods.GetForegroundWindow());
            }
        }

        private bool IsPotPlayerWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            IntPtr root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
            uint processId;
            NativeMethods.GetWindowThreadProcessId(root, out processId);
            if (processId == 0) return false;
            try
            {
                return Process.GetProcessById((int)processId).ProcessName.IndexOf("PotPlayer", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (mouseHook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(mouseHook);
            if (popupEventHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(popupEventHook);
            if (menuEventHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(menuEventHook);
            if (objectEventHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(objectEventHook);
            if (foregroundEventHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(foregroundEventHook);
            if (disposing)
            {
                nativeBridge.Dispose();
                bridgeMessageWindow.Dispose();
                installRepairTimer.Stop();
                installRepairTimer.Dispose();
                dispatcher.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            NativeMethods.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppPaths.MigrateLegacyData();
            PendingMenuRepair.TryApplyIfPlayerStopped();
            AppConfig config = AppConfig.Load();

            if (args.Length > 0)
            {
                return RunOneShot(config, args);
            }

            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\PotPlayerFrameClip-8D04C93A", out created))
            {
                if (!created) return 0;
                try
                {
                    Application.Run(new MenuBridgeContext(config));
                    return 0;
                }
                catch (Exception exception)
                {
                    MessageBox.Show(exception.Message, AppPaths.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }
        }

        private static int RunOneShot(AppConfig config, string[] args)
        {
            string requestedAction = args[0].Trim().ToLowerInvariant();
            if (requestedAction == "--apply-menu-language" && args.Length > 2)
            {
                try { return MenuLocalization.ApplyToFile(args[2], args[1]) ? 0 : 1; }
                catch { return 1; }
            }
            using (Form dispatcherForm = new Form())
            {
                dispatcherForm.CreateControl();
                CaptureEngine engine = new CaptureEngine(config, dispatcherForm);
                string action = requestedAction;
                if (action == "--seek" && args.Length > 1)
                {
                    long milliseconds;
                    return long.TryParse(args[1], out milliseconds) && engine.Seek(milliseconds) ? 0 : 2;
                }
                if (action == "--capture") engine.Execute(CaptureAction.CaptureFrame);
                else if (action == "--mark-in") engine.Execute(CaptureAction.MarkIn);
                else if (action == "--mark-out") engine.Execute(CaptureAction.MarkOut);
                else if (action == "--original") engine.Execute(CaptureAction.ExportOriginal);
                else if (action == "--precise") engine.Execute(CaptureAction.ExportPrecise);
                else if (action == "--clear") engine.Execute(CaptureAction.ClearRange);
                else if (action == "--settings") engine.Execute(CaptureAction.Settings);
                else if (action == "--open-images") engine.Execute(CaptureAction.OpenImageOutput);
                else if (action == "--open-videos") engine.Execute(CaptureAction.OpenVideoOutput);
                else return 2;

                do
                {
                    Application.DoEvents();
                    Thread.Sleep(100);
                }
                while (engine.IsBusy || engine.HasActiveToast);
                return 0;
            }
        }
    }
}
