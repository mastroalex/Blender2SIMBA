# Changelog

All notable changes to SIMBA are documented here.

## [1.0.1] - 2026-08-03

### Fixed

- Resolved the `PackageInfo` ambiguity on Unity versions exposing both Editor types.
- Made the Import Simulation scroll view exception-safe to prevent unbalanced IMGUI layout and GUIClip errors.
- Added explicit package-manager type aliases in Python tool path resolution.

## [1.0.0] - 2026-08-03

### Added

- Unified public `SIMBAPlayer` component and API.
- Runtime playback, field, colormap, range and appearance controls.
- Runtime events for loading, frame, field, colormap and playback completion.
- Custom `SIMBAPlayer` Inspector with Play Mode controls.
- `SIMBAUtilities` screenshot helper.
- Editor menu entries for default material, screenshots, documentation and About.
- About window and replaceable placeholder icons.
- Expanded API and release documentation.

## [0.4.0] - 2026-08-03

- Dynamic field dropdown in the FieldColorController Inspector.
- Complete HDF5, Python, API and binary-format documentation.
- Conda `environment.yml` and pip `requirements.txt`.

## [0.3.0] - 2026-08-03

- Direct HDF5 import from Unity.
- Persistent Python/Conda interpreter settings.

## [0.2.0] - 2026-08-03

- SIMBA Import Simulation Editor window.
- GeometryType in binary format.

## [0.1.0] - 2026-08-03

- Initial UPM package.
