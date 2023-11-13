# Changelog
All notable changes to this project will be documented in this file.

## [v2023.11.0] - 2023-11-13

### Added

- Added default precision to pH sensor entity.
- Added unit of measurement to the pH sensor entity.

### Changed

- Updated package versions.
- Migrated from .NET 6.0 to .NET 7.0.
- Changed base images to Alpine from Debian to match the HA base images.

### Removed

- Removing support for i386 and armhf architectures as they are no longer supported by Microsoft (.NET). The previous versions of the add-on will continue to work, however they will no longer receive updates.

## [v2022.9.0] - 2022-09-12

### Changed
- Fixed issue with some Blue Connect measurements not being processed due to a conductivity measurement instead of a salinity measurement.
- MQTT state topic names updated.

## [v2022.1.1] - 2022-01-06

### Removed
- Removed Fahrenheit entity - now using HA's automatic converstion on a single temperature entity.

## [v2022.1.0] - 2022-01-06

### Changed
- Added device class to temperature sensor entities.

## [v2021.12.4] - 2021-12-31

### Changed
- Minor performance updates.

## [v2021.12.3] - 2021-12-30

### Added
- MQTT connectivity.

## [v2021.12.2] - 2021-12-30

### Added
- Entities for Celcius and Fahrenheit.

## [v2021.12.1] - 2021-12-30

### Added
- Initial functionality.