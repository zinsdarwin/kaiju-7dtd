"""Swap the POIs in an end city of an existing Kaiju Snake world for higher-tier ones, without
regenerating the world: for saves already in progress.

  python tools/snakemap/patch_city.py --world <GeneratedWorlds/...> --save <Saves/<world>/<game>>
      --city Frostport [--min-tier 4] [--landmarks a,b,...] [--repack] [--generated] [--out <dir>]

Each lot keeps its street, its side and its place along the block: a POI below min-tier is
replaced by the highest-tier one (at least min-tier, else min-tier - 1) whose footprint fits in
the old lot's width and the block's depth, facing the same street. The terrain is not touched (a
city is one flat pad), only the POI's prefabs.xml line (same line, so instance ids stay). Refuses
if the save has already generated any of the city, unless --generated: then the city's chunks must
be rebuilt in game afterwards (the console prints the chunkreset command). --landmarks: POIs to
place first, one each, in the widest lots they fit, whatever their tags. --repack: instead of
lot by lot, repack each whole block face (landmarks first, then the highest tiers), so POIs wider
than the old lots fit; old lines left over become a small sign on the sidewalk (line count and
order stay the same, so instance ids elsewhere stay). Blocks with the trader or a part site are
left alone. Writes in place, or into --out.
"""
import argparse
import io
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import snakemap as sm  # noqa: E402

GAME = r"C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"
FILLER = "sign_speed_25"
LINE = r'  <decoration type="model" name="[^"]+" position="-?\d+,-?\d+,-?\d+" rotation="\d" />'
PARSE = r'  <decoration type="model" name="([^"]+)" position="(-?\d+),(-?\d+),(-?\d+)" rotation="(\d)" />'


