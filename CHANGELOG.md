# Changelog

All notable changes to the Binding Redirect Fixer extension will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-03-30

### Added

- **ORPHANED .NET (Core)** status: no DLL found in a .NET (Core) project, binding redirect is orphaned and safe to remove (green)
- **ORPHANED .NET Framework** status: no DLL found in a .NET Framework project, likely orphaned but verify GAC/post-build (amber)
- Remove Redirect action for DEPRECATED items with warning to check NuGet references first
- Remove Redirect action for ORPHANED items (both .NET and .NET Framework)
- Framework detection from `.csproj` (reads `TargetFramework`, `TargetFrameworks`, `TargetFrameworkVersion`)
- Framework-aware detail panel warnings: green for .NET (Core), amber for .NET Framework
- CONFLICT, DEPRECATED, ORPHANED .NET (Core), ORPHANED .NET Framework cards in Background tab
- Status filter entries for "Orphaned .NET (Core)" and "Orphaned .NET Framework" for targeted batch fixes
- Test project with 55 unit tests (EvaluateStatus rules, ConfigPatcher, DeprecatedPackageRegistry, DetectNetFramework)

### Changed

- TOKEN LOST now only applies when DLL is present but unsigned (redirect still needed); no-DLL cases are now ORPHANED
- Background tab cards sorted by severity (red > amber > green > blue), then alphabetically
- Info bar updated to mention ORPHANED status

### Fixed

- Empty Analyse button text after cancellation (was binding to a removed property)
- Column header click area: sorting now works on the full header cell, not just the text label

## [0.1.7] - 2026-03-25

### Added

- Resizable columns: switched from fixed-width Grid layout to native WPF GridView with drag-to-resize column headers
- Sortable columns: click any column header to sort ascending/descending, with sort indicator (▲/▼)
- Horizontal scrollbar for overflow when columns exceed available width

## [0.1.6] - 2026-03-25

### Added

- Deprecated package detection: flags packages like `Microsoft.Azure.Services.AppAuthentication` with migration guidance instead of fixing their binding redirects
- Built-in registry of deprecated Azure SDK packages with replacement recommendations and migration URLs
- New DEPRECATED status in the Issues grid with filtering support

## [0.1.5] - 2026-03-19

### Added

- TokenLost status detection for binding redirect token loss scenarios

## [0.1.4] - 2026-03-15

### Changed

- Fix minor UI enhancements

## [0.1.0] - 2026-03-13

### Added

- Initial release
- Scan command via Tools menu to detect binding redirect issues
- Multi-source version resolution (NuGet resolved, package reference, bin/ DLL, config redirect)
- Support for PackageReference and packages.config projects
- Issue detection: STALE, MISSING, CONFLICT, DUPLICATE, MISMATCH statuses
- One-click fix for individual issues
- Fix All to batch-resolve all detected issues
- Timestamped config file backups before modifications
- Detail panel with educational "What happened?" explanations
- Learn tab with binding redirect documentation
- Project and status filtering
- Assembly name search
- Theme-aware UI (Light, Dark, Blue, High Contrast)
- Persistent user settings (backup preference, panel layout)
