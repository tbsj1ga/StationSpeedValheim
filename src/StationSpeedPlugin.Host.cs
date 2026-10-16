using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace StationSpeed
{
    // The shift for items added and seeds planted by a player WITHOUT the mod.
    //
    // Such a player owns the barrel or plant at that moment, so no owner-side patch runs; but
    // the server holds every ZDO of the world and gets the new start time within a second.
    // So on the server (the hosting player, or a dedicated server with the mod) every few
    // seconds the world's objects are walked, a slice per frame: a fermenter or plant whose
    // start time is fresh and not the one under our mark has just been started by someone
    // who did not shift it, and gets shifted here. Whoever did shift it left the mark, and
    // anything older than a few minutes is from before the mod and stays as it is. The write
    // reaches the owner like any other ZDO change: revisions, not ownership, decide.
    public partial class StationSpeedPlugin
    {
        private FieldInfo _fiObjectsBySector;

        // prefab hash -> the component on the prefab: durations, grown prefabs, the name
        private readonly Dictionary<int, Fermenter> _fermenterPrefabs = new Dictionary<int, Fermenter>();
        private readonly Dictionary<int, Plant> _plantPrefabs = new Dictionary<int, Plant>();
        private ZNetScene _indexedScene;

        // The world's objects, walked over several frames through the game's own per-sector
        // lists. 0.2.2 copied the whole id table into a list first: over a million ZDOs in a
        // big world, a 30 ms hitch every pass. A sector is always finished within one frame,
        // so its list never changes under the walk; an object that moves to a sector not yet
        // walked is seen twice, which the mark makes harmless.
        private List<ZDO>[] _sectors;
        private int _scanCursor = -1;
        private int _scanCount;
        private float _nextScan;
        private const int ScanPerFrame = 20000;

        // what counts as "just started": generous, so that a laggy peer still makes it, and
        // negative for a peer whose clock runs slightly ahead of ours
        private const double FreshSeconds = 300.0;
        private const double ClockSlackSeconds = 30.0;

        private bool _growTimeMismatch;
        private int _hostShifted;
        private int _lastScanCount;

        private void FindHostFields()
        {
            FieldInfo fi = typeof(ZDOMan).GetField("m_objectsBySector", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (fi != null && fi.FieldType == typeof(List<ZDO>[]))
                _fiObjectsBySector = fi;
            else
                Logger.LogWarning("ZDOMan.m_objectsBySector was not found or has an unexpected type; the host will not shift fermenters and plants of players without the mod.");
        }

        private bool HostActive
        {
            get
            {
                if (_fiObjectsBySector == null || !Active || !_cfgHostShift.Value) return false;
                ZNet znet = ZNet.instance;
                return znet != null && znet.IsServer() && ZDOMan.instance != null && ZNetScene.instance != null;
            }
        }

        private void UpdateHost()
        {
            if (!HostActive)
            {
                if (_scanCursor >= 0) ResetScan();
                return;
            }
            if (_scanCursor < 0)
            {
                if (Time.time < _nextScan) return;
                if (!BeginScan())
                {
                    _nextScan = Time.time + _cfgHostInterval.Value;
                    return;
                }
            }
            StepScan();
        }

        private void ResetScan()
        {
            _sectors = null;
            _scanCursor = -1;
        }

        private bool BeginScan()
        {
            IndexPrefabs();
            if (_fermenterPrefabs.Count == 0 && _plantPrefabs.Count == 0) return false;
            _sectors = _fiObjectsBySector.GetValue(ZDOMan.instance) as List<ZDO>[];
            if (_sectors == null) return false;
            _scanCursor = 0;
            _scanCount = 0;
            return true;
        }

        private void StepScan()
        {
            ZNet znet = ZNet.instance;
            ZDOMan man = ZDOMan.instance;
            // a new world, or the game rebuilt its sectors: start over on the next pass
            if (!ReferenceEquals(_fiObjectsBySector.GetValue(man), _sectors))
            {
                ResetScan();
                return;
            }
            DateTime now = znet.GetTime();
            int budget = ScanPerFrame;
            while (budget > 0 && _scanCursor < _sectors.Length)
            {
                List<ZDO> sector = _sectors[_scanCursor++];
                budget--;
                if (sector == null) continue;
                int n = sector.Count;
                for (int i = 0; i < n; i++) Examine(sector[i], now, man);
                budget -= n;
                _scanCount += n;
            }
            if (_scanCursor >= _sectors.Length)
            {
                _lastScanCount = _scanCount;
                ResetScan();
                _nextScan = Time.time + _cfgHostInterval.Value;
            }
        }

        // Every prefab with a Fermenter or a Plant, once per world; mods register theirs in
        // the same table, so they are covered too.
        private void IndexPrefabs()
        {
            ZNetScene zs = ZNetScene.instance;
            if (zs == _indexedScene || zs.m_prefabs == null) return;
            _fermenterPrefabs.Clear();
            _plantPrefabs.Clear();
            foreach (GameObject go in zs.m_prefabs)
            {
                if (go == null) continue;
                Fermenter f = go.GetComponent<Fermenter>();
                if (f != null) { _fermenterPrefabs[zs.GetPrefabHash(go)] = f; continue; }
                Plant p = go.GetComponent<Plant>();
                if (p != null) _plantPrefabs[zs.GetPrefabHash(go)] = p;
            }
            _indexedScene = zs;
            Debug("host: " + _fermenterPrefabs.Count + " fermenter and " + _plantPrefabs.Count + " plant prefabs indexed");
        }

        private void Examine(ZDO zdo, DateTime now, ZDOMan man)
        {
            if (zdo == null) return;
            int hash = zdo.GetPrefab();
            if (hash == 0) return;

            Fermenter f;
            if (_fermenterPrefabs.TryGetValue(hash, out f))
            {
                if (!Fresh(zdo, ZDOVars.s_startTime, now)) return;
                string prefab = f.gameObject.name;
                float k = Multiplier(prefab, _cfgFermenter);
                if (k == 1f || man.GetZDO(zdo.m_uid) != zdo) return;
                if (ShiftStart(zdo, ZDOVars.s_startTime, f.m_fermentationDuration, k))
                {
                    _hostShifted++;
                    Debug("host: " + prefab + " " + Pos(zdo.GetPosition()) + ": ready in " + F(f.m_fermentationDuration / k) + " s instead of " + F(f.m_fermentationDuration) + " (x" + F(k) + ")");
                }
                return;
            }

            Plant p;
            if (!_growTimeMismatch && _plantPrefabs.TryGetValue(hash, out p))
            {
                if (!Fresh(zdo, ZDOVars.s_plantTime, now)) return;
                string prefab = p.gameObject.name;
                float k = Multiplier(prefab, PlantKind(p, prefab));
                if (k == 1f || man.GetZDO(zdo.m_uid) != zdo) return;
                float growTime = GrowTime(zdo, p);
                if (ShiftStart(zdo, ZDOVars.s_plantTime, growTime, k))
                {
                    _hostShifted++;
                    Debug("host: " + prefab + " " + Pos(zdo.GetPosition()) + ": grows in " + F(growTime / k) + " s instead of " + F(growTime) + " (x" + F(k) + ")");
                }
            }
        }

        // Started a moment ago and not yet shifted by anyone.
        private static bool Fresh(ZDO zdo, int key, DateTime now)
        {
            long start = zdo.GetLong(key, 0L);
            if (start <= 0L || start > DateTime.MaxValue.Ticks) return false;
            if (zdo.GetLong(MarkHash, 0L) == start) return false;
            double age = (now - new DateTime(start)).TotalSeconds;
            return age > -ClockSlackSeconds && age < FreshSeconds;
        }

        // Plant.GetGrowTime without a Plant: the seed comes from the ZDO, or is derived from
        // the ZDO id the way Plant.Awake does it when there is none yet; the range comes from
        // the prefab. Same Unity random, same result on every client.
        private static float GrowTime(ZDO zdo, Plant prefab)
        {
            int seed = zdo.GetInt(ZDOVars.s_seed, 0);
            if (seed == 0) seed = unchecked((int)((long)zdo.m_uid.ID + zdo.m_uid.UserID));
            UnityEngine.Random.State state = UnityEngine.Random.state;
            UnityEngine.Random.InitState(seed);
            float t = UnityEngine.Random.value;
            UnityEngine.Random.state = state;
            return Mathf.Lerp(prefab.m_growTime, prefab.m_growTimeMax, t);
        }
    }
}
