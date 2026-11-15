#!/usr/bin/env python3
"""Makes the shared test pictures and their expected results (tests/pictures). Both picture shrinkers run them: this
plug-in's tests and the Starboard plug-in's tests (SYNC.md). Run it again only when a test picture is added; the
pictures are kept in git so both sides test the very same files.

Needs Pillow. Usage: python3 tools/make-test-pictures.py
"""
import json
import os
import random

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "tests", "pictures")
META = os.path.join(OUT, "metadata")


def photo(w, h, mode="RGB", seed=1):
    """A picture with gradients and a little noise, so it compresses like a real poster, not like a flat colour."""
    rnd = random.Random(seed)
    im = Image.new("RGB", (w, h))
    px = im.load()
    for y in range(h):
        for x in range(w):
            n = rnd.randint(-3, 3)
            px[x, y] = ((x * 255 // w + n) % 256, (y * 255 // h + n) % 256, ((x + y) * 128 // (w + h) + 60 + n) % 256)
    return im.convert(mode)


def save(rel, im, fmt, **kw):
    path = os.path.join(META, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    im.save(path, fmt, **kw)
    return path


def main():
    expected = {}

    def want(rel, outcome, why, kind=None, size=None, fmt=None):
        e = {"outcome": outcome, "why": why}
        if kind:
            e["kind"] = kind
        if size:
            e["width"], e["height"] = size
        if fmt:
            e["format"] = fmt
        expected[rel] = e

    save("library/0a/0a1b/poster.jpg", photo(1000, 1500, seed=1), "JPEG", quality=92)
    want("library/0a/0a1b/poster.jpg", "shrunk", "a poster taller than 900 pixels", "poster", (600, 900), "JPEG")
    save("library/0a/0a1b/backdrop.jpg", photo(960, 540, seed=2), "JPEG", quality=92)
    want("library/0a/0a1b/backdrop.jpg", "ignored", "backdrops are left alone")
    save("library/1c/1c2d/poster.png", photo(700, 1050, seed=3), "PNG")
    want("library/1c/1c2d/poster.png", "shrunk", "a PNG poster stays PNG", "poster", (600, 900), "PNG")
    save("library/2e/2e3f/poster.webp", photo(900, 1350, seed=4), "WEBP", quality=92)
    want("library/2e/2e3f/poster.webp", "shrunk", "a WebP poster stays WebP", "poster", (600, 900), "WEBP")
    save("library/3a/3a4b/poster.jpg", photo(400, 600, seed=5), "JPEG", quality=92)
    want("library/3a/3a4b/poster.jpg", "left", "already no taller than 900 pixels", "poster", (400, 600), "JPEG")
    save("library/7b/7b8c/poster.jpg", photo(1001, 1800, seed=6), "JPEG", quality=92)
    want("library/7b/7b8c/poster.jpg", "shrunk", "1001 by 1800 gives 500.5 pixels wide, rounded half to even", "poster", (500, 900), "JPEG")
    path = os.path.join(META, "library/4c/4c5d/poster.jpg")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(b"this is not a picture")
    want("library/4c/4c5d/poster.jpg", "failed", "it cannot be read as a picture", "poster")
    full = save("library/8d/8d9e/poster.jpg", photo(1000, 1500, seed=7), "JPEG", quality=92)
    with open(full, "rb") as f:
        data = f.read()
    with open(full, "wb") as f:
        f.write(data[: len(data) // 2])
    want("library/8d/8d9e/poster.jpg", "failed", "a truncated picture does not decode in full", "poster")
    save("library/5e/5e6f/poster.gif", photo(600, 1200, seed=8), "GIF")
    want("library/5e/5e6f/poster.gif", "ignored", "GIF is not one of the extensions")
    save("library/6a/6a7b/.poster.jpg", photo(600, 1200, seed=9), "JPEG", quality=92)
    want("library/6a/6a7b/.poster.jpg", "ignored", "hidden files are left alone")
    save("People/A/Alice Example/folder.jpg", photo(800, 1200, seed=10), "JPEG", quality=92)
    want("People/A/Alice Example/folder.jpg", "shrunk", "a photo of a person wider than 400 pixels", "person", (400, 600), "JPEG")
    save("People/B/Bob Example/folder.png", photo(600, 600, seed=11), "PNG")
    want("People/B/Bob Example/folder.png", "shrunk", "a PNG photo stays PNG", "person", (400, 400), "PNG")
    save("People/C/Cara Example/folder.jpg", photo(300, 450, seed=12), "JPEG", quality=92)
    want("People/C/Cara Example/folder.jpg", "left", "already no wider than 400 pixels", "person", (300, 450), "JPEG")
    save("People/D/Dan Example/folder.jpg", photo(1000, 1000, "L", seed=13), "JPEG", quality=92)
    want("People/D/Dan Example/folder.jpg", "shrunk", "a grey photo", "person", (400, 400), "JPEG")
    save("collections/x/poster.jpg", photo(600, 1200, seed=14), "JPEG", quality=92)
    want("collections/x/poster.jpg", "ignored", "only library and People are looked at")

    doc = {
        "about": "The shared test pictures. Outcomes: shrunk (to width by height, same format), left (small enough or not smaller), failed (original kept), ignored (not a picture the shrinker looks at). Sizes assume the default rules in rules.json.",
        "pictures": dict(sorted(expected.items())),
    }
    with open(os.path.join(OUT, "expected.json"), "w") as f:
        json.dump(doc, f, indent=2)
        f.write("\n")


if __name__ == "__main__":
    main()
