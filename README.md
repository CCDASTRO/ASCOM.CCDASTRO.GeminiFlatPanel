# CCDASTRO Gemini FlatPanel

ASCOM CoverCalibrator and Switch drivers for the Gemini motorized flat panel. Version **0.11**.

[Download the installer — GitHub release v0.11](https://github.com/CCDASTRO/ASCOM.CCDASTRO.GeminiFlatPanel/releases/tag/v0.11) · [Offline HTML guide](docs/GeminiFlatPanel-Guide.html)

Control the cover, flat-field light, dew heater, brightness mode, and beep from ASCOM clients such as NINA.

## Install and connect

This prototype package installs both ASCOM devices. It requires Windows 10 or later, ASCOM Platform 7.1 or later, and Microsoft .NET Framework 4.8 or later.



1. Disconnect both Gemini devices in all clients and close Gemini setup before installing or upgrading.

1. Run the installer. On the final page, check **Open the driver and Switch usage guide** to open this page. You can also open it later from the Start menu.

1. In NINA’s Flat Panel selection, choose **CCDASTRO Gemini FlatPanel**. Open its ASCOM setup and select the controller’s actual COM port.

1. For heater and auxiliary controls, select **CCDASTRO Gemini Dew Heater** in NINA’s Switch selection.

1. Connect either or both devices. They share one physical serial connection; disconnecting one leaves it available to the other.

**Prototype motion lock:** fresh installations start with cover motion locked. Existing settings are retained. Verify the controller’s calibration and arrange supervised setup before enabling movement. Installing this package does not calibrate the cover or unlock motion.

Close the Gemini vendor application before connecting through ASCOM so that it releases the serial port. Connection reads status; it does not change brightness mode or beep.




## Flat-panel controls

The Flat Panel device provides cover open/close, cover status, light on/off, and numerical brightness through the standard ASCOM CoverCalibrator interface. The client determines which controls it displays.

Brightness is the light’s numerical level. **High brightness** in the Switch device selects the separate hardware brightness mode. Use the brightness range reported by the driver in your client.

A requested brightness of zero remains a valid light-on setting: ASCOM reports **Ready**. Explicitly turning the light off reports **Off**.

### Stopping a moving cover

**Stop cover motion** sends the controller’s halt command. The cover stays where it stops, which can be partially open. It does not close the cover or resume travel. The driver waits for position readings to confirm that motion has stopped; the interface can remain busy during confirmation. A cover stopped between endpoints is not reported as fully open or closed.




## Four Switch controls


| Control | Usage |
| --- | --- |
| Dew heater (ID 0) | Set 0–100%. Zero turns output off. Fractional values round to the nearest whole percent, with .5 rounding up. |
| High brightness (ID 1) | ON requests high brightness mode; OFF requests low mode. |
| Beep (ID 2) | ON enables beep; OFF disables it. To keep beep enabled, explicitly set ON and leave it there. |
| Stop cover motion (ID 3) | ON sends halt. OFF clears the indicator only and never resumes movement. Turn OFF then ON to send halt again. |
**Mode and beep indicators show the last requested setting, not verified hardware readback.** At a new physical connection their initial OFF indicators are unverified. Set the desired value explicitly. Setup-window commands also update these indicators.

For clients sending numerical values to the binary controls, values below 0.5 mean OFF and values at or above 0.5 mean ON. Values outside each control’s advertised range are rejected. Brightness-mode and beep changes require the cover to be stationary.




## What to expect

### Traffic while connected

Repeated G/J queries read position and light level; S queries read controller status. Periodic read traffic while a client remains connected is normal. Disconnect both devices and close setup to release the connection.

### A control becomes gray or times out

Allow an active movement or halt confirmation to finish. If the problem persists, disconnect both devices, close setup, and check the COM port and driver log. Do not repeatedly issue movement commands when status is uncertain.

### Settings and updates

Settings are stored per Windows user in `%LOCALAPPDATA%\CCDASTRO\GeminiFlatPanel`. Installation and removal retain these settings and do not send hardware configuration commands. This package includes no machine-specific calibration files.

### Firmware updates

Existing commands may remain compatible after a firmware update, but compatibility is not guaranteed. Even without new features, firmware can change reply formats, status meanings, endpoint readings, or halt timing. The behavior validated for this prototype used firmware V107; other versions require verification.

Before updating, record the working firmware version and calibration settings, retain a copy of the driver settings, and follow the update instructions from the manufacturer. After updating, reconnect and arrange supervised checks of cover status, opening and closing, halt, light brightness, and Switch controls before unattended use. Keep beep enabled if that is your preference; do not toggle it off merely as part of a routine check.

The driver compares firmware identification and controller limits with the verified baseline before opening or closing. A detected mismatch blocks movement, enables the motion lock, and clears locally remembered endpoint positions. Controller-limit fallback for endpoint recognition also requires a matching baseline. These checks cannot detect every possible firmware behavior change and do not replace physical validation.

If movement is blocked after an update, inspect and revalidate calibration and compatibility before re-establishing the verified baseline through supervised setup. Do not simply bypass the lock. A driver update may be needed if controller behavior has changed.

### Validation of this prototype

Version 0.11 passed 201 simulated assertions and the Switch Conform property, method, and write tests, including fractional values, with no reported errors or issues. CoverCalibrator property and method tests passed on version 0.10; version 0.11 adds Switch rounding and version metadata changes. Performance stress tests were disabled. These results are development validation, not a claim of ASCOM certification.





## Build from source

Requires Windows, the .NET SDK/.NET Framework 4.8 targeting pack, and ASCOM Developer Components.

```powershell
dotnet build ASCOM.CCDASTRO.GeminiFlatPanel.sln -c Release
& '.\tests\GeminiFlatPanel.Tests\bin\Release\net48\GeminiFlatPanel.Tests.exe'
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' '.\installer\GeminiFlatPanel.iss'
```

The installer is written to `dist`. See [installer details](installer/README.md). The local server provides both 32-bit and 64-bit COM registration from one shared process.

The v0.11 installer was successfully installed by the user and NINA displayed v0.11. Uninstall and upgrade paths have not yet been separately validated. The installer is unsigned.

