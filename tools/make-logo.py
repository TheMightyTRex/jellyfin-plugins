# Jellyfin catalogue logo for Audio description (16:9, as Jellyfin's catalogue shows it), on the same dark blue as
# Picture shrinker's, so the two look like a set (owner, 17 November 2026: "Each jellyfin plugin needs its own image").
# A film frame with sprocket holes, a sunset scene in it, and a narrator's speech bubble with sound waves coming from
# it, with the name. Drawn at 3x and scaled down for smooth edges. Run from the plug-in folder:
#   python3 tools/make-logo.py [FONT]   (FONT defaults to the design system's Atkinson Hyperlegible Next)
import os, sys
from PIL import Image, ImageDraw, ImageFont, ImageFilter
W, H, S = 1280, 720, 3
FONT = sys.argv[1] if len(sys.argv) > 1 else os.path.expanduser("~/src/design-system/fonts/atkinson-hyperlegible-next.woff2")
img = Image.new("RGB", (W * S, H * S))
d = ImageDraw.Draw(img)
top, bot = (0x16, 0x2c, 0x5c), (0x0a, 0x14, 0x30)            # background gradient, dark blue (as Picture shrinker)
for y in range(H * S):
    t = y / (H * S); d.line([(0, y), (W * S, y)], fill=tuple(int(a + (b - a) * t) for a, b in zip(top, bot)))
s = lambda *v: [int(x * S) for x in v]
WHITE, CREAM, TEAL = (255, 255, 255), (0xd6, 0xe6, 0xff), (0x2a, 0x9d, 0x8f)

def shadow(box, r):
    sh = Image.new("L", img.size, 0); ImageDraw.Draw(sh).rounded_rectangle(s(*box), radius=r * S, fill=120)
    sh = sh.filter(ImageFilter.GaussianBlur(14 * S)); img.paste((0x02, 0x06, 0x14), (0, 0), sh)

# The film frame: a dark strip with sprocket holes, and a sunset scene in it (Picture shrinker's poster colours).
fx0, fy0, fx1, fy1 = 90, 170, 470, 470
shadow((fx0 + 6, fy0 + 10, fx1 + 6, fy1 + 10), 16)
d.rounded_rectangle(s(fx0, fy0, fx1, fy1), radius=16 * S, fill=(0x10, 0x18, 0x2c), outline=WHITE, width=5 * S)
for x in range(fx0 + 26, fx1 - 20, 44):
    for y in (fy0 + 16, fy1 - 36):
        d.rounded_rectangle(s(x, y, x + 22, y + 20), radius=4 * S, fill=CREAM)
sx0, sy0, sx1, sy1 = fx0 + 22, fy0 + 52, fx1 - 22, fy1 - 52
scene = Image.new("RGB", (int((sx1 - sx0) * S), int((sy1 - sy0) * S)))
pd = ImageDraw.Draw(scene); pw, ph = scene.size
for y in range(ph):
    t = y / ph; pd.line([(0, y), (pw, y)], fill=(int(0xf2 - 0x30 * t), int(0x9a - 0x50 * t), int(0x3c + 0x40 * t)))
u = pw / 336
pd.ellipse([230 * u, 30 * u, 280 * u, 80 * u], fill=(0xff, 0xf2, 0xc8))
pd.polygon([(0, 150 * u), (90 * u, 70 * u), (160 * u, 130 * u), (230 * u, 90 * u), (336 * u, 150 * u), (336 * u, 200 * u), (0, 200 * u)], fill=(0x7a, 0x2e, 0x5a))
pd.polygon([(0, 175 * u), (130 * u, 130 * u), (336 * u, 170 * u), (336 * u, 200 * u), (0, 200 * u)], fill=(0x45, 0x18, 0x40))
mask = Image.new("L", scene.size, 0); ImageDraw.Draw(mask).rounded_rectangle([0, 0, *scene.size], radius=8 * S, fill=255)
img.paste(scene, s(sx0, sy0), mask)

# The narrator: a teal speech bubble (Picture shrinker's cast photo colour) with a speaker in it, and sound waves.
bx0, by0, bx1, by1 = 330, 380, 500, 520
shadow((bx0 + 4, by0 + 8, bx1 + 4, by1 + 8), 22)
d.rounded_rectangle(s(bx0, by0, bx1, by1), radius=22 * S, fill=TEAL, outline=WHITE, width=5 * S)
d.polygon(s(360, 515, 352, 572, 405, 515), fill=TEAL)
d.line(s(360, 517, 352, 572, 405, 517), fill=WHITE, width=5 * S)
d.polygon(s(372, 430, 398, 430, 432, 402, 432, 498, 398, 470, 372, 470), fill=WHITE)
for r in (22, 42):
    d.arc(s(432 - r, 450 - r, 432 + r, 450 + r), -50, 50, fill=WHITE, width=7 * S)
for r in (70, 100):
    d.arc(s(500 - r + 20, 450 - r, 500 + r + 20, 450 + r), -40, 40, fill=CREAM, width=7 * S)

# Name and a line about it.
f1 = ImageFont.truetype(FONT, 104 * S); f1.set_variation_by_axes([700]) if hasattr(f1, "set_variation_by_axes") else None
f2 = ImageFont.truetype(FONT, 40 * S)
d.text(s(640, 230), "Audio", font=f1, fill=WHITE)
d.text(s(640, 340), "description", font=f1, fill=WHITE)
d.text(s(644, 480), "Find, tag and play the", font=f2, fill=CREAM)
d.text(s(644, 530), "described sound track", font=f2, fill=CREAM)
img.resize((W, H), Image.LANCZOS).save("images/logo.png", optimize=True)
