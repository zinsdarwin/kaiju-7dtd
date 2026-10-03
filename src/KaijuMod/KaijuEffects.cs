using System.Collections.Generic;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// One-shot visual effects, particles and lights only, anchored to a world position (they
    /// follow the scene origin as it shifts). Sized from Godzilla's height. Materials come from the
    /// model's bundle (KaijuSmoke alpha-blended, KaijuSpark additive).
    /// </summary>
    public static class KaijuEffects
    {
        /// <summary>At most this many mushroom clouds at once; the oldest is removed first.</summary>
        public const int MaxClouds = 6;
        private static readonly List<GameObject> clouds = new List<GameObject>();

        /// <summary>
        /// Atomic-bomb impact (Minus One style): white flash, a dust shockwave ring along the ground,
        /// and a mushroom cloud whose cap rises to about 5x his height over a minute, lit orange
        /// underneath at first, then grey, drifting and fading over a couple of minutes.
        /// </summary>
        public static void Explosion(Vector3 worldPos, float height, Material smoke, Material spark)
        {
            clouds.RemoveAll(c => c == null);
            while (clouds.Count >= MaxClouds)
            {
                Object.Destroy(clouds[0]);
                clouds.RemoveAt(0);
            }
            var go = new GameObject("KaijuMushroomCloud");
            Object.DontDestroyOnLoad(go);
            var cloud = go.AddComponent<MushroomCloud>();
            cloud.Init(worldPos, height, smoke, spark);
            clouds.Add(go);
        }

        /// <summary>A brief bright light, e.g. the Oxygen Destroyer going off.</summary>
        public static void Flash(Vector3 worldPos, float range, float intensity, float seconds, Color color)
        {
            var go = new GameObject("KaijuFlash");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Flash>().Init(worldPos, range, intensity, seconds, color);
        }

        /// <summary>Bubbles and foam rising around a point for a while (the Oxygen Destroyer at work).</summary>
        public static void Bubbles(Vector3 worldPos, float height, float seconds, Material spark)
        {
            if (spark == null)
                return;
            var go = new GameObject("KaijuBubbles");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Bubbles>().Init(worldPos, height, seconds, spark);
        }

        internal static ParticleSystem Particles(Transform parent, string name, Material mat, int max, bool local)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = max;
            main.simulationSpace = local ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        internal static Gradient Fade(Color from, Color to, float fadeInEnd, float fadeOutStart)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeInEnd), new GradientAlphaKey(1f, fadeOutStart), new GradientAlphaKey(0f, 1f) });
            return g;
        }
    }

    /// <summary>
    /// Lasting fallout over a ruined city: a dark remnant cloud hanging high above it and a low
    /// green-grey haze over the ruins. Slow, sparse particles that never stop emitting.
    /// </summary>
    public class FalloutCloud : WorldAnchored
    {
        public static FalloutCloud Create(Vector3 worldPos, float height, float cityHalfWidth, Material smoke)
        {
            if (smoke == null)
                return null;
            var go = new GameObject("KaijuFallout");
            Object.DontDestroyOnLoad(go);
            var f = go.AddComponent<FalloutCloud>();
            f.worldPos = worldPos;
            f.LateUpdate();
            float h = Mathf.Max(10f, height);

            var remnant = KaijuEffects.Particles(go.transform, "Remnant", smoke, 70, true);
            remnant.transform.localPosition = new Vector3(0f, h * 4.2f, 0f);
            var main = remnant.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(80f, 120f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(h * 1.0f, h * 1.8f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.24f, 0.23f, 0.22f, 0.55f), new Color(0.32f, 0.3f, 0.27f, 0.45f));
            var shape = remnant.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = h * 1.6f;
            shape.scale = new Vector3(1.6f, 0.45f, 1.6f);
            var col = remnant.colorOverLifetime;
            col.enabled = true;
            col.color = KaijuEffects.Fade(Color.white, Color.white, 0.15f, 0.8f);
            var emission = remnant.emission;
            emission.rateOverTime = 0.7f;
            remnant.Play();
            remnant.Simulate(60f, true, false); // start already formed after a load

            var haze = KaijuEffects.Particles(go.transform, "Haze", smoke, 60, true);
            haze.transform.localPosition = new Vector3(0f, h * 0.35f, 0f);
            main = haze.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(50f, 80f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(h * 0.8f, h * 1.4f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.42f, 0.47f, 0.34f, 0.35f), new Color(0.36f, 0.4f, 0.3f, 0.3f));
            shape = haze.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(cityHalfWidth * 2.2f, h * 0.4f, 4f * 78f);
            col = haze.colorOverLifetime;
            col.enabled = true;
            col.color = KaijuEffects.Fade(Color.white, Color.white, 0.2f, 0.75f);
            emission = haze.emission;
            emission.rateOverTime = 0.9f;
            haze.Play();
            haze.Simulate(50f, true, false);
            return f;
        }
    }

    /// <summary>Grey ash drifting down around the local player while they are in the radiation.</summary>
    public class Ashfall : MonoBehaviour
    {
        private ParticleSystem ps;
        private float strength;
        public float Target;

        public static Ashfall Create(Material smoke)
        {
            if (smoke == null)
                return null;
            var go = new GameObject("KaijuAshfall");
            Object.DontDestroyOnLoad(go);
            var a = go.AddComponent<Ashfall>();
            a.ps = KaijuEffects.Particles(go.transform, "Ash", smoke, 400, false);
            var main = a.ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.55f, 0.52f, 0.9f), new Color(0.3f, 0.3f, 0.28f, 0.8f));
            main.gravityModifier = 0.03f;
            var shape = a.ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(40f, 1f, 40f);
            var noise = a.ps.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 0.3f;
            var col = a.ps.colorOverLifetime;
            col.enabled = true;
            col.color = KaijuEffects.Fade(Color.white, Color.white, 0.1f, 0.8f);
            a.ps.Play();
            return a;
        }

        /// <summary>Follows the player (scene position), fading the ash in and out toward Target (0-1).</summary>
        public void Follow(Vector3 playerScenePos)
        {
            transform.position = playerScenePos + Vector3.up * 14f;
            strength = Mathf.MoveTowards(strength, Target, Time.deltaTime * 0.25f);
            var emission = ps.emission;
            emission.rateOverTime = 60f * strength;
        }
    }

    /// <summary>Keeps an effect at a fixed world position while the scene origin shifts.</summary>
    public class WorldAnchored : MonoBehaviour
    {
        protected Vector3 worldPos;

        protected virtual void LateUpdate()
        {
            transform.position = GameApi.WorldToScene(worldPos);
        }
    }

    /// <summary>A pulsing point light marking a part crate or the armed Oxygen Destroyer.</summary>
    public class Beacon : WorldAnchored
    {
        private Light light;
        private float baseIntensity;

        public static Beacon Create(Vector3 worldPos, Color color, float range, float intensity)
        {
            var go = new GameObject("KaijuBeacon");
            Object.DontDestroyOnLoad(go);
            var b = go.AddComponent<Beacon>();
            b.worldPos = worldPos;
            b.baseIntensity = intensity;
            b.light = go.AddComponent<Light>();
            b.light.type = LightType.Point;
            b.light.color = color;
            b.light.range = range;
            b.light.shadows = LightShadows.None;
            b.LateUpdate();
            return b;
        }

        private void Update()
        {
            light.intensity = baseIntensity * (0.65f + 0.35f * Mathf.Sin(Time.time * 3f));
        }
    }

    public class Flash : WorldAnchored
    {
        private Light light;
        private float t, seconds, intensity;

        public void Init(Vector3 pos, float range, float intensity, float seconds, Color color)
        {
            worldPos = pos;
            this.intensity = intensity;
            this.seconds = Mathf.Max(0.1f, seconds);
            light = gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            LateUpdate();
        }

        private void Update()
        {
            t += Time.deltaTime;
            float k = t / seconds;
            light.intensity = intensity * (1f - k) * (1f - k);
            if (k >= 1f)
                Destroy(gameObject);
        }
    }

    public class Bubbles : WorldAnchored
    {
        private ParticleSystem ps;
        private float t, seconds;

        public void Init(Vector3 pos, float height, float seconds, Material spark)
        {
            worldPos = pos;
            this.seconds = seconds;
            LateUpdate();
            ps = KaijuEffects.Particles(transform, "Bubbles", spark, 600, true);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(height * 0.05f, height * 0.15f);
            main.startSize = new ParticleSystem.MinMaxCurve(height * 0.01f, height * 0.04f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.95f, 1f, 0.9f), new Color(0.4f, 0.8f, 1f, 0.6f));
            main.gravityModifier = -0.15f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = height * 0.4f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var emission = ps.emission;
            emission.rateOverTime = 180f;
            ps.Play();
        }

        private void Update()
        {
            t += Time.deltaTime;
            if (t > seconds && ps != null)
            {
                var emission = ps.emission;
                emission.enabled = false;
            }
            if (t > seconds + 4f)
                Destroy(gameObject);
        }
    }

    /// <summary>Flash, ground shockwave ring, rising stem and billowing cap; fades over ~2.5 minutes.</summary>
    public class MushroomCloud : WorldAnchored
    {
        private const float CapRise = 60f;     // seconds for the cap to climb
        private const float EmitFor = 40f;     // seconds the stem and cap keep emitting
        private const float LifeSpan = 160f;

        private float h, t;
        private Transform capRoot;
        private ParticleSystem ring, stem, cap, fire;
        private Light glow;

        public void Init(Vector3 pos, float height, Material smoke, Material spark)
        {
            worldPos = pos;
            h = Mathf.Max(10f, height);
            LateUpdate();

            KaijuEffects.Flash(pos + Vector3.up * h * 0.5f, h * 20f, 10f, 1.8f, new Color(1f, 0.97f, 0.9f));
            glow = gameObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.55f, 0.25f);
            glow.range = h * 4f;
            glow.intensity = 4f;
            glow.shadows = LightShadows.None;

            if (smoke != null)
            {
                // Dust shockwave: one burst racing out along the ground.
                ring = KaijuEffects.Particles(transform, "Shockwave", smoke, 300, true);
                var main = ring.main;
                main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(h * 0.9f, h * 1.4f);
                main.startSize = new ParticleSystem.MinMaxCurve(h * 0.2f, h * 0.4f);
                main.startColor = new Color(0.55f, 0.5f, 0.45f, 0.7f);
                var shape = ring.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = h * 0.15f;
                shape.rotation = new Vector3(-90f, 0f, 0f);
                var limit = ring.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.limit = h * 0.1f;
                limit.dampen = 0.08f;
                var col = ring.colorOverLifetime;
                col.enabled = true;
                col.color = KaijuEffects.Fade(Color.white, Color.white, 0.05f, 0.4f);
                var size = ring.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.5f));
                ring.Emit(260);

                // Stem: a column rising from the impact.
                stem = KaijuEffects.Particles(transform, "Stem", smoke, 400, true);
                main = stem.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(25f, 35f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(h * 0.12f, h * 0.16f);
                main.startSize = new ParticleSystem.MinMaxCurve(h * 0.25f, h * 0.45f);
                shape = stem.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 4f;
                shape.radius = h * 0.08f;
                shape.rotation = new Vector3(-90f, 0f, 0f);
                col = stem.colorOverLifetime;
                col.enabled = true;
                col.color = KaijuEffects.Fade(Color.white, Color.white, 0.05f, 0.6f);
                size = stem.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2f));
                var emission = stem.emission;
                emission.rateOverTime = 12f;
                stem.Play();

                // Cap: billows out from a point that climbs to ~5x his height.
                capRoot = new GameObject("CapRoot").transform;
                capRoot.SetParent(transform, false);
                cap = KaijuEffects.Particles(capRoot, "Cap", smoke, 500, true);
                main = cap.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(60f, 95f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(h * 0.02f, h * 0.08f);
                main.startSize = new ParticleSystem.MinMaxCurve(h * 0.7f, h * 1.3f);
                shape = cap.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = h * 0.5f;
                col = cap.colorOverLifetime;
                col.enabled = true;
                col.color = KaijuEffects.Fade(Color.white, Color.white, 0.03f, 0.7f);
                size = cap.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 2.4f));
                var vel = cap.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.Local;
                vel.x = new ParticleSystem.MinMaxCurve(1.2f);
                vel.y = new ParticleSystem.MinMaxCurve(0.4f);
                vel.z = new ParticleSystem.MinMaxCurve(0.3f);
                emission = cap.emission;
                emission.rateOverTime = 9f;
                cap.Play();
            }
            if (spark != null && capRoot != null)
            {
                // Fire glowing inside the base of the cap for the first seconds.
                fire = KaijuEffects.Particles(capRoot, "Fire", spark, 200, true);
                var main = fire.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(h * 0.02f, h * 0.06f);
                main.startSize = new ParticleSystem.MinMaxCurve(h * 0.4f, h * 0.8f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.2f, 0.8f), new Color(1f, 0.3f, 0.1f, 0.6f));
                var shape = fire.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = h * 0.4f;
                var col = fire.colorOverLifetime;
                col.enabled = true;
                col.color = KaijuEffects.Fade(Color.white, new Color(1f, 0.4f, 0.2f), 0.1f, 0.5f);
                var emission = fire.emission;
                emission.rateOverTime = 30f;
                fire.Play();
            }
        }

        private void Update()
        {
            t += Time.deltaTime;
            // Cap climbs fast at first, then slows: 1 - e^(-t/20) of 5 heights.
            if (capRoot != null)
            {
                float rise = 1f - Mathf.Exp(-t / (CapRise / 3f));
                capRoot.localPosition = new Vector3(0f, h * (0.6f + 4.4f * rise), 0f);
                var shape = cap.shape;
                shape.radius = h * Mathf.Lerp(0.5f, 1.6f, rise);
            }
            // Orange lit underneath at first, grey later.
            float heat = Mathf.Clamp01(1f - t / 20f);
            Color grey = new Color(0.42f, 0.41f, 0.43f, 0.75f);
            Color hot = new Color(0.85f, 0.45f, 0.25f, 0.8f);
            if (stem != null)
            {
                var main = stem.main;
                main.startColor = Color.Lerp(grey, hot, heat);
            }
            if (cap != null)
            {
                var main = cap.main;
                main.startColor = Color.Lerp(grey, hot, heat * 0.8f);
            }
            if (glow != null)
                glow.intensity = 4f * heat;
            if (fire != null && t > 12f)
            {
                var emission = fire.emission;
                emission.enabled = false;
            }
            if (t > EmitFor)
            {
                if (stem != null) { var e = stem.emission; e.enabled = false; }
                if (cap != null) { var e = cap.emission; e.enabled = false; }
            }
            if (t > LifeSpan)
                Destroy(gameObject);
        }
    }
}
