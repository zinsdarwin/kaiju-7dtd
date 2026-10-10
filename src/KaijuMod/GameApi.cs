using System.Collections.Generic;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// Every call into the game's own assembly goes through this class, so verifying the mod
    /// against V3.2 means checking this one file.
    ///
    /// Calls tagged VERIFIED were checked against the decompiled V3.3.0 (b17) Assembly-CSharp.dll
    /// (see docs/engine-api.md). Calls tagged UNVERIFIED were written from memory of earlier game
    /// versions. If one fails to compile or misbehaves, fix it here; nothing else in the mod
    /// touches the engine directly.
    /// </summary>
    public static class GameApi
    {
        /// <summary>The running world, or null in the main menu or while loading.</summary>
        public static World World
        {
            get
            {
                // VERIFIED (V3.3): GameManager.Instance and GameManager.World (compiles against b17).
                var gm = GameManager.Instance;
                return gm == null ? null : gm.World;
            }
        }

        /// <summary>
        /// True for a local single player game. The event is disabled in hosted and dedicated games.
        /// </summary>
        public static bool IsSinglePlayer()
        {
            // VERIFIED (V3.3): ConnectionManager.IsSinglePlayer is also true for a hosted game that
            // nobody has joined yet, so check for an offline server instead.
            if (GameManager.IsDedicatedServer)
                return false;
            var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
            return cm != null && cm.CurrentMode == ProtocolManager.NetworkType.OfflineServer;
        }

        /// <summary>Snapshot of the players in the world.</summary>
        public static List<EntityPlayer> Players(World world)
        {
            // VERIFIED (V3.3): World.Players is a public DictionaryList<int, EntityPlayer> with .list.
            return new List<EntityPlayer>(world.Players.list);
        }

        /// <summary>The local player, used by console commands to place test routes.</summary>
        public static EntityPlayer LocalPlayer(World world)
        {
            // VERIFIED (V3.3): World.GetPrimaryPlayer() returns the EntityPlayerLocal.
            return world.GetPrimaryPlayer();
        }

        public static Vector3 Position(EntityPlayer player)
        {
            // VERIFIED (V3.3): Entity.position is a public Vector3 field in world coordinates.
            return player.position;
        }

        /// <summary>Facing direction in degrees around the vertical axis (0 = +z, 90 = +x).</summary>
        public static float YawDegrees(EntityPlayer player)
        {
            // VERIFIED (V3.3): Entity.rotation is a public Vector3 field. UNVERIFIED in game: y as yaw in degrees.
            return player.rotation.y;
        }

        public static bool IsAlive(EntityPlayer player)
        {
            // VERIFIED (V3.3): EntityAlive.IsDead().
            return player != null && !player.IsDead();
        }

        /// <summary>Kills the player outright. Crushing damage, far above any health pool.</summary>
        public static void Kill(EntityPlayer player)
        {
            // VERIFIED (V3.3): DamageSource(EnumDamageSource, EnumDamageTypes) constructor and
            // EntityAlive.DamageEntity(DamageSource, int, bool, float). DamageEntity returns -1
            // without damage while IsGodMode is on (debug god mode), so test with it off.
            var source = new DamageSource(EnumDamageSource.External, EnumDamageTypes.Crushing);
            player.DamageEntity(source, 100000, false, 1f);
        }

        /// <summary>Surface height (top terrain block) at a column.</summary>
        public static int TerrainHeight(World world, int x, int z)
        {
            // VERIFIED (V3.3): World.GetTerrainHeight(int, int) reads the chunk's terrain height map.
            // World.GetHeight includes buildings, trees and water, so the box would climb onto roofs.
            return world.GetTerrainHeight(x, z);
        }

        /// <summary>
        /// Ground height at a column anywhere on the map: the loaded chunk's terrain height, or the
        /// world heightmap where the chunk is not loaded (far from the player).
        /// </summary>
        public static float GroundHeight(World world, int x, int z)
        {
            // VERIFIED (V3.3): World.GetHeightAt(x, z) reads the terrain generator's heightmap
            // (TerrainFromRaw.GetTerrainHeightAt, world coordinates, whole map, no chunk needed).
            if (IsChunkLoaded(world, x, z))
                return TerrainHeight(world, x, z);
            return world.GetHeightAt(x, z);
        }

        /// <summary>True if the chunk holding this column is loaded, so its blocks can be read and changed.</summary>
        public static bool IsChunkLoaded(World world, int x, int z)
        {
            // VERIFIED (V3.3): World.GetChunkFromWorldPos(Vector3i) returns null for unloaded chunks.
            // Block changes sent to unloaded chunks are silently dropped, so this check matters.
            return world.GetChunkFromWorldPos(new Vector3i(x, 0, z)) != null;
        }

        /// <summary>
        /// True if the block at this position should be destroyed: anything that is not air or
        /// terrain. Clearing any cell of a multi-block removes the whole block (verified, V3.3).
        /// </summary>
        public static bool IsDestructible(World world, int x, int y, int z)
        {
            // VERIFIED (V3.3, compiles): World.GetBlock(Vector3i), BlockValue.isair,
            // BlockValue.Block.shape.IsTerrain().
            BlockValue bv = world.GetBlock(new Vector3i(x, y, z));
            if (bv.isair)
                return false;
            var block = bv.Block;
            if (block == null || block.shape == null)
                return false;
            // The mod's own blocks (part crates, the Oxygen Destroyer) survive him.
            // VERIFIED (V3.3): Block.GetBlockName().
            if (IsKaijuBlock(block))
                return false;
            return !block.shape.IsTerrain();
        }

        /// <summary>True for blocks this mod defines (all named kaiju*): Godzilla never destroys them.</summary>
        public static bool IsKaijuBlock(Block block)
        {
            string name = block.GetBlockName();
            return name != null && name.StartsWith("kaiju", System.StringComparison.Ordinal);
        }

        /// <summary>Name of the block at a position, or null for air or an unloaded chunk.</summary>
        public static string BlockName(World world, Vector3i pos)
        {
            // VERIFIED (V3.3): World.GetBlock, BlockValue.isair, Block.GetBlockName.
            if (!IsChunkLoaded(world, pos.x, pos.z))
                return null;
            BlockValue bv = world.GetBlock(pos);
            if (bv.isair || bv.Block == null)
                return null;
            return bv.Block.GetBlockName();
        }

        /// <summary>True if the position holds air (water counts as air: it is stored separately).</summary>
        public static bool IsAir(World world, Vector3i pos)
        {
            // VERIFIED (V3.3): World.GetBlock, BlockValue.isair.
            return world.GetBlock(pos).isair;
        }

        /// <summary>Places a block by name. Returns false if the name is unknown.</summary>
        public static bool PlaceBlock(World world, Vector3i pos, string blockName)
        {
            // VERIFIED (V3.3): Block.GetBlockValue(name) and WorldBase.SetBlockRPC(BlockValueRef, BlockValue)
            // (Vector3i converts to BlockValueRef). A CompositeTileEntity block creates its tile entity
            // (the loot container) when added. UNVERIFIED in game: the loot list fills on first open.
            BlockValue bv = Block.GetBlockValue(blockName);
            if (bv.isair)
                return false;
            world.SetBlockRPC(pos, bv);
            return true;
        }

        /// <summary>
        /// True if a placed POI has a "Rally" block, the quest start marker. Without one a quest
        /// there never shows its start marker (e.g. house_old_cottage_01_sleeper).
        /// </summary>
        public static bool HasRallyBlock(PrefabInstance poi)
        {
            // VERIFIED (V3.3): ObjectiveRallyPoint.GetRallyPosition looks for chunk.IndexedBlocks["Rally"]
            // inside the POI and gives up (no marker) if there is none. The prefab xml lists it under
            // IndexedBlockOffsets. Read the xml itself: Prefab.indexedBlockOffsets is empty at runtime
            // for world-placed prefabs (cleared once their blocks are copied into the chunks; checked
            // in game, 0.5.0 wrongly skipped prefabs that have a Rally block).
            // VERIFIED (V3.3): PrefabInstance.location / Prefab.location is the prefab's .tts
            // AbstractedLocation; FullPathNoExtension + ".xml" is its xml.
            if (poi == null)
                return true;
            var loc = poi.location;
            string path = loc.FullPathNoExtension;
            if (string.IsNullOrEmpty(path) && poi.prefab != null)
                path = poi.prefab.location.FullPathNoExtension;
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path + ".xml"))
                return true; // can't tell: assume it works
            try
            {
                return System.IO.File.ReadAllText(path + ".xml").Contains("class=\"Rally\"");
            }
            catch (System.Exception)
            {
                return true;
            }
        }

        /// <summary>
        /// True if the loot container at pos has been opened and no longer holds the item. False if
        /// it is untouched, still holds it, or is not a loot container (unknown).
        /// </summary>
        public static bool LootTakenFrom(World world, Vector3i pos, string itemName)
        {
            // VERIFIED (V3.3): World.GetTileEntity(Vector3i); loot chests are TileEntityComposite with a
            // TEFeatureStorage (GetFeature<T>); its itemGrid.Touched is set once opened (loot rolled),
            // and HasItem(ItemValue) checks the grid by item type.
            var te = world.GetTileEntity(pos) as TileEntityComposite;
            if (te == null)
                return false;
            var storage = te.GetFeature<TEFeatureStorage>();
            if (storage == null || storage.itemGrid == null || !storage.itemGrid.Touched)
                return false;
            ItemValue item = ItemClass.GetItem(itemName);
            if (item == null || item.IsEmpty())
                return false;
            return !storage.HasItem(item);
        }

        /// <summary>POIs placed in the world: name and bounding box (world coordinates, min corner).</summary>
        public static List<PrefabInstance> Pois()
        {
            // VERIFIED (V3.3): GameManager.GetDynamicPrefabDecorator().allPrefabs; PrefabInstance has
            // name, boundingBoxPosition and boundingBoxSize.
            var decorator = GameManager.Instance.GetDynamicPrefabDecorator();
            return decorator != null ? decorator.allPrefabs : new List<PrefabInstance>();
        }

        /// <summary>How many of an item the player carries (backpack and toolbelt).</summary>
        public static int ItemCount(EntityPlayer player, string itemName)
        {
            // VERIFIED (V3.3): ItemClass.GetItem(name), Entity.bag.GetItemCount, EntityAlive.inventory.GetItemCount.
            ItemValue item = ItemClass.GetItem(itemName);
            if (item == null || item.IsEmpty())
                return 0;
            int n = 0;
            if (player.bag != null)
                n += player.bag.GetItemCount(item);
            if (player.inventory != null)
                n += player.inventory.GetItemCount(item);
            return n;
        }

        /// <summary>Puts one of an item in the player's backpack. Returns false if it did not fit.</summary>
        public static bool GiveItem(EntityPlayer player, string itemName)
        {
            // VERIFIED (V3.3): Bag.AddItem(ItemStack). UNVERIFIED in game: the backpack UI refreshes.
            ItemValue item = ItemClass.GetItem(itemName);
            if (item == null || item.IsEmpty() || player.bag == null)
                return false;
            return player.bag.AddItem(new ItemStack(item, 1));
        }

        /// <summary>Takes count of an item from the player (backpack first, then toolbelt). False, taking nothing, if they have fewer.</summary>
        public static bool TakeItem(EntityPlayer player, string itemName, int count = 1)
        {
            // VERIFIED (V3.3): Bag.DecItem and Inventory.DecItem(ItemValue, count, ...) return the number removed.
            ItemValue item = ItemClass.GetItem(itemName);
            if (item == null || item.IsEmpty() || ItemCount(player, itemName) < count)
                return false;
            int left = count;
            if (player.bag != null)
                left -= player.bag.DecItem(item, left);
            if (left > 0 && player.inventory != null)
                left -= player.inventory.DecItem(item, left);
            return left <= 0;
        }

        /// <summary>A compass and map marker at a world position, using a nav_objects.xml class.</summary>
        public static NavObject AddMarker(string navClass, Vector3 worldPos)
        {
            // VERIFIED (V3.3): NavObjectManager.Instance.RegisterNavObject(class, Vector3 position);
            // the game passes world block positions (BlockUtilityNavIcon, POIWaypoint).
            return NavObjectManager.Instance != null ? NavObjectManager.Instance.RegisterNavObject(navClass, worldPos) : null;
        }

        public static void RemoveMarker(NavObject marker)
        {
            // VERIFIED (V3.3): NavObjectManager.UnRegisterNavObject(NavObject).
            if (marker != null && NavObjectManager.Instance != null)
                NavObjectManager.Instance.UnRegisterNavObject(marker);
        }

        /// <summary>Sets every listed position to air in one batched change.</summary>
        public static void ClearBlocks(World world, List<Vector3i> positions)
        {
            if (positions.Count == 0)
                return;
            // VERIFIED (V3.3): GameManager.SetBlocksRPC(List<BlockChangeInfo>) batches changes, and
            // BlockChangeInfo(BlockValueRef, BlockValue.Air, true) clears the block and its density
            // (Vector3i converts implicitly to BlockValueRef). See docs/engine-api.md.
            // Plain removal is preferred over GameManager.ExplosionServer, which can spawn a
            // falling-block entity per block. Remesh cost of large batches still needs an in-game test.
            var changes = new List<BlockChangeInfo>(positions.Count);
            foreach (var p in positions)
                changes.Add(new BlockChangeInfo(p, BlockValue.Air, true));
            GameManager.Instance.SetBlocksRPC(changes);
        }

        /// <summary>
        /// Converts a world position to Unity scene space. The game shifts its scene origin as the
        /// player travels, so a plain GameObject must subtract the current origin.
        /// </summary>
        public static Vector3 WorldToScene(Vector3 worldPos)
        {
            // VERIFIED (V3.3): Unity position = world position - Origin.position. The origin shifts
            // about every 260 m; the box is re-placed every frame, so it follows the shift.
            return worldPos - Origin.position;
        }

        /// <summary>World coordinates of a Unity scene position (inverse of WorldToScene).</summary>
        public static Vector3 SceneToWorld(Vector3 scenePos)
        {
            // VERIFIED (V3.3): see WorldToScene.
            return scenePos + Origin.position;
        }

        /// <summary>The player's view ray (crosshair) in world coordinates.</summary>
        public static Ray LookRay(EntityPlayer player)
        {
            // VERIFIED (V3.3): EntityAlive.GetLookRay() is virtual; EntityPlayerLocal overrides it with
            // the camera's centre ray, its origin already shifted to world coordinates (+ Origin.position).
            return player.GetLookRay();
        }

        /// <summary>First collider hit along a world-space ray (terrain, blocks, entities), or null.</summary>
        public static Vector3? Raycast(Vector3 worldOrigin, Vector3 direction, float maxDistance)
        {
            // UNVERIFIED in game: that chunk meshes carry colliders Physics.Raycast hits. Unity physics
            // works in scene space, so shift by Origin.position both ways. Triggers are ignored.
            RaycastHit hit;
            if (Physics.Raycast(worldOrigin - Origin.position, direction, out hit, maxDistance, ~0, QueryTriggerInteraction.Ignore))
                return hit.point + Origin.position;
            return null;
        }

        /// <summary>
        /// First solid cell along a world-space ray, from block data: any non-air block, or below the
        /// terrain surface. Works where Raycast misses: physics colliders only exist for chunks near
        /// the player, but block data is loaded much further out. Steps of half a metre; skips
        /// unloaded chunks. Returns null if nothing is hit within maxDistance.
        /// </summary>
        public static Vector3? BlockRaycast(World world, Vector3 worldOrigin, Vector3 direction, float maxDistance)
        {
            // VERIFIED (V3.3): World.GetBlock, World.GetTerrainHeight (see TerrainHeight), chunk check.
            Vector3 dir = direction.normalized;
            for (float d = 2f; d <= maxDistance; d += 0.5f)
            {
                Vector3 p = worldOrigin + dir * d;
                int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
                if (y < 0 || y > 254)
                    continue;
                if (!IsChunkLoaded(world, x, z))
                {
                    // No block data out here: hit the ground from the world heightmap.
                    if (p.y <= world.GetHeightAt(x, z))
                        return p;
                    continue;
                }
                if (p.y <= TerrainHeight(world, x, z) || !world.GetBlock(new Vector3i(x, y, z)).isair)
                    return p;
            }
            return null;
        }

        /// <summary>
        /// Loads a prefab from a Unity asset bundle in the mod's Resources folder, or null if the
        /// file is missing. bundleFile is relative to the mod folder, e.g. "Resources/kaiju.unity3d".
        /// </summary>
        public static GameObject LoadModPrefab(string bundleFile, string assetName)
        {
            return LoadModAsset<GameObject>(bundleFile, assetName);
        }

        /// <summary>Loads any asset (prefab, material, ...) from a bundle in the mod folder, or null if the file is missing.</summary>
        public static T LoadModAsset<T>(string bundleFile, string assetName) where T : Object
        {
            // Check first: AssetBundleManager logs an error for a missing file, and the model is
            // optional (it stays local and is never committed).
            string dir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (!System.IO.File.Exists(System.IO.Path.Combine(dir ?? ".", bundleFile)))
                return null;
            // VERIFIED (V3.3): DataLoader.LoadAsset<T>("#<bundle>?<asset>"); C# needs the
            // #@modfolder(ModName): form, plain @modfolder: only works in XML.
            return DataLoader.LoadAsset<T>("#@modfolder(KaijuMod):" + bundleFile + "?" + assetName);
        }

        /// <summary>Folder of the world being played (where kaiju.xml sits), or null.</summary>
        public static string WorldFolder()
        {
            // VERIFIED (V3.3): the game resolves a world's folder this way itself
            // (BiomeIntensityMap, WorldBiomeProviderFromImage, ChunkProviderDisc).
            string name = GamePrefs.GetString(EnumGamePrefs.GameWorld);
            if (string.IsNullOrEmpty(name))
                return null;
            var location = PathAbstractions.WorldsSearchPaths.GetLocation(name);
            return location.Type == PathAbstractions.EAbstractedLocationType.None ? null : location.FullPath;
        }

        /// <summary>Folder of the current save game, for the mod's own state file.</summary>
        public static string SaveFolder()
        {
            // VERIFIED (V3.3): GameIO.GetSaveGameDir() = Saves/<GameWorld>/<GameName> for the current game.
            return GameIO.GetSaveGameDir();
        }

        /// <summary>In-game time in ticks: 1000 per hour, 24000 per day; day 1 starts at 0.</summary>
        public static ulong WorldTime(World world)
        {
            // VERIFIED (V3.3): World.worldTime; GameUtils.WorldTimeToDays = time / 24000 + 1.
            return world.worldTime;
        }

        public static int Day(ulong worldTime)
        {
            // VERIFIED (V3.3)
            return GameUtils.WorldTimeToDays(worldTime);
        }

        public static int Hour(ulong worldTime)
        {
            // VERIFIED (V3.3)
            return GameUtils.WorldTimeToHours(worldTime);
        }

        public static ulong DayTimeToWorldTime(int day, int hour, int minute)
        {
            // VERIFIED (V3.3)
            return GameUtils.DayTimeToWorldTime(day, hour, minute);
        }

        /// <summary>Day of the next (or current) blood moon.</summary>
        public static int BloodMoonDay()
        {
            // VERIFIED (V3.3): AIDirectorBloodMoonComponent reads GameStats BloodMoonDay and moves it
            // to the next blood moon once the current one ends.
            return GameStats.GetInt(EnumGameStats.BloodMoonDay);
        }

        /// <summary>Dusk and dawn hours (Item1, Item2) for the game's day length setting.</summary>
        public static (int, int) DuskDawn()
        {
            // VERIFIED (V3.3): World.DuskDawnInit uses CalcDuskDawnHours(GameStats DayLightLength).
            return GameUtils.CalcDuskDawnHours(GameStats.GetInt(EnumGameStats.DayLightLength));
        }

        /// <summary>True from dusk on the blood moon day until dawn the next day: the horde night.</summary>
        public static bool IsBloodMoonNow(World world)
        {
            // VERIFIED (V3.3): the same test AIDirectorBloodMoonComponent and DayTimeTracker use.
            return GameUtils.IsBloodMoonTime(world.worldTime, DuskDawn(), BloodMoonDay());
        }

        /// <summary>Shows an on-screen tooltip to the local player.</summary>
        public static void Tooltip(EntityPlayer player, string text)
        {
            // VERIFIED (V3.3): GameManager.ShowTooltip(EntityPlayerLocal, string, ...) queues a popup.
            // UNVERIFIED in game: that plain text (not a localization key) is shown as is.
            var local = player as EntityPlayerLocal;
            if (local != null)
                GameManager.ShowTooltip(local, text);
        }

        /// <summary>Radiation damage from the front.</summary>
        public static void RadiationDamage(EntityPlayer player, int amount)
        {
            // VERIFIED (V3.3): EnumDamageTypes.Radiation exists; same DamageEntity call as Kill.
            // UNVERIFIED in game: how much armour or radiation resistance reduces it.
            var source = new DamageSource(EnumDamageSource.External, EnumDamageTypes.Radiation);
            player.DamageEntity(source, amount, false, 0f);
        }

        /// <summary>
        /// Forces the fog colour and density (fallout haze); the game fades toward it over a few
        /// seconds. ClearFog hands fog back to the weather.
        /// </summary>
        public static void SetFog(World world, Color color, float density)
        {
            // VERIFIED (V3.3): WorldEnvironment.SetFogOverride(Color, float); WorldEnvironment.Update
            // uses fogColorOverride/fogDensityOverride when density >= 0 and lerps the fog toward
            // them by 0.01 a frame. World.m_WorldEnvironment holds the instance.
            if (world != null && world.m_WorldEnvironment != null)
                world.m_WorldEnvironment.SetFogOverride(color, density);
        }

        public static void ClearFog(World world)
        {
            // VERIFIED (V3.3): density -1 turns the override off.
            if (world != null && world.m_WorldEnvironment != null)
                world.m_WorldEnvironment.SetFogOverride(default(Color), -1f);
        }

        /// <summary>Loads a vanilla audio clip by its sounds.xml ClipName (e.g. "@:Sounds/Explosions/explosion1.wav").</summary>
        public static AudioClip LoadAudioClip(string clipName)
        {
            // VERIFIED (V3.3): Audio.Manager loads clips with DataLoader.LoadAsset<AudioClip>(ClipName),
            // where "@:" names an Addressables asset (DataLoader.ParseDataPathIdentifier).
            return DataLoader.LoadAsset<AudioClip>(clipName);
        }

        /// <summary>Plays a sound from sounds.xml in the player's head (e.g. "buff_geiger_counter").</summary>
        public static void PlaySound(EntityPlayer player, string soundName)
        {
            // VERIFIED (V3.3): Audio.Manager.PlayInsidePlayerHead(name, entityId); buff_geiger_counter
            // is a vanilla SoundDataNode (three geiger clips).
            if (player != null)
                Audio.Manager.PlayInsidePlayerHead(soundName, player.entityId);
        }

        /// <summary>Writes a line to the F1 console, or the log when no console is available.</summary>
        public static void ConsoleOut(string line)
        {
            // VERIFIED (V3.3): SdtdConsole.Output(string) only appends to the output of the command
            // being executed, so lines written outside a console command are not shown. Use Log.Out there.
            var console = SingletonMonoBehaviour<SdtdConsole>.Instance;
            if (console != null)
                console.Output(line);
            else
                Log.Out(line);
        }
    }
}
