using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SimpleFishingTrap
{
    /// <summary>
    /// Simple Fishing Trap — a buildable trap that slowly fills with Raw Fish, like a bee hive fills
    /// with honey. Place it next to water, come back later, press E to collect.
    /// </summary>
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "BhimaJenkins.SimpleFishingTrap";
        public const string ModName = "SimpleFishingTrap";
        public const string ModVersion = "0.1.0";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static string PluginDir;

        // Catching
        internal static ConfigEntry<float> SecondsPerFish;
        internal static ConfigEntry<int> MaxFish;
        internal static ConfigEntry<bool> CatchUpWhileAway;
        internal static ConfigEntry<string> ItemPrefab;

        // Placement
        internal static ConfigEntry<float> MaxWaterDistance;
        internal static ConfigEntry<float> MaxHeightAboveWater;

        // Building
        internal static ConfigEntry<string> Recipe;
        internal static ConfigEntry<string> CraftingStationName;

        // Server
        internal static ConfigEntry<bool> EnforceServerConfig;

        private Harmony _harmony;
        private FileSystemWatcher _watcher;
        private volatile bool _reloadRequested;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            PluginDir = Path.GetDirectoryName(Info.Location);

            const string catching = "1 - Catching";
            SecondsPerFish = Config.Bind(catching, "Seconds Per Fish", 180f,
                new ConfigDescription("How long each Raw Fish takes to catch (180 = 3 minutes).",
                    new AcceptableValueRange<float>(10f, 3600f)));
            MaxFish = Config.Bind(catching, "Trap Capacity", 10,
                new ConfigDescription("How many Raw Fish a trap holds. A full trap stops catching until it is emptied.",
                    new AcceptableValueRange<int>(1, 100)));
            CatchUpWhileAway = Config.Bind(catching, "Catch While Away", true,
                "On: traps keep catching while nobody is nearby (like bee hives), and catch up when you return. " +
                "Off: traps only catch while their area is loaded.");
            ItemPrefab = Config.Bind(catching, "Item Prefab", "FishRaw",
                "Prefab name of the item the trap produces. Leave as FishRaw for vanilla Raw Fish.");

            const string placement = "2 - Placement";
            MaxWaterDistance = Config.Bind(placement, "Max Distance From Water", 0.25f,
                new ConfigDescription("How close (sideways, in metres) the trap must be to water to be placed.",
                    new AcceptableValueRange<float>(0f, 5f)));
            MaxHeightAboveWater = Config.Bind(placement, "Max Height Above Water", 0.25f,
                new ConfigDescription("How far above the water surface the trap's base may be. " +
                                      "Raise this (to ~1.5) to allow placing traps on docks.",
                    new AcceptableValueRange<float>(0f, 5f)));

            const string building = "3 - Building";
            Recipe = Config.Bind(building, "Recipe", "Wood:6, LeatherScraps:10",
                "Build cost as ItemPrefab:Amount pairs, comma-separated.");
            CraftingStationName = Config.Bind(building, "Crafting Station", "piece_workbench",
                "Prefab name of the station that must be nearby to build the trap. Leave empty for none.");

            EnforceServerConfig = Config.Bind("4 - Server", "Enforce Server Settings", true,
                "SERVER ONLY. Sends the server's settings to every player who joins, so catch rate, capacity, " +
                "placement rules and build cost are the same for everyone.");

            Recipe.SettingChanged += (_, __) => OnSettingsChanged();
            CraftingStationName.SettingChanged += (_, __) => OnSettingsChanged();
            SecondsPerFish.SettingChanged += (_, __) => OnSettingsChanged();
            MaxFish.SettingChanged += (_, __) => OnSettingsChanged();
            CatchUpWhileAway.SettingChanged += (_, __) => OnSettingsChanged();
            ItemPrefab.SettingChanged += (_, __) => OnSettingsChanged();
            MaxWaterDistance.SettingChanged += (_, __) => OnSettingsChanged();
            MaxHeightAboveWater.SettingChanged += (_, __) => OnSettingsChanged();

            _harmony = new Harmony(ModGuid);
            Patches.ApplyAll(_harmony);
            SetupConfigWatcher();

            Log.LogInfo($"{ModName} {ModVersion} loaded.");
        }

        internal static void OnSettingsChanged()
        {
            TrapPrefab.ApplyRecipe();
            ConfigSync.BroadcastIfServer();
        }

        private void Update()
        {
            if (!_reloadRequested) return;
            _reloadRequested = false;
            try
            {
                Config.Reload();
                Log.LogInfo("Config reloaded from disk.");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Could not reload config: {ex.Message}");
            }
            OnSettingsChanged();
        }

        private void SetupConfigWatcher()
        {
            try
            {
                _watcher = new FileSystemWatcher(Paths.ConfigPath, Path.GetFileName(Config.ConfigFilePath));
                _watcher.Changed += (_, __) => _reloadRequested = true;
                _watcher.Created += (_, __) => _reloadRequested = true;
                _watcher.Renamed += (_, __) => _reloadRequested = true;
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Config watcher unavailable ({ex.Message}); edits apply on restart.");
            }
        }

        private void OnDestroy()
        {
            _watcher?.Dispose();
            _harmony?.UnpatchSelf();
        }
    }
}
