using System.Collections.Generic;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// Destroys every non-terrain block inside Godzilla's footprint: a vertical cylinder around his
    /// position. Columns are scanned once as they enter the cylinder, and the blocks found are
    /// cleared over the following frames under fixed budgets, so a city does not vanish in one
    /// frame and stall the game.
    /// </summary>
    public class Footprint
    {
        /// <summary>Horizontal radius in metres (one block = one metre).</summary>
        public float Radius = 10f;
        /// <summary>Blocks cleared above his base height.</summary>
        public int Height = 51; // Godzilla Minus One
        /// <summary>Blocks cleared below his base height (foundations, shallow basements).</summary>
        public int Depth = 3;
        /// <summary>Block reads allowed per frame while scanning new columns.</summary>
        public int ReadBudget = 6000;
        /// <summary>Blocks set to air per frame. The main knob for frame rate and remeshing cost.</summary>
        public int ClearBudget = 250;

        private readonly HashSet<long> scanned = new HashSet<long>();
        private readonly Queue<Vector2Int> columnQueue = new Queue<Vector2Int>();
        private readonly HashSet<long> queuedColumns = new HashSet<long>();
        private readonly Queue<Vector3i> pendingClears = new Queue<Vector3i>();
        private readonly List<Vector3i> batch = new List<Vector3i>();
        private Vector2 lastPrunePos;

        public int PendingBlocks { get { return pendingClears.Count; } }
        public int PendingColumns { get { return columnQueue.Count; } }
        public long TotalCleared { get; private set; }

        public void Reset()
        {
            scanned.Clear();
            columnQueue.Clear();
            queuedColumns.Clear();
            pendingClears.Clear();
            TotalCleared = 0;
        }

        /// <summary>Called every frame with his current position and base height.</summary>
        public void Tick(World world, Vector2 center, int baseY)
        {
            EnqueueNewColumns(world, center);
            ScanColumns(world, center, baseY);
            ClearPending(world);
            PruneScanned(center);
        }

        private void EnqueueNewColumns(World world, Vector2 center)
        {
            int r = Mathf.CeilToInt(Radius);
            int cx = Mathf.FloorToInt(center.x);
            int cz = Mathf.FloorToInt(center.y);
            float r2 = Radius * Radius;
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    if (dx * dx + dz * dz > r2)
                        continue;
                    int x = cx + dx, z = cz + dz;
                    long key = Key(x, z);
                    if (scanned.Contains(key) || queuedColumns.Contains(key))
                        continue;
                    // Unloaded columns are left for the devastation pass (later milestone). Not
                    // marking them scanned means they are picked up if the chunk loads while he
                    // is still standing on it.
                    if (!GameApi.IsChunkLoaded(world, x, z))
                        continue;
                    columnQueue.Enqueue(new Vector2Int(x, z));
                    queuedColumns.Add(key);
                }
            }
        }

        private void ScanColumns(World world, Vector2 center, int baseY)
        {
            int reads = 0;
            int bottom = Mathf.Max(1, baseY - Depth); // never touch the bedrock layer at y = 0
            int top = Mathf.Min(253, baseY + Height);
            float pruneR2 = (Radius + 2f) * (Radius + 2f);
            while (columnQueue.Count > 0 && reads < ReadBudget)
            {
                Vector2Int col = columnQueue.Dequeue();
                long key = Key(col.x, col.y);
                queuedColumns.Remove(key);
                // He may have walked on before this column's turn came; skip columns he has left.
                if ((new Vector2(col.x, col.y) - center).sqrMagnitude > pruneR2)
                    continue;
                if (!GameApi.IsChunkLoaded(world, col.x, col.y))
                    continue;
                scanned.Add(key);
                // Top down, so upper floors go before the floors holding them up.
                for (int y = top; y >= bottom; y--)
                {
                    if (GameApi.IsDestructible(world, col.x, y, col.y))
                        pendingClears.Enqueue(new Vector3i(col.x, y, col.y));
                }
                reads += top - bottom + 1;
            }
        }

        private void ClearPending(World world)
        {
            batch.Clear();
            while (pendingClears.Count > 0 && batch.Count < ClearBudget)
                batch.Add(pendingClears.Dequeue());
            if (batch.Count == 0)
                return;
            GameApi.ClearBlocks(world, batch);
            TotalCleared += batch.Count;
        }

        /// <summary>Forgets columns far behind him so the set does not grow for the whole run.</summary>
        private void PruneScanned(Vector2 center)
        {
            if ((center - lastPrunePos).sqrMagnitude < 32f * 32f)
                return;
            lastPrunePos = center;
            float keepR2 = (Radius * 2f) * (Radius * 2f);
            scanned.RemoveWhere(k =>
            {
                int x = (int)(k >> 32);
                int z = (int)(k & 0xffffffffL);
                return (new Vector2(x, z) - center).sqrMagnitude > keepR2;
            });
        }

        private static long Key(int x, int z)
        {
            return ((long)x << 32) | (uint)z;
        }
    }
}
