# Changelog
All notable changes to this project will be documented in this file.

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
