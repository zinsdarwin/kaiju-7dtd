using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// Milestone 1 stand-in for Godzilla: a plain Unity cube, not a game Entity. It has no
    /// collider and no AI; the director moves it directly every frame.
    /// </summary>
    public class GreyBox
    {
        private GameObject go;

        public void Show(float width, float height)
        {
            if (go != null)
                return;
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "KaijuGreyBox";
            // Visual only: the footprint code decides what he hits, not physics.
            var collider = go.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);
            go.transform.localScale = new Vector3(width, height, width);
            // If the default material's shader is stripped from the game build, the cube renders
            // magenta. That is fine for a grey box: it is easy to spot.
            Object.DontDestroyOnLoad(go);
        }

        /// <summary>Places the box with its base at worldBase, facing heading (x, z).</summary>
        public void Place(Vector3 worldBase, Vector2 heading)
        {
            if (go == null)
                return;
            float h = go.transform.localScale.y;
            go.transform.position = GameApi.WorldToScene(worldBase + new Vector3(0f, h * 0.5f, 0f));
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
    }
}
