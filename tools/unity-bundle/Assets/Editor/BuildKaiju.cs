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
/// tall, Standard-shader materials, legacy Animation playing the walk on loop. Writes
/// kaiju.unity3d and preview.png to the output folder.
/// </summary>
public static class BuildKaiju
{
    const string ModelPath = "Assets/Model/godzilla.glb";
    const string PrefabPath = "Assets/Kaiju.prefab";
    const string GenDir = "Assets/Generated";

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
        if (clips.Count > 0)
        {
            walk = clips.OrderByDescending(c => c.length).First();
            if (!walk.legacy)
            {
                walk = UnityEngine.Object.Instantiate(walk);
                walk.legacy = true;
                AssetDatabase.CreateAsset(walk, GenDir + "/walk.anim");
            }
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
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();

        var build = new AssetBundleBuild { assetBundleName = "kaiju.unity3d", assetNames = new[] { PrefabPath } };
        string tmp = Path.Combine(Path.GetTempPath(), "kaiju-bundle");
        Directory.CreateDirectory(tmp);
        BuildPipeline.BuildAssetBundles(tmp, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        File.Copy(Path.Combine(tmp, "kaiju.unity3d"), Path.Combine(outDir, "kaiju.unity3d"), true);
        Debug.Log("[BuildKaiju] wrote " + Path.Combine(outDir, "kaiju.unity3d"));
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
