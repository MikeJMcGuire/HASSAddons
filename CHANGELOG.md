# Changelog
All notable changes to this project will be documented in this file.

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
