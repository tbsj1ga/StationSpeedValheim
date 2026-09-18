# StationSpeed

Speed multipliers for the working stations: smelter, blast furnace, charcoal
kiln, spinning wheel, windmill, eitr refinery, cooking stations and the oven,
fermenter, beehive, sap extractor, crops and saplings. One multiplier per kind,
optional overrides per prefab. The server's settings apply on every client
with the mod.

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

*Developed with the help of an AI assistant (Claude by Anthropic); the design
decisions, verification against the game code and in-game testing are the
author's.*
