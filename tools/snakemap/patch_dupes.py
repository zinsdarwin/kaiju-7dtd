"""Replace repeated POIs in every town and city of an existing Kaiju Snake world, without
regenerating it: for saves already in progress.

  python tools/snakemap/patch_dupes.py --world <GeneratedWorlds/...> --save <Saves/<world>/<game>> [--out <dir>]

In each settlement the second and later copies of a POI (or of one with the same English name)
are replaced by a POI not yet in that settlement, from its biome's tier band (snakemap.TIER_BAND;
else a tier below), that fits the old lot's width and the block's depth facing the same street;
if none fits, the lot is left empty but for a small sign on the sidewalk. A city whose style allows
repeats (Ashmouth's big tier 5s) is left as it is.
Only the POI's prefabs.xml line changes (same line count and order, so instance ids stay; the
terrain is one flat pad). Traders are left alone. Settlements the save has already generated
only change once their chunks are rebuilt: the chunkreset command for each is printed.
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
PARSE = r'  <decoration type="model" name="([^"]+)" position="(-?\d+),(-?\d+),(-?\d+)" rotation="(\d)" />'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--world", required=True)
    ap.add_argument("--save", required=True)
    ap.add_argument("--out", default=None)
    a = ap.parse_args()

    pois = {p.name: p for p in sm.load_pois(GAME)}
    display = sm.load_display_names(GAME)
    family = lambda n: display.get(n, n)
    kx = open(os.path.join(a.world, "kaiju.xml"), encoding="utf-8").read()
    px = open(os.path.join(a.world, "prefabs.xml"), encoding="utf-8-sig").read()
    regions = {tuple(int(v) for v in r.groups()) for r in
               (re.match(r"r\.(-?\d+)\.(-?\d+)\.7rg", f) for f in os.listdir(os.path.join(a.save, "Region"))) if r}
    lines = re.findall(r'  <decoration type="model" name="[^"]+" position="-?\d+,-?\d+,-?\d+" rotation="\d" />', px)
    total = 0
    resets = []
    for m in re.finditer(r'<settlement name="([^"]+)" kind="(\w+)" role="(\w+)" strip="\d" biome="(\w+)" x="(-?\d+)" y="-?\d+" '
                         r'z="(-?\d+)" halfwidth="(\d+)"(?: halfdepth="(\d+)")?', kx):
        name, kind, role, biome, cx, zc, half = m.group(1), m.group(2), m.group(3), m.group(4), int(m.group(5)), int(m.group(6)), int(m.group(7))
        rows = 2 if kind == "city" else 1
        hd = int(m.group(8)) if m.group(8) else rows * sm.BLOCK_S + 10
        block = (hd - 10) // rows
        x0, x1, z0, z1 = cx - half - 10, cx + half + 10, zc - hd, zc + hd
        style = sm.CITY_STYLE.get(biome, {}) if role == "end" else {}
        if style.get("repeats"):
            continue
        lo, hi = style.get("band", sm.TIER_BAND[biome])
        pool = [p for p in pois.values() if sm.usable(p, biome) and
                (("city" in p.townships) if kind == "city" else ("town" in p.townships or "city" in p.townships))]
        inside = []
        for line in lines:
            n, x, y, z, b = re.match(PARSE, line).groups()
            x, y, z, b = int(x), int(y), int(z), int(b)
            p = pois.get(n)
            if p is None or n.startswith("trader_"):
                continue
            w, d = p.footprint(b)
            if x0 <= x and x + w <= x1 and z0 <= z and z + d <= z1:
                inside.append((line, p, x, y, z, b, w, d))
        fams = set()
        dup_lines = []
        for e in inside:
            f = family(e[1].name)
            if f in fams:
                dup_lines.append(e)
            fams.add(f)
        changed = 0
        for line, old, x, y, z, b, w, d in dup_lines:
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
                if family(p.name) in fams or not (max(1, lo - 1) <= p.tier <= hi):
                    continue
                nb = (p.rot_north + k) & 3
                pw, pd = p.footprint(nb)
                if pw <= w and pd <= room:
                    key = (lo <= p.tier, pw * pd, p.tier)
                    if best is None or key > best[0]:
                        best = (key, p, nb, pd)
            if best is None:
                sz = face + hw + 1 if north else face - hw - 2
                new = '  <decoration type="model" name="%s" position="%d,%d,%d" rotation="0" />' % (FILLER, x, y - old.yoff, sz)
                px = px.replace(line, new, 1)
                print("  %-17s %-26s -> empty lot" % (name, old.name))
                changed += 1
                continue
            _, p, nb, pd = best
            fams.add(family(p.name))
            nz = face + hw + 2 if north else face - hw - 2 - pd
            new = '  <decoration type="model" name="%s" position="%d,%d,%d" rotation="%d" />' % (p.name, x, y - old.yoff + p.yoff, nz, nb)
            px = px.replace(line, new, 1)
            print("  %-17s %-26s -> %-26s (tier %d)" % (name, old.name, p.name, p.tier))
            changed += 1
        total += changed
        if changed and {(rx, rz) for rx in range(x0 // 512, x1 // 512 + 1) for rz in range(z0 // 512, z1 // 512 + 1)} & regions:
            resets.append((name, x0, z0, x1, z1))
    print("%d repeated POIs replaced" % total)
    out = os.path.join(a.out, "world") if a.out else a.world
    os.makedirs(out, exist_ok=True)
    io.open(os.path.join(out, "prefabs.xml"), "w", encoding="utf-8-sig", newline="\n").write(px)
    for name, x0, z0, x1, z1 in resets:
        print("%s is already generated in the save: rebuild it in game with  chunkreset %d %d %d %d" % (name, x0, z0, x1, z1))


if __name__ == "__main__":
    main()
