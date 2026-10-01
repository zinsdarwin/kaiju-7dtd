using System.Collections.Generic;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// Every call into the game's own assembly goes through this class, so verifying the mod
    /// against V3.2 means checking this one file.
    ///
    /// Calls tagged UNVERIFIED were written from memory of earlier game versions and have not
    /// been checked against the decompiled V3.2 Assembly-CSharp.dll yet. If one fails to compile
    /// or misbehaves, fix it here; nothing else in the mod touches the engine directly.
    /// </summary>
    public static class GameApi
    {
        /// <summary>The running world, or null in the main menu or while loading.</summary>
        public static World World
        {
            get
            {
                // UNVERIFIED: GameManager.Instance and GameManager.World.
                var gm = GameManager.Instance;
                return gm == null ? null : gm.World;
            }
        }

        /// <summary>
        /// True for a local single player game. The event is disabled in hosted and dedicated games.
        /// </summary>
        public static bool IsSinglePlayer()
        {
            // UNVERIFIED: GameManager.IsDedicatedServer (static) and ConnectionManager.IsSinglePlayer.
            if (GameManager.IsDedicatedServer)
                return false;
            var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
            return cm != null && cm.IsSinglePlayer;
        }

        /// <summary>Snapshot of the players in the world.</summary>
        public static List<EntityPlayer> Players(World world)
        {
            // UNVERIFIED: World.Players is a DictionaryList<int, EntityPlayer> with a public .list.
            return new List<EntityPlayer>(world.Players.list);
        }

        /// <summary>The local player, used by console commands to place test routes.</summary>
        public static EntityPlayer LocalPlayer(World world)
        {
            // UNVERIFIED: World.GetPrimaryPlayer().
            return world.GetPrimaryPlayer();
        }

        public static Vector3 Position(EntityPlayer player)
        {
            // UNVERIFIED: Entity.position is a public Vector3 field in world coordinates.
            return player.position;
        }

        /// <summary>Facing direction in degrees around the vertical axis (0 = +z, 90 = +x).</summary>
        public static float YawDegrees(EntityPlayer player)
        {
            // UNVERIFIED: Entity.rotation is a public Vector3 field whose y is the yaw in degrees.
            return player.rotation.y;
        }

        public static bool IsAlive(EntityPlayer player)
        {
            // UNVERIFIED: EntityAlive.IsDead().
            return player != null && !player.IsDead();
        }

        /// <summary>Kills the player outright. Crushing damage, far above any health pool.</summary>
        public static void Kill(EntityPlayer player)
        {
            // UNVERIFIED: DamageSource(EnumDamageSource, EnumDamageTypes) constructor and
            // EntityAlive.DamageEntity(DamageSource, int, bool, float). If god mode or a buff
            // blocks this, look for a direct kill such as SetDead / Kill(DamageResponse).
            var source = new DamageSource(EnumDamageSource.External, EnumDamageTypes.Crushing);
            player.DamageEntity(source, 100000, false, 1f);
        }

        /// <summary>Surface height (top terrain block) at a column.</summary>
        public static int TerrainHeight(World world, int x, int z)
        {
            // UNVERIFIED: World.GetHeight(int, int) returning the top terrain block's y.
            return world.GetHeight(x, z);
        }

        /// <summary>True if the chunk holding this column is loaded, so its blocks can be read and changed.</summary>
        public static bool IsChunkLoaded(World world, int x, int z)
        {
            // UNVERIFIED: World.GetChunkFromWorldPos(Vector3i) returns null for unloaded chunks.
            return world.GetChunkFromWorldPos(new Vector3i(x, 0, z)) != null;
        }

        /// <summary>
        /// True if the block at this position should be destroyed: anything that is not air,
        /// terrain, or a child cell of a multi-block (the parent cell owns removal).
        /// </summary>
        public static bool IsDestructible(World world, int x, int y, int z)
        {
            // UNVERIFIED: World.GetBlock(Vector3i), BlockValue.isair, BlockValue.ischild,
            // BlockValue.Block.shape.IsTerrain().
            BlockValue bv = world.GetBlock(new Vector3i(x, y, z));
            if (bv.isair || bv.ischild)
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
            // UNVERIFIED: BlockChangeInfo(Vector3i, BlockValue, bool updateLight) constructor,
            // BlockValue.Air, and World.SetBlocksRPC(List<BlockChangeInfo>) as the batched,
            // server-side block change. This is the main thing to check for chunk remesh cost.
            var changes = new List<BlockChangeInfo>(positions.Count);
            foreach (var p in positions)
                changes.Add(new BlockChangeInfo(p, BlockValue.Air, true));
            world.SetBlocksRPC(changes);
        }

        /// <summary>
        /// Converts a world position to Unity scene space. The game shifts its scene origin as the
        /// player travels, so a plain GameObject must subtract the current origin.
        /// </summary>
        public static Vector3 WorldToScene(Vector3 worldPos)
        {
            // UNVERIFIED: static Origin.position holds the current floating-origin offset.
            return worldPos - Origin.position;
        }

        /// <summary>Writes a line to the F1 console, or the log when no console is available.</summary>
        public static void ConsoleOut(string line)
        {
            // UNVERIFIED: SingletonMonoBehaviour<SdtdConsole>.Instance.Output(string).
            var console = SingletonMonoBehaviour<SdtdConsole>.Instance;
            if (console != null)
                console.Output(line);
            else
                Log.Out(line);
        }
    }
}
