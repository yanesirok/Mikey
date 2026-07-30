"""Позонный замер кадра арены против эталона D5 #63.

Спека: docs/superpowers/specs/2026-07-31-arena-vs-d5-measurement-design.md

Запуск из корня проекта:
    Unity.exe -batchmode -quit -projectPath . -executeMethod FightCapture.Shoot \
              -captureOut docs/superpowers/specs/refs/2026-07-31-arena-now.png \
              -captureSize 1920x1080
    python docs/superpowers/specs/refs/measure_vs_d5.py

Зоны заданы в долях кадра, а не в пикселях: у двух кадров разные пропорции, и одна и та же
по смыслу область стоит в разных местах. Зоны арены обходят прямоугольник, в котором стоят
бойцы, — в батч-режиме они в T-позе и занимают треть кадра, а средой не являются.
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
D5 = os.path.join(HERE, '2026-07-31-d5-63.png')
ARENA = os.path.join(HERE, '2026-07-31-arena-now.png')

# имя -> (x0, y0, x1, y1) в долях кадра
D5_ZONES = {
    'глубина':   (0.55, 0.20, 0.68, 0.34),
    'бамбук L':  (0.02, 0.05, 0.14, 0.45),
    'бамбук R':  (0.87, 0.05, 0.99, 0.45),
    'настил':    (0.06, 0.68, 0.28, 0.75),
    'вода':      (0.28, 0.86, 0.72, 0.99),
    'берег':     (0.02, 0.55, 0.16, 0.68),
    'подлесок':  (0.32, 0.50, 0.50, 0.62),
}
ARENA_ZONES = {
    'глубина':   (0.470, 0.20, 0.525, 0.34),
    'бамбук L':  (0.02, 0.05, 0.14, 0.45),
    'бамбук R':  (0.87, 0.05, 0.99, 0.45),
    'настил':    (0.06, 0.80, 0.22, 0.88),
    'вода':      (0.28, 0.94, 0.72, 0.99),
    'берег':     (0.00, 0.42, 0.10, 0.56),
    # Эквивалента нет: в D5 это масса камня и подлеска, закрывающая основания бамбука.
    # В арене бамбук стоит в открытой воде. Строка показывает, что стоит на этом месте.
    'подлесок':  (0.30, 0.55, 0.44, 0.62),
}
NON_EQUIVALENT = {'подлесок'}

# Прямоугольник, в котором стоят бойцы. Исключается из глобальной статистики арены.
FIGHTERS = (0.16, 0.08, 0.85, 0.74)


def load(path):
    """Кадр без чёрных полей. Без обрезки поля тянут нижние перцентили в ноль."""
    a = np.asarray(Image.open(path).convert('RGB')).astype(np.float64)
    lum = a.mean(axis=2)
    rows = np.where(lum.max(axis=1) > 8)[0]
    cols = np.where(lum.max(axis=0) > 8)[0]
    return a[rows.min():rows.max() + 1, cols.min():cols.max() + 1]


def luma(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def saturation(a):
    mx, mn = a.max(axis=2), a.min(axis=2)
    return np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


def patch(a, box):
    h, w, _ = a.shape
    x0, y0, x1, y1 = box
    return a[int(h * y0):int(h * y1), int(w * x0):int(w * x1)]


def mask_out(l, box):
    h, w = l.shape
    x0, y0, x1, y1 = box
    keep = np.ones_like(l, dtype=bool)
    keep[int(h * y0):int(h * y1), int(w * x0):int(w * x1)] = False
    return keep


def overlay(path, zones, out):
    """Оверлей зон. Смотреть глазами до того, как считать из них числа."""
    im = Image.open(path).convert('RGB')
    a = np.asarray(im).astype(np.float64)
    lum = a.mean(axis=2)
    rows = np.where(lum.max(axis=1) > 8)[0]
    cols = np.where(lum.max(axis=0) > 8)[0]
    im = im.crop((cols.min(), rows.min(), cols.max() + 1, rows.max() + 1))
    draw = ImageDraw.Draw(im)
    w, h = im.size
    for name, (x0, y0, x1, y1) in zones.items():
        draw.rectangle([x0 * w, y0 * h, x1 * w, y1 * h], outline=(255, 220, 0), width=3)
        draw.text((x0 * w + 6, y0 * h + 4), name, fill=(255, 220, 0))
    im.thumbnail((900, 900))
    im.save(out, quality=90)


def main():
    for path in (D5, ARENA):
        if not os.path.exists(path):
            sys.exit('нет файла: ' + path)

    d5, ar = load(D5), load(ARENA)
    overlay(D5, D5_ZONES, os.path.join(HERE, '2026-07-31-zones-d5.jpg'))
    overlay(ARENA, ARENA_ZONES, os.path.join(HERE, '2026-07-31-zones-arena.jpg'))

    print('ГЛОБАЛЬНО (у арены бойцы исключены)')
    header = ''.join(f'{n:>7}' for n in ('p1', 'p5', 'p25', 'p50', 'p75', 'p95', 'p99'))
    print(f'{"":<10}{header}   насыщ.')
    for a, label, excl in ((d5, 'D5 #63', None), (ar, 'АРЕНА', FIGHTERS)):
        l, s = luma(a), saturation(a)
        if excl is not None:
            keep = mask_out(l, excl)
            l, s = l[keep], s[keep]
        q = np.percentile(l, [1, 5, 25, 50, 75, 95, 99])
        print(f'{label:<10}' + ''.join(f'{v:7.1f}' for v in q) + f'   {s.mean():.3f}')

    print()
    print(f'{"зона":<11}{"D5 L":>7}{"AR L":>7}{"Δ":>8}   {"D5 s":>6}{"AR s":>7}   {"D5":>8}{"AR":>9}')
    for name in D5_ZONES:
        p1, p2 = patch(d5, D5_ZONES[name]), patch(ar, ARENA_ZONES[name])
        l1, l2 = luma(p1).mean(), luma(p2).mean()
        h1 = '#%02X%02X%02X' % tuple(int(round(c)) for c in p1.reshape(-1, 3).mean(axis=0))
        h2 = '#%02X%02X%02X' % tuple(int(round(c)) for c in p2.reshape(-1, 3).mean(axis=0))
        delta = '     —' if name in NON_EQUIVALENT else f'{l2 - l1:+8.1f}'
        print(f'{name:<11}{l1:7.1f}{l2:7.1f}{delta}   '
              f'{saturation(p1).mean():6.3f}{saturation(p2).mean():7.3f}   {h1:>8}{h2:>9}')

    print()
    print('ПРОФИЛЬ ПО ПОЛОСАМ (сверху вниз, средняя L)')
    print(f'{"":<10}' + ''.join(f'{i:6d}' for i in range(10)))
    for a, label, excl in ((d5, 'D5 #63', None), (ar, 'АРЕНА', FIGHTERS)):
        l = luma(a)
        keep = mask_out(l, excl) if excl is not None else np.ones_like(l, dtype=bool)
        h = l.shape[0]
        cells = []
        for i in range(10):
            lo, hi = int(h * i / 10), int(h * (i + 1) / 10)
            band, bmask = l[lo:hi], keep[lo:hi]
            cells.append(band[bmask].mean() if bmask.any() else float('nan'))
        print(f'{label:<10}' + ''.join(f'{c:6.0f}' for c in cells))


if __name__ == '__main__':
    main()
