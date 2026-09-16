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

        private ConfigEntry<float> _cfgFermenter;
        private ConfigEntry<float> _cfgBeehive;
        private ConfigEntry<float> _cfgSapCollector;
        private ConfigEntry<float> _cfgPlants;

        private ConfigEntry<string> _cfgOverrides;
        private readonly Dictionary<string, float> _overrides = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        private const float MinMultiplier = 0.1f;
        private const float MaxMultiplier = 100f;

        private ConfigEntry<float> BindMultiplier(string section, string key, string what)
        {
            return Config.Bind(section, key, 1f,
                new ConfigDescription("Speed multiplier for " + what + ". 2 = twice as fast, 0.5 = twice as slow.",
                    new AcceptableValueRange<float>(MinMultiplier, MaxMultiplier)));
        }

        private void BindConfig()
        {
            _cfgEnabled = Config.Bind("01 General", "Enabled", true, "Master switch. Stations already loaded keep their current speed until they are loaded again.");
            _cfgDebug = Config.Bind("01 General", "Debug", false, "Log every station the mod touches.");

            _cfgSmelter = BindMultiplier("02 Smelters", "Smelter", "the smelter (prefab smelter)");
            _cfgBlastFurnace = BindMultiplier("02 Smelters", "BlastFurnace", "the blast furnace (blastfurnace)");
            _cfgCharcoalKiln = BindMultiplier("02 Smelters", "CharcoalKiln", "the charcoal kiln (charcoal_kiln)");
            _cfgSpinningWheel = BindMultiplier("02 Smelters", "SpinningWheel", "the spinning wheel (piece_spinningwheel)");
            _cfgWindmill = BindMultiplier("02 Smelters", "Windmill", "the windmill (windmill)");
            _cfgEitrRefinery = BindMultiplier("02 Smelters", "EitrRefinery", "the eitr refinery (eitrrefinery)");
            _cfgOtherSmelter = BindMultiplier("02 Smelters", "OtherSmelter", "any other station built on the Smelter component, e.g. from other mods");

            _cfgCookingStation = BindMultiplier("03 Cooking", "CookingStation", "the cooking station and the iron cooking station");
            _cfgOven = BindMultiplier("03 Cooking", "Oven", "the stone oven (piece_oven)");
            _cfgOtherCooking = BindMultiplier("03 Cooking", "OtherCooking", "any other station built on the CookingStation component");

            _cfgFermenter = BindMultiplier("04 Timestamp stations", "Fermenter", "the fermenter. Applied by moving the start time when the item is added, so a player without the mod sees the same readiness");
            _cfgPlants = BindMultiplier("04 Timestamp stations", "Plants", "growth of everything planted with the cultivator: crops and saplings. Applied by moving the planting time, same as the fermenter");

            _cfgBeehive = BindMultiplier("05 Collectors", "Beehive", "the beehive");
            _cfgSapCollector = BindMultiplier("05 Collectors", "SapCollector", "the sap extractor");

            _cfgOverrides = Config.Bind("06 Overrides", "ByPrefab", "",
                "Per-prefab multipliers that win over the kind settings above: prefab=multiplier, comma separated. Example: smelter=3,piece_beehive=0.5");
            _cfgOverrides.SettingChanged += delegate { ReadOverrides(); };
            ReadOverrides();
        }

        private void ReadOverrides()
        {
            _overrides.Clear();
            foreach (string raw in _cfgOverrides.Value.Split(','))
            {
                string s = raw.Trim();
                int eq = s.IndexOf('=');
                if (eq <= 0) continue;
                float k;
                if (!float.TryParse(s.Substring(eq + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out k)) continue;
                _overrides[s.Substring(0, eq).Trim()] = Mathf.Clamp(k, MinMultiplier, MaxMultiplier);
            }
        }

        // Multiplier for a station: explicit override by prefab name, else the setting for its kind.
        private float Multiplier(string prefab, ConfigEntry<float> kind)
        {
            float k;
            if (_overrides.TryGetValue(prefab, out k)) return k;
            return kind.Value;
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
    }
}
