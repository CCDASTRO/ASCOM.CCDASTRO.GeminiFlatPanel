# Gemini installer

Build the Release net48 server first, then run Inno Setup 6:

```powershell
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' '.\installer\GeminiFlatPanel.iss'
```

The version comes from the built server executable. Output is `dist/CCDASTRO.GeminiFlatPanel.Setup-<version>.exe`.

Based on the installed ASCOM Developer Installer Generator `DriverInstallTemplate.iss`, using its .NET local-server registration model, Common Files ASCOM location, prerequisite check, and supplied wizard artwork. The original template is retained in `resources/ASCOM-DriverInstallTemplate.iss.txt` for reference. Adaptations include Platform 7.1/.NET 4.8 checks, a stable Inno AppId for upgrades, running-server checks, verification of both COM registrations in both registry views, and an optional finish-page HTML guide. No driver license is assigned by this packaging work.

The checkbox is unchecked by default. Checked opens the installed guide in the user's associated browser; unchecked does not. Silent installations skip opening the guide. A Start menu shortcut provides later access.

The package contains only the server executable/config, Core library, and guide. Settings, calibration, logs, and ASCOM dependencies are not bundled. Settings are retained on uninstall. Installation registers both driver interfaces but does not connect hardware.

Before distribution, exercise install/upgrade/uninstall on a test Windows system with ASCOM 7.1 and .NET 4.8: confirm both chooser entries, both checkbox states, silent install, running-server refusal, and preserved settings. Compilation alone does not verify these runtime paths. The package is unsigned.
