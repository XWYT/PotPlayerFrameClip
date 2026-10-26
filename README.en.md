<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/readme/banner-dark.png">
  <source media="(prefers-color-scheme: light)" srcset="assets/readme/banner-light.png">
  <img alt="FrameClip: lossless stills, source-copy and precisely transcoded clips, with multiple HDR/SDR color spaces" src="assets/readme/banner-dark.png" width="1600">
</picture>

# PotPlayer FrameClip

**Film and television stills and clips, for professional reference and personal collections.**

Collect color, lighting and tonal references for grading, organize shots for creative work, or keep favorite frames from films and series. FrameClip works from PotPlayer's context menu and reads the media source directly, without desktop capture or monitor tone mapping.

Collect lossless stills in multiple formats, preserve original clips or make precise exports for editing, with support for multiple HDR/SDR color spaces.

**Current version: 0.4.0 preview.** [Downloads and release notes](https://github.com/XWYT/PotPlayerFrameClip/releases/tag/v0.4.0) · [中文](README.md)

## Highlights

- **Lossless stills:** capture directly from the media source in multiple image formats, with color information for reference and organization.
- **HDR and SDR references:** support multiple HDR/SDR color spaces, retain HDR originals and optionally create SDR viewing copies.
- **Clips for preservation or editing:** keep the original streams or export precise cuts in editing formats.
- **Background processing:** capture stills during exports; track, cancel and retry concurrent jobs. GPU-preferred decoding preserves the requested output format and color pipeline.
- **Per-title organization:** group images and clips by work, recognize episodes and common release names, and keep uncertain titles in stable unclassified folders.
- **Native operation:** PotPlayer context-menu actions, Chinese/English UI, light/dark themes and multi-monitor notifications. Supports 32/64-bit and portable players, preserves custom menus and restores them during uninstall.

Output locations, image format, transcoding presets, performance and notification placement are configurable. Accepted jobs keep their submitted source, range and settings.

0.4.0 updates the native interface, task progress, concurrent exports, GPU-preferred processing and menu bridge. See [release notes](docs/releases/v0.4.0.md). Intel/AMD real-device checks, broader skin coverage, mixed-DPI interaction and long-duration acceptance remain open. The public installer is lightweight and does not include FFmpeg.

## GPU Processing

Settings > Performance > Video and screenshots provides GPU preferred (default)
and CPU only. Hardware decoding reuses external FFmpeg and installed graphics
drivers, with no bundled CUDA Toolkit. Capability-driven paths include CUDA,
D3D11VA, Intel QSV and single-adapter DXVA2 fallback with per-adapter budgets.
Intel/AMD policy tests are not real-device acceptance. Editing codecs, 16-bit PNG/TIFF and the
reference color pipeline remain unchanged. Tasks report the actual backend;
unsupported formats or insufficient estimated resources use CPU. Short tasks are
not guaranteed to be faster. See [GPU processing](docs/gpu-processing.md) for
open-source options, boundaries and local measurements. These features are included
in the 0.4.0 preview; this is not a claim of universal GPU compatibility.

## Requirements

### Installer And Offline Dependencies

Install/update/uninstall share the native app controls. Start with Windows means auto-starting FrameClip when the Windows user signs in. Updates require idle tasks and a closed player; this is safe in-place replacement with backup/rollback, not hot rolling replacement.

The lightweight Setup.exe supports an existing FFmpeg pair or configuration later. Setup-Full.exe additionally offers Not installed: use bundled FFmpeg, installing a private version under tools/ffmpeg/<version>/ without internet access, PATH changes or replacement of external tools. Capture/export need FFmpeg configured; installing the app does not require it. Configure later preserves existing paths on upgrades. Default uninstall keeps settings/history and all exported media.

The bundled media tools require 64-bit Windows (32-bit PotPlayer on 64-bit Windows is supported). Use compatible external tools on 32-bit Windows. Full is a local development candidate, not a published download: corresponding-source/build-recipe and dependency-license archival remains a public-release gate.

Build preparation: scripts/prepare-ffmpeg.ps1 -Download; then scripts/build.ps1 -Version 0.4.0 -BundleFFmpeg. Downloads are build-time only, pinned by installer/ffmpeg-manifest.json. Silent setup accepts /FFMPEGMODE=existing|bundled|later and /FFMPEGPATH for an existing pair. Lightweight setup rejects bundled mode before replacement.

- Windows 10 or Windows 11
- PotPlayer 32-bit or 64-bit
- .NET Framework 4.6.2 or newer
- FFmpeg and FFprobe

Install FFmpeg with WinGet:

```powershell
winget install --id Gyan.FFmpeg --exact --source winget --accept-package-agreements --accept-source-agreements
```

## Install

### Option 1: install with an AI agent

Copy the complete prompt below and send it to an AI agent that can access the network, local files, and PowerShell:

```text
Install the 0.4.0 preview release of PotPlayer FrameClip on this Windows computer. Tell me explicitly that it is a preview.

Project URL: https://github.com/XWYT/PotPlayerFrameClip

Use only the GitHub repository above. Download PotPlayerFrameClip-v<version>-Setup.exe from its official Releases page, not a source archive or third-party mirror. Verify a published SHA-256 value when available. Close PotPlayer before installing or upgrading because the native bridge remains loaded for the player process lifetime. Check PotPlayer, FFmpeg/FFprobe, and .NET Framework 4.6.2 or newer; install Gyan.FFmpeg with WinGet only when FFmpeg is missing. Detect standard, 32-bit, 64-bit, and portable PotPlayer installations without changing unrelated settings. Install for the current user without restarting Windows, then verify the installed executable, FrameClip process, and Menus\FrameClipMenu.xml. Do not disable Windows security or bypass an unverified warning. Report the installed version, resolved paths, verification results, and any action I still need to take.
```

### Option 2: install manually

Download `PotPlayerFrameClip-v<version>-Setup.exe` from GitHub Releases and run it. Do not download the source archive. Restart PotPlayer once after installation. Portable installations can be selected in the wizard.

Close PotPlayer before installing or upgrading. The native menu bridge remains loaded for the player process lifetime and cannot be replaced safely while the player is running. A Windows restart is not required.

Silent installation:

```powershell
$p=Start-Process -FilePath '.\PotPlayerFrameClip-v0.4.0-Setup.exe' -ArgumentList '/VERYSILENT','/NORESTART','/POTPLAYERDIR="D:\Apps\PotPlayer"','/FFMPEGPATH="D:\Tools\ffmpeg\bin\ffmpeg.exe"' -PassThru; Wait-Process -Id $p.Id
```

## Color handling

Stills use 16-bit RGB PNG/TIFF. Source-copy clips use MKV; precise MOV exports support ProRes 422 HQ, 4444, 4444 XQ, DNxHR HQX and 444 with 24-bit PCM audio. PNG receives available color tags, filenames record color information, and source-copy retains compatible audio, video, subtitles and metadata.

Lossless refers to PNG/TIFF compression and copying original encoded streams. Decoding, YCbCr-to-RGB conversion and the source itself still have precision limits; 16-bit output cannot restore detail already lost in the source. ProRes/DNxHR exports are transcodes, not lossless copies of the source stream.

FrameClip converts the source YCbCr values of the original still to 16-bit RGB while retaining the encoded transfer characteristic. Assign that file's input color space in the destination application using the filename and source metadata. TIFF tag support varies by application; manual input assignment is recommended.

When **Create a tone-mapped Rec.709 SDR copy for HDR captures** is enabled, PQ and HLG captures also produce a file marked `Rec709-SDR-TONEMAPPED`. The copy is processed in linear floating point with FFmpeg's Mobius tone mapper and encoded as full-range 16-bit Rec.709 RGB. It is a convenient SDR reference, not a reversible mastering transform, and the original HDR still remains unchanged. This option requires FFmpeg builds with the `zscale` and `tonemap` filters.

The language selector updates the FrameClip settings and extension menus. Restart PotPlayer after saving so its loaded XML menu refreshes. Windows may request administrator confirmation when the PotPlayer menu is installed in a protected directory.

Starting with 0.3.2, skinned menus use bundled x64/x86 native bridge DLLs. A thread-specific Windows hook loads the matching bridge into PotPlayer and intercepts only a pending FrameClip menu command. The bridge does not replace or patch PotPlayer files. Windows UI Automation, desktop-wide menu scanning, and the separate action popup are no longer used.

Dolby Vision sources without a reliable HDR10/HLG-compatible base layer are rejected for decoded reference output. Transcoded ProRes/DNxHR output does not retain Dolby Vision dynamic metadata. Use source-copy export when the original streams must be preserved.

## Build

```powershell
winget install --id JRSoftware.InnoSetup --exact --source winget --accept-package-agreements --accept-source-agreements
winget install --id zig.zig --exact --source winget --accept-package-agreements --accept-source-agreements
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Version 0.4.0
```

The release workflow prepares a draft containing the lightweight `PotPlayerFrameClip-v<version>-Setup.exe` and its `.sha256` file. It excludes Full installers. A reviewed preview release is published separately.

## Status and affiliation

FrameClip is an independent external extension. It is not an official PotPlayer SDK plugin and is not affiliated with or endorsed by Kakao or the PotPlayer developers.

For complete installation, troubleshooting, command-line options, and uninstall instructions, see the [Chinese README](README.md).

## License

MIT. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for third-party notices.
