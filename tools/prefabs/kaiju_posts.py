"""Writes the five Kaiju army posts, one per biome strip, as 7 Days to Die prefabs.

Each post sits on the mountain shelf north of a strip's end city (snakemap places them): a fenced
yard with one gate toward the city, open ground for traps, and a tower about 20 m high with a
single ladder route to a top platform that looks south over the city. The towers are plain
blocks on purpose, so they are easy to turn into a blood moon base: cut the ladder, plate the
shaft, and fight from the top.

  python tools/prefabs/kaiju_posts.py [--game <7 Days To Die install>] [--out KaijuMod/Prefabs]
      [--render tools/prefabs/out]

Writes <name>.tts, <name>.blocks.nim and <name>.xml for each post into --out (the game loads
prefabs from a mod's Prefabs folder) and, with --render, a preview picture of each.

Prefab format (V3.3, read from Prefab.cs): "tts\\0", uint32 version 19, int16 size x/y/z, then
one uint32 block value per cell (x + y*sx + z*sx*sy; type in the low 16 bits, rotation in bits
16-20) mapped through the .blocks.nim names, a density byte per cell, two damage bytes per cell,
a bit stream of textured cells (none here), a bit stream of water cells (none), then the tile
entity and trigger counts (none). The .blocks.nim is int32 1, int32 count, then (int32 id,
.NET string name) pairs. The xml holds size, rotation, tier, sleeper volumes and the indexed
blocks (Rally, FetchContainer) the quests use.

Front (gate, view) is -z in prefab space; RotationToFaceNorth is 2 like the game's army camps,
so snakemap's rotation (rot_north + 2) & 3 = 0 leaves the front facing south, toward the city.
"""
import argparse
import glob
import math
import os
import re
import struct

GAME = r"C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"
VERSION = 19
DENSITY_AIR, DENSITY_TERRAIN = 127, 128   # sbyte 127 and -128

# Ladder rotation by the side its wall is on (measured on the game's prefabs).
LADDER = {"-z": 0, "-x": 1, "+z": 2, "+x": 3}
ALONG_X, ALONG_Z = 0, 1                     # sandbags, railings, barriers

INDEXED = {"questRallyMarker": "Rally", "cntQuestRandomLootHelper": "FetchContainer"}


class Prefab:
    def __init__(self, name, sx, sy, sz, tier, title):
        self.name, self.sx, self.sy, self.sz = name, sx, sy, sz
        self.tier, self.title = tier, title
        self.cells = {}            # (x, y, z) -> (block, rotation)
        self.volumes = []          # (start, size, group, count, loot)

    # ---- drawing

    def set(self, x, y, z, block, rot=0):
        if 0 <= x < self.sx and 0 <= y < self.sy and 0 <= z < self.sz:
            if block in (None, "air"):
                self.cells.pop((x, y, z), None)
            else:
                self.cells[(x, y, z)] = (block, rot)

    def get(self, x, y, z):
        c = self.cells.get((x, y, z))
        return c[0] if c else "air"

    def fill(self, x0, y0, z0, x1, y1, z1, block, rot=0):
        for x in range(min(x0, x1), max(x0, x1) + 1):
            for y in range(min(y0, y1), max(y0, y1) + 1):
                for z in range(min(z0, z1), max(z0, z1) + 1):
                    self.set(x, y, z, block, rot)

    def clear(self, x0, y0, z0, x1, y1, z1):
        self.fill(x0, y0, z0, x1, y1, z1, None)

    def walls(self, x0, z0, x1, z1, y0, y1, block):
        """The four walls of a box (corners included)."""
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.set(x, y, z0, block)
                self.set(x, y, z1, block)
            for z in range(z0, z1 + 1):
                self.set(x0, y, z, block)
                self.set(x1, y, z, block)

    def ring(self, x0, z0, x1, z1, y, block, rot_x=ALONG_X, rot_z=ALONG_Z, gaps=()):
        """A one-block-high line round a rectangle (sandbags, railings, fences); gaps: (x, z) cells left open."""
        for x in range(x0, x1 + 1):
            for z in (z0, z1):
                if (x, z) not in gaps:
                    self.set(x, y, z, block, rot_x)
        for z in range(z0 + 1, z1):
            for x in (x0, x1):
                if (x, z) not in gaps:
                    self.set(x, y, z, block, rot_z)

    def ladder(self, x, z, y0, y1, wall):
        for y in range(y0, y1 + 1):
            self.set(x, y, z, "ladderMetal", LADDER[wall])

    def sleepers(self, start, size, group, count, loot=True, spots=()):
        """A sleeper volume (prefab coordinates, inclusive start, size) and the sleeper blocks in it."""
        self.volumes.append((start, size, group, count, loot))
        for x, y, z, block, rot in spots:
            self.set(x, y, z, block, rot)

    # ---- output

    def write(self, out):
        os.makedirs(out, exist_ok=True)
        names = ["air"] + sorted({b for b, _ in self.cells.values()})
        ids = {n: i for i, n in enumerate(names)}
        sx, sy, sz = self.sx, self.sy, self.sz
        n = sx * sy * sz
        blocks = [0] * n
        dens = bytearray([DENSITY_AIR]) * n
        for (x, y, z), (b, rot) in self.cells.items():
            i = x + y * sx + z * sx * sy
            blocks[i] = ids[b] | (rot & 31) << 16
            if b.startswith("terr"):
                dens[i] = DENSITY_TERRAIN
        nbits = (n + 7) // 8
        with open(os.path.join(out, self.name + ".tts"), "wb") as f:
            f.write(b"tts\0" + struct.pack("<I3h", VERSION, sx, sy, sz))
            f.write(struct.pack("<%dI" % n, *blocks))
            f.write(bytes(dens))
            f.write(bytes(2 * n))                                   # damage
            f.write(struct.pack("<i", nbits) + bytes(nbits))         # textured cells: none
            f.write(struct.pack("<i", nbits) + bytes(nbits))         # water cells: none
            f.write(struct.pack("<hh", 0, 0))                       # tile entities, triggers
        with open(os.path.join(out, self.name + ".blocks.nim"), "wb") as f:
            f.write(struct.pack("<ii", 1, len(names)))
            for i, nm in enumerate(names):
                b = nm.encode("utf-8")
                f.write(struct.pack("<i", i) + net_len(len(b)) + b)
        with open(os.path.join(out, self.name + ".xml"), "w", encoding="utf-8", newline="\r\n") as f:
            f.write(self.xml())

    def xml(self):
        p = []

        def prop(k, v):
            p.append('  <property name="%s" value="%s" />' % (k, v))

        vols = self.volumes
        prop("CopyAirBlocks", "True")
        prop("ExcludeDistantPOIMesh", "True")
        prop("ExcludePOICulling", "True")
        prop("DistantPOIYOffset", "0")
        prop("DifficultyTier", self.tier)
        prop("ShowQuestClearCount", "1")
        prop("TraderArea", "False")
        prop("PrefabSize", "%d, %d, %d" % (self.sx, self.sy, self.sz))
        prop("RotationToFaceNorth", "2")
        prop("Zoning", "none")
        prop("YOffset", "-1")
        prop("SleeperVolumeSize", "#".join("%d, %d, %d" % v[1] for v in vols))
        prop("SleeperVolumeStart", "#".join("%d, %d, %d" % v[0] for v in vols))
        prop("SleeperVolumeGroupId", ",".join("0" for _ in vols))
        prop("SleeperVolumeGroup", ",".join("%s,%d,%d" % (v[2], v[3], v[3]) for v in vols))
        prop("SleeperIsLootVolume", ",".join(str(v[4]) for v in vols))
        prop("SleeperIsQuestExclude", ",".join("False" for _ in vols))
        prop("SleeperVolumeFlags", ",".join("0" for _ in vols))
        prop("SleeperVolumeTriggeredBy", "#".join(" " for _ in vols))
        prop("Tags", "kaiju_post")
        prop("ThemeTags", "siteMilitary")
        prop("QuestTags", "clear, fetch")
        idx = {}
        for (x, y, z), (b, _) in sorted(self.cells.items(), key=lambda c: (c[0][1], c[0][2], c[0][0])):
            if b in INDEXED:
                idx.setdefault(INDEXED[b], []).append((x, y, z))
        p.append('  <property class="IndexedBlockOffsets">')
        for k in sorted(idx):
            p.append('    <property class="%s">' % k)
            for i, (x, y, z) in enumerate(idx[k]):
                p.append('      <property name="%d" value="%d, %d, %d" />' % (i, x, y, z))
            p.append('    </property>')
        p.append('  </property>')
        return '<?xml version="1.0" encoding="UTF-8"?>\n<prefab>\n' + "\n".join(p) + "\n</prefab>\n"


