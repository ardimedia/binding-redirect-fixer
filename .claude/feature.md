You are an expert .NET/C# developer specializing in Visual Studio VSIX extensions
and NuGet dependency resolution.

Build a Visual Studio 2026 VSIX extension called "BindingRedirectFixer" that
automatically detects and repairs stale or missing assembly binding redirects
in .NET Framework web/app projects (web.config / app.config).

## Core Problem

After NuGet updates or partial deployments, binding redirects in config files
often reference wrong versions, causing runtime errors like:
  "Could not load file or assembly ... manifest definition does not match"

## Important: Package Version vs Assembly Version

  Binding redirects operate on ASSEMBLY versions, not NuGet package versions.
  These are often different numbers:

    Package version:  Newtonsoft.Json 13.0.3
    Assembly version: Newtonsoft.Json 13.0.0.0

  All version comparisons and grid displays in this extension MUST use
  assembly versions. Package versions are shown as secondary context only.

  To obtain the assembly version from a NuGet package:
  1. Locate the DLL via project.assets.json (packageFolders + runtime path)
     or via packages/ folder for packages.config projects
  2. Read assembly version using MetadataLoadContext (preferred, no file lock)
     or AssemblyName.GetAssemblyName() (fallback, briefly locks file)
  3. Also extract PublicKeyToken and Culture from the assembly metadata
  4. Only process assemblies with culture="neutral" (binding redirects for
     satellite assemblies with specific cultures are rare and out of scope for v1)

## Project Types: PackageReference vs packages.config

  .NET Framework projects use two different NuGet management styles.
  The extension MUST support both:

  PackageReference projects (SDK-style or migrated .csproj):
    → obj/project.assets.json exists after NuGet restore
    → Authoritative source: assembly version from NuGet cache DLL
      (path computed from project.assets.json packageFolders + runtime path)
    → Requested source: PackageReference version in .csproj

  packages.config projects (traditional .NET Framework):
    → NO project.assets.json
    → Authoritative source: assembly version from packages/ folder DLL
      (path: packages/{PackageId}.{Version}/lib/{tfm}/{Assembly}.dll)
    → Requested source: package version in packages.config

  Detection: check for packages.config file in project directory first.
  If present → packages.config path. If absent → PackageReference path.
  (Do NOT use project.assets.json existence for detection — an unrestored
  PackageReference project also lacks project.assets.json and would be
  misclassified as packages.config.)

  Service abstraction:
    IVersionResolver interface with two implementations:
    - AssetsJsonVersionResolver (PackageReference projects)
    - PackagesConfigVersionResolver (packages.config projects)

## Version Resolution Logic

To determine the CORRECT assembly version for each assembly, check these
sources in priority order:

  1. Resolved assembly version (authoritative)
     → PackageReference: read DLL from NuGet cache via project.assets.json
     → packages.config: read DLL from packages/ folder
     → Use MetadataLoadContext to avoid file locking
     → This is the ground truth

  2. Requested package version (cross-check)
     → PackageReference: version attribute in .csproj
     → packages.config: version attribute in packages.config
     → May differ from resolved due to version ranges or transitive deps

  3. Physical DLL in bin/ folder
     → Use MetadataLoadContext or AssemblyName.GetAssemblyName(path)
     → Returns AssemblyVersion (correct for binding redirects, not FileVersion)
     → What is actually present on disk

  4. Existing binding redirect in web.config/app.config
     → What the runtime currently uses

  Flag a mismatch when sources disagree.
  The resolved assembly version (source 1) is authoritative.

  Pre-check: if project.assets.json is missing AND project uses PackageReference,
  show warning: "NuGet restore required. Run 'Restore NuGet Packages' first."
  Offer a [Restore NuGet Packages] button that triggers restore via in-proc
  VSSDK bridge (direct restore trigger not available out-of-proc; subscribe
  to IVsNuGetProjectUpdateEvents via NuGet.VisualStudio.Contracts for
  completion notification). Degrade gracefully using remaining sources
  (bin/ DLL + config).

