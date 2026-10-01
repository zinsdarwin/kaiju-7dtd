# Kaiju mod for 7 Days to Die

A Godzilla world event for 7 Days to Die **V3.2**. You don't fight him; you run. He walks city to city levelling everything, and anyone caught in his path dies. Later: assemble the Oxygen Destroyer and plant it in his path. See [docs/design.md](docs/design.md).

> Entity name: `zombieKaiju`. The code is a generic kaiju engine (MIT licensed). Character assets such as a Godzilla model, textures and roar are **not** in this repo: they are built into a local asset bundle and git-ignored, so the public repo holds no third-party IP.

## Why this needs code, not just XML

Godzilla is not a zombie: no zombie AI or pathfinding. A server-side C# director moves him between cities as plain data, applies destruction along his footprint, kills players inside it, and moves a long-distance visual model. Single player only.

## License

Code: MIT (see `LICENSE`). Covers this repo only, not any character assets you use with it.

## Layout

```
KaijuMod/            <- the deployable mod folder (copy into <game>/Mods/)
  ModInfo.xml
  Config/            <- XPath patches (push from server automatically)
  Resources/         <- Unity asset bundles (every client must install)
  KaijuMod.dll       <- built from src/ (copied here by the build)
src/KaijuMod/        <- Harmony C# project (.NET Framework 4.8)
docs/                <- design notes and research
```

## Roadmap

- [x] Phase 0: repo, mod skeleton, Harmony project
- [ ] Milestone 1: grey box walks between two towns, destroys blocks, kills players in its path
- [ ] Long-distance visual model
- [ ] Devastation pass for areas crossed while unloaded
- [ ] The Run game mode (pacing, routes, blood moon interplay)
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
3. Start a single player game, open the console (F1) and run `kaiju test`. A grey box appears 120 m in front of you and walks straight through you, clearing every non-terrain block in a 10 m radius. Stand still to get crushed.

Other commands (`kaiju help` lists them): `kaiju start` walks the route in `KaijuMod/route.txt` (or the placeholder list in `Route.cs`), `kaiju stop`, `kaiju status`, `kaiju speed <m/s>`, `kaiju radius <m>`. To record a real route between two towns, walk the road and run `kaiju addpoint` at each bend, then `kaiju saveroute`.

All calls into the game go through `src/KaijuMod/GameApi.cs`. Those marked `UNVERIFIED` still need checking against the decompiled game assembly (the rest were checked against V3.3.0); if the build fails, that file is where to look.

## Character assets (local only)

Put your model's exported bundle at `KaijuMod/Resources/kaiju.unity3d` and keep source files in `assets-local/`. Both paths are git-ignored. `entityclasses.xml` points at the bundle by path, so the engine works with any model you drop in.

## Unity

Asset bundles must be built with the exact Unity editor version the game ships with. Read it from the first lines of `Player.log` before installing Unity.

## Research

See the research doc for prior art, the AI problem in detail, and the full build plan: [docs/research.md](docs/research.md).
