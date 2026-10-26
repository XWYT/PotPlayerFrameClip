# GPU Processing

Status: development candidate. Installation and public release are separate gates.

## User Setting

Settings > Performance > Video and screenshots offers **GPU preferred** (default)
and **CPU only**. Existing configurations without ProcessingDevice use GPU
preferred. Accepted jobs and retries retain their original setting.

GPU preferred accelerates supported source decoding, not every processing step.
The source-copy MKV command performs no decoding. ProRes/DNxHR, PNG/TIFF encoding,
reference color conversion and optional SDR tone mapping retain the CPU pipeline.
The task row and history report the actual backend, not merely the preference.
No NVENC H.264/HEVC substitution is made for editing codecs.

## Lightweight Implementation

- Reuse the separately installed FFmpeg and the existing graphics driver.
- Enumerate hardware adapters once through Windows DXGI; no recurring WMI scan,
  GPU-monitoring subprocess, bundled CUDA Toolkit or additional media runtime.
- Probe the external FFmpeg's compiled accelerators and decoders once per
  path/size/mtime identity. Collapse concurrent queries; retain at most eight
  capability records. A failed optional query uses CPU with a visible reason,
  not a fake capability record; cancellation still cancels the job.
- Try up to four adapters, ordered by dedicated capacity. One unambiguous NVIDIA
  permits CUDA device 0, then bound D3D11VA; multiple NVIDIA cards use explicit
  D3D11VA indices, never guessed CUDA ordinals. Intel additionally permits QSV
  when the matching codec decoder exists; derive it from the exact D3D11 device.
  AMD and other compatible Windows adapters use D3D11VA. DXVA2 is the last
  hardware fallback only on a single-adapter system: D3D9/DXGI ordinals are not
  interchangeable, and hybrid systems must not charge an unknown default device
  against another adapter's memory budget. Then use the reference CPU path.
- Force hardware output surfaces, then download once before the unchanged
  reference filter chain. Do not repeatedly upload/download frames for CPU codecs.
- At most two GPU video workers, FIFO admission, and one independent image lane.
  Admission estimates decoder surfaces from resolution/bit depth. Its video budget
  is per adapter: one quarter of dedicated VRAM, bounded to 256-1536 MiB; unknown
  capacity uses 512 MiB. UMA with at most 256 MiB dedicated and larger shared
  capacity instead uses shared/8, bounded to 256-1024 MiB. This is a soft estimate,
  not live free-VRAM telemetry or a hard GPU cap. Two active video decoders total
  remains the maximum; separate adapters do not share one capacity estimate.
- A source larger than that per-decoder estimate budget uses CPU. Screenshots
  bypass the video wait queue but still undergo the per-decoder safety check.
- Cache hardware initialization failures for five minutes, up to 64 format/tool
  keys including adapter/backend identity. A failed adapter does not poison
  another. When all candidates are cached, CPU work bypasses occupied GPU slots.
  Cancellation, disk failure and failures after reported progress are not
  silently replayed. Unsupported reference formats use CPU immediately.
- No automatic driver installation or changes to PotPlayer rendering/OSD settings.

The current conservative allowlist covers H.264 8-bit 4:2:0 and HEVC/VP9/AV1
8/10-bit 4:2:0. AV1 additionally requires ffprobe to explicitly report
`film_grain=0`; grain or an unknown grain state uses CPU for reference fidelity.
It excludes RGB, 4:2:2, 4:4:4, 12-bit and Dolby Vision from hardware decoding.
Hardware/driver support can still reject an allowlisted source. H.264, HEVC10,
VP9 8/10 and grain-free AV1 8/10 have CUDA/D3D11VA/DXVA2 pixel checks on RTX 4060.
AMD/Intel policy/argument tests are not actual device acceptance; QSV and other
vendors/drivers still need real-device testing. No universal-GPU claim is made.

## Open-Source Options Evaluated

| Project/API | Decision | Reason |
| --- | --- | --- |
| FFmpeg NVDEC/CUDA, D3D11VA, QSV and DXVA2 | Use now, capability-driven | Existing external dependency; no extra distributed runtime; QSV/Intel hardware acceptance outstanding |
| nv-codec-headers | Build-time reference only | Headers expose NVIDIA codec APIs; not a separate end-user dependency |
| libplacebo via FFmpeg | Isolated Vulkan trial completed, not enabled | HDR output differs from reference and short-job initialization costs dominate this sample |
| Intel VPL / QSV | Reuse FFmpeg QSV now; no new bundled VPL | Exact D3D11-derived device; relies on the user's capable FFmpeg and Intel driver/runtime |
| AMD AMF | Evaluated, no new runtime | The inspected FFmpeg n7.1.1 has no AMF decoder; AMF encoding is not a replacement for requested ProRes/DNxHR |
| Direct in-process libavcodec/CUDA integration | Defer | Could amortize context creation, but adds native ABI, crash isolation, cancellation and redistribution complexity |

No code from libplacebo, AMF or Intel VPL is bundled in this candidate. libplacebo's
license is LGPL-2.1; the reviewed Intel VPL repository uses MIT. FFmpeg licensing
depends on the user's selected build. CUDA Toolkit is not needed to run an
appropriately built FFmpeg binary, but compatible NVIDIA drivers are required.

Primary references reviewed on 2026-10-26:

