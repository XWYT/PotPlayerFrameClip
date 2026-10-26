# README Artwork

FrameClip collects film/TV stills and clips for professional references and
personal collections. Banners describe output capabilities, not configurable
queue sizes or implementation details. Do not describe ProRes/DNxHR transcoding
as lossless source copying or imply all HDR formats are supported.
Lead with multiple HDR/SDR color spaces, not lists of transfer functions,
primaries, bit depths or codec profiles. Keep those details in technical sections.

The artwork uses the native interface's graphite/green palette, a semibold
wordmark and a softened dot-matrix treatment sampled from the actual Clip glyphs.
Chinese and English each have static light/dark PNGs. GitHub selects the theme
through a picture element; no web font, animation or runtime dependency is added.
README.md uses banner-zh-light.png and banner-zh-dark.png; README.en.md uses
banner-light.png and banner-dark.png. The documentation check enforces this pairing.

The intrinsic image size is 1600 x 420. Set only `width="1600"` on the
fallback image, with no height attribute. GitHub constrains width to the README
column; a fixed height stretches the artwork when that column narrows. Let the
browser calculate height from the intrinsic ratio in both languages and themes.

Regenerate with Python and Pillow 12.3.0, supplying locally licensed font paths:

```powershell
python scripts/render-banner.py --font <regular-font> --display-font <semibold-font> --cjk-font <chinese-font>
python scripts/check-docs.py
```

The checked-in images use OpenAI Sans Regular/Semibold and Microsoft YaHei for
Chinese. Fonts themselves are not redistributed. The renderer can use other
local sans-serif faces. Changing fonts requires another visual fit check.

Review all four images at full size and README width. Keep each image below
150 KB, preserve accessible alternative text, and mirror content in both
READMEs. Installer documentation and transactional rollback paths must include
the exact images and linked documents they ship.
