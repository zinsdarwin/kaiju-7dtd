"""Move the Oxygen Destroyer part sites of an existing Kaiju Snake world up the mountains north of
their end cities (as snakemap places them) and grade the hillside below them, without
regenerating the world: for saves already in progress. A site already up there (moved by an
earlier run) stays where it is and only gets the hillside regraded.

  python tools/snakemap/patch_posts.py --world <GeneratedWorlds/...> --save <Saves/<world>/<game>>
      [--parts 2,3,4,5] [--out <dir>]

Only the listed parts' sites are moved; pick parts whose areas the save has never generated
(the script refuses parts whose site or trail lies in a region file the save already has).
It edits, in place or into --out:
- prefabs.xml: the part site's decoration line, same POI and line order (instance ids stay),
  new position and rotation;
- dtm.raw: the shelf and the hillside below it, same maths as snakemap.build_part_sites;
- splat3.png: the trail;
- kaiju.xml: the site entries;
- the save's decoration.7dt: trees and rocks on the reshaped ground are dropped (they were
  placed on the old terrain and would float or be buried);
and deletes the world's processed files so the game rebuilds them.
"""
import argparse
import io
import math
import os
import re
import shutil
import struct
import sys

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import snakemap as sm  # noqa: E402

RISE, MIN_DZ, BLEND, MARGIN = 45.0, 260, 25, 6
GAME = r"C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"