### Why This Priority Order

  Sources closer to NuGet's resolution are more trustworthy;
  sources closer to the runtime are more likely to be stale or manually edited.

  1. Resolved assembly version — read from the actual DLL that NuGet resolved.
     After dependency graph analysis, this is what WILL be used after a correct
     build. This is the single source of truth.

  2. Requested package version — reflects what was REQUESTED, but version
     ranges, floating versions, and transitive dependencies mean the actual
     resolved version may differ. Useful as a cross-check.

  3. Physical DLL in bin/ — shows what is ACTUALLY on disk right now, but
     can be stale after incomplete builds, failed cleans, or manual file
     copies. A mismatch here signals a build/deployment problem, not a
     redirect problem.

  4. Existing binding redirect — this is what the runtime currently uses,
     but it is the very thing that tends to go wrong. It is the SYMPTOM,
     not the AUTHORITY.

  These rationales are surfaced in the UI via column header tooltips
  and a dismissible info bar (see TOOL WINDOW UI, feature 3).

### Visualization: Multi-Source Grid

  The tool window displays ALL 4 sources as columns so the developer
  can see exactly where versions diverge.

  All versions shown are ASSEMBLY versions (not package versions):

  | Project      | Assembly          | Resolved  | Requested | bin/ DLL  | Config    | Status   |
  |--------------|-------------------|-----------|-----------|-----------|-----------|----------|
  | MyWeb        | Newtonsoft.Json    | 13.0.0.0  | 13.0.3    | 13.0.0.0  | 12.0.0.0* | STALE    |
  | MyWeb        | System.Net.Http    | 4.2.0.0   | 4.2.0     | 4.2.0.0   | 4.0.0.0*  | STALE    |
  | MyWeb        | Serilog            | 3.1.0.0   | 3.1.0     | 3.1.0.0   | —         | MISSING  |
  | MyWeb        | System.Memory      | 4.0.1.2   | 4.5.5     | 4.0.1.1*  | 4.0.1.2   | CONFLICT |
  | MyApi        | Newtonsoft.Json    | 13.0.0.0  | 13.0.3    | 13.0.0.0  | 13.0.0.0  | OK       |

  * = cell highlighted red (diverges from authoritative resolved version)

  Note: "Requested" column shows package version (informational context).
  All other version columns show assembly versions.

  Per-cell coloring (theme-aware via VsBrushes — adapts to Light/Dark/Blue/HC):
  - Match: standard VS text color (value matches authoritative version)
  - Diverge: VS warning/error brush (value differs from authoritative version)
  - Unavailable: VS gray text brush + dash (source not available)
  - See "Remote UI Theming & Styling" in Key Implementation Details for specifics

  Detail panel (master-detail layout — grid on top, detail panel below):
  - Shown when a row is selected in the grid
  - Shows the diagnostic message (see Issue Detail Panel below)
  - Shows a contextual action button whose label and behavior match the
    suggested action (e.g., "Update Redirect", "Add Redirect", "Rebuild Project")
  - Uses master-detail layout as the default pattern (safe for Remote UI
    compatibility — does not depend on RowDetailsTemplate support)

### MISSING Detection Algorithm

  An assembly needs a binding redirect when multiple versions of it exist
  in the dependency graph. Detection:

  For PackageReference projects:
    1. Parse project.assets.json targets[framework] section
    2. Collect all runtime DLL references across all packages
    3. Group by assembly name
    4. Flag assemblies that appear with more than one version
    5. For each flagged assembly: if no binding redirect exists in config → MISSING

  For packages.config projects:
    1. Parse packages.config for all package entries
    2. For each package, read its .nuspec to find dependency groups
    3. Identify assemblies required at different versions by different packages
    4. For each such assembly: if no binding redirect exists in config → MISSING

