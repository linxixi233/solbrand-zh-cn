"""Draw the installer's original compass icon; no external assets required."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
scale = 4
image = Image.new("RGBA", (256 * scale, 256 * scale))
draw = ImageDraw.Draw(image)
def box(values):
    return tuple(round(v * scale) for v in values)
def points(values):
    return [(round(x * scale), round(y * scale)) for x, y in values]
draw.rounded_rectangle(box((4, 4, 252, 252)), radius=52 * scale, fill="#132938")
draw.ellipse(box((40, 40, 216, 216)), outline="#589f9c", width=3 * scale)
draw.ellipse(box((52, 52, 204, 204)), outline="#254c5b", width=2 * scale)
for a, b in [((128, 26), (128, 48)), ((128, 208), (128, 230)),
             ((26, 128), (48, 128)), ((208, 128), (230, 128))]:
    draw.line(points([a, b]), fill="#8bb7b5", width=3 * scale)
draw.polygon(points([(154, 61), (141, 141), (128, 128), (115, 115)]), fill="#6bd7c3")
draw.polygon(points([(102, 195), (115, 115), (128, 128), (141, 141)]), fill="#e0efe9")
image = image.resize((256, 256), Image.Resampling.LANCZOS)
image.save(root / "installer" / "solbrand.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
