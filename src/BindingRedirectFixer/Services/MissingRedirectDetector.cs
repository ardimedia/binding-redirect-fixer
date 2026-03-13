using System.Xml.Linq;
using NuGet.ProjectModel;

namespace BindingRedirectFixer.Services;

/// <summary>
/// Detects assemblies that need binding redirects but currently lack them.
/// Supports both PackageReference (project.assets.json) and packages.config projects.
/// </summary>
public sealed class MissingRedirectDetector
{
    /// <summary>
    /// Detects assemblies with version conflicts that may require binding redirects.
    /// </summary>
    /// <param name="projectDirectory">Path to the project directory.</param>
    /// <param name="isPackagesConfig">
    /// <c>true</c> to use packages.config detection; <c>false</c> for PackageReference (assets.json).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// List of <see cref="ResolvedAssemblyInfo"/> for assemblies that have version conflicts
    /// and likely need a binding redirect.
    /// </returns>
    public Task<List<ResolvedAssemblyInfo>> DetectMissingRedirectsAsync(
        string projectDirectory,
        bool isPackagesConfig,
        CancellationToken cancellationToken)
    {
        return isPackagesConfig
            ? Task.FromResult(DetectFromPackagesConfig(projectDirectory, cancellationToken))
            : Task.FromResult(DetectFromAssetsJson(projectDirectory, cancellationToken));
    }

