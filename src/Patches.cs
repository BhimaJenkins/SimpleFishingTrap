using System;
using HarmonyLib;

namespace SimpleFishingTrap
{
    /// <summary>
    /// Each hook is applied by name and checked on its own, so if a Valheim update renames something,
    /// the log says exactly what broke and the rest keeps working.
    /// </summary>
    internal static class Patches
    {
        internal static void ApplyAll(Harmony h)
        {
            // Register the trap before ZNetScene indexes its prefab list.
            TryPatch(h, typeof(ZNetScene), new[] { "Awake" },
                prefix: Hook(typeof(Patches), nameof(ZNetScene_Awake_Prefix)));

            TryPatch(h, typeof(Player), new[] { "UpdatePlacementGhost" },
                postfix: Hook(typeof(Placement), nameof(Placement.UpdatePlacementGhost_Postfix)));
            TryPatch(h, typeof(Player), new[] { "TryPlacePiece", "PlacePiece" },
                prefix: Hook(typeof(Placement), nameof(Placement.TryPlacePiece_Prefix)));

            TryPatch(h, typeof(ZNet), new[] { "OnNewConnection" },
                postfix: Hook(typeof(ConfigSync), nameof(ConfigSync.OnNewConnection_Postfix)));
            TryPatch(h, typeof(ZNet), new[] { "RPC_PeerInfo" },
                postfix: Hook(typeof(ConfigSync), nameof(ConfigSync.RPC_PeerInfo_Postfix)));
            TryPatch(h, typeof(ZNet), new[] { "OnDestroy" },
                postfix: Hook(typeof(ConfigSync), nameof(ConfigSync.ZNet_OnDestroy_Postfix)));
        }

        private static void ZNetScene_Awake_Prefix(ZNetScene __instance)
        {
            try { TrapPrefab.Register(__instance); }
            catch (Exception ex) { Plugin.Log.LogError($"Failed to register Simple Fishing Trap: {ex}"); }
        }

        private static HarmonyMethod Hook(Type host, string name) => new HarmonyMethod(AccessTools.Method(host, name));

        private static void TryPatch(Harmony h, Type type, string[] names, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            foreach (var name in names)
            {
                var target = AccessTools.Method(type, name);
                if (target == null) continue;
                try
                {
                    h.Patch(target, prefix, postfix);
                    return;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Failed to patch {type.Name}.{name}: {ex.Message}");
                    return;
                }
            }
            Plugin.Log.LogWarning($"Could not find {type.Name}.{string.Join("/", names)} — that feature is disabled " +
                                  "(a Valheim update probably renamed it).");
        }
    }
}
