#!/usr/bin/env python3
"""
Kaiju snake map generator for 7 Days to Die V3.x (6144 m, single player).

Writes a complete custom world folder (no game run needed):
  - five biome strips, south to north: pine, burnt, desert, snow, wasteland
  - ocean along the west and east edges, mountains along the north and south edges
  - a mountain range between each pair of strips, with one pass at alternating ends
  - one highway snaking through all five strips: west to east in the pine strip, north
    through the pass, east to west, and so on
  - a big city at the coastal far end of every strip (where the pass north begins), a start
    city on the pine strip's west coast, and 3 towns along the road in each strip, built
    from the game's own POIs, each with a trader
  - wilderness POIs scattered between
  - kaiju.xml: Godzilla's route (road waypoints) and the city centres, for the mod

Formats (verified against a V3.2 RWG world and the decompiled game; see README.md):
dtm.raw is uint16 metres*256, row 0 = south (z = -3072). PNGs are the other way up: the game
loads them with Unity, bottom row first, so the bottom row is south (see save_png).
The game rebuilds dtm_processed.raw, splat*_processed.png, splat*_half.png and
checksums.txt on first load, stamping POI footprints into the heightmap and smoothing roads.

Usage:
  python tools/snakemap/snakemap.py [--name "Kaiju Snake"] [--seed 7] [--out DIR] [--preview PNG]
"""
import argparse
import glob
import math
import os
import random
import re
import shutil

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

N = 6144                 # world size, metres = heightmap pixels
HALF = N // 2
SEA = 30                 # sea level (water surface), metres
STRIPS = 5
STRIP_H = N / STRIPS     # 1228.8 m
COAST = 2640             # distance from the centre line to the shoreline (x), metres
CITY_X = 2270            # end/start city centre, |x|
PASS_X = 1975            # pass road through the range, |x| (beside the end city)
RIDGE_H = 120            # mountain range height above the plains
RIDGE_W = 200            # half width of a range
RIM_H = 130              # north/south edge mountains
RIM_W = 260
BASE_H = 52              # plains height

HIGHWAY_HW = 6           # half widths, metres
STREET_HW = 4
BLOCK_S = 78             # street spacing in a settlement (one row of lots per block)
LOT_GAP = 3

# (key, biomes.xml name, map colour, terrain roughness in metres)
BIOMES = [
    ("pine", "pine_forest", (0x00, 0x40, 0x00), 11),
    ("burnt", "burnt_forest", (0xba, 0x00, 0xff), 14),
    ("desert", "desert", (0xff, 0xe4, 0x77), 6),
    ("snow", "snow", (0xff, 0xff, 0xff), 24),
    ("wasteland", "wasteland", (0xff, 0xa8, 0x00), 9),
]

CITY_NAMES = {
    "pine": ["Cedar Harbor", "Port Hemlock"],
    "burnt": ["Cinder Bay"],
    "desert": ["Dune Point"],
    "snow": ["Frostport"],
    "wasteland": ["Ashmouth"],
}
TOWN_NAMES = {
    "pine": ["Fernvale", "Mossbridge", "Elk Hollow"],
    "burnt": ["Charwood", "Emberton", "Soot Creek"],
    "desert": ["Mesa Flats", "Rattler Springs", "Sandy Wells"],
    "snow": ["Iceford", "Whitepine", "Glacier Run"],
    "wasteland": ["Rustgate", "Slag Hill", "Fallout Junction"],
}

EXCLUDE_TAGS = {"hideui", "navonly", "devonly", "part", "streettile", "rwgonly", "tdowntowntile",
                "inter02downtowntile", "gateway"}
EXCLUDE_ZONING = {"navonly", "devonly", "biomeonly"}
EXCLUDE_PREFIX = ("rwg_", "part_", "deco_", "sign_", "street_", "bridge_", "rubble_", "cave_",
                  "trader_", "test", "ai_", "spawn_", "player_", "lot_")


# ---------------------------------------------------------------- helpers

def smoothstep(x):
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3.0 - 2.0 * x)


def fbm(rng, octaves):
    """Fractal value noise in roughly [-1, 1], N x N float32, from upscaled random grids."""
    out = np.zeros((N, N), np.float32)
    total = 0.0
    for cells, amp in octaves:
        g = (rng.random((cells + 1, cells + 1)) * 2 - 1).astype(np.float32)
        out += np.asarray(Image.fromarray(g, mode="F").resize((N, N), Image.BICUBIC)) * amp
        total += amp
    return out / total


def noise1d(rng, length, cells, amp):
    """Smooth 1D noise in [-amp, amp]: random control points, interpolated, then blurred."""
    g = rng.random(cells + 1) * 2 - 1
    xs = np.linspace(0, cells, length)
    v = ndimage.gaussian_filter1d(np.interp(xs, np.arange(cells + 1), g), sigma=length / cells / 2.5, mode="nearest")
    return v / max(1e-6, np.abs(v).max()) * amp


def strip_center(i):
    return -HALF + (i + 0.5) * STRIP_H


def strip_dir(i):
    return 1 if i % 2 == 0 else -1


def to_px(x, z):
    return x + HALF, z + HALF


def save_png(rgba, path):
    """Saves an RGBA map whose row 0 is south. Unity reads PNGs bottom row first, so the
    bottom row of the file must be the south edge: flip before writing."""
    Image.fromarray(np.ascontiguousarray(rgba[::-1]), "RGBA").save(path)


