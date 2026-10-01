# Engine API reference (verified by decompiling)

Decompiled 2026-09-30 with `ilspycmd` 8.2 from the local install's `7DaysToDie_Data/Managed/Assembly-CSharp.dll`.

- `Constants.cVersionInformation` = `(V, 3, 30, 17)`, which the game formats as **V 3.3.0 (b17)**, not V3.2. Check this against the in-game version string; everything below is from that build.
- Unity `2022.3.62` (UnityPlayer.dll). Build asset bundles with the same 2022.3 editor.
- Harmony ships as `Mods/0_TFP_Harmony/0Harmony.dll` (HarmonyX 2.13, `HarmonyLib` namespace).
- Almost all game types are in the global namespace. Unity types come from `UnityEngine`.

"Confirmed" means read in the decompiled source. Nothing here has been run in game yet.

---

## 0. Mod entry points

**`IModApi`** (global namespace) is the only entry interface:

```csharp
public interface IModApi { void InitMod(Mod _modInstance); }
```

The game instantiates every `IModApi` class in the mod's DLL. `Mod` exposes `Path` (absolute mod folder), `FolderName`, `Name` and `DisplayName`.

**`ModEvents`** (static class). Each event is a `ModEvent<TData>` with `RegisterHandler(ModEventHandlerDelegate<TData>)` and `UnregisterHandler(...)`. The delegate takes the data by `ref`:

```csharp
public delegate void ModEventHandlerDelegate<TData>(ref TData _data) where TData : struct;
```

`ModEventInterruptible<TData>` handlers return `EModEventResult` instead. The game catches and logs handler exceptions per receiver.

| Event | Data | Fired from | Use for |
| --- | --- | --- | --- |
| `GameAwake` | empty | startup | |
| `GameStarting` | `bool AsServer` | `GameManager` | |
| `CreateWorldDone` | empty | `GameManager` | world exists |
| `GameStartDone` | empty | `GameManager` | **start the director, do the SP check** |
| `GameUpdate` | empty | `GameManager.Update` (main thread, every frame) | **director tick** |
| `UnityUpdate` | empty | Unity update | |
| `WorldShuttingDown` | empty | `GameManager` | **stop and clean up** |
| `GameShutdown` | empty | | |
| `ServerRegistered` | | | |
| `PlayerLogin` (interruptible), `PlayerJoinedGame`, `PlayerSpawning`, `PlayerSpawnedInWorld`, `PlayerDisconnected`, `SavePlayerData` | | | `PlayerJoinedGame`: re-check single player |
| `GameMessage`, `ChatMessage` (interruptible) | | | |
| `CalcChunkColorsDone` | `Chunk Chunk` | `Chunk` map-colour pass | not a reliable load hook (see 3) |
| `EntityKilled` | | | |
| `GameFocus`, `MainMenuOpening` (interruptible), `MainMenuOpened` | | | |

```csharp
public class KaijuMod : IModApi
{
    public void InitMod(Mod mod)
    {
        new HarmonyLib.Harmony("kaiju.godzilla").PatchAll(System.Reflection.Assembly.GetExecutingAssembly());
        ModEvents.GameStartDone.RegisterHandler((ref ModEvents.SGameStartDoneData _) => Director.OnGameStart());
        ModEvents.GameUpdate.RegisterHandler((ref ModEvents.SGameUpdateData _) => Director.Tick());
        ModEvents.WorldShuttingDown.RegisterHandler((ref ModEvents.SWorldShuttingDownData _) => Director.Stop());
    }
}
```

Gotcha: the handlers take `ref` structs, so older-style `Action` lambdas (A21 and earlier) will not compile.

---

## 1. Server explosion: confirmed

`GameManager` (global), instance via `GameManager.Instance`:

```csharp
public void ExplosionServer(Vector3 _worldPos, Vector3i _blockPos, Quaternion _rotation,
    ExplosionData _explosionData, int _entityId, float _delay,
    bool _bRemoveBlockAtExplPosition, ItemValue _itemValueExplosionSource = null)
```

What it does on the server, with `_delay <= 0`, via the private `explode`:
1. `new Explosion(world, worldPos, blockPos, data, entityId)`.
2. `Explosion.AttackBlocks(entityId, itemValue)` raycasts outward from `blockPos` in a cube of radius `ceil(BlockRadius)`. Damage falls off linearly with distance and is divided by block hardness, scaled by `explosionResistance`. Blocks that reach `MaxDamage` downgrade through `DowngradeBlock` or become air. It only computes `ChangedBlockPositions`; nothing is written yet.
3. `Explosion.AttackEntites(...)` runs `Physics.OverlapSphere(worldPos - Origin.position, EntityRadius)` and calls `DamageEntity` with `EntityDamage`. It also kills dropped items in range.
4. Destroyed blocks that are `IsExplosionAffected` become `EntityFallingBlock` debris, spread over frames by `ExplodeGroupFrameUpdate`.
5. `ExplosionClient(...)` spawns the particle prefab (only if `ParticleIndex > 0`), applies physics force, then **writes the block changes** with `ChangeBlocks(null, changes)`, which runs the same path as `SetBlocksRPC` below. Clients get a `NetPackageExplosionClient`.
6. `_delay > 0` uses a coroutine.

