// Put this file in your Unity project at:  Assets/Editor/BuildSimpleFishingTrapBundle.cs
// Then use the menu:  Simple Fishing Trap > Build AssetBundle
// Output: <Unity project>/AssetBundles/simplefishingtrap  -> copy into the mod's Assets folder.
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildSimpleFishingTrapBundle
{
    [MenuItem("Simple Fishing Trap/Build AssetBundle")]
    public static void Build()
    {
        const string outDir = "AssetBundles";
        Directory.CreateDirectory(outDir);

        string[] modelGuids = AssetDatabase.FindAssets("SimpleFishingTrap_Model t:Prefab");
        string[] iconGuids = AssetDatabase.FindAssets("SimpleFishingTrap_Icon t:Sprite");
        if (modelGuids.Length == 0)
        {
            EditorUtility.DisplayDialog("Simple Fishing Trap",
                "No prefab named 'SimpleFishingTrap_Model' found. Make the model a prefab with that exact name.", "OK");
            return;
        }

        var assets = new System.Collections.Generic.List<string> { AssetDatabase.GUIDToAssetPath(modelGuids[0]) };
        if (iconGuids.Length > 0) assets.Add(AssetDatabase.GUIDToAssetPath(iconGuids[0]));

        var build = new AssetBundleBuild
        {
            assetBundleName = "simplefishingtrap",
            assetNames = assets.ToArray()
        };

        BuildPipeline.BuildAssetBundles(outDir, new[] { build },
            BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);

        EditorUtility.RevealInFinder(Path.Combine(outDir, "simplefishingtrap"));
        Debug.Log("Built simplefishingtrap bundle with: " + string.Join(", ", assets));
    }
}
