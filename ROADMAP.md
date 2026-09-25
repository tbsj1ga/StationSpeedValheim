# StationSpeed — status and plan

**English** · [Русский](ROADMAP-RU.md)

Station speed multipliers. Ticking stations — time per product from the
prefab's value divided by the multiplier (applies on the owner, recalculated
for loaded stations when the settings change); fermenter and plants — a
one-time shift of the start timestamp with a mark in the ZDO, so a vanilla
client computes readiness the same way; other players' loads are shifted by the
host; the server's settings go to the clients.

Current version: **0.2.0** — built and checked by `check-refs.ps1`. 0.1.0 ran
in game (single player + one guest): the smelter, charcoal kiln and cooking
station multipliers applied correctly, no errors from the mod. What is new in
0.2.0 has not run in game yet. Game: Valheim 1.0.14 (network version 40),
BepInEx 5.4.23.5, Harmony 2.9.

---

## Done

- [x] **Smelter** (smelter, blast furnace, charcoal kiln, spinning wheel,
      windmill, eitr refinery, others) — `m_secPerProduct = prefab / k` in an
      `Awake` postfix and in the recalculation.
- [x] **CookingStation** (cooking stations, oven, others) — `m_cookTime` of
      every conversion from the prefab; burning in the game is
      `2 × m_cookTime`, scaled along. `ScaleOvenFuel` — the oven's
      `m_secPerFuel` too.
- [x] **Beehive**, **SapCollector** — `m_secPerUnit` from the prefab.
- [x] **Fermenter** — prefix/postfix of `RPC_AddItem`: if the `StartTime`
      timestamp changed (the load was accepted) and we are the owner — shift by
      `D − D/k` plus the mark.
- [x] **Plant** — prefix/postfix of `Awake`: if `plantTime` was 0 and appeared
      (just planted, we are the owner) — shift by `T − T/k`, where `T` is
      `GetGrowTime()` through reflection. Crops and saplings have separate
      multipliers (sapling = grown prefab with `TreeBase`).
- [x] **Host shift of other players' loads** — `StationSpeedPlugin.Host.cs`:
      walks `ZDOMan.m_objectsByID`, 20,000 objects per frame, every
      `HostScanInterval` s; a fresh (< 5 min) timestamp without our mark —
      shift and mark. Grow time without an instance — a copy of
      `Plant.GetGrowTime` from the seed in the ZDO, compared with the original
      on the first planting on an owner.
- [x] **Settings sync** — `StationSpeedPlugin.Sync.cs`: routed RPC
      `j1ga.stationspeed.config` a second after `RPC_PeerInfo` and on every
      settings change on the server; the client keeps the server's values next
      to its own while connected.
- [x] **Recalculating loaded stations** on a settings change, when settings
      arrive from the server and on `stationspeed rescan`.
- [x] Multiplier per kind + `ByPrefab` with priority; console `stationspeed
      status | rescan`.
- [x] Build `build.ps1`, check `check-refs.ps1` (types, members, reflection,
      `[HarmonyPatch]` targets).

## Checked against the game's IL (1.0.14)

- [x] `Fermenter.RPC_AddItem` on refusal (not the owner, not empty, wrong item)
      writes nothing — `StartTime` changes only on acceptance, so no double
      shift from that.
- [x] `Fermenter.RPC_Tap` writes an **int** 0 into `StartTime` (a vanilla
      slip), the long timestamp stays; `GetFermentationTime` reads the long.
      Handy for the host: after a tap the timestamp equals the mark, a new load
      gives a new one.
- [x] `Plant.GetGrowTime` = `Lerp(m_growTime, m_growTimeMax, Random.value)`
      with `InitState(m_seed)`; `m_seed` comes from the ZDO `seed`, and when it
      is zero `(int)(uid.ID + uid.UserID)` is written into the ZDO by any
      client. Same on all clients and reproducible without an instance.
- [x] `Smelter.UpdateSmelter`: fuel is spent as `1 / (m_secPerProduct /
      m_fuelPerProduct)` per second — dividing `m_secPerProduct` does not
      change fuel per product by itself. Windmill: the timer step is multiplied
      by `Windmill.GetPowerOutput()`, `m_secPerProduct` stays the threshold.
- [x] `CookingStation.UpdateFuel`: `fuel -= dt / m_secPerFuel` (int) by the
      clock — hence `ScaleOvenFuel`.
- [x] `ZDOMan.RPC_ZDOData` accepts data if the incoming `DataRevision` is
      higher than the local one, ownership is not checked;
      `ZDOMan.CreateSyncList` on the server sends every object of the sector
      with a higher revision. A write from the host reaches the owner.
- [x] `ZNet.RPC_PeerInfo` on the server ends with `ZRoutedRpc.AddPeer` — a
      routed RPC to the new peer can be sent right after; we send after a
      second.

## To check in game

- [ ] `Awake` order: `ZNetView.Awake` before `Plant.Awake` (the game relies on
      it itself), otherwise the prefix sees a `null` ZDO and there is no shift.
- [ ] Barrel at `k=2`: for a second client (without the mod) "done" comes after
      `D/2` and the tap works.
- [ ] Host: a guest without the mod fills a barrel and plants a crop far from
      the host — the host's `Debug` log shows `host: fermenter …` /
      `host: sapling_…` and readiness comes earlier for the guest.
- [ ] Sync: a guest with the mod and other multipliers in their file sees
      `Settings from the server: …` in the log after joining, and
      `stationspeed status` shows the server's values.
- [ ] `plant grow time check: … same as the game's` in the `Debug` log on the
      first planting — the copy of `GetGrowTime` matched.
- [ ] Smelter changing owner mid-batch: the accumulator in the ZDO is shared,
      the speed becomes the new owner's — expected, but make sure nothing
      resets.
- [ ] `stationspeed rescan` and a live config edit change the speed of loaded
      stations without rejoining.
- [ ] Cost of the walk on the host: ~470,000 ZDOs in the current world → ~24
      frames per pass every 5 s; see whether it shows on fps.

## Next

- [x] **Station ownership on the host** — moved into a separate mod, HostOwner
      (group `Stations` on by default): the host takes stations in its active
      area from players without the mod, and the host's multipliers apply to
      them.
- [x] Thunderstore icon 256×256 in the shared style of the set.
- [ ] Migrating the old `Plants` key (0.1.0) to `Crops`/`Saplings`, should
      anyone besides the author need it.

## Ideas

- Show the multiplier in effect in the station's hover text.
- `stationspeed host` — the list of what the host shifted this session, with
  coordinates.
