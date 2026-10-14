using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// G-cell zombies: the fallout carries Godzilla's cells and the dead it touches get back up.
    /// Each zombie's skin texture is darkened and grown over with black scales when its model is
    /// built, more with each vanilla tier (normal, feral, radiated, charged, infernal); the glow of
    /// the radiated, charged and infernal tiers is his atomic blue, stronger each tier
    /// (Config/entityclasses.xml). The textures are made in code from the zombie's own, nothing
    /// is shipped. The cop's vomit is a small atomic breath: a glowing blue bolt with a blue burst.
    /// </summary>
    public static class KaijuGCell
    {
        /// <summary>
        /// Per stage (0 normal, 1 feral, 2 radiated, 3 charged, 4 infernal): how dark the skin goes and
        /// how much is scales. Charged and infernal are the same top tier (as in the game) with two
        /// flavours: charged keeps more skin and glows electric blue, infernal is charred near black
        /// with blue embers in the scales.
        /// </summary>
        public static readonly float[] Darken = { 0.35f, 0.45f, 0.58f, 0.68f, 0.88f };
        public static readonly float[] Coverage = { 0.25f, 0.35f, 0.5f, 0.78f, 0.82f };
        /// <summary>Largest texture side the scale pass works at (zombie textures are scaled down to this).</summary>
        public static int MaxSize = 512;
        /// <summary>`kaiju gcell skins off` leaves zombies' own skins (each new skin costs a moment's work when it first appears).</summary>
        public static bool SkinsOn = true;
        // New skins made at most this often, seconds; zombies spawned meanwhile keep their own skin
        // until the zombie tick checks them again.
        private static float nextSkin;
        /// <summary>True when a new skin may be made now (the zombie tick waits for this before checking a zombie).</summary>
        public static bool Ready { get { return Time.realtimeSinceStartup >= nextSkin; } }
        public static float SkinInterval = 0.2f;

        // Processed textures, by source texture and stage.
        private static readonly Dictionary<long, Texture2D> cache = new Dictionary<long, Texture2D>();

        public static int StageOf(string entityClassName)
        {
            if (entityClassName.EndsWith("Infernal"))
                return 4;
            if (entityClassName.EndsWith("Charged"))
                return 3;
            if (entityClassName.EndsWith("Radiated"))
                return 2;
            if (entityClassName.EndsWith("Feral"))
                return 1;
            return 0;
        }

        // VERIFIED (V3.3): EModelBase.createModel(World, EntityClass) builds the model and applies
        // AltMats/MatColor (the radiated glow, as _EmissiveColor) to the SkinnedMeshRenderers tagged
        // "LOD"; DismembermentManager reads a zombie's skin as _ZombieColor (else _Albedo).
        [HarmonyPatch(typeof(EModelBase), nameof(EModelBase.createModel))]
        private static class ModelPatch
        {
            private static void Postfix(EModelBase __instance, EntityClass _ec)
            {
                try
                {
                    if (!SkinsOn || __instance == null || _ec == null || _ec.entityClassName == null || !_ec.entityClassName.StartsWith("zombie"))
                        return;
                    // VERIFIED (V3.3): EModelBase.entity.
                    if (__instance.entity != null && Plain.Contains(__instance.entity.entityId))
                        return;
                    Apply(__instance.gameObject, _ec.entityClassName);
                }
                catch (System.Exception e)
                {
                    Log.Warning("[KaijuMod] G-cell skin skipped: " + e.Message);
                }
            }
        }

        // Skin texture properties, the game's zombie shader first (DismembermentManager), then common ones.
        private static readonly string[] SkinProps = { "_ZombieColor", "_Albedo", "_MainTex", "_BaseColorMap", "_BaseMap" };
        private static readonly HashSet<string> reported = new HashSet<string>();
        private static readonly HashSet<Texture> made = new HashSet<Texture>();

        /// <summary>Darkens and scales the skin of every renderer of a zombie's model; logs once per zombie type what it did.</summary>
        /// <summary>Zombies left as the game makes them (the vanilla one `kaiju gcell` spawns to compare).</summary>
        public static readonly HashSet<int> Plain = new HashSet<int>();

        /// <summary>Infects the skins of a zombie's model. True when every skin texture it has is done (false: try again later).</summary>
        public static bool Apply(GameObject model, string className)
        {
            int stage = StageOf(className);
            int renderers = 0, textured = 0, done = 0;
            string shaders = "";
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer))
                    continue;
                renderers++;
                foreach (var m in r.materials)
                {
                    if (m == null)
                        continue;
                    if (shaders.Length < 120 && m.shader != null && shaders.IndexOf(m.shader.name) < 0)
                        shaders += m.shader.name + " ";
                    foreach (string prop in SkinProps)
                    {
                        if (!m.HasProperty(prop))
                            continue;
                        var tex = m.GetTexture(prop);
                        if (tex == null)
                            continue;
                        if (made.Contains(tex))
                        {
                            textured++;
                            done++;
                            break; // already infected
                        }
                        textured++;
                        var g = Infect(tex, stage);
                        if (g != null)
                        {
                            m.SetTexture(prop, g);
                            done++;
                        }
                        break;
                    }
                }
            }
            if (done == textured && reported.Add(className))
                Log.Out("[KaijuMod] G-cell skin, " + className + " (stage " + stage + "): " + renderers + " renderers, " + textured
                    + " skin textures, " + done + " infected; shaders " + shaders);
            return done == textured;
        }

        /// <summary>The zombie skin darkened and grown over with scales for a stage (cached).</summary>
        public static Texture2D Infect(Texture src, int stage)
        {
            long key = ((long)src.GetInstanceID() << 3) | (uint)stage;
            Texture2D t;
            if (cache.TryGetValue(key, out t))
                return t;
            // One new skin at a time, spread out: making one stalls a frame.
            if (Time.realtimeSinceStartup < nextSkin)
                return null;
            nextSkin = Time.realtimeSinceStartup + SkinInterval;
            cache[key] = null; // one try per texture: a failure is not retried every spawn
            int w = src.width, h = src.height;
            while (w > MaxSize || h > MaxSize)
            {
                w /= 2;
                h /= 2;
            }
            // Game textures are not CPU-readable: copy through a render texture.
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            t = new Texture2D(w, h, TextureFormat.RGBA32, true, false);
            t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = t.GetPixels32();
            Scale(px, w, h, stage);
            t.SetPixels32(px);
            t.name = src.name + "_gcell" + stage;
            t.Apply(true, true);
            cache[key] = t;
            made.Add(t);
            return t;
        }

        /// <summary>Darkens toward black and lays scales in patches (low-frequency noise over the coverage).</summary>
        private static void Scale(Color32[] px, int w, int h, int stage)
        {
            float dark = Darken[stage], cover = Coverage[stage];
            const float cells = 22f; // scales across the texture: big enough to read at a distance
            // Each scale: a body shaded lighter toward the top (lit from above, so it reads as raised),
            // a pale ridge along its rim, and a dark crease where it tucks under the next row.
            Color body = new Color(0.17f, 0.17f, 0.17f), lit = new Color(0.42f, 0.42f, 0.4f), ridge = new Color(0.58f, 0.57f, 0.53f);
            Color crease = new Color(0.02f, 0.02f, 0.02f);
            if (stage >= 2)
            {
                body = new Color(0.13f, 0.15f, 0.18f);
                lit = new Color(0.35f, 0.42f, 0.5f);
                ridge = new Color(0.5f, 0.62f, 0.72f);
            }
            if (stage == 4)
            {
                // Infernal: charred, with blue embers along the rims.
                body = new Color(0.07f, 0.07f, 0.08f);
                lit = new Color(0.2f, 0.22f, 0.26f);
                ridge = new Color(0.2f, 0.55f, 1f);
                crease = new Color(0.02f, 0.05f, 0.12f);
            }
            float threshold = 0.78f - cover * 0.62f; // the patch noise runs about 0.2-0.8
            for (int y = 0; y < h; y++)
            {
                float v = (float)y / h;
                for (int x = 0; x < w; x++)
                {
                    float u = (float)x / w;
                    int i = y * w + x;
                    Color c = px[i];
                    c = Color.Lerp(c, new Color(0.05f, 0.05f, 0.06f, c.a), dark);
                    // Patches: smooth noise above a threshold set by the coverage, with a crisp edge.
                    float n = ValueNoise(u * 6f, v * 6f) * 0.65f + ValueNoise(u * 17f, v * 17f) * 0.35f;
                    float patch = Mathf.Clamp01((n - threshold) * 12f);
                    if (patch > 0f)
                    {
                        // Overlapping rows, each offset by half a scale and each scale nudged a little.
                        float sy = v * cells, row = Mathf.Floor(sy);
                        float sx = u * cells + (row % 2f) * 0.5f, col = Mathf.Floor(sx);
                        float r1 = Hash((int)col, (int)row), r2 = Hash((int)col + 7, (int)row);
                        float fx = sx - col - 0.5f + (r1 - 0.5f) * 0.18f, fy = sy - row - 0.3f + (r2 - 0.5f) * 0.15f;
                        float d = Mathf.Sqrt(fx * fx + fy * fy * 1.4f);
                        Color s = Color.Lerp(body, lit, Mathf.Clamp01(0.6f - fy));
                        s = Color.Lerp(s, ridge, Edge(0.38f, 0.45f, d) * (1f - Edge(0.47f, 0.5f, d)) * Mathf.Clamp01(1.1f - fy * 1.4f));
                        s = Color.Lerp(s, crease, Edge(0.49f, 0.54f, d));
                        s *= 0.85f + 0.3f * r1;
                        s.a = c.a;
                        c = Color.Lerp(c, s, patch);
                    }
                    px[i] = c;
                }
            }
        }

        /// <summary>0 below e0, 1 above e1, smooth between (GLSL smoothstep; Unity's Mathf.SmoothStep interpolates values instead).</summary>
        private static float Edge(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        private static float Hash(int x, int y)
        {
            unchecked
            {
                int n = x * 374761393 + y * 668265263;
                n = (n ^ (n >> 13)) * 1274126177;
                return ((n ^ (n >> 16)) & 0xffff) / 65535f;
            }
        }

        private static float ValueNoise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Mathf.Lerp(Hash(xi, yi), Hash(xi + 1, yi), fx);
            float b = Mathf.Lerp(Hash(xi, yi + 1), Hash(xi + 1, yi + 1), fx);
            return Mathf.Lerp(a, b, fy);
        }

        // ---- the cop's atomic spit

        // VERIFIED (V3.3): ProjectileMoveScript.Fire(start, dir, firingEntity, ...) launches a
        // projectile; itemProjectile is its ItemClass (the cop's are ammoProjectileZombieVomit*).
        [HarmonyPatch(typeof(ProjectileMoveScript), nameof(ProjectileMoveScript.Fire))]
        private static class SpitPatch
        {
            private static void Postfix(ProjectileMoveScript __instance)
            {
                try
                {
                    var ic = __instance != null ? __instance.itemProjectile : null;
                    if (ic != null && ic.Name != null && ic.Name.StartsWith("ammoProjectileZombieVomit") && __instance.GetComponent<AtomicSpit>() == null)
                        __instance.gameObject.AddComponent<AtomicSpit>();
                }
                catch (System.Exception e)
                {
                    Log.Warning("[KaijuMod] Atomic spit skipped: " + e.Message);
                }
            }
        }
    }

    /// <summary>The cop's vomit as a small atomic breath: a glowing blue bolt with a sparking trail and a blue burst where it lands.</summary>
    public class AtomicSpit : MonoBehaviour
    {
        private static readonly Color Core = new Color(0.8f, 0.95f, 1f), Glow = new Color(0.3f, 0.6f, 1f);
        private Vector3 lastWorld;

        private void Start()
        {
            foreach (var r in GetComponentsInChildren<Renderer>())
                r.enabled = false; // the green bulb
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(core.GetComponent<Collider>());
            core.transform.SetParent(transform, false);
            core.transform.localScale = Vector3.one * 0.45f;
            var mr = core.GetComponent<Renderer>();
            mr.material.color = Core;
            if (mr.material.HasProperty("_EmissionColor"))
            {
                mr.material.EnableKeyword("_EMISSION");
                mr.material.SetColor("_EmissionColor", Glow * 3f);
            }
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var light = gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Glow;
            light.range = 8f;
            light.intensity = 3f;
            light.shadows = LightShadows.None;
            var spark = KaijuDirector.Instance.EffectMaterial("KaijuSpark");
            if (spark != null)
            {
                var ps = KaijuEffects.Particles(transform, "Trail", spark, 300, false);
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
                main.startColor = new ParticleSystem.MinMaxGradient(Core, Glow);
                var emission = ps.emission;
                emission.rateOverDistance = 8f;
                ps.Play();
            }
            lastWorld = GameApi.SceneToWorld(transform.position);
        }

        private void LateUpdate()
        {
            lastWorld = GameApi.SceneToWorld(transform.position);
        }

        private void OnDestroy()
        {
            if (GameApi.World == null || KaijuDirector.Instance == null)
                return;
            KaijuEffects.Flash(lastWorld, 10f, 4f, 0.4f, Glow);
            KaijuEffects.Burst(lastWorld, KaijuDirector.Instance.EffectMaterial("KaijuSpark"), null, Core, Glow);
        }
    }
}
