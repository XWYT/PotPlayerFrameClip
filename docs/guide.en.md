# FrameClip user guide

[Home](../README.en.md) · [中文](guide.zh.md)

## Install and upgrade

Requires Windows 10/11, .NET Framework 4.6.2 or newer, and 32/64-bit PotPlayer, installed or portable. Captures and exports require a matching `ffmpeg.exe` / `ffprobe.exe` pair in the same directory. PotPlayer and .NET Framework are not bundled.

Download the installer and checksum from [Releases](https://github.com/XWYT/PotPlayerFrameClip/releases/tag/v0.4.1). Verify SHA-256:

```powershell
Get-FileHash '.\PotPlayerFrameClip-v0.4.1-Setup.exe' -Algorithm SHA256
```

Finish jobs and close both PotPlayer and FrameClip before upgrading. The injected bridge stays loaded for the player's lifetime; replacing it requires a closed player. Windows does not need to restart. Setup backs up files before replacement and rolls back a failed configuration. Existing settings and custom menus are preserved.

The public lightweight installer does not include FFmpeg. Choose an existing pair or configure it later in Settings > Output. Configure later preserves existing paths during upgrades. If FFmpeg is missing:

```powershell
winget install --id Gyan.FFmpeg --exact --source winget --accept-package-agreements --accept-source-agreements
```

The installer is not commercially code-signed. Check the source, filename and checksum before responding to security prompts; do not disable system protection. The running application does not access the network.

### Custom paths and silent setup

```powershell
$p=Start-Process -FilePath '.\PotPlayerFrameClip-v0.4.1-Setup.exe' -ArgumentList '/VERYSILENT','/NORESTART','/POTPLAYERDIR="D:\Apps\PotPlayer"','/FFMPEGPATH="D:\Tools\ffmpeg\bin\ffmpeg.exe"' -PassThru; Wait-Process -Id $p.Id
```

| Argument | Purpose |
| --- | --- |
| `/POTPLAYERDIR="folder"` | Folder containing the PotPlayer executable |
| `/FFMPEGPATH="file"` | Existing `ffmpeg.exe`, with sibling `ffprobe.exe` |
| `/FFMPEGMODE=existing\|later` | Reuse tools or configure later |
| `/NOSTARTUP=1` | Disable Start with Windows |
| `/DIR="folder"` | Change the application folder |
| `/VERYSILENT /NORESTART` | Silent setup, no Windows restart |

Start with Windows launches FrameClip when the current Windows user signs in.

## Capture and organization

Use Reference Frame & Clip Capture in PotPlayer's context menu to capture the current frame, set in/out points, export source-copy or precise clips, clear the range, or open the current work's image/video folders. Accepted jobs retain their submitted source, range, output settings and destination identity. Later playback or settings changes do not alter them.

Stills are decoded from the source at the playback timestamp, without desktop capture, monitor tone mapping or player display LUTs. Readable local media is required; DRM, web streams and some network locations cannot be captured.

Classification removes common release markers and uses titles, episode markers, folders, media titles and remembered aliases to match a work folder. It is basic name-based filing, without picture analysis or online lookup. Each work has `图片` (images) and `视频` (clips) folders; there is no automatic classification by genre, palette or person.

`待归类剧集 [xxxxxxxx]` / `待归类作品 [xxxxxxxx]` means the title could not be established, for example from `01.mkv`. Captures from one source directory stay together while separate unknown works are kept apart where possible.

Default output is `%USERPROFILE%\Videos\FrameClip` (the Windows Videos folder), with a configurable root. Application files usually reside in `%LOCALAPPDATA%\PotPlayerFrameClip`. Settings, history, aliases and logs use that user-data directory even with a custom application folder.

## Settings and jobs

- **Output:** library folder, PNG/TIFF, precise clip codec, HDR-to-SDR viewing copies and FFmpeg path.
- **Performance:** GPU preferred or CPU only; playback-first, balanced or export-first resource use; parallel video count and background priority. Screenshots use an independent lane.
- **Appearance:** language, light/dark theme, reduced motion, notification mode and placement. Choose a corner or drag a custom preview; the monitor and relative position are saved.

The task center supports progress, cancellation, retry, history and opening output folders. GPU preferred is enabled by default, mainly accelerating decoding. Image and ProRes/DNxHR encoding, plus parts of color filtering, stay on CPU. Unsupported sources or insufficient resources use CPU; short jobs may be slower on GPU.

Available paths depend on FFmpeg and drivers: CUDA, D3D11VA, Intel QSV and single-adapter DXVA2 fallback. Actual media comparisons cover the NVIDIA development host. Intel/AMD real devices, more skins, physical mixed-DPI and long-duration coverage remain limited. See [GPU processing](gpu-processing.md).

After a language change, reopen PotPlayer to refresh its loaded XML menu. Updating a menu in a protected directory may require administrator confirmation.

## Formats and color

| Output | Format and precision |
| --- | --- |
| Images | 16-bit RGB PNG/TIFF, lossless compression |
| Source-copy clips | MKV; first video stream, compatible audio/subtitles and container metadata; no re-encoding, keyframe-limited start |
| Precise clips | MOV; ProRes 422 HQ, 4444, 4444 XQ, DNxHR HQX or 444; 24-bit PCM / 48 kHz audio |

ProRes 422 HQ and DNxHR HQX use 10-bit 4:2:2 input; ProRes 4444/4444 XQ and DNxHR 444 use 10-bit 4:4:4 input. Precise clips are re-encoded; some subtitles, original compressed audio and dynamic metadata are not carried through. Lossless does not mean error-free decoding or YCbCr-to-RGB conversion, and 16-bit output cannot recover detail already lost in the source.

HDR stills apply the YCbCr-to-RGB matrix and range conversion while retaining PQ/HLG transfer functions, without a display LUT. PNG receives available color tags. TIFF tag support varies by application; assign input color space using filenames and source metadata.

### Import into post-production software

| Filename marker | Interpretation |
| --- | --- |
| `Rec709-*` | Use the transfer function recorded in the name |
| `Rec2100-PQ` | Rec.2100 ST 2084/PQ input |
| `Rec2100-HLG` | Rec.2100 HLG input |
| `Rec709-SDR-TONEMAPPED` | Tone-mapped, full-range 16-bit Rec.709 RGB |
| `DV-P*-Base-*` | Decodable base layer, without Dolby Vision dynamic mapping |

Include SDR viewing image applies only to PQ/HLG sources. It uses linear floating-point processing and FFmpeg Mobius tone mapping, requiring `zscale` and `tonemap`. This SDR reference is not a replacement for the HDR original or a reversible mastering transform.

Dolby Vision sources without a reliable HDR10/HLG-compatible base layer, including Profile 5, are rejected for decoded reference output. Compatible Profile 7/8 sources use their decodable base layer. Dynamic metadata is not baked into stills or precise transcodes. Use source-copy clips to retain copyable source-stream information.

## Troubleshooting

| Problem | Check |
| --- | --- |
| Missing menu, or actions open About PotPlayer / toggle playback | Finish jobs, close both apps, install the current release and reopen. Verify the player folder, `Menus\FrameClipMenu.xml` and bridge files below. Check security software logs without disabling protection |
| Cannot locate current media | Use a local file, verify its path, check that another extension has not replaced the player title, and reopen the file |
| Missing FFmpeg/FFprobe | Select a matching pair in Settings; `Get-Command ffmpeg,ffprobe -ErrorAction SilentlyContinue` checks PATH |
| HDR still appears dark or discolored | Assign the input color space in color-managed software, or enable an SDR viewing copy |
| No SDR copy | SDR sources do not get another image. For PQ/HLG, check `zscale` and `tonemap`; inspect job errors/logs if the HDR original succeeded |
| Player menu language unchanged | Reopen PotPlayer after saving; repeat the save if administrator confirmation was canceled, or rerun setup |

```powershell
Get-Item "$env:LOCALAPPDATA\PotPlayerFrameClip\FrameClipBridge64.dll",
         "$env:LOCALAPPDATA\PotPlayerFrameClip\FrameClipBridge32.dll",
         "$env:LOCALAPPDATA\PotPlayerFrameClip\FrameClipBridgeHost32.exe"
ffmpeg -filters | Select-String 'zscale|tonemap'
```

The native bridge handles FrameClip menu commands without replacing or patching PotPlayer DLLs. For persistent problems, provide the player version, architecture, skin, scale, `bridge-debug.log`, `menu-debug.log` and the latest runtime log under the user-data `logs` directory. Redact personal paths before posting logs publicly.

## Uninstall

Finish jobs, close PotPlayer and uninstall through Windows Installed apps. Settings, history, logs and aliases are kept by default. Exported media is preserved.

For silent removal with explicit deletion of user settings/data:

```powershell
& "$env:LOCALAPPDATA\PotPlayerFrameClip\unins000.exe" /VERYSILENT /NORESTART /REMOVEUSERDATA=1
```

## Build from source

Requires the .NET Framework 4.x C# compiler, Zig and Inno Setup 6. Zig is a build dependency for the native bridge:

```powershell
winget install --id Microsoft.DotNet.Framework.DeveloperPack_4 --exact --source winget --accept-package-agreements --accept-source-agreements
winget install --id JRSoftware.InnoSetup --exact --source winget --accept-package-agreements --accept-source-agreements
winget install --id zig.zig --exact --source winget --accept-package-agreements --accept-source-agreements
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Version 0.4.1
python scripts/check-docs.py
```

The build runs C#, native bridge and isolated installation regressions, producing `dist\PotPlayerFrameClip-v0.4.1-Setup.exe` and SHA-256. GitHub prepares a lightweight draft; publication follows review.

For a local Full offline candidate, run `scripts/prepare-ffmpeg.ps1 -Download`, then `scripts/build.ps1 -Version 0.4.1 -BundleFFmpeg`. Versions and checksums are pinned in `installer/ffmpeg-manifest.json`. Private tools install under `tools/ffmpeg/<version>/` without changing PATH or external installations. Bundled tools need 64-bit Windows; use compatible external FFmpeg on 32-bit Windows. Full supports `/FFMPEGMODE=bundled`; lightweight setup rejects it. Full is not publicly downloadable until corresponding sources, build recipes and dependency-license archival are complete.
