"""Check local Markdown/image targets and the lightweight public-release boundary."""
import re
from html.parser import HTMLParser
from pathlib import Path

from PIL import Image


class BannerParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.images = []

    def handle_starttag(self, tag, attrs):
        if tag == "img":
            self.images.append(dict(attrs))


root = Path(__file__).resolve().parents[1]
errors = []
documents = list(root.glob("*.md")) + list((root / "docs").rglob("*.md"))
for document in documents:
    text = document.read_text(encoding="utf-8-sig")
    targets = re.findall(r"\[[^\]]*\]\(([^)]+)\)", text)
    targets += re.findall(r'(?:src|srcset)="([^"]+)"', text)
    for target in targets:
        if target.startswith(("https://", "http://", "#", "mailto:")):
            continue
        path = target.split("#", 1)[0]
        if not (document.parent / path).is_file():
            errors.append(str(document.relative_to(root)) + ": missing " + target)
for name, prefix in (("README.md", "banner-zh-"), ("README.en.md", "banner-")):
    text = (root / name).read_text(encoding="utf-8-sig")
    picture = re.search(r"<picture>(.*?)</picture>", text, re.S)
    if picture is None:
        errors.append(name + ": missing localized banner")
        continue
    expected = {"assets/readme/" + prefix + theme + ".png" for theme in ("light", "dark")}
    targets = re.findall(r'(?:src|srcset)="([^"]+)"', picture.group(1))
    if set(targets) != expected:
        errors.append(name + ": banner language or theme mismatch")
    banner = BannerParser()
    banner.feed(picture.group(1))
    if len(banner.images) != 1:
        errors.append(name + ": banner must contain one fallback image")
    else:
        attrs = banner.images[0]
        if "height" in attrs or attrs.get("width") != "1600":
            errors.append(name + ": banner must use intrinsic aspect ratio (width=1600, no fixed height)")
for name in ("banner-light", "banner-dark", "banner-zh-light", "banner-zh-dark"):
    path = root / "assets" / "readme" / (name + ".png")
    with Image.open(path) as image:
        if image.size != (1600, 420):
            errors.append("Unexpected banner size: " + str(image.size))
        if path.stat().st_size > 150_000:
            errors.append("Banner exceeds the static artwork budget")
workflow = (root / ".github" / "workflows" / "build.yml").read_text(encoding="utf-8-sig")
if "--draft" not in workflow or "-BundleFFmpeg" in workflow:
    errors.append("Release automation must remain draft-only and lightweight")
if errors:
    raise SystemExit("\n".join(errors))
print("Documentation links, themed artwork and release boundary passed")
