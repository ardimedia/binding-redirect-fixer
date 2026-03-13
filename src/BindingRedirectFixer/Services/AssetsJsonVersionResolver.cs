using NuGet.ProjectModel;

namespace BindingRedirectFixer.Services;

/// <summary>
/// Resolves assembly versions for PackageReference projects by parsing
/// <c>obj/project.assets.json</c> via <see cref="LockFileUtilities"/>.
/// </summary>
public sealed class AssetsJsonVersionResolver : IVersionResolver
{
    /// <inheritdoc />
    public Task<Dictionary<string, ResolvedAssemblyInfo>> ResolveAsync(
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, ResolvedAssemblyInfo>(StringComparer.OrdinalIgnoreCase);

        string assetsPath = Path.Combine(projectDirectory, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
        {
            return Task.FromResult(results);
        }

        LockFile? lockFile;
        try
        {
            lockFile = LockFileUtilities.GetLockFile(assetsPath, NuGet.Common.NullLogger.Instance);
        }
        catch (Exception)
        {
            // Corrupted or unreadable assets file — return empty
            return Task.FromResult(results);
        }

        if (lockFile is null)
        {
            return Task.FromResult(results);
        }

        string packageFolders = lockFile.PackageFolders.FirstOrDefault()?.Path ?? string.Empty;
        if (string.IsNullOrEmpty(packageFolders))
        {
            return Task.FromResult(results);
        }

        foreach (LockFileTarget target in lockFile.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (LockFileTargetLibrary library in target.Libraries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (library.Type != "package")
                {
                    continue;
                }

                string packageId = library.Name ?? string.Empty;
                string packageVersion = library.Version?.ToNormalizedString() ?? string.Empty;

                foreach (LockFileItem runtimeAssembly in library.RuntimeAssemblies)
                {
                    // Skip placeholder entries like "_._"
                    if (runtimeAssembly.Path.EndsWith("_._", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string dllPath = Path.Combine(
                        packageFolders,
                        packageId.ToLowerInvariant(),
                        packageVersion.ToLowerInvariant(),
                        runtimeAssembly.Path.Replace('/', Path.DirectorySeparatorChar));

                    if (!File.Exists(dllPath))
                    {
                        continue;
                    }

                    var info = AssemblyMetadataReader.ReadAssemblyInfo(dllPath);
                    if (info is null)
                    {
                        continue;
                    }

                    string assemblyName = Path.GetFileNameWithoutExtension(runtimeAssembly.Path);

                    // If the same assembly appears in multiple targets, keep the first occurrence
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
        }

        return Task.FromResult(results);
    }
}