### Issue Detail Panel

  When a row is selected, the detail panel below the grid shows a rich,
  educational explanation. Structure for each status:

  Title → "What happened?" explanation → Version flow → Action button

  STALE detail panel:
  ┌──────────────────────────────────────────────────────────┐
  │ Newtonsoft.Json — STALE                                  │
  │                                                          │
  │ ⚠ What happened?                                        │
  │ Your web.config redirects all versions of this assembly  │
  │ to {configVer}, but NuGet resolved version {resolvedVer}.│
  │ This typically happens after a NuGet package update —    │
  │ the package was upgraded but the binding redirect in     │
  │ web.config was not updated to match.                     │
  │                                                          │
  │ Version flow:                                            │
  │   NuGet resolved: {resolvedVer} ✓  (authoritative)      │
  │   bin/ DLL:       {physicalVer} ✓                        │
  │   web.config:     {configVer}   ✗ ← redirect outdated   │
  │                                                          │
  │ The fix updates newVersion to {resolvedVer} and sets     │
  │ oldVersion to 0.0.0.0-{resolvedVer} so all prior        │
  │ versions are redirected.                                 │
  │                                                          │
  │              [Update Redirect]                           │
  └──────────────────────────────────────────────────────────┘
  Button: patches newVersion in config, sets oldVersion to "0.0.0.0-{resolvedVer}".
  Note: v1 always uses "0.0.0.0" as the lower bound for simplicity. Custom narrower
  ranges (if any existed) are overwritten. This is the standard MSBuild/NuGet behavior.

  MISSING detail panel:
  ┌──────────────────────────────────────────────────────────┐
  │ Serilog — MISSING                                        │
  │                                                          │
  │ ⚠ What happened?                                        │
  │ Multiple packages in your project depend on different    │
  │ versions of this assembly, but there is no binding       │
  │ redirect in web.config to tell .NET which version to     │
  │ use. Without a redirect, the runtime will fail when      │
  │ it encounters a version mismatch at load time.           │
  │                                                          │
  │ Version flow:                                            │
  │   NuGet resolved: {resolvedVer} ✓  (authoritative)      │
  │   bin/ DLL:       {physicalVer} ✓                        │
  │   web.config:     — (no redirect exists)                 │
  │                                                          │
  │ The fix adds a new <dependentAssembly> element with:     │
  │   assemblyIdentity: {name}, {publicKeyToken}, {culture}  │
  │   bindingRedirect:  0.0.0.0-{resolvedVer} → {resolvedVer}│
  │                                                          │
  │              [Add Redirect]                              │
  └──────────────────────────────────────────────────────────┘
  Button: inserts new binding redirect element with full assemblyIdentity

  CONFLICT detail panel:
  ┌──────────────────────────────────────────────────────────┐
  │ System.Memory — CONFLICT                                 │
  │                                                          │
  │ ⚠ What happened?                                        │
  │ The binding redirect in web.config is correct, but the   │
  │ physical DLL in your bin/ folder is an older version.    │
  │ This usually means the project needs a clean rebuild —   │
  │ a previous build left a stale DLL, or the bin/ folder    │
  │ was not fully cleaned before the last build.             │
  │                                                          │
  │ Version flow:                                            │
  │   NuGet resolved: {resolvedVer} ✓  (authoritative)      │
  │   bin/ DLL:       {physicalVer} ✗ ← stale on disk       │
  │   web.config:     {configVer}   ✓                        │
  │                                                          │
  │ The fix triggers a Clean + Build to replace the stale    │
  │ DLL with the correct version from the NuGet cache.       │
  │                                                          │
  │              [Rebuild Project]                           │
  └──────────────────────────────────────────────────────────┘
  Button: triggers Clean + Build via in-proc VSSDK bridge (IVsUIShell.PostExecCommand).
  After build completes (detected via IVsUpdateSolutionEvents2 from feature 4),
  automatically re-scans the affected project to verify the CONFLICT is resolved.

  OK: no detail panel content — all sources agree.

### Status Evaluation (Decision Tree)

  Evaluate in this exact order (first match wins):

  1. MISSING: no binding redirect exists in config AND the assembly has
     version conflicts in the dependency tree (see MISSING Detection)
  2. STALE: CurrentRedirectVersion != ResolvedAssemblyVersion
     → If PhysicalVersion also disagrees, append to DiagnosticMessage:
       "Note: bin/ DLL is also out of date ({physicalVer}). Rebuild after fixing."
  3. CONFLICT: PhysicalVersion != ResolvedAssemblyVersion
     (but config redirect IS correct)
  4. OK: all sources agree