def net_len(n):
    """.NET BinaryWriter string length prefix (7-bit encoded)."""
    out = bytearray()
    while True:
        b = n & 0x7F
        n >>= 7
        out.append(b | (0x80 if n else 0))
        if not n:
            return bytes(out)


# ---- shared pieces

W = D = 40                 # footprint, metres
CX = 20                    # tower centre x
GATE = range(18, 22)       # gate cells along the south fence


def base(p, ground, fence, fence2=None, wire=True):
    """Ground layer, perimeter with one gate in the south side, wire in front of it, Rally inside the gate."""
    p.fill(0, 0, 0, W - 1, 0, D - 1, ground)
    gaps = {(x, 1) for x in GATE}
    p.ring(1, 1, W - 2, D - 2, 1, fence, gaps=gaps)
    if fence2:
        p.ring(1, 1, W - 2, D - 2, 2, fence2, gaps=gaps)
    if wire:
        # Wire belts either side of the approach, leaving the road in the open.
        for x in list(range(4, 15)) + list(range(25, 36)):
            if x % 2 == 0:
                p.set(x, 1, 5, "barbedWireSpindle")
        for x in list(range(6, 15, 3)) + list(range(26, 36, 3)):
            p.set(x, 1, 9, "barbedWireSpindle1m")
    p.set(CX, 1, 4, "questRallyMarker")
    p.set(CX - 3, 1, 3, "flagPoleAmerican")


def crates(p, spots):
    for x, y, z, b in spots:
        p.set(x, y, z, b)


def yard_sleepers(p, group, count, spots):
    p.sleepers((3, 1, 3), (W - 6, 3, 12), group, count, True,
               [(x, 1, z, b, r) for x, z, b, r in spots])


# ---- 1 pine: Ridge Lookout, a fire lookout taken over by the army

