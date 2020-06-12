# Changelog
All notable changes to this project will be documented in this file.

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