## Features to Implement

  1. SCAN COMMAND
     - Menu item: Tools → Fix Binding Redirects
     - Scans all projects in the solution that have a web.config or app.config
     - Detects project type (PackageReference vs packages.config) per project
     - For each dependent assembly in config, resolve the correct assembly version
     - Report: project | assembly name | resolved | requested | bin/ | config | status
     - Status: OK / STALE / MISSING / CONFLICT

  2. FIX COMMAND
     - "Fix All" button in the tool window
     - Updates newVersion to resolved assembly version
     - Sets oldVersion range to "0.0.0.0-{resolvedAssemblyVersion}"
     - Adds missing redirects with full assemblyIdentity (name, publicKeyToken,
       culture) extracted from assembly metadata
     - Creates timestamped backup before modifying: web.config.{yyyy-MM-dd-HHmm}.bak
     - For source-controlled files (TFVC): calls IVsQueryEditQuerySave2.QueryEditFiles
       via in-proc VSSDK bridge (not available out-of-proc) to trigger checkout
       before modification. Shows error if checkout fails.

  3. TOOL WINDOW UI

     Window behavior:
     - Standard VS dockable tool window (like Error List, Solution Explorer)
     - Supports dock, float, auto-hide, tab alongside other panels
     - Default placement: bottom dock (next to Error List, Output, Terminal)
     - Title: "Binding Redirect Fixer"
     - Opened via: Tools → Fix Binding Redirects (same as Scan Command)

     Tab layout: [Issues] [Learn]

     ISSUES TAB (default):
     - Project dropdown filter at top, default selection: "All Projects"
     - Dismissible info bar below filter:
       "Version priority: Resolved (authoritative) > Requested > bin/ DLL
        > config redirect. Columns left to right = most to least trustworthy."
       → remember dismiss preference via VS settings store
       → show on first use or after extension update
     - Multi-source list (ListView + DataTemplate with Grid columns):
       Project | Assembly | Resolved | Requested | bin/ DLL | Config | Status
     - Per-cell color (theme-aware via VsBrushes, see Key Implementation Details):
       OK = standard text, diverging = warning/error brush, unavailable = gray text
     - Row-level status icon: ✓=OK, ⚠=STALE, ✗=MISSING/CONFLICT
     - Issue detail panel below the grid (master-detail layout):
       shows rich "What happened?" explanation + version flow + action button
       (see "Issue Detail Panel" section for full content per status)
     - Contextual action button per row (label matches diagnostic:
       Update Redirect / Add Redirect / Rebuild Project)
     - Fix All button: auto-applies STALE + MISSING fixes, then prompts per
       CONFLICT: "Rebuild {ProjectName}?"
     - Refresh button to re-scan
     - Column header tooltips (see "Why This Priority Order" for rationale):
       → Resolved: "Assembly Version from NuGet Cache (Authoritative) — Read from
          the actual DLL that NuGet's dependency resolver selected.
          Single source of truth."
       → Requested: "Package Version — Declared in PackageReference or
          packages.config. May differ from resolved due to version ranges
          or transitive dependencies. Informational only."
       → bin/ DLL: "Physical DLL on Disk — Can be stale after incomplete
          builds or failed cleans. A mismatch signals a build problem,
          not a redirect problem."
       → Config: "Current Binding Redirect — What the runtime uses now.
          This is the symptom, not the authority."

     LEARN TAB:
     Educational content that helps developers understand binding redirect
     issues. Static content rendered in a scrollable panel.

     Section 1 — "What are Binding Redirects?"
       When .NET Framework loads an assembly, it looks for an exact version
       match. If PackageA depends on Newtonsoft.Json 12.0.0.0 and PackageB
       depends on 13.0.0.0, the runtime cannot satisfy both — it fails with:
         "Could not load file or assembly ... manifest definition does not match"

       A binding redirect tells .NET: "whenever any code asks for versions
       0.0.0.0 through 13.0.0.0, give them 13.0.0.0 instead."

       This is configured in web.config or app.config:
         <dependentAssembly>
           <assemblyIdentity name="Newtonsoft.Json"
             publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
           <bindingRedirect oldVersion="0.0.0.0-13.0.0.0"
             newVersion="13.0.0.0" />
         </dependentAssembly>

     Section 2 — "Package Version vs Assembly Version"
       NuGet packages have a package version (e.g., Newtonsoft.Json 13.0.3).
       But the DLL inside the package has a separate assembly version
       (e.g., 13.0.0.0). Binding redirects use ASSEMBLY versions, not
       package versions. These numbers are set by the library author and
       often differ:

         Package version:  13.0.3   (changes with every release)
         Assembly version: 13.0.0.0 (often kept stable across minor releases)

       This tool resolves the actual assembly version by reading the DLL
       from the NuGet cache, not by using the package version number.

     Section 3 — "Common Scenarios" (clickable cards)

       [STALE card]
       After NuGet Update
       You ran "Update-Package" or edited a PackageReference version.
       NuGet updated the DLL in your packages, but the binding redirect
       in web.config still points to the old assembly version.
       → The tool detects this and offers [Update Redirect]

       [MISSING card]
       New Transitive Dependency
       A package you installed depends on another package that is also
       used by a different package at a different version. There is no
       binding redirect to resolve the conflict.
       → The tool detects this and offers [Add Redirect]

       [CONFLICT card]
       Stale Build Output
       The binding redirect is correct, but the DLL in bin/ is an older
       version — usually from a previous build that was not cleaned.
       The runtime will load the wrong DLL even though the redirect
       points to the right version.
       → The tool detects this and offers [Rebuild Project]

     Section 4 — "How This Tool Resolves Versions"
       Visual priority chain:

         ┌─────────────────────────┐
         │ 1. NuGet Resolved DLL   │ ← Authority (assembly version
         │    (cache / packages/)  │    read from actual DLL)
         └─────────┬───────────────┘
                   │ cross-check
         ┌─────────▼───────────────┐
         │ 2. Package Reference    │ ← What was requested
         │    (.csproj / pkgs.cfg) │    (package version)
         └─────────┬───────────────┘
                   │ verify on disk
         ┌─────────▼───────────────┐
         │ 3. bin/ DLL             │ ← What is deployed
         │    (assembly version)   │    (can be stale)
         └─────────┬───────────────┘
                   │ compare with
         ┌─────────▼───────────────┐
         │ 4. Config Redirect      │ ← What runtime uses
         │    (web/app.config)     │    (often wrong)
         └─────────────────────────┘

       When any of these disagree, the tool flags it and tells you
       exactly which source is wrong and what to do about it.

  4. BUILD EVENT INTEGRATION
     - Subscribe to build finished event via in-proc VSSDK bridge
       (IVsUpdateSolutionEvents2 — not available out-of-proc, requires
       VisualStudio.Extensibility.Contracts bridge to access classic VSSDK service)
     - Reuses the existing BindingRedirectAnalyzer — same scan logic, different trigger
     - Emit warnings (not errors) to VS Error List via GetDiagnosticsReporter()
       (this IS available out-of-proc — no bridge needed for diagnostics output)
     - Warning code: BRF001, category: "Binding Redirect"
     - Clickable navigation from Error List to the tool window
     - Performance: cache previous scan results, only re-scan changed projects
     - User setting: enable/disable post-build check (default: enabled)
     - Note: this is a lightweight addition — the core analysis engine is shared
       with the Scan Command, so this is just a new trigger + Error List output

