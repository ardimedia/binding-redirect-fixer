using BindingRedirectFixer.Models;

namespace BindingRedirectFixer.Services;

/// <summary>
/// Main analysis engine that combines all version sources (NuGet resolution, bin/ scanning,
/// config file reading, missing redirect detection) to produce a comprehensive binding
/// redirect report for a project.
/// </summary>
public sealed class BindingRedirectAnalyzer
{
    private readonly ConfigPatcher _configPatcher = new();
    private readonly BinFolderScanner _binScanner = new();
    private readonly MissingRedirectDetector _missingDetector = new();

    /// <summary>
    /// Analyzes all binding redirects for the specified project, combining data from
    /// NuGet resolution, physical bin/ folder, config file, and missing redirect detection.
    /// </summary>
    /// <param name="projectName">Display name of the project.</param>
    /// <param name="projectDirectory">Full path to the project directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A list of <see cref="AssemblyRedirectInfo"/> with status, diagnostics,
    /// and suggested actions for each assembly.
    /// </returns>
    public async Task<List<AssemblyRedirectInfo>> AnalyzeProjectAsync(
        string projectName,
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        var results = new List<AssemblyRedirectInfo>();

        try
        {
            // Step 1: Detect project type
            bool isPackagesConfig = File.Exists(
                Path.Combine(projectDirectory, "packages.config"));

            // Step 2: Create appropriate version resolver
            IVersionResolver resolver = isPackagesConfig
                ? new PackagesConfigVersionResolver()
                : new AssetsJsonVersionResolver();

            // Step 3: Run all data sources in parallel
            Task<Dictionary<string, ResolvedAssemblyInfo>> resolverTask =
                resolver.ResolveAsync(projectDirectory, cancellationToken);

            Task<Dictionary<string, string>> binScanTask =
                _binScanner.ScanAsync(projectDirectory, cancellationToken);

            Task<List<ResolvedAssemblyInfo>> missingTask =
                _missingDetector.DetectMissingRedirectsAsync(
                    projectDirectory, isPackagesConfig, cancellationToken);

            await Task.WhenAll(resolverTask, binScanTask, missingTask).ConfigureAwait(false);

            Dictionary<string, ResolvedAssemblyInfo> resolved = await resolverTask.ConfigureAwait(false);
            Dictionary<string, string> physicalVersions = await binScanTask.ConfigureAwait(false);
            List<ResolvedAssemblyInfo> missingRedirects = await missingTask.ConfigureAwait(false);

            // Step 4: Read existing config redirects
            string? configPath = _configPatcher.GetConfigFilePath(projectDirectory);
            Dictionary<string, (string OldVersion, string NewVersion, string PublicKeyToken)> existingRedirects =
                configPath is not null
                    ? _configPatcher.ReadRedirects(configPath)
                    : new Dictionary<string, (string, string, string)>();

            // Step 4b: Detect duplicate binding redirect entries
            HashSet<string> duplicateAssemblies = configPath is not null
                ? _configPatcher.DetectDuplicateRedirects(configPath)
                : [];

            // Step 5: Build the set of all known assembly names
            var allAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in resolved.Keys) allAssemblyNames.Add(name);
            foreach (string name in existingRedirects.Keys) allAssemblyNames.Add(name);
            foreach (var info in missingRedirects) allAssemblyNames.Add(info.AssemblyName);

            // Step 6: Build a lookup for missing redirects
            var missingLookup = new Dictionary<string, ResolvedAssemblyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in missingRedirects)
            {
                missingLookup.TryAdd(info.AssemblyName, info);
            }

            // Step 7: Evaluate each assembly
            foreach (string assemblyName in allAssemblyNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = new AssemblyRedirectInfo
                {
                    ProjectName = projectName,
                    Name = assemblyName
                };

                // Populate resolved version info
                if (resolved.TryGetValue(assemblyName, out ResolvedAssemblyInfo? resolvedInfo))
                {
                    entry.ResolvedAssemblyVersion = resolvedInfo.AssemblyVersion;
                    entry.ResolvedPackageVersion = resolvedInfo.PackageVersion;
                    entry.RequestedVersion = resolvedInfo.PackageVersion;
                    entry.PublicKeyToken = resolvedInfo.PublicKeyToken;
                    entry.Culture = resolvedInfo.Culture;
                }

                // Populate physical version
                if (physicalVersions.TryGetValue(assemblyName, out string? physicalVersion))
                {
                    entry.PhysicalVersion = physicalVersion;
                }

                // Populate current redirect version and config public key token
                if (existingRedirects.TryGetValue(assemblyName, out var redirect))
                {
                    entry.CurrentRedirectVersion = redirect.NewVersion;
                    entry.ConfigPublicKeyToken = redirect.PublicKeyToken;
                }

                // Populate from missing redirect detector if no resolved info exists
                if (resolvedInfo is null && missingLookup.TryGetValue(assemblyName, out var missingInfo))
                {
                    entry.ResolvedAssemblyVersion = missingInfo.AssemblyVersion;
                    entry.ResolvedPackageVersion = missingInfo.PackageVersion;
                    entry.PublicKeyToken = missingInfo.PublicKeyToken;
                    entry.Culture = missingInfo.Culture;
                }

                // Step 8: Apply decision tree
                EvaluateStatus(entry, missingLookup.ContainsKey(assemblyName), duplicateAssemblies.Contains(assemblyName));

                results.Add(entry);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Return a single error entry so the caller knows analysis failed
            results.Add(new AssemblyRedirectInfo
            {
                ProjectName = projectName,
                Name = "(analysis error)",
                Status = RedirectStatus.Conflict,
                DiagnosticMessage = $"Analysis failed: {ex.Message}",
                SuggestedAction = FixAction.None
            });
        }