**`ExplosionData`** (global) is a **struct** with these fields:
`int ParticleIndex; float Duration; float BlockRadius; int EntityRadius; int BlastPower; float EntityDamage; float BlockDamage; string BlockTags; bool IgnoreHeatMap; EnumDamageTypes DamageType; DamageMultiplier damageMultiplier; List<string> BuffActions;`

It has constructors `ExplosionData(DynamicProperties props, MinEffectController effects = null)`, which reads an `<property class="Explosion">` block, and `ExplosionData(byte[])`.

Which parameter damages what:
- Blocks: `BlockRadius`, `BlockDamage`, `BlockTags` + `damageMultiplier`.
- Entities: `EntityRadius`, `EntityDamage`, `DamageType`, `BuffActions`.
- `BlastPower` is visual/physics force only.

```csharp
var data = new ExplosionData {
    ParticleIndex = 0,              // 0 = no FX prefab
    BlockRadius = 8f, BlockDamage = 5000f,
    EntityRadius = 0, EntityDamage = 0f,   // we kill players ourselves
    BlastPower = 100,
    BlockTags = string.Empty,       // REQUIRED, see gotchas
    DamageType = EnumDamageTypes.Heat,
};
GameManager.Instance.ExplosionServer(center, World.worldToBlockPos(center), Quaternion.identity,
    data, -1, 0f, false);
```

Gotchas:
- **`new ExplosionData()` leaves `BlockTags` null.** `AttackBlocks` calls `BlockTags.Length` and throws a NullReferenceException. Always set it to `string.Empty`.
- `damageMultiplier == null` is safe: the multipliers are skipped.
- `AttackBlocks` **skips** blocks inside trader areas (when `SandboxUseTraderArea` is default), blocks where `InBoundsForPlayersPercent < 0.5` (map edge), water, and `StabilityIgnore` blocks. Damage is divided by the land-claim hardness modifier, so land claims resist.
- If the epicentre is a terrain surface block, `blockPos.y++`.
- If the entity id is -1, there is no attacker scaling (`GetBlockDamageScale`) and no perk effects. That is what we want.
- Every destroyed block can spawn a falling-block **entity**. At Godzilla scale this is the main cost; prefer direct `SetBlocksRPC` for bulk levelling and use explosions sparingly for spectacle.
- Changes to unloaded chunks are dropped (see 2).

---

## 2. Batched block changes: confirmed

`GameManager`:

```csharp
public void SetBlocksRPC(List<BlockChangeInfo> _changes, PlatformUserIdentifierAbs _persistentPlayerId = null)
public void ChangeBlocks(PlatformUserIdentifierAbs persistentPlayerId, List<BlockChangeInfo> _blocksToChange) // local apply only
```

`SetBlocksRPC` calls `ChangeBlocks` locally, then sends one `NetPackageSetBlock` to clients (on the server) or to the server (on a client). `ChangeBlocks` wraps the whole list in `ChunkCluster.ChunkPosNeedsRegeneration_DelayedStart/Stop`, so remeshing is batched per call. That is the point of batching.

Single-block variants on `WorldBase` (`GameManager.Instance.World`): `SetBlockRPC(BlockChangeInfo)` and `SetBlockRPC(BlockValueRef, BlockValue[, sbyte density][, int changingEntityId])`.

**`BlockChangeInfo`** (global, class). Main constructors:

```csharp
BlockChangeInfo(BlockValueRef _bvRef, BlockValue _blockValue)                 // sets bChangeBlockValue = true
BlockChangeInfo(BlockValueRef _bvRef, BlockValue _blockValue, bool _updateLight)
BlockChangeInfo(BlockValueRef _bvRef, BlockValue _blockValue, sbyte _density)
BlockChangeInfo(BlockValueRef _bvRef, BlockValue _blockValue, sbyte _density, int _changedByEntityId)
BlockChangeInfo(BlockValueRef _bvRef, sbyte _density, bool _bForceDensityChange = false)
```

Fields: `blockValueRef, bChangeBlockValue, bChangeDamage, blockValue, bChangeDensity, bForceDensity, density, bUpdateLight, bChangeTexture, textureFull, changedByEntityId`.

