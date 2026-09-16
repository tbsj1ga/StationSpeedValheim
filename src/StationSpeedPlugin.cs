using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace StationSpeed
{
    // Work speed of the crafting stations: smelters of every kind, cooking stations, the
    // fermenter, beehives, sap collectors and plant growth. One multiplier per kind.
    //
    // Two mechanisms, because the game keeps progress in two different ways:
    //
    //   * Ticking stations (Smelter, CookingStation, Beehive, SapCollector) advance their
    //     progress once a second on the client that OWNS the station and write it into the
    //     ZDO. Their per-item time is an instance field, so it is divided by the multiplier
    //     in Awake. That works whenever a client with this mod owns the station, which is
    //     the client nearest to it.
    //
    //   * Timestamp stations (Fermenter, Plant) store only the moment they were started;
    //     readiness is computed by EVERY client from its own copy of the duration. Changing
    //     the duration here would make this client and a vanilla client disagree about the
    //     same barrel. So the duration is left alone and the timestamp is moved instead,
    //     once, when the item is added: startTime = now - D + D/k. The vanilla formula then
    //     gives the same answer on every client, with or without the mod.
    [BepInPlugin(Guid, Name, Version)]
    public partial class StationSpeedPlugin : BaseUnityPlugin
    {
        public const string Guid = "j1ga.stationspeed";
        public const string Name = "Station Speed";
        public const string Version = "0.1.0";

        // Harmony patches are static; they reach the running plugin through this.
        public static StationSpeedPlugin Instance;

        private Harmony _harmony;
        private MethodInfo _plantGrowTime;

        private int _errorCount;
        private bool _disabledByErrors;
        private const int MaxErrors = 25;
        private readonly HashSet<string> _loggedErrors = new HashSet<string>();

        // ------------------------------------------------------------------
        // lifecycle
        // ------------------------------------------------------------------
        private void Awake()
        {
            try
            {
                Instance = this;
                BindConfig();
                RegisterCommands();

                // Plant.GetGrowTime is private and seeded per plant, so every client agrees on
                // it; there is no public way to ask, hence reflection (checked by check-refs.ps1).
                _plantGrowTime = AccessTools.Method(typeof(Plant), "GetGrowTime");
                if (_plantGrowTime == null)
                    Logger.LogWarning("Plant.GetGrowTime was not found; plant growth will not be changed.");

                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(StationSpeedPlugin).Assembly);
                Logger.LogInfo(Name + " " + Version + " loaded.");
            }
            catch (Exception e)
            {
                Logger.LogError("Awake failed, mod is inert: " + e);
                _disabledByErrors = true;
            }
        }

        private void OnDestroy()
        {
            try
            {
                if (_harmony != null) _harmony.UnpatchSelf();
            }
            catch (Exception e)
            {
                Logger.LogWarning("OnDestroy: " + e.Message);
            }
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------
        // helpers
        // ------------------------------------------------------------------
        private bool Active { get { return !_disabledByErrors && _cfgEnabled.Value; } }

        private static string PrefabName(GameObject go)
        {
            if (go == null) return "";
            string n = go.name;
            int i = n.IndexOf("(Clone)", StringComparison.Ordinal);
            return i > 0 ? n.Substring(0, i) : n;
        }

        // Move a start timestamp so that the vanilla duration D reads as D/k. k < 1 moves it
        // into the future, which the game handles as "not ready yet" like any other.
        private static bool ShiftStart(ZDO zdo, int key, float duration, float k)
        {
            if (zdo == null || k <= 0f || k == 1f) return false;
            long ticks = zdo.GetLong(key, 0L);
            if (ticks == 0L) return false;
            double shift = duration - duration / k;
            DateTime start = new DateTime(ticks).AddSeconds(-shift);
            zdo.Set(key, start.Ticks);
            return true;
        }

        private void Debug(string text)
        {
            if (_cfgDebug.Value) Logger.LogInfo(text);
        }

        // Errors inside patches must never take the game down with them: log each distinct
        // message once, and after too many of them switch the mod off entirely.
        private void Fail(string where, Exception e)
        {
            string key = where + ": " + e.GetType().Name + ": " + e.Message;
            if (_loggedErrors.Add(key)) Logger.LogError(key + "\n" + e.StackTrace);
            if (++_errorCount >= MaxErrors && !_disabledByErrors)
            {
                _disabledByErrors = true;
                Logger.LogError("Too many errors, " + Name + " is now inert until the game restarts.");
            }
        }
    }
}
