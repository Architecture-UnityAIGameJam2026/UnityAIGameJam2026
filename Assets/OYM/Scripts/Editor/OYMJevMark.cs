using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OYM.EditorTools
{
    /// Editor side of the JEV radio / lock-on / CITADEL weak points (pipeline/jevmark).
    ///   BakeWeakPoints  finds the CITADEL's red sensor lights: every LOD0 part with an emissive map is sampled through
    ///                   its mesh UVs; bright red/pink faces are clustered into glow spots per part (part space) and
    ///                   saved to Resources/OYM_CitadelWeakPoints (CitadelWeakPoints reads it at runtime)
    ///   Deploy          batch entry: bake, radio clip import settings, radio pack check
    public static class OYMJevMark
    {
        const string PrefabPath = "Assets/OYM/Enemies/Prefabs/citadel_boss.prefab";
        const string MapPath = "Assets/OYM/Resources/OYM_CitadelWeakPoints.asset";
        const string RadioDir = "Assets/OYM/Resources/OYM_Radio";

        public static void Deploy()
        {
            ImportRadio();
            ImportMusic();
            BakeWeakPoints();
            CheckRadio();
            Debug.Log("[OYM-SIGNAL] deploy done");
        }

        /// the CITADEL theme clips: compressed in memory (gapless loop, small), preloaded so PlayScheduled is exact
        static void ImportMusic()
        {
            const string dir = "Assets/OYM/Resources/OYM_Music";
            int n = 0, found = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is AudioImporter ai)) continue;
                found++;
                var s = ai.defaultSampleSettings;
                if (!ai.forceToMono && s.preloadAudioData && !ai.loadInBackground && s.loadType == AudioClipLoadType.CompressedInMemory
                    && s.compressionFormat == AudioCompressionFormat.Vorbis && Mathf.Abs(s.quality - 0.8f) < 0.01f) continue;
                ai.forceToMono = false;
                s.preloadAudioData = true;
                ai.loadInBackground = false;
                s.loadType = AudioClipLoadType.CompressedInMemory;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.8f;
                ai.defaultSampleSettings = s;
                ai.SaveAndReimport();
                n++;
            }
            foreach (var name in new[] { "intro", "loop", "end" })
                if (!Resources.Load<AudioClip>("OYM_Music/boss_citadel_" + name)) throw new System.Exception("boss music clip missing: " + name);
            Debug.Log($"[OYM-SIGNAL] boss music: {found} clips ({n} reimported: stereo, Vorbis 0.8, compressed in memory, preloaded)");
        }

        /// the user's own play check (GUI editor): the deploy steps, then the game from the main title; the editor stays open
        public static void OpenForUser()
        {
            try { Deploy(); }
            catch (System.Exception e) { Debug.LogError("[OYM-SIGNAL] deploy step failed (the game still opens): " + e); }
            Debug.Log("[OYM-SIGNAL] opening the game from the main title for the user");
            OYMMainTitle.OpenAndPlay();
        }

        /// GUI editor with -oymSignalDemo: the game scene in play mode, OYM.Signal.SignalDemo drives it and quits
        public static void PlaySignalDemo()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/OYM/Scenes/Showcase_DebrisField.unity");
            Debug.Log("[OYM-SIGNAL] entering play mode for the signal demo");
            EditorApplication.EnterPlaymode();
        }

        // ------------------------------------------------------------------ radio pack
        static void ImportRadio()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { RadioDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is AudioImporter ai)) continue;
                var s = ai.defaultSampleSettings;
                bool dirty = !ai.forceToMono || s.loadType != AudioClipLoadType.DecompressOnLoad || s.compressionFormat != AudioCompressionFormat.Vorbis || Mathf.Abs(s.quality - 0.7f) > 0.01f;
                if (!dirty) continue;
                ai.forceToMono = true;
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
                ai.defaultSampleSettings = s;
                ai.SaveAndReimport();
                n++;
            }
            Debug.Log($"[OYM-SIGNAL] radio clips: {n} reimported (mono, Vorbis 0.7, decompress on load)");
        }

        static void CheckRadio()
        {
            var cat = OYM.Signal.RadioCatalog.Load();
            int missing = 0, jev = 0;
            foreach (var l in cat.Lines)
            {
                if (l.Jev) jev++;
                if (!Resources.Load<AudioClip>(l.clipPath)) { missing++; Debug.LogError("[OYM-SIGNAL] radio clip missing: " + l.clipPath); }
            }
            Debug.Log($"[OYM-SIGNAL] radio pack: {cat.Lines.Count} lines ({jev} JEV, {cat.Lines.Count - jev} partner), {missing} clips missing, Jev key: {(OYM.Signal.JevLink.HasKey ? OYM.Signal.JevLink.KeySource : "none")}");
            if (cat.Lines.Count == 0 || missing > 0) throw new System.Exception("radio pack incomplete");
        }

        // ------------------------------------------------------------------ weak points
        sealed class Acc
        {
            public Transform part;
            public readonly List<(Vector3 p, float w)> faces = new List<(Vector3, float)>();
            public Vector3 sum, nsum;
            public float wsum;
            public Bounds bounds;
        }

        static float Glow(Color c)
        {
            float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (mx < 0.22f) return 0f;
            return c.r >= 0.22f && c.r >= c.g * 1.35f ? mx : 0f;                 // red / pink, not white or amber
        }

        [MenuItem("OYM/Signal/Bake CITADEL Weak Points")]
        public static void BakeWeakPoints()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            var texCache = new Dictionary<Texture, Texture2D>();
            var accs = new Dictionary<Transform, Acc>();
            var log = new StringBuilder();
            try
            {
                foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!mr.name.EndsWith("_LOD0")) continue;
                    var part = mr.transform.parent;
                    if (!part || part.name.Contains("_damaged") || part.name.Contains("dart")) continue;
                    var mf = mr.GetComponent<MeshFilter>();
                    var mesh = mf ? mf.sharedMesh : null;
                    var mat = mr.sharedMaterial;
                    if (!mesh || !mat || !mat.HasProperty("_EmissiveColorMap")) continue;
                    var src = mat.GetTexture("_EmissiveColorMap");
                    if (!src) continue;
                    if (!texCache.TryGetValue(src, out var img))
                    {
                        img = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                        img.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(src)));
                        texCache[src] = img;
                    }
                    var V = mesh.vertices; var UV = mesh.uv; var T = mesh.triangles;
                    if (UV == null || UV.Length != V.Length) continue;
                    if (!accs.TryGetValue(part, out var acc)) { acc = new Acc { part = part }; accs[part] = acc; }
                    var toPart = part.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    bool first = acc.faces.Count == 0 && acc.wsum == 0f;
                    for (int i = 0; i < V.Length; i += Mathf.Max(1, V.Length / 4000))
                    {
                        var q = toPart.MultiplyPoint3x4(V[i]);
                        if (first) { acc.bounds = new Bounds(q, Vector3.zero); first = false; } else acc.bounds.Encapsulate(q);
                    }
                    for (int t = 0; t < T.Length; t += 3)
                    {
                        int a = T[t], b = T[t + 1], c = T[t + 2];
                        Vector2 uc = (UV[a] + UV[b] + UV[c]) / 3f;
                        float g = Mathf.Max(Glow(img.GetPixelBilinear(uc.x, uc.y)),
                                  Mathf.Max(Glow(img.GetPixelBilinear(UV[a].x, UV[a].y)), Mathf.Max(Glow(img.GetPixelBilinear(UV[b].x, UV[b].y)), Glow(img.GetPixelBilinear(UV[c].x, UV[c].y)))));
                        if (g <= 0f) continue;
                        Vector3 pa = toPart.MultiplyPoint3x4(V[a]), pb = toPart.MultiplyPoint3x4(V[b]), pc = toPart.MultiplyPoint3x4(V[c]);
                        Vector3 cr = Vector3.Cross(pb - pa, pc - pa);
                        float area = cr.magnitude * 0.5f;
                        if (area <= 0f) continue;
                        float w = area * g;
                        Vector3 cen = (pa + pb + pc) / 3f;
                        acc.faces.Add((cen, w));
                        acc.sum += cen * w; acc.nsum += cr.normalized * w; acc.wsum += w;
                    }
                }

                // boss-space frame for the sanity log (where on the hull each light sits, -1..1 per axis)
                Bounds hull = default; bool any = false;
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!r.name.EndsWith("_LOD0") || r.name.Contains("_damaged")) continue;
                    var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                    var mb = mf.sharedMesh.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                        var q = root.transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                        if (!any) { hull = new Bounds(q, Vector3.zero); any = true; } else hull.Encapsulate(q);
                    }
                }

                var entries = new List<CitadelWeakPointMap.Entry>();
                foreach (var acc in accs.Values)
                {
                    if (acc.wsum <= 1e-6f || acc.faces.Count < 2) continue;
                    Vector3 center = acc.sum / acc.wsum;
                    // part units are tens of metres (claw ~46 m/unit): the floor is 0.5 m expressed in part units
                    var ls = acc.part.lossyScale;
                    float unit = Mathf.Max(1e-4f, Mathf.Max(Mathf.Abs(ls.x), Mathf.Max(Mathf.Abs(ls.y), Mathf.Abs(ls.z))));
                    float spotR = Mathf.Max(0.5f / unit, acc.bounds.size.magnitude * 0.06f);
                    // greedy clusters, heaviest faces first
                    acc.faces.Sort((x, y) => y.w.CompareTo(x.w));
                    var spots = new List<(Vector3 s, float w)>();
                    foreach (var f in acc.faces)
                    {
                        int k = -1;
                        for (int i = 0; i < spots.Count; i++) if (Vector3.Distance(spots[i].s / spots[i].w, f.p) < spotR) { k = i; break; }
                        if (k >= 0) spots[k] = (spots[k].s + f.p * f.w, spots[k].w + f.w);
                        else if (spots.Count < 8) spots.Add((f.p * f.w, f.w));
                    }
                    spots.Sort((x, y) => y.w.CompareTo(x.w));
                    float rr = 0f;
                    foreach (var f in acc.faces) rr += (f.p - center).sqrMagnitude * f.w;
                    float radius = Mathf.Max(spotR, Mathf.Sqrt(rr / acc.wsum));
                    var e = new CitadelWeakPointMap.Entry
                    {
                        part = acc.part.name, center = center, radius = radius, strength = acc.wsum,
                        normal = acc.nsum.sqrMagnitude > 1e-8f ? acc.nsum.normalized : Vector3.forward,
                        spots = spots.ConvertAll(s => s.s / s.w).ToArray(),
                    };
                    entries.Add(e);
                    Vector3 bossP = root.transform.InverseTransformPoint(acc.part.TransformPoint(e.spots[0]));
                    Vector3 rel = any ? new Vector3((bossP.x - hull.center.x) / hull.extents.x, (bossP.y - hull.center.y) / hull.extents.y, (bossP.z - hull.center.z) / hull.extents.z) : Vector3.zero;
                    log.AppendLine($"  {e.part}: {acc.faces.Count} glowing faces, {e.spots.Length} spots, glow radius {e.radius * unit:F1} m, strength {e.strength:F3}, main light at hull {rel.x:+0.00;-0.00} x {rel.y:+0.00;-0.00} y {rel.z:+0.00;-0.00} z, normal {acc.part.TransformVector(e.normal).normalized} (part scale {ls})");
                }
                entries.Sort((a, b) =>
                {
                    int ha = a.part.Contains("head") ? 0 : 1, hb = b.part.Contains("head") ? 0 : 1;
                    return ha != hb ? ha.CompareTo(hb) : string.CompareOrdinal(a.part, b.part);
                });

                var map = AssetDatabase.LoadAssetAtPath<CitadelWeakPointMap>(MapPath);
                bool create = !map;
                if (create) map = ScriptableObject.CreateInstance<CitadelWeakPointMap>();
                map.entries = entries;
                map.source = $"{PrefabPath} emissive maps, baked {System.DateTime.Now:yyyy-MM-dd HH:mm}";
                if (create) AssetDatabase.CreateAsset(map, MapPath); else EditorUtility.SetDirty(map);
                AssetDatabase.SaveAssets();
                Debug.Log($"[OYM-SIGNAL] CITADEL weak points baked: {entries.Count} parts with red lights -> {MapPath}\n{log}");
                if (entries.Count == 0) throw new System.Exception("no red lights found on the CITADEL parts");
            }
            finally
            {
                foreach (var t in texCache.Values) if (t) Object.DestroyImmediate(t);
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
