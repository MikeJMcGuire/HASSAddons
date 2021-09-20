# Changelog
All notable changes to this project will be documented in this file.

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

### Updated
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
