# PotPlayer FrameClip

在 PotPlayer 右键菜单中直接截取高精度参照帧，或按入点、出点导出带声音的原码片段与后期友好片段。FrameClip 读取本地媒体源文件，不经过 Windows 桌面截图、播放器渲染、显示器色调映射等显示链路；SDR、HDR10/PQ、HLG 画面均按源编码特征保留，适合剪辑、调色、合成、画面分析与资料归档。

安装后只增加一个简洁菜单：**参照帧与片段截取**。

## 主要功能

- 截取 16-bit RGB PNG 或 TIFF 静帧。
- PNG 写入可用的色域、传递函数与范围标签；文件名同时记录色彩解释信息。
- 导出原码 MKV 片段，流复制视频、音频、字幕与容器元数据。
- 导出精确 MOV 片段，可选 ProRes 422 HQ、ProRes 4444、ProRes 4444 XQ、DNxHR HQX、DNxHR 444，并使用 24-bit PCM 音频。
- 自动建立 `作品名\图片` 与 `作品名\视频`，识别季集、分辨率、平台、编码、发布组等常见命名差异。
- 文件、媒体元数据和上级目录都没有作品名时，使用带稳定来源标识的“待归类”目录，避免把纯集数误判成片名。
- 支持 32 位、64 位 PotPlayer，以及便携版目录。
- 可指定输出根目录、图片格式、精确片段编码和 FFmpeg 路径。
- 保留安装前的 PotPlayer 自定义菜单；重复安装、升级和卸载均带恢复逻辑。

## 精度说明

FrameClip 的图片转换仅完成 YCbCr 到 RGB 的矩阵与码值范围变换，不应用显示 LUT，也不把 PQ、HLG 自动压成 SDR。HDR 图片在普通图片查看器中可能显得暗淡或异常，这是查看器按错误传递函数显示造成的结果。导入后期软件后，应依据文件名和源元数据指定正确的输入色彩空间。

Dolby Vision Profile 5 等缺少可靠 HDR10/HLG 兼容基础层的来源会被拒绝生成参照帧，防止输出偏色图片。Profile 7/8 等兼容来源输出可解码基础层；动态元数据不会烘焙进 PNG、TIFF、ProRes 或 DNxHR。需要保留完整源流时使用“导出原码片段”。

## 系统要求

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 或 Windows 11，x64/x86 |
| 播放器 | PotPlayer 32 位或 64 位；标准安装、便携版均可 |
| 运行环境 | .NET Framework 4.6.2 或更高版本 |
| 媒体工具 | `ffmpeg.exe` 与同目录下的 `ffprobe.exe` |

FrameClip 不捆绑 PotPlayer、FFmpeg 或 .NET Framework。程序运行期间不访问网络。

### 安装 FFmpeg

推荐使用 WinGet 安装当前稳定版：

```powershell
winget install --id Gyan.FFmpeg --exact --source winget --accept-package-agreements --accept-source-agreements
```

关闭并重新打开 PowerShell 后检查：

```powershell
ffmpeg -version
ffprobe -version
```

