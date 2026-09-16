# StationSpeed

Speed multipliers for the working stations: smelter, blast furnace, charcoal
kiln, spinning wheel, windmill, eitr refinery, cooking stations and the oven,
fermenter, beehive, sap extractor, and the growth of everything planted. One
multiplier per kind, optional overrides per prefab.

What makes it different from other speed mods is how it treats **players
without the mod**:

- Smelters, cooking stations and collectors advance on the client that owns
  them (the nearest player). With the mod on that client they run faster;
  everyone sees the right items and fuel either way.
- The fermenter and plants store only a start time, and every client computes
  readiness from it with its own copy of the duration. Changing the duration
  would make a modded and a vanilla client disagree about the same barrel. So
  this mod leaves the duration alone and moves the start time once, when the
  item is added: the vanilla formula then gives the same answer on every client.

Console: `stationspeed status`. Config in `BepInEx/config/j1ga.stationspeed.cfg`.
Stations pick up new values when they are loaded again.

Client-side; not needed on a dedicated server.
