using System.Collections.Generic;
using UnityEngine;

namespace KaijuMod
{
    /// <summary>
    /// Stand-in Godzilla built from Unity primitives: legs, tilted torso, small arms, head, a long
    /// tail and three rows of dorsal plates. Not a game Entity: no collider, no AI; the director
    /// places it every frame and it animates its own walk cycle.
    ///
    /// Modelled 1 unit tall (top of the plates) facing +z with feet at y = 0, then scaled
    /// uniformly to the footprint height. A real model from an asset bundle replaces this later.
    /// </summary>
    public class KaijuBody
    {
        private static readonly Color Skin = new Color(0.16f, 0.18f, 0.16f);
        private static readonly Color Belly = new Color(0.24f, 0.25f, 0.22f);
        private static readonly Color Plate = new Color(0.55f, 0.56f, 0.52f);
        private static readonly Color Eye = new Color(1f, 0.85f, 0.4f);

        private GameObject root;
        private Transform hips;
        private Transform legL, legR;
        private readonly List<Transform> tail = new List<Transform>();
        private float phase;
        private Vector3 lastBase;
        private bool haveLast;

        public void Show(float width, float height)
        {
            if (root != null)
                return;
            root = new GameObject("KaijuBody");
            Object.DontDestroyOnLoad(root);
            var skin = MakeMaterial(Skin);
            var belly = MakeMaterial(Belly);
            var plate = MakeMaterial(Plate);
            var eye = MakeMaterial(Eye);

            // Legs pivot at the hip so they can swing.
            legL = Pivot("LegL", root.transform, new Vector3(-0.13f, 0.36f, 0f));
            legR = Pivot("LegR", root.transform, new Vector3(0.13f, 0.36f, 0f));
            foreach (var leg in new[] { legL, legR })
            {
                Part(PrimitiveType.Capsule, leg, new Vector3(0f, -0.13f, 0f), Vector3.zero, new Vector3(0.17f, 0.15f, 0.2f), skin);
                Part(PrimitiveType.Cube, leg, new Vector3(0f, -0.34f, 0.04f), Vector3.zero, new Vector3(0.13f, 0.05f, 0.2f), skin);
            }

            // Upper body leans forward over the hips and bobs as he walks.
            hips = Pivot("Hips", root.transform, new Vector3(0f, 0.36f, 0f));
            Part(PrimitiveType.Capsule, hips, new Vector3(0f, 0.18f, 0.02f), new Vector3(15f, 0f, 0f), new Vector3(0.36f, 0.3f, 0.32f), skin);
            Part(PrimitiveType.Capsule, hips, new Vector3(0f, 0.16f, 0.08f), new Vector3(15f, 0f, 0f), new Vector3(0.28f, 0.26f, 0.22f), belly);
            Part(PrimitiveType.Capsule, hips, new Vector3(0f, 0.43f, 0.11f), new Vector3(30f, 0f, 0f), new Vector3(0.17f, 0.12f, 0.17f), skin); // neck
            Part(PrimitiveType.Cube, hips, new Vector3(0f, 0.53f, 0.19f), new Vector3(10f, 0f, 0f), new Vector3(0.13f, 0.1f, 0.15f), skin);   // skull
            Part(PrimitiveType.Cube, hips, new Vector3(0f, 0.5f, 0.29f), new Vector3(10f, 0f, 0f), new Vector3(0.1f, 0.06f, 0.1f), skin);     // snout
            Part(PrimitiveType.Cube, hips, new Vector3(0f, 0.465f, 0.27f), new Vector3(20f, 0f, 0f), new Vector3(0.09f, 0.025f, 0.11f), belly); // jaw
            Part(PrimitiveType.Sphere, hips, new Vector3(-0.055f, 0.555f, 0.255f), Vector3.zero, Vector3.one * 0.022f, eye);
            Part(PrimitiveType.Sphere, hips, new Vector3(0.055f, 0.555f, 0.255f), Vector3.zero, Vector3.one * 0.022f, eye);
            foreach (float side in new[] { -1f, 1f })
            {
                Part(PrimitiveType.Capsule, hips, new Vector3(0.17f * side, 0.27f, 0.17f), new Vector3(60f, 0f, 20f * side), new Vector3(0.06f, 0.08f, 0.06f), skin);
                Part(PrimitiveType.Capsule, hips, new Vector3(0.19f * side, 0.2f, 0.25f), new Vector3(110f, 0f, 0f), new Vector3(0.045f, 0.06f, 0.045f), skin);
            }
            // Plates down the back: three rows, tallest in the middle.
            for (int i = 0; i < 6; i++)
            {
                float t = i / 5f;
                float size = Mathf.Lerp(0.07f, 0.13f, Mathf.Sin(t * Mathf.PI) * 0.8f + 0.2f);
                var at = new Vector3(0f, Mathf.Lerp(0.5f, 0.2f, t), Mathf.Lerp(0.05f, -0.15f, t));
                AddPlateRow(hips, at, size, plate);
            }

            // Tail: a chain of segments from the hips, each a child of the last so a small
            // per-segment sway adds up to a swinging tail.
            tail.Clear();
            Transform parent = root.transform;
            Vector3 at0 = new Vector3(0f, 0.32f, -0.16f);
            const int Segments = 10;
            for (int i = 0; i < Segments; i++)
            {
                float t = i / (float)(Segments - 1);
                var seg = Pivot("Tail" + i, parent, i == 0 ? at0 : new Vector3(0f, -0.022f, -0.085f));
                float r = Mathf.Lerp(0.15f, 0.03f, t);
                Part(PrimitiveType.Capsule, seg, new Vector3(0f, -0.011f, -0.045f), new Vector3(90f, 0f, 0f), new Vector3(r, 0.06f, r), skin);
                if (i < Segments - 2)
                    AddPlateRow(seg, new Vector3(0f, r * 0.45f, -0.04f), Mathf.Lerp(0.08f, 0.03f, t), plate);
                tail.Add(seg);
                parent = seg;
            }

            root.transform.localScale = Vector3.one * height;
            haveLast = false;
        }

