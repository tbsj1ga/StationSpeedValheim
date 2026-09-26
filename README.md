# StationSpeed

**English** · [Русский](README-RU.md)

A Valheim mod: speed multipliers for the working stations — every kind of
smelter, the cooking stations and the oven, the fermenter, beehives, sap
extractors and the growth of what you plant (crops and saplings separately).
One multiplier per kind, optionally one per prefab. The server's settings apply
on every client with the mod.

The difference from Valheim Plus / OdinsQOL and the like is how it reaches a
player **without mods**: barrels and plants stay consistent for them (see
below) instead of showing "still fermenting" when yours says "done"; and what
they put in themselves is shifted by the host.

Status and plans are in `ROADMAP.md`, version history in `CHANGELOG.md`.

## How it works

The game keeps station progress in two different ways, and the mod treats them
differently.

**Ticking stations** — `Smelter` (smelter, blast furnace, charcoal kiln,
spinning wheel, windmill, eitr refinery), `CookingStation` (cooking stations,
oven), `Beehive`, `SapCollector`. Once a second the **owner** client of the
station (usually the nearest player) adds the elapsed time to an accumulator in
the ZDO and produces an item when `m_secPerProduct` has built up. That field
lives on the instance, and the mod sets it to the prefab's value divided by the
multiplier: in `Awake`, and again for every loaded station whenever the
settings change (config edit, values from the server, `stationspeed rescan`).
It works whenever a client with the mod owns the station. If a player without
the mod owns it, the station runs at vanilla speed; the picture (items, slots,
fuel) is right for them either way, because it comes from the ZDO. To keep the
stations near the host with the host, there is a separate mod, [HostOwner](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/)
(group `Stations`).

Fuel. Smelters burn it in fractions of a product (`m_fuelPerProduct /
m_secPerProduct` per second), so coal per bar does not change by itself. The
oven burns its fuel by the clock (`m_secPerFuel`), so `ScaleOvenFuel` (on)
divides that too — wood per bread as in vanilla; off — wood burns for the
vanilla time and the speed-up makes baking cheaper. The windmill still depends
on the wind on top of the multiplier.

**Timestamp stations** — `Fermenter` and `Plant`. The ZDO holds only the start
moment (`StartTime`, `plantTime`); **every client computes readiness itself**
from its own copy of the duration (`m_fermentationDuration`, `GetGrowTime()`).
Change the duration and your client and a vanilla one disagree about the same
barrel: "done" for you, "fermenting" for them, and their `Interact` will not
send `RPC_Tap` until the vanilla time has passed. So the duration is left alone
and the timestamp is moved once instead: when the item is added the owner
writes `start = now − D + D/k`. The vanilla formula then gives the same answer
on every client, with the mod or without. Plants work the same way with
`plantTime`; their `GetGrowTime()` is seeded per plant, so all clients agree on
it too.

Next to the shifted timestamp the mod leaves its own mark
(`j1ga.stationspeed.shifted` — the timestamp after the shift): while it equals
the timestamp, the shift is done; a new load gives a new timestamp and settles
the question by itself.

**The host shifts other players' loads.** If a player without the mod filled a
barrel or planted something, they were the owner at that moment and nothing ran
on their side; but the server holds every ZDO of the world and gets the new
timestamp within a second. The host (or a dedicated server with the mod) walks
the world's objects every `HostScanInterval` seconds — 20,000 per frame, so
there is no hitch — and shifts anything whose timestamp is fresh (younger than
five minutes) and differs from the mark. The write reaches the owner like any
other: `ZDOMan.RPC_ZDOData` goes by the data revision, not by ownership. The
grow time of a plant with no loaded instance is computed the host's way, like
`Plant.GetGrowTime`, from the seed in the ZDO; on the first planting on an
owner both values are compared, and should they ever differ (a game update) a
warning goes to the log and the host stops touching plants.

**Settings from the server.** A second after a client joins (and on every
settings change) the server sends its multipliers, `ByPrefab`, `ScaleOvenFuel`
and `Enabled` through its own routed RPC; a client with the mod uses them while
connected and recalculates the loaded stations at once. So all owners agree on
the speed. Packets not from the server are ignored. Without the mod on the
server, everyone uses their own file.

## Screenshots

