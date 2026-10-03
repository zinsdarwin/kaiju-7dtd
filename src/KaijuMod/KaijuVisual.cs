using System.Collections.Generic;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// What the player sees: the Godzilla model from KaijuMod/Resources/kaiju.unity3d if it is
    /// installed, otherwise the stand-in body built from primitives (KaijuBody). Either way it is
    /// a plain Unity object, not a game Entity: no collider, no AI; the director moves it directly
    /// every frame.
    /// </summary>
    public class KaijuVisual
    {
        /// <summary>Asset bundle with the model, relative to the mod folder. Git-ignored, never committed.</summary>
        public const string BundleFile = "Resources/kaiju.unity3d";
        /// <summary>Name of the prefab inside the bundle.</summary>
        public const string AssetName = "Kaiju";

        /// <summary>
        /// Ground covered by one loop of the model's walk, in body heights. The walk's playback
        /// speed follows his real speed through this, so his feet don't slide. Tunable with
        /// `kaiju stride`: raise it if the walk looks too fast, lower it if too slow.
        /// </summary>
        public static float StrideHeights = 0.6f;

        private GameObject go;
        private Animation anim;
        private float clipLength;
        private Vector3 lastBase;
        private bool haveLast;
        private readonly KaijuBody body = new KaijuBody();
        private bool usingBody;
        // Height of the model at scale 1, measured once from its renderers.
        private float modelHeight = 1f;
        // Atomic breath hooks from the bundle: the "Mouth" marker on the head bone and the dorsal
        // plate materials (emission enabled at black by the bundle build).
        private Transform mouth;
        private readonly List<Material> plates = new List<Material>();
        private static readonly Color PlateGlowColor = new Color(0.3f, 0.6f, 1f);
        private readonly Dictionary<string, Material> effectMaterials = new Dictionary<string, Material>();
        private Vector3 lastPlaceBase;
        private Vector2 lastPlaceHeading = Vector2.up;
        private float lastPlaceHeight = 1f;

        public void Show(float width, float height)
        {
            if (go != null || usingBody)
                return;
            GameObject prefab = null;
            try
            {
                prefab = GameApi.LoadModPrefab(BundleFile, AssetName);
            }
            catch (System.Exception e)
            {
                Log.Warning("[KaijuMod] Could not load " + BundleFile + "?" + AssetName + ", using the stand-in body: " + e.Message);
            }

            if (prefab != null)
            {
                go = Object.Instantiate(prefab);
                go.name = "KaijuModel";
                // The bundle build bakes the mesh to exactly 1 unit tall. Renderer bounds overstate it
                // (skinned mesh bounds are padded, measured 1.25 in game), which made him 20% short.
                modelHeight = 1f;
                Log.Out("[KaijuMod] Using model " + AssetName + " (renderer bounds " + MeasureHeight(go) + " tall at scale 1; using 1)");
            }
            else
            {
                body.Show(width, height);
                usingBody = true;
                return;
            }
            // Visual only: the footprint code decides what he hits, not physics.
            foreach (var collider in go.GetComponentsInChildren<Collider>())
                Object.Destroy(collider);
            Object.DontDestroyOnLoad(go);
            anim = go.GetComponentInChildren<Animation>();
            clipLength = anim != null && anim.clip != null ? anim.clip.length : 0f;
            haveLast = false;
            mouth = FindChild(go.transform, "Mouth");
            plates.Clear();
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials) // per-instance copies, so the glow stays on this model
                    if (m.name.Contains("Scales") && m.HasProperty("_EmissionColor"))
                        plates.Add(m);
            if (mouth == null)
                Log.Warning("[KaijuMod] Model has no Mouth marker (older bundle?); breath fires from an estimated point");
        }

        /// <summary>
        /// Places him with his feet at worldBase, facing heading (x, z). The model is scaled
        /// uniformly to the footprint height, so set its pivot at its feet and its front along +z.
        /// </summary>
        public void Place(Vector3 worldBase, Vector2 heading, float width, float height)
        {
            lastPlaceBase = worldBase;
            if (heading.sqrMagnitude > 0.0001f)
                lastPlaceHeading = heading.normalized;
            lastPlaceHeight = height;
            if (usingBody)
            {
                body.Place(worldBase, heading, width, height);
                return;
            }
            if (go == null)
                return;
            float s = height / modelHeight;
            go.transform.localScale = new Vector3(s, s, s);
            go.transform.position = GameApi.WorldToScene(worldBase);
            if (heading.sqrMagnitude > 0.0001f)
                go.transform.rotation = Quaternion.LookRotation(new Vector3(heading.x, 0f, heading.y));
            SyncWalk(worldBase, height);
        }

        /// <summary>Plays the walk at the rate that matches the ground he actually covered this frame.</summary>
        private void SyncWalk(Vector3 worldBase, float height)
        {
            float dt = Time.deltaTime;
            if (anim == null || clipLength <= 0f || dt <= 0f)
                return;
            if (haveLast)
            {
                float moved = new Vector2(worldBase.x - lastBase.x, worldBase.z - lastBase.z).magnitude;
                float metresPerLoop = Mathf.Max(0.01f, StrideHeights * height);
                float rate = moved / dt * clipLength / metresPerLoop;
                foreach (AnimationState state in anim)
                    state.speed = rate;
            }
            lastBase = worldBase;
            haveLast = true;
        }

        public void Hide()
        {
            body.Hide();
            usingBody = false;
            if (go == null)
                return;
            foreach (var m in plates)
                Object.Destroy(m);
            plates.Clear();
            mouth = null;
            Object.Destroy(go);
            go = null;
        }

        /// <summary>Mouth position in world coordinates: the bundle's marker, or an estimate from his size and heading.</summary>
        public Vector3 MouthWorld()
        {
            if (mouth != null && go != null)
                return GameApi.SceneToWorld(mouth.position);
            Vector3 fwd = new Vector3(lastPlaceHeading.x, 0f, lastPlaceHeading.y);
            return lastPlaceBase + Vector3.up * (lastPlaceHeight * 0.9f) + fwd * (lastPlaceHeight * (usingBody ? 0.28f : 0.65f));
        }

        /// <summary>Lights the dorsal plates: 0 = off, ~5 = full breath glow.</summary>
        public void SetPlateGlow(float level)
        {
            Color c = PlateGlowColor * Mathf.Max(0f, level);
            foreach (var m in plates)
                if (m != null)
                    m.SetColor("_EmissionColor", c);
        }

        /// <summary>
        /// Turns his head (and neck, a little) so the mouth points at sceneTarget, blended by weight.
        /// Call after the walk animation has posed the skeleton (LateUpdate). Limited to 55 degrees.
        /// </summary>
        public void AimHead(Vector3 sceneTarget, float weight)
        {
            if (mouth == null || weight <= 0.001f)
                return;
            Transform head = mouth.parent;
            Transform neck = head != null ? head.parent : null;
            if (neck != null)
                TurnToward(neck, sceneTarget, weight * 0.4f);
            if (head != null)
                TurnToward(head, sceneTarget, weight * 0.75f);
        }

        private void TurnToward(Transform bone, Vector3 sceneTarget, float weight)
        {
            Vector3 want = sceneTarget - mouth.position;
            if (want.sqrMagnitude < 0.01f)
                return;
            Quaternion delta = Quaternion.FromToRotation(mouth.forward, want.normalized);
            delta = Quaternion.RotateTowards(Quaternion.identity, delta, 55f);
            bone.rotation = Quaternion.Slerp(Quaternion.identity, delta, weight) * bone.rotation;
        }

        /// <summary>An effect material from the model's bundle (KaijuBeam, KaijuSpark, KaijuSmoke), or a built-in fallback.</summary>
        public Material EffectMaterial(string name)
        {
            Material m;
            if (effectMaterials.TryGetValue(name, out m) && m != null)
                return m;
            m = null;
            try
            {
                m = GameApi.LoadModAsset<Material>(BundleFile, name);
            }
            catch (System.Exception e)
            {
                Log.Warning("[KaijuMod] Could not load effect material " + name + ": " + e.Message);
            }
            if (m == null)
            {
                // Older bundle or no model: try built-in particle shaders (may be stripped from the game build).
                var shader = Shader.Find(name == "KaijuSmoke" ? "Legacy Shaders/Particles/Alpha Blended" : "Legacy Shaders/Particles/Additive")
                    ?? Shader.Find("Sprites/Default");
                if (shader != null)
                    m = new Material(shader);
                Log.Warning("[KaijuMod] Effect material " + name + " not in the bundle; using " + (shader != null ? shader.name : "nothing"));
            }
            effectMaterials[name] = m;
            return m;
        }

        private static Transform FindChild(Transform t, string name)
        {
            if (t.name == name)
                return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var found = FindChild(t.GetChild(i), name);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static float MeasureHeight(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return 1f;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return b.size.y > 0.01f ? b.size.y : 1f;
        }
    }
}
