# CLAUDE.md

Godzilla world-event mod for 7 Days to Die V3.2, single player only. Read `docs/design.md` first; it is the source of truth. `docs/research.md` is background from an earlier boss-fight direction (superseded).

## Key decisions
- Godzilla is not a zombie: no zombie AI or pathfinding. A C# director moves him along the main road's waypoint list as plain data, destroys blocks in his footprint, and kills players inside it. You never fight him.
- Single player only. Disable the event with a console warning in hosted/dedicated games.
- Custom 6k (6144 m) snake map: 5 biome strips (pine, burnt, desert, snow, wasteland) alternating direction, one road through all, ridges between strips. One biome per 7-day blood moon cycle.
- Godzilla model, textures and audio stay local (git-ignored: `KaijuMod/Resources/*.unity3d`, `assets-local/`). Never commit them. Code is MIT.

## Working on this machine
- Build: `dotnet build src/KaijuMod -c Release -p:GamePath="<7 Days To Die install>"`; output DLL copies into `KaijuMod/`.
- Deploy: copy `KaijuMod/` into `<install>/Mods/`; launch without EAC.
- Logs: `%APPDATA%\7DaysToDie\logs\` (or `Player.log` next to the exe on some setups).
- Verify engine APIs by decompiling `7DaysToDie_Data/Managed/Assembly-CSharp.dll` (e.g. `ilspycmd`) before relying on them. Explosion, batched block changes, chunk-load hook, single-player check, visual-only GameObject and ModEvents are verified in `docs/engine-api.md` (from build V 3.3.0 b17); its last section lists what still needs an in-game test.

## Next steps
1. Map generator (`tools/`): generate a vanilla 6k world as a template, inspect its real V3.2 file formats, then write a script that rewrites biomes, roads, heightmap and prefab placements for the snake layout into a new world folder (never modify the template).
2. Milestone 1: grey box walks the road between two towns, destroys blocks under it, kills a player in its path.

## Open questions
- Godzilla follows the players, or his own route? (Map design implies the road.)
- Does his pace ramp up over time, or does he start a day behind?

## Commits
End commit messages with the co-author line Claude Code adds by default. Author: Darwin.
