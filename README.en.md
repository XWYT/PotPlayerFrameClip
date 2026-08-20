# PotPlayer FrameClip

PotPlayer FrameClip adds a **Reference Frame & Clip Capture** submenu to PotPlayer. It decodes high-precision stills from the local source file and exports source-copy or edit-friendly clips between user-defined in/out points. The capture path bypasses Windows desktop screenshots, renderer output, display tone mapping, and monitor transforms.

## Highlights

- 16-bit RGB PNG or TIFF stills.
- Source-copy MKV clips with video, optional audio, subtitles, chapters, and metadata.
- Accurate ProRes or DNxHR clips with 24-bit PCM audio.
- Automatic per-title `Images` and `Videos` organization with release-name matching.
- SDR, HDR10/PQ, and HLG source labeling without automatic tone mapping.
- 32-bit and 64-bit PotPlayer support, including portable installations.
- Single-file per-user installer; FFmpeg remains an external dependency.

## Requirements

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
Install the latest stable release of PotPlayer FrameClip on this Windows computer.

Project URL: https://github.com/XWYT/PotPlayerFrameClip

Use only the GitHub repository above. Download PotPlayerFrameClip-v<version>-Setup.exe from its official Releases page, not a source archive or third-party mirror. Verify a published SHA-256 value when available. Check PotPlayer, FFmpeg/FFprobe, and .NET Framework 4.6.2 or newer; install Gyan.FFmpeg with WinGet only when FFmpeg is missing. Detect standard, 32-bit, 64-bit, and portable PotPlayer installations without changing unrelated settings. Install for the current user without restarting Windows, then verify the installed executable, FrameClip process, and Menus\FrameClipMenu.xml. Do not disable Windows security or bypass an unverified warning. Report the installed version, resolved paths, verification results, and any action I still need to take.
```

### Option 2: install manually

Download `PotPlayerFrameClip-v<version>-Setup.exe` from GitHub Releases and run it. Do not download the source archive. Restart PotPlayer once after installation. Portable installations can be selected in the wizard.

Silent installation:

```powershell
$p=Start-Process -FilePath '.\PotPlayerFrameClip-v0.2.0-Setup.exe' -ArgumentList '/VERYSILENT','/NORESTART','/POTPLAYERDIR="D:\Apps\PotPlayer"','/FFMPEGPATH="D:\Tools\ffmpeg\bin\ffmpeg.exe"' -PassThru; Wait-Process -Id $p.Id
```

## Color handling

FrameClip converts source YCbCr values to 16-bit RGB while retaining the encoded transfer characteristic. It does not display-render or tone-map PQ/HLG to SDR. Assign the input color space in the destination application using the filename and source metadata. TIFF tag support varies by application; manual input assignment is recommended.

Dolby Vision sources without a reliable HDR10/HLG-compatible base layer are rejected for decoded reference output. Transcoded ProRes/DNxHR output does not retain Dolby Vision dynamic metadata. Use source-copy export when the original streams must be preserved.

## Build

```powershell
winget install --id JRSoftware.InnoSetup --exact --source winget --accept-package-agreements --accept-source-agreements
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Version 0.2.0
```

The release workflow uploads one asset: `PotPlayerFrameClip-v<version>-Setup.exe`.

## Status and affiliation

FrameClip is an independent external extension. It is not an official PotPlayer SDK plugin and is not affiliated with or endorsed by Kakao or the PotPlayer developers.

For complete installation, troubleshooting, command-line options, and uninstall instructions, see the [Chinese README](README.md).

## License

MIT. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for third-party notices.
