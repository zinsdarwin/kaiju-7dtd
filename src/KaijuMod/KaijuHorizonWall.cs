using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// A wall of dark smoke filling the horizon on the radiation side after an attack: a few
    /// curved bands (one mesh each, so a handful of draw calls), centred on the player so it always
    /// sits on the horizon, at a distance that follows the radiation front so it looms closer as
    /// the front nears. Billows by scrolling a generated smoke texture; dark at the base, fading
    /// upward; glows orange low down for a few minutes after a blast. Built once: no per-frame
    /// allocations, no shadows. The front cloud bank stays as the near detail.
    /// </summary>
    public class KaijuHorizonWall : MonoBehaviour
    {
        /// <summary>Width of the wall, degrees of the horizon.</summary>
        public static float ArcDegrees = 160f;
        /// <summary>Distance range, metres. The game camera draws to 2000 m, so it stays inside that.</summary>
        public static float MinDistance = 700f, MaxDistance = 1800f;
        /// <summary>Height of the main band as a fraction of its distance (0.42 at 1500 m = 630 m).</summary>
        public static float HeightFactor = 0.42f;
        public static float FadeSeconds = 60f, GlowSeconds = 180f;

        private struct Layer
        {
            public Transform T;
            public Renderer R;
            public Material M;
            public float RadiusMul, HeightMul, Scroll, Alpha;
            public Color Tint;
            public bool Glow;
        }

        private Layer[] layers;
        private float level, target, glowStart = -9999f, scroll;
        private Vector3 centre, dir = Vector3.left;
        private float distance = MaxDistance, baseY;
        private static Texture2D smokeTex;
        private static readonly int TintColorId = Shader.PropertyToID("_TintColor");

        public static KaijuHorizonWall Create(Material smoke)
        {
            if (smoke == null)
                return null;
            var go = new GameObject("KaijuHorizonWall");
            Object.DontDestroyOnLoad(go);
            var w = go.AddComponent<KaijuHorizonWall>();
            w.Build(smoke);
            return w;
        }

        private void Build(Material smoke)
        {
            Mesh arc = BuildArc(48);
            var tex = SmokeTexture();
            // radius, height, tint (Alpha Blended doubles it: 0.5 is neutral), scroll speed, tiling, alpha
            layers = new[]
            {
                MakeLayer(arc, smoke, tex, 1.15f, 1.10f, new Color(0.17f, 0.16f, 0.16f), 0.0020f, 5f, 0.85f, false),
                MakeLayer(arc, smoke, tex, 1.00f, 0.90f, new Color(0.12f, 0.115f, 0.11f), -0.0030f, 7f, 0.9f, false),
                MakeLayer(arc, smoke, tex, 0.90f, 0.60f, new Color(0.09f, 0.085f, 0.08f), 0.0045f, 9f, 0.95f, false),
                MakeLayer(arc, smoke, tex, 0.93f, 0.20f, new Color(0.95f, 0.42f, 0.14f), 0.0060f, 8f, 0.8f, true),
            };
            SetVisible(false);
        }

        private Layer MakeLayer(Mesh arc, Material smoke, Texture tex, float r, float h, Color tint, float speed, float tiles, float alpha, bool glow)
        {
            var go = new GameObject(glow ? "Glow" : "Band");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = arc;
            var mr = go.AddComponent<MeshRenderer>();
            var m = new Material(smoke);
            m.mainTexture = tex;
            m.mainTextureScale = new Vector2(tiles, 1f);
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return new Layer { T = go.transform, R = mr, M = m, RadiusMul = r, HeightMul = h, Scroll = speed, Tint = tint, Alpha = alpha, Glow = glow };
        }

        /// <summary>
        /// A unit arc facing +z (radius 1, height 1, ArcDegrees wide), three rows: dark and opaque
        /// at the base, thinning at mid height, clear at the top (vertex colours; the particle
        /// shader multiplies them in).
        /// </summary>
        private static Mesh BuildArc(int segments)
        {
            float[] rowY = { -0.05f, 0.45f, 1f };
            float[] rowA = { 1f, 0.75f, 0f };
            int cols = segments + 1;
            var v = new Vector3[cols * 3];
            var uv = new Vector2[cols * 3];
            var c = new Color[cols * 3];
            for (int i = 0; i < cols; i++)
            {
                float u = i / (float)segments;
                float a = Mathf.Deg2Rad * (u - 0.5f) * ArcDegrees;
                // Fade the two ends so the wall has no hard edge at the sides.
                float edge = Mathf.Clamp01(Mathf.Min(u, 1f - u) / 0.12f);
                for (int r = 0; r < 3; r++)
                {
                    int k = r * cols + i;
                    v[k] = new Vector3(Mathf.Sin(a), rowY[r], Mathf.Cos(a));
                    uv[k] = new Vector2(u, rowY[r]);
                    c[k] = new Color(1f, 1f, 1f, rowA[r] * edge);
                }
            }
            var tris = new int[segments * 2 * 6];
            int t = 0;
            for (int r = 0; r < 2; r++)
                for (int i = 0; i < segments; i++)
                {
                    int a0 = r * cols + i, a1 = a0 + 1, b0 = a0 + cols, b1 = b0 + 1;
                    tris[t++] = a0; tris[t++] = b0; tris[t++] = a1;
                    tris[t++] = a1; tris[t++] = b0; tris[t++] = b1;
                }
            var m = new Mesh { name = "KaijuHorizonArc", vertices = v, uv = uv, colors = c, triangles = tris };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Billowy smoke: a few octaves of Perlin noise in alpha and shade, tiling sideways.</summary>
        private static Texture2D SmokeTexture()
        {
            if (smokeTex != null)
                return smokeTex;
            const int W = 256, H = 128;
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float n = 0f, amp = 0.5f, f = 1f;
                    for (int o = 0; o < 4; o++)
                    {
                        // Wrap in x so the texture tiles along the arc.
                        float ang = x / (float)W * Mathf.PI * 2f;
                        n += amp * Mathf.PerlinNoise(Mathf.Cos(ang) * 2f * f + 10f, Mathf.Sin(ang) * 2f * f + y / (float)H * 3f * f);
                        amp *= 0.5f;
                        f *= 2f;
                    }
                    float a = Mathf.Clamp01(0.45f + n * 0.9f);
                    float s = 0.7f + 0.3f * n;
                    px[y * W + x] = new Color(s, s, s, a);
                }
            smokeTex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "kaiju_horizon_smoke", wrapMode = TextureWrapMode.Repeat };
            smokeTex.SetPixels(px);
            smokeTex.Apply();
            return smokeTex;
        }

        /// <summary>
        /// Where the wall should be: dangerDir is the world direction of the radiation (x, z),
        /// distance how far away it is (clamped), baseY the ground height under the player.
        /// on: fade in (true) or out.
        /// </summary>
        public void Set(bool on, Vector3 playerScene, Vector2 dangerDir, float frontDistance, float groundY)
        {
            target = on ? 1f : 0f;
            centre = playerScene;
            if (dangerDir.sqrMagnitude > 0.0001f)
                dir = new Vector3(dangerDir.x, 0f, dangerDir.y).normalized;
            distance = Mathf.Clamp(frontDistance, MinDistance, MaxDistance);
            baseY = groundY;
        }

        /// <summary>A city was just blasted: the low orange glow flares and dies down over GlowSeconds.</summary>
        public void Flare()
        {
            glowStart = Time.time;
        }

        private void SetVisible(bool on)
        {
            for (int i = 0; i < layers.Length; i++)
                layers[i].R.enabled = on;
        }

        private void LateUpdate()
        {
            level = Mathf.MoveTowards(level, target, Time.deltaTime / FadeSeconds);
            if (level <= 0f)
            {
                SetVisible(false);
                return;
            }
            transform.position = new Vector3(centre.x, baseY, centre.z);
            transform.rotation = Quaternion.LookRotation(dir);
            scroll += Time.deltaTime;
            float glow = Mathf.Clamp01(1f - (Time.time - glowStart) / GlowSeconds);
            for (int i = 0; i < layers.Length; i++)
            {
                var L = layers[i];
                float r = distance * L.RadiusMul;
                L.T.localScale = new Vector3(r, distance * HeightFactor * L.HeightMul, r);
                L.M.mainTextureOffset = new Vector2(scroll * L.Scroll, 0f);
                float a = L.Alpha * level * (L.Glow ? glow * (0.85f + 0.15f * Mathf.Sin(scroll * 2.3f)) : 1f);
                L.M.SetColor(TintColorId, new Color(L.Tint.r, L.Tint.g, L.Tint.b, a * 0.5f));
                L.R.enabled = a > 0.001f;
            }
        }
    }
}
