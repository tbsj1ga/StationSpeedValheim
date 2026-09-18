using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace StationSpeed
{
    public partial class StationSpeedPlugin
    {
        // ------------------------------------------------------------------
        // config
        // ------------------------------------------------------------------
        private ConfigEntry<bool> _cfgEnabled;
        private ConfigEntry<bool> _cfgDebug;

        private ConfigEntry<float> _cfgSmelter;
        private ConfigEntry<float> _cfgBlastFurnace;
        private ConfigEntry<float> _cfgCharcoalKiln;
        private ConfigEntry<float> _cfgSpinningWheel;
        private ConfigEntry<float> _cfgWindmill;
        private ConfigEntry<float> _cfgEitrRefinery;
        private ConfigEntry<float> _cfgOtherSmelter;

        private ConfigEntry<float> _cfgCookingStation;
        private ConfigEntry<float> _cfgOven;
        private ConfigEntry<float> _cfgOtherCooking;
        private ConfigEntry<bool> _cfgScaleOvenFuel;

        private ConfigEntry<float> _cfgFermenter;
        private ConfigEntry<float> _cfgCrops;
        private ConfigEntry<float> _cfgSaplings;
        private ConfigEntry<float> _cfgBeehive;
        private ConfigEntry<float> _cfgSapCollector;

        private ConfigEntry<string> _cfgOverrides;
        private readonly Dictionary<string, float> _overrides = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        private ConfigEntry<bool> _cfgHostShift;
        private ConfigEntry<float> _cfgHostInterval;
        private ConfigEntry<bool> _cfgSync;

        // Every multiplier, in binding order: what the server sends and the status lists.
        private readonly List<ConfigEntry<float>> _multipliers = new List<ConfigEntry<float>>();

        private const float MinMultiplier = 0.1f;
        private const float MaxMultiplier = 100f;

        private ConfigEntry<float> BindMultiplier(string section, string key, string what)
        {
            ConfigEntry<float> e = Config.Bind(section, key, 1f,
                new ConfigDescription("Speed multiplier for " + what + ". 2 = twice as fast, 0.5 = twice as slow.",
                    new AcceptableValueRange<float>(MinMultiplier, MaxMultiplier)));
            _multipliers.Add(e);
            return e;
        }

        private void BindConfig()
        {
            _cfgEnabled = Config.Bind("01 General", "Enabled", true, "Master switch. Loaded stations go back to vanilla speed at once; the fermenters and plants already shifted stay shifted.");
            _cfgDebug = Config.Bind("01 General", "Debug", false, "Log every station the mod touches.");

            _cfgSmelter = BindMultiplier("02 Smelters", "Smelter", "the smelter (prefab smelter)");
            _cfgBlastFurnace = BindMultiplier("02 Smelters", "BlastFurnace", "the blast furnace (blastfurnace)");
            _cfgCharcoalKiln = BindMultiplier("02 Smelters", "CharcoalKiln", "the charcoal kiln (charcoal_kiln)");
            _cfgSpinningWheel = BindMultiplier("02 Smelters", "SpinningWheel", "the spinning wheel (piece_spinningwheel)");
            _cfgWindmill = BindMultiplier("02 Smelters", "Windmill", "the windmill (windmill). The wind still scales its speed on top, as in vanilla");
            _cfgEitrRefinery = BindMultiplier("02 Smelters", "EitrRefinery", "the eitr refinery (eitrrefinery)");
            _cfgOtherSmelter = BindMultiplier("02 Smelters", "OtherSmelter", "any other station built on the Smelter component, e.g. from other mods");

            _cfgCookingStation = BindMultiplier("03 Cooking", "CookingStation", "the cooking station and the iron cooking station");
            _cfgOven = BindMultiplier("03 Cooking", "Oven", "the stone oven (piece_oven)");
            _cfgOtherCooking = BindMultiplier("03 Cooking", "OtherCooking", "any other station built on the CookingStation component");
            _cfgScaleOvenFuel = Config.Bind("03 Cooking", "ScaleOvenFuel", true,
                "Burn the oven's own fuel faster by the same multiplier, so that the wood per loaf stays as in vanilla (smelters do this by themselves). Off: the fuel lasts the vanilla time and faster baking also means cheaper baking.");

            _cfgFermenter = BindMultiplier("04 Timestamp stations", "Fermenter", "the fermenter. Applied by moving the start time when the item is added, so a player without the mod sees the same readiness");
            _cfgCrops = BindMultiplier("04 Timestamp stations", "Crops", "everything planted with the cultivator that does not grow into a tree: carrots, turnips, onions, barley, flax, mushrooms, seeds, vines. Applied by moving the planting time, same as the fermenter");
            _cfgSaplings = BindMultiplier("04 Timestamp stations", "Saplings", "planted trees: beech, fir, pine, birch, oak, ancient, yggdrasil shoot and any other sapling that grows into a tree");

            _cfgBeehive = BindMultiplier("05 Collectors", "Beehive", "the beehive");
            _cfgSapCollector = BindMultiplier("05 Collectors", "SapCollector", "the sap extractor");

            _cfgOverrides = Config.Bind("06 Overrides", "ByPrefab", "",
                "Per-prefab multipliers that win over the kind settings above: prefab=multiplier, comma separated. Example: smelter=3,piece_beehive=0.5,Oak_Sapling=4");
            _cfgOverrides.SettingChanged += delegate { ReadOverrides(_cfgOverrides.Value, _overrides); };
            ReadOverrides(_cfgOverrides.Value, _overrides);

            _cfgHostShift = Config.Bind("07 Host", "HostShift", true,
                "On the host (or a dedicated server with the mod): every few seconds look through the world's fermenters and plants for a start time that was just written and not shifted yet - an item added or a seed planted by a player without the mod - and shift it. Off: only the owner's own shift at the moment of adding.");
            _cfgHostInterval = Config.Bind("07 Host", "HostScanInterval", 5f,
                new ConfigDescription("Seconds between two passes over the world's objects on the host. A pass is spread over several frames.",
                    new AcceptableValueRange<float>(1f, 60f)));

            _cfgSync = Config.Bind("08 Sync", "SyncConfig", true,
                "The server sends its multipliers, overrides, ScaleOvenFuel and Enabled to every connecting client with the mod, and again when they change; the client uses them instead of its own file while connected. Off on the server: every client uses its own file. Has no effect on a client.");

            // Any change: re-apply to the loaded stations and, on the server, tell the clients.
            Config.SettingChanged += delegate { _syncDirty = true; };
        }

        private static void ReadOverrides(string text, Dictionary<string, float> into)
        {
            into.Clear();
            if (string.IsNullOrEmpty(text)) return;
            foreach (string raw in text.Split(','))
            {
                string s = raw.Trim();
                int eq = s.IndexOf('=');
                if (eq <= 0) continue;
                float k;
                if (!float.TryParse(s.Substring(eq + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out k)) continue;
                into[s.Substring(0, eq).Trim()] = Mathf.Clamp(k, MinMultiplier, MaxMultiplier);
            }
        }

        // ------------------------------------------------------------------
        // values in effect: the server's while connected to a server with the mod, else ours
        // ------------------------------------------------------------------
        private bool Enabled { get { return _serverSynced ? _serverEnabled : _cfgEnabled.Value; } }

        private bool ScaleOvenFuel { get { return _serverSynced ? _serverScaleOvenFuel : _cfgScaleOvenFuel.Value; } }

        private float Kind(ConfigEntry<float> kind)
        {
            float k;
            if (_serverSynced && _serverValues.TryGetValue(kind.Definition.Key, out k)) return Mathf.Clamp(k, MinMultiplier, MaxMultiplier);
            return kind.Value;
        }

        // Multiplier for a station: explicit override by prefab name, else the setting for its kind.
        private float Multiplier(string prefab, ConfigEntry<float> kind)
        {
            float k;
            Dictionary<string, float> overrides = _serverSynced ? _serverOverrides : _overrides;
            if (overrides.TryGetValue(prefab, out k)) return k;
            return Kind(kind);
        }

        private ConfigEntry<float> SmelterKind(string prefab)
        {
            switch (prefab)
            {
                case "smelter": return _cfgSmelter;
                case "blastfurnace": return _cfgBlastFurnace;
                case "charcoal_kiln": return _cfgCharcoalKiln;
                case "piece_spinningwheel": return _cfgSpinningWheel;
                case "windmill": return _cfgWindmill;
                case "eitrrefinery": return _cfgEitrRefinery;
                default: return _cfgOtherSmelter;
            }
        }

        private ConfigEntry<float> CookingKind(string prefab)
        {
            switch (prefab)
            {
                case "piece_cookingstation":
                case "piece_cookingstation_iron": return _cfgCookingStation;
                case "piece_oven": return _cfgOven;
                default: return _cfgOtherCooking;
            }
        }

        // A sapling is a plant that grows into a tree; the game does not tell them apart, the
        // grown prefab does. Decided once per prefab.
        private readonly Dictionary<string, bool> _saplingByPrefab = new Dictionary<string, bool>();

        private ConfigEntry<float> PlantKind(Plant p, string prefab)
        {
            bool sapling;
            if (!_saplingByPrefab.TryGetValue(prefab, out sapling))
            {
                sapling = false;
                if (p != null && p.m_grownPrefabs != null)
                {
                    foreach (GameObject g in p.m_grownPrefabs)
                    {
                        if (g != null && g.GetComponent<TreeBase>() != null) { sapling = true; break; }
                    }
                }
                _saplingByPrefab[prefab] = sapling;
            }
            return sapling ? _cfgSaplings : _cfgCrops;
        }
    }
}
