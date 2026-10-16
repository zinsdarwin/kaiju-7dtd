"""Replace the north part of an existing Kaiju Snake world (every strip from one on) with the same
strips of a newer world made from the same seed, without regenerating it: for saves in progress
whose player has not been that far yet.

  python tools/snakemap/patch_strips.py --world <GeneratedWorlds/old> --donor <GeneratedWorlds/new>
      --save <Saves/<old>/<game>> [--from-strip 2] [--out <dir>]

North of the boundary between from-strip - 1 and from-strip (the mountain crest), the terrain
(dtm.raw, blended over Blend metres across the crest), the road and texture maps (splat3/4) and
every POI come from the donor. POI lines: the old world's lines up there are reused in order for
the donor's (instance ids of everything south stay); extra donor POIs are appended; old lines left
over become a small sign out of the way. kaiju.xml: the donor's settlements and part sites from
from-strip on. The save's trees and rocks (decoration.7dt) are dropped where the terrain changed
or a POI stands, old or new. Refuses if the save has generated anything north of the crest.
"""
import argparse
import io
import os
import re
import struct
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import snakemap as sm  # noqa: E402

GAME = r"C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"
LINE = r'  <decoration type="model" name="[^"]+" position="-?\d+,-?\d+,-?\d+" rotation="\d" />'
PARSE = r'  <decoration type="model" name="([^"]+)" position="(-?\d+),(-?\d+),(-?\d+)" rotation="(\d)" />'
FILLER = "sign_speed_25"
BLEND = 25


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--world", required=True)
    ap.add_argument("--donor", required=True)
    ap.add_argument("--save", required=True)
    ap.add_argument("--from-strip", type=int, default=2)
    ap.add_argument("--out", default=None)
    a = ap.parse_args()
    N, H = sm.N, sm.HALF
    zc = int(round(-H + a.from_strip * sm.STRIP_H))  # the crest between the strips
    out_world = os.path.join(a.out, "world") if a.out else a.world
    out_save = os.path.join(a.out, "save") if a.out else a.save
    os.makedirs(out_world, exist_ok=True)
    os.makedirs(out_save, exist_ok=True)

    regions = {tuple(int(v) for v in r.groups()) for r in
               (re.match(r"r\.(-?\d+)\.(-?\d+)\.7rg", f) for f in os.listdir(os.path.join(a.save, "Region"))) if r}
    north = sorted(r for r in regions if (r[1] + 1) * 512 > zc - BLEND)
    if north:
        raise SystemExit("the save has generated region(s) %s north of z %d; not patched" % (north, zc - BLEND))

    # Terrain: donor north of the crest, blended across it.
    old = np.fromfile(os.path.join(a.world, "dtm.raw"), "<u2").reshape(N, N)
    new = np.fromfile(os.path.join(a.donor, "dtm.raw"), "<u2").reshape(N, N)
    h = old.astype(np.float32)
    r0 = zc + H
    h[r0 + BLEND:] = new[r0 + BLEND:]
    t = sm.smoothstep((np.arange(r0 - BLEND, r0 + BLEND) - (r0 - BLEND)) / (2.0 * BLEND))[:, None]
    h[r0 - BLEND:r0 + BLEND] = old[r0 - BLEND:r0 + BLEND] * (1 - t) + new[r0 - BLEND:r0 + BLEND] * t
    changed = np.abs(h - old) > 8  # more than 3 cm
    np.clip(np.round(h), 0, 65535).astype("<u2").tofile(os.path.join(out_world, "dtm.raw"))
    print("terrain: %d cells changed north of z %d" % (int(changed.sum()), zc - BLEND))

    # Road and texture maps (PNG row 0 is north).
    for f in ("splat3.png", "splat4.png"):
        if not os.path.exists(os.path.join(a.donor, f)):
            continue
        o = np.asarray(Image.open(os.path.join(a.world, f)).convert("RGBA")).copy()
        d = np.asarray(Image.open(os.path.join(a.donor, f)).convert("RGBA"))
        row = N - 1 - r0
        o[:row + 1] = d[:row + 1]
        Image.fromarray(o, "RGBA").save(os.path.join(out_world, f))

    # POIs: reuse the old world's northern lines in order for the donor's.
    pois = {p.name: p for p in sm.load_pois(GAME)}
    px = open(os.path.join(a.world, "prefabs.xml"), encoding="utf-8-sig").read()
    dx = open(os.path.join(a.donor, "prefabs.xml"), encoding="utf-8-sig").read()
    north_of = lambda line: int(re.match(PARSE, line).group(4)) >= zc
    old_lines = [l for l in re.findall(LINE, px) if north_of(l)]
    new_lines = [l for l in re.findall(LINE, dx) if north_of(l)]
    boxes = []
    for l in old_lines + new_lines:
        n, x, y, z, b = re.match(PARSE, l).groups()
        p = pois.get(n)
        w, d = p.footprint(int(b)) if p else (40, 40)
        boxes.append((int(x) - 8, int(z) - 8, int(x) + w + 8, int(z) + d + 8))
    for i, l in enumerate(old_lines):
        if i < len(new_lines):
            px = px.replace(l, new_lines[i], 1)
        else:
            # Left over: a sign in the sea off the north-east corner, out of everyone's way.
            px = px.replace(l, '  <decoration type="model" name="%s" position="%d,%d,%d" rotation="0" />'
                            % (FILLER, H - 30 - (i % 20), sm.SEA, H - 30 - (i // 20)), 1)
    extra = new_lines[len(old_lines):]
    if extra:
        px = px.replace("</prefabs>", "".join(l + "\n" for l in extra) + "</prefabs>")
    io.open(os.path.join(out_world, "prefabs.xml"), "w", encoding="utf-8-sig", newline="\n").write(px)
    print("POIs north of the crest: %d old, %d new (%d appended, %d turned into signs)"
          % (len(old_lines), len(new_lines), len(extra), max(0, len(old_lines) - len(new_lines))))

    # kaiju.xml: the donor's settlements and part sites from the strip on.
    kx = open(os.path.join(a.world, "kaiju.xml"), encoding="utf-8").read()
    kd = open(os.path.join(a.donor, "kaiju.xml"), encoding="utf-8").read()
    for tag in ("settlement", "site"):
        pat = r'    <%s [^>]* strip="(\d)"[^>]*/>\n' % tag
        keep = [m.group(0) for m in re.finditer(pat, kx) if int(m.group(1)) < a.from_strip]
        take = [m.group(0) for m in re.finditer(pat, kd) if int(m.group(1)) >= a.from_strip]
        first = re.search(pat, kx)
        allold = [m.group(0) for m in re.finditer(pat, kx)]
        block = "".join(keep + take)
        kx = kx[:first.start()] + block + kx[first.start() + sum(len(s) for s in allold):]
    io.open(os.path.join(out_world, "kaiju.xml"), "w", encoding="utf-8", newline="\n").write(kx)

    # Trees and rocks where the ground changed or a POI stands.
    deco = open(os.path.join(a.save, "decoration.7dt"), "rb").read()
    ver, n = deco[0], struct.unpack("<i", deco[1:5])[0]
    keep, dropped = [], 0
    for i in range(n):
        rec = deco[5 + i * 17:5 + (i + 1) * 17]
        v = struct.unpack("<Q", rec[:8])[0]
        x, z = ((v >> 32) & 0xFFFF) - 32768, (v & 0xFFFF) - 32768
        if z >= zc - BLEND and (changed[z + H, x + H] or any(x0 <= x < x1 and z0 <= z < z1 for x0, z0, x1, z1 in boxes)):
            dropped += 1
        else:
            keep.append(rec)
    open(os.path.join(out_save, "decoration.7dt"), "wb").write(bytes([ver]) + struct.pack("<i", len(keep)) + b"".join(keep) + deco[5 + n * 17:])
    print("decoration.7dt: %d of %d trees/rocks dropped" % (dropped, n))
    if not a.out:
        for f in ("dtm_processed.raw", "splat3_half.png", "splat3_processed.png", "splat4_half.png", "splat4_processed.png", "checksums.txt"):
            if os.path.exists(os.path.join(a.world, f)):
                os.remove(os.path.join(a.world, f))


if __name__ == "__main__":
    main()
