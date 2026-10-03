using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch-mode builder for the Kaiju asset bundle:
///   Unity.exe -batchmode -projectPath . -executeMethod BuildKaiju.Build -kaijuOut <dir> -quit
/// Makes prefab "Kaiju" from Assets/Model/godzilla.glb: feet at the origin, facing +z, 1 unit
/// tall, Standard-shader materials, legacy Animation playing the walk on loop (plus "roar" and
/// "breath", if the model has clips so named, holding only the bones they move, for the mod to
/// layer on top), and an empty
/// "Mouth" on the head bone (forward = where the breath goes). The dorsal plate material
/// (name contains "Scales") has emission enabled at black so the mod can light it. Also bundles
/// the atomic breath materials KaijuBeam, KaijuSpark and KaijuSmoke. Writes kaiju.unity3d,
/// preview.png and preview-glow.png (plates lit) to the output folder.
/// </summary>
public static class BuildKaiju
{
    const string ModelPath = "Assets/Model/godzilla.glb";
    const string PrefabPath = "Assets/Kaiju.prefab";
    const string GenDir = "Assets/Generated";
    const string PlateMaterialKey = "Scales";

    public static void Build()
    {
        string outDir = Arg("-kaijuOut") ?? Path.GetFullPath("Build");
        Directory.CreateDirectory(outDir);
        if (!AssetDatabase.IsValidFolder(GenDir))
            AssetDatabase.CreateFolder("Assets", "Generated");

        var all = AssetDatabase.LoadAllAssetsAtPath(ModelPath);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
            throw new Exception("Model did not import: " + ModelPath);
        var clips = all.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToList();
        Debug.Log("[BuildKaiju] clips: " + string.Join(", ", clips.Select(c => c.name + " " + c.length + "s legacy=" + c.legacy)));

        var root = new GameObject("Kaiju");
        var inst = (GameObject)UnityEngine.Object.Instantiate(model, root.transform);
        inst.name = "Model";

        // Legacy Animation: no Animator controller needed, plays straight from the bundle.
        var anim = inst.GetComponent<Animation>();
        if (anim == null) anim = inst.AddComponent<Animation>(); // not ??: Unity fakes null for missing components
        AnimationClip walk = null;
        // Overlays (optional) play on top of the walk: "roar" keeps only the bones it moves (neck,
        // head, jaw) so he walks on while roaring; "breath" holds the whole body (he stands planted).
        var overlays = new List<AnimationClip>();
        foreach (var key in new[] { "roar", "breath" })
        {
            var src = clips.FirstOrDefault(c => c.name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0);
            if (src == null)
                continue;
            overlays.Add(src);
            var clip = Legacy(src, key);
            if (key == "roar")
                StripStillCurves(clip);
            clip.wrapMode = WrapMode.Once;
            anim.AddClip(clip, key);
        }
        var walks = clips.Where(c => !overlays.Contains(c)).ToList();
        if (walks.Count > 0)
        {
            walk = walks.FirstOrDefault(c => c.name.IndexOf("walk", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? walks.OrderByDescending(c => c.length).First();
            walk = Legacy(walk, "walk");
            walk.wrapMode = WrapMode.Loop;
            anim.AddClip(walk, "walk");
            anim.clip = walk;
            anim.wrapMode = WrapMode.Loop;
            anim.playAutomatically = true;
            anim.cullingType = AnimationCullingType.AlwaysAnimate;
            walk.SampleAnimation(inst, 0f);
        }

        // Orient: the long horizontal axis runs head to tail; the head end is the one nearer the feet.
        var verts = BakedVertices(root);
        float minY = verts.Min(v => v.y), maxY = verts.Max(v => v.y);
        float h = maxY - minY;
        var feet = verts.Where(v => v.y < minY + 0.05f * h).ToList();
        Vector3 feetC = new Vector3(feet.Average(v => v.x), 0f, feet.Average(v => v.z));
        float minX = verts.Min(v => v.x), maxX = verts.Max(v => v.x);
        float minZ = verts.Min(v => v.z), maxZ = verts.Max(v => v.z);
        Vector3 front;
        if (maxX - minX > maxZ - minZ)
            front = (maxX - feetC.x) < (feetC.x - minX) ? Vector3.right : Vector3.left;
        else
            front = (maxZ - feetC.z) < (feetC.z - minZ) ? Vector3.forward : Vector3.back;
        Debug.Log("[BuildKaiju] raw bounds x " + minX + ".." + maxX + " y " + minY + ".." + maxY + " z " + minZ + ".." + maxZ + ", feet " + feetC + ", front " + front);
        inst.transform.rotation = Quaternion.FromToRotation(front, Vector3.forward) * inst.transform.rotation;

        // Pivot at the feet, ground at y = 0, height 1.
        verts = BakedVertices(root);
        minY = verts.Min(v => v.y);
        h = verts.Max(v => v.y) - minY;
        feet = verts.Where(v => v.y < minY + 0.05f * h).ToList();
        feetC = new Vector3(feet.Average(v => v.x), minY, feet.Average(v => v.z));
        inst.transform.position -= feetC;
        var scaler = new GameObject("Scale").transform;
        scaler.SetParent(root.transform, false);
        inst.transform.SetParent(scaler, true);
        scaler.localScale = Vector3.one / h;
        verts = BakedVertices(root);
        Debug.Log("[BuildKaiju] final bounds x " + verts.Min(v => v.x) + ".." + verts.Max(v => v.x) + " y " + verts.Min(v => v.y) + ".." + verts.Max(v => v.y) + " z " + verts.Min(v => v.z) + ".." + verts.Max(v => v.z));
        Vector3? snout = AddMouth(root);
        SplitPlates(root, snout);

        // Root motion check: a walk that moves the hips would slide him backwards each loop.
        if (walk != null)
        {
            var hips = inst.GetComponentsInChildren<SkinnedMeshRenderer>().Select(r => r.rootBone).FirstOrDefault(b => b != null);
            if (hips != null)
            {
                walk.SampleAnimation(inst, 0f);
                Vector3 a = root.transform.InverseTransformPoint(hips.position);
                walk.SampleAnimation(inst, walk.length);
                Vector3 b = root.transform.InverseTransformPoint(hips.position);
                walk.SampleAnimation(inst, 0f);
                Debug.Log("[BuildKaiju] root bone " + hips.name + " moves " + (b - a) + " (body heights) over one loop");
            }
        }

        // Standard-shader copies of the materials: the game is built-in pipeline and keeps Standard.
        var made = new Dictionary<Material, Material>();
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                if (!made.TryGetValue(mats[i], out var m))
                {
                    m = ToStandard(mats[i]);
                    if (m.name.Contains(PlateMaterialKey))
                    {
                        // Emission on at black: the mod raises _EmissionColor for the breath charge-up.
                        // Enabling it here keeps the _EMISSION shader variant in the bundle.
                        m.EnableKeyword("_EMISSION");
                        m.SetColor("_EmissionColor", Color.black);
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
                    AssetDatabase.CreateAsset(m, GenDir + "/" + Safe(mats[i].name) + ".mat");
                    made[mats[i]] = m;
                }
                mats[i] = m;
            }
            r.sharedMaterials = mats;
            if (r is SkinnedMeshRenderer smr)
                smr.updateWhenOffscreen = true; // he is huge; stale bounds would cull him while visible
        }

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        RenderPreview(root, Path.Combine(outDir, "preview.png"));
        foreach (var m in made.Values.Where(m => m.name.Contains(PlateMaterialKey)))
            m.SetColor("_EmissionColor", new Color(0.35f, 0.7f, 1f) * 3f);
        RenderPreview(root, Path.Combine(outDir, "preview-glow.png"));
        foreach (var m in made.Values.Where(m => m.name.Contains(PlateMaterialKey)))
            m.SetColor("_EmissionColor", Color.black);
        UnityEngine.Object.DestroyImmediate(root);
        var effectPaths = MakeEffectMaterials();
        AssetDatabase.SaveAssets();

        var build = new AssetBundleBuild { assetBundleName = "kaiju.unity3d", assetNames = new[] { PrefabPath }.Concat(effectPaths).ToArray() };
        string tmp = Path.Combine(Path.GetTempPath(), "kaiju-bundle");
        Directory.CreateDirectory(tmp);
        BuildPipeline.BuildAssetBundles(tmp, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        File.Copy(Path.Combine(tmp, "kaiju.unity3d"), Path.Combine(outDir, "kaiju.unity3d"), true);
        Debug.Log("[BuildKaiju] wrote " + Path.Combine(outDir, "kaiju.unity3d"));
    }

    /// <summary>A legacy copy of an imported clip, saved under Generated (imported clips are read-only).</summary>
    static AnimationClip Legacy(AnimationClip src, string name)
    {
        var c = UnityEngine.Object.Instantiate(src);
        c.name = name;
        c.legacy = true;
        AssetDatabase.CreateAsset(c, GenDir + "/" + name + ".anim");
        return c;
    }

    /// <summary>
    /// Removes the curves of every transform property that never changes in the clip (the glTF
    /// export samples all bones), so a clip layered over the walk only overrides what it moves.
    /// </summary>
    static void StripStillCurves(AnimationClip clip)
    {
        var bindings = AnimationUtility.GetCurveBindings(clip);
        int kept = 0, removed = 0;
        foreach (var group in bindings.GroupBy(b => b.path + "|" + b.propertyName.Substring(0, Math.Max(0, b.propertyName.LastIndexOf('.')))))
        {
            bool moves = group.Any(b =>
            {
                var keys = AnimationUtility.GetEditorCurve(clip, b).keys;
                return keys.Length > 1 && keys.Max(k => k.value) - keys.Min(k => k.value) > 1e-4f;
            });
            foreach (var b in group)
            {
                if (moves) { kept++; continue; }
                AnimationUtility.SetEditorCurve(clip, b, null);
                removed++;
            }
        }
        EditorUtility.SetDirty(clip);
        Debug.Log("[BuildKaiju] " + clip.name + ": kept " + kept + " curves, removed " + removed + " still ones");
    }

    /// <summary>
    /// Adds an empty "Mouth" at the front of the snout, parented to the bone that moves it most,
    /// facing his front. The mod fires the breath from it and turns that bone (and its parent) to aim.
    /// </summary>
    static Vector3? AddMouth(GameObject root)
    {
        var all = new List<(Vector3 pos, BoneWeight w, Transform[] bones)>();
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = new Mesh();
            smr.BakeMesh(mesh, true);
            var m = root.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;
            var v = mesh.vertices;
            var w = smr.sharedMesh.boneWeights;
            for (int i = 0; i < v.Length && i < w.Length; i++)
                all.Add((m.MultiplyPoint3x4(v[i]), w[i], smr.bones));
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        // Snout tip: the front-most point of the head (above the arms).
        var head = all.Where(a => a.pos.y > 0.7f).ToList();
        if (head.Count == 0)
        {
            Debug.LogWarning("[BuildKaiju] no head vertices found; no Mouth marker");
            return null;
        }
        Vector3 tip = head.OrderByDescending(a => a.pos.z).First().pos;
        Vector3 mouth = tip + new Vector3(0f, -0.02f, -0.015f);
        var score = new Dictionary<Transform, float>();
        foreach (var a in all.Where(a => (a.pos - tip).sqrMagnitude < 0.06f * 0.06f))
        {
            void Add(int idx, float wt)
            {
                if (wt <= 0f || idx < 0 || idx >= a.bones.Length || a.bones[idx] == null) return;
                score.TryGetValue(a.bones[idx], out var s0);
                score[a.bones[idx]] = s0 + wt;
            }
            Add(a.w.boneIndex0, a.w.weight0); Add(a.w.boneIndex1, a.w.weight1);
            Add(a.w.boneIndex2, a.w.weight2); Add(a.w.boneIndex3, a.w.weight3);
        }
        if (score.Count == 0)
        {
            Debug.LogWarning("[BuildKaiju] no bone weights near the snout; no Mouth marker");
            return tip;
        }
        Transform bone = score.OrderByDescending(kv => kv.Value).First().Key;
        var marker = new GameObject("Mouth").transform;
        marker.position = root.transform.TransformPoint(mouth);
        marker.rotation = Quaternion.LookRotation(root.transform.forward);
        marker.SetParent(bone, true);
        Debug.Log("[BuildKaiju] mouth at " + mouth + " on bone " + bone.name + " (parent " + (bone.parent ? bone.parent.name : "none") + ")");
        return tip;
    }

    /// <summary>
    /// The plate material may also cover claws and teeth. Moves those triangles to a copy of the
    /// material without "Scales" in its name, so only the dorsal plates glow. Classified by
    /// position in the 1-unit, +z-facing pose: feet claws are near the ground, hand claws are
    /// forward and below the head, teeth are near the snout.
    /// </summary>
    static void SplitPlates(GameObject root, Vector3? snout)
    {
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mats = smr.sharedMaterials;
            int sub = Array.FindIndex(mats, m => m != null && m.name.Contains(PlateMaterialKey));
            // Models whose plates have their own material (named "...NoSplit...") need no split.
            if (sub < 0 || mats[sub].name.Contains("NoSplit"))
                continue;
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var tm = root.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;
            var pos = baked.vertices.Select(v => tm.MultiplyPoint3x4(v)).ToArray();
            UnityEngine.Object.DestroyImmediate(baked);

            var mesh = UnityEngine.Object.Instantiate(smr.sharedMesh);
            mesh.name = smr.sharedMesh.name + "_split";
            var tris = mesh.GetTriangles(sub);
            var plates = new List<int>();
            var other = new List<int>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 c = (pos[tris[i]] + pos[tris[i + 1]] + pos[tris[i + 2]]) / 3f;
                bool claw = c.y < 0.1f || (c.z > 0.3f && c.y < 0.72f);
                bool tooth = snout.HasValue && (c - snout.Value).magnitude < 0.09f;
                (claw || tooth ? other : plates).AddRange(new[] { tris[i], tris[i + 1], tris[i + 2] });
            }
            if (other.Count == 0)
                continue;
            mesh.subMeshCount = mats.Length + 1;
            mesh.SetTriangles(plates, sub);
            mesh.SetTriangles(other, mats.Length);
            AssetDatabase.CreateAsset(mesh, GenDir + "/" + Safe(mesh.name) + ".asset");
            smr.sharedMesh = mesh;
            var claws = new Material(mats[sub]) { name = "GZ_Claws" };
            smr.sharedMaterials = mats.Concat(new[] { claws }).ToArray();
            Debug.Log("[BuildKaiju] plates: " + plates.Count / 3 + " triangles glow, " + other.Count / 3 + " claw/teeth triangles moved to GZ_Claws");
        }
    }

