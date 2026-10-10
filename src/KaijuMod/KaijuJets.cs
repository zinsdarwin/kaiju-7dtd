using System.Collections.Generic;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// The airstrike from the radio above Dune Point: three jets in a V come in low from the sea,
    /// carpet-bomb a line of explosions across him, turn and make a second pass from inland. Visual
    /// only (no blocks or players harmed); he roars and stops for a few seconds.
    /// </summary>
    public class KaijuJets : MonoBehaviour
    {
        /// <summary>Jet speed m/s, height above him, and how far out a pass starts, metres.</summary>
        public static float Speed = 200f, Altitude = 180f, RunIn = 2500f;
        /// <summary>Seconds between the first and second pass.</summary>
        public static float PassGap = 22f;
        /// <summary>Seconds he stops when the bombs hit him.</summary>
        public static float Stagger = 4f;

        private float seaSign, t;
        private bool second;

        /// <summary>Calls the strike in: the first pass comes from the sea side (seaSign: +1 east, -1 west).</summary>
        public static void Strike(float seaSign)
        {
            var go = new GameObject("KaijuAirstrike");
            Object.DontDestroyOnLoad(go);
            var s = go.AddComponent<KaijuJets>();
            s.seaSign = seaSign;
            s.Pass(-seaSign);
        }

        private void Update()
        {
            t += Time.deltaTime;
            if (!second && t >= PassGap)
            {
                second = true;
                Pass(seaSign);
            }
            if (t > PassGap + 30f)
                Destroy(gameObject);
        }

        /// <summary>One pass of three jets flying along x in direction dir (+1 east, -1 west) over him.</summary>
        private void Pass(float dir)
        {
            var d = KaijuDirector.Instance;
            if (!d.Running || d.Dying)
                return;
            float y = d.BaseY + d.Footprint.Height + Altitude;
            var start = new Vector3(d.Position.x - dir * RunIn, y, d.Position.y);
            Jet.Create(start, dir, 0f);
            Jet.Create(start + new Vector3(-dir * 30f, 4f, -28f), dir, 0.15f);
            Jet.Create(start + new Vector3(-dir * 30f, 4f, 28f), dir, 0.3f);
        }
    }

    public class Jet : WorldAnchored
    {
        private float dir, bombDelay;
        private bool bombed;

        public static void Create(Vector3 worldPos, float dir, float bombDelay)
        {
            var go = new GameObject("KaijuJet");
            Object.DontDestroyOnLoad(go);
            var j = go.AddComponent<Jet>();
            j.worldPos = worldPos;
            j.dir = dir;
            j.bombDelay = bombDelay;
            j.LateUpdate();
            go.transform.rotation = Quaternion.LookRotation(new Vector3(dir, 0f, 0f));
            j.Build();
            KaijuAudio.Loop(go, KaijuAudio.JetClip, 1.6f, 1f, 2500f);
            Destroy(go, 2f * KaijuJets.RunIn / KaijuJets.Speed + 2f);
        }

        /// <summary>A plain grey jet from primitives: fuselage, swept wings, tailplane, fin; an afterburner.</summary>
        private void Build()
        {
            var grey = new Color(0.32f, 0.34f, 0.36f);
            Part(PrimitiveType.Capsule, new Vector3(0f, 0f, 0f), new Vector3(1.6f, 7f, 1.6f), new Vector3(90f, 0f, 0f), grey);
            Part(PrimitiveType.Cube, new Vector3(0f, 0f, -0.5f), new Vector3(11f, 0.25f, 3.2f), new Vector3(0f, 0f, 0f), grey);
            Part(PrimitiveType.Cube, new Vector3(0f, 0f, -6f), new Vector3(4.5f, 0.2f, 1.6f), Vector3.zero, grey);
            Part(PrimitiveType.Cube, new Vector3(0f, 1.3f, -6f), new Vector3(0.2f, 2.4f, 1.8f), Vector3.zero, grey);
            var spark = KaijuDirector.Instance.EffectMaterial("KaijuSpark");
            var burner = new GameObject("Afterburner");
            burner.transform.SetParent(transform, false);
            burner.transform.localPosition = new Vector3(0f, 0f, -7.2f);
            if (spark != null)
            {
                var ps = KaijuEffects.Particles(burner.transform, "Flame", spark, 400, false);
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.35f), new Color(1f, 0.4f, 0.15f));
                var emission = ps.emission;
                emission.rateOverDistance = 0.6f;
                ps.Play();
            }
            var light = burner.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.3f);
            light.range = 18f;
            light.intensity = 3f;
            light.shadows = LightShadows.None;
        }

        private void Part(PrimitiveType type, Vector3 pos, Vector3 scale, Vector3 euler, Color color)
        {
            var p = GameObject.CreatePrimitive(type);
            Object.Destroy(p.GetComponent<Collider>());
            p.transform.SetParent(transform, false);
            p.transform.localPosition = pos;
            p.transform.localRotation = Quaternion.Euler(euler);
            p.transform.localScale = scale;
            var r = p.GetComponent<Renderer>();
            r.material.color = color;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void Update()
        {
            worldPos += new Vector3(dir * KaijuJets.Speed * Time.deltaTime, 0f, 0f);
            var d = KaijuDirector.Instance;
            // Release a little before passing over him: the bombs carry on forward as they fall.
            if (!bombed && d.Running && (d.Position.x - worldPos.x) * dir <= 140f)
            {
                bombed = true;
                CarpetBomb.Drop(new Vector3(worldPos.x, worldPos.y, worldPos.z), dir, bombDelay);
            }
        }
    }

    /// <summary>A line of explosions along a jet's track across him: on him where the line crosses his body, on the ground either side.</summary>
    public class CarpetBomb : MonoBehaviour
    {
        public static int Count = 12;
        public static float Spacing = 14f, FallTime = 1.4f;

        private readonly List<Vector3> points = new List<Vector3>();
        private readonly List<float> times = new List<float>();
        private float t;
        private bool staggered;

        public static void Drop(Vector3 release, float dir, float delay)
        {
            var go = new GameObject("KaijuCarpetBomb");
            Object.DontDestroyOnLoad(go);
            var c = go.AddComponent<CarpetBomb>();
            var d = KaijuDirector.Instance;
            World world = GameApi.World;
            float h = d.Footprint.Height, r = d.Footprint.Radius;
            // Centred on him, along the jet's line.
            for (int k = 0; k < Count; k++)
            {
                float x = d.Position.x + dir * (k - (Count - 1) / 2f) * Spacing;
                float z = release.z + Random.Range(-4f, 4f);
                float y;
                var off = new Vector2(x - d.Position.x, z - d.Position.y);
                if (off.sqrMagnitude <= r * r)
                    y = d.BaseY + h * Random.Range(0.35f, 0.8f);
                else
                    y = world != null ? GameApi.GroundHeight(world, Mathf.FloorToInt(x), Mathf.FloorToInt(z)) + 1f : d.BaseY;
                c.points.Add(new Vector3(x, y, z));
                c.times.Add(delay + FallTime + k * 0.07f);
            }
            Destroy(go, delay + FallTime + Count * 0.07f + 1f);
        }

        private void Update()
        {
            t += Time.deltaTime;
            var spark = KaijuDirector.Instance.EffectMaterial("KaijuSpark");
            var smoke = KaijuDirector.Instance.EffectMaterial("KaijuSmoke");
            for (int k = times.Count - 1; k >= 0; k--)
            {
                if (t < times[k])
                    continue;
                KaijuEffects.Flash(points[k], 70f, 5f, 0.5f, new Color(1f, 0.7f, 0.35f));
                KaijuEffects.Burst(points[k], spark, smoke);
                KaijuAudio.MissileHit(points[k]);
                times.RemoveAt(k);
                points.RemoveAt(k);
                if (!staggered)
                {
                    staggered = true;
                    KaijuDirector.Instance.Flinch(KaijuJets.Stagger);
                }
            }
        }
    }
}
