using System.Collections.Generic;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// Moves Godzilla along his route as plain data: a position on a polyline, advanced every
    /// frame by speed * time. No entity, no pathfinding. Each frame it also drives the footprint
    /// destruction, the kill zone and his visual.
    /// </summary>
    public class KaijuDirector
    {
        public static readonly KaijuDirector Instance = new KaijuDirector();

        /// <summary>Walking speed in metres per real second. Tunable with `kaiju speed`.</summary>
        public float Speed = 10f;
        /// <summary>How fast his base height follows the ground, in metres per second.</summary>
        public float ClimbRate = 6f;

        public readonly Footprint Footprint = new Footprint();
        private readonly KaijuVisual visual = new KaijuVisual();
        private readonly KaijuBreath breath;
        // Direction he faces. Follows the route heading, except while breathing, when he turns
        // toward the target (degrees per second).
        private Vector2 facing = Vector2.up;
        public float TurnRate = 30f;

        public KaijuDirector()
        {
            breath = new KaijuBreath(visual);
        }

        public bool Breathing { get { return breath.Active; } }

        /// <summary>An effect material from the model's bundle (KaijuSmoke, KaijuSpark, KaijuBeam).</summary>
        public Material EffectMaterial(string name)
        {
            return visual.EffectMaterial(name);
        }
        public long BreathCleared { get { return breath.TotalCleared; } }

        private List<Vector2> route = new List<Vector2>();
        private int segment;
        // City attacks: scripted breaths by distance walked, rising from and sinking into the sea.
        private readonly List<BreathEvent> events = new List<BreathEvent>();
        private int nextEvent;
        private float breathScale = 1f;
        private float traveled, routeLength;
        private bool fromSea;
        private float normalSpeed = -1f;
        /// <summary>Metres walked while rising out of the sea at the start (and sinking at the end) of an attack.</summary>
        public float EmergeDistance = 90f;
        /// <summary>Name of the city being attacked, or null.</summary>
        public string Attacking { get; private set; }
        // Death sequence (Oxygen Destroyer): he stops, the water boils, he sinks and is gone.
        private bool dying;
        private float dieTimer;
        /// <summary>Seconds the death sequence takes.</summary>
        public float DeathTime = 12f;
        public bool Dying { get { return dying; } }
        // Roars: on rising from the sea, before the first breath of an attack, every 30-60 s while
        // walking, and on death.
        private bool roaredRise, roaredFirstBreath;
        private float nextRoar;
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
            traveled = 0f;
            routeLength = 0f;
            for (int i = 1; i < route.Count; i++)
                routeLength += (route[i] - route[i - 1]).magnitude;
            position = route[0];
            heading = (route[1] - route[0]).normalized;
            facing = heading;
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
                Log.Out("[KaijuMod] Stopped at " + position + ", " + Footprint.TotalCleared + " blocks cleared"
                    + (Attacking != null ? " (attack on " + Attacking + ")" : ""));
            Running = false;
            dying = false;
            roaredRise = roaredFirstBreath = false;
            breath.Cancel();
            visual.Hide();
            events.Clear();
            nextEvent = 0;
            breathScale = 1f;
            fromSea = false;
            Attacking = null;
            if (normalSpeed > 0f)
                Speed = normalSpeed;
            normalSpeed = -1f;
        }

        /// <summary>
        /// A city attack: rises from the sea at the route's start, walks it at the attack speed,
        /// fires the scripted breaths, and sinks back into the sea at its end.
        /// </summary>
        public bool StartAttack(CityAttack plan, float seaLevel, out string error)
        {
            if (!Start(plan.Route, out error))
                return false;
            events.AddRange(plan.Breaths);
            events.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            nextEvent = 0;
            breathScale = CityAttack.BreathScale;
            fromSea = true;
            Attacking = plan.City.Name;
            roaredRise = roaredFirstBreath = false;
            nextRoar = Time.time + Random.Range(30f, 60f);
            normalSpeed = Speed;
            Speed = CityAttack.Speed;
            // Offshore chunks are usually not loaded yet; start on the seabed rather than at y = 0.
            baseY = seaLevel - 15f;
            haveBaseY = true;
            return true;
        }

        public void Tick(float dt)
        {
            if (!Running)
            {
                // Blast craters from his last breath keep clearing after he has gone.
                World w = GameApi.World;
                if (w != null)
                    breath.Tick(w, dt);
                return;
            }
            World world = GameApi.World;
            if (world == null)
            {
                // Left the game: drop the event rather than carry it into the next world.
                Stop();
                return;
            }

            if (dying)
            {
                TickDeath(world, dt);
                return;
            }

            // He stands still while breathing.
            if (!breath.Active)
                Advance(dt);
            if (fromSea && !roaredRise && traveled >= EmergeDistance * 0.6f)
            {
                roaredRise = true;
                Roar();
            }
            if (!breath.Active && Time.time >= nextRoar)
            {
                nextRoar = Time.time + Random.Range(30f, 60f);
                Roar();
            }
            if (!breath.Active && nextEvent < events.Count && traveled >= events[nextEvent].Distance)
            {
                if (!roaredFirstBreath)
                {
                    roaredFirstBreath = true;
                    Roar();
                }
                breath.Begin(events[nextEvent].Target, Footprint.Height, breathScale);
                nextEvent++;
            }
            UpdateBaseY(world, dt);
            UpdateFacing(dt);
            breath.Tick(world, dt);

            int y = Mathf.RoundToInt(baseY);
            Footprint.Tick(world, position, y);
            KillPlayersInside(world);
            visual.Place(new Vector3(position.x, baseY - Submerged() * Footprint.Height * 0.95f, position.y), facing, Footprint.Radius * 2f, Footprint.Height);

            if (segment >= route.Count - 1 && !breath.Active)
            {
                Log.Out("[KaijuMod] Reached the last waypoint");
                Stop();
            }
        }

        /// <summary>After the walk animation has run: aim the head and draw the breath on the mouth.</summary>
        public void LateTick()
        {
            if (Running)
                breath.LateTick();
        }

        /// <summary>Atomic breath at a world-space point. He stops, turns to face it, charges and fires.</summary>
        public bool Breathe(Vector3 worldTarget, out string error)
        {
            error = null;
            if (!Running)
            {
                error = "He isn't out. Use kaiju test or kaiju start first.";
                return false;
            }
            breath.Begin(worldTarget, Footprint.Height);
            return true;
        }

        /// <summary>He roars from his mouth (also `kaiju roar`). False if he is not out.</summary>
        public bool Roar()
        {
            if (!Running)
                return false;
            KaijuAudio.Roar(visual.MouthWorld());
            return true;
        }

        /// <summary>
        /// The Oxygen Destroyer has gone off: he stops where he is, a flash and boiling bubbles
        /// surround him, and he sinks into the ground over DeathTime seconds, then is gone.
        /// </summary>
        public void Die(Vector3 deviceWorldPos)
        {
            if (!Running || dying)
                return;
            dying = true;
            dieTimer = 0f;
            breath.Cancel();
            float h = Footprint.Height;
            KaijuEffects.Flash(deviceWorldPos + Vector3.up * 3f, h * 15f, 9f, 2.5f, new Color(0.8f, 0.95f, 1f));
            KaijuEffects.Bubbles(new Vector3(position.x, baseY, position.y), h, DeathTime, visual.EffectMaterial("KaijuSpark"));
            Roar();
            Log.Out("[KaijuMod] Oxygen Destroyer: Godzilla is dying at " + position);
        }

        private void TickDeath(World world, float dt)
        {
            dieTimer += dt;
            float k = Mathf.Clamp01(dieTimer / DeathTime);
            // A last stagger, then a slow collapse into the ground.
            float sink = k * k * (3f - 2f * k);
            visual.Place(new Vector3(position.x, baseY - sink * Footprint.Height * 1.05f, position.y), facing, Footprint.Radius * 2f, Footprint.Height);
            visual.SetPlateGlow(Mathf.Max(0f, 3f * (1f - k)) * (0.5f + 0.5f * Mathf.Sin(dieTimer * 9f)));
            if (k >= 1f)
            {
                Log.Out("[KaijuMod] Godzilla is dead");
                Stop();
            }
        }

        /// <summary>How far under the sea he is during an attack: 1 = fully, 0 = standing on the ground.</summary>
        private float Submerged()
        {
            if (!fromSea)
                return 0f;
            float rise = Mathf.Clamp01(traveled / EmergeDistance);
            float sink = Mathf.Clamp01((routeLength - traveled) / EmergeDistance);
            float up = Mathf.Min(rise, sink);
            return 1f - up * up * (3f - 2f * up);
        }

        private void UpdateFacing(float dt)
        {
            Vector2 want = heading;
            Vector2? target = breath.FacingTarget;
            if (target.HasValue)
            {
                Vector2 d = target.Value - position;
                if (d.sqrMagnitude > 1f)
                    want = d.normalized;
            }
            float a = Vector2.SignedAngle(facing, want);
            float step = TurnRate * dt;
            facing = Mathf.Abs(a) <= step ? want : (Vector2)(Quaternion.Euler(0f, 0f, Mathf.Sign(a) * step) * facing);
            facing.Normalize();
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
                    traveled += dist;
                }
                else
                {
                    position += heading * remaining;
                    traveled += remaining;
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
