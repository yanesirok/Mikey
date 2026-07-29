"""
Measures the water bands of a fight capture.

Bands are fractions of frame size, picked against a 1600x900 capture with the camera in its
default framing. Run with --overlay first and look at the file: if a band has drifted onto the
deck, a fighter or the bank, the numbers under it mean nothing.

Everything is reported in linear light, because that is the space the shader's formula works in
and the space a ratio between channels is meaningful in.

Run:
  python tools/water_probe.py issues/water_caustics.png --overlay issues/water_probe.png
"""
import sys
import numpy as np
from PIL import Image, ImageDraw

# name -> (x0, y0, x1, y1) in fractions of width/height
#
# Two bands that looked reasonable and were not: a "shadow" band under the deck landed on the lily
# pads, which are geometry above the water and read as holes (row variance 36 against 2 for water),
# and a "bank" band landed on grass rather than soil. Both are why --overlay exists.
BANDS = {
    "near, in front of the deck (~3 m)": (0.06, 0.90, 0.34, 0.97),
    "mid, left of the pair (~10 m)": (0.05, 0.62, 0.22, 0.67),
    "far, before the fog (~16 m)": (0.40, 0.545, 0.52, 0.575),
    "control, sky (static)": (0.42, 0.10, 0.58, 0.18),
    "bank soil, above the waterline": (0.03, 0.585, 0.09, 0.607),
}


def srgb_to_linear(a):
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)


def main():
    path = sys.argv[1]
    img = Image.open(path).convert("RGB")
    w, h = img.size
    px = srgb_to_linear(np.asarray(img, dtype=np.float64) / 255.0)

    print(f"{path}  {w}x{h}")
    print(f"{'band':32s} {'R':>8s} {'G':>8s} {'B':>8s} {'G/R':>7s} {'lum':>8s} {'rowvar':>8s}")
    for name, (x0, y0, x1, y1) in BANDS.items():
        crop = px[int(y0 * h):int(y1 * h), int(x0 * w):int(x1 * w)]
        mean = crop.reshape(-1, 3).mean(axis=0)
        lum = 0.2126 * mean[0] + 0.7152 * mean[1] + 0.0722 * mean[2]
        # Luminance variance between rows: the flicker measure the ripple work used.
        rows = crop.mean(axis=(1, 2))
        print(f"{name:32s} {mean[0]:8.4f} {mean[1]:8.4f} {mean[2]:8.4f} "
              f"{mean[1] / max(mean[0], 1e-6):7.2f} {lum:8.4f} {rows.var() * 1e4:8.3f}")

    if "--overlay" in sys.argv:
        out = sys.argv[sys.argv.index("--overlay") + 1]
        vis = img.copy()
        draw = ImageDraw.Draw(vis)
        for x0, y0, x1, y1 in BANDS.values():
            draw.rectangle([x0 * w, y0 * h, x1 * w, y1 * h], outline=(255, 0, 0), width=3)
        vis.save(out)
        print(f"overlay -> {out}")


if __name__ == "__main__":
    main()
