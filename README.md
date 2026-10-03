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
- [x] Map generator: custom 6k snake world (`tools/snakemap`)
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
3. Start a single player game (debug god mode off, or he can't hurt you), open the console (F1) and run `kaiju test`. A stand-in Godzilla built from simple shapes appears 120 m in front of you and walks straight through you, clearing every non-terrain block in a 10 m radius. Stand still to get crushed. The log (`%APPDATA%\7DaysToDie\logs\`) shows `[KaijuMod]` lines.

Other commands (`kaiju help` lists them): `kaiju start` walks the route in `KaijuMod/route.txt` (or the placeholder list in `Route.cs`), `kaiju stop`, `kaiju status`, `kaiju speed <m/s>`, `kaiju radius <m>`, `kaiju height <m>`, `kaiju stride <body heights>` (slows or speeds his walk animation), `kaiju breath` (atomic breath at what you are looking at; `kaiju breath me` aims at you). The breath stops him, turns him to face the target, lights his dorsal plates for 3 s, then fires a 4 s beam that destroys blocks along its path (terrain stays) and kills anyone in it. It needs the model bundle for the glow, head aim and beam materials. When the beam ends on a target it detonates like an atomic bomb, Minus One style: a white flash, a dust shockwave along the ground, a crater four times the beam's width (blocks only, terrain stays; anyone inside dies), and a mushroom cloud that climbs to about five times his height over a minute and drifts away over a couple of minutes. To record a real route between two towns, walk the road and run `kaiju addpoint` at each bend, then `kaiju saveroute`.

### The Run

Start a new game on the **Kaiju Snake** world (from `tools/snakemap`) and The Run starts by itself: the mod reads `kaiju.xml` from the world folder (other worlds leave it off).

- 90 s after you first spawn, Godzilla rises from the sea and destroys the start city. He walks in along its main road, back out along the next street, and uses his atomic breath on the blocks in between.
- A radiation front then sweeps along the strip from the ruined city toward the strip's end city, timed to stop 200 m short of it when the blood moon starts. Anyone behind it, or in an earlier strip, takes 4 radiation damage a second and gets an on-screen warning.
- When the blood moon horde ends (dawn), he destroys the end city. For the next 4 in-game hours the front sweeps over that city to the coast while you escape north through the pass; then the next strip's front starts.
- **The Oxygen Destroyer.** Cedar Harbor, Cinder Bay, Dune Point and Frostport each hold one part in a glowing crate (placed in the building nearest the city centre once you get near; a compass marker appears inside the city). Godzilla never destroys the crates, so a missed part can be dug out of the ruins. Craft the Oxygen Destroyer at a workbench from the four parts (plus 10 electrical parts and 20 forged steel), place it inside Ashmouth and use it to arm it. When the last blood moon horde ends he attacks Ashmouth: if he comes within 40 m of the armed device he dies and the run is won; otherwise the city falls and the run is lost.
- Progress is saved to `kaiju_run.xml` in the save folder.

Commands: `kaiju run` (status: day, strip, front position, next attack; `kaiju run reset` starts over), `kaiju attack <city name|start|end>` (send him at a city now), `kaiju radiation <on|off>`, `kaiju parts` (where each part's crate is and which you carry), `kaiju give <1-4|all>` (testing), `kaiju attack finale` (the last attack, with the Oxygen Destroyer check, now).

All calls into the game go through `src/KaijuMod/GameApi.cs`, checked against V3.3.0 (b17); see [docs/engine-api.md](docs/engine-api.md).

## Character assets (local only)

Put your model's exported bundle at `KaijuMod/Resources/kaiju.unity3d` and keep source files in `assets-local/`. Both paths are git-ignored. Without a bundle, a stand-in body built from primitives is shown.

### Model credit

The bundle used in development is built from "Godzilla First Walk Animation (scrunchy32205 alt)" by carladoll996 (https://sketchfab.com/3d-models/godzilla-first-walk-animationscrunchy32205-alt-e46c2cc5b698471588afd0ff9875d519), licensed CC BY 4.0 (http://creativecommons.org/licenses/by/4.0/). Converted to a Unity asset bundle (re-oriented, scaled, Standard-shader materials); not distributed with this repo. Godzilla is a trademark of Toho Co., Ltd.; this is an unofficial fan mod.

## Unity

Asset bundles must be built with the exact Unity editor version the game ships with: **2022.3.62f2** for V3.3.0 (b17) (read it from `UnityPlayer.dll`'s product version or the first lines of `Player.log`).

`tools/unity-bundle/` holds the build script. To build the bundle: copy it to a project under `assets-local/` (e.g. `assets-local/unity-kaiju/`), put the model at `Assets/Model/godzilla.glb`, then run:
```
"<Unity 2022.3.62f2>\Editor\Unity.exe" -batchmode -projectPath assets-local/unity-kaiju -executeMethod BuildKaiju.Build -kaijuOut KaijuMod/Resources -logFile build.log -quit
```

It imports the glb with glTFast, stands the model on its feet facing +z at height 1, swaps in Standard-shader materials, loops the walk with a legacy Animation, and writes `kaiju.unity3d` plus `preview.png` (a render of the model; git-ignored).

## Research

[docs/research.md](docs/research.md) is background from an earlier boss-fight direction, now superseded by the design doc.