        return results;
    }

    /// <summary>
    /// Evaluates the redirect status using the decision tree.
    /// The effective target version is MAX(resolved, physical) because the binding redirect
    /// must cover all referenced versions and newVersion must match what the runtime loads.
    /// 0. DUPLICATE: multiple binding redirect entries exist for the same assembly.
    /// 0b. MISMATCH: redirect targets a version not on disk (bin/ DLL older than NuGet resolved).
    /// 1. MISSING: no redirect exists AND assembly has version conflicts AND is in bin/.
    /// 2. STALE: CurrentRedirectVersion != EffectiveTargetVersion.
    /// 3. OK (informational): bin/ DLL differs from NuGet resolved — no action needed.
    /// 4. OK: all sources agree.
    /// </summary>
    /// <param name="entry">The assembly info entry to evaluate.</param>
    /// <param name="hasVersionConflict">Whether the missing redirect detector flagged this assembly.</param>
    /// <param name="hasDuplicateRedirects">Whether the config file has multiple entries for this assembly.</param>
    private static void EvaluateStatus(AssemblyRedirectInfo entry, bool hasVersionConflict, bool hasDuplicateRedirects)
    {
        string? target = entry.EffectiveTargetVersion;

        // Rule -1: DEPRECATED — package replaced by a modern equivalent
        if (DeprecatedPackageRegistry.TryGetDeprecation(entry.Name, out var deprecation))
        {
            entry.Status = RedirectStatus.Deprecated;
            entry.DiagnosticMessage =
                $"'{entry.Name}' is deprecated. Migrate to '{deprecation!.ReplacementPackage}' " +
                "instead of fixing the binding redirect." +
                (deprecation.MigrationUrl is not null ? $" See: {deprecation.MigrationUrl}" : "");
            entry.SuggestedAction = FixAction.None;
            return;
        }

        // Rule 0: DUPLICATE — multiple binding redirect entries for the same assembly
        if (hasDuplicateRedirects)
        {
            entry.Status = RedirectStatus.Duplicate;
            entry.DiagnosticMessage =
                $"Multiple binding redirect entries exist for '{entry.Name}' in the config file. " +
                $"Only one entry should exist, targeting version {target ?? "unknown"}.";
            entry.SuggestedAction = FixAction.RemoveDuplicate;
            return;
        }

        // Rule 0b: MISMATCH — redirect targets a version not available on disk
        // When bin/ DLL is older than NuGet resolved and the redirect targets the NuGet version,
        // the runtime will try to load a version that doesn't exist. This is common for .NET Framework
        // assemblies (System.ServiceModel.*, etc.) where the bin/ copy comes from the runtime/GAC
        // but NuGet resolved a newer cross-platform package version.
        if (!string.IsNullOrEmpty(entry.CurrentRedirectVersion) &&
            !string.IsNullOrEmpty(entry.PhysicalVersion) &&
            !string.IsNullOrEmpty(entry.ResolvedAssemblyVersion))
        {
            try
            {
                var physicalVer = new Version(entry.PhysicalVersion);
                var resolvedVer = new Version(entry.ResolvedAssemblyVersion);
                var configVer = new Version(entry.CurrentRedirectVersion);

                // bin/ is older than NuGet resolved, and config targets something higher than bin/
                if (physicalVer < resolvedVer && configVer > physicalVer)
                {
                    entry.Status = RedirectStatus.Mismatch;
                    entry.DiagnosticMessage =
                        $"The binding redirect for '{entry.Name}' targets version {entry.CurrentRedirectVersion}, " +
                        $"but the DLL in bin/ is only {entry.PhysicalVersion} " +
                        $"(NuGet resolved {entry.ResolvedAssemblyVersion} from a cross-platform package). " +
                        "The redirect points to a version that is not available on disk. " +
                        "This entry should be removed from the config file — the runtime will load " +
                        $"the {entry.PhysicalVersion} DLL from bin/ without a redirect.";
                    entry.SuggestedAction = FixAction.RemoveRedirect;
                    return;
                }
            }
            catch
            {
                // Version parsing failed — fall through to other rules
            }
        }

        // Rule 0c: TOKEN_LOST — the resolved assembly's public key token is empty
        // but the config file has a non-empty token. This usually means the NuGet package
        // resolved an unsigned build of the assembly (e.g., preview/RC build, wrong TFM,
        // or the package author dropped strong naming).
        // Preserve the config's token so the runtime can still match the assembly identity.
        if (!string.IsNullOrEmpty(entry.CurrentRedirectVersion) &&
            !string.IsNullOrEmpty(entry.ConfigPublicKeyToken) &&
            string.IsNullOrEmpty(entry.PublicKeyToken))
        {
            // Preserve the config token for any subsequent fix operations
            entry.PublicKeyToken = entry.ConfigPublicKeyToken;

            bool versionNeedsUpdate = !string.IsNullOrEmpty(target) &&
                !string.Equals(entry.CurrentRedirectVersion, target, StringComparison.OrdinalIgnoreCase);

            entry.Status = RedirectStatus.TokenLost;
            entry.DiagnosticMessage =
                $"The resolved assembly for '{entry.Name}' has no public key token (unsigned), " +
                $"but the config file has publicKeyToken=\"{entry.ConfigPublicKeyToken}\". " +
                "This usually means the NuGet package resolved an unsigned build of the assembly. " +
                $"The original token will be preserved." +
                (versionNeedsUpdate
                    ? $" The redirect version also needs updating from {entry.CurrentRedirectVersion} to {target}."
                    : string.Empty);
            entry.SuggestedAction = versionNeedsUpdate ? FixAction.UpdateRedirect : FixAction.None;
            return;
        }

        // Rule 1: MISSING — no redirect exists but the assembly has version conflicts
        // Only flag if the assembly is actually deployed to bin/ — if it's only in the NuGet
        // dependency graph but not in the output folder, there's no runtime binding conflict.
        if (string.IsNullOrEmpty(entry.CurrentRedirectVersion) &&
            hasVersionConflict &&
            !string.IsNullOrEmpty(entry.PhysicalVersion))
        {
            entry.Status = RedirectStatus.Missing;
            entry.DiagnosticMessage =
                $"No binding redirect exists for '{entry.Name}' but multiple versions are referenced. " +
                $"Target version: {target ?? "unknown"}.";
            entry.SuggestedAction = FixAction.AddRedirect;
            return;
        }

        // Rule 2: STALE — redirect exists but doesn't match the effective target version
        if (!string.IsNullOrEmpty(entry.CurrentRedirectVersion) &&
            !string.IsNullOrEmpty(target) &&
            !string.Equals(entry.CurrentRedirectVersion, target, StringComparison.OrdinalIgnoreCase))
        {
            entry.Status = RedirectStatus.Stale;
            string detail = !string.IsNullOrEmpty(entry.PhysicalVersion) &&
                            !string.Equals(entry.PhysicalVersion, entry.ResolvedAssemblyVersion, StringComparison.OrdinalIgnoreCase)
                ? $" (bin/ DLL is {entry.PhysicalVersion}, NuGet resolved is {entry.ResolvedAssemblyVersion}; using highest)"
                : string.Empty;
            entry.DiagnosticMessage =
                $"Binding redirect for '{entry.Name}' points to {entry.CurrentRedirectVersion} " +
                $"but the effective target version is {target}.{detail}";
            entry.SuggestedAction = FixAction.UpdateRedirect;
            return;
        }

        // Rule 3: Physical DLL differs from NuGet resolved — informational only
        // When no binding redirect exists, the runtime simply loads whatever DLL is in bin/.
        // The version difference is expected (transitive dependencies, framework assemblies)
        // and does not cause runtime issues. Only flag as informational OK.
        if (!string.IsNullOrEmpty(entry.PhysicalVersion) &&
            !string.IsNullOrEmpty(entry.ResolvedAssemblyVersion) &&
            !string.Equals(entry.PhysicalVersion, entry.ResolvedAssemblyVersion, StringComparison.OrdinalIgnoreCase))
        {
            entry.Status = RedirectStatus.OK;
            try
            {
                var physicalVer = new Version(entry.PhysicalVersion);
                var resolvedVer = new Version(entry.ResolvedAssemblyVersion);

                entry.DiagnosticMessage = physicalVer > resolvedVer
                    ? $"The DLL in bin/ for '{entry.Name}' is version {entry.PhysicalVersion} " +
                      $"(newer than NuGet-resolved {entry.ResolvedAssemblyVersion}). " +
                      "This is typical for transitive dependencies where another package brings in a newer version."
                    : $"The DLL in bin/ for '{entry.Name}' is version {entry.PhysicalVersion} " +
                      $"(older than NuGet-resolved {entry.ResolvedAssemblyVersion}). " +
                      "This is typical for .NET Framework assemblies where the bin/ copy " +
                      "comes from the runtime/GAC.";
            }
            catch
            {
                entry.DiagnosticMessage =
                    $"The DLL in bin/ for '{entry.Name}' is version {entry.PhysicalVersion} " +
                    $"but the NuGet-resolved version is {entry.ResolvedAssemblyVersion}.";
            }

            entry.SuggestedAction = FixAction.None;
            return;
        }

        // Rule 4: OK — all sources agree
        entry.Status = RedirectStatus.OK;
        entry.DiagnosticMessage = string.Empty;
        entry.SuggestedAction = FixAction.None;
    }
}
