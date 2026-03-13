using System.Xml.Linq;

namespace BindingRedirectFixer.Services;

/// <summary>
/// Resolves assembly versions for packages.config projects by parsing
/// the packages.config XML and locating DLLs in the solution-level packages/ folder.
/// </summary>
public sealed class PackagesConfigVersionResolver : IVersionResolver
{
    /// <summary>
    /// Known target framework monikers ordered by preference (newest first).
    /// Used to select the best matching lib subfolder.
    /// </summary>
    private static readonly string[] TfmPreference =
    [
        "net48", "net472", "net471", "net47",
        "net462", "net461", "net46",
        "net452", "net451", "net45",
        "net40", "net403", "net40-client",
        "net35", "net20",
        "netstandard2.1", "netstandard2.0", "netstandard1.6",
        "netstandard1.5", "netstandard1.4", "netstandard1.3",
        "netstandard1.2", "netstandard1.1", "netstandard1.0"
    ];

    /// <inheritdoc />
    public Task<Dictionary<string, ResolvedAssemblyInfo>> ResolveAsync(
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, ResolvedAssemblyInfo>(StringComparer.OrdinalIgnoreCase);

        string packagesConfigPath = Path.Combine(projectDirectory, "packages.config");
        if (!File.Exists(packagesConfigPath))
        {
            return Task.FromResult(results);
        }

        XDocument doc;
        try
        {
            doc = XDocument.Load(packagesConfigPath);
        }
        catch (Exception)
        {
            return Task.FromResult(results);
        }

        if (doc.Root is null)
        {
            return Task.FromResult(results);
        }

        string? packagesFolder = FindPackagesFolder(projectDirectory);
        if (packagesFolder is null)
        {
            return Task.FromResult(results);
        }

        foreach (XElement packageElement in doc.Root.Elements("package"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? packageId = packageElement.Attribute("id")?.Value;
            string? packageVersion = packageElement.Attribute("version")?.Value;

            if (string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(packageVersion))
            {
                continue;
            }

            string packageFolder = Path.Combine(packagesFolder, $"{packageId}.{packageVersion}");
            if (!Directory.Exists(packageFolder))
            {
                continue;
            }

            string libFolder = Path.Combine(packageFolder, "lib");
            if (!Directory.Exists(libFolder))
            {
                continue;
            }

            string? bestTfmFolder = SelectBestTfmFolder(libFolder);
            if (bestTfmFolder is null)
            {
                continue;
            }

            string[] dlls;
            try
            {
                dlls = Directory.GetFiles(bestTfmFolder, "*.dll");
            }
            catch (Exception)
            {
                continue;
            }

            foreach (string dllPath in dlls)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var info = AssemblyMetadataReader.ReadAssemblyInfo(dllPath);
                if (info is null)
                {
                    continue;
                }

                string assemblyName = Path.GetFileNameWithoutExtension(dllPath);

                if (results.ContainsKey(assemblyName))
                {
                    continue;
                }

                results[assemblyName] = new ResolvedAssemblyInfo(
                    AssemblyName: assemblyName,
                    AssemblyVersion: info.AssemblyVersion.ToString(),
                    PackageVersion: packageVersion,
                    PublicKeyToken: info.PublicKeyToken,
                    Culture: info.Culture);
            }
        }

        return Task.FromResult(results);
    }

    /// <summary>
    /// Finds the solution-level packages/ folder by walking up from the project directory.
    /// Looks for a "packages" directory that is a sibling of a .sln file or simply exists
    /// at any ancestor level.
    /// </summary>
    /// <param name="projectDirectory">Starting project directory.</param>
    /// <returns>Full path to the packages folder, or <c>null</c> if not found.</returns>
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
    /// Selects the best target framework subfolder from a package's lib/ directory.
    /// Prefers known TFMs in order; falls back to the first available subfolder,
    /// or the lib/ folder itself if it contains DLLs directly.
    /// </summary>
    /// <param name="libFolder">The lib/ folder path.</param>
    /// <returns>Path to the best matching TFM folder, or <c>null</c> if none found.</returns>
    private static string? SelectBestTfmFolder(string libFolder)
    {
        string[] subDirs;
        try
        {
            subDirs = Directory.GetDirectories(libFolder);
        }
        catch (Exception)
        {
            return null;
        }

        if (subDirs.Length == 0)
        {
            // Some packages put DLLs directly in lib/ without a TFM subfolder
            return Directory.GetFiles(libFolder, "*.dll").Length > 0 ? libFolder : null;
        }

        // Try preferred TFMs in order
        foreach (string tfm in TfmPreference)
        {
            string candidate = Path.Combine(libFolder, tfm);
            if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "*.dll").Length > 0)
            {
                return candidate;
            }
        }

        // Fall back to the first subfolder that contains DLLs
        foreach (string subDir in subDirs)
        {
            if (Directory.GetFiles(subDir, "*.dll").Length > 0)
            {
                return subDir;
            }
        }

        return null;
    }
}