    /// <summary>Additive beam and spark materials and an alpha-blended smoke material, with generated soft textures.</summary>
    static string[] MakeEffectMaterials()
    {
        var beamTex = SoftTexture("kaiju_beam_tex", (u, v) => Mathf.Exp(-Mathf.Pow((v - 0.5f) * 4.5f, 2f)));
        var dotTex = SoftTexture("kaiju_dot_tex", (u, v) =>
        {
            float d = new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f;
            return Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d);
        });
        return new[]
        {
            EffectMaterial("KaijuBeam", "Legacy Shaders/Particles/Additive", beamTex),
            EffectMaterial("KaijuSpark", "Legacy Shaders/Particles/Additive", dotTex),
            EffectMaterial("KaijuSmoke", "Legacy Shaders/Particles/Alpha Blended", dotTex),
        };
    }

    static string EffectMaterial(string name, string shader, Texture2D tex)
    {
        var sh = Shader.Find(shader);
        if (sh == null)
            throw new Exception("Shader not found: " + shader);
        var m = new Material(sh) { name = name };
        m.SetTexture("_MainTex", tex);
        m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        string path = GenDir + "/" + name + ".mat";
        AssetDatabase.CreateAsset(m, path);
        return path;
    }

    static Texture2D SoftTexture(string name, Func<float, float, float> alpha)
    {
        const int N = 64;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp };
        var px = new Color[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float a = alpha((x + 0.5f) / N, (y + 0.5f) / N);
                px[y * N + x] = new Color(a, a, a, a);
            }
        t.SetPixels(px);
        t.Apply();
        AssetDatabase.CreateAsset(t, GenDir + "/" + name + ".asset");
        return t;
    }

    static List<Vector3> BakedVertices(GameObject root)
    {
        var list = new List<Vector3>();
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            var mesh = new Mesh();
            smr.BakeMesh(mesh, true);
            var m = root.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;
            list.AddRange(mesh.vertices.Select(v => m.MultiplyPoint3x4(v)));
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
        {
            var m = root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            list.AddRange(mf.sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v)));
        }
        return list;
    }

    static Material ToStandard(Material src)
    {
        var m = new Material(Shader.Find("Standard")) { name = src.name };
        var tex = FirstTexture(src, "baseColorTexture", "_BaseMap", "_MainTex");
        if (tex != null) m.SetTexture("_MainTex", tex);
        foreach (var p in new[] { "baseColorFactor", "_BaseColor", "_Color" })
            if (src.HasProperty(p)) { m.color = src.GetColor(p); break; }
        var normal = FirstTexture(src, "normalTexture", "_BumpMap");
        if (normal != null)
        {
            m.SetTexture("_BumpMap", normal);
            m.EnableKeyword("_NORMALMAP");
        }
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Glossiness", 0.25f);
        Debug.Log("[BuildKaiju] material " + src.name + " (" + src.shader.name + ") tex=" + (tex ? tex.name : "none") + " normal=" + (normal ? normal.name : "none"));
        return m;
    }

    static Texture FirstTexture(Material m, params string[] props)
    {
        foreach (var p in props)
            if (m.HasProperty(p) && m.GetTexture(p) != null)
                return m.GetTexture(p);
        return null;
    }

    static void RenderPreview(GameObject root, string path)
    {
        var camGo = new GameObject("PreviewCam");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.65f, 0.75f);
        cam.fieldOfView = 30f;
        camGo.transform.position = new Vector3(2.6f, 0.9f, 1.6f);
        camGo.transform.LookAt(new Vector3(0f, 0.45f, -0.3f));
        var lightGo = new GameObject("PreviewLight");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        lightGo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
        RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);
        var rt = new RenderTexture(1024, 768, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1024, 768, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1024, 768), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(camGo);
        UnityEngine.Object.DestroyImmediate(lightGo);
        Debug.Log("[BuildKaiju] preview " + path);
    }

    static string Safe(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
