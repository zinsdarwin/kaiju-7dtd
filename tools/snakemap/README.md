# Snake map generator

Builds the custom 6k (6144 m) world for the Kaiju run: five biome strips stacked south to north
(pine, burnt, desert, snow, wasteland), ocean along the west and east edges, mountains along the
north and south edges, and a mountain range between each pair of strips with one pass at
alternating ends. One highway snakes through all five strips. Each strip has a big city at its
coastal far end, where the pass north begins; the pine strip also has a start city on its west
coast. Three towns sit along the road in each strip. Every settlement has a trader. Wilderness
POIs are scattered between.

```
python tools/snakemap/snakemap.py
```

Needs Python 3 with numpy, Pillow and scipy. Runs in about 15 s. Writes the world to
`%APPDATA%\7DaysToDie\GeneratedWorlds\Kaiju Snake` (pick it under New Game) and a preview to
`tools/snakemap/out/preview.png` (git-ignored). Options: `--name`, `--seed`, `--out`, `--game`
(install folder, for the POI list), `--template` (any V3.x generated world; only its `main.ttw` and
game version are read, never modified), `--preview`.

## Output

| File | Contents |
|---|---|
| `dtm.raw` | heightmap, 6144x6144 little-endian uint16, metres x 256 |
| `biomes.png` | 768x768 RGBA, biome colours from `biomes.xml` |
| `splat3.png` | roads, 6144x6144 RGBA, asphalt (255,0,0,255) |
| `splat4.png` | water, 6144x6144 RGBA, blue = water surface height (sea level 30) |
| `radiation.png` | 192x192 RGBA, empty |
| `prefabs.xml` | POI placements |
| `spawnpoints.xml` | player spawns, just east of the start city |
| `main.ttw`, `map_info.xml` | world metadata |
| `kaiju.xml` | for the mod: settlements (name, kind, role, strip, biome, centre) and Godzilla's route (road waypoints every 25 m) |

The game builds `dtm_processed.raw`, `splat3_processed.png`, `splat4_processed.png`, the
`_half` textures and `checksums.txt` itself on the first load. That step stamps POI footprints
into the terrain and smooths the roads, so the first load of a new world takes a little longer.

## Format notes

Verified against a V3.2 generated world and the decompiled game (`ChunkProviderGenerateWorldFromRaw`,
`WorldGenerationEngineFinal.WorldBuilder`, `WorldDecoratorPOIFromImage`):

- Row 0 of `dtm.raw` is the south edge (z = -3072); column 0 is x = -3072. PNGs are loaded by
  Unity bottom row first, so their bottom row is the south edge (checked on a generated world:
  its water only lies over below-sea-level terrain, and its roads only on graded ground, when
  read that way; the first Kaiju Snake build had them upside down and spawned players in the
  wasteland).
- Prefab `position` is the bounding-box minimum corner in centred world coordinates; x/z size swap
  for rotations 1 and 3; y is ground height plus the prefab's `YOffset`.
- Rotation is `(RotationToFaceNorth + quarter turns) & 3`, from the prefab's own XML.
  Settlement lots face north (0 turns) or south (2 turns) toward their street.
- The game re-processes a world when `checksums.txt` is missing or does not match `prefabs.xml`,
  `dtm.raw`, `splat3.png`, `water_info.xml`, `main.ttw` or `biomes.png`.

## Not yet

- Towns are street grids of single POIs, not the game's own town tiles.
- No rivers or lakes; wilderness POIs avoid slopes rather than reshaping hills.
- Godzilla's route in `kaiju.xml` is not read by the mod yet (`route.txt` still drives `kaiju start`).
