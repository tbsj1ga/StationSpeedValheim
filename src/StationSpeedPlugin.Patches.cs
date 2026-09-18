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
        // ticking stations: the per-item time is an instance field, set to the prefab's value
        // over the multiplier. Only the owner of the station runs its update, so this matters
        // on the owner only. Called from Awake (awake = true) and from Reapply for a loaded
        // station; without a registered prefab to read the vanilla value from, Awake divides
        // in place and Reapply leaves the station alone.
        // ------------------------------------------------------------------
        private bool ApplySmelter(Smelter s, bool awake)
        {
            if (s == null) return false;
            string prefab = PrefabName(s.gameObject);
            float k = Active ? Multiplier(prefab, SmelterKind(prefab)) : 1f;
            Smelter src = PrefabComponent<Smelter>(prefab);
            if (src != null && src != s) s.m_secPerProduct = src.m_secPerProduct / k;
            else if (awake && k != 1f) s.m_secPerProduct /= k;
            else return false;
            if (k != 1f) Debug(prefab + ": " + F(s.m_secPerProduct) + " s per product (x" + F(k) + ")");
            return true;
        }

        private bool ApplyCookingStation(CookingStation c, bool awake)
        {
            if (c == null || c.m_conversion == null) return false;
            string prefab = PrefabName(c.gameObject);
            float k = Active ? Multiplier(prefab, CookingKind(prefab)) : 1f;
            CookingStation src = PrefabComponent<CookingStation>(prefab);
            // Unity copies the conversions for every instance; should they ever be shared with
            // the prefab, dividing them by the prefab's values would compound.
            if (src == c || (src != null && ReferenceEquals(src.m_conversion, c.m_conversion))) src = null;
            if (src == null && (!awake || k == 1f)) return false;

            // Burning happens at twice the cook time in the game's update, so scaling the
            // cook time scales both.
            for (int i = 0; i < c.m_conversion.Count; i++)
            {
                CookingStation.ItemConversion conv = c.m_conversion[i];
                if (conv == null) continue;
                if (src != null)
                {
                    if (src.m_conversion == null || i >= src.m_conversion.Count || src.m_conversion[i] == null) continue;
                    conv.m_cookTime = src.m_conversion[i].m_cookTime / k;
                }
                else conv.m_cookTime /= k;
            }

            // The oven burns its own fuel by the clock, one unit per m_secPerFuel seconds
            // (whole seconds), whether or not anything is baking. Scaled along by default so
            // that the fuel per loaf stays vanilla.
            if (c.m_useFuel)
            {
                float fuelK = Active && ScaleOvenFuel ? k : 1f;
                if (src != null) c.m_secPerFuel = Mathf.Max(1, Mathf.RoundToInt(src.m_secPerFuel / fuelK));
                else if (fuelK != 1f) c.m_secPerFuel = Mathf.Max(1, Mathf.RoundToInt(c.m_secPerFuel / fuelK));
            }

            if (k != 1f) Debug(prefab + ": " + c.m_conversion.Count + " recipes (x" + F(k) + ")" + (c.m_useFuel ? ", fuel " + c.m_secPerFuel + " s per unit" : ""));
            return true;
        }

        private bool ApplyBeehive(Beehive b, bool awake)
        {
            if (b == null) return false;
            string prefab = PrefabName(b.gameObject);
            float k = Active ? Multiplier(prefab, _cfgBeehive) : 1f;
            Beehive src = PrefabComponent<Beehive>(prefab);
            if (src != null && src != b) b.m_secPerUnit = src.m_secPerUnit / k;
            else if (awake && k != 1f) b.m_secPerUnit /= k;
            else return false;
            if (k != 1f) Debug(prefab + ": " + F(b.m_secPerUnit) + " s per unit (x" + F(k) + ")");
            return true;
        }

        private bool ApplySapCollector(SapCollector s, bool awake)
        {
            if (s == null) return false;
            string prefab = PrefabName(s.gameObject);
            float k = Active ? Multiplier(prefab, _cfgSapCollector) : 1f;
            SapCollector src = PrefabComponent<SapCollector>(prefab);
            if (src != null && src != s) s.m_secPerUnit = src.m_secPerUnit / k;
            else if (awake && k != 1f) s.m_secPerUnit /= k;
            else return false;
            if (k != 1f) Debug(prefab + ": " + F(s.m_secPerUnit) + " s per unit (x" + F(k) + ")");
            return true;
        }

        // Every loaded ticking station gets the multipliers in effect now: after a config
        // change, after the server's values arrived, on 'stationspeed rescan'.
        private int Reapply()
        {
            if (ZNetScene.instance == null) return 0;
            int n = 0;
            foreach (Smelter s in UnityEngine.Object.FindObjectsByType<Smelter>(FindObjectsSortMode.None)) if (ApplySmelter(s, false)) n++;
            foreach (CookingStation c in UnityEngine.Object.FindObjectsByType<CookingStation>(FindObjectsSortMode.None)) if (ApplyCookingStation(c, false)) n++;
            foreach (Beehive b in UnityEngine.Object.FindObjectsByType<Beehive>(FindObjectsSortMode.None)) if (ApplyBeehive(b, false)) n++;
            foreach (SapCollector s in UnityEngine.Object.FindObjectsByType<SapCollector>(FindObjectsSortMode.None)) if (ApplySapCollector(s, false)) n++;
            return n;
        }

        // ------------------------------------------------------------------
        // timestamp stations: the start time is moved once, right after the game sets it.
        // ------------------------------------------------------------------
        private static ZNetView View(Component c)
        {
            return c != null ? c.GetComponent<ZNetView>() : null;
        }

        private static long StartTime(Fermenter f)
        {
            ZNetView nv = View(f);
            if (nv == null || nv.GetZDO() == null) return 0L;
            return nv.GetZDO().GetLong(ZDOVars.s_startTime, 0L);
        }

        // RPC_AddItem runs on the owner and, when it accepts the item, writes content and the
        // start time; when it refuses (not the owner, not empty, wrong item) it writes nothing.
        // A different start time after the call is the sign that it accepted.
        private void OnFermenterAdded(Fermenter f, long before)
        {
            if (!Active || f == null) return;
            ZNetView nv = View(f);
            if (nv == null || nv.GetZDO() == null || !nv.IsOwner()) return;
            ZDO zdo = nv.GetZDO();
            long after = zdo.GetLong(ZDOVars.s_startTime, 0L);
            if (after == 0L || after == before) return;
            string prefab = PrefabName(f.gameObject);
            float k = Multiplier(prefab, _cfgFermenter);
            if (ShiftStart(zdo, ZDOVars.s_startTime, f.m_fermentationDuration, k))
                Debug(prefab + " " + Pos(f.transform.position) + ": ready in " + F(f.m_fermentationDuration / k) + " s instead of " + F(f.m_fermentationDuration) + " (x" + F(k) + ")");
        }

        private static long PlantTime(Plant p)
        {
            ZNetView nv = View(p);
            if (nv == null || nv.GetZDO() == null) return -1L;
            return nv.GetZDO().GetLong(ZDOVars.s_plantTime, 0L);
        }

        // Plant.Awake stamps the planting time on the owner when there is none yet, i.e. for a
        // plant that was just placed. A plant that already had one was planted earlier, by
        // this client or another, and has been shifted then or is meant to stay vanilla.
        private void OnPlantAwake(Plant p, long before)
        {
            if (!Active || p == null || before != 0L || _plantGrowTime == null) return;
            ZNetView nv = View(p);
            if (nv == null || nv.GetZDO() == null || !nv.IsOwner()) return;
            ZDO zdo = nv.GetZDO();
            if (zdo.GetLong(ZDOVars.s_plantTime, 0L) == 0L) return;
            string prefab = PrefabName(p.gameObject);
            float k = Multiplier(prefab, PlantKind(p, prefab));
            if (k == 1f) return;
            float growTime = (float)_plantGrowTime.Invoke(p, null);
            CheckGrowTime(zdo, p, growTime);
            if (ShiftStart(zdo, ZDOVars.s_plantTime, growTime, k))
                Debug(prefab + " " + Pos(p.transform.position) + ": grows in " + F(growTime / k) + " s instead of " + F(growTime) + " (x" + F(k) + ")");
        }

        // The host computes the grow time of a plant it has no instance of the way the game
        // does it (GrowTime in StationSpeedPlugin.Host.cs). Whenever the real thing is at hand
        // the two are compared, and a difference is reported once.
        private bool _growTimeReported;

        private void CheckGrowTime(ZDO zdo, Plant p, float vanilla)
        {
            if (_growTimeReported) return;
            _growTimeReported = true;
            float ours = GrowTime(zdo, p);
            _growTimeMismatch = Mathf.Abs(ours - vanilla) > 0.01f;
            if (_growTimeMismatch)
                Logger.LogWarning("Plant grow time computed by the mod (" + F(ours) + " s) differs from the game's (" + F(vanilla) + " s) for " + p.gameObject.name + "; the host would shift plants of players without the mod by the wrong amount, so it will not shift plants.");
            else
                Debug("plant grow time check: " + p.gameObject.name + " " + F(vanilla) + " s, same as the game's");
        }

        // ------------------------------------------------------------------
        // Harmony
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(Smelter), "Awake")]
        private static class Smelter_Awake_Patch
        {
            private static void Postfix(Smelter __instance)
            {
                StationSpeedPlugin p = Instance;
                if (p == null || !p.Active) return;
                try { p.ApplySmelter(__instance, true); } catch (Exception e) { p.Fail("Smelter.Awake", e); }
            }
        }

        [HarmonyPatch(typeof(CookingStation), "Awake")]
        private static class CookingStation_Awake_Patch
        {
            private static void Postfix(CookingStation __instance)
            {
                StationSpeedPlugin p = Instance;
                if (p == null || !p.Active) return;
                try { p.ApplyCookingStation(__instance, true); } catch (Exception e) { p.Fail("CookingStation.Awake", e); }
            }
        }

        [HarmonyPatch(typeof(Beehive), "Awake")]
        private static class Beehive_Awake_Patch
        {
            private static void Postfix(Beehive __instance)
            {
                StationSpeedPlugin p = Instance;
                if (p == null || !p.Active) return;
                try { p.ApplyBeehive(__instance, true); } catch (Exception e) { p.Fail("Beehive.Awake", e); }
            }
        }

        [HarmonyPatch(typeof(SapCollector), "Awake")]
        private static class SapCollector_Awake_Patch
        {
            private static void Postfix(SapCollector __instance)
            {
                StationSpeedPlugin p = Instance;
                if (p == null || !p.Active) return;
                try { p.ApplySapCollector(__instance, true); } catch (Exception e) { p.Fail("SapCollector.Awake", e); }
            }
        }

        [HarmonyPatch(typeof(Fermenter), "RPC_AddItem")]
        private static class Fermenter_RPC_AddItem_Patch
        {
            private static void Prefix(Fermenter __instance, out long __state)
            {
                __state = StartTime(__instance);
            }

            private static void Postfix(Fermenter __instance, long __state)
            {
                StationSpeedPlugin p = Instance;
                if (p == null) return;
                try { p.OnFermenterAdded(__instance, __state); } catch (Exception e) { p.Fail("Fermenter.RPC_AddItem", e); }
            }
        }

        [HarmonyPatch(typeof(Plant), "Awake")]
        private static class Plant_Awake_Patch
        {
            private static void Prefix(Plant __instance, out long __state)
            {
                __state = PlantTime(__instance);
            }

            private static void Postfix(Plant __instance, long __state)
            {
                StationSpeedPlugin p = Instance;
                if (p == null) return;
                try { p.OnPlantAwake(__instance, __state); } catch (Exception e) { p.Fail("Plant.Awake", e); }
            }
        }
    }
}
