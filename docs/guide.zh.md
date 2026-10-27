# FrameClip 使用指南

[返回首页](../README.md) · [English](guide.en.md)

## 安装与升级

支持 Windows 10/11、.NET Framework 4.6.2 或更高版本，以及标准安装、便携版的 32/64 位 PotPlayer。截图和导出需要同目录下配套的 `ffmpeg.exe` 与 `ffprobe.exe`。FrameClip 不捆绑 PotPlayer 或 .NET Framework。

从[项目 Releases](https://github.com/XWYT/PotPlayerFrameClip/releases/tag/v0.4.1) 下载安装包与校验文件，核对 SHA-256：

```powershell
Get-FileHash '.\PotPlayerFrameClip-v0.4.1-Setup.exe' -Algorithm SHA256
```

先完成任务并退出 PotPlayer 与 FrameClip，再运行安装包。桥接 DLL 会随播放器保持加载，升级时必须关闭播放器；不需要重启 Windows。安装器先备份再替换，配置失败时回退。已有设置和自定义菜单会保留。

公开轻量包不含 FFmpeg。安装时可以选择已有版本，或稍后在“设置 > 输出”中配置；升级时选择“稍后配置”不会清空已有路径。缺少 FFmpeg 可通过 WinGet 安装：

```powershell
winget install --id Gyan.FFmpeg --exact --source winget --accept-package-agreements --accept-source-agreements
```

安装包尚未进行商业代码签名。出现安全提示时先确认来源、文件名和校验值，不要关闭系统防护。FrameClip 在程序运行期间不访问网络。

### 自定义路径与静默安装

```powershell
$p=Start-Process -FilePath '.\PotPlayerFrameClip-v0.4.1-Setup.exe' -ArgumentList '/VERYSILENT','/NORESTART','/LANG=chinesesimplified','/POTPLAYERDIR="D:\Apps\PotPlayer"','/FFMPEGPATH="D:\Tools\ffmpeg\bin\ffmpeg.exe"' -PassThru; Wait-Process -Id $p.Id
```

| 参数 | 作用 |
| --- | --- |
| `/POTPLAYERDIR="目录"` | 包含 PotPlayer 主程序的目录 |
| `/FFMPEGPATH="文件"` | 已有的 `ffmpeg.exe`，同目录须有 `ffprobe.exe` |
| `/FFMPEGMODE=existing\|later` | 使用已有工具，或稍后配置 |
| `/NOSTARTUP=1` | 关闭 FrameClip 开机自启动 |
| `/DIR="目录"` | 更改程序安装目录 |
| `/VERYSILENT /NORESTART` | 静默安装，不重启 Windows |

“开机自启动”在当前 Windows 用户登录时启动 FrameClip。

## 采集与分类

通过 PotPlayer 右键菜单“参照帧与片段截取”操作：截取当前帧、设置入点/出点、导出原码或精确片段、清除范围，以及打开当前作品的图片/视频文件夹。任务提交后保留当时的源文件、时间范围、输出设置与归档信息，之后切换视频或修改设置不会改变已提交的任务。

图片从源视频按播放时间解码，不截取桌面，不应用显示器色调映射或播放器显示 LUT。仅支持可读取的本地媒体；网页流、DRM 和部分网络地址无法采集。

分类会清理常见发行名称中的分辨率、编码等标记，结合片名、季集、目录、媒体标题和已记住的别名匹配作品文件夹。这是初步的名称归类，不分析画面或联网查询。同一作品下建立“图片”和“视频”子目录，不会自动按题材、色调或人物分类。

`待归类剧集 [xxxxxxxx]` / `待归类作品 [xxxxxxxx]` 表示信息不足以确认作品名，例如仅有 `01.mkv`。同一来源目录会集中保存，不同未知作品尽量分开。

默认输出位于 `%USERPROFILE%\Videos\FrameClip`（Windows 视频目录）；输出根目录可修改。程序与设置、任务记录、别名和日志通常位于 `%LOCALAPPDATA%\PotPlayerFrameClip`，自定义程序目录不改变用户数据目录。

## 设置与任务

- **输出**：素材库目录、PNG/TIFF、精确片段编码、HDR 的 SDR 副本和 FFmpeg 路径。
- **性能**：GPU 优先或仅 CPU，播放优先/均衡/导出优先、视频并发数与后台进程优先级。截图使用独立通道。
- **外观**：语言、明暗主题、减少动态效果、提醒显示方式与位置。提醒可选四角或拖放自定义位置，保存显示器及相对位置。

任务中心支持进度、取消、重试、历史记录和打开输出文件夹。GPU 优先默认开启，但主要加速解码，图片编码、ProRes/DNxHR 编码和部分色彩滤镜仍由 CPU 完成；不支持的来源或资源不足时使用 CPU，短任务不保证更快。

FFmpeg/驱动能力决定可用后端，包括 CUDA、D3D11VA、Intel QSV 和单显卡场景的 DXVA2 回退。NVIDIA 开发主机已有实际媒体对照；Intel/AMD 实机、更多皮肤、物理混合 DPI 与长时间使用仍需扩大验证。详见 [GPU 说明](gpu-processing.md)。

切换语言后重新打开 PotPlayer，以刷新已载入的 XML 菜单。菜单位于受保护目录时，更新菜单可能需要一次管理员确认。

## 格式与色彩

| 输出 | 格式与精度 |
| --- | --- |
| 图片 | 16-bit RGB PNG/TIFF，无损压缩 |
| 原码片段 | MKV；首个视频流、可复制的音频/字幕和容器元数据；不重新编码，起点受关键帧限制 |
| 精确片段 | MOV；ProRes 422 HQ、4444、4444 XQ、DNxHR HQX、444；24-bit PCM / 48 kHz 音频 |

ProRes 422 HQ 与 DNxHR HQX 使用 10-bit 4:2:2 输入；ProRes 4444/4444 XQ 与 DNxHR 444 使用 10-bit 4:4:4 输入。精确片段会重新编码，部分字幕、源压缩音频和动态元数据不进入输出。“无损”不表示源视频解码、YCbCr 到 RGB 转换没有误差，也不表示 16-bit 输出能够恢复源文件已丢失的细节。

HDR 原图完成 YCbCr 到 RGB 的矩阵与码值范围变换，保留 PQ/HLG 传递函数，不烘焙显示 LUT。PNG 写入可用的色彩标签；TIFF 的标签支持因软件而异，建议按文件名和源元数据指定输入色彩空间。

### 导入后期软件

| 文件名标记 | 解释方式 |
| --- | --- |
| `Rec709-*` | 按文件名标出的传递函数解释 |
| `Rec2100-PQ` | Rec.2100 ST 2084/PQ 输入 |
| `Rec2100-HLG` | Rec.2100 HLG 输入 |
| `Rec709-SDR-TONEMAPPED` | 经色调映射的全范围 16-bit Rec.709 RGB |
| `DV-P*-Base-*` | 可解码基础层，未应用 Dolby Vision 动态映射 |

“同时生成 SDR 查看图”仅对 PQ/HLG 来源生效。副本通过线性浮点光和 FFmpeg Mobius 色调映射生成，需要 `zscale` 与 `tonemap` 滤镜；它用于普通查看器及 SDR 参照，不替代原 HDR 图，也不是可逆母版转换。

Dolby Vision Profile 5 等缺少可靠 HDR10/HLG 兼容基础层的来源会被拒绝生成解码参照。Profile 7/8 等兼容来源使用可解码基础层；动态元数据不会烘焙进图片或精确转码片段。需要保留可复制的源流信息时选择原码片段。

## 问题排查

| 问题 | 检查方法 |
| --- | --- |
| 插件菜单缺失，或点击后打开“关于 PotPlayer”/播放暂停 | 完成任务后退出播放器与 FrameClip，安装最新版并重开；确认播放器目录正确，存在 `Menus\FrameClipMenu.xml` 及下列桥接文件。检查安全软件拦截记录，不要关闭防护 |
| 无法定位当前媒体 | 使用本地文件，确认文件仍在原路径，播放器标题未被其他插件改写，再重新打开媒体 |
| 缺少 FFmpeg/FFprobe | 在设置中选择配套的工具；可用 `Get-Command ffmpeg,ffprobe -ErrorAction SilentlyContinue` 检查 PATH |
| HDR 原图发暗或色彩异常 | 在支持色彩管理的软件中指定输入色彩空间，或启用 SDR 查看图 |
| 没有生成 SDR 副本 | SDR 来源不会额外生成；PQ/HLG 来源检查 FFmpeg 是否有 `zscale`、`tonemap`。原图成功而副本失败时查看任务错误和日志 |
| 菜单语言未变化 | 保存后重开 PotPlayer；若取消过管理员确认，重新保存并允许菜单更新，或重跑安装器 |

```powershell
Get-Item "$env:LOCALAPPDATA\PotPlayerFrameClip\FrameClipBridge64.dll",
         "$env:LOCALAPPDATA\PotPlayerFrameClip\FrameClipBridge32.dll",
         "$env:LOCALAPPDATA\PotPlayerFrameClip\FrameClipBridgeHost32.exe"
ffmpeg -filters | Select-String 'zscale|tonemap'
```

原生桥接只处理 FrameClip 菜单命令，不覆盖或修改 PotPlayer 自带 DLL。问题持续时提交播放器版本、位数、皮肤、缩放比例，以及数据目录中的 `bridge-debug.log`、`menu-debug.log` 和 `logs` 中最新运行日志。日志可能含本地路径，公开前请遮盖个人信息。

## 卸载

完成任务，关闭 PotPlayer，再从 Windows“已安装的应用”卸载。默认保留设置、任务记录、日志和作品别名；导出素材不会删除。

静默卸载并主动删除用户设置数据：

```powershell
& "$env:LOCALAPPDATA\PotPlayerFrameClip\unins000.exe" /VERYSILENT /NORESTART /REMOVEUSERDATA=1
```

## 从源码构建

需要 .NET Framework 4.x C# 编译器、Zig 和 Inno Setup 6。Zig 仅用于构建原生桥接：

```powershell
winget install --id Microsoft.DotNet.Framework.DeveloperPack_4 --exact --source winget --accept-package-agreements --accept-source-agreements
winget install --id JRSoftware.InnoSetup --exact --source winget --accept-package-agreements --accept-source-agreements
winget install --id zig.zig --exact --source winget --accept-package-agreements --accept-source-agreements
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Version 0.4.1
python scripts/check-docs.py
```

构建会运行 C#、原生桥接和隔离安装回归，生成 `dist\PotPlayerFrameClip-v0.4.1-Setup.exe` 与 SHA-256。GitHub 工作流创建轻量包草稿，复查后再公开发布。

本地可构建离线 Full 候选：运行 `scripts/prepare-ffmpeg.ps1 -Download`，再运行 `scripts/build.ps1 -Version 0.4.1 -BundleFFmpeg`。下载版本与校验值固定在 `installer/ffmpeg-manifest.json`；工具安装到程序下的 `tools/ffmpeg/<版本>/`，不改 PATH 或外部工具。内置工具需要 64 位 Windows；32 位 Windows 应选择兼容外部 FFmpeg。Full 包支持 `/FFMPEGMODE=bundled`，轻量包会拒绝该选项。Full 包须完成对应源码、构建配方与依赖许可归档后才能公开分发，目前不提供公开下载。
