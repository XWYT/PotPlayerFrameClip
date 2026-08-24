using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PotPlayerFrameClip
{
    internal static class Tests
    {
        private static int failures;

        private static void Check(bool condition, string message)
        {
            if (condition) return;
            Console.Error.WriteLine(message);
            failures++;
        }

        public static int Main()
        {
            Check(AppPaths.ConfigPath.StartsWith(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                StringComparison.OrdinalIgnoreCase), "Config must be stored under LocalAppData.");
            Check(AppPaths.DefaultOutputDirectory.IndexOf("FrameClip", StringComparison.OrdinalIgnoreCase) >= 0,
                "Default output directory is invalid.");
            Check(CaptureFormats.NormalizeImageFormat("TIFF16") == "tiff16", "TIFF normalization failed.");
            Check(CaptureFormats.NormalizeVideoPreset("prores4444xq") == "prores4444xq", "Video preset normalization failed.");
            Check(UiText.NormalizeLanguage("English") == UiText.English, "English language normalization failed.");
            Check(UiText.NormalizeLanguage("unknown") == UiText.Chinese, "Unknown languages must fall back to Simplified Chinese.");
            Check(UiText.ActionLabel(UiText.English, CaptureAction.CaptureFrame).StartsWith("Capture current frame", StringComparison.Ordinal),
                "English menu labels are unavailable.");
            AppConfig defaults = new AppConfig();
            Check(!defaults.ExportRec709ForHdr, "HDR Rec.709 companion output must be disabled by default.");
            Check(defaults.Language == UiText.Chinese, "Simplified Chinese must remain the default language.");

            CaptureEngine processEngine = new CaptureEngine(defaults, null);
            System.Diagnostics.Stopwatch pipeTimer = System.Diagnostics.Stopwatch.StartNew();
            bool pipeFailureReturned = false;
            try
            {
                processEngine.RunProcess(
                    "cmd.exe",
                    "/d /c \"for /L %i in (1,1,20000) do @echo xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx 1>&2 & exit /b 7\"",
                    "stderr saturation probe",
                    10000);
            }
            catch (InvalidOperationException)
            {
                pipeFailureReturned = true;
            }
            catch { }
            pipeTimer.Stop();
            Check(pipeFailureReturned && pipeTimer.ElapsedMilliseconds < 9000,
                "External process stderr saturation caused a redirected-pipe deadlock.");

            using (ToastForm toast = new ToastForm("提示标题", "提示正文必须被完整绘制。", 2000, new Rectangle(0, 0, 1920, 1080)))
            using (Bitmap toastBitmap = new Bitmap(toast.Width, toast.Height))
            {
                toast.CreateControl();
                toast.DrawToBitmap(toastBitmap, new Rectangle(Point.Empty, toast.Size));
                int titlePixels = 0;
                int messagePixels = 0;
                for (int y = 8; y < 38; y++)
                    for (int x = 14; x < toastBitmap.Width - 14; x++)
                        if (toastBitmap.GetPixel(x, y).R > 120) titlePixels++;
                for (int y = 38; y < toastBitmap.Height - 8; y++)
                    for (int x = 14; x < toastBitmap.Width - 14; x++)
                        if (toastBitmap.GetPixel(x, y).R > 120) messagePixels++;
                Check(toast.Controls.Count == 0, "Toast must not create child control rectangles.");
                Check(titlePixels > 20 && messagePixels > 20, "Toast title or body was not owner-drawn.");
                string previewPath = Environment.GetEnvironmentVariable("FRAMECLIP_TOAST_PREVIEW");
                if (!String.IsNullOrEmpty(previewPath)) toastBitmap.Save(previewPath);
            }

            VideoInfo pqInfo = new VideoInfo
            {
                ColorRange = "tv",
                ColorPrimaries = "bt2020",
                ColorTransfer = "smpte2084",
                ColorSpace = "bt2020nc"
            };
            string toneMapFilter = CaptureEngine.BuildRec709ToneMapFilter(pqInfo, "rgb48be");
            Check(toneMapFilter.Contains("t=linear") && toneMapFilter.Contains("format=gbrpf32le") && toneMapFilter.Contains("tonemap=mobius"),
                "HDR tone mapping must operate in linear floating-point RGB.");
            Check(toneMapFilter.Contains("p=bt709:t=bt709:m=gbr:r=full") && toneMapFilter.Contains("color_primaries=bt709"),
                "HDR tone mapping output is not tagged as full-range Rec.709 RGB.");
            Check(MediaOrganizer.DeriveWorkTitle("For.All.Mankind.S05E04.2160p.ATVP.WEB-DL.mkv", "") == "For All Mankind",
                "Series title normalization failed.");
            Check(MediaOrganizer.DeriveClassificationTitle(@"D:\Media\For All Mankind\S01 4K.HDR\02.mkv", "", false) == "For All Mankind",
                "Parent series title recovery failed.");
            string unresolved = MediaOrganizer.DeriveClassificationTitle(@"D:\BaiduNetdiskDownload\S01 4K.HDR\02.mkv", "", false);
            Check(unresolved.StartsWith("待归类剧集 [", StringComparison.Ordinal) && unresolved.EndsWith("]", StringComparison.Ordinal),
                "Weak numeric episode title must remain explicitly unclassified.");
            Check(MediaOrganizer.DeriveClassificationTitle(@"D:\BaiduNetdiskDownload\Reacher.S04E02.2026.2160p.mkv", "", false) == "Reacher",
                "Descriptive episodic filename classification failed.");
            Check(MediaOrganizer.DeriveClassificationTitle(@"D:\BaiduNetdiskDownload\2026.2160p.iT.mkv", "", false).StartsWith("待归类作品 [", StringComparison.Ordinal),
                "Technical-only filename must not become a work title.");

            Check(PotPlayerMediaLocator.ExtractPathFromIniValue(@"1833982=D:\Media\01.mkv") == @"D:\Media\01.mkv",
                "RememberFiles playback-position parsing failed.");
            Check(PotPlayerMediaLocator.ExtractPathFromIniValue(@"D:\Media\01.mkv") == @"D:\Media\01.mkv",
                "Plain INI media path parsing failed.");

            CaptureAction[] expectedActions = new[]
            {
                CaptureAction.CaptureFrame, CaptureAction.MarkIn, CaptureAction.MarkOut,
                CaptureAction.ExportOriginal, CaptureAction.ExportPrecise, CaptureAction.ClearRange,
                CaptureAction.Settings, CaptureAction.OpenImageOutput, CaptureAction.OpenVideoOutput
            };
            string[] titles = new[]
            {
                "截取当前帧（自动识别色彩 · 16-bit）", "设置入点", "设置出点",
                "导出原码片段（保留源色彩/DV + 原音频）", "导出精确片段（可选编码 + PCM）",
                "清除入点和出点", "设置…", "打开当前作品图片文件夹", "打开当前作品视频文件夹"
            };
            string[] englishTitles = new[]
            {
                "Capture current frame (automatic color detection · 16-bit)", "Set in point", "Set out point",
                "Export source clip (source color/DV + original audio)", "Export precise clip (selectable codec + PCM)",
                "Clear in and out points", "Settings…", "Open current title image folder", "Open current title video folder"
            };
            for (int index = 0; index < expectedActions.Length; index++)
            {
                CaptureAction mappedAction;
                bool titleMapped = UiText.TryMapActionLabel(titles[index], out mappedAction);
                Check(titleMapped && mappedAction == expectedActions[index], "Menu title mapping failed at index " + index + ".");

                bool englishMapped = UiText.TryMapActionLabel(englishTitles[index], out mappedAction);
                Check(englishMapped && mappedAction == expectedActions[index], "English menu title mapping failed at index " + index + ".");
            }

            string temporary = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".exe");
            string mediaDirectory = Path.Combine(Path.GetTempPath(), "FrameClipTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllBytes(temporary, new byte[] { 0 });
                Check(ToolLocator.Find(temporary, "ffmpeg.exe") == Path.GetFullPath(temporary), "Configured tool path was not retained.");

                Directory.CreateDirectory(mediaDirectory);
                string first = Path.Combine(mediaDirectory, "01.mkv");
                string second = Path.Combine(mediaDirectory, "02.mkv");
                File.WriteAllBytes(first, new byte[] { 0 });
                File.WriteAllBytes(second, new byte[] { 0 });
                IList<string> commandLinePaths = PotPlayerMediaLocator.ExtractExistingMediaArguments(
                    "\"C:\\Apps\\PotPlayerMini64.exe\" \"" + first + "\"");
                Check(commandLinePaths.Count == 1 && commandLinePaths[0] == Path.GetFullPath(first),
                    "PotPlayer command-line media parsing failed.");
                Check(PotPlayerMediaLocator.FindExactOrSibling("02.mkv", commandLinePaths) == Path.GetFullPath(second),
                    "Trusted sibling media recovery failed.");

                string portableIni = Path.Combine(mediaDirectory, "PotPlayerMini64.ini");
                File.WriteAllLines(portableIni, new[] { "[Settings]", "LastMenuName=OldMenu.xml" }, System.Text.Encoding.Unicode);
                Check(PendingMenuRepair.SetIniValue(portableIni, "Settings", "LastMenuName", "FrameClipMenu.xml"),
                    "Pending menu repair could not update a portable INI.");
                Check(Array.Exists(File.ReadAllLines(portableIni, System.Text.Encoding.Unicode),
                    delegate(string line) { return line == "LastMenuName=FrameClipMenu.xml"; }),
                    "Pending menu repair wrote an incorrect menu selection.");

                string utf8Ini = Path.Combine(mediaDirectory, "PotPlayer.ini");
                File.WriteAllText(utf8Ini, "[Settings]\r\nLastMenuName=旧菜单.xml\r\n", new System.Text.UTF8Encoding(false));
                Check(PendingMenuRepair.SetIniValue(utf8Ini, "Settings", "LastMenuName", "FrameClipMenu.xml"),
                    "Pending menu repair could not update a UTF-8 INI.");
                byte[] utf8Bytes = File.ReadAllBytes(utf8Ini);
                Check(!(utf8Bytes.Length >= 3 && utf8Bytes[0] == 0xEF && utf8Bytes[1] == 0xBB && utf8Bytes[2] == 0xBF),
                    "Pending menu repair changed the UTF-8 BOM policy.");
                Check(File.ReadAllText(utf8Ini, new System.Text.UTF8Encoding(false)).Contains("LastMenuName=FrameClipMenu.xml"),
                    "Pending menu repair damaged a UTF-8 INI.");

                string menuPath = Path.Combine(mediaDirectory, "FrameClipMenu.xml");
                File.WriteAllText(menuPath,
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?><Menu><SubMenu Name=\"参照帧与片段截取\">" +
                    "<MenuItem CmdID=\"ID_APP_ABOUT\" Name=\"截取当前帧（自动识别色彩 · 16-bit）\"/><MenuItem CmdID=\"\"/>" +
                    "<MenuItem CmdID=\"ID_APP_ABOUT\" Name=\"设置入点\"/><MenuItem CmdID=\"ID_APP_ABOUT\" Name=\"设置出点\"/>" +
                    "<MenuItem CmdID=\"ID_APP_ABOUT\" Name=\"导出原码片段（保留源色彩/DV + 原音频）\"/>" +
                    "<MenuItem CmdID=\"ID_APP_ABOUT\" Name=\"导出精确片段（可选编码 + PCM）\"/><MenuItem CmdID=\"\"/>" +
                    "<MenuItem CmdID=\"ID_APP_ABOUT\" Name=\"清除入点和出点\"/><MenuItem CmdID=\"ID_APP_ABOUT\" Name=\"设置…\"/>" +
                    "<MenuItem CmdID=\"ID_APP_ABOUT\" Name=\"打开当前作品图片文件夹\"/><MenuItem CmdID=\"ID_APP_ABOUT\" Name=\"打开当前作品视频文件夹\"/>" +
                    "</SubMenu><SubMenu Name=\"User menu\"><MenuItem CmdID=\"ID_APP_ABOUT\" Name=\"About\"/></SubMenu></Menu>",
                    new System.Text.UTF8Encoding(false));
                Check(MenuLocalization.ApplyToFile(menuPath, UiText.English), "English menu localization failed.");
                System.Xml.XmlDocument localizedMenu = new System.Xml.XmlDocument();
                localizedMenu.Load(menuPath);
                Check(localizedMenu.SelectSingleNode("/Menu/SubMenu[1]").Attributes["Name"].Value == "Reference Frame & Clip Capture",
                    "English submenu title was not written.");
                Check(localizedMenu.SelectSingleNode("/Menu/SubMenu[1]/MenuItem[@CmdID != ''][1]").Attributes["Name"].Value.StartsWith("Capture current frame", StringComparison.Ordinal),
                    "English capture command was not written.");
                Check(localizedMenu.SelectNodes("/Menu/SubMenu[1]/MenuItem[@CmdID='ID_APP_ABOUT']").Count == 9,
                    "Menu localization changed the leaf placeholder commands.");
                Check(localizedMenu.SelectSingleNode("/Menu/SubMenu[2]").Attributes["Name"].Value == "User menu",
                    "Menu localization modified an unrelated user menu.");
                byte[] localizedBytes = File.ReadAllBytes(menuPath);
                Check(MenuLocalization.ApplyToFile(menuPath, UiText.English), "Matching menu language should be accepted.");
                Check(localizedBytes.SequenceEqual(File.ReadAllBytes(menuPath)),
                    "Matching menu language should not rewrite the PotPlayer menu file.");
            }
            finally
            {
                try { File.Delete(temporary); }
                catch { }
                try { Directory.Delete(mediaDirectory, true); }
                catch { }
            }
            return failures == 0 ? 0 : 1;
        }
    }
}
