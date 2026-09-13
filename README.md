# CCDASTRO Gemini FlatPanel

ASCOM CoverCalibrator and Switch drivers for the Gemini motorized flat panel. Version **0.16**, with optional SVBONY heater control and corrected COM server shutdown.

[Download Windows installer v0.16](https://github.com/CCDASTRO/ASCOM.CCDASTRO.GeminiFlatPanel/releases/download/v0.16/CCDASTRO.GeminiFlatPanel.Setup-0.16.0.0.exe) · [Offline HTML guide](docs/GeminiFlatPanel-Guide.html)

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
| Dew heater (ID 0) | Legacy Gemini 0–100 command range retained for sequence compatibility. The Gemini 12 V socket has been reported as on/off only; use an appended SVBONY PWM channel for proportional heating. |
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


## SVBONY integration and NINA usage

Install SVBONY's ASCOM Switch driver separately. In Gemini setup, with all Switch clients disconnected, choose **Choose SVBONY driver**, select the SVBONY Switch, and configure its COM port through **Properties**. Keep the Gemini COM port configured for the flat panel. Close the vendor applications before connecting.

In NINA, select **CCDASTRO Gemini FlatPanel** for the Flat Device and **CCDASTRO Gemini Dew Heater** for Switch. Connect once to discover the appended SVBONY controls. Use **Disable external** while disconnected to return to Gemini-only operation.

| Device and control | Usage |
| --- | --- |
| Gemini Flat Device | Open/close the cover, switch the flat light on/off, and set brightness (0–255). |
| Gemini / Dew heater (ID 0) | Native 0–100 command retained for future firmware support; the current socket is reported as on/off only. This does not control SVBONY. |
| Gemini / High brightness (ID 1) | ON selects high mode; OFF selects low mode. Set while the cover is stationary. |
| Gemini / Beep (ID 2) | ON enables beep; OFF disables it. |
| Gemini / Stop cover motion (ID 3) | ON sends halt; OFF resets the indicator without moving the cover. |
| SVBONY / Dew heater 1 / … (%) (ID 18) | PWM1 automatic heater control, normalized to 0–100%. |
| SVBONY / Dew heater 2 / … (%) (ID 19) | PWM2 manual heater control, normalized to 0–100%. |
| Other SVBONY controls | Vendor DC/USB controls, adjustable voltage in volts, and read-only environmental gauges. |

The SVBONY channel IDs above apply to the tested 17-channel vendor driver. Gemini adds 4 to each vendor ID. Device prefixes clearly separate the controls; NINA controls the visual layout. Review names and assignments after vendor updates.

### Example sequence

1. Connect both Gemini devices in NINA.
2. Use a Switch value step for the desired **SVBONY / Dew heater … (%)** channel: for example, **40** requests 40%. Use **0** for off and **100** for maximum. PWM1 retains the vendor's automatic-control behavior; use PWM2 for manual heater control.
3. Use the Flat Device's open-cover operation for imaging, with the flat light off.
4. For flats, close the cover, select Gemini high/low mode if needed, then let NINA's flat workflow set the light brightness. Brightness and heater power are separate settings.
5. After flats, turn the flat light off. Set the SVBONY heater to the desired value or 0, and open or close the cover as your shutdown sequence requires.

Both heater values and readbacks use whole percentages in 1% steps. The bridge converts to the vendor's reported raw maximum (253 in the tested driver). Boolean heater OFF/ON sends 0/100%. Other external channels retain vendor behavior. Existing sequences using raw PWM values must be converted: `round(old value * 100 / vendor maximum)`. For example, 249 becomes 98% with a maximum of 253. Reconnect NINA after upgrading to refresh labels and ranges.

### Connections, upgrades, and validation

Multiple Gemini Switch clients share one vendor connection. The bridge releases it when the last Switch client disconnects, without sending output-setting commands. An independently connected Flat Device remains usable. Native mode and beep indicators show command receipts, not verified readback.

Close all ASCOM clients and setup windows before upgrading. Version 0.16 fixes a Dispose/finalizer bug that left the server running after clients closed. Allow about 10–15 seconds for cleanup. An older lingering server may need to be ended once in Task Manager after all devices are disconnected. Settings and cover calibration are retained.

Validation: 668 simulated assertions passed, including both vendor PWM minima, percentage conversion, shared connection cleanup, and object lifetime. An installed COM client was created, disposed, and released without connecting hardware; the server exited. The user confirmed NINA cover operation, SVBONY controls, and the shutdown fix. Combined-driver ASCOM Conform certification is not claimed.
