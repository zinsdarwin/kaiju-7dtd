using System.Collections.Generic;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// Moves Godzilla along his route as plain data: a position on a polyline, advanced every
    /// frame by speed * time. No entity, no pathfinding. Each frame it also drives the footprint
    /// destruction, the kill zone and the grey box visual.
    /// </summary>
    public class KaijuDirector
    {
        public static readonly KaijuDirector Instance = new KaijuDirector();

        /// <summary>Walking speed in metres per real second. Tunable with `kaiju speed`.</summary>
        public float Speed = 3f;
        /// <summary>How fast his base height follows the ground, in metres per second.</summary>
        public float ClimbRate = 6f;

        public readonly Footprint Footprint = new Footprint();
        private readonly GreyBox visual = new GreyBox();

        private List<Vector2> route = new List<Vector2>();
        private int segment;
        private Vector2 position;
        private Vector2 heading;
        private float baseY;
        private bool haveBaseY;
        // Players already reported as crushed this run, so a failed kill (god mode) logs once.
        private readonly HashSet<EntityPlayer> crushed = new HashSet<EntityPlayer>();

        public bool Running { get; private set; }
        public Vector2 Position { get { return position; } }
        public float BaseY { get { return baseY; } }
        public int Segment { get { return segment; } }
        public int WaypointCount { get { return route.Count; } }

        /// <summary>Starts walking from the first point of the given route.</summary>
        public bool Start(List<Vector2> points, out string error)
        {
            error = null;
            if (!GameApi.IsSinglePlayer())
            {
                error = "Kaiju event is single player only; disabled in hosted and dedicated games.";
                Log.Warning("[KaijuMod] " + error);
                return false;
            }
            if (GameApi.World == null)
            {
                error = "No world loaded.";
                return false;
            }
            if (points == null || points.Count < 2)
            {
                error = "Route needs at least two points.";
                return false;
            }
            Stop();
            route = new List<Vector2>(points);
            segment = 0;
            position = route[0];
            heading = (route[1] - route[0]).normalized;
            haveBaseY = false;
            Footprint.Reset();
            crushed.Clear();
            visual.Show(Footprint.Radius * 2f, Footprint.Height);
            Running = true;
            Log.Out("[KaijuMod] Walking " + route.Count + " waypoints from " + position + " at " + Speed + " m/s");
            return true;
        }

        public void Stop()
        {
            if (Running)
                Log.Out("[KaijuMod] Stopped at " + position + ", " + Footprint.TotalCleared + " blocks cleared");
            Running = false;
            visual.Hide();
        }

        public void Tick(float dt)
        {
            if (!Running)
                return;
            World world = GameApi.World;
            if (world == null)
            {
                // Left the game: drop the event rather than carry it into the next world.
                Stop();
                return;
            }

            Advance(dt);
            UpdateBaseY(world, dt);

            int y = Mathf.RoundToInt(baseY);
            Footprint.Tick(world, position, y);
            KillPlayersInside(world);
            visual.Place(new Vector3(position.x, baseY, position.y), heading);

            if (segment >= route.Count - 1)
            {
                Log.Out("[KaijuMod] Reached the last waypoint");
                Stop();
            }
        }

        private void Advance(float dt)
        {
            float remaining = Speed * dt;
            while (remaining > 0f && segment < route.Count - 1)
            {
                Vector2 to = route[segment + 1];
                Vector2 delta = to - position;
                float dist = delta.magnitude;
                if (dist > 0.0001f)
                    heading = delta / dist;
                if (dist <= remaining)
                {
                    position = to;
                    segment++;
                    remaining -= dist;
                }
                else
                {
                    position += heading * remaining;
                    remaining = 0f;
                }
            }
        }

        /// <summary>
        /// Follows the terrain under his centre (buildings excluded), rate-limited so steep ground
        /// does not make the box jump.
        /// </summary>
        private void UpdateBaseY(World world, float dt)
        {
            int x = Mathf.FloorToInt(position.x);
            int z = Mathf.FloorToInt(position.y);
            if (!GameApi.IsChunkLoaded(world, x, z))
                return; // keep the last known height while walking through unloaded ground
            float ground = GameApi.TerrainHeight(world, x, z);
            if (!haveBaseY)
            {
                baseY = ground;
                haveBaseY = true;
                return;
            }
            baseY = Mathf.MoveTowards(baseY, ground, ClimbRate * dt);
        }

        private void KillPlayersInside(World world)
        {
            float r = Footprint.Radius + 1f;
            float r2 = r * r;
            float bottom = baseY - Footprint.Depth - 2f;
            float top = baseY + Footprint.Height;
            foreach (EntityPlayer player in GameApi.Players(world))
            {
                if (!GameApi.IsAlive(player))
                {
                    crushed.Remove(player); // log again if they respawn and walk back in
                    continue;
                }
                Vector3 p = GameApi.Position(player);
                float dx = p.x - position.x, dz = p.z - position.y;
                if (dx * dx + dz * dz > r2 || p.y < bottom || p.y > top)
                    continue;
                if (crushed.Add(player))
                    Log.Out("[KaijuMod] Crushed player at " + p);
                GameApi.Kill(player);
            }
        }
    }
}
