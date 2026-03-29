---
status: Stable
updated: 2026-03-26 22:30h
references:
  - .claude/deploy.md — Marketplace publishing details
  - .claude/enhancements.md — Signing roadmap
  - D:\CODE\.claude\code-signing.md — Ardimedia code signing strategy
---

# Release Process

When the user asks to publish, release, or bump the version, follow these steps.

## 1. Determine version bump

Ask the user which version bump to apply if not specified:

- **patch** (0.1.0 → 0.1.1) — bug fixes
- **minor** (0.1.0 → 0.2.0) — new features, backwards compatible
- **major** (0.1.0 → 1.0.0) — breaking changes

## 2. Bump the version

Edit the `<Version>` property in `src/BindingRedirectFixer/BindingRedirectFixer.csproj`.

## 3. Verify README.md is up-to-date

Before bumping the version, check that `README.md` reflects the current state:

- All `RedirectStatus` enum values are in the **Issue Types** table
- All user-visible features are in the **Features** section
- No removed features are still listed

See `.claude/rules/readme-sync.md` for the full checklist.

## 4. Update CHANGELOG.md

Add a new section at the top of `CHANGELOG.md` (below the header), following the [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format:

```markdown
## [{new_version}] - {YYYY-MM-DD}

### Added / Changed / Fixed / Removed
- Description of changes
```

Ask the user what changed, or review recent commits since the last tag to summarize changes.
Use the appropriate subsections: **Added** for new features, **Changed** for modifications,
**Fixed** for bug fixes, **Removed** for removed features.

## 5. Commit the version bump

```bash
git add src/BindingRedirectFixer/BindingRedirectFixer.csproj CHANGELOG.md README.md
git commit -m "bump version to {new_version}"
```

## 6. Create a git tag

```bash
git tag v{new_version}
```

## 7. Push commit and tag

```bash
git push && git push --tags
```

This triggers the GitHub Actions workflow (`.github/workflows/release.yml`) which builds the VSIX and publishes it to the Visual Studio Marketplace.

## 8. Verify

After pushing, tell the user to check the workflow run at:
`https://github.com/ardimedia/binding-redirect-fixer/actions`

## Notes

- The tag **must** start with `v` (e.g. `v0.2.0`) to trigger the publish job
- The version in the csproj must match the tag (without the `v` prefix)
- Do NOT amend previous commits — always create a new commit for the version bump
- Required GitHub secret: `VS_MARKETPLACE_PAT` (Azure DevOps PAT with Marketplace > Manage scope)