def prefab_info(name):
    t = open(os.path.join(GAME, "Data", "Prefabs", "POIs", name + ".xml"), encoding="utf-8-sig", errors="ignore").read()

    def prop(n, d=None):
        m = re.search(r'name="%s" value="([^"]*)"' % n, t)
        return m.group(1) if m else d

    sx, _, sz = [int(v) for v in prop("PrefabSize").split(",")]
    return sx, sz, int(prop("YOffset", "0")), int(prop("RotationToFaceNorth", "0"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--world", required=True)
    ap.add_argument("--save", required=True)
    ap.add_argument("--parts", default="2,3,4,5")
    ap.add_argument("--out", default=None, help="write the edited files here instead of in place")
    a = ap.parse_args()
    parts = [int(p) for p in a.parts.split(",")]
    out_world = os.path.join(a.out, "world") if a.out else a.world
    out_save = os.path.join(a.out, "save") if a.out else a.save
    os.makedirs(out_world, exist_ok=True)
    os.makedirs(out_save, exist_ok=True)

    N, H = sm.N, sm.HALF
    h = np.fromfile(os.path.join(a.world, "dtm.raw"), "<u2").reshape(N, N).astype(np.float32) / 256.0
    kx = open(os.path.join(a.world, "kaiju.xml"), encoding="utf-8").read()
    px = open(os.path.join(a.world, "prefabs.xml"), encoding="utf-8-sig").read()
    regions = {tuple(int(v) for v in m) for m in
               (re.match(r"r\.(-?\d+)\.(-?\d+)\.7rg", f).groups() for f in os.listdir(os.path.join(a.save, "Region"))
                if re.match(r"r\.(-?\d+)\.(-?\d+)\.7rg", f))}
    cities = {m[2]: dict(name=m[0], x=int(m[3]), y=int(m[4]), z=int(m[5]), half=int(m[6]), depth=int(m[7] or 2 * sm.BLOCK_S + 10))
              for m in re.findall(r'<settlement name="([^"]+)" kind="(city)" role="end" strip="(\d)" biome="\w+" '
                                  r'x="(-?\d+)" y="(-?\d+)" z="(-?\d+)" halfwidth="(\d+)"(?: halfdepth="(\d+)")?', kx)}
    cleared = []        # (x0, z0, x1, z1) world rectangles whose trees and rocks go
    trails = []
    for part in parts:
        m = re.search(r'<site part="%d" strip="(\d)" city="([^"]+)" name="([^"]+)" tier="(\d)" x="(-?\d+)" y="(-?\d+)" '
                      r'z="(-?\d+)" w="(\d+)" d="(\d+)" />' % part, kx)
        strip, cname, poi, tier, ox, oy, oz = int(m.group(1)), m.group(2), m.group(3), m.group(4), *map(int, m.group(5, 6, 7))
        city = cities[str(strip)]
        sx, sz, yoff, rotn = prefab_info(poi)
        b = (rotn + 2) & 3
        w, d = (sz, sx) if b % 2 else (sx, sz)
        zc = city["z"]
        z1 = zc + city["depth"]
        pad = float(h[zc + H, city["x"] + H])
        moved = oz >= zc + MIN_DZ
        if moved:
            xmin, zmin = ox, oz
        else:
            xmin = int(round(city["x"] - w / 2))
            zmax = int(min(sm.strip_center(strip) + sm.STRIP_H / 2 - 20, H - 120) - d)
            zmin = int(max(z1 + 60, zc + MIN_DZ))
            while zmin < zmax and float(np.median(h[zmin + H:zmin + H + d, xmin + H:xmin + H + w])) < pad + RISE:
                zmin += 4
        c0 = xmin + H
        cx = xmin + w // 2
        apron_hw = w / 2 + 15 + 60 + 80        # grade_apron's widest reach: bottom half width + blend
        # Refuse anything the save has already generated (its chunks are saved; edits would not show).
        x_lo, x_hi = int(min(xmin - MARGIN - BLEND, cx - apron_hw)), int(max(xmin + w + MARGIN + BLEND, cx + apron_hw))
        z_lo, z_hi = z1, zmin + d + MARGIN + BLEND
        touched = {(rx, rz) for rx in range(x_lo // 512, x_hi // 512 + 1) for rz in range(z_lo // 512, z_hi // 512 + 1)} & regions
        if touched:
            print("part %d: skipped, the save already has region(s) %s there" % (part, sorted(touched)))
            continue
        # Shelf.
        r0 = zmin + H
        gh = sm.snap_ground(float(np.median(h[r0:r0 + d, c0:c0 + w])))
        rr0, rr1, cc0, cc1 = r0 - MARGIN - BLEND, r0 + d + MARGIN + BLEND, c0 - MARGIN - BLEND, c0 + w + MARGIN + BLEND
        yy, xx = np.mgrid[rr0:rr1, cc0:cc1]
        dx = np.maximum(np.maximum(c0 - MARGIN - xx, xx - (c0 + w + MARGIN)), 0)
        dz = np.maximum(np.maximum(r0 - MARGIN - yy, yy - (r0 + d + MARGIN)), 0)
        wgt = 1 - sm.smoothstep(np.hypot(dx, dz) / float(BLEND))
        h[rr0:rr1, cc0:cc1] = h[rr0:rr1, cc0:cc1] * (1 - wgt) + gh * wgt
        cleared.append((cc0 - H, rr0 - H, cc1 - H, rr1 - H))
        # Hillside.
        za, zb = z1, zmin + 2
        ha, hb = float(h[za + H, cx + H]), gh
        cleared.append(sm.grade_apron(h, cx, za, zb, ha, hb, w / 2 + 15))
        trails.append(((cx, z1), (cx, zmin)))
        # prefabs.xml: same line, new place.
        y = sm.prefab_y(gh) + yoff
        old = re.search(r'  <decoration type="model" name="%s" position="%d,-?\d+,%d" rotation="\d" />' % (re.escape(poi), ox, oz), px)
        px = px.replace(old.group(0), '  <decoration type="model" name="%s" position="%d,%d,%d" rotation="%d" />' % (poi, xmin, y, zmin, b))
        kx = kx.replace(m.group(0), '<site part="%d" strip="%d" city="%s" name="%s" tier="%s" x="%d" y="%d" z="%d" w="%d" d="%d" />'
                        % (part, strip, cname, poi, tier, xmin, sm.prefab_y(gh), zmin, w, d))
        print("part %d: %s by %s %s x %d z %d, %.0f m up, hillside %.0f%%"
              % (part, poi, cname, "regraded at" if moved else "moved from z %d to" % oz, xmin, zmin, gh - pad,
                 100 * abs(hb - ha) / max(1, zb - za)))
    if not trails:
        print("nothing to change")
        return

    np.clip(np.round(h * 256), 0, 65535).astype("<u2").tofile(os.path.join(out_world, "dtm.raw"))
    io.open(os.path.join(out_world, "prefabs.xml"), "w", encoding="utf-8-sig", newline="\n").write(px)
    io.open(os.path.join(out_world, "kaiju.xml"), "w", encoding="utf-8", newline="\n").write(kx)
    # Trails on the road splat (row 0 of the file is north; snakemap's save_png flips).
    img = Image.open(os.path.join(a.world, "splat3.png")).convert("RGBA")
    dr = ImageDraw.Draw(img)
    for (xa, za), (xb, zb) in trails:
        dr.line([(xa + H, N - 1 - (za + H)), (xb + H, N - 1 - (zb + H))], fill=(255, 0, 0, 255), width=6)
    img.save(os.path.join(out_world, "splat3.png"))
    if not a.out:
        for f in ("dtm_processed.raw", "splat3_half.png", "splat3_processed.png", "splat4_half.png", "splat4_processed.png", "checksums.txt"):
            if os.path.exists(os.path.join(a.world, f)):
                os.remove(os.path.join(a.world, f))
    # Trees and rocks on the reshaped ground.
    deco = open(os.path.join(a.save, "decoration.7dt"), "rb").read()
    ver, n = deco[0], struct.unpack("<i", deco[1:5])[0]
    keep, dropped = [], 0
    for i in range(n):
        rec = deco[5 + i * 17:5 + (i + 1) * 17]
        v = struct.unpack("<Q", rec[:8])[0]
        x, z = ((v >> 32) & 0xFFFF) - 32768, (v & 0xFFFF) - 32768
        if any(x0 <= x < x1 and z0 <= z < z1 for x0, z0, x1, z1 in cleared):
            dropped += 1
        else:
            keep.append(rec)
    open(os.path.join(out_save, "decoration.7dt"), "wb").write(bytes([ver]) + struct.pack("<i", len(keep)) + b"".join(keep) + deco[5 + n * 17:])
    print("decoration.7dt: %d of %d trees/rocks dropped on the reshaped ground" % (dropped, n))


if __name__ == "__main__":
    main()
