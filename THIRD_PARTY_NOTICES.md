# Third-party notices

## FFmpeg and FFprobe

PotPlayer FrameClip invokes `ffmpeg.exe` and `ffprobe.exe` as separate external programs. They are not included in the release installer.

FFmpeg builds may use LGPL, GPL, or additional terms depending on their configuration. Review the selected build before redistribution:

- https://ffmpeg.org/download.html
- https://ffmpeg.org/legal.html

## PotPlayer

PotPlayer is a separate product and is not redistributed by this project. PotPlayer FrameClip is an independent external extension and is not affiliated with or endorsed by Kakao or the PotPlayer developers.

## Inno Setup

The Windows installer is compiled with Inno Setup. Inno Setup is a build-time dependency and is not installed on the user's computer by FrameClip.

- https://jrsoftware.org/isinfo.php
- Inno Setup license: https://jrsoftware.org/files/is6-license.txt

## Simplified Chinese translation for Inno Setup

The source tree vendors `installer/languages/ChineseSimplified.isl` from `kira-96/Inno-Setup-Chinese-Simplified-Translation`, pinned to commit `1ff90acc4ed4aee82b1cda43253243deee3daed4`.

- Source: https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation
- License copy: `installer/languages/LICENSE`
- License: MIT, copyright (c) 2019-2020 kirakira

The translation file is used only while compiling the installer. Its license text is retained in the source repository.
