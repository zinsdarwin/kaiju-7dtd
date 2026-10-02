using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// What the player sees: the Godzilla model from KaijuMod/Resources/kaiju.unity3d if it is
    /// installed, otherwise a plain grey box. Either way it is a plain Unity object, not a game
    /// Entity: no collider, no AI; the director moves it directly every frame.
    /// </summary>
    public class GreyBox
    {
        /// <summary>Asset bundle with the model, relative to the mod folder. Git-ignored, never committed.</summary>
        public const string BundleFile = "Resources/kaiju.unity3d";
        /// <summary>Name of the prefab inside the bundle.</summary>
        public const string AssetName = "Kaiju";

        private GameObject go;
        private bool isModel;
        // Height of the model at scale 1, measured once from its renderers.
        private float modelHeight = 1f;

        public void Show(float width, float height)
        {
            if (go != null)
                return;
            GameObject prefab = null;
            try
            {
                prefab = GameApi.LoadModPrefab(BundleFile, AssetName);
            }
            catch (System.Exception e)
            {
                Log.Warning("[KaijuMod] Could not load " + BundleFile + "?" + AssetName + ", using the grey box: " + e.Message);
            }

            if (prefab != null)
            {
                go = Object.Instantiate(prefab);
                go.name = "KaijuModel";
                isModel = true;
                modelHeight = MeasureHeight(go);
                Log.Out("[KaijuMod] Using model " + AssetName + " (" + modelHeight + " m tall at scale 1)");
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "KaijuGreyBox";
                isModel = false;
                // If the default material's shader is stripped from the game build, the cube
                // renders magenta. That is fine for a grey box: it is easy to spot.
            }
            // Visual only: the footprint code decides what he hits, not physics.
            foreach (var collider in go.GetComponentsInChildren<Collider>())
                Object.Destroy(collider);
            Object.DontDestroyOnLoad(go);
        }

        /// <summary>
        /// Places him with his feet at worldBase, facing heading (x, z). The box is sized to the
        /// footprint; the model is scaled uniformly to the footprint height, so set the model's
        /// pivot at its feet and its front along +z.
        /// </summary>
        public void Place(Vector3 worldBase, Vector2 heading, float width, float height)
        {
            if (go == null)
                return;
            if (isModel)
            {
                float s = height / modelHeight;
                go.transform.localScale = new Vector3(s, s, s);
                go.transform.position = GameApi.WorldToScene(worldBase);
            }
            else
            {
                go.transform.localScale = new Vector3(width, height, width);
                go.transform.position = GameApi.WorldToScene(worldBase + new Vector3(0f, height * 0.5f, 0f));
            }
            if (heading.sqrMagnitude > 0.0001f)
                go.transform.rotation = Quaternion.LookRotation(new Vector3(heading.x, 0f, heading.y));
        }

        public void Hide()
        {
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
