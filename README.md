# Hideout We Deserve - TimeRP

By HeroicLarvy.

This folder is the drag-and-drop package for the standalone Hideout We Deserve - TimeRP addon. Copy the `SPT_Runtime` folder into the SPT installation folder, keeping the directory structure intact.

- `SPT_Runtime\user\mods\HideoutTimeRaid`: SPT server mod, config editor, and web test page. Survived PMC and scav raids (SPT's `Savage` raid type) advance the PMC hideout profile; for Fika, install this on the server that owns player profiles.

Restart the SPT server after installing or replacing the mod.

The config editor is available at `https://127.0.0.1:6969/HWDTimeRP` or from the SPT server web UI's mod links. Config changes are saved to the addon's `config.json`. Progression tests are hidden by default; to reveal them, stop the server, set `"debug": true` in that JSON file, and restart SPT. The tests make real, saved changes to the selected profile's active craft and hideout upgrade timers. Use them only for testing.

The server mod source and build instructions are in `SPT_Runtime\user\mods\HideoutTimeRaid\README.md`. To rebuild the server mod against your SPT installation:

```powershell
dotnet build SPT_Runtime\user\mods\HideoutTimeRaid\HideoutTimeRaid.csproj -c Release -p:SPTRuntimePath="D:\Path\To\SPT\SPT_Runtime"
```
