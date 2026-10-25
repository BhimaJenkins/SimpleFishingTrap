using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace SimpleFishingTrap
{
    /// <summary>
    /// Loads the trap model from plain files next to the DLL, so no Unity install is needed:
    ///
    ///   SimpleFishingTrap.obj          the model (metres, pivot at bottom centre)
    ///   SimpleFishingTrap_Albedo.png   colour texture
    ///   SimpleFishingTrap_Normal.png   normal map (optional)
    ///   SimpleFishingTrap_Icon.png     build-menu icon (optional)
    ///
    /// Each named object ("o ..." in the OBJ) becomes its own child:
    ///   Collider*    → invisible box collider sized to that object
    ///   FishVisual*  → shown progressively as the trap fills up
    ///   anything else → visible mesh
    /// </summary>
    internal static class ObjModel
    {
        internal const string ObjFile = "SimpleFishingTrap.obj";
        internal const string AlbedoFile = "SimpleFishingTrap_Albedo.png";
        internal const string NormalFile = "SimpleFishingTrap_Normal.png";
        internal const string IconFile = "SimpleFishingTrap_Icon.png";

        private class ObjObject
        {
            public string Name;
            public readonly List<int[]> Corners = new List<int[]>(); // triangles: 3 corners of [v, vt, vn]
        }

        internal static GameObject Load(string objPath, Material baseMaterial, Transform holder)
        {
            try
            {
                var objects = Parse(objPath, out var positions, out var uvs, out var normals);
                if (objects.Count == 0)
                {
                    Plugin.Log.LogWarning($"'{objPath}' has no faces.");
                    return null;
                }

                var material = MakeMaterial(baseMaterial);

                var root = new GameObject(TrapPrefab.ModelAssetName);
                root.transform.SetParent(holder, false);

                bool hasCollider = false;
                foreach (var o in objects)
                {
                    var mesh = BuildMesh(o, positions, uvs, normals);
                    if (mesh == null) continue;

                    var child = new GameObject(o.Name);
                    child.transform.SetParent(root.transform, false);

                    if (o.Name.StartsWith("Collider", StringComparison.OrdinalIgnoreCase))
                    {
                        var box = child.AddComponent<BoxCollider>();
                        box.center = mesh.bounds.center;
                        box.size = mesh.bounds.size;
                        hasCollider = true;
                        continue;
                    }

                    child.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = child.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = material;
                    mr.shadowCastingMode = ShadowCastingMode.On;
                    mr.receiveShadows = true;
                }

                if (!hasCollider) Plugin.Log.LogInfo("OBJ has no 'Collider' object; a box will be fitted around the mesh.");
                Plugin.Log.LogInfo($"Loaded OBJ model ({objects.Count} object(s), {positions.Count} vertices).");
                return root;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to load '{objPath}': {ex}");
                return null;
            }
        }

        // ------------------------------------------------------------ OBJ parsing

        private static List<ObjObject> Parse(string path, out List<Vector3> positions, out List<Vector2> uvs, out List<Vector3> normals)
        {
            positions = new List<Vector3>();
            uvs = new List<Vector2>();
            normals = new List<Vector3>();
            var objects = new List<ObjObject>();
            ObjObject current = null;
            var inv = CultureInfo.InvariantCulture;

            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string[] p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                switch (p[0])
                {
                    // OBJ is right-handed, Unity is left-handed: mirror X (and flip triangle winding below).
                    case "v":
                        positions.Add(new Vector3(-float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv)));
                        break;
                    case "vt":
                        uvs.Add(new Vector2(float.Parse(p[1], inv), p.Length > 2 ? float.Parse(p[2], inv) : 0f));
                        break;
                    case "vn":
                        normals.Add(new Vector3(-float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv)));
                        break;
                    case "o":
                    case "g":
                        string name = p.Length > 1 ? string.Join(" ", p, 1, p.Length - 1) : "Mesh";
                        current = new ObjObject { Name = name };
                        objects.Add(current);
                        break;
                    case "f":
                        if (current == null)
                        {
                            current = new ObjObject { Name = "Mesh" };
                            objects.Add(current);
                        }
                        var poly = new List<int[]>();
                        for (int i = 1; i < p.Length; i++)
                            poly.Add(ParseCorner(p[i], positions.Count, uvs.Count, normals.Count));
                        // Fan-triangulate, reversed winding for the handedness flip.
                        for (int i = 1; i + 1 < poly.Count; i++)
                        {
                            current.Corners.Add(poly[0]);
                            current.Corners.Add(poly[i + 1]);
                            current.Corners.Add(poly[i]);
                        }
                        break;
                }
            }

            objects.RemoveAll(o => o.Corners.Count == 0);
            return objects;
        }

        private static int[] ParseCorner(string token, int vCount, int vtCount, int vnCount)
        {
            string[] s = token.Split('/');
            return new[]
            {
                Index(s, 0, vCount),
                Index(s, 1, vtCount),
                Index(s, 2, vnCount)
            };
        }

        private static int Index(string[] s, int i, int count)
        {
            if (i >= s.Length || s[i].Length == 0) return -1;
            int n = int.Parse(s[i], CultureInfo.InvariantCulture);
            return n > 0 ? n - 1 : count + n; // negative = relative to end
        }

        private static Mesh BuildMesh(ObjObject o, List<Vector3> pos, List<Vector2> uv, List<Vector3> nrm)
        {
            var verts = new List<Vector3>();
            var uvsOut = new List<Vector2>();
            var nrmOut = new List<Vector3>();
            var tris = new List<int>();
            var lookup = new Dictionary<string, int>();
            bool haveNormals = true;

            foreach (var c in o.Corners)
            {
                string key = c[0] + "/" + c[1] + "/" + c[2];
                if (!lookup.TryGetValue(key, out int idx))
                {
                    idx = verts.Count;
                    lookup[key] = idx;
                    verts.Add(pos[c[0]]);
                    uvsOut.Add(c[1] >= 0 ? uv[c[1]] : Vector2.zero);
                    if (c[2] >= 0) nrmOut.Add(nrm[c[2]]);
                    else { nrmOut.Add(Vector3.up); haveNormals = false; }
                }
                tris.Add(idx);
            }
            if (verts.Count == 0) return null;

            var mesh = new Mesh { name = o.Name };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvsOut);
            mesh.SetTriangles(tris, 0);
            if (haveNormals) mesh.SetNormals(nrmOut);
            else mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------ textures

        private static Material MakeMaterial(Material baseMaterial)
        {
            // Use a plain Unity shader that maps the texture exactly as the model's UVs say.
            // (Valheim's piece shader maps textures its own way, which scrambled the trap's look.)
            Shader plain = null;
            foreach (string name in new[] { "Standard", "Legacy Shaders/Diffuse", "Diffuse", "Mobile/Diffuse" })
            {
                plain = Shader.Find(name);
                if (plain != null) break;
            }

            if (plain != null)
            {
                var pm = new Material(plain) { name = "SimpleFishingTrap_Mat" };
                var tex = LoadTexture(TrapPrefab.FindFile(AlbedoFile), linear: false);
                if (tex != null) pm.mainTexture = tex;
                else Plugin.Log.LogWarning($"'{AlbedoFile}' not found — model will be untextured.");
                pm.color = Color.white;
                if (pm.HasProperty("_Metallic")) pm.SetFloat("_Metallic", 0f);
                if (pm.HasProperty("_Glossiness")) pm.SetFloat("_Glossiness", 0.1f);

                var nrm = LoadTexture(TrapPrefab.FindFile(NormalFile), linear: true);
                if (nrm != null && pm.HasProperty("_BumpMap"))
                {
                    pm.SetTexture("_BumpMap", nrm);
                    pm.EnableKeyword("_NORMALMAP");
                }
                Plugin.Log.LogInfo($"Trap material uses shader '{plain.name}'.");
                return pm;
            }

            Plugin.Log.LogWarning("No plain Unity shader found; falling back to a copy of the chest material.");
            Material m = baseMaterial != null
                ? new Material(baseMaterial)
                : new Material(Shader.Find("Custom/Piece"));
            m.name = "SimpleFishingTrap_Mat";

            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);

            var albedo = LoadTexture(TrapPrefab.FindFile(AlbedoFile), linear: false);
            if (albedo != null && m.HasProperty("_MainTex")) m.SetTexture("_MainTex", albedo);
            else if (albedo == null) Plugin.Log.LogWarning($"'{AlbedoFile}' not found — model will be untextured.");

            var normal = LoadTexture(TrapPrefab.FindFile(NormalFile), linear: true);
            if (m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", normal); // null clears the chest's normal map
                if (normal != null) m.EnableKeyword("_NORMALMAP");
            }

            // The copied chest material keeps the chest's texture tiling/offset, which made our
            // texture land in the wrong places. Reset every texture slot's tiling to 1x1, no offset.
            string[] texProps;
            try { texProps = m.GetTexturePropertyNames(); }
            catch { texProps = new[] { "_MainTex", "_BumpMap" }; }

            var report = new System.Text.StringBuilder();
            foreach (string prop in texProps)
            {
                if (!m.HasProperty(prop)) continue;
                var tex = m.GetTexture(prop);
                report.Append($"\n  {prop}: {(tex != null ? tex.name : "none")}, tiling {m.GetTextureScale(prop)}, offset {m.GetTextureOffset(prop)}");
                if (prop == "_MainTex" || prop == "_BumpMap")
                {
                    m.SetTextureScale(prop, Vector2.one);
                    m.SetTextureOffset(prop, Vector2.zero);
                }
            }
            Plugin.Log.LogInfo($"Trap material shader '{m.shader.name}', texture slots before reset:{report}");

            // Don't keep the chest's other maps (metal/gloss/emission) on our model.
            foreach (string prop in new[] { "_MetallicGlossMap", "_EmissionMap", "_OcclusionMap", "_DetailAlbedoMap" })
                if (m.HasProperty(prop)) m.SetTexture(prop, null);

            // No metal shine (the chest's iron bands made the copy metallic, which renders dark).
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.1f);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
            m.DisableKeyword("_METALLICGLOSSMAP");

            return m;
        }

        private static Texture2D LoadTexture(string path, bool linear)
        {
            if (path == null) return null;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
                if (!LoadImage(tex, File.ReadAllBytes(path)))
                {
                    Plugin.Log.LogWarning($"Couldn't read image '{path}'.");
                    return null;
                }
                tex.name = Path.GetFileNameWithoutExtension(path);
                tex.wrapMode = TextureWrapMode.Repeat;
                tex.filterMode = FilterMode.Trilinear;
                tex.anisoLevel = 4;
                return tex;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Couldn't load texture '{path}': {ex.Message}");
                return null;
            }
        }

        // UnityEngine.ImageConversion lives in a module built against netstandard 2.1, which a net48
        // project can't reference directly — so it's looked up by name at runtime instead.
        private static System.Reflection.MethodInfo _loadImage;

        private static bool LoadImage(Texture2D tex, byte[] data)
        {
            if (_loadImage == null)
            {
                var t = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
                _loadImage = t?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                if (_loadImage == null)
                {
                    Plugin.Log.LogError("UnityEngine.ImageConversion.LoadImage not found — textures can't be loaded.");
                    return false;
                }
            }
            return (bool)_loadImage.Invoke(null, new object[] { tex, data });
        }

        internal static Sprite LoadSprite(string path)
        {
            var tex = LoadTexture(path, linear: false);
            if (tex == null) return null;
            tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
    }
}