## Tech Stack
  - VSIX project targeting Visual Studio 2026 (18.0+), net10.0
  - VisualStudio.Extensibility (out-of-proc extensibility model)
    → Extension class entry point (NOT AsyncPackage)
    → Code-based command registration via CommandConfiguration (no VSCT files)
  - Remote UI for tool window (RemoteUserControl + DataContract proxy binding)
    → NOT traditional WPF — out-of-proc extensions cannot use WPF directly
  - NuGet.ProjectModel 7.3.0+ to parse project.assets.json
  - Microsoft.Build + MSBuildLocator for project file access
  - System.Xml.Linq for config file manipulation
  - System.Reflection.MetadataLoadContext for reading assembly versions
    without file locking (preferred over AssemblyName.GetAssemblyName)
  - Hybrid out-of-proc + in-proc model:
    → Most features run out-of-proc (scan, analyze, UI, diagnostics output)
    → In-proc VSSDK bridge (VisualStudio.Extensibility.Contracts) required for:
      build event subscription (IVsUpdateSolutionEvents2),
      build trigger for CONFLICT fix (IVsUIShell.PostExecCommand),
      source control checkout (IVsQueryEditQuerySave2),
      NuGet restore trigger
  - NuGet.VisualStudio.Contracts for NuGet restore event subscription