    /// <summary>
    /// Detects missing redirects for PackageReference projects by analyzing project.assets.json.
    /// Groups all runtime DLL references by assembly name and flags those with multiple versions.
    /// </summary>
    private static List<ResolvedAssemblyInfo> DetectFromAssetsJson(
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        var results = new List<ResolvedAssemblyInfo>();

        string assetsPath = Path.Combine(projectDirectory, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
        {
            return results;
        }

        LockFile? lockFile;
        try
        {
            lockFile = LockFileUtilities.GetLockFile(assetsPath, NuGet.Common.NullLogger.Instance);
        }
        catch (Exception)
        {
            return results;
        }

        if (lockFile is null)
        {
            return results;
        }

        string packageFolder = lockFile.PackageFolders.FirstOrDefault()?.Path ?? string.Empty;

        // Collect all assembly versions across all targets and libraries
        // Key: assembly name, Value: set of (version, packageId, packageVersion, dllPath)
        var assemblyVersions = new Dictionary<string, List<(string Version, string PackageId, string PackageVersion, string DllPath)>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (LockFileTarget target in lockFile.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (LockFileTargetLibrary library in target.Libraries)
            {
                if (library.Type != "package")
                {
                    continue;
                }

                string packageId = library.Name ?? string.Empty;
                string packageVersion = library.Version?.ToNormalizedString() ?? string.Empty;

                foreach (LockFileItem runtimeAssembly in library.RuntimeAssemblies)
                {
                    if (runtimeAssembly.Path.EndsWith("_._", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string assemblyName = Path.GetFileNameWithoutExtension(runtimeAssembly.Path);
                    string dllPath = !string.IsNullOrEmpty(packageFolder)
                        ? Path.Combine(
                            packageFolder,
                            packageId.ToLowerInvariant(),
                            packageVersion.ToLowerInvariant(),
                            runtimeAssembly.Path.Replace('/', Path.DirectorySeparatorChar))
                        : string.Empty;

                    if (!assemblyVersions.TryGetValue(assemblyName, out var versionList))
                    {
                        versionList = [];
                        assemblyVersions[assemblyName] = versionList;
                    }

                    versionList.Add((packageVersion, packageId, packageVersion, dllPath));
                }

                // Also check compile-time assemblies for version conflicts
                foreach (LockFileItem compileAssembly in library.CompileTimeAssemblies)
                {
                    if (compileAssembly.Path.EndsWith("_._", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string assemblyName = Path.GetFileNameWithoutExtension(compileAssembly.Path);

                    if (!assemblyVersions.ContainsKey(assemblyName))
                    {
                        assemblyVersions[assemblyName] = [];
                    }
                }
            }
        }

        // Flag assemblies referenced by multiple packages (potential version conflicts)
        foreach (var (assemblyName, versionList) in assemblyVersions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var distinctPackages = versionList
                .Select(v => v.PackageId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (distinctPackages.Count <= 1)
            {
                continue;
            }

            // Multiple packages reference this assembly — read actual version from the highest package version
            var best = versionList
                .OrderByDescending(v => v.PackageVersion)
                .First();

            string assemblyVersion = string.Empty;
            string publicKeyToken = string.Empty;
            string culture = "neutral";

            if (File.Exists(best.DllPath))
            {
                var info = AssemblyMetadataReader.ReadAssemblyInfo(best.DllPath);
                if (info is not null)
                {
                    assemblyVersion = info.AssemblyVersion.ToString();
                    publicKeyToken = info.PublicKeyToken;
                    culture = info.Culture;
                }
            }

            results.Add(new ResolvedAssemblyInfo(
                AssemblyName: assemblyName,
                AssemblyVersion: assemblyVersion,
                PackageVersion: best.PackageVersion,
                PublicKeyToken: publicKeyToken,
                Culture: culture));
        }

        return results;
    }

    /// <summary>
    /// Detects missing redirects for packages.config projects by parsing package dependencies
    /// from .nuspec files and identifying version conflicts.
    /// </summary>
    private static List<ResolvedAssemblyInfo> DetectFromPackagesConfig(
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        var results = new List<ResolvedAssemblyInfo>();

        string packagesConfigPath = Path.Combine(projectDirectory, "packages.config");
        if (!File.Exists(packagesConfigPath))
        {
            return results;
        }

        XDocument packagesDoc;
        try
        {
            packagesDoc = XDocument.Load(packagesConfigPath);
        }
        catch (Exception)
        {
            return results;
        }

        if (packagesDoc.Root is null)
        {
            return results;
        }

        string? packagesFolder = FindPackagesFolder(projectDirectory);
        if (packagesFolder is null)
        {
            return results;
        }

        // Collect installed packages
        var installedPackages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement pkg in packagesDoc.Root.Elements("package"))
        {
            string? id = pkg.Attribute("id")?.Value;
            string? version = pkg.Attribute("version")?.Value;
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(version))
            {
                installedPackages[id] = version;
            }
        }

        // Track dependency version requests: assembly/dependency name -> set of requested versions
        var dependencyVersions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        // Parse .nuspec files to find dependency version conflicts
        foreach (var (packageId, packageVersion) in installedPackages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string nuspecPath = Path.Combine(
                packagesFolder,
                $"{packageId}.{packageVersion}",
                $"{packageId}.nuspec");

            if (!File.Exists(nuspecPath))
            {
                continue;
            }

            XDocument nuspecDoc;
            try
            {
                nuspecDoc = XDocument.Load(nuspecPath);
            }
            catch (Exception)
            {
                continue;
            }

            if (nuspecDoc.Root is null)
            {
                continue;
            }

            XNamespace ns = nuspecDoc.Root.GetDefaultNamespace();

            IEnumerable<XElement> dependencies = nuspecDoc.Descendants(ns + "dependency");
            foreach (XElement dep in dependencies)
            {
                string? depId = dep.Attribute("id")?.Value;
                string? depVersion = dep.Attribute("version")?.Value;

                if (string.IsNullOrEmpty(depId) || string.IsNullOrEmpty(depVersion))
                {
                    continue;
                }

                if (!dependencyVersions.TryGetValue(depId, out var versions))
                {
                    versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    dependencyVersions[depId] = versions;
                }

                versions.Add(depVersion);
            }
        }

        // Find dependencies with multiple version requests that are also installed
        foreach (var (depId, versions) in dependencyVersions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (versions.Count <= 1)
            {
                continue;
            }

            // This dependency is requested at multiple versions — potential redirect needed
            if (!installedPackages.TryGetValue(depId, out string? installedVersion))
            {
                continue;
            }

            // Try to read the actual assembly info
            string packageFolder = Path.Combine(packagesFolder, $"{depId}.{installedVersion}", "lib");
            string assemblyVersion = string.Empty;
            string publicKeyToken = string.Empty;
            string culture = "neutral";

            if (Directory.Exists(packageFolder))
            {
                string? dllPath = FindFirstDll(packageFolder, depId);
                if (dllPath is not null)
                {
                    var info = AssemblyMetadataReader.ReadAssemblyInfo(dllPath);
                    if (info is not null)
                    {
                        assemblyVersion = info.AssemblyVersion.ToString();
                        publicKeyToken = info.PublicKeyToken;
                        culture = info.Culture;
                    }
                }
            }

            results.Add(new ResolvedAssemblyInfo(
                AssemblyName: depId,
                AssemblyVersion: assemblyVersion,
                PackageVersion: installedVersion,
                PublicKeyToken: publicKeyToken,
                Culture: culture));
        }

        return results;
    }

    /// <summary>
    /// Finds the solution-level packages/ folder by walking up from the project directory.
    /// </summary>
    private static string? FindPackagesFolder(string projectDirectory)
    {
        string? current = projectDirectory;

        while (current is not null)
        {
            string candidate = Path.Combine(current, "packages");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            string? parent = Directory.GetParent(current)?.FullName;
            if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }

        return null;
    }

    /// <summary>
    /// Finds the first DLL matching the given package ID in a lib/ folder hierarchy.
    /// </summary>
    private static string? FindFirstDll(string libFolder, string packageId)
    {
        try
        {
            // Look for {packageId}.dll in all subdirectories
            string[] allDlls = Directory.GetFiles(libFolder, $"{packageId}.dll", SearchOption.AllDirectories);
            if (allDlls.Length > 0)
            {
                return allDlls[0];
            }

            // Fall back to any DLL
            allDlls = Directory.GetFiles(libFolder, "*.dll", SearchOption.AllDirectories);
            return allDlls.Length > 0 ? allDlls[0] : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
