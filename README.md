# Kaiju mod for 7 Days to Die

A giant kaiju boss for 7 Days to Die **V3.2**: a skyscraper-scale monster with a breath beam, ground stomp and tail sweep that can actually breach walls and hit players at its feet.

> Entity name: `zombieKaiju`. The code is a generic kaiju engine (MIT licensed). Character assets such as a Godzilla model, textures and roar are **not** in this repo: they are built into a local asset bundle and git-ignored, so the public repo holds no third-party IP.

## Why this needs code, not just XML

Vanilla zombie AI only considers the two blocks in front of an entity (feet + head). A scaled-up zombie looks huge but ignores anything 3+ blocks up and can't land melee on a player standing at its feet. This mod keeps a small path collider so it navigates like a zombie, and adds its own "smash box" and ranged/AoE attacks in C#.

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
- [ ] Phase 1: XML prototype (scaled vanilla brute, console spawn)
- [ ] Phase 2: custom rigged model + Animator exported as an asset bundle
- [ ] Phase 3: C# fight logic (smash box, breath beam, stomp AoE)
- [ ] Phase 4: encounter design (blood moon finale / world boss, scaling, loot)
- [ ] Phase 5: dedicated-server hardening and performance

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
3. In game, open the console (F1) and run `spawnentity <yourEntityId> zombieKaiju`.

## Character assets (local only)

Put your model's exported bundle at `KaijuMod/Resources/kaiju.unity3d` and keep source files in `assets-local/`. Both paths are git-ignored. `entityclasses.xml` points at the bundle by path, so the engine works with any model you drop in.

## Unity

Asset bundles must be built with the exact Unity editor version the game ships with. Read it from the first lines of `Player.log` before installing Unity.

## Research

See the research doc for prior art, the AI problem in detail, and the full build plan: [docs/research.md](docs/research.md).