- FFmpeg command reference: https://ffmpeg.org/ffmpeg-doc.html
- NVIDIA FFmpeg guide: https://docs.nvidia.com/video-technologies/video-codec-sdk/13.0/ffmpeg-with-nvidia-gpu/index.html
- NVDEC implementation: https://github.com/FFmpeg/FFmpeg/blob/n7.1.1/libavcodec/nvdec.c
- D3D11VA implementation: https://github.com/FFmpeg/FFmpeg/blob/n7.1.1/libavcodec/d3d11va.c
- NVIDIA interface headers: https://github.com/FFmpeg/nv-codec-headers
- libplacebo: https://github.com/haasn/libplacebo
- Intel VPL: https://github.com/intel/libvpl
- QSV device derivation: https://github.com/FFmpeg/FFmpeg/blob/n7.1.1/libavutil/hwcontext_qsv.c
- DXVA2 formats/devices: https://github.com/FFmpeg/FFmpeg/blob/n7.1.1/libavutil/hwcontext_dxva2.c
- AMD AMF: https://github.com/GPUOpen-LibrariesAndSDKs/AMF
- AV1 grain signaling: https://github.com/FFmpeg/FFmpeg/blob/n7.1.1/fftools/ffprobe.c
- DXGI adapter description: https://learn.microsoft.com/en-us/windows/win32/api/dxgi/ns-dxgi-dxgi_adapter_desc

## Measured Results

2026-10-26, i7-13700KF / RTX 4060 / FFmpeg n7.1.1. Two 3-second ProRes HQ
exports plus one PNG screenshot, playback-first shared CPU budget. Two samples per
case, reversed order in the second pass; synthetic 24fps sources. These are small
local samples, not universal speed or playback-smoothness guarantees.

| Source / backend | Mean wall s | Mean CPU s | Sampled group peak MiB | Mean image latency s |
| --- | ---: | ---: | ---: | ---: |
| 1080p H.264 / CPU | 4.94 | 20.38 | 416 | 0.25 |
| 1080p H.264 / CUDA | 5.09 | 19.70 | 739 | 0.48 |
| 1080p H.264 / D3D11VA | 5.37 | 20.61 | 583 | 0.56 |
| UHD HEVC10 PQ / CPU | 18.00 | 76.66 | 1913 | 0.92 |
| UHD HEVC10 PQ / CUDA | 17.88 | 72.50 | 1708 | 1.02 |
| UHD HEVC10 PQ / D3D11VA | 18.40 | 75.39 | 2506 | 1.37 |

CUDA reduced CPU time by about 5.4% and sampled host working set by about 10.7%
for this UHD case, with essentially unchanged wall time. Short H.264 and screenshot
tasks incurred initialization overhead. CPU only remains available. GPU decoding
is not a promise of faster ProRes export while encoding remains CPU-bound.

The initial 1024 MiB estimated video budget serialized the two UHD GPU jobs,
roughly doubling elapsed time. The capacity-scaled budget restores two-video
admission on this 8 GiB card. Raw before/after evidence is in the ignored local
dist/validation/gpu-benchmark-conservative.xml and gpu-benchmark.xml files.

First 24 decoded video frames and the screenshot match the CPU benchmark hashes.
Focused production tests also compare PNG/TIFF, PQ SDR companions and all five
ProRes/DNxHR presets, metadata and audio codec/sample-rate fields. They do not
constitute whole-library, Dolby Vision, all-driver or real-player acceptance.

## Isolated libplacebo Trial

2026-10-26, RTX 4060 driver 32.0.16.1656, external FFmpeg n7.1.1. Synthetic
640x360 PQ/BT.2020 10-bit, 24 frames; CPU/GPU/GPU/CPU process order. CPU reference
zscale + Mobius measured 158/132 ms; Vulkan/libplacebo measured 1263/1147 ms.
Both exited successfully but decoded RGB48 frame hashes differed. These are
short-process measurements, not a sustained UHD performance comparison or an
image-quality ranking. This trial does not justify silently switching tone
mappers, even with the same Mobius parameter name. Production keeps the existing
color pipeline. Local hashes: dist/validation/placebo-{cpu,gpu}.md5 (ignored).

GPU trial filter, with `-init_hw_device vulkan=fc:0 -filter_hw_device fc`:

```text
format=yuv420p10le,hwupload,libplacebo=tonemapping=mobius:tonemapping_param=0.3:peak_detect=false:color_primaries=bt709:color_trc=bt709:colorspace=bt709:range=full:format=yuv444p16le:dithering=none,hwdownload,format=yuv444p16le,format=rgb48be
```

The single AV1 grain fixture also matched CPU on this host across the three
tested backends; the grain guard is precautionary for unvalidated drivers, not
a claim that this host produced wrong pixels. Intel/AMD real-device results
cannot be inferred from these NVIDIA results.

## Validation Commands

Run from the dedicated plugin checkout, without modifying the live installation:

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-jobs.ps1 -FfmpegPath 'C:\Program Files\FFmpeg\ffmpeg.exe' -RenderDirectory .\dist\validation\ui
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-jobs.ps1 -FfmpegPath 'C:\Program Files\FFmpeg\ffmpeg.exe' -RenderDirectory .\dist\validation -GpuBenchmark

The hardware integration/benchmark suite intentionally requires a suitable local
GPU. The standard build does not require GPU hardware or media fixtures.

The latest focused run passed 508 checks with zero failures. Besides real-media
fidelity, child-process fixtures exercise initialization fallback, disk-full and
post-progress failures, failure-cache isolation, temporary-output cleanup and
CPU fallback while both GPU video slots are occupied, optional capability-query
failure, multi-adapter cache/budget isolation, UMA admission, and exact QSV binding.
Portable policy tests do not establish real Intel/AMD hardware support. These fixtures test the
real runner/fallback boundary without intentionally destabilizing the GPU driver.