## Project Structure
  BindingRedirectFixer/
  ├── BindingRedirectFixerExtension.cs   (Extension class entry point)
  ├── Commands/
  │   └── ScanCommand.cs                (CommandConfiguration + Placements)
  ├── Services/
  │   ├── IVersionResolver.cs           (abstraction for version resolution)
  │   ├── AssetsJsonVersionResolver.cs   (PackageReference projects)
  │   ├── PackagesConfigVersionResolver.cs (packages.config projects)
  │   ├── AssemblyMetadataReader.cs      (MetadataLoadContext wrapper)
  │   ├── BindingRedirectAnalyzer.cs     (compare sources, detect mismatches)
  │   ├── MissingRedirectDetector.cs     (dependency graph conflict detection)
  │   ├── ConfigPatcher.cs              (read/write web.config/app.config)
  │   └── BinFolderScanner.cs           (scan physical DLLs)
  ├── Models/
  │   └── AssemblyRedirectInfo.cs       (holds all version info per assembly)
  ├── ToolWindows/
  │   ├── BindingRedirectToolWindow.cs          (ToolWindow provider)
  │   ├── BindingRedirectToolWindowControl.xaml  (RemoteUserControl, not WPF)
  │   ├── BindingRedirectToolWindowControl.cs    (RemoteUserControl subclass)
  │   └── BindingRedirectToolWindowViewModel.cs  (DataContract for Remote UI binding)
  └── source.extension.vsixmanifest

## Key Implementation Details

  Remote UI data binding pattern:
  - ViewModel must use [DataContract] and [DataMember] attributes
  - Properties communicated via proxy between extension process and VS process
  - Commands in ViewModel use IAsyncCommand
  - Collections use ObservableList<T> (from VisualStudio.Extensibility)
  - XAML uses standard WPF syntax but runs in Remote UI infrastructure
  - Per-cell styling: bind cell background to a color property on the row model
    (each version field has a corresponding status/color field in the DataContract)

  Remote UI Theming & Styling:
  - XAML can reference VS process types and assemblies — use VS resource keys
    (VsResourceKeys, VsBrushes) to match the native VS look exactly
  - Theme auto-adaptation: colors automatically change to reflect the current
    user-selected theme (Light, Dark, Blue) and system High Contrast mode
  - Fonts and spacing: use VS resource keys for consistent sizing and spacing
  - XAML resource dictionaries (available since VS 17.10): define shared styles,
    templates, and resources used across both the Issues and Learn tabs
  - Goal: extension should look indistinguishable from first-party VS panels

  Per-cell coloring (theme-aware approach via IValueConverter):
  - Remote UI does not support DataTriggers — use IValueConverter instead
  - Create StatusToBrushConverter: maps Status enum → VS brush resource key
  - OK cells: use standard VS foreground (VsBrushes.WindowText)
  - Diverging cells: use VsBrushes.ControlLinkText or a warning/error brush
    from VsResourceKeys that adapts to all themes
  - Gray/unavailable cells: use VsBrushes.GrayText
  - Bind cell Foreground to converter: Foreground="{Binding Status, Converter={StaticResource StatusBrush}}"
  - Row highlight on selection: use VsBrushes.Highlight + VsBrushes.HighlightText

  Remote UI constraints (important for implementation):
  - XAML runs in the VS process but extension code runs out-of-process
  - XAML can ONLY reference types from the VS process — no third-party
    control libraries (Telerik, Syncfusion, etc.) can be used
  - Limited to standard WPF controls + VS resource keys
  - DataGrid is NOT supported in Remote UI — use ListView + DataTemplate instead
  - No DataTriggers — use IValueConverter for conditional styling
  - No code-behind event handlers — use IAsyncCommand bindings
  - Grid layout: use ListView with DataTemplate containing a Grid layout
    with ColumnDefinitions to create table-like rows (official MS pattern)
  - For the Learn tab: use standard TextBlock, StackPanel, Border, Expander
  - For scenario cards: use Border + StackPanel with VS-themed backgrounds
  - For the version flow diagram: use TextBlock with monospace font

  AssemblyMetadataReader (using MetadataLoadContext):
  - Does NOT lock the DLL file (unlike AssemblyName.GetAssemblyName)
  - Returns: AssemblyVersion, PublicKeyToken, Culture
  - Requires a resolver for core assembly references (use PathAssemblyResolver)
  - Fallback to AssemblyName.GetAssemblyName() with IOException handling
    for cases where MetadataLoadContext is unavailable

  AssetsJsonVersionResolver should parse:
  {
    "targets": {
      ".NETFramework,Version=v4.8": {
        "Newtonsoft.Json/13.0.3": {
          "runtime": {
            "lib/net45/Newtonsoft.Json.dll": {}
          }
        }
      }
    },
    "packageFolders": {
      "C:\\Users\\.../.nuget/packages/": {}
    }
  }
  → Compute DLL path: {packageFolder}/{packageId}/{version}/{runtimePath}
  → Read assembly version from that DLL via AssemblyMetadataReader
  → Return: packageVersion=13.0.3, assemblyVersion=13.0.0.0,
            publicKeyToken=30ad4fe6b2a6aeed, culture=neutral

  PackagesConfigVersionResolver should:
  1. Parse packages.config for package id + version
  2. Locate DLL in packages/{id}.{version}/lib/{tfm}/
  3. Read assembly version via AssemblyMetadataReader
  4. Return same shape as AssetsJsonVersionResolver

  AssemblyRedirectInfo model:
  {
    ProjectName: string,             // which project this belongs to
    Name: string,                    // assembly name
    PublicKeyToken: string,          // from assembly metadata (needed for Add Redirect)
    Culture: string,                 // from assembly metadata (needed for Add Redirect)
    ResolvedAssemblyVersion: Version, // from NuGet cache/packages folder DLL (authoritative)
    ResolvedPackageVersion: string,   // informational: package version from NuGet
    RequestedVersion: string,        // from PackageReference/packages.config
    PhysicalVersion: Version,        // from bin/ DLL (assembly version)
    CurrentRedirectVersion: Version, // from web.config/app.config
    Status: RedirectStatus,          // OK | Stale | Missing | Conflict
    DiagnosticMessage: string,       // plain-English explanation of the issue
    SuggestedAction: FixAction       // None | UpdateRedirect | AddRedirect | RebuildProject
  }

  Status evaluation (decision tree, first match wins):
  1. MISSING: no redirect exists AND assembly has version conflicts in dep tree
  2. STALE: CurrentRedirectVersion != ResolvedAssemblyVersion
  3. CONFLICT: PhysicalVersion != ResolvedAssemblyVersion (but redirect is correct)
  4. OK: all sources agree

