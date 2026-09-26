using System;
using System.Collections.Generic;
using System.Globalization;
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
    //     ZDO. Their per-item time is an instance field, so it is set to the prefab's value
    //     divided by the multiplier: in Awake, and again for every loaded station whenever
    //     the multipliers change (config edit, values from the server). That works whenever
    //     a client with this mod owns the station, which is the client nearest to it.
    //
    //   * Timestamp stations (Fermenter, Plant) store only the moment they were started;
    //     readiness is computed by EVERY client from its own copy of the duration. Changing
    //     the duration here would make this client and a vanilla client disagree about the
    //     same barrel. So the duration is left alone and the timestamp is moved instead,
    //     once, when the item is added: startTime = now - D + D/k. The vanilla formula then
    //     gives the same answer on every client, with or without the mod. The owner does
    //     that at the moment of adding; the host does it a few seconds later for anything
    //     an owner without the mod has added (StationSpeedPlugin.Host.cs).
    //
    // The server's multipliers are sent to every client with the mod on connect, so all
    // owners agree (StationSpeedPlugin.Sync.cs).
    [BepInPlugin(Guid, Name, Version)]
    public partial class StationSpeedPlugin : BaseUnityPlugin
    {
        public const string Guid = "j1ga.stationspeed";
        public const string Name = "Station Speed";
        public const string Version = "0.2.1";

        // Harmony patches are static; they reach the running plugin through this.
        public static StationSpeedPlugin Instance;

        private Harmony _harmony;
        private MethodInfo _plantGrowTime;

        private int _errorCount;
        private bool _disabledByErrors;
        private const int MaxErrors = 25;
        private readonly HashSet<string> _loggedErrors = new HashSet<string>();

        // Our own key in the ZDO of a fermenter or plant: the start time as we left it. Vanilla
        // never touches it and saves it with the rest; while it equals the current start time
        // the shift has been done, by whoever did it. A new start time means a new item.
        private static readonly int MarkHash = "j1ga.stationspeed.shifted".GetStableHashCode();

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

                FindHostFields();

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

        private void Update()
        {
            if (_disabledByErrors) return;
            try
            {
                if (_syncDirty) FlushSync();
                UpdateHost();
            }
            catch (Exception e)
            {
                Fail("Update", e);
                ResetScan();
            }
        }

        private void OnDestroy()
        {
            try
            {
                StopAllCoroutines();
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
        private bool Active { get { return !_disabledByErrors && Enabled; } }

        private static string PrefabName(GameObject go)
        {
            if (go == null) return "";
            string n = go.name;
            int i = n.IndexOf("(Clone)", StringComparison.Ordinal);
            return i > 0 ? n.Substring(0, i) : n;
        }

        // The component on the registered prefab of a loaded station: the vanilla values that
        // the instance was copied from. Null outside a world or for an unregistered object.
        private static T PrefabComponent<T>(string prefab) where T : Component
        {
            ZNetScene zs = ZNetScene.instance;
            if (zs == null || string.IsNullOrEmpty(prefab)) return null;
            GameObject go = zs.GetPrefab(prefab);
            return go != null ? go.GetComponent<T>() : null;
        }

        // Move a start timestamp so that the vanilla duration D reads as D/k, and leave our
        // mark. k < 1 moves it into the future, which the game handles as "not ready yet"
        // like any other.
        private static bool ShiftStart(ZDO zdo, int key, float duration, float k)
        {
            if (zdo == null || k <= 0f || k == 1f) return false;
            long ticks = zdo.GetLong(key, 0L);
            if (ticks <= 0L || ticks > DateTime.MaxValue.Ticks) return false;
            double shift = duration - duration / k;
            long shifted = new DateTime(ticks).AddSeconds(-shift).Ticks;
            zdo.Set(key, shifted);
            zdo.Set(MarkHash, shifted);
            return true;
        }

        // Numbers in the log with a dot, whatever the system language.
        private static string F(float v)
        {
            return v.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string Pos(Vector3 p)
        {
            return "(" + p.x.ToString("0", CultureInfo.InvariantCulture) + ", " + p.y.ToString("0", CultureInfo.InvariantCulture) + ", " + p.z.ToString("0", CultureInfo.InvariantCulture) + ")";
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
