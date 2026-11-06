"""Generates icon variants for the net-suite shortcut (PNG previews + multi-size ICO)."""
import math
from PIL import Image, ImageDraw, ImageFont

OUT = r"C:\Users\Максим\AppData\Local\net-suite\icons"
import os
os.makedirs(OUT, exist_ok=True)

S = 1024          # supersampled canvas
SZ = 256          # final size
R = 0.22 * S      # corner radius

def rounded_bg(draw, color):
    draw.rounded_rectangle([0, 0, S - 1, S - 1], radius=R, fill=color)

def shield(draw, cx, cy, w, h, fill, outline=None, ow=0):
    pts = [
        (cx - w / 2, cy - h / 2),
        (cx + w / 2, cy - h / 2),
        (cx + w / 2, cy + h * 0.10),
        (cx, cy + h / 2),
        (cx - w / 2, cy + h * 0.10),
    ]
    draw.polygon(pts, fill=fill, outline=outline, width=ow)

def bolt(draw, cx, cy, w, h, fill):
    pts = [
        (cx - w * 0.10, cy - h / 2),
        (cx + w * 0.55, cy - h / 2),
        (cx + w * 0.10, cy - h * 0.05),
        (cx + w * 0.50, cy - h * 0.05),
        (cx - w * 0.45, cy + h / 2),
        (cx - w * 0.10, cy + h * 0.08),
        (cx - w * 0.48, cy + h * 0.08),
    ]
    draw.polygon(pts, fill=fill)

def font(size):
    for name in ("seguisb.ttf", "segoeuib.ttf", "arialbd.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except Exception:
            continue
    return ImageFont.load_default()

variants = {}

# A: щит + молния (обход блокировок)
img = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
rounded_bg(d, (18, 32, 58, 255))
shield(d, S / 2, S * 0.5, S * 0.62, S * 0.74, (77, 163, 255, 255))
bolt(d, S / 2, S * 0.50, S * 0.30, S * 0.42, (15, 27, 45, 255))
variants["A-shield-bolt"] = img

# B: глобус + две стрелки по орбите
img = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
rounded_bg(d, (10, 26, 40, 255))
c = S / 2; rr = S * 0.22
d.ellipse([c - rr, c - rr, c + rr, c + rr], outline=(52, 211, 153, 255), width=int(S * 0.055))
d.arc([c - rr * 1.55, c - rr * 1.55, c + rr * 1.55, c + rr * 1.55], 200, 340, fill=(52, 211, 153, 255), width=int(S * 0.045))
# arrow head
ax, ay = c + rr * 1.45 * math.cos(math.radians(340)), c + rr * 1.45 * math.sin(math.radians(340))
d.polygon([(ax, ay), (ax - S * 0.055, ay - S * 0.02), (ax - S * 0.02, ay - S * 0.055)], fill=(52, 211, 153, 255))
d.arc([c - rr * 1.55, c - rr * 1.55, c + rr * 1.55, c + rr * 1.55], 20, 160, fill=(110, 231, 183, 255), width=int(S * 0.045))
bx, by = c + rr * 1.45 * math.cos(math.radians(160)), c + rr * 1.45 * math.sin(math.radians(160))
d.polygon([(bx, by), (bx + S * 0.055, by + S * 0.02), (bx + S * 0.02, by + S * 0.055)], fill=(110, 231, 183, 255))
variants["B-globe-arrows"] = img

# C: монограмма NS
img = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
rounded_bg(d, (36, 24, 60, 255))
f = font(int(S * 0.42))
txt = "NS"
bb = d.textbbox((0, 0), txt, font=f)
d.text(((S - (bb[2] - bb[0])) / 2 - bb[0], (S - (bb[3] - bb[1])) / 2 - bb[1] - S * 0.02), txt, font=f, fill=(244, 114, 182, 255))
d.rounded_rectangle([S * 0.20, S * 0.74, S * 0.80, S * 0.78], radius=S * 0.02, fill=(129, 140, 248, 255))
variants["C-monogram"] = img

# D: три узла сети
img = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
rounded_bg(d, (12, 22, 34, 255))
pts = [(S * 0.5, S * 0.27), (S * 0.27, S * 0.70), (S * 0.73, S * 0.70)]
for a, b in ((0, 1), (1, 2), (2, 0)):
    d.line([pts[a], pts[b]], fill=(96, 165, 250, 255), width=int(S * 0.035))
for i, p in enumerate(pts):
    r = S * 0.085 if i == 0 else S * 0.075
    col = (250, 204, 21, 255) if i == 0 else (96, 165, 250, 255)
    d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=col)
variants["D-network"] = img

# E: кнопка питания + дуги wifi
img = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
rounded_bg(d, (20, 30, 25, 255))
c = S * 0.5; rr = S * 0.17
d.arc([c - rr, c - rr, c + rr, c + rr], 300, 240, fill=(74, 222, 128, 255), width=int(S * 0.06))
d.line([(c, S * 0.30), (c, S * 0.47)], fill=(74, 222, 128, 255), width=int(S * 0.06))
for i, k in enumerate((0.60, 0.74)):
    w = S * k
    d.arc([c - w, S * 0.62 - w * 0.7, c + w, S * 0.62 + w * 0.7], 205, 335, fill=(163, 230, 53, 255), width=int(S * 0.03))
variants["E-power-wifi"] = img

# F: круг + молния, контурный (хорошо читается мелко)
img = Image.new("RGBA", (S, S), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
rounded_bg(d, (26, 20, 20, 255))
d.ellipse([S * 0.14, S * 0.14, S * 0.86, S * 0.86], outline=(251, 146, 60, 255), width=int(S * 0.055))
bolt(d, S * 0.5, S * 0.5, S * 0.26, S * 0.40, (251, 191, 36, 255))
variants["F-circle-bolt"] = img

sizes = [16, 24, 32, 48, 64, 128, 256]
for name, im in variants.items():
    small = im.resize((SZ, SZ), Image.LANCZOS)
    small.save(os.path.join(OUT, name + ".png"))
    im.resize((256, 256), Image.LANCZOS).save(
        os.path.join(OUT, name + ".ico"),
        sizes=[(s, s) for s in sizes],
    )

# контактный лист 3x2 с подписями
cells, pad, label_h = 3, 28, 56
cw = 256
sheet_w = pad + cells * (cw + pad)
sheet_h = pad + 2 * (cw + label_h + pad)
sheet = Image.new("RGB", (sheet_w, sheet_h), (24, 24, 27))
sd = ImageDraw.Draw(sheet)
lf = font(26)
for i, (name, im) in enumerate(variants.items()):
    col, row = i % cells, i // cells
    x = pad + col * (cw + pad)
    y = pad + row * (cw + label_h + pad)
    sheet.paste(im.resize((cw, cw), Image.LANCZOS), (x, y), im.resize((cw, cw), Image.LANCZOS))
    sd.text((x, y + cw + 10), name, font=lf, fill=(228, 228, 231))

sheet.save(os.path.join(OUT, "contact-sheet.png"))
print("ok:", len(variants), "variants ->", OUT)
