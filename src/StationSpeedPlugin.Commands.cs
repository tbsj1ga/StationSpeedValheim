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
                "Station Speed. 'stationspeed status' lists the multipliers in effect, 'stationspeed rescan' applies them to the loaded stations again",
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
                if (sub == "rescan")
                {
                    int n = Reapply();
                    if (HostActive) _nextScan = 0f;
                    Say(args, "Multipliers applied to " + n + " loaded stations" + (HostActive ? "; host pass starts now." : "."));
                    return;
                }
                Say(args, "stationspeed status | rescan");
            }
            catch (Exception e)
            {
                Say(args, "stationspeed: " + e.Message);
                Fail("command", e);
            }
        }

        // The multipliers in effect, in one line.
        private string StatusValues()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("smelter x").Append(F(Kind(_cfgSmelter)))
              .Append(", blast furnace x").Append(F(Kind(_cfgBlastFurnace)))
              .Append(", kiln x").Append(F(Kind(_cfgCharcoalKiln)))
              .Append(", spinning wheel x").Append(F(Kind(_cfgSpinningWheel)))
              .Append(", windmill x").Append(F(Kind(_cfgWindmill)))
              .Append(", eitr refinery x").Append(F(Kind(_cfgEitrRefinery)))
              .Append(", other smelters x").Append(F(Kind(_cfgOtherSmelter)))
              .Append(", cooking x").Append(F(Kind(_cfgCookingStation)))
              .Append(", oven x").Append(F(Kind(_cfgOven)))
              .Append(ScaleOvenFuel ? " (fuel too)" : " (fuel as vanilla)")
              .Append(", other cooking x").Append(F(Kind(_cfgOtherCooking)))
              .Append(", fermenter x").Append(F(Kind(_cfgFermenter)))
              .Append(", crops x").Append(F(Kind(_cfgCrops)))
              .Append(", saplings x").Append(F(Kind(_cfgSaplings)))
              .Append(", beehive x").Append(F(Kind(_cfgBeehive)))
              .Append(", sap x").Append(F(Kind(_cfgSapCollector)));
            Dictionary<string, float> overrides = _serverSynced ? _serverOverrides : _overrides;
            if (overrides.Count > 0)
            {
                sb.Append("; overrides:");
                foreach (KeyValuePair<string, float> kv in overrides) sb.Append(' ').Append(kv.Key).Append('=').Append(F(kv.Value));
            }
            sb.Append('.');
            return sb.ToString();
        }

        private string StatusText()
        {
            if (_disabledByErrors) return Name + " is inert after errors, see the log.";
            StringBuilder sb = new StringBuilder();
            if (_serverSynced)
                sb.Append(_serverEnabled ? "Settings from the server: " : "Disabled by the server. Its settings: ");
            else if (!_cfgEnabled.Value)
                return Name + " is disabled in the config.";
            sb.Append(StatusValues());
            ZNet znet = ZNet.instance;
            if (znet != null && znet.IsServer())
            {
                if (HostActive)
                    sb.Append(" Host: ").Append(_hostShifted).Append(" shifted for players without the mod, ")
                      .Append(_lastScanCount).Append(" objects per pass every ").Append(F(_cfgHostInterval.Value)).Append(" s.");
                else
                    sb.Append(" Host shift is off.");
                sb.Append(_cfgSync.Value ? " Settings are sent to the clients." : " Settings are not sent to the clients.");
            }
            return sb.ToString();
        }
    }
}
