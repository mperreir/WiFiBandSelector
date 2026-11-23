# WiFiBandSelector

A small Windows notification-area utility that switches a laptop's Wi-Fi band in two clicks:
**2.4 GHz**, **5 GHz** or **6 GHz** when the driver exposes them. It only writes the advanced
settings the driver already declares in the registry; it changes no driver or Wi-Fi profile and
sends no data anywhere.

> Target devices: two HP ProBook 440 G9 laptops used by students. Developed and verified on a
> MediaTek MT7921 adapter; classification is generic and works with other drivers, but the
> ProBooks (Realtek) still need validation with a diagnostic report.

## Language

The application uses the Windows default UI language: **French** and **English** are supported.
Any other UI culture falls back to English. This applies to the tray menu, notifications,
messages, logs and diagnostic reports.

> The NSIS installer assistant itself is still French; only the installed application is localized.

## Installation (for students)

1. Download `WiFiBandSelector-Setup-0.2.0.exe` from the
   [Releases](https://github.com/mperreir/WiFiBandSelector/releases) page.
2. If Windows shows "Windows protected your PC", click **More info** and then **Run anyway**
   (the executable is not code-signed).
3. Run the file, answer **Yes** to the UAC prompt and then **Finish**.
4. The .NET 8 Desktop Runtime is downloaded and installed automatically if needed (an Internet
   connection is required). Otherwise use
   `WiFiBandSelector-Setup-0.2.0.exe /RUNTIME=C:\path\to\windowsdesktop-runtime-win-x64.exe`.
5. A Wi-Fi icon appears near the clock; the application starts at every logon, for all accounts.

## Usage

Click the icon (left or right click) to open the menu:

- header: selected adapter, current setting and the band actually associated;
- **Preferred band**: soft preference, both bands remain allowed;
- **Lock a band**: strict setting, offered only when the driver exposes it;
- **Roaming**: aggressiveness, with a reset to the driver default;
- **Reconnect now**, **Start with Windows**, **Undo last change**;
- **Diagnostic...**, **Log...**, **About**, **Exit**.

In French these entries are **Bande préférée**, **Verrouiller une bande**, **Itinérance**,
**Reconnecter maintenant**, **Démarrage avec Windows**, **Annuler la dernière modification**,
**Diagnostic…**, **Journal…**, **À propos** and **Quitter**.

After a lock, the application checks that the connection comes back within 30 seconds; otherwise
it restores the previous value and shows a warning. It also refuses to lock a band that is absent
from a non-empty network scan.

If the icon says "Restart as administrator" ("Relancer en administrateur"), the menu is read-only:
registry writes require the elevated rights provided by the scheduled task.

## Diagnostics

Menu **Diagnostic...** ("Diagnostic…"): a report is written to
`%LOCALAPPDATA%\WiFiBandSelector\`, copied to the clipboard and opened in Explorer. Send that
report (and, if needed, `%LOCALAPPDATA%\WiFiBandSelector\log.txt`) to the developer.

For a headless report:

```powershell
& "C:\Program Files\WiFiBandSelector\WiFiBandSelector.exe" /report "$env:TEMP\report.txt"
```

The report lists the adapter, the recognized families, the associated band, all `enum` values of
the driver and the classification notes. An adapter without a band keyword is reported as
"unsupported": that is a normal result, not a bug.

For a headless write (support only, requires elevation):

```powershell
& "C:\Program Files\WiFiBandSelector\WiFiBandSelector.exe" /set preference 1 "$env:TEMP\result.txt"
```

The family is `preference`, `lock` or `roaming` (the v0.1 French aliases `préférence`, `verrou`
and `itinérance` are still accepted); the code comes from the report. The result (`Applied`,
`Refused`, ...) is written to the file; exit code `0` means success, `1` means failure.

## Known limitations

- **Realtek RTL8852BE**: the exact keyword names still need confirmation on the ProBooks; the
  report will show whether the classification needs adjusting.
- **Intel AX211**: no usable lock (`Wireless Mode` would reduce throughput); only the preference
  is offered.
- **Roaming**: without several access points on the same SSID, this setting has no effect.
- RTL8852BE Wi-Fi instability is often caused by the driver or power saving; those settings are
  deliberately out of scope.
- A driver update may reset values: the application re-reads the registry each time the menu opens.
- A preference stays a preference: the driver may choose another band when the signal is weak.
- The connection drops briefly when a change is applied (a few seconds in lock mode).
- The executable is not signed: prefer a USB stick or `Unblock-File` over a OneDrive transfer.
- The scheduled task runs with "highest available" rights for the *Users* group: a standard
  account can therefore change a network setting through the application.

## Development

```powershell
git clone https://github.com/mperreir/WiFiBandSelector.git
cd WiFiBandSelector
dotnet test tests\WiFiBandSelector.Tests\WiFiBandSelector.Tests.csproj
.\build.ps1
```

`build.ps1` runs the tests, publishes x64, downloads portable NSIS 3.11 and the `inetc` plugin if
missing, then runs `makensis`. Output: `dist\WiFiBandSelector-Setup-<version>.exe`.

- `src\WiFiBandSelector\Resources\Strings.resx` (English, neutral) and `Strings.fr.resx` (French)
  hold all user-facing strings. Add the same key to both files, then reference it through the
  generated `Strings` class; use `FormatWith(...)` for templates with `{0}` placeholders.
- `tools\fixtures\*.json`: registry snapshots replayed by the tests (MediaTek, Intel, Realtek,
  exotic adapter, adapter without bands). The Realtek fixture is still to be confirmed with the
  real INF.
- `installer\setup.nsi`: writes the scheduled-task XML in UTF-16 with BOM using the real install
  path, line by line — an NSIS variable is limited to 1024 characters and would truncate it.
- `C:\Users\nous\.opencode\plan\wifi-band-selector.md`: working plan (outside the repository).

## License

MIT - 2026 Matthieu Perreira Da Silva.
