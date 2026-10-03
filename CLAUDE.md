# CLAUDE.md

Godzilla world-event mod for 7 Days to Die V3.2, single player only. Read `docs/design.md` first; it is the source of truth. `docs/research.md` is background from an earlier boss-fight direction (superseded).

## Key decisions
- Godzilla is not a zombie: no zombie AI or pathfinding. A C# director moves him along a waypoint list as plain data, destroys blocks in his footprint, and kills players inside it. You never fight him.
- The Run: he comes out of the ocean and destroys a big city at the end of each strip when the blood moon horde ends; radiation chases you along the strip during the week; you escape north through a mountain pass. See docs/design.md.
- Single player only. Disable the event with a console warning in hosted/dedicated games.
- Custom 6k (6144 m) snake map: ocean on the west and east edges, 5 biome strips (pine, burnt, desert, snow, wasteland) alternating direction, one road through all, mountain ranges with one pass between strips, a big coastal city at each strip's end. One biome per 7-day blood moon cycle.
- Godzilla model, textures and audio stay local (git-ignored: `KaijuMod/Resources/*.unity3d`, `assets-local/`). Never commit them. Code is MIT.

## Working on this machine
- Build: `dotnet build src/KaijuMod -c Release -p:GamePath="<7 Days To Die install>"`; output DLL copies into `KaijuMod/`.
- Deploy: copy `KaijuMod/` into `<install>/Mods/`; launch without EAC.
- Logs: `%APPDATA%\7DaysToDie\logs\` (or `Player.log` next to the exe on some setups).
- Verify engine APIs by decompiling `7DaysToDie_Data/Managed/Assembly-CSharp.dll` (e.g. `ilspycmd`) before relying on them. Explosion, batched block changes, chunk-load hook, single-player check, visual-only GameObject and ModEvents are verified in `docs/engine-api.md` (from build V 3.3.0 b17); its last section lists what still needs an in-game test.

## Next steps
1. Map generator (`tools/snakemap`): a Python script that writes the snake world as a new GeneratedWorlds folder, using an existing V3.2 6k world as a read-only format reference (never modify it).
2. Milestone 1: grey box walks the road between two towns, destroys blocks under it, kills a player in its path.

## Open questions
- Does Godzilla roam between attacks, or stay offshore until the next end city? (Default: offshore.)

## Commits
End commit messages with the co-author line Claude Code adds by default. Author: Darwin.