# ---------------------------------------------------------------- prefabs

def prefab_y(terrain):
    """Prefab ground layer for a terrain height. The game's own worlds put it at terrain + ~0.83
    (measured on an RWG world: y - YOffset - dtm height is 0.76..0.86); round(terrain) left
    every POI one block below the roads on a flat pad."""
    return int(round(terrain + 0.83))


def snap_ground(h):
    """Flat ground height for a prefab site, as the game's worlds have it: a whole block + 0.17."""
    return math.floor(h) + 0.17


class Poi:
    def __init__(self, name, sx, sz, yoff, rot_north, townships, tags, zoning, tier=0, quest_broken=False):
        self.name, self.sx, self.sz, self.yoff = name, sx, sz, yoff
        self.rot_north, self.townships, self.tags, self.zoning = rot_north, townships, tags, zoning
        self.tier = tier
        self.quest_broken = quest_broken   # quest tags but no Rally block: quests there never start

    def footprint(self, b):
        return (self.sz, self.sx) if b % 2 else (self.sx, self.sz)


def load_pois(game):
    pois = []
    for f in glob.glob(os.path.join(game, "Data", "Prefabs", "POIs", "*.xml")):
        name = os.path.splitext(os.path.basename(f))[0]
        if not os.path.exists(f[:-4] + ".tts"):
            continue
        t = open(f, encoding="utf-8-sig", errors="ignore").read()

        def prop(n, default=None):
            m = re.search(r'name="%s" value="([^"]*)"' % n, t)
            return m.group(1) if m else default

        size = prop("PrefabSize")
        if not size:
            continue
        sx, _, sz = [int(v) for v in size.split(",")]
        p = Poi(name, sx, sz, int(prop("YOffset", "0")), int(prop("RotationToFaceNorth", "0")),
                {s.strip().lower() for s in prop("AllowedTownships", "").split(",") if s.strip()},
                {s.strip().lower() for s in prop("Tags", "").split(",") if s.strip()},
                {s.strip().lower() for s in prop("Zoning", "").split(",") if s.strip()},
                int(prop("DifficultyTier", "0") or 0),
                'name="QuestTags"' in t and 'class="Rally"' not in t and not name.startswith("trader_"))
        pois.append(p)
    return pois


def load_part(game, name):
    """A prefab from Data/Prefabs/Parts (the game finds prefabs in any folder under Prefabs)."""
    t = open(os.path.join(game, "Data", "Prefabs", "Parts", name + ".xml"), encoding="utf-8-sig", errors="ignore").read()

    def prop(n, default=None):
        m = re.search(r'name="%s" value="([^"]*)"' % n, t)
        return m.group(1) if m else default

    sx, _, sz = [int(v) for v in prop("PrefabSize").split(",")]
    return Poi(name, sx, sz, int(prop("YOffset", "0")), int(prop("RotationToFaceNorth", "0")), set(), set(), set())


def usable(p, biome):
    # The game's world generator places only tagged POIs. Untagged ones are editor/test variants
    # (house_old_cottage_01_sleeper and _detail have no Rally block, so their quests never start).
    # A quest POI without a Rally block (house_old_gambrel_04) gets no quest start marker either.
    if not p.tags or p.quest_broken:
        return False
    if p.tags & EXCLUDE_TAGS or p.zoning & EXCLUDE_ZONING or p.name.startswith(EXCLUDE_PREFIX):
        return False
    if "oldwest" in p.tags and biome != "desert":
        return False
    if "wasteland" in p.tags and biome != "wasteland":
        return False
    return True


# ---------------------------------------------------------------- generator

