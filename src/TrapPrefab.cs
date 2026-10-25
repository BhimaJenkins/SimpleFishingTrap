using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SimpleFishingTrap
{
    /// <summary>
    /// Builds the "SimpleFishingTrap" piece at runtime and registers it with the game (no Jotunn).
    ///
    /// The trap starts as a copy of the vanilla wooden chest — that gives us working wear/damage,
    /// build and destroy sounds, and a placeholder look. If Paul's model bundle is found next to the
    /// DLL, the chest visuals are swapped for that model.
    /// </summary>
    internal static class TrapPrefab
    {
        internal const string PrefabName = "SimpleFishingTrap";
        private const string BasePrefabName = "piece_chest_wood";
        private const string HammerName = "Hammer";

        internal const string BundleFileName = "simplefishingtrap";
        internal const string ModelAssetName = "SimpleFishingTrap_Model";
        internal const string IconAssetName = "SimpleFishingTrap_Icon";

        internal static GameObject Prefab;
        private static GameObject _holder;
        private static AssetBundle _bundle;
        private static bool _bundleTried;

        private static List<GameObject> _sceneList; // ZNetScene prefab list, for lookups before ZNetScene.instance exists

        // ------------------------------------------------------------ ZNetScene.Awake (prefix)

        internal static void Register(ZNetScene scene)
        {
            if (scene?.m_prefabs == null) return;
            _sceneList = scene.m_prefabs;

            if (Prefab == null) Prefab = Build();
            if (Prefab == null) return;

            if (!scene.m_prefabs.Contains(Prefab)) scene.m_prefabs.Add(Prefab);

            var hammer = Find(HammerName)?.GetComponent<ItemDrop>();
            var table = hammer?.m_itemData?.m_shared?.m_buildPieces;
            if (table == null)
                Plugin.Log.LogError("Couldn't find the Hammer's build table — the trap won't appear in the build menu.");
            else if (!table.m_pieces.Contains(Prefab))
                table.m_pieces.Add(Prefab);

            ApplyRecipe();
        }

        internal static GameObject Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var fromScene = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
            if (fromScene != null) return fromScene;
            if (_sceneList != null)
                foreach (var p in _sceneList)
                    if (p != null && p.name == name) return p;
            return ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(name) : null;
        }

        // ------------------------------------------------------------ prefab construction

        private static GameObject Build()
        {
            var basePrefab = Find(BasePrefabName);
            if (basePrefab == null)
            {
                Plugin.Log.LogError($"Base prefab '{BasePrefabName}' not found — Simple Fishing Trap disabled.");
                return null;
            }

            // Inactive holder: anything instantiated under it doesn't run Awake (so no ZDO gets created),
            // and DontDestroyOnLoad keeps it alive between worlds.
            if (_holder == null)
            {
                _holder = new GameObject("SimpleFishingTrap_Prefabs");
                _holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_holder);
            }

            var go = UnityEngine.Object.Instantiate(basePrefab, _holder.transform, false);
            go.name = PrefabName;

            foreach (var c in go.GetComponentsInChildren<Container>(true))
                UnityEngine.Object.DestroyImmediate(c);

            var piece = go.GetComponent<Piece>();
            piece.m_name = "Simple Fishing Trap";
            piece.m_description = "Slowly catches Raw Fish. Must be placed right next to water. " +
                                  "Holds a limited number of fish — empty it to keep it catching.";
            piece.m_category = Piece.PieceCategory.Crafting;
            piece.m_groundOnly = false;
            piece.m_noInWater = false;

            var wnt = go.GetComponent<WearNTear>();
            if (wnt != null) wnt.m_noRoofWear = false; // doesn't rot in the rain

            bool customModel = TrySwapInModel(go, wnt, piece);
            if (!customModel)
                Plugin.Log.LogInfo($"No custom model found ('{ObjModel.ObjFile}' next to the DLL) — using placeholder chest model.");

            go.AddComponent<FishTrap>();
            return go;
        }

        private static bool TrySwapInModel(GameObject go, WearNTear wnt, Piece piece)
        {
            // The chest's material uses Valheim's piece shader; the OBJ model reuses it with new textures.
            var chestRenderer = go.GetComponentInChildren<Renderer>(true);
            var baseMaterial = chestRenderer != null ? chestRenderer.sharedMaterial : null;

            GameObject model = null;
            Sprite icon = null;
            string source = null;

            // 1) Unity AssetBundle (optional, for anyone who prefers Unity).
            var bundle = LoadBundle();
            if (bundle != null)
            {
                model = bundle.LoadAsset<GameObject>(ModelAssetName) ?? bundle.LoadAllAssets<GameObject>().FirstOrDefault();
                icon = bundle.LoadAsset<Sprite>(IconAssetName) ?? bundle.LoadAllAssets<Sprite>().FirstOrDefault();
                if (model != null) source = "AssetBundle";
            }

            // 2) Plain files next to the DLL: SimpleFishingTrap.obj + PNG textures. No Unity needed.
            if (model == null)
            {
                string objPath = FindFile(ObjModel.ObjFile);
                if (objPath != null)
                {
                    model = ObjModel.Load(objPath, baseMaterial, _holder.transform);
                    if (model != null) source = objPath;
                }
            }

            if (icon == null) icon = ObjModel.LoadSprite(FindFile(ObjModel.IconFile));
            if (icon != null) piece.m_icon = icon;

            if (model == null) return false;
            Plugin.Log.LogInfo($"Using custom trap model from {source}.");

            // Strip the chest's look and collision (keep the structure: WearNTear, effects, snap points).
            foreach (var lod in go.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(lod);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) UnityEngine.Object.DestroyImmediate(r);
            foreach (var f in go.GetComponentsInChildren<MeshFilter>(true)) UnityEngine.Object.DestroyImmediate(f);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);

            // WearNTear swaps between new / worn / broken visuals by health; give each one a copy.
            var parents = new List<Transform>();
            if (wnt != null)
                foreach (var v in new[] { wnt.m_new, wnt.m_worn, wnt.m_broken })
                    if (v != null && !parents.Contains(v.transform)) parents.Add(v.transform);
            if (parents.Count == 0) parents.Add(go.transform);

            int pieceLayer = LayerMask.NameToLayer("piece");
            var valheimShader = Shader.Find("Custom/Piece");

            foreach (var parent in parents)
            {
                var inst = UnityEngine.Object.Instantiate(model, parent, false);
                inst.name = ModelAssetName;
                inst.transform.localPosition = Vector3.zero;
                inst.transform.localRotation = Quaternion.identity;

                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                    if (pieceLayer >= 0) t.gameObject.layer = pieceLayer;

                if (valheimShader != null)
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in r.sharedMaterials)
                            if (m != null && m.shader != null && m.shader.name == "Standard")
                                m.shader = valheimShader; // gets wet/snow like vanilla pieces

                if (inst.GetComponentInChildren<Collider>(true) == null)
                    AddFallbackCollider(inst);
            }
            return true;
        }

        private static void AddFallbackCollider(GameObject inst)
        {
            var mf = inst.GetComponentInChildren<MeshFilter>(true);
            var box = inst.AddComponent<BoxCollider>();
            if (mf != null && mf.sharedMesh != null)
            {
                var b = mf.sharedMesh.bounds;
                box.center = b.center;
                box.size = b.size;
            }
            Plugin.Log.LogWarning("Model has no collider; added a box collider around the first mesh.");
        }

        /// <summary>
        /// Looks for a mod file next to the DLL, in an Assets subfolder, or anywhere under plugins
        /// (r2modman flattens folders).
        /// </summary>
        internal static string FindFile(string fileName)
        {
            var candidates = new List<string>
            {
                Path.Combine(Plugin.PluginDir, fileName),
                Path.Combine(Path.Combine(Plugin.PluginDir, "Assets"), fileName)
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
            try
            {
                var found = Directory.GetFiles(BepInEx.Paths.PluginPath, fileName, SearchOption.AllDirectories);
                if (found.Length > 0) return found[0];
            }
            catch { /* ignore */ }
            return null;
        }

        private static AssetBundle LoadBundle()
        {
            if (_bundleTried) return _bundle;
            _bundleTried = true;

            string path = FindFile(BundleFileName);
            if (path == null) return null;
            try
            {
                _bundle = AssetBundle.LoadFromFile(path);
                if (_bundle != null) Plugin.Log.LogInfo($"Loaded model bundle: {path}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Couldn't load bundle '{path}': {ex.Message}");
            }
            return _bundle;
        }

        // ------------------------------------------------------------ build cost

        internal static void ApplyRecipe()
        {
            if (Prefab == null) return;
            var piece = Prefab.GetComponent<Piece>();
            if (piece == null) return;

            var reqs = new List<Piece.Requirement>();
            foreach (var part in (ConfigSync.Recipe ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var bits = part.Split(':');
                string item = bits[0].Trim();
                int amount = 1;
                if (bits.Length > 1 && !int.TryParse(bits[1].Trim(), out amount)) amount = 1;
                if (item.Length == 0 || amount <= 0) continue;

                var drop = Find(item)?.GetComponent<ItemDrop>();
                if (drop == null)
                {
                    Plugin.Log.LogWarning($"Recipe item '{item}' not found (prefab names are case-sensitive).");
                    continue;
                }
                reqs.Add(new Piece.Requirement { m_resItem = drop, m_amount = amount, m_recover = true });
            }

            if (reqs.Count == 0)
            {
                Plugin.Log.LogWarning("Recipe is empty or invalid — falling back to Wood:6, LeatherScraps:10.");
                var wood = Find("Wood")?.GetComponent<ItemDrop>();
                var scraps = Find("LeatherScraps")?.GetComponent<ItemDrop>();
                if (wood != null) reqs.Add(new Piece.Requirement { m_resItem = wood, m_amount = 6, m_recover = true });
                if (scraps != null) reqs.Add(new Piece.Requirement { m_resItem = scraps, m_amount = 10, m_recover = true });
            }
            piece.m_resources = reqs.ToArray();

            string stationName = ConfigSync.CraftingStation;
            piece.m_craftingStation = string.IsNullOrWhiteSpace(stationName)
                ? null
                : Find(stationName.Trim())?.GetComponent<CraftingStation>();
            if (!string.IsNullOrWhiteSpace(stationName) && piece.m_craftingStation == null)
                Plugin.Log.LogWarning($"Crafting station '{stationName}' not found — trap can be built anywhere.");
        }
    }
}