        /// <summary>Places him with his feet at worldBase, facing heading (x, z), and steps the walk cycle.</summary>
        public void Place(Vector3 worldBase, Vector2 heading, float width, float height)
        {
            if (root == null)
                return;
            root.transform.localScale = Vector3.one * height;
            root.transform.position = GameApi.WorldToScene(worldBase);
            if (heading.sqrMagnitude > 0.0001f)
                root.transform.rotation = Quaternion.LookRotation(new Vector3(heading.x, 0f, heading.y));

            // Walk cycle driven by distance covered, so it matches his speed: one stride per
            // ~0.7 body heights.
            if (haveLast)
            {
                Vector2 moved = new Vector2(worldBase.x - lastBase.x, worldBase.z - lastBase.z);
                phase += moved.magnitude / Mathf.Max(1f, height * 0.7f) * Mathf.PI * 2f;
            }
            lastBase = worldBase;
            haveLast = true;

            float s = Mathf.Sin(phase);
            legL.localRotation = Quaternion.Euler(25f * s, 0f, 0f);
            legR.localRotation = Quaternion.Euler(-25f * s, 0f, 0f);
            hips.localPosition = new Vector3(0f, 0.36f + 0.012f * Mathf.Abs(Mathf.Cos(phase)), 0f);
            hips.localRotation = Quaternion.Euler(0f, 0f, 3f * s);
            for (int i = 0; i < tail.Count; i++)
                tail[i].localRotation = Quaternion.Euler(i == 0 ? -8f : 2f, 4f * Mathf.Sin(phase - i * 0.45f), 0f);
        }

        public void Hide()
        {
            if (root == null)
                return;
            Object.Destroy(root);
            root = null;
            tail.Clear();
        }

        private static void AddPlateRow(Transform parent, Vector3 at, float size, Material mat)
        {
            // Diamond plates: thin cubes turned 45 degrees, a big one in the middle and two smaller beside it.
            Part(PrimitiveType.Cube, parent, at + new Vector3(0f, size * 0.35f, 0f), new Vector3(45f, 0f, 0f), new Vector3(0.012f, size, size), mat);
            Part(PrimitiveType.Cube, parent, at + new Vector3(-0.04f, size * 0.15f, 0f), new Vector3(45f, 0f, -15f), new Vector3(0.01f, size * 0.65f, size * 0.65f), mat);
            Part(PrimitiveType.Cube, parent, at + new Vector3(0.04f, size * 0.15f, 0f), new Vector3(45f, 0f, 15f), new Vector3(0.01f, size * 0.65f, size * 0.65f), mat);
        }

        private static Transform Pivot(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        private static void Part(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 euler, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            // Visual only: the footprint code decides what he hits, not physics.
            var collider = go.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            if (mat != null)
                go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private static Material MakeMaterial(Color color)
        {
            // UNVERIFIED in game: which built-in shaders survive in the player build. If none of
            // these is found the primitive keeps its default material (magenta if stripped).
            foreach (string name in new[] { "Standard", "Legacy Shaders/Diffuse", "Diffuse", "Unlit/Color" })
            {
                var shader = Shader.Find(name);
                if (shader != null)
                    return new Material(shader) { color = color };
            }
            return null;
        }
    }
}
