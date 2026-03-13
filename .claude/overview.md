# Binding Redirect Fixer

Automatically detects and repairs stale or missing assembly binding redirects in .NET Framework projects.

## The Problem

After NuGet updates or partial deployments, binding redirects in `web.config` / `app.config` often reference wrong assembly versions, causing runtime errors like:

> Could not load file or assembly 'Newtonsoft.Json, Version=12.0.0.0, ...' manifest definition does not match the assembly reference.

Tracking down which redirects are wrong and what versions they should point to is tedious and error-prone. **This extension automates the entire process.**

## Features

- **Automatic Detection** -- scans all projects in your solution for stale, missing, or conflicting binding redirects
- **Multi-Source Version Resolution** -- cross-references four independent sources to pinpoint exactly where versions diverge:
  1. NuGet resolved DLL (authoritative)
  2. Package reference version (cross-check)
  3. Physical DLL in `bin/` (what is deployed)
  4. Config redirect (what the runtime uses)
- **One-Click Fix** -- update stale redirects, add missing ones, or rebuild projects with conflicting bin/ output
- **Fix All** -- batch-fix all detected issues in a single click
- **Supports Both Project Types** -- works with PackageReference and `packages.config` projects
- **Educational UI** -- a built-in Learn tab explains what binding redirects are, why they break, and how this tool resolves them
- **Theme-Aware** -- fully adapts to Light, Dark, Blue, and High Contrast themes
- **Non-Destructive** -- creates timestamped backups before modifying any config file

## Usage

1. Open a solution containing .NET Framework projects
2. Go to **Tools** > **Binding Redirect Fixer**
3. The tool window opens and automatically scans your solution
4. Review the detected issues in the multi-source grid
5. Click **Fix All** to resolve all issues, or fix them individually

## How It Works

The extension reads assembly versions from multiple sources and compares them:

| Source | What It Represents | Trust Level |
|---|---|---|
| NuGet Resolved DLL | The actual DLL from the NuGet cache | Authoritative |
| Package Reference | What was requested in `.csproj` / `packages.config` | Cross-check |
| bin/ DLL | What is physically on disk | Can be stale |
| Config Redirect | What the runtime currently uses | Often wrong |

## Issue Types

| Status | Meaning | Auto-Fix |
|---|---|---|
| **STALE** | Config redirect points to an old assembly version | Updates `newVersion` |
| **MISSING** | Redirect needed but does not exist | Adds `dependentAssembly` element |
| **CONFLICT** | Config is correct but bin/ DLL is outdated | Triggers clean rebuild |
| **DUPLICATE** | Multiple redirects for the same assembly | Removes duplicate |
| **MISMATCH** | Resolved and physical versions disagree | Flags for review |

## Requirements

- Visual Studio 2022 (17.14+) or Visual Studio 2026
- .NET Framework projects with `web.config` or `app.config`
- NuGet packages restored

## Links

- [Source Code](https://github.com/ardimedia/binding-redirect-fixer)
- [Report Issues](https://github.com/ardimedia/binding-redirect-fixer/issues)
- [License (MIT)](https://github.com/ardimedia/binding-redirect-fixer/blob/main/LICENSE)