也可从 [FFmpeg 官方下载页](https://ffmpeg.org/download.html)进入其列出的 Windows 构建站点。手动解压时，请在安装器中选择 `bin\ffmpeg.exe`；`ffprobe.exe` 应位于同一目录。

### 安装 .NET Framework

Windows 10/11 通常已包含兼容的 .NET Framework 4.x。程序启动时若提示缺少运行环境，可安装 [Microsoft .NET Framework 4.8.1 Runtime](https://dotnet.microsoft.com/download/dotnet-framework/net481)。用于构建源码的完整开发包也可通过 WinGet 安装：

```powershell
winget install --id Microsoft.DotNet.Framework.DeveloperPack_4 --exact --source winget --accept-package-agreements --accept-source-agreements
```

## 安装

### 方式一：交给 AI Agent 安装

直接复制下面整段提示词，发送给能够访问网络、文件和 PowerShell 的 AI Agent：

```text
请在这台 Windows 电脑上安装 PotPlayer FrameClip。

项目地址：https://github.com/XWYT/PotPlayerFrameClip

要求：
1. 只从上述项目的正式 GitHub Releases 下载最新稳定版安装程序 PotPlayerFrameClip-v<版本>-Setup.exe，不要下载源码压缩包，也不要从第三方网站获取文件。
2. 确认下载链接属于上述 GitHub 仓库；如果发布页提供 SHA-256，请在安装前核对。安装包目前没有商业代码签名，不要关闭 Windows 安全功能，也不要在来源无法确认时绕过安全警告。
3. 检查 PotPlayer、ffmpeg.exe、ffprobe.exe 和 .NET Framework 4.6.2 或更高版本。缺少 FFmpeg 时，优先使用 WinGet 安装 Gyan.FFmpeg；不要重新安装或修改已经正常工作的依赖。
4. 自动识别 PotPlayer 安装目录和 FFmpeg 路径。标准安装、32 位、64 位或便携版无法自动识别时，再根据本机实际路径向安装程序传入 /POTPLAYERDIR 和 /FFMPEGPATH。
5. 使用安装程序完成当前用户安装，不重启 Windows。如果 PotPlayer 正在运行且会影响安装，请先让我关闭，不要擅自结束播放器或其他无关进程。
6. 安装后确认 %LOCALAPPDATA%\PotPlayerFrameClip\PotPlayerFrameClip.exe 存在，FrameClip 常驻程序能够启动，并检查 PotPlayer 目录下是否生成 Menus\FrameClipMenu.xml。
7. 告诉我最终安装的版本、程序目录、识别到的 PotPlayer 与 FFmpeg 路径、验证结果，以及是否需要我重新打开 PotPlayer。遇到失败时保留错误信息并说明具体处理办法，不要反复盲目重试。
```

Agent 仍可能需要你确认 Windows SmartScreen 提示或 PotPlayer 便携版目录。安装结束后，重新打开一次 PotPlayer，右键菜单中应出现 **参照帧与片段截取**。

### 方式二：下载安装包

1. 从 GitHub Releases 下载一个文件：`PotPlayerFrameClip-v<版本>-Setup.exe`，不要下载 `Source code` 压缩包。
2. 双击安装程序。Windows SmartScreen 在未签名开源程序上可能显示警告，请先核对文件名与 GitHub 仓库；确认来源后选择“更多信息”继续。
3. 安装器会自动查找 PotPlayer 和 FFmpeg。便携版或自定义目录未被识别时，在向导中手动选择。
4. 完成安装后关闭并重新打开一次 PotPlayer，使菜单选择稳定落盘。
5. 播放本地媒体，右键选择 **参照帧与片段截取**。

程序、设置、日志和作品别名位于：

```text
%LOCALAPPDATA%\PotPlayerFrameClip
```

默认输出位于：

```text
%USERPROFILE%\Videos\FrameClip\作品名\图片
%USERPROFILE%\Videos\FrameClip\作品名\视频
```

输出根目录可以在 FrameClip 设置中修改。程序不会在参照素材目录中写入使用说明或安装文件。

### 静默安装与自定义路径

已经下载安装包时，可以在安装包所在目录运行以下 PowerShell 命令：

```powershell
$setup=Get-ChildItem -LiteralPath . -Filter 'PotPlayerFrameClip-v*-Setup.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1; if(-not $setup){throw '当前目录没有 FrameClip 安装包'}; $p=Start-Process -FilePath $setup.FullName -ArgumentList '/VERYSILENT','/NORESTART','/LANG=chinesesimplified' -PassThru; Wait-Process -Id $p.Id
```

便携版 PotPlayer 与自定义 FFmpeg：

```powershell
$p=Start-Process -FilePath '.\PotPlayerFrameClip-v0.2.0-Setup.exe' -ArgumentList '/VERYSILENT','/NORESTART','/LANG=chinesesimplified','/POTPLAYERDIR="D:\Apps\PotPlayer"','/FFMPEGPATH="D:\Tools\ffmpeg\bin\ffmpeg.exe"' -PassThru; Wait-Process -Id $p.Id
```

可选参数：

| 参数 | 作用 |
| --- | --- |
| `/POTPLAYERDIR="目录"` | 指定包含 PotPlayer 主程序的目录 |
| `/FFMPEGPATH="文件"` | 指定 `ffmpeg.exe` |
| `/NOSTARTUP=1` | 不随当前用户登录启动菜单桥接程序 |
| `/DIR="目录"` | 修改 FrameClip 程序安装目录 |
| `/VERYSILENT /NORESTART` | 无界面安装，不重启 Windows |

## 使用

| 菜单项 | 结果 |
| --- | --- |
| 截取当前帧 | 按当前播放时间从源文件解码一张 16-bit RGB 图片 |
| 设置入点 / 设置出点 | 记录当前媒体与时间，用于后续片段导出 |
| 导出原码片段 | 快速流复制，保留源编码和可复制的音频、字幕；切点受关键帧限制 |
| 导出精确片段 | 重新编码，切点更准确，输出 ProRes 或 DNxHR 与 PCM 音频 |
| 清除入点和出点 | 删除当前记录的范围 |
| 设置 | 修改输出位置、图片格式、片段编码和 FFmpeg 路径 |
| 打开当前作品图片/视频文件夹 | 目录不存在时先创建，再由资源管理器打开 |

若输出进入 `待归类剧集 [xxxxxxxx]` 或 `待归类作品 [xxxxxxxx]`，说明源文件只提供了 `01.mkv`、`02.mkv` 等弱名称，媒体元数据与上级目录也没有可验证的作品名。此时程序会保持同一来源目录的素材集中，并避免与其他未知作品合并。

## 导入后期软件

- `Rec709-*`：按文件名标出的传递函数解释；常见来源为 Rec.709/BT.1886 类显示链路。
- `Rec2100-PQ`：指定 Rec.2100 ST 2084/PQ 输入。
- `Rec2100-HLG`：指定 Rec.2100 HLG 输入。
- `DV-P*-Base-*`：仅含可解码基础层，未应用 Dolby Vision 动态映射。
- PNG 优先用于需要读取色彩标签的流程。
- TIFF 为 16-bit 无损 RGB，但不同软件对 TIFF 色彩标签的支持差异较大，建议按文件名手动指定输入色彩空间。

原码片段保留源流，适合保存 HDR/Dolby Vision 信息或后续自行处理。精确片段会重新编码，动态元数据、部分字幕和原压缩音频不进入输出。

## 问题排查

### 右键菜单没有“参照帧与片段截取”

1. 关闭并重新打开 PotPlayer。
2. 确认 `%LOCALAPPDATA%\PotPlayerFrameClip\PotPlayerFrameClip.exe` 存在。
3. 便携版用户重新运行安装器，并在“PotPlayer 位置”中选择正确目录。
4. 检查 PotPlayer 目录下是否存在 `Menus\FrameClipMenu.xml`。

### 点击所有扩展按钮都变成播放或暂停

这表示 PotPlayer 已载入菜单 XML，但菜单桥接程序没有运行，或仍在使用旧版本：

```powershell
Get-CimInstance Win32_Process -Filter "Name='PotPlayerFrameClip.exe'" | Select-Object ProcessId,ExecutablePath
```

没有结果时启动程序：

```powershell
Start-Process "$env:LOCALAPPDATA\PotPlayerFrameClip\PotPlayerFrameClip.exe"
```

若路径指向旧文件夹，重新运行最新版安装器。安装完成后重启一次 PotPlayer。

### 设置窗口无法打开，或菜单动作明显错位

只重启 FrameClip 常驻程序，不需要结束 PotPlayer：

```powershell
Stop-Process -Name PotPlayerFrameClip -Force -ErrorAction SilentlyContinue; Start-Process "$env:LOCALAPPDATA\PotPlayerFrameClip\PotPlayerFrameClip.exe"
```

仍有问题时重新安装最新版。不同 PotPlayer 皮肤可能改变自绘菜单行为；提交问题时请附上 PotPlayer 版本、皮肤名与 `menu-debug.log`。

### 提示无法定位当前播放文件

- FrameClip 只读取本地文件；DRM、网页流、部分网络地址无法截取。
- 确认媒体文件仍在原路径，且 PotPlayer 窗口标题没有被其他插件完全改写。
- 重新打开该文件后再试。
- 查看 `%LOCALAPPDATA%\PotPlayerFrameClip\logs` 中最新日志。

### 提示 FFmpeg 或 FFprobe 不存在

```powershell
Get-Command ffmpeg,ffprobe -ErrorAction SilentlyContinue | Select-Object Name,Source
```

若没有结果，按“安装 FFmpeg”一节安装；也可以在 FrameClip 设置中直接选择 `ffmpeg.exe`。

### HDR 图片在图片查看器中发暗、发灰或色彩异常

图片保留源传递函数，没有执行显示色调映射。普通查看器常把 PQ/HLG RGB 当作 sRGB 显示。请在支持色彩管理的后期软件中按文件名指定 Rec.2100 PQ、Rec.2100 HLG 或相应 SDR 输入空间。

### Windows SmartScreen 阻止安装

当前安装包没有商业代码签名。请从项目 Releases 下载，核对版本化文件名；发布者提供 SHA-256 时可运行：

```powershell
Get-FileHash '.\PotPlayerFrameClip-v0.2.0-Setup.exe' -Algorithm SHA256
```

## 卸载

从 Windows“已安装的应用”卸载即可。为了可靠恢复安装前的 PotPlayer 菜单，请先关闭 PotPlayer。

默认卸载会保留设置、日志与作品别名。静默卸载并删除这些数据：

```powershell
& "$env:LOCALAPPDATA\PotPlayerFrameClip\unins000.exe" /VERYSILENT /NORESTART /REMOVEUSERDATA=1
```

参照图片与视频位于独立输出目录，卸载程序不会删除。

## 从源码构建

构建环境需要 .NET Framework 4.x C# 编译器和 Inno Setup 6：

```powershell
winget install --id Microsoft.DotNet.Framework.DeveloperPack_4 --exact --source winget --accept-package-agreements --accept-source-agreements
winget install --id JRSoftware.InnoSetup --exact --source winget --accept-package-agreements --accept-source-agreements
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Version 0.2.0
```

构建会执行 C# 单元测试、菜单静态检查，以及隔离的安装、重复安装、卸载恢复测试。最终发布资产为：

```text
dist\PotPlayerFrameClip-v0.2.0-Setup.exe
```

`dist\release` 与 `dist\obj` 仅供本机构建暂存，GitHub Release 工作流只上传单个安装包。源码按仓库原目录发布。

## 隐私与安全边界

- 不上传媒体、文件名、截图、日志或配置。
- 不访问网络，不包含遥测与自动更新。
- 只在用户执行命令时读取当前本地媒体，并调用外部 FFmpeg/FFprobe。
- 菜单桥接程序仅监听 PotPlayer 菜单会话；点击离开 PotPlayer 后会清除会话状态。
- FrameClip 是独立外部扩展，未使用 PotPlayer 官方 SDK，也未获得 PotPlayer 开发者或 Kakao 的背书。

## 许可

源码采用 MIT License。第三方组件、构建工具和翻译文件的许可见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

English documentation: [README.en.md](README.en.md)
