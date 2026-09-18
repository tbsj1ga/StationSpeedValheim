using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace StationSpeed
{
    // The server's settings on every client with the mod, so that a station runs at the same
    // speed whoever owns it. One routed RPC, sent to a peer a moment after its login and to
    // everybody when a setting changes on the server; the client keeps the values beside its
    // own file and uses them while connected. A server without the mod sends nothing, and
    // the client's own file applies.
    public partial class StationSpeedPlugin
    {
        private const string RpcName = "j1ga.stationspeed.config";
        private const int PacketVersion = 1;

        private bool _serverSynced;
        private bool _serverEnabled = true;
        private bool _serverScaleOvenFuel = true;
        private readonly Dictionary<string, float> _serverValues = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _serverOverrides = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        // set by any config change; handled once per frame in Update
        private bool _syncDirty;

        // ------------------------------------------------------------------
        // both sides
        // ------------------------------------------------------------------
        private void RegisterRpc()
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.Register<ZPackage>(RpcName, new Action<long, ZPackage>(OnConfigPacket));
        }

        private void FlushSync()
        {
            _syncDirty = false;
            int n = Reapply();
            if (n > 0) Debug("multipliers re-applied to " + n + " loaded stations");
            ZNet znet = ZNet.instance;
            if (znet == null || !znet.IsServer() || !_cfgSync.Value || ZRoutedRpc.instance == null) return;
            if (znet.GetPeers().Count == 0) return;
            SendConfig(ZRoutedRpc.Everybody);
            Debug("settings sent to " + znet.GetPeers().Count + " peers");
        }

        // ------------------------------------------------------------------
        // server
        // ------------------------------------------------------------------
        private ZPackage BuildPacket()
        {
            ZPackage pkg = new ZPackage();
            pkg.Write(PacketVersion);
            pkg.Write(_cfgEnabled.Value);
            pkg.Write(_multipliers.Count);
            foreach (ConfigEntry<float> e in _multipliers)
            {
                pkg.Write(e.Definition.Key);
                pkg.Write(e.Value);
            }
            pkg.Write(_cfgOverrides.Value ?? "");
            pkg.Write(_cfgScaleOvenFuel.Value);
            return pkg;
        }

        private void SendConfig(long target)
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(target, RpcName, new object[] { BuildPacket() });
        }

        // ZNet.RPC_PeerInfo on the server: the peer has just been accepted, its routed RPC is
        // up. A moment later, so that the client has finished its own end of the handshake.
        private void OnPeerInfo(ZNet znet, ZRpc rpc)
        {
            if (!znet.IsServer() || !_cfgSync.Value || rpc == null) return;
            foreach (ZNetPeer peer in znet.GetPeers())
            {
                if (peer == null || peer.m_rpc != rpc) continue;
                if (peer.m_uid != 0L) StartCoroutine(SendConfigLater(peer.m_uid));
                return;
            }
        }

        private IEnumerator SendConfigLater(long uid)
        {
            yield return new WaitForSeconds(1f);
            ZNet znet = ZNet.instance;
            if (znet == null || !znet.IsServer() || ZRoutedRpc.instance == null || !_cfgSync.Value) yield break;
            ZNetPeer peer = znet.GetPeer(uid);
            if (peer == null) yield break;
            try
            {
                SendConfig(uid);
                Debug("settings sent to " + peer.m_playerName);
            }
            catch (Exception e)
            {
                Fail("send config", e);
            }
        }

        // ------------------------------------------------------------------
        // client
        // ------------------------------------------------------------------
        private void OnConfigPacket(long sender, ZPackage pkg)
        {
            try
            {
                ZNet znet = ZNet.instance;
                if (znet == null || znet.IsServer() || pkg == null) return;
                ZNetPeer server = znet.GetServerPeer();
                if (server == null || sender != server.m_uid)
                {
                    Logger.LogWarning("Settings packet from peer " + sender + ", which is not the server; ignored.");
                    return;
                }
                int version = pkg.ReadInt();
                if (version != PacketVersion)
                {
                    Logger.LogWarning("The server runs a " + Name + " with packet version " + version + ", this one understands " + PacketVersion + "; using the local settings.");
                    return;
                }
                bool enabled = pkg.ReadBool();
                int n = pkg.ReadInt();
                if (n < 0 || n > 1000) return;
                Dictionary<string, float> values = new Dictionary<string, float>();
                for (int i = 0; i < n; i++)
                {
                    string key = pkg.ReadString();
                    float v = pkg.ReadSingle();
                    if (!string.IsNullOrEmpty(key)) values[key] = v;
                }
                string overrides = pkg.ReadString();
                bool scaleOvenFuel = pkg.ReadBool();

                _serverValues.Clear();
                foreach (KeyValuePair<string, float> kv in values) _serverValues[kv.Key] = kv.Value;
                ReadOverrides(overrides, _serverOverrides);
                _serverEnabled = enabled;
                _serverScaleOvenFuel = scaleOvenFuel;
                _serverSynced = true;

                int applied = Reapply();
                Logger.LogInfo("Settings from the server: " + StatusValues() + (applied > 0 ? " Re-applied to " + applied + " loaded stations." : ""));
            }
            catch (Exception e)
            {
                Fail("config packet", e);
            }
        }

        private void ClearServerValues()
        {
            _serverSynced = false;
            _serverValues.Clear();
            _serverOverrides.Clear();
            _serverEnabled = true;
            _serverScaleOvenFuel = true;
        }

        // ------------------------------------------------------------------
        // Harmony
        // ------------------------------------------------------------------
        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class ZNet_Awake_Patch
        {
            private static void Postfix()
            {
                StationSpeedPlugin p = Instance;
                if (p == null) return;
                try { p.RegisterRpc(); } catch (Exception e) { p.Fail("ZNet.Awake", e); }
            }
        }

        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        private static class ZNet_RPC_PeerInfo_Patch
        {
            private static void Postfix(ZNet __instance, ZRpc rpc)
            {
                StationSpeedPlugin p = Instance;
                if (p == null || p._disabledByErrors) return;
                try { p.OnPeerInfo(__instance, rpc); } catch (Exception e) { p.Fail("ZNet.RPC_PeerInfo", e); }
            }
        }

        // The world is gone with the ZNet: back to the local file, nothing left to scan.
        [HarmonyPatch(typeof(ZNet), "OnDestroy")]
        private static class ZNet_OnDestroy_Patch
        {
            private static void Postfix()
            {
                StationSpeedPlugin p = Instance;
                if (p == null) return;
                try
                {
                    p.StopAllCoroutines();
                    p.ClearServerValues();
                    p.ResetScan();
                    p._indexedScene = null;
                    p._saplingByPrefab.Clear();
                }
                catch (Exception e) { p.Fail("ZNet.OnDestroy", e); }
            }
        }
    }
}