class SnakeMap:
    def __init__(self, seed, game):
        self.seed = seed
        self.rng = np.random.default_rng(seed)
        self.rand = random.Random(seed)
        self.game = game
        self.pois = load_pois(game)
        self.traders = [p for p in self.pois if p.name.startswith("trader_") and "_signs" not in p.name]
        self.prefabs = []      # (name, x, y, z, rot)
        self.roads = []        # (points[(x,z)], half width, asphalt?)
        self.settlements = []
        self.route = []
        self.used = {}

    # ---- terrain

    def build_terrain(self):
        rng = self.rng
        xs = np.arange(N, dtype=np.float32) - HALF
        zs = np.arange(N, dtype=np.float32) - HALF
        X = xs[None, :]
        Z = zs[:, None]

        # Plains: roughness per strip, blended across strip boundaries.
        hills = fbm(rng, [(5, 1.0), (11, 0.6), (23, 0.35), (47, 0.18), (95, 0.08), (190, 0.04)])
        rough = np.zeros(N, np.float32)
        for k, z in enumerate(zs):
            f = (z + HALF) / STRIP_H - 0.5
            i0 = int(np.clip(math.floor(f), 0, STRIPS - 1))
            i1 = int(np.clip(i0 + 1, 0, STRIPS - 1))
            t = float(smoothstep(np.float32(f - math.floor(f)))) if 0 <= f <= STRIPS - 1 else 0.0
            rough[k] = BIOMES[i0][3] * (1 - t) + BIOMES[i1][3] * t
        h = BASE_H + hills * rough[:, None] * 1.6

        # Mountain ranges between strips, each with a pass beside that strip's end city.
        crag = fbm(rng, [(24, 1.0), (60, 0.6), (150, 0.35), (300, 0.2)])
        ridge = np.zeros((N, N), np.float32)
        for k in range(1, STRIPS):
            zb = -HALF + k * STRIP_H
            bump = (np.cos(np.pi * np.clip(np.abs(Z - zb) / RIDGE_W, 0, 1)) + 1) / 2
            xp = strip_dir(k - 1) * PASS_X
            gap = smoothstep((np.abs(X - xp) - 90) / 220)
            ridge = np.maximum(ridge, RIDGE_H * bump * gap)
        # North and south edge mountains.
        for edge in (-HALF, HALF):
            t = 1 - np.clip(np.abs(Z - edge) / RIM_W, 0, 1)
            ridge = np.maximum(ridge, RIM_H * smoothstep(t) * np.ones_like(X))
        h = h + ridge * (0.8 + 0.35 * crag)
        self.ridge = ridge

        # Ocean on the west and east: shoreline wobbles along z.
        shore = COAST + noise1d(rng, N, 40, 70)[:, None]
        inland = shore - np.abs(X)
        land = h.copy()
        beach = SEA + 1.5 + (land - SEA - 1.5) * smoothstep(inland / 160)
        sea = SEA - 1.0 - np.minimum(22.0, -inland * 0.08)
        h = np.where(inland >= 0, beach, sea).astype(np.float32)
        self.inland = inland.astype(np.float32)
        self.h = h

    def height(self, x, z):
        c, r = int(round(x + HALF)), int(round(z + HALF))
        c = min(max(c, 0), N - 1)
        r = min(max(r, 0), N - 1)
        return float(self.h[r, c])

    # ---- layout: settlements and roads

    def plan(self):
        for i in range(STRIPS):
            key = BIOMES[i][0]
            d = strip_dir(i)
            zc = strip_center(i)
            start_x = -CITY_X if i == 0 else -d * PASS_X
            end_x = d * CITY_X
            if i == 0:
                self.settlements.append(dict(kind="city", role="start", strip=i, biome=key, x=start_x,
                                             name=CITY_NAMES[key][1], half=250, rows=2))
            names = list(TOWN_NAMES[key])
            for n, t in enumerate((0.27, 0.5, 0.73)):
                x0 = start_x + d * (310 if i == 0 else 120)
                x1 = d * PASS_X - d * 80
                x = x0 + (x1 - x0) * t + self.rand.uniform(-60, 60)
                self.settlements.append(dict(kind="town", role="town", strip=i, biome=key, x=x,
                                             name=names[n], half=150, rows=1))
            self.settlements.append(dict(kind="city", role="end", strip=i, biome=key, x=end_x,
                                         name=CITY_NAMES[key][0], half=250, rows=2))

        # Main road per strip: meanders between settlements, straight through them.
        self.strip_roads = []
        for i in range(STRIPS):
            d = strip_dir(i)
            zc = strip_center(i)
            start_x = -CITY_X if i == 0 else -d * PASS_X
            end_x = d * CITY_X
            xs = np.arange(min(start_x, end_x), max(start_x, end_x) + 1, 2.0)
            if d < 0:
                xs = xs[::-1]
            calm = np.zeros_like(xs)
            for s in self.settlements:
                if s["strip"] == i:
                    calm = np.maximum(calm, 1 - smoothstep((np.abs(xs - s["x"]) - s["half"] - 40) / 160))
            for xj in (d * PASS_X, -d * PASS_X):
                calm = np.maximum(calm, 1 - smoothstep((np.abs(xs - xj) - 40) / 200))
            ph1, ph2 = self.rand.uniform(0, 6.28), self.rand.uniform(0, 6.28)
            wob = 75 * np.sin(2 * np.pi * xs / 1650 + ph1) + 25 * np.sin(2 * np.pi * xs / 530 + ph2)
            zz = zc + wob * (1 - calm)
            self.strip_roads.append(list(zip(xs.tolist(), zz.tolist())))

        # Passes: from strip i's junction north to strip i+1's road start.
        self.pass_roads = []
        for i in range(STRIPS - 1):
            xp = strip_dir(i) * PASS_X
            z0, z1 = strip_center(i), strip_center(i + 1)
            zz = np.arange(z0, z1, 2.0)
            t = (zz - z0) / (z1 - z0)
            xx = xp + 45 * np.sin(2 * np.pi * t) * np.sin(np.pi * t) * -strip_dir(i)
            self.pass_roads.append(list(zip(xx.tolist(), zz.tolist())))

        # Godzilla's route: each strip's road up to its pass junction, then the pass; the last
        # strip runs into its end city.
        route = []
        for i in range(STRIPS):
            d = strip_dir(i)
            pts = self.strip_roads[i]
            if i < STRIPS - 1:
                jx = d * PASS_X
                pts = [p for p in pts if (p[0] - jx) * d <= 0]
                route += pts + self.pass_roads[i]
            else:
                route += pts
        self.route = route

    def settlement_rect(self, s):
        zc = self.road_z(s["strip"], s["x"])
        depth = s["rows"] * BLOCK_S + 10
        return s["x"] - s["half"] - 10, s["x"] + s["half"] + 10, zc - depth, zc + depth, zc

    def road_z(self, strip, x):
        pts = self.strip_roads[strip]
        best = min(pts, key=lambda p: abs(p[0] - x))
        return best[1]

    # ---- shaping: settlement pads, road grading

    def shape(self):
        h = self.h
        # Pads: settlements sit on flat ground at the road's height.
        pad_mask = np.zeros((N, N), bool)
        pad_h = np.zeros((N, N), np.float32)
        for s in self.settlements:
            x0, x1, z0, z1, zc = self.settlement_rect(s)
            c0, r0 = to_px(x0, z0)
            c1, r1 = to_px(x1, z1)
            c0, c1 = int(max(c0, 0)), int(min(c1, N - 1))
            r0, r1 = int(max(r0, 0)), int(min(r1, N - 1))
            region = h[r0:r1, c0:c1]
            ph = snap_ground(float(np.clip(np.median(region), SEA + 4, BASE_H + 18)))
            s["pad"] = ph
            s["z"] = zc
            pad_mask[r0:r1, c0:c1] = True
            pad_h[r0:r1, c0:c1] = ph
        dist, idx = ndimage.distance_transform_edt(~pad_mask, return_indices=True)
        w = 1 - smoothstep(dist / 90.0)
        near = pad_h[idx[0], idx[1]]
        self.h = h = (h * (1 - w) + near * w).astype(np.float32)
        del dist, idx, near, w

        # Road profiles: terrain under the centreline, flat through settlements, smoothed and
        # slope-limited, then carved into the terrain with shoulders.
        centre = np.full((N, N), np.nan, np.float32)
        strip_prof = [self.grade(pts) for pts in self.strip_roads]
        pass_prof = []
        for i, pts in enumerate(self.pass_roads):
            # Bend each pass's profile so it meets both strip roads at exactly their heights.
            prof = self.grade(pts)
            jx = strip_dir(i) * PASS_X
            j = min(range(len(self.strip_roads[i])), key=lambda k: abs(self.strip_roads[i][k][0] - jx))
            d0 = strip_prof[i][j] - prof[0]
            d1 = strip_prof[i + 1][0] - prof[-1]
            prof = prof + np.linspace(d0, d1, len(prof)).astype(np.float32)
            pass_prof.append(prof)
        lines = [(pts, prof, HIGHWAY_HW) for pts, prof in zip(self.strip_roads, strip_prof)]
        lines += [(pts, prof, HIGHWAY_HW) for pts, prof in zip(self.pass_roads, pass_prof)]
        for pts, prof, hw in lines:
            for (x, z), ph in zip(pts, prof):
                c, r = to_px(x, z)
                c, r = int(round(c)), int(round(r))
                if 0 <= c < N and 0 <= r < N:
                    centre[r, c] = ph
            self.roads.append((pts, hw, True))
        has = ~np.isnan(centre)
        dist, idx = ndimage.distance_transform_edt(~has, return_indices=True)
        road_h = centre[idx[0], idx[1]]
        w = 1 - smoothstep((dist - HIGHWAY_HW - 3) / 40.0)
        self.h = (self.h * (1 - w) + road_h * w).astype(np.float32)
        self.road_dist = dist.astype(np.float32)
        del idx, road_h, w, centre

    def grade(self, pts):
        """Road height profile: terrain under the centreline, flat through settlements,
        smoothed and limited to a 6% grade (points are 2 m apart)."""
        prof = np.array([self.height(x, z) for x, z in pts], np.float32)
        for s in self.settlements:
            for k, (x, z) in enumerate(pts):
                if abs(x - s["x"]) <= s["half"] + 15 and abs(z - s["z"]) < 40:
                    prof[k] = s["pad"]
        prof = np.maximum(prof, SEA + 3)
        prof = ndimage.uniform_filter1d(prof, 75, mode="nearest")
        for _ in range(2):
            for k in range(1, len(prof)):
                prof[k] = np.clip(prof[k], prof[k - 1] - 0.12, prof[k - 1] + 0.12)
            for k in range(len(prof) - 2, -1, -1):
                prof[k] = np.clip(prof[k], prof[k + 1] - 0.12, prof[k + 1] + 0.12)
        return prof

    # ---- settlements: street grids and POIs

    def build_settlements(self):
        # The first pine town after the start city is where the game really starts (Godzilla
        # destroys the start city; the opening quest goes there): it gets Trader Jen.
        first_pine = [s for s in self.settlements if s["strip"] == 0 and s["kind"] == "town"][0]
        self.first_traders = [t for t in self.traders if t.name == "trader_jen"] or self.traders
        for s in self.settlements:
            x0, x1, z0, z1, zc = self.settlement_rect(s)
            n = s["rows"]
            street_z = [zc + j * BLOCK_S for j in range(-n, n + 1)]
            step = 112 if s["kind"] == "city" else 100
            m = int((s["half"]) // step)
            cross_x = [s["x"] + k * step for k in range(-m, m + 1)]
            xa, xb = s["x"] - s["half"], s["x"] + s["half"]
            cross_x = [xa] + [x for x in cross_x if xa + 40 < x < xb - 40] + [xb]
            for j, z in enumerate(street_z):
                if j != n:  # the main road (j == n) is already the highway
                    self.roads.append(([(xa, z), (xb, z)], STREET_HW, True))
            for x in cross_x:
                self.roads.append(([(x, street_z[0]), (x, street_z[-1])], STREET_HW, True))
            pool = [p for p in self.pois if usable(p, s["biome"]) and
                    (("city" in p.townships) if s["kind"] == "city" else ("town" in p.townships or "city" in p.townships))]
            trader_done = False
            for j in range(len(street_z) - 1):
                za, zb = street_z[j], street_z[j + 1]
                north_of_main = j >= n
                # Lots face the street nearer the main road.
                face_z = za if north_of_main else zb
                hw_face = HIGHWAY_HW if abs(face_z - zc) < 1 else STREET_HW
                depth = BLOCK_S - hw_face - STREET_HW - 6
                for c in range(len(cross_x) - 1):
                    bx0 = cross_x[c] + STREET_HW + 2
                    bx1 = cross_x[c + 1] - STREET_HW - 2
                    x = bx0
                    try_trader = True
                    while True:
                        # The trader goes on the main road from the middle block on, at the first
                        # spot it fits.
                        want_trader = (not trader_done and try_trader and abs(face_z - zc) < 1
                                       and c >= len(cross_x) // 2 - 1)
                        cand = (self.first_traders if s is first_pine else self.traders) if want_trader else pool
                        placed = self.place_lot(cand, s, x, bx1, face_z, hw_face, depth, north_of_main)
                        if placed is None:
                            if want_trader:
                                try_trader = False  # not in this block; the next block tries again
                                continue
                            break
                        if want_trader:
                            trader_done = True
                            s["trader"] = self.prefabs[-1][0]
                        x = placed + LOT_GAP
            if not trader_done:
                print("warning: no trader fits in", s["name"])
        # The start city is the first attack, so the first pine town is where the game really starts.
        first = [s for s in self.settlements if s["strip"] == 0 and s["kind"] == "town"][0]
        if "trader" not in first:
            raise SystemExit("no trader in %s (first pine town); try another --seed" % first["name"])
        print("traders:", ", ".join("%s %s" % (s["name"], s.get("trader", "-")) for s in self.settlements))

    def place_lot(self, pool, s, x, xmax, face_z, hw_face, depth, north_of_main):
        """Places one POI at x along a block edge facing a street. Returns its far x, or None."""
        room = xmax - x
        if room < 12:
            return None
        k = 2 if north_of_main else 0  # quarter turns from facing north
        fits = []
        for p in pool:
            b = (p.rot_north + k) & 3
            w, dpt = p.footprint(b)
            if w <= room and dpt <= depth:
                fits.append((p, b, w, dpt))
        if not fits:
            return None
        # Prefer unused, larger lots; a little randomness.
        fits.sort(key=lambda f: (self.used.get(f[0].name, 0), -f[2] * f[3] + self.rand.uniform(-900, 900)))
        p, b, w, dpt = fits[0] if self.rand.random() < 0.7 else self.rand.choice(fits[:8])
        self.used[p.name] = self.used.get(p.name, 0) + 1
        if north_of_main:
            zmin = face_z + hw_face + 2
        else:
            zmin = face_z - hw_face - 2 - dpt
        y = prefab_y(s["pad"]) + p.yoff
        self.prefabs.append((p.name, int(round(x)), y, int(round(zmin)), b))
        return x + w

    # ---- wilderness

    def build_wilderness(self, per_strip=26):
        pool = {}
        placed = [(s["x"], s["z"], s["half"] + 80) for s in self.settlements]
        for i in range(STRIPS):
            key = BIOMES[i][0]
            pool[i] = [p for p in self.pois if usable(p, key) and "wilderness" in p.townships
                       and max(p.sx, p.sz) <= 70 and "town" not in p.townships]
            count = 0
            tries = 0
            while count < per_strip and tries < 4000:
                tries += 1
                x = self.rand.uniform(-COAST + 250, COAST - 250)
                z = strip_center(i) + self.rand.uniform(-STRIP_H / 2 + RIDGE_W + 30, STRIP_H / 2 - RIDGE_W - 30)
                p = self.rand.choice(pool[i])
                b = self.rand.randrange(4)
                w, dpt = p.footprint(b)
                c, r = int(x + HALF), int(z + HALF)
                if self.road_dist[r, c] < 60 + max(w, dpt) / 2:
                    continue
                if any(math.hypot(x - px, z - pz) < pr + max(w, dpt) / 2 for px, pz, pr in placed):
                    continue
                c0, r0 = int(x - w / 2 + HALF), int(z - dpt / 2 + HALF)
                foot = self.h[r0:r0 + dpt, c0:c0 + w]
                if foot.size == 0 or foot.max() - foot.min() > 7 or foot.min() < SEA + 4 or self.ridge[r, c] > 8:
                    continue
                gh = snap_ground(float(foot.mean()))
                # Flatten the footprint plus a margin, blended out over 20 m.
                m = 10
                rr0, rr1 = max(r0 - m - 20, 0), min(r0 + dpt + m + 20, N)
                cc0, cc1 = max(c0 - m - 20, 0), min(c0 + w + m + 20, N)
                sub = self.h[rr0:rr1, cc0:cc1]
                yy, xx = np.mgrid[rr0:rr1, cc0:cc1]
                dx = np.maximum(np.maximum(c0 - m - xx, xx - (c0 + w + m)), 0)
                dz = np.maximum(np.maximum(r0 - m - yy, yy - (r0 + dpt + m)), 0)
                wgt = 1 - smoothstep(np.hypot(dx, dz) / 20.0)
                self.h[rr0:rr1, cc0:cc1] = sub * (1 - wgt) + gh * wgt
                self.prefabs.append((p.name, c0 - HALF, prefab_y(gh) + p.yoff, r0 - HALF, b))
                placed.append((x, z, max(w, dpt) / 2 + 60))
                count += 1

    # ---- Oxygen Destroyer part sites

    # Tier of each strip's military site: pine, burnt, desert, snow, wasteland (vanilla has no
    # tier 1-2 military POIs, so the first strips get tier 3).
    PART_TIERS = (3, 3, 3, 4, 5)
    MILITARY = re.compile(r"(army_camp|roadside_checkpoint|base_military|bunker)_\d+$")

    SITE_RISE = 45.0      # how far above the city the part site's shelf sits, metres
    SITE_MIN_DZ = 260     # its near edge at least this far north of the city centre (blast kills within 250)

    def build_part_sites(self):
        """A military POI on a shelf up the mountains north of each strip's end city, beside the
        pass into the next strip, facing the city: you hole up there for the blood moon, watch him
        come in from the sea and fire on the city at dawn (out of his path and blast), then take
        the pass north ahead of the radiation. A trail leads up from the city. The mod puts that
        strip's Oxygen Destroyer part crate inside it. Sets self.part_sites."""
        mil = [p for p in self.pois if self.MILITARY.match(p.name)]
        self.part_sites = []
        for i, tier in enumerate(self.PART_TIERS):
            city = next(s for s in self.settlements if s["role"] == "end" and s["strip"] == i)
            pool = [p for p in mil if p.tier == tier] or [p for p in mil if abs(p.tier - tier) <= 1]
            p = self.rand.choice(pool)
            x0, x1, z0, z1, zc = self.settlement_rect(city)
            b = (p.rot_north + 2) & 3                 # entrance to the south, toward the city
            w, dpt = p.footprint(b)
            xmin = int(round(city["x"] - w / 2))
            c0 = xmin + HALF
            # Climb north from the city until the ground under the site is SITE_RISE above it, up to
            # just below the crest (or short of the map's north edge on the last strip).
            zmax = int(min(strip_center(i) + STRIP_H / 2 - 20, HALF - 120) - dpt)
            zmin = int(max(z1 + 60, zc + self.SITE_MIN_DZ))
            target = city["pad"] + self.SITE_RISE
            while zmin < zmax and float(np.median(self.h[zmin + HALF:zmin + HALF + dpt, c0:c0 + w])) < target:
                zmin += 4
            r0 = zmin + HALF
            foot = self.h[r0:r0 + dpt, c0:c0 + w]
            gh = snap_ground(float(np.median(foot)))
            m, bl = 6, 25
            rr0, rr1 = r0 - m - bl, r0 + dpt + m + bl
            cc0, cc1 = c0 - m - bl, c0 + w + m + bl
            yy, xx = np.mgrid[rr0:rr1, cc0:cc1]
            dx = np.maximum(np.maximum(c0 - m - xx, xx - (c0 + w + m)), 0)
            dz = np.maximum(np.maximum(r0 - m - yy, yy - (r0 + dpt + m)), 0)
            wgt = 1 - smoothstep(np.hypot(dx, dz) / float(bl))
            self.h[rr0:rr1, cc0:cc1] = self.h[rr0:rr1, cc0:cc1] * (1 - wgt) + gh * wgt
            self.prefabs.append((p.name, xmin, prefab_y(gh) + p.yoff, zmin, b))
            self.part_sites.append(dict(part=i + 1, strip=i, city=city["name"], name=p.name, tier=p.tier,
                                        x=xmin, z=zmin, w=w, d=dpt, y=prefab_y(gh)))
            # A trail up from the city's north edge to the site's gate, graded into a straight ramp
            # (cut and fill, 8 m wide with 3 m shoulders); left on the raw slope it crossed cliffs
            # where the shelf meets the hillside. About 15% on these ranges.
            cx = xmin + w // 2
            za, zb = int(z1), int(zmin) + 2
            ha, hb = self.height(cx, za), gh
            half, sh = 4, 3
            c0r, c1r = cx - half - sh + HALF, cx + half + sh + HALF + 1
            off = np.abs(np.arange(c0r, c1r) - (cx + HALF))
            wgt_x = 1 - smoothstep((off - half) / float(sh))
            for zz in range(za, zb + 1):
                hz = ha + (hb - ha) * (zz - za) / float(max(1, zb - za))
                row = self.h[zz + HALF, c0r:c1r]
                self.h[zz + HALF, c0r:c1r] = row * (1 - wgt_x) + hz * wgt_x
            grade = abs(hb - ha) / max(1, zb - za)
            if grade > 0.16:
                print("warning: trail to the part %d site is %.0f%%: steeper than a walkable 15%%" % (i + 1, grade * 100))
            self.roads.append(([(cx, z1), (cx, zmin)], 3, True))
            print("part %d site: %s (tier %d) at x %d z %d by %s, %.0f m up, %.0f m from the city centre"
                  % (i + 1, p.name, p.tier, xmin, zmin, city["name"], gh - city["pad"],
                     math.hypot(cx - city["x"], zmin + dpt / 2 - zc)))

    # ---- lookout: where you start

    def make_lookout(self, rise=55.0, radius=22, blend=20):
        """A flat ledge cut into the mountainside south of the start city, rise metres above it,
        with a campsite on it: you were out camping in the mountains and wake up to watch him come
        out of the sea and fire on the city, well outside the blast. Sets self.lookout = (x, z,
        ground height); you start at its north edge, the campsite is behind you."""
        s = next(s for s in self.settlements if s["role"] == "start")
        x = int(s["x"] - 80)
        z = int(s["z"] - 300)
        while z > -HALF + 100 and self.height(x, z) < s["pad"] + rise:
            z -= 2
        gh = snap_ground(self.height(x, z))
        m = radius + blend
        r0, r1 = z + HALF - m, z + HALF + m + 1
        c0, c1 = x + HALF - m, x + HALF + m + 1
        yy, xx = np.mgrid[r0:r1, c0:c1]
        d = np.hypot(xx - (x + HALF), yy - (z + HALF))
        w = 1 - smoothstep((d - radius) / float(blend))
        self.h[r0:r1, c0:c1] = self.h[r0:r1, c0:c1] * (1 - w) + gh * w
        # Campsite (campfire, chairs; one lumberjack zombie asleep) and a tent, uphill of the spawn.
        for name, dx, dz in (("part_campsite_01", -11, -16), ("part_wilderness_filler_02_tent_01", 3, -17)):
            p = load_part(self.game, name)
            b = p.rot_north & 3
            self.prefabs.append((p.name, x + dx, prefab_y(gh) + p.yoff, z + dz, b))
        self.lookout = (x, z + 8, gh)
        print("lookout at x %d z %d, ground %.1f (city %.1f), %.0f m from the city centre"
              % (x, z, gh, s["pad"], math.hypot(x - s["x"], z - s["z"])))

    # ---- output

    def write(self, out, template, name):
        os.makedirs(out, exist_ok=True)
        # The game's processed copies of an earlier version would hide this one; it rebuilds them.
        for f in ("dtm_processed.raw", "splat3_half.png", "splat3_processed.png",
                  "splat4_half.png", "splat4_processed.png", "checksums.txt"):
            if os.path.exists(os.path.join(out, f)):
                os.remove(os.path.join(out, f))
        h = np.clip(np.round(self.h * 256), 0, 65535).astype("<u2")
        h.tofile(os.path.join(out, "dtm.raw"))

        bio = np.zeros((N // 8, N // 8, 4), np.uint8)
        for r in range(N // 8):
            z = r * 8 + 4 - HALF
            i = int(np.clip((z + HALF) // STRIP_H, 0, STRIPS - 1))
            bio[r, :, :3] = BIOMES[i][2]
            bio[r, :, 3] = 255
        save_png(bio, os.path.join(out, "biomes.png"))
        save_png(np.zeros((N // 32, N // 32, 4), np.uint8), os.path.join(out, "radiation.png"))

        roads = Image.new("L", (N, N), 0)
        dr = ImageDraw.Draw(roads)
        for pts, hw, _ in self.roads:
            px = [to_px(x, z) for x, z in pts]
            dr.line(px, fill=255, width=int(hw * 2), joint="curve")
        r = np.asarray(roads)
        s3 = np.zeros((N, N, 4), np.uint8)
        s3[r > 0] = (255, 0, 0, 255)
        save_png(s3, os.path.join(out, "splat3.png"))
        self.road_mask = r > 0

        s4 = np.zeros((N, N, 4), np.uint8)
        s4[:, :, 2] = np.where((self.h < SEA - 0.2) & (self.inland < 0), SEA, 0).astype(np.uint8)
        save_png(s4, os.path.join(out, "splat4.png"))
        self.water = s4[:, :, 2] > 0

        with open(os.path.join(out, "prefabs.xml"), "w", encoding="utf-8-sig", newline="\n") as f:
            f.write('<?xml version="1.0" encoding="UTF-8"?>\n<prefabs>\n')
            for pname, x, y, z, b in self.prefabs:
                f.write('  <decoration type="model" name="%s" position="%d,%d,%d" rotation="%d" />\n' % (pname, x, y, z, b))
            f.write("</prefabs>\n")

        # Spawn: on the lookout above the start city, facing between the city and the sea
        # where he comes ashore (yaw 0 = north, 90 = east).
        start = next(s for s in self.settlements if s["role"] == "start")
        lx, lz, _ = self.lookout
        yaw = int(round(math.degrees(math.atan2(start["x"] - 250 - lx, start["z"] - lz)))) % 360
        with open(os.path.join(out, "spawnpoints.xml"), "w", encoding="utf-8", newline="\n") as f:
            f.write('<?xml version="1.0" encoding="UTF-8"?>\n<spawnpoints>\n')
            for dx, dz in ((0, 0), (4, 0), (-4, 0), (0, -4)):
                x, z = lx + dx, lz + dz
                f.write('  <spawnpoint position="%d,%.2f,%d" rotation="0,%d,0" />\n' % (x, self.height(x, z) + 1, z, yaw))
            f.write("</spawnpoints>\n")

        shutil.copyfile(os.path.join(template, "main.ttw"), os.path.join(out, "main.ttw"))
        tmpl = open(os.path.join(template, "map_info.xml"), encoding="utf-8-sig").read()
        ver = re.search(r'name="GameVersion" value="([^"]*)"', tmpl).group(1)
        with open(os.path.join(out, "map_info.xml"), "w", encoding="utf-8", newline="\n") as f:
            f.write('<?xml version="1.0" encoding="UTF-8"?>\n<MapInfo>\n'
                    '  <property name="SchemaVersion" value="1" />\n'
                    '  <property name="Scale" value="1" />\n'
                    '  <property name="HeightMapSize" value="%d,%d" />\n'
                    '  <property name="Modes" value="Survival,SurvivalSP,SurvivalMP,Creative" />\n'
                    '  <property name="FixedWaterLevel" value="false" />\n'
                    '  <property name="RandomGeneratedWorld" value="true" />\n'
                    '  <property name="GameVersion" value="%s" />\n'
                    '  <property name="Seed" value="%d" />\n'
                    '  <property name="Description" value="Kaiju snake map (tools/snakemap)" />\n'
                    '</MapInfo>\n' % (N, N, ver, self.seed))

        # Data for the mod: Godzilla's route and the cities he attacks.
        with open(os.path.join(out, "kaiju.xml"), "w", encoding="utf-8", newline="\n") as f:
            f.write('<?xml version="1.0" encoding="UTF-8"?>\n')
            f.write('<!-- Generated by tools/snakemap. World coordinates (x, z); y is ground height. -->\n')
            f.write('<kaiju world="%s" seed="%d" size="%d" sealevel="%d">\n' % (name, self.seed, N, SEA))
            f.write('  <settlements>\n')
            for s in self.settlements:
                f.write('    <settlement name="%s" kind="%s" role="%s" strip="%d" biome="%s" x="%d" y="%d" z="%d" halfwidth="%d" />\n'
                        % (s["name"], s["kind"], s["role"], s["strip"], s["biome"], s["x"], round(s["pad"]), round(s["z"]), s["half"]))
            f.write('  </settlements>\n  <partsites>\n')
            for ps in self.part_sites:
                f.write('    <site part="%d" strip="%d" city="%s" name="%s" tier="%d" x="%d" y="%d" z="%d" w="%d" d="%d" />\n'
                        % (ps["part"], ps["strip"], ps["city"], ps["name"], ps["tier"], ps["x"], ps["y"], ps["z"], ps["w"], ps["d"]))
            f.write('  </partsites>\n  <route>\n')
            last = None
            for x, z in self.route:
                if last is None or math.hypot(x - last[0], z - last[1]) >= 25:
                    f.write('    <p x="%d" z="%d" />\n' % (round(x), round(z)))
                    last = (x, z)
            f.write('  </route>\n</kaiju>\n')

    def preview(self, path, size=1536):
        f = N // size
        h = self.h[::f, ::f][:size, :size]
        gy, gx = np.gradient(h)
        shade = np.clip(0.75 + (gx - gy) * 0.08, 0.35, 1.25)
        img = np.zeros((size, size, 3), np.float32)
        for r in range(size):
            i = int(np.clip((r * f) // STRIP_H, 0, STRIPS - 1))
            img[r] = BIOMES[i][2]
        img = img * 0.55 + 90
        img *= shade[:, :, None]
        hn = np.clip((h - BASE_H) / 140, 0, 1)[:, :, None]
        img = img * (1 - hn * 0.5) + 255 * hn * 0.5
        water = self.water[::f, ::f][:size, :size]
        img[water] = (40, 90, 160)
        road = ndimage.maximum_filter(self.road_mask, size=3)[::f, ::f][:size, :size]
        img[road] = (30, 30, 30)
        im = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)[::-1])  # north up
        dr = ImageDraw.Draw(im)
        for pname, x, y, z, b in self.prefabs:
            px, pz = (x + HALF) / f, size - (z + HALF) / f
            dr.rectangle([px, pz - 3, px + 3, pz], fill=(200, 60, 40))
        for s in self.settlements:
            px, pz = (s["x"] + HALF) / f, size - (s["z"] + HALF) / f
            dr.text((px - 30, pz - 30), s["name"], fill=(0, 0, 0))
        pts = [((x + HALF) / f, size - (z + HALF) / f) for x, z in self.route]
        dr.line(pts, fill=(255, 40, 40), width=1)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        im.save(path)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--name", default="Kaiju Snake")
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--game", default=r"C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die")
    ap.add_argument("--template", default=os.path.expandvars(r"%APPDATA%\7DaysToDie\GeneratedWorlds\Luguja Valley"),
                    help="any V3.x RWG world; only main.ttw and the GameVersion are taken from it (read-only)")
    ap.add_argument("--out", default=None, help="world folder (default: GeneratedWorlds/<name>)")
    ap.add_argument("--preview", default=os.path.join(here, "out", "preview.png"))
    a = ap.parse_args()
    out = a.out or os.path.join(os.path.expandvars(r"%APPDATA%\7DaysToDie\GeneratedWorlds"), a.name)
    if os.path.abspath(out) == os.path.abspath(a.template):
        raise SystemExit("refusing to overwrite the template world")

    m = SnakeMap(a.seed, a.game)
    print("POIs:", len(m.pois), "traders:", [t.name for t in m.traders])
    m.build_terrain()
    print("terrain done")
    m.plan()
    m.shape()
    print("roads and pads done")
    m.build_settlements()
    m.build_part_sites()
    m.build_wilderness()
    m.make_lookout()
    print("prefabs:", len(m.prefabs))
    m.write(out, a.template, a.name)
    m.preview(a.preview)
    print("world:", out)
    print("preview:", a.preview)


if __name__ == "__main__":
    main()
