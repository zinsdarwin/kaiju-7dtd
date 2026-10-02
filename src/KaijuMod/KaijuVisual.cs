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
                modelHeight = MeasureHeight(go);
                Log.Out("[KaijuMod] Using model " + AssetName + " (" + modelHeight + " m tall at scale 1)");
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
        }

        /// <summary>
        /// Places him with his feet at worldBase, facing heading (x, z). The model is scaled
        /// uniformly to the footprint height, so set its pivot at its feet and its front along +z.
        /// </summary>
        public void Place(Vector3 worldBase, Vector2 heading, float width, float height)
        {
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
            Object.Destroy(go);
            go = null;
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
