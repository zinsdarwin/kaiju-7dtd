# Design: Godzilla as an unstoppable world event

Decided 2026-09-30. Supersedes the "boss fight" direction in research.md.

## Pillars

- You never fight Godzilla. You can't. You run, you plan, and eventually you trap him.
- He is massive and he levels cities. Caught in his path = dead.
- Tension comes from the horizon: you see him coming and have to stay ahead.

## Game modes

### 1. The Run (first)
A survival run across a custom snake-shaped map, one biome per 7-day blood moon cycle: a full run is 35 days. Decided by Darwin 2026-10-03.

1. **Day 1.** You spawn in the pine forest at the bottom left of the map, just east of a big coastal city. Godzilla comes out of the western ocean and destroys that city.
2. **The chase.** Radiation spreads east from the ruined city and chases you along the strip. You have 7 days to loot your way to the big city at the strip's east end.
3. **Blood moon.** You survive the horde in or near the east city. When the horde ends, Godzilla comes out of the eastern ocean and destroys that city.
4. **The escape.** You have a few hours after the horde to get north through the mountain pass into the burnt forest.
5. **Repeat.** The radiation now chases you west along the burnt strip to its west-end city, Godzilla hits it after the next horde, and so on through all five biomes.

Defaults picked where the plan does not say yet (tunable):
- The radiation is the week-long chaser. Godzilla stays offshore between attacks and comes ashore only at the end cities.
- Smaller towns sit along the road between the two big cities of each strip.

## The map (6k, 6144 m)

- Ocean along the whole west and east edges. Godzilla lives there and comes ashore at the end cities.
- Five horizontal biome strips, about 1.2 km tall each, alternating direction:
  1. Pine forest, bottom, west to east (start)
  2. Burnt forest, east to west
  3. Desert, west to east
  4. Snow, east to west
  5. Wasteland, west to east (finale: the Oxygen Destroyer)
- High mountain ranges between strips. Each has one pass, at alternating ends, beside that strip's end city: the only way north.
- A big city at the far (coastal) end of each strip, plus the starting city at the west end of the pine strip.
- One main road snakes through all five strips and every pass, with towns along it.

### Building it
The game's random world generator can't make this layout, so it is a custom world written by a Python script in `tools/snakemap`. It uses a V3.2 random-generated 6k world as a read-only format reference and writes a new world folder ("Kaiju Snake") into GeneratedWorlds. It also writes the road's waypoints and the end cities' positions for the mod.

### The finale: the Oxygen Destroyer
Part of The Run. Decided by Darwin 2026-10-03.

- Four pieces of the Oxygen Destroyer, one in each of Cedar Harbor, Cinder Bay, Dune Point and Frostport. Port Hemlock falls on day 1, too soon to loot, so it has none.
- Each piece is in a glowing crate somewhere in its city. A compass marker shows it once you reach the city. Grab it before Godzilla destroys the city after the blood moon. The crate survives his attack, so a missed piece can still be dug out of the ruins during the grace hours, ahead of the radiation.
- In Ashmouth, the wasteland's end city, craft the Oxygen Destroyer at a workbench from the four pieces, place it inside the city and arm it.
- When the final blood moon horde ends, Godzilla attacks Ashmouth. If he walks into the armed device's radius: death sequence, run won. Otherwise he levels Ashmouth and the run is lost.

## Architecture

Godzilla is **not** a zombie entity and uses no zombie AI or pathfinding.

**Single player only.** The game still runs a local server and client in one process, so code uses server-side calls, but there is no network sync. The mod disables the event (with a console warning) in hosted or dedicated games.

| Piece | Runs on | Job |
| --- | --- | --- |
| Director | Server (C#) | Brings him ashore at each end city when the blood moon horde ends and walks him through it along a waypoint list, advancing his position every tick as plain data, even through unloaded chunks |
| Radiation | Server (C#) | A front that sweeps along each strip over the week, from the ruined city toward the next one, hurting players behind it |
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