def repack(px, pois, pool, landmarks, sites, min_tier, cx, zc, half, block, box):
    """Repacks each block face of the city; returns (old line, new line, old POI, new name) swaps."""
    x0, x1, z0, z1 = box
    entries = []
    for line in re.findall(LINE, px):
        name, x, y, z, b = re.match(PARSE, line).groups()
        x, y, z, b = int(x), int(y), int(z), int(b)
        p = pois.get(name)
        if p is None:
            continue
        w, d = p.footprint(b)
        if x0 <= x and x + w <= x1 and z0 <= z and z + d <= z1:
            entries.append((line, p, x, y, z, b, w, d))
    step = 112
    m = half // step
    xa, xb = cx - half, cx + half
    cross = [xa] + [cx + k * step for k in range(-m, m + 1) if xa + 40 < cx + k * step < xb - 40] + [xb]
    streets = [zc + j * block for j in range(-2, 3)]
    used = {}
    swaps = []
    for j in range(4):
        za, zb = streets[j], streets[j + 1]
        north = j >= 2
        face = za if north else zb
        hw = sm.HIGHWAY_HW if face == zc else sm.STREET_HW
        room_d = block - hw - sm.STREET_HW - 6
        k = 2 if north else 0
        for c in range(len(cross) - 1):
            bx0, bx1 = cross[c] + sm.STREET_HW + 2, cross[c + 1] - sm.STREET_HW - 2
            group = sorted((e for e in entries if bx0 - 2 <= e[2] and e[2] + e[6] <= bx1 + 2 and za < e[4] + e[7] / 2 < zb),
                           key=lambda e: e[2])
            if not group or any(e[1].name.startswith("trader_") or (e[1].name, str(e[2]), str(e[4])) in sites for e in group):
                continue
            pad = group[0][3] - group[0][1].yoff
            x, new = bx0, []
            while True:
                best = None
                for p in pool:
                    if p.tier < min_tier - 1 or (p.name in landmarks and used.get(p.name)):
                        continue
                    nb = (p.rot_north + k) & 3
                    pw, pd = p.footprint(nb)
                    if pw <= bx1 - x and pd <= room_d:
                        key = (p.name in landmarks, p.tier, -used.get(p.name, 0), pw * pd)
                        if best is None or key > best[0]:
                            best = (key, p, nb, pw, pd)
                if best is None or len(new) >= len(group):
                    break
                _, p, nb, pw, pd = best
                used[p.name] = used.get(p.name, 0) + 1
                nz = face + hw + 2 if north else face - hw - 2 - pd
                new.append('  <decoration type="model" name="%s" position="%d,%d,%d" rotation="%d" />' % (p.name, x, pad + p.yoff, nz, nb))
                x += pw + sm.LOT_GAP
            if not new:
                continue
            for i, e in enumerate(group):
                if i < len(new):
                    nl = new[i]
                else:
                    sz = face + hw + 1 if north else face - hw - 2
                    nl = '  <decoration type="model" name="%s" position="%d,%d,%d" rotation="%d" />' % (FILLER, bx0 + 3 * (i - len(new)), pad, sz, 0)
                swaps.append((e[0], nl, e[1], re.match(PARSE, nl).group(1)))
    return swaps


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--world", required=True)
    ap.add_argument("--save", required=True)
    ap.add_argument("--city", required=True)
    ap.add_argument("--min-tier", type=int, default=4)
    ap.add_argument("--landmarks", default="")
    ap.add_argument("--repack", action="store_true", help="repack whole block faces (POIs wider than the old lots)")
    ap.add_argument("--generated", action="store_true", help="the save has the city already; print the chunkreset to run")
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
    if touched and not a.generated:
        raise SystemExit("%s: the save already has region(s) %s there; not patched" % (a.city, sorted(touched)))

    pois = {p.name: p for p in sm.load_pois(GAME)}
    landmarks = [n for n in a.landmarks.split(",") if n in pois]
    pool = [p for p in pois.values() if sm.usable(p, biome) and "city" in p.townships]
    pool += [pois[n] for n in landmarks if pois[n] not in pool]
    sites = set(re.findall(r'<site part="\d" strip="\d" city="[^"]+" name="([^"]+)" tier="\d" x="(-?\d+)" y="-?\d+" z="(-?\d+)"', kx))
    px_path = os.path.join(a.world, "prefabs.xml")
    px = open(px_path, encoding="utf-8-sig").read()
    if a.repack:
        swaps = repack(px, pois, pool, landmarks, sites, a.min_tier, cx, zc, half, block, (x0, x1, z0, z1))
        for line, nl, old, new in swaps:
            px = px.replace(line, nl, 1)
            print("  %-28s tier %d -> %s" % (old.name, old.tier, new))
        missing = [n for n in landmarks if not any(s[3] == n for s in swaps)]
        print("%s: %d lines changed%s" % (a.city, len(swaps), "; landmarks that did not fit: " + ", ".join(missing) if missing else ""))
        return write(a, px, touched, x0, z0, x1, z1)
    used = {}
    swaps = []
    lots = []
    for line in re.findall(r'  <decoration type="model" name="[^"]+" position="-?\d+,-?\d+,-?\d+" rotation="\d" />', px):
        name, x, y, z, b = re.match(r'  <decoration type="model" name="([^"]+)" position="(-?\d+),(-?\d+),(-?\d+)" rotation="(\d)" />', line).groups()
        x, y, z, b = int(x), int(y), int(z), int(b)
        old = pois.get(name)
        if old is None or name.startswith("trader_") or (name, str(x), str(z)) in sites:
            continue
        w, d = old.footprint(b)
        if not (x0 <= x and x + w <= x1 and z0 <= z and z + d <= z1) or old.tier >= a.min_tier:
            continue
        lots.append((line, old, x, y, z, b, w, d))
    # Widest lots first, so the landmarks get the lots they fit.
    lots.sort(key=lambda l: -l[6])
    for line, old, x, y, z, b, w, d in lots:
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
            if p.tier < a.min_tier - 1 or p.tier <= old.tier or (p.name in landmarks and used.get(p.name)):
                continue
            nb = (p.rot_north + k) & 3
            pw, pd = p.footprint(nb)
            if pw <= w and pd <= room:
                key = (p.name in landmarks and not used.get(p.name), p.tier >= a.min_tier, -used.get(p.name, 0), pw * pd, p.tier)
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
    write(a, px, touched, x0, z0, x1, z1)


def write(a, px, touched, x0, z0, x1, z1):
    out = os.path.join(a.out, "world") if a.out else a.world
    os.makedirs(out, exist_ok=True)
    io.open(os.path.join(out, "prefabs.xml"), "w", encoding="utf-8-sig", newline="\n").write(px)
    if touched:
        print("The save has generated %s: in game, with the patched world loaded, rebuild its chunks with the console command:\n"
              "  chunkreset %d %d %d %d" % (a.city, x0, z0, x1, z1))


if __name__ == "__main__":
    main()
