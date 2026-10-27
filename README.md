<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/readme/banner-zh-dark.png">
  <source media="(prefers-color-scheme: light)" srcset="assets/readme/banner-zh-light.png">
  <img alt="FrameClip：把喜欢的镜头留下来。无损静帧与原码片段，按片名自动归档，支持多种格式、编码与 HDR/SDR 色彩空间。" src="assets/readme/banner-zh-dark.png" width="1600">
</picture>

# PotPlayer FrameClip

**把喜欢的镜头留下来，为收藏与创作积累素材。**

FrameClip 是 PotPlayer 的影视静帧与片段采集工具。从右键菜单保存无损图片、截取原码片段，用来收藏电影画面，或整理调色、剪辑和镜头研究所需的参照。素材按视频名称自动归入对应作品文件夹。

[下载 0.4.2 正式版](https://github.com/XWYT/PotPlayerFrameClip/releases/tag/v0.4.2) · [English](README.en.md) · [使用指南](docs/guide.zh.md)

## 主要功能

- **无损静帧与原码片段**：直接读取源文件，保存 PNG/TIFF 图片，或复制原编码的视频片段。另有精确切点导出，便于后期编辑。
- **按作品自动归档**：识别文件名中的片名、季集等信息，将同部作品的图片和片段集中保存；结合目录、媒体标题和已记住的名称映射进行归类。无法确认作品名时放入“待归类”文件夹。
- **多格式、编码与色彩空间**：支持多种 HDR/SDR 色彩空间、16-bit 图片，以及 ProRes、DNxHR 等编辑编码。截取 HDR 静帧时可同时生成 SDR 查看图。
- **采集不中断**：后台并行导出片段，期间仍可截图；查看进度、取消任务、重试或打开结果文件。默认优先使用可用的 GPU 解码，支持调整资源使用策略。

界面提供简体中文与英文、浅色与深色主题，以及可调整位置的多屏提醒。支持 32/64 位及便携版 PotPlayer，媒体处理在本机完成，无遥测。

## 安装

需要 Windows 10/11、PotPlayer、.NET Framework 4.6.2 或更高版本，以及配套的 `ffmpeg.exe` 和 `ffprobe.exe`。

1. 下载 [Releases](https://github.com/XWYT/PotPlayerFrameClip/releases/tag/v0.4.2) 中的 `PotPlayerFrameClip-v0.4.2-Setup.exe` 和同名 `.sha256` 校验文件。
2. 等待任务结束，退出 PotPlayer 和 FrameClip，再运行安装包。便携版播放器可在安装时手动选择目录。
3. 选择已有的 FFmpeg，或安装后在“设置 > 输出”中配置。
4. 重新打开 PotPlayer，播放本地视频，右键选择 **参照帧与片段截取**。

公开安装包不含 FFmpeg。没有安装时可先运行：

```powershell
winget install --id Gyan.FFmpeg --exact --source winget --accept-package-agreements --accept-source-agreements
```

可以先安装 FrameClip、稍后配置 FFmpeg；配置完成前无法截图或导出。安装包尚未进行商业代码签名，请从项目 Releases 下载并核对校验值。详细安装、自定义目录和卸载方法见[使用指南](docs/guide.zh.md#安装与升级)。

## 使用

| 想做什么 | 操作 |
| --- | --- |
| 保存当前画面 | 播放到所需位置，选择“截取当前帧” |
| 收藏一段原片 | 设置入点和出点，选择“导出原码片段” |
| 导出供剪辑的片段 | 设置入点和出点，选择“导出精确片段” |
| 查看任务进度或重试 | 从 FrameClip 托盘图标打开任务中心 |
| 找到已保存的素材 | 选择“打开当前作品图片文件夹”或“打开当前作品视频文件夹” |
| 更改格式、输出目录或提醒位置 | 打开“设置” |

默认保存在 Windows 视频目录下：

```text
FrameClip/
└── 作品名/
    ├── 图片/
    └── 视频/
```

输出根目录可以修改。分类依据视频名称等已有信息，不分析画面内容，也不联网检索影片资料。

## 格式与画质

| 输出 | 用途与支持 |
| --- | --- |
| 无损图片 | 16-bit RGB PNG、TIFF；PNG 写入可用色彩标签，文件名记录色彩信息 |
| 原码片段 | MKV，保留原视频编码和可复制的音频、字幕及元数据；切点受关键帧限制 |
| 精确片段 | MOV，ProRes 422 HQ / 4444 / 4444 XQ、DNxHR HQX / 444，配合 24-bit PCM 音频 |
| HDR / SDR | 支持多种色彩空间；HDR 原图保留源传递函数，可另存经色调映射的 Rec.709 SDR 副本 |

“无损”指图片的无损压缩和原码片段不重新编码。源视频解码与 RGB 转换仍有精度边界；ProRes/DNxHR 精确片段属于转码。HDR 原图应在支持色彩管理的软件中指定正确的输入色彩空间。Dolby Vision 的支持范围、导入说明及 GPU 兼容性见[使用指南](docs/guide.zh.md#格式与色彩)。

## 更多

- [使用指南：安装、设置、色彩处理与问题排查](docs/guide.zh.md)
- [GPU 处理与兼容性](docs/gpu-processing.md)：NVIDIA 主机已有实际媒体验证，Intel/AMD 实机覆盖仍有限。
- [版本说明](docs/releases/v0.4.2.md) · [更新记录](CHANGELOG.md)
- [从源码构建](docs/guide.zh.md#从源码构建)

FrameClip 是独立外部扩展，未使用 PotPlayer 官方 SDK，也未获得 PotPlayer 开发者或 Kakao 的背书。源码采用 [MIT License](LICENSE)，第三方许可见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
