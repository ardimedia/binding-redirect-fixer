# Changelog

All notable changes to the Binding Redirect Fixer extension will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.3] - 2026-03-15

### Added

- GitHub Actions workflow for automated build and publish to VS Marketplace

### Fixed

- Publish workflow: use VsixPublisherAction to avoid .NET assembly conflicts on CI runners
- Publish manifest: remove invalid categories (debugging, testing)

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
