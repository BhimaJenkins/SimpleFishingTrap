using System;

namespace SimpleFishingTrap
{
    /// <summary>
    /// Server → client settings sync (no ServerSync/Jotunn). The server sends its values when a player
    /// joins and whenever they change; clients use them until they disconnect.
    /// If the server doesn't run the mod, clients use their own settings.
    /// </summary>
    internal static class ConfigSync
    {
        private const string RpcName = "SimpleFishingTrap_SyncConfig";
        private const int PackageVersion = 1;

        internal static bool HasServerConfig { get; private set; }

        private static float _secondsPerFish;
        private static int _maxFish;
        private static bool _catchUp;
        private static string _itemPrefab;
        private static float _maxDistance;
        private static float _maxHeight;
        private static string _recipe;
        private static string _station;

        internal static float SecondsPerFish => Math.Max(1f, HasServerConfig ? _secondsPerFish : Plugin.SecondsPerFish.Value);
        internal static int MaxFish => Math.Max(1, HasServerConfig ? _maxFish : Plugin.MaxFish.Value);
        internal static bool CatchUpWhileAway => HasServerConfig ? _catchUp : Plugin.CatchUpWhileAway.Value;
        internal static string ItemPrefab => HasServerConfig ? _itemPrefab : Plugin.ItemPrefab.Value;
        internal static float MaxWaterDistance => HasServerConfig ? _maxDistance : Plugin.MaxWaterDistance.Value;
        internal static float MaxHeightAboveWater => HasServerConfig ? _maxHeight : Plugin.MaxHeightAboveWater.Value;
        internal static string Recipe => HasServerConfig ? _recipe : Plugin.Recipe.Value;
        internal static string CraftingStation => HasServerConfig ? _station : Plugin.CraftingStationName.Value;

        // ------------------------------------------------------------ Harmony hooks

        internal static void OnNewConnection_Postfix(ZNetPeer peer)
        {
            try { peer?.m_rpc?.Register<ZPackage>(RpcName, RPC_Receive); }
            catch (Exception ex) { Plugin.Log.LogWarning($"Could not register sync RPC: {ex.Message}"); }
        }

        internal static void RPC_PeerInfo_Postfix(ZNet __instance, ZRpc rpc)
        {
            if (__instance == null || rpc == null || !__instance.IsServer() || !Plugin.EnforceServerConfig.Value) return;
            try { rpc.Invoke(RpcName, Build()); }
            catch (Exception ex) { Plugin.Log.LogWarning($"Could not send settings to joining player: {ex.Message}"); }
        }

        internal static void ZNet_OnDestroy_Postfix()
        {
            if (!HasServerConfig) return;
            HasServerConfig = false;
            TrapPrefab.ApplyRecipe();
            Plugin.Log.LogInfo("Left server; using local settings again.");
        }

        // ------------------------------------------------------------ send / receive

        internal static void BroadcastIfServer()
        {
            var znet = ZNet.instance;
            if (znet == null || !znet.IsServer() || !Plugin.EnforceServerConfig.Value) return;
            foreach (var peer in znet.GetPeers())
            {
                if (peer?.m_rpc == null) continue;
                try { peer.m_rpc.Invoke(RpcName, Build()); }
                catch (Exception ex) { Plugin.Log.LogWarning($"Sync to a peer failed: {ex.Message}"); }
            }
        }

        private static ZPackage Build()
        {
            var p = new ZPackage();
            p.Write(PackageVersion);
            p.Write(Plugin.SecondsPerFish.Value);
            p.Write(Plugin.MaxFish.Value);
            p.Write(Plugin.CatchUpWhileAway.Value);
            p.Write(Plugin.ItemPrefab.Value ?? "FishRaw");
            p.Write(Plugin.MaxWaterDistance.Value);
            p.Write(Plugin.MaxHeightAboveWater.Value);
            p.Write(Plugin.Recipe.Value ?? "");
            p.Write(Plugin.CraftingStationName.Value ?? "");
            return p;
        }

        private static void RPC_Receive(ZRpc rpc, ZPackage p)
        {
            if (ZNet.instance == null || ZNet.instance.IsServer()) return;
            try
            {
                int version = p.ReadInt();
                if (version != PackageVersion)
                {
                    Plugin.Log.LogWarning($"Server settings format v{version}, expected v{PackageVersion}. " +
                                          "Update the mod to match the server. Using local settings.");
                    return;
                }
                _secondsPerFish = p.ReadSingle();
                _maxFish = p.ReadInt();
                _catchUp = p.ReadBool();
                _itemPrefab = p.ReadString();
                _maxDistance = p.ReadSingle();
                _maxHeight = p.ReadSingle();
                _recipe = p.ReadString();
                _station = p.ReadString();
                HasServerConfig = true;

                TrapPrefab.ApplyRecipe();
                Plugin.Log.LogInfo("Using server settings for Simple Fishing Trap.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Bad settings package from server: {ex.Message}");
            }
        }
    }
}
