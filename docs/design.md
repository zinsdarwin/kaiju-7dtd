# Design: Godzilla as an unstoppable world event

Decided 2026-09-30. Supersedes the "boss fight" direction in research.md.

## Pillars

- You never fight Godzilla. You can't. You run, you plan, and eventually you trap him.
- He is massive and he levels cities. Caught in his path = dead.
- Tension comes from the horizon: you see him coming and have to stay ahead.

## Game modes

### 1. The Run (first)
A survival run that has to stay ahead of the apocalypse. Godzilla moves city to city; blood moons keep their normal pressure. You scavenge in towns he hasn't reached yet and leave before he arrives.

Open questions: does he track the players or follow his own route? Does his pace rise each in-game week?

### 2. Oxygen Destroyer (later)
Find components across the map, assemble the device at a workbench, plant it in a city on his route. If he walks into its radius while it's armed: death sequence, run won.

## Architecture

Godzilla is **not** a zombie entity and uses no zombie AI or pathfinding.

| Piece | Runs on | Job |
| --- | --- | --- |
| Director | Server (C#) | Picks next city from world POI data, advances his position every tick as plain data, even through unloaded chunks |
| Destruction | Server (C#) | Rate-limited explosion-style block damage along his footprint; caps on structural collapse |
| Kill zone | Server (C#) | Players inside the footprint radius die |
| Devastation pass | Server (C#, Harmony on chunk load) | Applies ruin to areas he crossed while nobody was nearby |
| Visual | Every client | Visual-only model (not an Entity) so it renders at long distance; position synced by a custom NetPackage |
| Oxygen Destroyer | XML (quests, items, recipes, blocks) + C# trigger | Assembly and the win condition |

## First milestone

A grey box walks between two towns, destroys blocks under it, and kills a player standing in its path. No Godzilla model. If that is tense, everything after it is content.

## Risks

- Block-change volume in multiplayer (network and chunk remeshing).
- Long-distance rendering of a non-entity model (LOD, fog, view distance).
- Engine APIs to verify by decompiling the V3.2 assembly before relying on them: server explosion call, batched block-change RPC, POI/town list for a generated world, chunk-loaded hook.
