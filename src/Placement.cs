using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SimpleFishingTrap
{
    /// <summary>
    /// "Must be placed near water." The ghost turns red when the trap is too far from water, and
    /// clicking shows a message saying why.
    ///
    /// All Valheim water (sea, rivers, lakes, swamps) sits at one sea level, so "near water" means:
    /// the trap's base is no more than MaxHeightAboveWater above sea level, and somewhere within
    /// MaxWaterDistance sideways the ground dips below sea level.
    /// </summary>
    internal static class Placement
    {
        private static readonly FieldInfo GhostField = AccessTools.Field(typeof(Player), "m_placementGhost");
        private static readonly FieldInfo StatusField = AccessTools.Field(typeof(Player), "m_placementStatus");
        private static readonly MethodInfo SetGhostValid = AccessTools.Method(typeof(Player), "SetPlacementGhostValid");

        private static object _invalidStatus;
        private static object _validStatus;

        private const int RingSamples = 16;
        private const float MinWaterDepth = 0.05f;

        internal static bool IsOurs(GameObject go) =>
            go != null && go.name.StartsWith(TrapPrefab.PrefabName, StringComparison.Ordinal);

        internal static bool NearWater(Vector3 pos)
        {
            var zs = ZoneSystem.instance;
            if (zs == null) return true; // can't tell; don't block

            float water = zs.m_waterLevel;
            if (pos.y - water > ConfigSync.MaxHeightAboveWater) return false;

            if (IsWater(zs, pos, water)) return true;

            float maxDist = ConfigSync.MaxWaterDistance;
            if (maxDist <= 0f) return false;

            // Check a ring at full distance and a ring halfway in.
            foreach (float r in new[] { maxDist * 0.5f, maxDist })
                for (int i = 0; i < RingSamples; i++)
                {
                    float a = i * Mathf.PI * 2f / RingSamples;
                    var p = new Vector3(pos.x + Mathf.Cos(a) * r, pos.y, pos.z + Mathf.Sin(a) * r);
                    if (IsWater(zs, p, water)) return true;
                }
            return false;
        }

        private static bool IsWater(ZoneSystem zs, Vector3 p, float water) =>
            zs.GetGroundHeight(p) < water - MinWaterDepth;

        private static void EnsureStatusValues()
        {
            if (_invalidStatus != null || StatusField == null) return;
            var t = StatusField.FieldType;
            _validStatus = Enum.Parse(t, "Valid");
            _invalidStatus = Enum.Parse(t, "Invalid");
        }

        // Postfix on Player.UpdatePlacementGhost — turn the ghost red when not near water.
        internal static void UpdatePlacementGhost_Postfix(Player __instance)
        {
            try
            {
                if (GhostField == null || StatusField == null) return;
                var ghost = GhostField.GetValue(__instance) as GameObject;
                if (!IsOurs(ghost)) return;

                EnsureStatusValues();
                if (!Equals(StatusField.GetValue(__instance), _validStatus)) return; // already invalid for another reason

                if (!NearWater(ghost.transform.position))
                {
                    StatusField.SetValue(__instance, _invalidStatus);
                    SetGhostValid?.Invoke(__instance, new object[] { false });
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogDebug($"Placement check error: {ex.Message}");
            }
        }

        // Prefix on Player.TryPlacePiece — explain why it can't be placed.
        internal static bool TryPlacePiece_Prefix(Player __instance, Piece __0, ref bool __result)
        {
            if (__0 == null || !IsOurs(__0.gameObject)) return true;

            var ghost = GhostField?.GetValue(__instance) as GameObject;
            if (ghost == null || NearWater(ghost.transform.position)) return true;

            __instance.Message(MessageHud.MessageType.Center,
                $"Must be placed within {ConfigSync.MaxWaterDistance:0.##}m of water");
            __result = false;
            return false;
        }
    }

    /// <summary>Calls Valheim's Localization by name (it lives in assembly_guiutils.dll).</summary>
    internal static class Loc
    {
        private static PropertyInfo _instance;
        private static MethodInfo _localize;
        private static bool _looked;

        internal static string Localize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            try
            {
                if (!_looked)
                {
                    _looked = true;
                    var t = AccessTools.TypeByName("Localization");
                    _instance = t == null ? null : AccessTools.Property(t, "instance");
                    _localize = t == null ? null : AccessTools.Method(t, "Localize", new[] { typeof(string) });
                }
                var loc = _instance?.GetValue(null, null);
                if (loc != null && _localize != null) return _localize.Invoke(loc, new object[] { text }) as string ?? text;
            }
            catch { /* fall through */ }
            return text;
        }
    }
}
