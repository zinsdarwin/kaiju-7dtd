using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// The missile battery's salvo: missiles climb out of the launcher, arc over and strike his
    /// body, each with a fiery trail, a burst and a bang. They do not hurt him; the first hit makes
    /// him roar and stop for a moment (KaijuDirector.Flinch).
    /// </summary>
    public class Missile : WorldAnchored
    {
        /// <summary>Missile speed along its arc, m/s.</summary>
        public static float Speed = 170f;

        private Vector3 from, lift;
        private Vector3 aim;          // offset from his feet to the spot it hits
        private float t, seconds, delay;
        private bool flying;
        private Light glow;
        private ParticleSystem fire, smoke;

        /// <summary>A salvo of count missiles from a launcher at fromWorld, one every 0.3 s.</summary>
        public static void Salvo(Vector3 fromWorld, int count)
        {
            var director = KaijuDirector.Instance;
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("KaijuMissile");
                Object.DontDestroyOnLoad(go);
                var m = go.AddComponent<Missile>();
                m.from = fromWorld + new Vector3(Random.Range(-1.5f, 1.5f), 0f, Random.Range(-1.5f, 1.5f));
                float h = director.Footprint.Height;
                Vector2 jitter = Random.insideUnitCircle * director.Footprint.Radius * 0.35f;
                m.aim = new Vector3(jitter.x, h * Random.Range(0.45f, 0.85f), jitter.y);
                m.delay = i * 0.3f;
                m.worldPos = m.from;
            }
        }

        private Vector3 Target()
        {
            var d = KaijuDirector.Instance;
            return new Vector3(d.Position.x, d.BaseY, d.Position.y) + aim;
        }

        private void Launch()
        {
            flying = true;
            Vector3 to = Target();
            float dist = (to - from).magnitude;
            seconds = Mathf.Max(2f, dist / Speed);
            // Straight up out of the tube first, then over toward him.
            lift = from + Vector3.up * Mathf.Clamp(dist * 0.35f, 60f, 250f) + (to - from) * 0.25f;
            LateUpdate();
            var smokeMat = KaijuDirector.Instance.EffectMaterial("KaijuSmoke");
            var sparkMat = KaijuDirector.Instance.EffectMaterial("KaijuSpark");
            if (sparkMat != null)
            {
                fire = KaijuEffects.Particles(transform, "Fire", sparkMat, 300, false);
                var main = fire.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.45f, 0.1f));
                var emission = fire.emission;
                emission.rateOverDistance = 1.5f;
                fire.Play();
            }
            if (smokeMat != null)
            {
                smoke = KaijuEffects.Particles(transform, "Smoke", smokeMat, 1500, false);
                var main = smoke.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(2f, 3.5f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.85f, 0.85f, 0.6f), new Color(0.6f, 0.6f, 0.6f, 0.5f));
                var size = smoke.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 3f));
                var col = smoke.colorOverLifetime;
                col.enabled = true;
                col.color = KaijuEffects.Fade(Color.white, Color.white, 0.05f, 0.4f);
                var emission = smoke.emission;
                emission.rateOverDistance = 0.4f;
                smoke.Play();
            }
            glow = gameObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.6f, 0.25f);
            glow.range = 25f;
            glow.intensity = 3f;
            glow.shadows = LightShadows.None;
            KaijuAudio.Launch(from);
        }

        private void Update()
        {
            if (!flying)
            {
                delay -= Time.deltaTime;
                if (delay <= 0f)
                    Launch();
                return;
            }
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / seconds);
            // Quadratic Bezier from the tube over the lift point onto him, following him as he moves.
            Vector3 to = Target();
            worldPos = (1 - k) * (1 - k) * from + 2 * (1 - k) * k * lift + k * k * to;
            if (k < 1f)
                return;
            Impact(to);
        }

        private void Impact(Vector3 at)
        {
            var spark = KaijuDirector.Instance.EffectMaterial("KaijuSpark");
            var smokeMat = KaijuDirector.Instance.EffectMaterial("KaijuSmoke");
            KaijuEffects.Flash(at, 80f, 6f, 0.6f, new Color(1f, 0.7f, 0.35f));
            KaijuEffects.Burst(at, spark, smokeMat);
            KaijuAudio.MissileHit(at);
            KaijuDirector.Instance.Flinch();
            // Leave the trail to fade out where it is.
            if (glow != null)
                Destroy(glow);
            foreach (var ps in new[] { fire, smoke })
            {
                if (ps == null)
                    continue;
                var emission = ps.emission;
                emission.enabled = false;
            }
            flying = false;
            enabled = false;
            Destroy(gameObject, 5f);
        }
    }
}
