using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// The maser cannon's shot: a crackling blue-white beam from the dish to his chest for a few
    /// seconds, sparks bursting off his hide. He staggers while it lasts; when it ends, Done is
    /// called (KaijuRun has him turn his breath on the cannon).
    /// </summary>
    public class KaijuMaser : MonoBehaviour
    {
        public static float Duration = 6f;
        private static readonly Color CoreColor = new Color(0.9f, 0.97f, 1f, 1f);
        private static readonly Color GlowColor = new Color(0.45f, 0.75f, 1f, 0.7f);

        private Vector3 from;
        private float t, nextSpark;
        private LineRenderer core, glow;
        private Light muzzle, hit;
        private System.Action done;

        public static void Fire(Vector3 fromWorld, System.Action done)
        {
            var go = new GameObject("KaijuMaser");
            Object.DontDestroyOnLoad(go);
            var m = go.AddComponent<KaijuMaser>();
            m.from = fromWorld;
            m.done = done;
            var beam = KaijuDirector.Instance.EffectMaterial("KaijuBeam");
            if (beam != null)
            {
                m.glow = m.Line(beam, GlowColor);
                m.core = m.Line(beam, CoreColor);
            }
            m.muzzle = m.MakeLight(30f);
            m.hit = m.MakeLight(60f);
            KaijuAudio.MaserZap(fromWorld);
            KaijuDirector.Instance.Flinch(Duration + 0.5f);
        }

        private Vector3 Target()
        {
            var d = KaijuDirector.Instance;
            return new Vector3(d.Position.x, d.BaseY + d.Footprint.Height * 0.6f, d.Position.y);
        }

        private void Update()
        {
            t += Time.deltaTime;
            var d = KaijuDirector.Instance;
            if (t >= Duration || !d.Running || d.Dying)
            {
                if (done != null && d.Running && !d.Dying)
                    done();
                Destroy(gameObject);
                return;
            }
            Vector3 to = Target();
            Vector3 a = GameApi.WorldToScene(from), b = GameApi.WorldToScene(to);
            // Crackle: the width jumps about every frame.
            float w = Random.Range(1.2f, 2.4f);
            Set(glow, a, b, w * 3f);
            Set(core, a, b, w);
            muzzle.transform.position = a;
            hit.transform.position = b;
            muzzle.intensity = Random.Range(4f, 8f);
            hit.intensity = Random.Range(6f, 12f);
            if (t >= nextSpark)
            {
                nextSpark = t + 0.25f;
                Vector2 j = Random.insideUnitCircle * d.Footprint.Radius * 0.3f;
                KaijuEffects.Burst(to + new Vector3(j.x, Random.Range(-10f, 10f), j.y), KaijuDirector.Instance.EffectMaterial("KaijuSpark"), null);
            }
        }

        private static void Set(LineRenderer lr, Vector3 a, Vector3 b, float width)
        {
            if (lr == null)
                return;
            lr.enabled = true;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lr.startWidth = width;
            lr.endWidth = width * 1.3f;
        }

        private LineRenderer Line(Material mat, Color color)
        {
            var go = new GameObject("Beam");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.sharedMaterial = mat;
            lr.startColor = color;
            lr.endColor = color;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.numCapVertices = 4;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        private Light MakeLight(float range)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = GlowColor;
            l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }
    }
}
