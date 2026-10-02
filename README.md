# Kaiju mod for 7 Days to Die

A Godzilla world event for 7 Days to Die **V3.2**, single player only. You don't fight him; you run. He walks the main road city to city levelling everything, and anyone caught in his path dies. Later: assemble the Oxygen Destroyer and plant it in his path. See [docs/design.md](docs/design.md).

The code is a generic kaiju engine (MIT licensed). Character assets such as a Godzilla model, textures and roar are **not** in this repo: they are built into a local asset bundle and git-ignored, so the public repo holds no third-party IP.

## Why this needs code, not just XML

Godzilla is not a zombie: no zombie AI or pathfinding. A C# director moves him along the road's waypoint list as plain data, destroys blocks in his footprint, kills players inside it, and moves a visual-only model that renders at long distance. In hosted or dedicated games the event is disabled with a console warning.

## License

Code: MIT (see `LICENSE`). Covers this repo only, not any character assets you use with it.

## Layout

```
KaijuMod/            <- the deployable mod folder (copy into <game>/Mods/)
  ModInfo.xml
  Config/            <- XPath patches (Oxygen Destroyer items, quests, recipes; later)
  Resources/         <- local Unity asset bundles (git-ignored)
  KaijuMod.dll       <- built from src/ (copied here by the build)
src/KaijuMod/        <- Harmony C# project (.NET Framework 4.8)
tools/               <- map generator for the custom snake world (planned)
docs/                <- design notes and research
```

## Roadmap

- [x] Phase 0: repo, mod skeleton, Harmony project
- [ ] Map generator: custom 6k snake world from a vanilla V3.2 template
- [ ] Milestone 1: grey box walks the road between two towns, destroys blocks, kills a player in its path
- [ ] Long-distance visual model
- [ ] Devastation pass for areas crossed while unloaded
- [ ] The Run game mode (pacing, blood moon interplay)
- [ ] Godzilla model (local asset bundle)
- [ ] Oxygen Destroyer quest line and win condition

## Building the DLL

The C# project references the game's own assemblies, which are not in this repo.

1. Install 7 Days to Die V3.2 and note the install path.
2. Build, pointing `GamePath` at it:
   ```
   dotnet build src/KaijuMod -c Release -p:GamePath="C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"
   ```
   The DLL is copied into `KaijuMod/` automatically.

## Installing for testing

1. Copy the `KaijuMod` folder into `<game>/Mods/`.
2. Launch **without Easy Anti-Cheat** (required for DLL mods).
3. Start a single player game (debug god mode off, or he can't hurt you), open the console (F1) and run `kaiju test`. A grey box appears 120 m in front of you and walks straight through you, clearing every non-terrain block in a 10 m radius. Stand still to get crushed. The log (`%APPDATA%\7DaysToDie\logs\`) shows `[KaijuMod]` lines.

Other commands (`kaiju help` lists them): `kaiju start` walks the route in `KaijuMod/route.txt` (or the placeholder list in `Route.cs`), `kaiju stop`, `kaiju status`, `kaiju speed <m/s>`, `kaiju radius <m>`, `kaiju height <m>`. To record a real route between two towns, walk the road and run `kaiju addpoint` at each bend, then `kaiju saveroute`.

All calls into the game go through `src/KaijuMod/GameApi.cs`, checked against V3.3.0 (b17); see [docs/engine-api.md](docs/engine-api.md).

## Character assets (local only)

Put your model's exported bundle at `KaijuMod/Resources/kaiju.unity3d` and keep source files in `assets-local/`. Both paths are git-ignored.

## Unity

Asset bundles must be built with the exact Unity editor version the game ships with. Read it from the first lines of `Player.log` before installing Unity.

## Research

[docs/research.md](docs/research.md) is background from an earlier boss-fight direction, now superseded by the design doc.