## Error Handling

  File access:
  - Check file write access before attempting modifications
  - Handle IOException for locked files (build in progress)
  - For read-only files: show "File is read-only" with suggested action

  Source control (TFVC/TFS):
  - Call IVsQueryEditQuerySave2.QueryEditFiles before modifying config files
    (requires in-proc VSSDK bridge — not available out-of-proc)
  - Triggers checkout dialog for TFVC-controlled files
  - Show error if checkout is denied or fails

  Missing data:
  - project.assets.json missing + PackageReference project → show [Restore] button
  - packages.config missing → skip project with warning
  - bin/ folder empty → show dash in grid, note "Project not built yet"
  - Assembly not in NuGet cache → show dash, note "Package not restored"

## Solution Lifecycle

  - Subscribe to solution events via SolutionService.SubscribeAsync
    (available out-of-proc — no bridge needed)
  - On solution close: clear the tool window grid
  - On solution open: auto-scan if tool window is visible (user setting: enable/disable, default: enabled)
  - On project load/unload: refresh affected rows

## Output Window Logging

  - Create custom Output Window pane: "Binding Redirect Fixer"
  - Use IOutputWindowService from VisualStudio.Extensibility
  - Log: scan start/end with project count, per-assembly status changes,
    fix actions taken (before/after versions), errors and warnings
  - Verbosity: always log errors and fixes; log OK statuses only in verbose mode

Generate the full implementation with all files, including .csproj,
vsixmanifest, and all C# classes. Add XML doc comments.
Use async/await throughout. Handle exceptions gracefully with
output to VS Output window.
