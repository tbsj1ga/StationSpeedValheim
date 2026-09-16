using System;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace StationSpeed
{
    public partial class StationSpeedPlugin
    {
        // ------------------------------------------------------------------
        // console command: stationspeed
        // ------------------------------------------------------------------
        private void RegisterCommands()
        {
            new Terminal.ConsoleCommand("stationspeed",
                "Station Speed. 'stationspeed status' lists the multipliers in effect",
                delegate(Terminal.ConsoleEventArgs args) { RunCommand(args); });
        }

        private static void Say(Terminal.ConsoleEventArgs args, string text)
        {
            if (args != null && args.Context != null) args.Context.AddString(text);
        }

        private void RunCommand(Terminal.ConsoleEventArgs args)
        {
            try
            {
                string sub = args.Args.Length > 1 ? args.Args[1].ToLowerInvariant() : "";
                if (sub == "status") { Say(args, StatusText()); return; }
                Say(args, "stationspeed status");
            }
            catch (Exception e)
            {
                Say(args, "stationspeed: " + e.Message);
                Fail("command", e);
            }
        }

        private string StatusText()
        {
            if (_disabledByErrors) return Name + " is inert after errors, see the log.";
            if (!_cfgEnabled.Value) return Name + " is disabled in the config.";
            StringBuilder sb = new StringBuilder();
            sb.Append("smelter x").Append(_cfgSmelter.Value)
              .Append(", blast furnace x").Append(_cfgBlastFurnace.Value)
              .Append(", kiln x").Append(_cfgCharcoalKiln.Value)
              .Append(", spinning wheel x").Append(_cfgSpinningWheel.Value)
              .Append(", windmill x").Append(_cfgWindmill.Value)
              .Append(", eitr refinery x").Append(_cfgEitrRefinery.Value)
              .Append(", cooking x").Append(_cfgCookingStation.Value)
              .Append(", oven x").Append(_cfgOven.Value)
              .Append(", fermenter x").Append(_cfgFermenter.Value)
              .Append(", plants x").Append(_cfgPlants.Value)
              .Append(", beehive x").Append(_cfgBeehive.Value)
              .Append(", sap x").Append(_cfgSapCollector.Value);
            if (_overrides.Count > 0)
            {
                sb.Append("; overrides:");
                foreach (KeyValuePair<string, float> kv in _overrides) sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
            }
            sb.Append(". New values apply to stations loaded from now on.");
            return sb.ToString();
        }
    }
}
