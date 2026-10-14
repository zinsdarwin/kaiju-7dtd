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
3. **Blood moon.** You survive the horde in or near the east city. An in-game hour after the horde ends (the Disaster Response Bureau's note warns that the horde comes before him), Godzilla comes out of the eastern ocean and destroys that city.
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

### The atomic breath
His breath ends in an atomic blast, as in Minus One: flash, shockwave, a crater much wider than the beam, and a mushroom cloud a few hundred metres tall. He uses it on each city he attacks.

### Weapons and the finale
Part of The Run. Decided by Darwin 2026-10-03, reworked 2026-10-10 and 2026-10-11.

- Each strip after the pine forest has a weapon for its end-city attack, built from parts found in that strip's towns. Each of the 3 towns has one part, in the main loot chest (the end-of-POI reinforced or hardened chest) of the town's best quest POI, where the glowing crate takes the chest's place, keeping its usual loot. The town's trader offers it as a quest at the top of the job list. Craft the weapon at a workbench from its 3 parts and place it anywhere in range, e.g. the army post in the mountains north of the end city, where you hole up for the blood moon. None of the weapons stops the attack:
  - Burnt forest (Cinder Bay): a missile battery launch control. It fires a salvo of 8 missiles that arc onto him; he roars and stops for a moment. One salvo every 30 s.
  - Desert (Dune Point): an airstrike radio. Three jets come in from the sea, carpet-bomb a line of explosions across him, turn and make a second pass, and he staggers. Once per attack.
  - Snow (Frostport): an experimental maser cannon. Fuel it with 25 gas three times to charge it. Its one shot is a crackling beam for 6 s while he staggers. Then he turns his breath on the cannon and destroys it, so get clear.
  - Wasteland (Ashmouth): the Oxygen Destroyer.
- The pine forest has no weapon: Cedar Harbor gets the plain attack.
- Two of the snow quests also reward the gyrocopter chassis and accessories: assemble a gyrocopter at a workbench before the last blood moon.
- The finale: when the final blood moon horde ends, Godzilla attacks Ashmouth, walking slower than in the other attacks. Fly the gyrocopter over him with the Oxygen Destroyer aboard and drop it on him (right mouse button). It falls, its fins steer it onto him, and he dies (death sequence, run won). A miss doesn't go off, and the device comes back to you to try again. If the attack ends without a hit, Ashmouth falls and the run is lost.
- POI tiers climb along the run (decided by Darwin 2026-10-13). Every town and city in a strip fills its lots from its biome's band first: pine 1-2, burnt 2-3, desert 3-4, snow 4-5, wasteland 4-5. Other tiers only fill gaps. The snow and wasteland settlements have deeper blocks so the 100 m tier 4-5 POIs fit. Ashmouth is the big tier 5 city: every big tier 5 landmark (Dishong Tower, Crack-a-Book HQ, Higashi Pharmaceutical, Vanity Tower...) and then more tier 5.
- G-cell zombies: the fallout carries Godzilla's cells, and the dead it touches get back up. That's the outbreak's story, told in the starting note from the Large-Scale Disaster Response Bureau (Godzilla Minus Zero's response agency, director Kenji Noda). You also start with your bike: you were out camping with it when he came. The vanilla tiers are stages of infection (decided by Darwin 2026-10-13):
  - Normal and feral zombies are darkened, with patches of black scales.
  - Radiated zombies have more scales, darker skin, and his atomic blue glow. Charged and infernal are the top stage, as in the game, in two flavours. Charged keeps more skin and glows bright electric blue, and its breath hits harder. Infernal is charred near black with blue embers, and blows up bigger. The skins are made in code from each zombie's own texture.
  - Radiated, charged and infernal zombies now and then spit a small atomic breath at you (a blue bolt you can dodge; range, damage and frequency rise by stage).
  - They blow up in a small atomic blast when they die: blue flash, shockwave and damage nearby, but no block damage.
  - The cop's vomit is a blue atomic bolt.
  - `kaiju zbreath` and `kaiju zblast` turn the breath and blasts off; `kaiju gcell` spawns one of each stage to look at.

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
| Oxygen Destroyer | XML (quests, items, recipes, blocks, loot) + C# (crates, missile battery, gyrocopter drop) | Assembly and the win condition |

## First milestone

A grey box walks the road between two towns, destroys blocks under it, and kills a player standing in its path. No Godzilla model. If that is tense, everything after it is content.

## Risks

- Block-change volume (chunk remeshing and frame rate).
- Long-distance rendering of a non-entity model (LOD, fog, view distance).
- Engine APIs to verify by decompiling the V3.2 assembly before relying on them: server explosion call, batched block-change RPC, chunk-loaded hook.
