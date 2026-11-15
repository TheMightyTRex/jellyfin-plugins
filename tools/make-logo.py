# Jellyfin catalogue logo for Picture shrinker (16:9, as Jellyfin's catalogue shows it), in the brown of the Starboard
# plug-in's logo.svg. A film poster shrinks inside a dashed outline of its old size, with corner arrows pointing in, a
# cast photo beside it, and the name. Drawn at 3x and scaled down for smooth edges.
from PIL import Image, ImageDraw, ImageFont, ImageFilter
W, H, S = 1280, 720, 3
FONT = "fonts/atkinson-hyperlegible-next.woff2"  # Atkinson Hyperlegible Next, from the TRex design system
img = Image.new("RGB", (W * S, H * S))
d = ImageDraw.Draw(img)
top, bot = (0x9a, 0x55, 0x10), (0x5e, 0x30, 0x05)            # background gradient, brown of logo.svg
for y in range(H * S):
    t = y / (H * S); d.line([(0, y), (W * S, y)], fill=tuple(int(a + (b - a) * t) for a, b in zip(top, bot)))
s = lambda *v: [int(x * S) for x in v]
WHITE, CREAM = (255, 255, 255), (0xff, 0xf3, 0xe0)

def shadow(box, r):
    sh = Image.new("L", img.size, 0); ImageDraw.Draw(sh).rounded_rectangle(s(*box), radius=r * S, fill=120)
    sh = sh.filter(ImageFilter.GaussianBlur(14 * S)); img.paste((0x30, 0x18, 0x00), (0, 0), sh)

# Old size: dashed outline.
ox0, oy0, ox1, oy1 = 120, 110, 470, 610
def dashed(x0, y0, x1, y1, dash=22, gap=14, w=5):
    for (a, b) in [((x0, y0), (x1, y0)), ((x1, y0), (x1, y1)), ((x1, y1), (x0, y1)), ((x0, y1), (x0, y0))]:
        L = abs(b[0] - a[0]) + abs(b[1] - a[1]); n = 0
        while n < L:
            e = min(n + dash, L); f = lambda k: (a[0] + (b[0] - a[0]) * k / L, a[1] + (b[1] - a[1]) * k / L)
            d.line(s(*f(n), *f(e)), fill=CREAM, width=w * S); n += dash + gap
dashed(ox0, oy0, ox1, oy1)

# New size: the poster, a night scene with moon and hills.
px0, py0, px1, py1 = 190, 210, 400, 520
shadow((px0 + 6, py0 + 10, px1 + 6, py1 + 10), 14)
poster = Image.new("RGB", (int((px1 - px0) * S), int((py1 - py0) * S)))
pd = ImageDraw.Draw(poster); ph = poster.size[1]
for y in range(ph):
    t = y / ph; pd.line([(0, y), (poster.size[0], y)], fill=(int(0x1b + 0x50 * t), int(0x2a + 0x30 * t), int(0x5a + 0x30 * t)))
pw = poster.size[0]; u = pw / 210
pd.ellipse([150 * u, 40 * u, 185 * u, 75 * u], fill=(0xff, 0xe8, 0xa8))
for sx, sy in [(30, 30), (70, 60), (110, 25), (40, 95), (125, 85)]:
    pd.ellipse([sx * u, sy * u, (sx + 4) * u, (sy + 4) * u], fill=WHITE)
pd.polygon([(0, 230 * u), (70 * u, 140 * u), (120 * u, 200 * u), (160 * u, 160 * u), (210 * u, 220 * u), (210 * u, 310 * u), (0, 310 * u)], fill=(0x2c, 0x1a, 0x3a))
pd.polygon([(0, 270 * u), (90 * u, 215 * u), (210 * u, 265 * u), (210 * u, 310 * u), (0, 310 * u)], fill=(0x18, 0x10, 0x22))
pd.rectangle([25 * u, 250 * u, 185 * u, 262 * u], fill=(0xff, 0xff, 0xff))       # title bar on the poster
pd.rectangle([55 * u, 272 * u, 155 * u, 279 * u], fill=(0xcc, 0xcc, 0xdd))
mask = Image.new("L", poster.size, 0); ImageDraw.Draw(mask).rounded_rectangle([0, 0, *poster.size], radius=12 * S, fill=255)
img.paste(poster, s(px0, py0), mask)
d.rounded_rectangle(s(px0, py0, px1, py1), radius=12 * S, outline=WHITE, width=5 * S)

# Corner arrows pointing in, from the old outline to the poster.
def arrow(x0, y0, x1, y1):
    d.line(s(x0, y0, x1, y1), fill=WHITE, width=9 * S)
    import math
    a = math.atan2(y1 - y0, x1 - x0); L = 26
    for da in (2.5, -2.5):
        d.line(s(x1, y1, x1 - L * math.cos(a + da / 6 * 1), y1 - L * math.sin(a + da / 6 * 1)), fill=WHITE, width=9 * S)
    for x, y in [(x0, y0), (x1, y1)]:
        d.ellipse(s(x - 4.5, y - 4.5, x + 4.5, y + 4.5), fill=WHITE)
arrow(ox0 + 14, oy0 + 14, px0 - 18, py0 - 18); arrow(ox1 - 14, oy0 + 14, px1 + 18, py0 - 18)
arrow(ox0 + 14, oy1 - 14, px0 - 18, py1 + 18); arrow(ox1 - 14, oy1 - 14, px1 + 18, py1 + 18)

# A cast photo, shrunk too.
cx0, cy0, cx1, cy1 = 415, 300, 505, 418
shadow((cx0 + 4, cy0 + 8, cx1 + 4, cy1 + 8), 10)
d.rounded_rectangle(s(cx0, cy0, cx1, cy1), radius=10 * S, fill=(0x3d, 0x6b, 0x8c), outline=WHITE, width=5 * S)
d.ellipse(s(442, 320, 478, 356), fill=CREAM)
inner = Image.new("L", img.size, 0); ImageDraw.Draw(inner).rounded_rectangle(s(cx0 + 5, cy0 + 5, cx1 - 5, cy1 - 5), radius=6 * S, fill=255)
body = Image.new("L", img.size, 0); ImageDraw.Draw(body).pieslice(s(425, 362, 495, 432), 180, 360, fill=255)
from PIL import ImageChops
img.paste(CREAM, (0, 0), ImageChops.multiply(inner, body))

# Name and a line about it.
f1 = ImageFont.truetype(FONT, 104 * S); f1.set_variation_by_axes([700]) if hasattr(f1, "set_variation_by_axes") else None
f2 = ImageFont.truetype(FONT, 40 * S)
d.text(s(590, 250), "Picture", font=f1, fill=WHITE)
d.text(s(590, 360), "shrinker", font=f1, fill=WHITE)
d.text(s(594, 500), "Smaller posters and photos,", font=f2, fill=CREAM)
d.text(s(594, 550), "the same pictures", font=f2, fill=CREAM)
img.resize((W, H), Image.LANCZOS).save("logo.png", optimize=True)
