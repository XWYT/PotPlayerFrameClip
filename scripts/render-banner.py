"""Render static, theme-aware README artwork; Pillow is a build-only dependency."""
import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont


def render(font_path, display_path, cjk_path, output, theme, language):
    scale = 2
    width, height = 1600, 420
    dark = theme == "dark"
    background = (23, 26, 25) if dark else (244, 246, 244)
    foreground = (235, 241, 237) if dark else (32, 42, 36)
    muted = (162, 178, 168) if dark else (77, 96, 84)
    accent = (133, 187, 155) if dark else (47, 112, 74)
    image = Image.new("RGB", (width * scale, height * scale), background)
    title_font = ImageFont.truetype(str(display_path), 132 * scale)
    value_font = ImageFont.truetype(str(display_path), 29 * scale)
    label_font = ImageFont.truetype(str(font_path), 20 * scale)
    zh = language == "zh"
    chinese_font = ImageFont.truetype(str(cjk_path), 30 * scale) if zh else None
    chinese_body = ImageFont.truetype(str(cjk_path), 23 * scale) if zh else None
    chinese_label = ImageFont.truetype(str(cjk_path), 20 * scale) if zh else None
    mask = Image.new("L", image.size)
    ImageDraw.Draw(mask).text((58 * scale, 100 * scale), "FrameClip", font=title_font, fill=255, anchor="lt")
    image = image.convert("RGBA")
    solid_mask = mask.copy()
    ImageDraw.Draw(solid_mask).rectangle((454 * scale, 0, width * scale, height * scale), fill=0)
    image.paste(foreground + (255,), (0, 0), solid_mask)
    # Sampling the actual wordmark gives the dot pattern meaning: pixels become type.
    dots = Image.new("RGBA", image.size)
    draw = ImageDraw.Draw(dots)
    for y in range(98, 244, 5):
        for x in range(456, 875, 5):
            coverage = mask.getpixel((x * scale, y * scale))
            if coverage < 30:
                continue
            r = 1.6 * scale
            draw.ellipse((x*scale-r, y*scale-r, x*scale+r, y*scale+r), fill=accent + (coverage,))
    image = Image.alpha_composite(image, dots.filter(ImageFilter.GaussianBlur(.48 * scale)))
    draw = ImageDraw.Draw(image)
    draw.text((64 * scale, 42 * scale), "PotPlayer", font=label_font, fill=muted, anchor="lt")
    if zh:
        draw.text((928 * scale, 111 * scale), "影视静帧与片段采集", font=chinese_font, fill=foreground, anchor="lt")
        draw.text((928 * scale, 167 * scale), "收集创作参照，保存喜爱的画面。", font=chinese_body, fill=muted, anchor="lt")
        features = ((64, "无损静帧", "多种格式，清晰保存"),
                    (572, "片段截取", "原码导出，或精确转码"),
                    (1104, "HDR / SDR 色彩", "支持多种色彩空间"))
    else:
        draw.text((1018 * scale, 104 * scale), "Stills and clips.", font=value_font, fill=foreground, anchor="lt")
        draw.text((1018 * scale, 148 * scale), "From films you love.", font=value_font, fill=foreground, anchor="lt")
        draw.text((1018 * scale, 202 * scale), "For reference and personal collections.", font=label_font, fill=muted, anchor="lt")
        features = ((64, "Lossless stills", "Multiple output formats"),
                    (572, "Video clips", "Source copy or precise transcoding"),
                    (1104, "HDR / SDR color", "Multiple color spaces"))
    for x, value, label in features:
        face = chinese_font if zh else value_font
        if draw.textlength(value, font=face) > (width - x - 45) * scale:
            raise ValueError("Banner heading would clip: " + value)
        draw.text((x * scale, 300 * scale), value, font=face, fill=foreground, anchor="lt")
        draw.text((x * scale, 347 * scale), label, font=chinese_label if zh else label_font, fill=muted, anchor="lt")
    prefix = "banner-zh-" if zh else "banner-"
    image.convert("RGB").resize((width, height), Image.Resampling.LANCZOS).save(
        output / (prefix + theme + ".png"), optimize=True
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--font", required=True, type=Path, help="Path to a locally licensed sans-serif font")
    parser.add_argument("--display-font", type=Path, help="Optional semibold/bold face for the wordmark")
    parser.add_argument("--cjk-font", type=Path, help="Local CJK face; also renders Chinese banners")
    args = parser.parse_args()
    for face in (args.font, args.display_font, args.cjk_font):
        if face is not None and not face.is_file():
            parser.error("The selected font does not exist: " + str(face))
    output = Path(__file__).resolve().parents[1] / "assets" / "readme"
    output.mkdir(parents=True, exist_ok=True)
    for language in (("en", "zh") if args.cjk_font else ("en",)):
        for theme in ("light", "dark"):
            render(args.font, args.display_font or args.font, args.cjk_font, output, theme, language)


if __name__ == "__main__":
    main()
