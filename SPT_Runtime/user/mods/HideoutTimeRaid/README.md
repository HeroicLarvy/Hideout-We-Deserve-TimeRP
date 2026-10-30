# Hideout We Deserve - TimeRP

By HeroicLarvy.

Standalone SPT 4.1 server mod. Install this folder as `SPT_Runtime\user\mods\HideoutTimeRaid`. It requires `HideoutTimeRaid.dll` at the top level; SPT will not load the source files by themselves. For Fika, install it on the server that owns player profiles. The config editor and optional test interface are served by the SPT server at `https://127.0.0.1:6969/HWDTimeRP`.

## Time calculation

Only survived PMC or scav raids advance hideout timers. Raid duration is read in seconds from SPT's local raid-end request. SPT's `Savage` raid identifier is recognized as a scav raid.

- The first 15 real minutes count at 7x on every map.
- Time after 15 minutes also counts at 7x, multiplied by that map's danger value.
- Deaths and run-throughs grant no time.

For example, a 25-minute raid grants 178.5 minutes of progression on Woods or 206.5 minutes on Factory.

```text
progress = 7 * min(raid duration, 15 minutes)
         + 7 * map danger * max(raid duration - 15 minutes, 0)
```

## Configuration

Edit `config.json` directly or open the authenticated web config editor at `/HWDTimeRP`. The editor validates and saves changes to the mod's `config.json`, and applies the new values immediately without a server restart.

- `baseTimeMultiplier`: default time multiplier (`7`).
- `dangerThresholdMinutes`: duration before map danger bonuses apply (`15`).
- `defaultDangerMultiplier`: multiplier for maps not explicitly listed (`1`, no danger bonus).
- `debug`: set to `true` in `config.json` to show the progression test panel. The setting is intentionally not exposed as an editable web UI control; test endpoints are unavailable when debug is false.
- `mapDangerMultipliers`, highest to lowest:
  - Labyrinth: `1.75`
  - Labs: `1.65`
  - Streets of Tarkov: `1.55`
  - Factory: `1.45`
  - Reserve: `1.35`
  - Lighthouse: `1.30`
  - Ground Zero: `1.25`
  - Interchange: `1.20`
  - Customs: `1.15`
  - Shoreline: `1.10`
  - Woods: `1.05`

Factory day/night, Ground Zero variants, Customs, Reserve, and Streets map IDs are normalized to their config names. Craft progress is advanced directly in SPT's persisted `Production.Progress` seconds field, then saved with the PMC hideout profile. Hideout upgrades are advanced through their completion timer.

## Web config editor and test interface

Open the SPT server web UI and select **Hideout We Deserve - TimeRP**, or visit `https://127.0.0.1:6969/HWDTimeRP`. Update and save config values in the Configuration section. To show Progression Tests, stop the server, set `"debug": true` in this mod's `config.json`, and restart SPT. Select a profile to use either test:

- **Test 40 seconds on Factory**: runs the same progression path using a 40-second Factory raid (4 minutes 40 seconds of progression with the default config).
- **Test 40 minutes on Woods**: runs the same progression path using a 40-minute Woods raid, applying the 15-minute threshold and Woods danger multiplier (288.75 minutes of progression with the default config).

Both tests immediately change and save active crafts and hideout upgrades on the selected PMC hideout profile. They are real profile mutations, not previews. The server log reports each test and the number of timers changed. The client may need to refresh or reopen the hideout to display the updated timers.

## Build

Build against your SPT server runtime:

```powershell
dotnet build HideoutTimeRaid.csproj -c Release -p:SPTRuntimePath="D:\Random Games\SPT\SPT_Runtime"
```

The build outputs `HideoutTimeRaid.dll` and `config.json` under `bin\Release\net10.0`. Copy both into this mod's root folder. SPT searches only the mod root for assemblies.
