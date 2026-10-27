# README Artwork

FrameClip collects film/TV stills and clips for personal collections and
professional reference. Lead with the reader's goal: keep favorite shots and
gather material for creative work. Support that promise in this order:
lossless stills and source-copy clips; automatic folders from video names;
output formats, editing codecs and multiple HDR/SDR color spaces.
Use plain user-facing benefits, not configurable queue counts or engineering
history. Name-based classification is preliminary filing, not scene analysis or
online cataloging. Do not describe ProRes/DNxHR transcoding as lossless copying
or imply universal HDR support. Transfer functions, bit depths and codec profiles
belong in format tables and the bilingual user guides, not the banner.

READMEs contain the purpose, key capabilities, installation and everyday use.
Keep detailed color/import limits, maintenance commands and troubleshooting in
guide.zh.md / guide.en.md. Remove duplicate explanations and development
narration; retain the limits that affect the user's output or installation.

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
