# Generated world file format (V3.2)

Checked 2026-10-03 against a V3.2 random-generated 6k world (GameVersion V.3.20.10) and the decompiled game code (`ChunkProviderGenerateWorldFromRaw`, `WorldGenerationEngineFinal.WorldBuilder`, `WorldDecoratorPOIFromImage`). The snake map generator in `tools/snakemap` writes these files.

## What a world folder needs

The game's own generator writes only these files, so a custom world needs only them:

- `biomes.png`
- `radiation.png`
- `splat3.png`
- `splat4.png`
- `dtm.raw`
- `prefabs.xml`
- `spawnpoints.xml`
- `main.ttw`
- `map_info.xml`

The game builds `dtm_processed.raw`, `splat3_processed.png`, `splat4_processed.png`, the `_half` PNGs and `checksums.txt` itself on first load. It re-processes when any of them is missing, or when `checksums.txt` (`name=crc32`, zlib CRC32 in decimal, UTF-8 with BOM) does not match `prefabs.xml`, `dtm.raw`, `splat3.png`, `water_info.xml`, `main.ttw` and `biomes.png`. Processing stamps prefab heights into the heightmap (`CopyWorldPrefabHeightsIntoHeightMap`) and smooths the roads.

## Orientation

Row 0 of `dtm.raw` is the south edge (z = -3072). Column 0 is x = -3072.

PNGs are the other way up. The game loads them as Unity textures, which start from the image's bottom row, so a PNG's bottom row is the south edge. Checked on a generated world: read that way, all its water lies over terrain below sea level and its roads lie on graded ground. The first Kaiju Snake build wrote them top row south and spawned players in the wasteland.

## Files

- **dtm.raw:** 6144 × 6144, little-endian uint16, height in metres × 256.
- **biomes.png:** 768 × 768 RGBA (1/8 scale). Colours from `biomes.xml`:
  - pine `#004000`
  - burnt `#ba00ff`
  - desert `#FFE477`
  - snow `#FFFFFF`
  - wasteland `#ffa800`
- **radiation.png:** 192 × 192 RGBA (1/32 scale). Fully transparent in a normal random world.
- **splat3.png (roads):** 6144 × 6144 RGBA. Asphalt is (255,0,0,255), gravel is (0,255,0,255), everything else is (0,0,0,0).
- **splat4.png (water):** 6144 × 6144 RGBA. Only the blue channel is used: the water surface height in metres. The game fills water up to that height wherever the terrain is lower, so an ocean is a low seabed plus B = sea level. There is no `water_info.xml` in random worlds; pregen worlds have one with an empty `<WaterSources>`.
- **map_info.xml:**
  - SchemaVersion 1, Scale 1, HeightMapSize "6144,6144"
  - Modes "Survival,SurvivalSP,SurvivalMP,Creative"
  - FixedWaterLevel false, RandomGeneratedWorld true
  - GameVersion and Seed
  - a `<property class="Generation">` block with the generator's slider settings
- **main.ttw:** 264 bytes. A blank WorldState (version string plus a GUID). It is generic and can be copied.
- **prefabs.xml:** lines like `<decoration type="model" name="quarry_02" position="698,10,706" rotation="3" />`.
  - `position` is the bounding-box minimum corner in centred world coordinates, with the x and z sizes swapped for rotations 1 and 3.
  - y is the ground height plus the prefab's YOffset.
  - `rotation` = (RotationToFaceNorth + quarter turns) & 3.
  - Each prefab's PrefabSize, YOffset, RotationToFaceNorth, AllowedTownships, Tags and DifficultyTier come from `Data/Prefabs/POIs/<name>.xml`.
- **spawnpoints.xml:** entries like `<spawnpoint position="-191,42.11103,362" rotation="0,60,0" />`.
