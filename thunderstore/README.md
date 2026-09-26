# StationSpeed

Speed multipliers for the working stations: smelter, blast furnace, charcoal
kiln, spinning wheel, windmill, eitr refinery, cooking stations and the oven,
fermenter, beehive, sap extractor, crops and saplings. One multiplier per kind,
optional overrides per prefab. The server's settings apply on every client
with the mod.

| | |
|---|---|
| ![A smelter with a speed multiplier](https://raw.githubusercontent.com/tbsj1ga/StationSpeedValheim/main/docs/media/smelter.webp) | ![Crops grow in seconds](https://raw.githubusercontent.com/tbsj1ga/StationSpeedValheim/main/docs/media/crops.webp) |
| A smelter with a speed multiplier | Crops grow in seconds |

What makes it different from other speed mods is how it treats **players
without the mod**:

- Smelters, cooking stations and collectors advance on the client that owns
  them (the nearest player). With the mod on that client they run faster;
  everyone sees the right items and fuel either way. Fuel per product stays as
  in vanilla (for the oven, see `ScaleOvenFuel`).
- The fermenter and plants store only a start time, and every client computes
  readiness from it with its own copy of the duration. Changing the duration
  would make a modded and a vanilla client disagree about the same barrel. So
  this mod leaves the duration alone and moves the start time once, when the
  item is added: the vanilla formula then gives the same answer on every client.
- What a player without the mod adds or plants is shifted by the host a few
  seconds later: the host walks the world's objects and moves any fresh,
  not-yet-shifted start time itself. Works on the hosting player or on a
  dedicated server with the mod.
- The server sends its settings to every connecting client with the mod, so all
  owners agree on the speed. Changes apply to loaded stations at once.

Console: `stationspeed status`, `stationspeed rescan`. Config in
`BepInEx/config/j1ga.stationspeed.cfg`.

Install on every client that plays with mods and on the host; optional on a
dedicated server (for the host shift and the settings). Players without the
mod join as usual.

## Compatibility

Tested with **Valheim 1.0.16** (network version 40), **BepInEx 5.4.23.5** (BepInExPack_Valheim 5.4.2351).

## Who needs it

| Who | What |
|---|---|
| Players with the mod | stations they own run at the configured speed |
| Host | recommended: shifts barrels/plants of players without the mod and sends its settings |
| Dedicated server | optional, for the same two things |
| Players without the mod | join as usual; barrels and plants stay consistent for them |

## Known conflicts

- Other mods that change station or growth speed (Valheim Plus, OdinsQOL and similar) — the multipliers stack or fight. Use one of them for each kind of station.
- Mods that change a fermenter's duration or a plant's grow time on one client only break the same thing this mod avoids: vanilla clients disagreeing about readiness.

## Bugs and feedback

GitHub Issues: https://github.com/tbsj1ga/StationSpeedValheim/issues — please attach `BepInEx/LogOutput.log`.

## More mods by j1gA

| | Mod |
|---|---|
| [![LivingMap](https://raw.githubusercontent.com/tbsj1ga/LivingMapValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/LivingMap/) | **[LivingMap](https://thunderstore.io/c/valheim/p/j1gA/LivingMap/)** — Your buildings, roads and cleared forest on the map and the minimap. |
| [![WeaponArts](https://raw.githubusercontent.com/tbsj1ga/WeaponArtsValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/WeaponArts/) | **[WeaponArts](https://thunderstore.io/c/valheim/p/j1gA/WeaponArts/)** — One key, one active ability per weapon: stagger, taunt, heals, berserk, crits. |
| [![ExtendedBosses](https://raw.githubusercontent.com/tbsj1ga/ExtendedBossesValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/ExtendedBosses/) | **[ExtendedBosses](https://thunderstore.io/c/valheim/p/j1gA/ExtendedBosses/)** — Raid-style boss fights: phases, adds, nests, shields, marks — built from vanilla parts. |
| [![HostOwner](https://raw.githubusercontent.com/tbsj1ga/HostOwnerValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/) | **[HostOwner](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/)** — The host takes ownership of stations and bosses near it, so its mods work for everyone. |

