# Changelog
All notable changes to this project will be documented in this file.

## [v2022.10.0] - 2022-10-15

### Added

- Added logic to regenerate the pairing token and device identication files when the email address changes.
- Added MQTT connection errors to the logs.

## [v2022.8.0] - 2022-08-09

### Added

- Added additional HA attributes to battery entities.
- Added battery entities to PerZoneControls (already in PerZoneSensors).

### Changed

- Fix for Neo Systems that don't generate Actron Events.

## [v2022.5.0] - 2022-05-30

### Changed

- Added additional logic to support non-contiguous zones.

## [v2022.2.3] - 2022-02-09

### Changed

- Rounded per zone temperature to one decimal place.
- Aligned per sensor temperature entities' units to climate entities.

## [v2022.2.2] - 2022-02-08

### Changed

- Fixed per sensor temperature entity being incorrectly shown as a percentage.

## [v2022.2.1] - 2022-02-07

### Added

- Added support for zones with multiple sensors.

### Changed

- Changed per zone battery to per sensor battery to better reflect the data model from Actron. Use the PerZoneSensors option to create the entities in HA.

### Removed

- Removed per zone humidity sensors - the zone sensors don't measure humidity, even though they sometimes report a value.

## [v2022.2.0] - 2022-02-01

### Changed

- Fix for Neo systems.
- Update to MQTT library.

## [v2022.1.4] - 2022-01-29

### Changed

- Fix for systems with less than 8 zones.

## [v2022.1.3] - 2022-01-28

### Changed

- Code efficiency updates.
- Reduction in MQTT updates - updating changed items rather than all items.

## [v2022.1.2] - 2022-01-27

### Added

- Added entities for room/zone humidity and battery.

## [v2022.1.1] - 2022-01-25

### Added

- Added entities for Coil Inlet, Fan PWM, Fan RPM.

## [v2022.1.0] - 2022-01-17

### Changed
- Check for missing outdoor unit.

## [v2021.12.2] - 2021-12-10

### Changed
- Fixed number conversion error for compressor capacity and compressor power.

## [v2021.12.1] - 2021-12-07

### Added
- Added logic to support Neo Air Conditioners that do not record change events. In this mode, the add-on will pull a full status update every 30 seconds as there aren't incremental updates available.

## [v2021.12.0] - 2021-12-02

### Changed
- Migrated from .NET 5.0 to .NET 6.0.
- Changed version numbering scheme.
- Added logic to support the bearer token request returning BadRequest instead of Unauthorized, so that after a set number of attempts, the add-on will regenerate the pairing token.

## [v0.30] - 2021-09-22

### Changed
- New entities for power, outdoor temperature and compressor capacity removed in Neo mode.

## [v0.29] - 2021-09-20

### Added
- Setting the AC to auto mode will now set the heating and cooling temperatures to the single desired temperature.

## [v0.28] - 2021-09-04

### Added
- The compressor power usage reading from the outdoor is now presented as an entity. This is an experimental entity until more data is gathered.

## [v0.27] - 2021-08-30

### Added
- The outside temperature reading from the Master Controller is now presented as an entity.
- The compressor capacity reading from the Master Controller is now presented as an entity.
- The zone position reading from each zone is now presented as an entity.

### Changed
- Zones that are off will now receive set temperature updates.

## [v0.26] - 2021-08-01

### Changed
- Decreased logging from .NET Framework.
- Updated humidity entity to better support HomeKit.

## [v0.25] - 2021-07-31

### Added
- The humidity reading from the Master Controller is now presented as an entity.

## [v0.24] - 2021-05-28

### Added
- Support for MQTT TLS (with associated configuration option).
- Support for MQTT port specification (through host:port notation).

### Changed
- Moved to .NET 5.0 host model.

## [v0.23] - 2021-02-14

### Added
- Support for fan continuous mode. If you change speed whilst in continuous mode, continuous mode will be preserved. 

## [v0.22] - 2021-02-13

### Changed
- Fixed a bug with temperature updates for the Neo system.

## [v0.21] - 2021-02-12

### Added
- Initial support for the Actron Neo module.
- New configuration item SystemType for specifying que or neo system types - defaulting to que for backward compatibility.

## [v0.20] - 2021-02-10

### Changed
- Updated add-on configuration based on schema changes from HA. This should remove the warning appearing in the supervisor logs.

## [v0.19] - 2020-12-17

### Changed
- Updated to .NET 5.0 Framework.
- Updated Device Registration MQTT information.

## [v0.18] - 2020-08-24

### Added
- MQTT entities will now appear linked to a single device under the MQTT integration.

### Changed
- The add-on now correctly displays all Que serial numbers when a user has multiple Que units on their account.
- Improved logging for errors from bad data returned by Que API (in circumstances where a user has an old Que unit on their account with no data).

## [v0.17] - 2020-02-08

First major release.
