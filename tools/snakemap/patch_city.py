"""Swap the POIs in an end city of an existing Kaiju Snake world for higher-tier ones, without
regenerating the world: for saves already in progress.

  python tools/snakemap/patch_city.py --world <GeneratedWorlds/...> --save <Saves/<world>/<game>>
      --city Frostport [--min-tier 4] [--out <dir>]

Each lot keeps its street, its side and its place along the block: a POI below min-tier is
replaced by the highest-tier one (at least min-tier, else min-tier - 1) whose footprint fits in
the old lot's width and the block's depth, facing the same street. The terrain is not touched (a
city is one flat pad), only the POI's prefabs.xml line (same line, so instance ids stay). Refuses
if the save has already generated any of the city. Writes in place, or into --out.
"""
import argparse
import io
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import snakemap as sm  # noqa: E402

GAME = r"C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--world", required=True)
    ap.add_argument("--save", required=True)
    ap.add_argument("--city", required=True)
    ap.add_argument("--min-tier", type=int, default=4)
    ap.add_argument("--out", default=None)
    a = ap.parse_args()

    kx = open(os.path.join(a.world, "kaiju.xml"), encoding="utf-8").read()
    m = re.search(r'<settlement name="%s" kind="city"[^>]* biome="(\w+)" x="(-?\d+)" y="(-?\d+)" z="(-?\d+)" halfwidth="(\d+)"'
                  r'(?: halfdepth="(\d+)")?' % re.escape(a.city), kx)
    biome, cx, zc, half = m.group(1), int(m.group(2)), int(m.group(4)), int(m.group(5))
    block = (int(m.group(6)) - 10) // 2 if m.group(6) else sm.BLOCK_S
    depth_half = 2 * block + 10
    x0, x1, z0, z1 = cx - half - 10, cx + half + 10, zc - depth_half, zc + depth_half
    regions = {tuple(int(v) for v in r.groups()) for r in
               (re.match(r"r\.(-?\d+)\.(-?\d+)\.7rg", f) for f in os.listdir(os.path.join(a.save, "Region"))) if r}
    touched = {(rx, rz) for rx in range(x0 // 512, x1 // 512 + 1) for rz in range(z0 // 512, z1 // 512 + 1)} & regions
    if touched:
        raise SystemExit("%s: the save already has region(s) %s there; not patched" % (a.city, sorted(touched)))

    pois = {p.name: p for p in sm.load_pois(GAME)}
    pool = [p for p in pois.values() if sm.usable(p, biome) and "city" in p.townships]
    sites = set(re.findall(r'<site part="\d" strip="\d" city="[^"]+" name="([^"]+)" tier="\d" x="(-?\d+)" y="-?\d+" z="(-?\d+)"', kx))
    px_path = os.path.join(a.world, "prefabs.xml")
    px = open(px_path, encoding="utf-8-sig").read()
    used = {}
    swaps = []
    for line in re.findall(r'  <decoration type="model" name="[^"]+" position="-?\d+,-?\d+,-?\d+" rotation="\d" />', px):
        name, x, y, z, b = re.match(r'  <decoration type="model" name="([^"]+)" position="(-?\d+),(-?\d+),(-?\d+)" rotation="(\d)" />', line).groups()
        x, y, z, b = int(x), int(y), int(z), int(b)
        old = pois.get(name)
        if old is None or name.startswith("trader_") or (name, str(x), str(z)) in sites:
            continue
        w, d = old.footprint(b)
        if not (x0 <= x and x + w <= x1 and z0 <= z and z + d <= z1) or old.tier >= a.min_tier:
            continue
        # Which street the lot faces (snakemap.build_settlements / place_lot).
        north = z >= zc
        k = (b - old.rot_north) & 3
        if north:
            face = zc if z < zc + block else zc + block
        else:
            face = zc if z + d > zc - block else zc - block
        hw = sm.HIGHWAY_HW if face == zc else sm.STREET_HW
        room = block - hw - sm.STREET_HW - 6
        best = None
        for p in pool:
            if p.tier < a.min_tier - 1 or p.tier <= old.tier:
                continue
            nb = (p.rot_north + k) & 3
            pw, pd = p.footprint(nb)
            if pw <= w and pd <= room:
                key = (p.tier >= a.min_tier, -used.get(p.name, 0), pw * pd, p.tier)
                if best is None or key > best[0]:
                    best = (key, p, nb, pd)
        if best is None:
            continue
        _, p, nb, pd = best
        used[p.name] = used.get(p.name, 0) + 1
        nz = face + hw + 2 if north else face - hw - 2 - pd
        ny = y - old.yoff + p.yoff
        new = '  <decoration type="model" name="%s" position="%d,%d,%d" rotation="%d" />' % (p.name, x, ny, nz, nb)
        swaps.append((line, new, old, p))
    for line, new, old, p in swaps:
        px = px.replace(line, new, 1)
        print("  %-28s tier %d -> %-28s tier %d" % (old.name, old.tier, p.name, p.tier))
    print("%s: %d POIs swapped" % (a.city, len(swaps)))
    if not swaps:
        return
    out = os.path.join(a.out, "world") if a.out else a.world
    os.makedirs(out, exist_ok=True)
    io.open(os.path.join(out, "prefabs.xml"), "w", encoding="utf-8-sig", newline="\n").write(px)


if __name__ == "__main__":
    main()
