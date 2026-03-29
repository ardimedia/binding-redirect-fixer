---
status: Stable
updated: 2026-03-26 22:30h
globs: src/BindingRedirectFixer/**
---

# README Sync Rule

When adding, removing, or renaming issue types (RedirectStatus enum), features, or fix actions:

1. Update `README.md` to reflect the change:
   - **Issue Types table** — must list every RedirectStatus with correct meaning and auto-fix
   - **Features section** — must mention all user-visible capabilities
2. Update `CHANGELOG.md` in the next release

Checklist before publishing a release:
- [ ] All `RedirectStatus` enum values are documented in README > Issue Types
- [ ] All `FixAction` enum values are reflected in the Issue Types auto-fix column
- [ ] New user-visible features are listed in README > Features
