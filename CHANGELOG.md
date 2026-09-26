# Changelog

**English** · [Русский](CHANGELOG-RU.md)

The version is set in one place — `StationSpeedPlugin.Version` in `src/StationSpeedPlugin.cs`.

## 0.2.1

- Package page: a gallery of animations and screenshots, and a section with the author's
  other mods (icons, one line each, links). No code changes.

## 0.2.0

- **The host shifts other players' loads.** The host (or a dedicated server
  with the mod) walks the world's ZDOs every `HostScanInterval` seconds, split
  over several frames: a barrel or a plant whose start timestamp is fresh and
  differs from our mark in the ZDO was just loaded by a player without the mod
  and is shifted from the host. The mark (`j1ga.stationspeed.shifted`, the
  start timestamp after the shift) is now set on every shift, by the owner too,
  so nothing is shifted twice; a new load into the same barrel is a new
  timestamp that differs from the mark. The grow time of a plant with no
  instance is computed like `Plant.GetGrowTime` (seed from the ZDO or from its
  id, as in `Plant.Awake`); on the first planting on an owner both values are
  compared, a mismatch is logged once, and then the host leaves plants alone.
  Section `07 Host`.
- **Settings from the server.** A second after a client joins (and on every
  settings change) the server sends its multipliers, `ByPrefab`,
  `ScaleOvenFuel` and `Enabled` through its own routed RPC
  `j1ga.stationspeed.config`; a client with the mod uses them instead of its
  own file while connected. Packets not from the server or with another format
  version are ignored. Without the mod on the server — your own file. Section
  `08 Sync`, switched off on the server.
- **Recalculating loaded stations.** Multipliers of ticking stations are now
  computed from the prefab's value instead of dividing the current one, so they
  can be applied again: on any settings change, when settings arrive from the
  server and with `stationspeed rescan` every loaded smelter, cooking station,
  beehive and sap extractor gets the new values without rejoining. An object
  with no registered prefab is divided in place in `Awake`, as before.
- **Crops and saplings separately:** `Plants` is replaced by `Crops` and
  `Saplings`. A sapling is a plant that grows into a tree (`TreeBase` on the
  grown prefab); everything else is a crop.
- **Oven fuel:** `ScaleOvenFuel` (on by default) divides `m_secPerFuel` too, so
  wood per bread stays as in vanilla — smelters do this by themselves because
  they burn fuel in fractions of a product. The value is an integer, seconds
  are rounded.
- Windmill: checked in the IL that `m_secPerProduct` is the main limit and the
  wind strength multiplies the timer step on top; the multiplier works
  together with the wind.
- Numbers in the log use a dot whatever the system language.
- `Enabled=false` now returns loaded stations to vanilla speed at once (through
  the recalculation).

## 0.1.0

- First version. Multipliers for the smelter, blast furnace and charcoal kiln,
  spinning wheel, windmill, eitr refinery, cooking stations and the oven,
  beehive, sap extractor, fermenter and plant growth; per-prefab override.
- Ticking stations: time per product is divided in `Awake` (applies on the
  owner).
- Fermenter and plants: a one-time shift of the start timestamp on the owner,
  so a player without the mod sees the same readiness as one with it.
- Console command `stationspeed status`.
- Build `build.ps1`, reference check `check-refs.ps1` (including Harmony patch
  targets).
