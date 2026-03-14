---
status: Draft
updated: 2026-03-14 23:20h
references:
  - D:\CODE\.claude\code-signing.md — Ardimedia code signing strategy and Azure Trusted Signing setup
---

# Enhancements

## VSIX Signing

The VSIX package must be signed before publishing to the Visual Studio Marketplace.

### Current status

- First publish will be **unsigned** (no Sectigo signing — certificate expires June 2026 and will not be used for new projects)
- Signing will be added once **Azure Trusted Signing** is set up (target: May 2026)

### After Azure Trusted Signing is set up (target: May 2026)

- Add a GitHub Actions release workflow that:
  1. Builds the VSIX (`dotnet build -c Release`)
  2. Signs it via `azure/trusted-signing-action`
  3. Publishes to the VS Marketplace via `VsixPublisher`
- See `D:\CODE\.claude\code-signing.md` for full setup details
