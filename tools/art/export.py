#!/usr/bin/env python3
"""Exports the picked creature art in tools/art/final/ to the web client (specs/26 section 8).

final/<key>.webp           portrait master (1024 px), key = species id or apex (chase tree) id
final/sprite/<key>.webp    battle sprite master, transparent background
-> web/art/portrait/<key>.webp (512 px) and web/art/sprite/<key>.webp (trimmed to the creature, 192 px)
Run after changing anything in final/. Needs Pillow.
"""
import glob, os
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
FINAL = os.path.join(HERE, "final")
WEB = os.path.join(os.path.dirname(os.path.dirname(HERE)), "web", "art")


def main():
    os.makedirs(os.path.join(WEB, "portrait"), exist_ok=True)
    os.makedirs(os.path.join(WEB, "sprite"), exist_ok=True)
    for f in sorted(glob.glob(os.path.join(FINAL, "*.webp"))):
        key = os.path.basename(f)[:-5]
        Image.open(f).convert("RGB").resize((512, 512), Image.LANCZOS).save(
            os.path.join(WEB, "portrait", key + ".webp"), "WEBP", quality=80, method=6)
    for f in sorted(glob.glob(os.path.join(FINAL, "sprite", "*.webp"))):
        key = os.path.basename(f)[:-5]
        im = Image.open(f).convert("RGBA")
        box = im.getchannel("A").point(lambda a: 255 if a > 24 else 0).getbbox()   # trim faint halo pixels too
        im = im.crop(box)
        side = max(im.size)
        sq = Image.new("RGBA", (side, side), (0, 0, 0, 0))
        sq.paste(im, ((side - im.width) // 2, side - im.height))   # feet on the bottom edge
        sq.resize((192, 192), Image.LANCZOS).save(os.path.join(WEB, "sprite", key + ".webp"), "WEBP", quality=90, method=6)
    print("exported", len(glob.glob(os.path.join(WEB, "*", "*.webp"))), "files to", WEB)


if __name__ == "__main__":
    main()
