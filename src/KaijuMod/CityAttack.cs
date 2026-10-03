using System.Collections.Generic;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>A scripted atomic breath: fired when he has walked Distance metres along the route.</summary>
    public struct BreathEvent
    {
        public float Distance;
        public Vector3 Target; // world coordinates
    }

    /// <summary>
    /// How Godzilla attacks a coastal city: he rises from the sea offshore, walks in along the
    /// city's main road to its inland edge, crosses to the next street north and walks back out
    /// to sea along it, then sinks. Six breaths hit the rows he does not walk, so the whole city
    /// reads as destroyed rather than two trenches. Distances suit a 50-100 m Godzilla; the city
    /// layout (main road through the middle, streets every 78 m) comes from tools/snakemap.
    /// </summary>
    public class CityAttack
    {
        /// <summary>Street spacing in tools/snakemap settlements.</summary>
        public const float BlockSpacing = 78f;
        /// <summary>Metres offshore (from the map edge) where he rises and sinks.</summary>
        public const float OffshoreMargin = 70f;
        /// <summary>Breath damage radius multiplier for city attacks (beam ~20 m, crater ~61 m at 100 m tall).</summary>
        public static float BreathScale = 2.5f;
        /// <summary>Walking speed during an attack, m/s.</summary>
        public static float Speed = 10f;

        public Settlement City;
        public readonly List<Vector2> Route = new List<Vector2>();
        public readonly List<BreathEvent> Breaths = new List<BreathEvent>();

        public static CityAttack Plan(KaijuWorldData data, Settlement city)
        {
            var a = new CityAttack { City = city };
            float s = city.X < 0 ? -1f : 1f;           // sea side
            float c = city.X;
            float h = city.HalfWidth;
            float sea = s * (data.Size / 2f - OffshoreMargin);
            float inland = c - s * (h - 15f);
            float z0 = city.Z;                         // main road
            float z1 = city.Z + BlockSpacing;          // next street north
            float y = city.Y + 3f;

            a.Route.Add(new Vector2(sea, z0));
            a.Route.Add(new Vector2(inland, z0));
            a.Route.Add(new Vector2(inland, z1));
            a.Route.Add(new Vector2(sea, z1));

            float leg1 = Mathf.Abs(inland - sea);
            float leg2Start = leg1 + BlockSpacing;
            // Inbound along the main road: fire at the rows south of it.
            a.Add(Mathf.Abs((c + s * 0.8f * h) - sea), new Vector3(c + s * 0.2f * h, y, z0 - 1.5f * BlockSpacing));
            a.Add(Mathf.Abs(c - sea), new Vector3(c - s * 0.6f * h, y, z0 - 0.5f * BlockSpacing));
            a.Add(Mathf.Abs((c - s * 0.7f * h) - sea), new Vector3(c - s * 0.7f * h, y, z0 - 1.5f * BlockSpacing));
            // Outbound along the north street: fire at the rows north of it, then back across the middle.
            a.Add(leg2Start + Mathf.Abs((c - s * 0.5f * h) - inland), new Vector3(c - s * 0.1f * h, y, z1 + 0.5f * BlockSpacing));
            a.Add(leg2Start + Mathf.Abs((c + s * 0.3f * h) - inland), new Vector3(c + s * 0.8f * h, y, z1 + 0.5f * BlockSpacing));
            a.Add(leg2Start + Mathf.Abs((c + s * 0.9f * h) - inland), new Vector3(c + s * 0.5f * h, y, z0 - 0.5f * BlockSpacing));
            return a;
        }

        private void Add(float distance, Vector3 target)
        {
            Breaths.Add(new BreathEvent { Distance = distance, Target = target });
        }
    }
}
