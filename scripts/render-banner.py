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
    chinese_font = ImageFont.truetype(str(cjk_path), 28 * scale) if zh else None
    chinese_heading = ImageFont.truetype(str(cjk_path), 37 * scale) if zh else None
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
    def text(x, y, value, face, color, available):
        bounds = draw.textbbox((x * scale, y * scale), value, font=face, anchor="lt")
        if draw.textlength(value, font=face) > available * scale or bounds[3] > (height - 24) * scale:
            raise ValueError("Banner text would clip: " + value)
        draw.text((x * scale, y * scale), value, font=face, fill=color, anchor="lt")

    draw.text((64 * scale, 42 * scale), "PotPlayer", font=label_font, fill=muted, anchor="lt")
    if zh:
        text(928, 110, "把喜欢的镜头，留下来。", chinese_heading, foreground, 608)
        text(928, 174, "收藏电影画面，积累创作参照。", chinese_body, muted, 608)
        features = ((64, "无损静帧与原码片段", "从源文件采集，供收藏与创作"),
                    (572, "按片名自动归档", "识别视频名称，同部作品集中保存"),
                    (1104, "多格式 · HDR / SDR", "图片、编辑编码与多种色彩空间"))
    else:
        text(1018, 104, "Keep the shots", value_font, foreground, 518)
        text(1018, 148, "you love.", value_font, foreground, 518)
        text(1018, 202, "Collect favorites. Gather creative references.", label_font, muted, 518)
        features = ((64, "Lossless stills. Original clips.", "For your collection and creative work"),
                    (572, "Filed by film or series", "Automatic folders from video names"),
                    (1104, "Formats · HDR / SDR", "Image formats, editing codecs, color spaces"))
    for index, (x, value, label) in enumerate(features):
        face = chinese_font if zh else value_font
        available = (features[index + 1][0] - x - 40) if index + 1 < len(features) else width - x - 64
        text(x, 300, value, face, foreground, available)
        text(x, 347, label, chinese_label if zh else label_font, muted, available)
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
