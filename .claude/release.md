---
status: Stable
updated: 2026-03-14 23:40h
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

## 3. Commit the version bump

```bash
git add src/BindingRedirectFixer/BindingRedirectFixer.csproj
git commit -m "bump version to {new_version}"
```

## 4. Create a git tag

```bash
git tag v{new_version}
```

## 5. Push commit and tag

```bash
git push && git push --tags
```

This triggers the GitHub Actions workflow (`.github/workflows/release.yml`) which builds the VSIX and publishes it to the Visual Studio Marketplace.

## 6. Verify

After pushing, tell the user to check the workflow run at:
`https://github.com/ardimedia/binding-redirect-fixer/actions`

## Notes

- The tag **must** start with `v` (e.g. `v0.2.0`) to trigger the publish job
- The version in the csproj must match the tag (without the `v` prefix)
- Do NOT amend previous commits — always create a new commit for the version bump
- Required GitHub secret: `VS_MARKETPLACE_PAT` (Azure DevOps PAT with Marketplace > Manage scope)
