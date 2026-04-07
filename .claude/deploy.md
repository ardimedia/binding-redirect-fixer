---
status: Stable
updated: 2026-03-13 22:30h
---

# Publishing to the Visual Studio Marketplace

This extension uses the **VisualStudio.Extensibility SDK** (out-of-proc model) and requires **VS 2022 17.9+**.

## 1. Create a Publisher Account

- Go to the [Visual Studio Marketplace Publishing Portal](https://marketplace.visualstudio.com/manage)
- Sign in with a Microsoft account
- Create a **publisher** (e.g., `Ardimedia`) — this is your publisher ID

## 2. Create a Personal Access Token (PAT)

- Go to `dev.azure.com` > User Settings > Personal Access Tokens
- Create a token with scope **Marketplace > Manage**
- Save the token securely — you will need it for publishing

## 3. Build the VSIX

```bash
dotnet build -c Release
```

This produces a `.vsix` file in the output directory (`bin/Release/`).

## 4. Publish

### Option A: Manual Upload

- Go to [marketplace.visualstudio.com/manage](https://marketplace.visualstudio.com/manage)
- Click **New Extension** > **Visual Studio**
- Upload the `.vsix` file

### Option B: CLI with VsixPublisher (for CI/CD)

```bash
VsixPublisher.exe publish \
  -payload "path/to/extension.vsix" \
  -publishManifest "publishManifest.json" \
  -personalAccessToken "<PAT>"
```

For CLI publishing, create a `publishManifest.json` in the repo root:

```json
{
  "$schema": "http://json.schemastore.org/vsix-publish",
  "categories": ["coding"],
  "identity": {
    "internalName": "binding-redirect-fixer"
  },
  "overview": "README.md",
  "pricing": "Free",
  "publisher": "Ardimedia",
  "repo": "https://github.com/ardimedia-com/visualstudio-binding-redirect-fixer"
}
```

## 5. Required Metadata

Ensure the extension includes:

- **README.md** — used as the marketplace description page
- **Icon** (128x128 PNG recommended) — set via `<Icon>` in the `.csproj`
- **LICENSE** file
- Version, description, and tags in the `.csproj` (already configured)

## 6. Updating an Existing Extension

To publish an update:

1. Bump the `<Version>` in `binding-redirect-fixer.csproj`
2. Update `CHANGELOG.md`
3. Rebuild: `dotnet build -c Release`
4. Upload the new `.vsix` via the portal or CLI