**New in V3: `BlockValueRef`** (struct) replaces raw `Vector3i` in these APIs. It has implicit conversions both ways with `Vector3i` (and `PropRef`), so passing a `Vector3i` works.

```csharp
var changes = new List<BlockChangeInfo>(512);
foreach (Vector3i p in footprint)
    if (!world.GetBlock(p).isair)
        changes.Add(new BlockChangeInfo(p, BlockValue.Air, true));
GameManager.Instance.SetBlocksRPC(changes);
```

Behaviour and gotchas:
- **Setting air:** use `BlockValue.Air`. If the old density was solid (<0) and the new block is air, `ChangeBlocks` automatically sets the density to `MarchingCubes.DensityAir`, so terrain is carved correctly without setting density yourself.
- **Multi-blocks:** `Chunk.SetBlock` calls `Block.OnBlockRemoved`, which removes all children when the parent is removed (`multiBlockPos.RemoveChilds`) and the parent when a child is removed (`RemoveParentBlock`). Clearing either end is enough. Setting both is harmless but redundant.
- **Tile entities** (loot containers and similar) are removed when the block becomes air, and locks are force-released.
- **Unloaded chunks:** `ChunkCluster.SetBlock` returns early when `GetChunkSync` is null. Air writes only go to `DecoManager` (distant decorations). **Changes to unloaded chunks are silently lost.** This is why the devastation pass is needed (see 3).
- `bChangeDamage = true` entries are skipped if the block type changed meanwhile.
- `y <= 0` or `y >= 255` is ignored.
- Sleeping bags removed this way clear player spawn points.
- `QuestEventManager.BlockChanged` fires per block, so quest POIs react.
- Structural collapse is not triggered by `ChangeBlocks` itself. Stability and falling run in the game's own block-update path. Measure in game before relying on it.

---

## 3. Chunk-loaded hook and "is this chunk loaded": confirmed

There is **no ModEvent for chunk load**. Three options, best first:

**A. Harmony postfix on `Chunk.OnLoad(World _world)`** (recommended). `World.updateChunkAddedRemovedCallbacks()`, called from `World.OnUpdateTick` on the main thread, calls `chunk.OnLoad(this)` once the chunk is in the cache **and** `!chunk.NeedsDecoration`. So the chunk is fully generated and decorated, and you are on the main thread:

```csharp
[HarmonyPatch(typeof(Chunk), nameof(Chunk.OnLoad))]
static class ChunkOnLoadPatch
{
    static void Postfix(Chunk __instance, World _world)
    {
        if (_world.IsRemote()) return;
        Devastation.OnChunkLoaded(__instance);   // __instance.X / .Z are chunk coords; .Key is the long key
    }
}
```

**B. `IChunkCallback`** (no Harmony): implement `OnChunkAdded(Chunk)`, `OnChunkBeforeRemove(Chunk)` and `OnChunkBeforeSave(Chunk)`, then register with `world.ChunkCache.AddChunkCallback(cb)` (remove with `RemoveChunkCallback`). `World` itself uses this. **Gotchas:** `OnChunkAdded` runs inside `WorldChunkCache.AddChunkSync`, which may run off the main thread and **before decoration**. Only queue `chunk.Key` under a lock, then process it on `GameUpdate` once `!chunk.NeedsDecoration`. That is what `World` does.

**C. `ChunkCluster` events:** `OnChunkVisibleDelegates(long key, bool isDisplayed)` is about rendering, and `OnChunksFinishedLoadingDelegates` takes no chunk. Not suitable.

`ModEvents.CalcChunkColorsDone` fires from the map-colour pass, which is not guaranteed once per load. Avoid it.

**Is a chunk loaded?**

```csharp
World world = GameManager.Instance.World;
var c = world.GetChunkFromWorldPos(blockPos) as Chunk;      // WorldBase; null = not loaded
var c2 = world.GetChunkSync(World.toChunkXZ(x), World.toChunkXZ(z)) as Chunk;
bool area = world.IsChunkAreaLoaded(worldPos);              // the chunk(s) within ±8 blocks in x/z
bool cols = world.IsChunkAreaCollidersLoaded(worldPos);     // also colliders built
```

Gotcha: a chunk can be present but still `NeedsDecoration` or regenerating. Before bulk edits, also check `!chunk.NeedsDecoration`. `IsChunkAreaCollidersLoaded` is the stronger check.

---

## 4. Single-player detection: confirmed

- `SingletonMonoBehaviour<ConnectionManager>.Instance` exposes:
  - `bool IsServer`: `CurrentMode` is `Server` or `OfflineServer`.
  - `bool IsClient`.
  - `bool IsSinglePlayer => IsServer && ClientCount() == 0`.
  - `ProtocolManager.NetworkType CurrentMode`: `None`, `Client`, `Server` or `OfflineServer`.
  - `bool HasRunningServers`.
  - `int ClientCount()`.
