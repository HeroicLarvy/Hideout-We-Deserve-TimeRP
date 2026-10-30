# Hideout We Deserve - TimeRP

Source code for the standalone SPT 4.1 server mod by HeroicLarvy. The install-ready mod is published as a ZIP on the [GitHub Releases](https://github.com/HeroicLarvy/hideout-we-deserve-timerp/releases) page; the repository itself contains the source project, not a prebuilt `SPT_Runtime` install.

## Features

- Survived PMC and scav raids (SPT's `Savage` raid type) advance hideout crafts and station upgrades.
- The first 15 real minutes count at 7x on every map. Time after 15 minutes also counts at 7x, multiplied by the configured map danger value.
- Deaths and run-throughs grant no time.
- For Fika, install the server mod on the server that owns player profiles.
- The config editor is available at `https://127.0.0.1:6969/HWDTimeRP`. Progression tests are hidden unless `"debug": true` is set directly in `config.json`.

## Source layout

- `src/HideoutTimeRaid/HideoutTimeRaid.cs`: server mod source.
- `src/HideoutTimeRaid/HideoutTimeRaid.csproj`: .NET 10 project, built against the local SPT runtime.
- `src/HideoutTimeRaid/config.json`: default configuration.
- `src/HideoutTimeRaid/package.json`: SPT package metadata and version.
- `src/HideoutTimeRaid/wwwroot/index.html`: web config editor.
- `tools/Build-Release.ps1`: builds the server assembly and creates an install-ready ZIP containing `SPT_Runtime\user\mods\HideoutTimeRaid`.

## Build and package

Install the .NET 10 SDK and provide the path to your SPT `SPT_Runtime` folder:

```powershell
.\tools\Build-Release.ps1 -SPTRuntimePath "D:\Path\To\SPT\SPT_Runtime"
```

The script builds the source and writes `dist\Hideout-We-Deserve-TimeRP-<version>.zip`. Extract that ZIP and copy its `SPT_Runtime` folder into the SPT installation folder. Build outputs, generated archives, and prebuilt mod binaries are not tracked in this source repository.
