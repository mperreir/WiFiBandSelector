# AGENTS.md — WiFiBandSelector

Small Windows tray-only utility that changes a Wi-Fi adapter's band from the notification area.
Target devices are HP ProBook 440 G9 laptops; developed on a MediaTek MT7921.

## Ground rules

- .NET 8 / WinForms, `win-x64`, single-file framework-dependent publish, NSIS installer.
- **No PowerShell at app runtime**: registry writes, `wlanapi.dll` and `netsh` only.
- **Generic discovery**: never hard-code an adapter, registry key number, value code or driver
  label. Build menus from the driver's own `enum` values.
- **Never apply anything at startup**: refresh and menu opening only read state.
- Lock safety: refuse a band absent from a non-empty scan; auto-revert after ~30 s if the
  connection does not come back. Undo restores the exact previous value.

## Layout

- `src/WiFiBandSelector/` — app. `DriverModel.cs` classifies driver parameters into families;
  `BandController.cs` reads/writes and owns backup/undo; `TrayContext.cs` builds the menu;
  `Diagnostics.cs` writes the support report; `Resources/` holds the localized strings.
- `tests/WiFiBandSelector.Tests/` — xUnit tests replaying `tools/fixtures/*.json` registry
  snapshots; no live registry required.
- `build.ps1` — tests, publish, portable NSIS 3.11 + `inetc` download, `makensis`.
- `installer/setup.nsi` — downloads the .NET 8 Desktop Runtime, writes the per-machine scheduled
  task, registers the uninstaller.

## Commands

```powershell
dotnet test tests/WiFiBandSelector.Tests/WiFiBandSelector.Tests.csproj
.\build.ps1                       # -> dist\WiFiBandSelector-Setup-<version>.exe
```

`gh` is not installed locally; GitHub releases are created through the REST API with the token
returned by `git credential fill`.

## Localization

- `Resources/Strings.resx` is the neutral **English** source, `Strings.fr.resx` is **French**.
  Add the same key to both files. The strongly typed `Strings` class is generated into `obj`.
- Parameterized messages use `Strings.Key.FormatWith(...)` (extension in `Resources/Localization.cs`).
- The UI culture comes from Windows; unsupported cultures fall back to English.
- README and code comments are English. The NSIS installer assistant stays French by choice.

## Gotchas (learned the hard way)

- Tray left-click must call `NotifyIcon.ShowContextMenu()` via reflection. `ContextMenuStrip.Show()`
  creates an unactivated popup that does not close when clicking outside.
- The scheduled-task XML must be written line by line with `FileWriteUTF16LE`: an NSIS variable is
  limited to 1024 characters and truncates the document. Do not combine `InteractiveToken` with
  `GroupId` in the task XML.
- The generated `.resx` designer lands in `obj`; its namespace must match the embedded resource
  base name `WiFiBandSelector.Resources.Strings`. Internals are exposed to the test assembly.
- Windows PowerShell 5.1 misreads BOM-less UTF-8 scripts containing accented characters: keep the
  BOM on resource-generator scripts.
- `PublishSingleFile` embeds satellite assemblies: the `fr` resources need no installer change.
