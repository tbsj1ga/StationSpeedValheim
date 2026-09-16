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
        // ticking stations: the per-item time is an instance field, scaled once in Awake.
        // Only the owner of the station runs its update, so this matters on the owner only.
        // ------------------------------------------------------------------
        private void OnSmelterAwake(Smelter s)
        {
            if (!Active || s == null) return;
            string prefab = PrefabName(s.gameObject);
            float k = Multiplier(prefab, SmelterKind(prefab));
            if (k == 1f) return;
            s.m_secPerProduct /= k;
            Debug(prefab + ": " + s.m_secPerProduct.ToString("0.#") + " s per product (x" + k + ")");
        }

        private void OnCookingStationAwake(CookingStation c)
        {
            if (!Active || c == null || c.m_conversion == null) return;
            string prefab = PrefabName(c.gameObject);
            float k = Multiplier(prefab, CookingKind(prefab));
            if (k == 1f) return;
            // Burning happens at twice the cook time in the game's update, so scaling the
            // cook time scales both.
            for (int i = 0; i < c.m_conversion.Count; i++)
            {
                CookingStation.ItemConversion conv = c.m_conversion[i];
                if (conv != null) conv.m_cookTime /= k;
            }
            Debug(prefab + ": " + c.m_conversion.Count + " recipes (x" + k + ")");
        }

        private void OnBeehiveAwake(Beehive b)
        {
            if (!Active || b == null) return;
            string prefab = PrefabName(b.gameObject);
            float k = Multiplier(prefab, _cfgBeehive);
            if (k == 1f) return;
            b.m_secPerUnit /= k;
            Debug(prefab + ": " + b.m_secPerUnit.ToString("0.#") + " s per unit (x" + k + ")");
        }

        private void OnSapCollectorAwake(SapCollector s)
        {
            if (!Active || s == null) return;
            string prefab = PrefabName(s.gameObject);
            float k = Multiplier(prefab, _cfgSapCollector);
            if (k == 1f) return;
            s.m_secPerUnit /= k;
            Debug(prefab + ": " + s.m_secPerUnit.ToString("0.#") + " s per unit (x" + k + ")");
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
        // start time. A different start time after the call is the sign that it did.
        private void OnFermenterAdded(Fermenter f, long before)
        {
            if (!Active || f == null) return;
            long after = StartTime(f);
            ZNetView nv = View(f);
            if (after == 0L || after == before || nv == null || !nv.IsOwner()) return;
            string prefab = PrefabName(f.gameObject);
            float k = Multiplier(prefab, _cfgFermenter);
            if (ShiftStart(nv.GetZDO(), ZDOVars.s_startTime, f.m_fermentationDuration, k))
                Debug(prefab + ": ready in " + (f.m_fermentationDuration / k).ToString("0") + " s instead of " + f.m_fermentationDuration.ToString("0") + " (x" + k + ")");
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
            if (nv.GetZDO().GetLong(ZDOVars.s_plantTime, 0L) == 0L) return;
            string prefab = PrefabName(p.gameObject);
            float k = Multiplier(prefab, _cfgPlants);
            if (k == 1f) return;
            float growTime = (float)_plantGrowTime.Invoke(p, null);
            if (ShiftStart(nv.GetZDO(), ZDOVars.s_plantTime, growTime, k))
                Debug(prefab + ": grows in " + (growTime / k).ToString("0") + " s instead of " + growTime.ToString("0") + " (x" + k + ")");
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
                if (p == null) return;
                try { p.OnSmelterAwake(__instance); } catch (Exception e) { p.Fail("Smelter.Awake", e); }
            }
        }

        [HarmonyPatch(typeof(CookingStation), "Awake")]
        private static class CookingStation_Awake_Patch
        {
            private static void Postfix(CookingStation __instance)
            {
                StationSpeedPlugin p = Instance;
                if (p == null) return;
                try { p.OnCookingStationAwake(__instance); } catch (Exception e) { p.Fail("CookingStation.Awake", e); }
            }
        }

        [HarmonyPatch(typeof(Beehive), "Awake")]
        private static class Beehive_Awake_Patch
        {
            private static void Postfix(Beehive __instance)
            {
                StationSpeedPlugin p = Instance;
                if (p == null) return;
                try { p.OnBeehiveAwake(__instance); } catch (Exception e) { p.Fail("Beehive.Awake", e); }
            }
        }

        [HarmonyPatch(typeof(SapCollector), "Awake")]
        private static class SapCollector_Awake_Patch
        {
            private static void Postfix(SapCollector __instance)
            {
                StationSpeedPlugin p = Instance;
                if (p == null) return;
                try { p.OnSapCollectorAwake(__instance); } catch (Exception e) { p.Fail("SapCollector.Awake", e); }
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
