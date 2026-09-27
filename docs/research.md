# Research notes

Full research doc (living, commentable): https://claude.ai/code/artifact/2545ce6b-ea35-4037-9982-5633051475c3

## Key findings

- **Target:** V3.2 stable. V3.0 changed modding infrastructure (XUi rebuild, `entitygroups.xml` back to real XML, one publicized assembly); check tutorial versions.
- **The core problem:** zombie AI only checks the block at its feet and the one above. Giant entities ignore anything 3+ blocks up and can't hit a player at their feet. TFP dropped their own 5-block Behemoth partly over this and voxel pathfinding.
- **Approach:** keep a small (about 1x2 block) path collider so it navigates like a zombie; put hit colliders on the big body; add a custom "smash box" attack in C# that damages a volume of blocks in front of and above it. Lean on ranged/AoE attacks.
- **Vanilla systems to build on:** Chuck's boulder throw and the Rancher's insect swarm (ranged AI task + `Action1` on the hand item) for the breath; demolisher explosion and buffs for the stomp.
- **Prior art to study:** Kaiju Miniboss (V2/V3), ZombieBossVanilla (V3.x), FKD-Zombie_SizeScale, Server-Side Zombie Behemoth, Darkness Falls.
- **Asset bundles:** exact Unity version as the game; the game finds body parts by tag (for example `E_BP_BipedRoot`, `LargeEntityBlocker`). Copy a vanilla zombie's hierarchy.
- **Optional framework:** 0-SCore (SphereII) adds entity classes, AI tasks and MinEvents; its version must match the game build.

## Open decisions

- Original kaiju vs literal Godzilla (publishing needs an original design).
- Encounter type: blood moon finale, world boss, or both.