- `GameManager.IsDedicatedServer` is a `static bool`.

**Gotcha:** `IsSinglePlayer` is also true for a **hosted** game that nobody has joined yet. A truly offline game runs as `NetworkType.OfflineServer`. That happens when the game's server is disabled (`EnumGamePrefs.ServerEnabled == false`; `XUiC_ContinueGame` passes `offline = !ServerEnabled`), or when the platform user is offline or lacks multiplayer permission.

```csharp
static bool IsTrueSinglePlayer()
{
    var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
    return !GameManager.IsDedicatedServer
        && cm.CurrentMode == ProtocolManager.NetworkType.OfflineServer;
}
// On GameStartDone: if (!IsTrueSinglePlayer()) { Log.Warning("[Kaiju] Single player only; event disabled."); return; }
```

Decision for us: either the strict check above, or a lenient `IsServer && !IsDedicatedServer && ClientCount() == 0` plus a re-check on `ModEvents.PlayerJoinedGame`. Strict is simpler and matches the design.

---

## 5. Visual-only GameObject in the world: confirmed (APIs), untested (rendering at range)

**Floating origin.** `Origin` (global MonoBehaviour):
- `static Vector3 Origin.position` is the current world offset. **Unity position = world position − `Origin.position`**, which is how vanilla does it, e.g. `Instantiate(prefab, _center - Origin.position, rot)` in `ExplosionClient`.
- The origin moves automatically when the local player strays about 260 m from it (`cAutoRepositionDistanceSq = 67600`), snapped to multiples of 16.
- On move, the game shifts everything registered with `static void Origin.Add(Transform t, int level)`, where level `-1` moves `t` itself, `0` moves its direct children and `n` recurses. Unregister with `Origin.Remove(t)`. It then fires `static Action<Vector3> Origin.OriginChanged` with the new origin.
- Simplest for us: the director recomputes the transform every frame anyway, so set `t.position = godzillaWorldPos - Origin.position` in `GameUpdate` and skip `Origin.Add`. If anything is parented under a static root, `Origin.Add(root, 0)` like `DistantTerrain` does.

**Loading from the mod's bundle.** `DataLoader.LoadAsset<T>(string uri)` (sync). The URI format is `#<bundle path>?<asset name>`.
- **The `@modfolder:` shorthand is only rewritten in XML.** `XmlPatcher` turns it into `@modfolder(<ModName>):`. From C#, use the explicit form, or the absolute `Mod.Path`:

```csharp
// KaijuMod is the <Name> in ModInfo.xml
var prefab = DataLoader.LoadAsset<GameObject>("#@modfolder(KaijuMod):Resources/godzilla.unity3d?Godzilla");
// or: DataLoader.LoadAsset<GameObject>("#" + mod.Path + "/Resources/godzilla.unity3d?Godzilla");
```

- `AssetBundleManager.LoadAssetBundle(name)` takes rooted paths as-is (relative paths resolve under `Data/Bundles/Standalone…`). It caches the bundle, matches file names case-insensitively, and logs an error if the file is missing. `DataLoader.PreloadBundle(uri)` warms it.

**Spawn and parent:**

```csharp
var go = Object.Instantiate(prefab);
go.name = "Kaiju_Visual";
Object.DontDestroyOnLoad(go);   // or destroy on WorldShuttingDown
foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false; // visual only
go.transform.SetPositionAndRotation(worldPos - Origin.position, rot);
```

Gotchas and unverified points:
- Do not make it an `Entity` (`EntityFactory` registers entities with `Origin.Add` and the network).
- Strip or disable colliders, otherwise player physics and `Explosion.AttackEntites` OverlapSphere tags interact with it. Keep the layer on a non-entity layer.
- **Unverified:** distance culling. Check the camera far clip versus the 6k map, fog, and whether LODGroup or `Renderer.allowOcclusionWhenDynamic = false` is needed. Test in game.
- Destroy it on `WorldShuttingDown`. `Origin.Cleanup()` resets the origin to zero between worlds.

---

## Still unconfirmed (needs an in-game test)

- Real per-frame cost of `SetBlocksRPC` with hundreds to thousands of changes, and of explosion falling-block entities.
- Whether structural-integrity collapse runs after `ChangeBlocks` removes supports.
- Visual model draw distance and fog.
- `EnumGamePrefs.ServerEnabled` default and its UI label in V3.3 (the `PropertyDecl` default looks like `false`).
- Exact in-game version string. The assembly says V 3.3.0 b17 while CLAUDE.md says V3.2.