def pine():
    p = Prefab("kaiju_post_pine", W, 31, D, 1, "Ridge Lookout")
    base(p, "terrGravel", "sandbagStraight01")
    x0, z0, x1, z1 = CX - 4, 20, CX + 4, 28           # cabin deck 9x9
    top = 18                                           # deck floor
    # Four steel legs and ring beams every 6 m.
    for x, z in ((x0 + 1, z0 + 1), (x1 - 1, z0 + 1), (x0 + 1, z1 - 1), (x1 - 1, z1 - 1)):
        p.fill(x, 1, z, x, top - 1, z, "steelShapes:pillar50")
    for y in (6, 12):
        p.ring(x0 + 1, z0 + 1, x1 - 1, z1 - 1, y, "woodShapes:cube")
    # Shaft: a 3x3 plank column, doorway south, ladder on its north wall.
    sx, sz = CX, (z0 + z1) // 2
    p.walls(sx - 1, sz - 1, sx + 1, sz + 1, 1, top - 1, "woodShapes:cube")
    p.clear(sx, 1, sz - 1, sx, 2, sz - 1)              # doorway
    p.ladder(sx, sz, 1, top, "+z")
    # Deck, cabin with a window band, stepped roof, flag.
    p.fill(x0, top, z0, x1, top, z1, "woodShapes:cube")
    p.set(sx, top, sz, "ladderMetal", LADDER["+z"])
    p.ring(x0, z0, x1, z1, top + 1, "woodShapes:railing")
    p.walls(x0 + 1, z0 + 1, x1 - 1, z1 - 1, top + 1, top + 3, "woodShapes:cube")
    for x in range(x0 + 2, x1 - 1):
        p.clear(x, top + 2, z0 + 1, x, top + 2, z0 + 1)
        p.clear(x, top + 2, z1 - 1, x, top + 2, z1 - 1)
    for z in range(z0 + 2, z1 - 1):
        p.clear(x0 + 1, top + 2, z, x0 + 1, top + 2, z)
        p.clear(x1 - 1, top + 2, z, x1 - 1, top + 2, z)
    p.clear(CX, top + 1, z0 + 1, CX, top + 2, z0 + 1)  # cabin door onto the south deck
    for i in range(5):
        p.fill(x0 + i, top + 4 + i, z0 + i, x1 - i, top + 4 + i, z1 - i, "woodShapes:cube")
    p.set(CX, top + 9, (z0 + z1) // 2, "spotlightUpStraight_POI")
    crates(p, [(x0 + 2, top + 1, z1 - 2, "cntWoodenChestMilitaryRandomLootHelper"),
               (x1 - 2, top + 1, z1 - 2, "radioHam"),
               (x1 - 2, top + 1, z0 + 2, "cntAmmoPileMedium"),
               (x0 + 2, top + 1, z0 + 2, "cntQuestRandomLootHelper")])
    # Yard: supply tent corner, crates, a sandbag nest by the gate.
    p.ring(4, 30, 10, 36, 1, "sandbagStraight01", gaps={(7, 30)})
    crates(p, [(5, 1, 35, "cntFootlockerArmyGreenRandomLootHelper"), (9, 1, 35, "cntAmmoPileSmall"),
               (30, 1, 34, "cntWoodenChestMilitaryRandomLootHelper"), (34, 1, 34, "cntBarrelGasSingle00")])
    yard_sleepers(p, "GroupGenericZombie", 3, [(12, 8, "sleeperIdle", 0), (27, 10, "sleeperIdle", 2), (8, 12, "sleeperStomach", 1)])
    p.sleepers((x0 + 1, top + 1, z0 + 1), (7, 3, 7), "GroupZomSoldier", 1, True,
               [(x0 + 3, top + 1, z1 - 3, "sleeperSit", 2)])
    theme_pine(p)
    return p


# ---- 2 burnt: Cinder Relay, a scorched concrete bunker under a radio mast

def burnt():
    p = Prefab("kaiju_post_burnt", W, 33, D, 2, "Cinder Relay")
    base(p, "terrGravel", "barrierConcrete")
    bx0, bz0, bx1, bz1 = CX - 6, 21, CX + 6, 29        # bunker 13x9, 4 high
    p.fill(bx0, 1, bz0, bx1, 5, bz1, "concreteShapes:cube")
    p.clear(bx0 + 1, 1, bz0 + 1, bx1 - 1, 4, bz1 - 1)
    p.clear(CX, 1, bz0, CX, 2, bz0)                    # door, south
    for x in (bx0 + 2, bx1 - 2):                       # firing slits
        p.clear(x, 3, bz0, x, 3, bz0)
    p.set(bx0 + 1, 4, bz0 + 1, "lightIndustrialRed")
    # Mast: steel core with a ladder on its south face, from the bunker floor up.
    mx, mz = CX, bz1 - 2
    nest = 21
    p.fill(mx, 1, mz, mx, 30, mz, "steelShapes:cube")
    p.ladder(mx, mz - 1, 1, nest, "+z")
    p.clear(mx, 5, mz - 1, mx, 5, mz - 1)
    p.set(mx, 5, mz - 1, "ladderMetal", LADDER["+z"])
    for dx, dz in ((-2, -2), (2, -2), (-2, 2), (2, 2)):
        p.fill(mx + dx, 6, mz + dz, mx + dx, nest - 1, mz + dz, "steelShapes:pillar50")
    for y in range(9, nest, 4):
        p.ring(mx - 2, mz - 2, mx + 2, mz + 2, y, "corrugatedMetalShapes:cube",
               gaps={(mx, mz - 2)})
    # Crow's nest facing south, sandbag parapet.
    p.fill(mx - 3, nest, mz - 4, mx + 3, nest, mz + 2, "steelShapes:cube")
    p.set(mx, nest, mz - 1, "ladderMetal", LADDER["+z"])
    p.ring(mx - 3, mz - 4, mx + 3, mz + 2, nest + 1, "sandbagStraight01")
    p.fill(mx, 31, mz, mx, 32, mz, "steelShapes:pole")
    p.set(mx, 30, mz - 1, "lightIndustrialRed")
    crates(p, [(bx0 + 1, 1, bz1 - 1, "cntWoodenChestMilitaryRandomLootHelper"),
               (bx1 - 1, 1, bz1 - 1, "cntGunRackSmallRandomLootHelper"),
               (bx1 - 1, 1, bz0 + 1, "cntAmmoPileMedium"),
               (bx0 + 2, 1, bz0 + 2, "cntQuestRandomLootHelper"),
               (bx0 + 3, 1, bz1 - 1, "radioHam"),
               (mx - 2, nest + 1, mz + 1, "cntAmmoPileSmall")])
    # Burnt-out yard: oil drums and a wrecked wall.
    for x, z in ((6, 30), (7, 30), (6, 31), (33, 28), (34, 28)):
        p.set(x, 1, z, "cntBarrelOilSingle00")
    p.fill(26, 1, 14, 31, 2, 14, "concreteShapes:cube")
    p.clear(28, 2, 14, 31, 2, 14)
    yard_sleepers(p, "GroupGenericZombie", 4, [(10, 9, "sleeperIdle", 0), (30, 11, "sleeperBack", 1),
                                               (16, 13, "sleeperStomach", 2)])
    p.sleepers((bx0 + 1, 1, bz0 + 1), (11, 3, 7), "GroupZomSoldier", 2, True,
               [(bx0 + 4, 1, bz0 + 3, "sleeperSit", 2), (bx1 - 3, 1, bz0 + 4, "sleeperIdle", 0)])
    theme_burnt(p)
    return p


# ---- 3 desert: Sandstone Watch, a walled brick watchtower

def desert():
    p = Prefab("kaiju_post_desert", W, 28, D, 3, "Sandstone Watch")
    base(p, "terrSand", "sandbagStraight01", "sandbagStraight01")
    tx0, tz0, tx1, tz1 = CX - 3, 21, CX + 3, 27        # 7x7 tower
    top = 20
    p.walls(tx0, tz0, tx1, tz1, 1, top - 1, "brickShapes:cube")
    p.clear(CX, 1, tz0, CX, 2, tz0)                    # door
    lx, lz = tx1 - 1, tz1 - 1                          # ladder in the north-east corner
    for y in (5, 10, 15):
        p.fill(tx0 + 1, y, tz0 + 1, tx1 - 1, y, tz1 - 1, "brickShapes:cube")
        p.set(lx, y, lz, None)
    p.ladder(lx, lz, 1, top, "+z")
    for y in (3, 8, 13, 18):                           # slits on every face
        p.clear(CX, y, tz0, CX, y, tz0)
        p.clear(CX, y, tz1, CX, y, tz1)
        p.clear(tx0, y, (tz0 + tz1) // 2, tx0, y, (tz0 + tz1) // 2)
        p.clear(tx1, y, (tz0 + tz1) // 2, tx1, y, (tz0 + tz1) // 2)
    # Top: overhanging platform, crenellated parapet, sun roof on poles.
    p.fill(tx0 - 1, top, tz0 - 1, tx1 + 1, top, tz1 + 1, "brickShapes:cube")
    p.set(lx, top, lz, "ladderMetal", LADDER["+z"])
    for x in range(tx0 - 1, tx1 + 2):
        for z in (tz0 - 1, tz1 + 1):
            p.set(x, top + 1, z, "brickShapes:cube" if x % 2 else "sandbagStraight01", ALONG_X)
    for z in range(tz0, tz1 + 1):
        for x in (tx0 - 1, tx1 + 1):
            p.set(x, top + 1, z, "brickShapes:cube" if z % 2 else "sandbagStraight01", ALONG_Z)
    for x, z in ((tx0, tz0), (tx1, tz0), (tx0, tz1), (tx1, tz1)):
        p.fill(x, top + 1, z, x, top + 3, z, "woodShapes:pole")
    p.fill(tx0, top + 4, tz0, tx1, top + 4, tz1, "corrugatedMetalShapes:cube")
    crates(p, [(tx0 + 1, 1, tz1 - 1, "cntWoodenChestMilitaryRandomLootHelper"),
               (tx0 + 1, 1, tz0 + 1, "cntQuestRandomLootHelper"),
               (tx0 + 1, 11, tz1 - 1, "cntAmmoPileMedium"),
               (tx0 + 1, top + 1, tz1 - 1, "cntGunSafeRandomLootHelperArmyGreen"),
               (tx1 - 1, top + 1, tz0 + 1, "cntWeaponsBagSmall")])
    # Yard: a well of sandbags, water barrels, a tent pad.
    p.ring(5, 28, 11, 34, 1, "sandbagStraight01", gaps={(8, 28)})
    for x, z in ((30, 31), (31, 31), (30, 32)):
        p.set(x, 1, z, "cntBarrelPlasticSingle00")
    crates(p, [(6, 1, 33, "cntFootlockerArmyGreenRandomLootHelper"), (10, 1, 33, "cntMilitaryGoodsC")])
    yard_sleepers(p, "GroupGenericZombie", 4, [(11, 10, "sleeperIdle", 0), (29, 8, "sleeperIdle", 3),
                                               (24, 13, "sleeperSideLeft", 1)])
    p.sleepers((tx0 + 1, 1, tz0 + 1), (5, 3, 5), "GroupZomSoldier", 2, True,
               [(tx0 + 2, 1, tz0 + 2, "sleeperSit", 2), (tx0 + 2, 6, tz0 + 2, "sleeperIdle", 0)])
    p.sleepers((tx0, top + 1, tz0), (7, 3, 7), "GroupZomSoldier", 1, True,
               [(CX, top + 1, tz0 + 1, "sleeperStomach", 0)])
    theme_desert(p)
    return p


# ---- 4 snow: Frost Array, a radar tower

def snow():
    p = Prefab("kaiju_post_snow", W, 32, D, 4, "Frost Array")
    base(p, "terrGravel", "barrierMilitary", wire=True)
    cx, cz = CX, 25
    top = 20
    # 5x5 concrete core, ladder inside on its north wall; steel outrigger legs.
    p.walls(cx - 2, cz - 2, cx + 2, cz + 2, 1, top - 1, "concreteShapes:cube")
    p.clear(cx, 1, cz - 2, cx, 2, cz - 2)
    for y in (7, 14):
        p.fill(cx - 1, y, cz - 1, cx + 1, y, cz + 1, "concreteShapes:cube")
        p.set(cx, y, cz + 1, None)
    p.ladder(cx, cz + 1, 1, top, "+z")
    for dx, dz in ((-5, -5), (5, -5), (-5, 5), (5, 5)):
        p.fill(cx + dx, 1, cz + dz, cx + dx, top - 1, cz + dz, "steelShapes:pillar50")
    for y in (7, 14):
        p.ring(cx - 5, cz - 5, cx + 5, cz + 5, y, "steelShapes:cube")
    # Deck 11x11 with railings; radar dome on the north half, open view south.
    p.fill(cx - 5, top, cz - 5, cx + 5, top, cz + 5, "steelShapes:cube")
    p.set(cx, top, cz + 1, "ladderMetal", LADDER["+z"])
    p.ring(cx - 5, cz - 5, cx + 5, cz + 5, top + 1, "corrugatedMetalShapes:railing")
    dc, r = (cx, top + 1, cz + 3), 3.6
    for x in range(cx - 4, cx + 5):
        for y in range(top + 1, top + 6):
            for z in range(cz, cz + 6):
                d = math.sqrt((x - dc[0]) ** 2 + ((y - dc[1]) * 1.15) ** 2 + (z - dc[2]) ** 2)
                if r - 1.0 <= d <= r and z >= cz + 1:
                    p.set(x, y, z, "concreteShapes:cube")
    p.fill(cx, top + 6, cz + 3, cx, top + 8, cz + 3, "steelShapes:pole")
    p.set(cx, top + 9, cz + 3, "lightIndustrialRed")
    p.set(cx - 4, top + 1, cz - 4, "spotlightDownStraight_POI")
    p.set(cx + 4, top + 1, cz - 4, "spotlightDownStraight_POI")
    crates(p, [(cx - 1, 1, cz - 1, "cntQuestRandomLootHelper"), (cx + 1, 1, cz - 1, "cntAmmoPileMedium"),
               (cx - 1, 8, cz - 1, "cntWoodenChestMilitaryRandomLootHelper"),
               (cx - 4, top + 1, cz - 2, "cntGunSafeRandomLootHelperArmyGreen"),
               (cx + 4, top + 1, cz - 2, "cntMunitionsBoxArmy"),
               (cx + 4, top + 1, cz, "radioHam")])
    # Yard: a corrugated hut (supply store).
    hx0, hz0, hx1, hz1 = 4, 26, 12, 34
    p.walls(hx0, hz0, hx1, hz1, 1, 3, "corrugatedMetalShapes:cube")
    p.fill(hx0, 4, hz0, hx1, 4, hz1, "corrugatedMetalShapes:cube")
    p.clear(8, 1, hz0, 8, 2, hz0)
    crates(p, [(hx0 + 1, 1, hz1 - 1, "cntMilitaryMetalShelvesRandomLootHelper"),
               (hx1 - 1, 1, hz1 - 1, "cntFootlockerArmyGreenRandomLootHelper")])
    yard_sleepers(p, "GroupZomSoldier", 4, [(10, 9, "sleeperIdle", 0), (30, 9, "sleeperIdle", 0),
                                            (20, 12, "sleeperStomach", 2)])
    p.sleepers((hx0 + 1, 1, hz0 + 1), (7, 3, 7), "GroupZomSoldier", 2, True,
               [(hx0 + 3, 1, hz0 + 3, "sleeperSit", 2), (hx0 + 5, 1, hz0 + 4, "sleeperBack", 1)])
    p.sleepers((cx - 5, top + 1, cz - 5), (11, 3, 5), "GroupZomSoldier", 1, True,
               [(cx + 2, top + 1, cz - 3, "sleeperIdle", 0)])
    theme_snow(p)
    return p


# ---- 5 wasteland: Last Word, a hardened command tower

def wasteland():
    p = Prefab("kaiju_post_wasteland", W, 30, D, 5, "Last Word Command")
    base(p, "terrGravel", "barrierConcrete", "barbedWireSpindle1m")
    cx, cz = CX, 25
    top = 22
    # Two-storey blockhouse 11x9, steel-faced.
    bx0, bz0, bx1, bz1 = cx - 5, cz - 4, cx + 5, cz + 4
    p.fill(bx0, 1, bz0, bx1, 9, bz1, "concreteShapes:cube")
    p.clear(bx0 + 1, 1, bz0 + 1, bx1 - 1, 4, bz1 - 1)
    p.clear(bx0 + 1, 6, bz0 + 1, bx1 - 1, 8, bz1 - 1)
    p.walls(bx0, bz0, bx1, bz0, 1, 9, "steelShapes:cube")      # steel front
    p.clear(cx, 1, bz0, cx, 2, bz0)                            # one door
    for x in (bx0 + 2, bx1 - 2):
        p.clear(x, 3, bz0, x, 3, bz0)
        p.clear(x, 7, bz0, x, 7, bz0)
    # Tower 7x7 above, ladder shaft in its north-west corner from the ground up.
    tx0, tz0, tx1, tz1 = cx - 3, cz - 3, cx + 3, cz + 3
    p.walls(tx0, tz0, tx1, tz1, 10, top - 1, "concreteShapes:cube")
    p.fill(tx0 + 1, 15, tz0 + 1, tx1 - 1, 15, tz1 - 1, "concreteShapes:cube")
    lx, lz = tx0 + 1, tz1 - 1
    for y in (5, 9, 15):
        p.set(lx, y, lz, None)
    p.ladder(lx, lz, 1, top, "+z")
    for y in (12, 18):
        p.clear(cx, y, tz0, cx, y, tz0)
        p.clear(tx0, y, cz, tx0, y, cz)
        p.clear(tx1, y, cz, tx1, y, cz)
    # Command deck 11x11 with a 2-high steel parapet and slits, roofed at the back.
    p.fill(cx - 5, top, cz - 5, cx + 5, top, cz + 5, "steelShapes:cube")
    p.set(lx, top, lz, "ladderMetal", LADDER["+z"])
    p.walls(cx - 5, cz - 5, cx + 5, cz + 5, top + 1, top + 2, "steelShapes:cube")
    for x in range(cx - 4, cx + 5, 2):
        p.clear(x, top + 2, cz - 5, x, top + 2, cz - 5)
    for z in range(cz - 3, cz + 5, 2):
        p.clear(cx - 5, top + 2, z, cx - 5, top + 2, z)
        p.clear(cx + 5, top + 2, z, cx + 5, top + 2, z)
    for x, z in ((cx - 5, cz + 1), (cx + 5, cz + 1), (cx - 5, cz + 5), (cx + 5, cz + 5)):
        p.fill(x, top + 3, z, x, top + 4, z, "steelShapes:pillar50")
    p.fill(cx - 5, top + 5, cz + 1, cx + 5, top + 5, cz + 5, "steelShapes:cube")
    for x in (cx - 4, cx + 4):
        p.set(x, top + 3, cz - 4, "spotlightDownStraight_POI")
    crates(p, [(bx0 + 1, 1, bz1 - 1, "cntGunSafeRandomLootHelperArmyGreen"),
               (bx1 - 1, 1, bz1 - 1, "cntWoodenChestMilitaryRandomLootHelper"),
               (bx1 - 1, 1, bz0 + 1, "cntQuestRandomLootHelper"),
               (bx0 + 3, 1, bz1 - 1, "cntAmmoPileLarge"),
               (bx1 - 1, 6, bz1 - 1, "cntMilitaryMetalShelvesRandomLootHelper"),
               (bx0 + 3, 6, bz1 - 1, "radioHam"),
               (cx + 3, top + 1, cz + 3, "cntHardenedChestSecure"),
               (cx - 3, top + 1, cz + 3, "cntMunitionsBoxArmy")])
    # Yard: inner wire belt, tank traps of concrete, a spike field each side of the road.
    for x in range(4, 37, 2):
        if x not in GATE:
            p.set(x, 1, 13, "barbedWireSpindle")
    for x, z in ((8, 17), (12, 19), (24, 30), (6, 31), (34, 33)):
        p.fill(x, 1, z, x, 2, z, "concreteShapes:cube")
    for x in list(range(10, 16)) + list(range(25, 31)):
        p.set(x, 1, 10, "ironSpikesTrapBloodPOI")
    yard_sleepers(p, "GroupZomSoldier", 5, [(8, 7, "sleeperIdle", 0), (32, 7, "sleeperIdle", 0),
                                            (20, 11, "sleeperStomach", 2), (14, 13, "sleeperBack", 1)])
    p.sleepers((bx0 + 1, 1, bz0 + 1), (9, 3, 7), "GroupZomSoldier", 3, True,
               [(cx - 2, 1, cz, "sleeperSit", 2), (cx + 2, 1, cz + 1, "sleeperIdle", 0),
                (cx, 1, cz + 2, "sleeperSideRight", 3)])
    p.sleepers((bx0 + 1, 6, bz0 + 1), (9, 3, 7), "GroupZomSoldier", 2, True,
               [(cx - 2, 6, cz, "sleeperIdle", 0), (cx + 2, 6, cz - 1, "sleeperStomach", 1)])
    p.sleepers((cx - 4, top + 1, cz - 4), (9, 3, 9), "GroupZomSoldier", 2, True,
               [(cx, top + 1, cz - 2, "sleeperIdle", 0), (cx + 2, top + 1, cz + 2, "sleeperSit", 2)])
    theme_wasteland(p)
    return p


# ---- set pieces from each strip's weapon quest

def tent(p, x0, z0, x1, z1, wall, roof, open_side="-z"):
    """A walled shelter two high with a roof, open on one long side."""
    p.walls(x0, z0, x1, z1, 1, 2, wall)
    p.fill(x0, 3, z0, x1, 3, z1, roof)
    if open_side == "-z":
        p.clear(x0 + 1, 1, z0, x1 - 1, 2, z0)
    else:
        p.clear(x0, 1, z0 + 1, x0, 2, z1 - 1)


def pen(p, x0, z0, x1, z1, inside):
    """A barbed wire pen with things inside it."""
    p.ring(x0, z0, x1, z1, 1, "barbedWireSpindle1m")
    for x, z, b in inside:
        p.set(x, 1, z, b)


def theme_pine(p):
    """Disaster bureau field post: a triage tent, and a wired-off pen of irradiated drums (G-cell samples)."""
    tent(p, 27, 17, 35, 22, "corrugatedMetalShapes:cube", "corrugatedMetalShapes:cube", "-x")
    crates(p, [(34, 1, 18, "cntMedicLootPileA"), (34, 1, 21, "cntMedicLootPileB"),
               (32, 1, 21, "cntPillCaseRandomLootHelper"), (30, 1, 21, "cntMedicineCabinetClosedRandomLootHelper")])
    pen(p, 27, 26, 33, 31, [(29, 28, "cntBarrelRadiatedSingle00"), (31, 29, "cntBarrelRadiatedSingle00"),
                            (30, 27, "cntBarrelRadiatedSingle45")])
    p.set(13, 1, 18, "cntCollapsedCampfire")
    # Searchlights on the south deck, watching the sea.
    for x in (CX - 3, CX + 3):
        p.set(x, 19, 20, "spotlightUpStraight_POI")


def theme_burnt(p):
    """Missile battery site: a launcher trailer aimed west at the sea, missile racks, munitions;
    the bunker roof is a sandbagged pad for the launch control."""
    # Trailer chassis and a launcher box stepped up toward the west.
    p.fill(27, 1, 18, 35, 1, 20, "steelShapes:cube")
    for i, x in enumerate(range(27, 35)):
        y = 2 + (34 - x) // 3
        p.fill(x, y, 18, x, y + 2, 20, "corrugatedMetalShapes:cube")
        p.fill(x, 2, 19, x, y - 1, 19, "steelShapes:pillar50")
    for x in range(28, 36, 2):
        p.set(x, 1, 17, "barrierConcrete")
    # Missile racks and munitions.
    for z in (24, 26):
        p.fill(29, 1, z, 34, 1, z, "metalPipeStackLoose")
    crates(p, [(35, 1, 24, "cntMunitionsBoxArmy"), (35, 1, 26, "cntAmmoPileLarge"),
               (28, 1, 28, "cntWeaponsBagLarge"), (30, 1, 28, "cntMunitionsBoxArmy")])
    # Launch control pad on the bunker roof.
    p.ring(CX - 6, 21, CX + 6, 29, 6, "sandbagStraight01", gaps={(CX - 1, 29), (CX, 29), (CX + 1, 29)})


def theme_desert(p):
    """Airstrike radio relay: an antenna farm, a big X signal panel on the sand for the jets,
    a radio room in the tower, fuel drums."""
    for x, z in ((4, 34), (10, 36), (35, 35)):
        p.fill(x, 1, z, x, 12, z, "steelShapes:pole")
        p.set(x, 13, z, "lightIndustrialRed")
    for i in range(9):
        for x, z in ((25 + i, 15 + i), (33 - i, 15 + i)):
            p.set(x, 0, z, "terrConcrete")
            p.set(x + 1, 0, z, "terrConcrete")
    crates(p, [(CX + 2, 1, 26, "radioHam"), (CX + 1, 1, 26, "cntDeskMetalArmyGreenRandomLootHelper"),
               (CX + 2, 21, 21, "radioHam")])
    for x, z in ((14, 33), (16, 33), (14, 35)):
        p.set(x, 1, z, "cntBarrelGasQuadA")


def theme_snow(p):
    """Maser test site: a gas depot behind sandbags, generators by the hut, and a scorched test target
    out front; the deck's open south half is the cannon pad."""
    p.ring(28, 29, 35, 35, 1, "sandbagStraight01", gaps={(31, 29)})
    for x, z in ((29, 31), (31, 31), (33, 31), (29, 33), (31, 33), (33, 33)):
        p.set(x, 1, z, "cntBarrelGasQuadA")
    crates(p, [(13, 1, 27, "cntCollapsedGeneratorbank"), (13, 1, 30, "cntCollapsedGeneratorbank")])
    # Test target: a scorched, broken concrete wall.
    for x, h, b in ((30, 3, "concreteShapes:cubeTrimDestroyed03"), (31, 4, "concreteShapes:cubeTrimDestroyed01"),
                    (32, 2, "concreteShapes:cubeTrimDestroyed04"), (33, 3, "concreteShapes:cubeTrimDestroyed02")):
        p.fill(x, 1, 16, x, h - 1, 16, "concreteShapes:cube")
        p.set(x, h, 16, b)


def theme_wasteland(p):
    """Oxygen Destroyer staging post: a gyrocopter helipad with an H and lights, fuel, a lab on the
    blockhouse's upper floor and a pen of irradiated drums."""
    x0, z0 = 26, 15
    p.fill(x0, 0, z0, x0 + 10, 0, z0 + 10, "terrAsphalt")
    for z in range(z0 + 2, z0 + 9):
        p.set(x0 + 3, 0, z, "terrConcrete")
        p.set(x0 + 7, 0, z, "terrConcrete")
    for x in range(x0 + 3, x0 + 8):
        p.set(x, 0, z0 + 5, "terrConcrete")
    for x, z in ((x0, z0), (x0 + 10, z0), (x0, z0 + 10), (x0 + 10, z0 + 10)):
        p.set(x, 1, z, "spotlightUpStraight_POI")
    for x, z in ((35, 28), (35, 30), (33, 30)):
        p.set(x, 1, z, "cntBarrelGasQuadA")
    crates(p, [(CX - 4, 6, 22, "cntShippingCrateLabEquipment"), (CX - 4, 6, 24, "cntChemPileLarge"),
               (CX + 4, 6, 22, "cntChemPileMedium"), (CX - 1, 6, 28, "lightDisplayXRayFull")])
    pen(p, 4, 18, 9, 23, [(6, 20, "cntBarrelRadiatedSingle00"), (7, 21, "cntBarrelRadiatedSingle45")])


POSTS = (pine, burnt, desert, snow, wasteland)


# ---- checks

def known_blocks(game):
    """Block names the game knows: blocks.xml plus every name in its own prefabs' .blocks.nim (shapes)."""
    names = set(re.findall(r'<block name="([^"]+)"', open(os.path.join(game, "Data", "Config", "blocks.xml"),
                                                          encoding="utf-8-sig").read()))
    for f in glob.glob(os.path.join(game, "Data", "Prefabs", "*", "*.blocks.nim")):
        b = open(f, "rb").read()
        o = 8
        for _ in range(struct.unpack_from("<i", b, 4)[0]):
            o += 4
            n, sh = 0, 0
            while True:
                c = b[o]
                o += 1
                n |= (c & 0x7F) << sh
                sh += 7
                if c < 0x80:
                    break
            names.add(b[o:o + n].decode("utf-8"))
            o += n
    return names


# ---- preview render (isometric, from the south-west, so the front shows)

COLOURS = [
    ("ladder", (200, 170, 60)), ("questRally", (40, 200, 255)), ("cntQuest", (40, 200, 255)),
    ("sleeper", (220, 40, 40)), ("cnt", (90, 140, 60)), ("radio", (90, 140, 60)),
    ("light", (255, 230, 120)), ("spotlight", (255, 230, 120)), ("flag", (200, 40, 40)),
    ("barbed", (70, 70, 70)), ("ironSpikes", (120, 60, 50)), ("sandbag", (185, 165, 115)),
    ("barrier", (160, 160, 150)), ("brick", (190, 130, 90)), ("wood", (140, 100, 60)),
    ("steel", (95, 105, 115)), ("corrugated", (120, 130, 125)), ("concrete", (165, 165, 160)),
    ("terrConcrete", (235, 235, 225)), ("terrAsphalt", (55, 55, 60)), ("metalPipe", (150, 60, 50)),
    ("terrSand", (220, 200, 150)), ("terrGravel", (130, 125, 115)), ("terr", (110, 95, 70)),
]
THIN = ("ladder", "railing", "barbed", "pole", "pillar", "sleeper", "questRally", "flag", "light", "spotlight", "ironSpikes")


def colour(b):
    for k, c in COLOURS:
        if b.startswith(k) or k in b:
            return c
    return (200, 0, 200)


def render(p, path, scale=9):
    """An isometric picture from the south-east, so the front (gate, view) faces the viewer."""
    from PIL import Image, ImageDraw
    s = scale
    w = (p.sx + p.sz + 4) * s
    h = int((p.sx + p.sz) * s * 0.5 + (p.sy + 4) * s) + 30
    ox, oy = (p.sz + 2) * s, (p.sy + 2) * s + 20
    img = Image.new("RGB", (w, h), (230, 236, 242))
    dr = ImageDraw.Draw(img)

    def proj(x, y, z):
        v = p.sz - z                     # toward the front
        return (ox + (x - v) * s, oy + (x + v) * s * 0.5 - y * s)

    def shade(c, f):
        return tuple(max(0, min(255, int(k * f))) for k in c)

    for (x, y, z), (b, _) in sorted(p.cells.items(), key=lambda c: (c[0][0] - c[0][2], c[0][1])):
        if y == 0 and p.get(x, 1, z) != "air":
            continue
        c = colour(b)
        t = 0.32 if any(k in b for k in THIN) else 0.0
        x0, x1, z0, z1, y0, y1 = x + t, x + 1 - t, z + t, z + 1 - t, y, y + 1
        south = [proj(x0, y0, z0), proj(x1, y0, z0), proj(x1, y1, z0), proj(x0, y1, z0)]
        east = [proj(x1, y0, z0), proj(x1, y0, z1), proj(x1, y1, z1), proj(x1, y1, z0)]
        top = [proj(x0, y1, z0), proj(x1, y1, z0), proj(x1, y1, z1), proj(x0, y1, z1)]
        dr.polygon(south, fill=shade(c, 0.78), outline=shade(c, 0.55))
        dr.polygon(east, fill=shade(c, 0.6), outline=shade(c, 0.45))
        dr.polygon(top, fill=c, outline=shade(c, 0.8))
    dr.text((8, 6), "%s  (tier %d, %d x %d m, tower top %d m; red: sleepers, blue: quest blocks)"
            % (p.title, p.tier, p.sx, p.sz, max(y for _, y, _ in p.cells) ), fill=(20, 20, 20))
    img.save(path)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.dirname(os.path.dirname(here))
    ap = argparse.ArgumentParser()
    ap.add_argument("--game", default=GAME)
    ap.add_argument("--out", default=os.path.join(root, "KaijuMod", "Prefabs"))
    ap.add_argument("--render")
    a = ap.parse_args()
    known = known_blocks(a.game) if os.path.isdir(a.game) else None
    for make in POSTS:
        p = make()
        used = {b for b, _ in p.cells.values()}
        if known is not None:
            missing = sorted(used - known)
            if missing:
                raise SystemExit("%s uses blocks the game does not have: %s" % (p.name, ", ".join(missing)))
        p.write(a.out)
        if a.render:
            os.makedirs(a.render, exist_ok=True)
            render(p, os.path.join(a.render, p.name + ".png"))
        print("%s: %s, tier %d, %d blocks, %d sleeper volumes" % (p.name, p.title, p.tier, len(p.cells), len(p.volumes)))


if __name__ == "__main__":
    main()
