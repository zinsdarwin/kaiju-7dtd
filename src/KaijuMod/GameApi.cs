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
            return !block.shape.IsTerrain();
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
