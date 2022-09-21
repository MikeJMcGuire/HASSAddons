# Changelog
All notable changes to this project will be documented in this file.

## [v2022.9.1] - 2022-09-21

### Changed

- Updated dotnet sdk.

## [v2022.9.0] - 2022-09-16

### Added
- Added configuration option to limit MQTT logging.

### Changed
- Updated package versions.

## [v2022.5.0] - 2022-05-30

### Added
- Added sensors for ESP and Continuous Fan.

## [v2022.2.1] - 2022-02-23

### Changed
- Fixed issue with zone switches when using multiple AC units.

## [v2022.2.0] - 2022-02-22

### Added
- Added support for multiple ActronConnect units.

### Changed
- Changed MQTT topics to include a unit identifier. This should not impact your MQTT device, unless you're directly sending MQTT commands.

## [v2021.12.0] - 2021-12-01

### Added
- Additional logic added to the suppression code that suppresses old data received from the AC after a change has been made. This should result in faster updates in HA after changing the AC settings.

### Changed
- Updated package versions.
- Migrated from .NET 5.0 to .NET 6.0.

## [v2021.10.1] - 2021-10-10

### Added
- Updated logging to highlight when the Actron Connect has stopped communicating with the add-on.

## [v2021.10.0] - 2021-10-01

### Changed
- Updated package versions.
- Updated version number scheme.

## [v0.98] - 2021-06-05

### Changed
- Updated package versions.
- Support for MQTT TLS (with associated configuration option).
- Support for MQTT port specification (through host:port notation).

## [v0.97] - 2021-02-13

### Changed
- Updated package versions.

## [v0.96] - 2021-02-10

### Changed
- Updated add-on configuration based on schema changes from HA. This should remove the warning appearing in the supervisor logs.

## [v0.95] - 2020-10-18

### Changed
- Updated to .NET 5.0 Framework.
- Added registration pass-through to the cloud service. When the client app is used to reconfigure the AC, the add-on will pass the registration request to the cloud service to receive an API Token, and then continue to function as before. This allows for using the App to reconfigure Wi-Fi with the custom DNS entries in place.
- Updated Device Registration MQTT information.

## [v0.94] - 2020-06-12

### Added
- The ForwardToOriginalWebService option has been added enabling data from the Actron to be forwarded to the original web service. This will enable the Actron phone app to see (but not control) the state of the air conditioner.

## [v0.93] - 2020-06-09

### Added
- MQTT entities will now appear linked to a single device under the MQTT integration.

### Changed
- Fix automatic mode for the Actron is now working.

## [v0.91] 

### Added
- MQTT devices will now appear online/offline depending on the connection status of the Actron Connect (i.e. if the Actron Connect is not sending data to the add-on, the MQTT devices will appear as unavailable).

## [v0.9]

### Added
- Command changes are reflected back to HA immediately to prevent the appearance of changes not taking effect.
- Compressor state reflected in HA climate entity.

## [v0.7]

### Added
- Introduced a short delay to suppress incoming data from the actron connect after sending it new commands, as the actron connect has some delay between accepting a command, and reflecting that in updates that it sends back to HA.

## [v0.6]

### Added
- Supports newer firmware versions of the Actron Connect that use HTTP long polling.

## [v0.5]

### Added
- For air conditioners with per-zone temperatures, set 'RegisterZoneTemperatures' to true in the options.json (add-on options). This will create per-zone sensor elements in Home Assistant.