| | |
|---|---|
| ![A smelter with a speed multiplier](https://raw.githubusercontent.com/tbsj1ga/StationSpeedValheim/main/docs/media/smelter.webp) | ![From planting to harvest in seconds](https://raw.githubusercontent.com/tbsj1ga/StationSpeedValheim/main/docs/media/crops.webp) |
| A smelter with a speed multiplier | From planting to harvest in seconds |

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

## Installation

Through r2modman / Thunderstore, or put `build/StationSpeed.dll` into

```
%AppData%\r2modmanPlus-local\Valheim\profiles\Valheim\BepInEx\plugins\StationSpeed\
```

(or `build.ps1 -Install`). Needed on the clients that will own stations (in
practice, everyone who plays with mods) and on the host: the host shifts the
loads of players without the mod and hands out the settings. On a dedicated
server it is optional, for the same two things. Players without the mod join
as usual.

## Settings

`BepInEx\config\j1ga.stationspeed.cfg`, created on first start. All
multipliers default to 1, range 0.1…100; 2 is twice as fast, 0.5 half as fast.
Edits apply to loaded stations at once.

| Section | Key | Prefabs / meaning |
|---|---|---|
| Smelters | `Smelter`, `BlastFurnace`, `CharcoalKiln`, `SpinningWheel`, `Windmill`, `EitrRefinery`, `OtherSmelter` | `smelter`, `blastfurnace`, `charcoal_kiln`, `piece_spinningwheel`, `windmill`, `eitrrefinery`, anything else with a `Smelter` |
| Cooking | `CookingStation`, `Oven`, `OtherCooking`, `ScaleOvenFuel` | `piece_cookingstation`, `piece_cookingstation_iron`; `piece_oven`; anything else with a `CookingStation`; burn the oven's fuel with the same multiplier |
| Timestamp stations | `Fermenter`, `Crops`, `Saplings` | `fermenter`; plants that do not grow into a tree; tree saplings |
| Collectors | `Beehive`, `SapCollector` | `piece_beehive`, `piece_sapcollector` |
| Overrides | `ByPrefab` | `prefab=multiplier, …` — takes priority over the kind |
| Host | `HostShift`, `HostScanInterval` | shift other players' loads from the host; scan period, s |
| Sync | `SyncConfig` | the server hands its settings to clients (set on the server) |
| General | `Enabled`, `Debug` | master switch; log every station touched |

Console (F5): `stationspeed status` — the multipliers in effect (the server's,
if connected to a server with the mod) and host statistics; `stationspeed
rescan` — apply the multipliers to the loaded stations again.

## Where things are

| What | Where |
|---|---|
| Config | `BepInEx\config\j1ga.stationspeed.cfg` |
| Sources | `src\StationSpeedPlugin*.cs` — one `partial class`, one file per area |
| Build output | `build\StationSpeed.dll` |
| Mod version (one place) | the `Version` constant in `src\StationSpeedPlugin.cs`; `build.ps1 -Package` writes it into `manifest.json` |
| Reference check | `check-refs.ps1`, run by the build |
| Thunderstore package | `thunderstore\` (manifest, icon 256×256, README) → `build\StationSpeed-<version>.zip` |
| License | `LICENSE`, MIT |

| File | Contents |
|---|---|
| `StationSpeedPlugin.cs` | constants, `Awake`/`Update`/`OnDestroy`, `ShiftStart` with the mark, `PrefabComponent`, helpers, error handling |
| `StationSpeedPlugin.Config.cs` | every `ConfigEntry`, the values in effect (own or the server's), multiplier lookup by prefab and kind, crop/sapling |
| `StationSpeedPlugin.Patches.cs` | applying to ticking stations from the prefab, `Reapply`, the shift on the owner, the stations' Harmony patches |
| `StationSpeedPlugin.Host.cs` | the ZDO walk on the host, freshness and the mark, the copy of `GetGrowTime` |
| `StationSpeedPlugin.Sync.cs` | the settings routed RPC: sending from the server, receiving on the client, `ZNet` patches |
| `StationSpeedPlugin.Commands.cs` | the `stationspeed` console command |

## Building

```
powershell -ExecutionPolicy Bypass -File .\build.ps1            # build and check references
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install   # ... and copy into plugins
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Package   # ... and make the Thunderstore zip
```

The compiler is `csc.exe` from the .NET Framework (C# 5: no `out var`, `?.`,
`$""`, `nameof`); references come straight from the game folder and the
r2modman profile's `BepInEx\core`; the paths are at the top of `build.ps1`,
`check-refs.ps1` and `src\StationSpeed.csproj`. After the build,
`check-refs.ps1` uses Mono.Cecil to check every type and member reference, the
reflection targets (`Plant.GetGrowTime`, `ZDOMan.m_objectsByID`) and the Harmony
patch targets against the game.

## Repository

Branch `main` on GitHub: https://github.com/tbsj1ga/StationSpeedValheim. Versioned:
sources, `.csproj`, scripts, documentation, the Thunderstore template and
`build\StationSpeed.dll`. Not versioned: the BepInEx config, `bin/`, `obj/`,
zip packages — see `.gitignore`.

## More mods by j1gA

| | Mod |
|---|---|
| [![LivingMap](https://raw.githubusercontent.com/tbsj1ga/LivingMapValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/LivingMap/) | **[LivingMap](https://thunderstore.io/c/valheim/p/j1gA/LivingMap/)** — Your buildings, roads and cleared forest on the map and the minimap. |
| [![WeaponArts](https://raw.githubusercontent.com/tbsj1ga/WeaponArtsValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/WeaponArts/) | **[WeaponArts](https://thunderstore.io/c/valheim/p/j1gA/WeaponArts/)** — One key, one active ability per weapon: stagger, taunt, heals, berserk, crits. |
| [![ExtendedBosses](https://raw.githubusercontent.com/tbsj1ga/ExtendedBossesValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/ExtendedBosses/) | **[ExtendedBosses](https://thunderstore.io/c/valheim/p/j1gA/ExtendedBosses/)** — Raid-style boss fights: phases, adds, nests, shields, marks — built from vanilla parts. |
| [![HostOwner](https://raw.githubusercontent.com/tbsj1ga/HostOwnerValheim/main/docs/media/icon-128.png)](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/) | **[HostOwner](https://thunderstore.io/c/valheim/p/j1gA/HostOwner/)** — The host takes ownership of stations and bosses near it, so its mods work for everyone. |

## AI assistance

This mod was developed with the help of an AI assistant (Claude by Anthropic).
The code and the documentation were written together with it and checked
against the game's IL; the design decisions, in-game testing and releases are
the author's.
