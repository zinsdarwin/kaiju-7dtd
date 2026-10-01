# Design: Godzilla as an unstoppable world event

Decided 2026-09-30. Supersedes the "boss fight" direction in research.md.

## Pillars

- You never fight Godzilla. You can't. You run, you plan, and eventually you trap him.
- He is massive and he levels cities. Caught in his path = dead.
- Tension comes from the horizon: you see him coming and have to stay ahead.

## Game modes

### 1. The Run (first)
A survival run on a custom snake-shaped map. Godzilla follows the main road city to city, levelling each one. You loot ahead of him and must reach the next biome before he catches up. One biome per 7-day blood moon cycle: a full run is 35 days.

## The map (6k, 6144 m)

- Five horizontal biome strips, about 1.2 km tall each, alternating direction:
  1. Pine forest, bottom, left to right (start)
  2. Burnt forest, right to left
  3. Desert, left to right
  4. Snow, right to left
  5. Wasteland, left to right (finale; later the Oxygen Destroyer)
- One main road snakes through all five, about 5.5 km per strip plus 4 northward switchbacks (roughly 28-30 km total). Cities and towns sit along it.
- High ridges between strips, so the road's switchback is the only way north.
- Godzilla's route is the road: a fixed waypoint list. No route planning.
- Pace (tunable): about one strip per 7 in-game days. On 60-minute days that is slower than walking; the pressure is looting each city before he arrives. Options: start him a day behind, or ramp his speed.

### Building it
The game's random world generator can't make this layout, so it is a custom world. Plan: generate a normal 6k world in V3.2 as a template, then a script in `tools/` rewrites its biome map, road map, heightmap and prefab placements. Using real V3.2 files as the template avoids guessing at formats (the only format reference found is from Alpha 17).


### 2. Oxygen Destroyer (later)
Find components across the map, assemble the device at a workbench, plant it in a city on his route. If he walks into its radius while it's armed: death sequence, run won.

## Architecture

Godzilla is **not** a zombie entity and uses no zombie AI or pathfinding.

**Single player only.** The game still runs a local server and client in one process, so code uses server-side calls, but there is no network sync. The mod disables the event (with a console warning) in hosted or dedicated games.

| Piece | Runs on | Job |
| --- | --- | --- |
| Director | Server (C#) | Follows the road's waypoint list, advances his position every tick as plain data, even through unloaded chunks |
| Destruction | Server (C#) | Rate-limited explosion-style block damage along his footprint; caps on structural collapse |
| Kill zone | Server (C#) | Players inside the footprint radius die |
| Devastation pass | Server (C#, Harmony on chunk load) | Applies ruin to areas he crossed while nobody was nearby |
| Visual | Same process | Visual-only model (not an Entity) so it renders at long distance; the director moves it directly |
| Oxygen Destroyer | XML (quests, items, recipes, blocks) + C# trigger | Assembly and the win condition |

## First milestone

A grey box walks the road between two towns, destroys blocks under it, and kills a player standing in its path. No Godzilla model. If that is tense, everything after it is content.

## Risks

- Block-change volume (chunk remeshing and frame rate).
- Long-distance rendering of a non-entity model (LOD, fog, view distance).
- Engine APIs to verify by decompiling the V3.2 assembly before relying on them: server explosion call, batched block-change RPC, chunk-loaded hook.
