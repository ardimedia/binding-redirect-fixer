namespace BindingRedirectFixer.Services;

/// <summary>
/// Scans physical DLLs in a project's bin/ output folder to determine
/// the actual assembly versions present on disk.
/// </summary>
public sealed class BinFolderScanner
{
    /// <summary>
    /// Default output subdirectories to scan when looking for compiled assemblies.
    /// </summary>
    private static readonly string[] OutputSubdirectories =
    [
        Path.Combine("bin", "Debug"),
        Path.Combine("bin", "Release"),
        "bin"
    ];

    /// <summary>
    /// Scans the project's bin/ output folder for .dll files and reads their assembly versions.
    /// </summary>
    /// <param name="projectDirectory">Path to the project directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Dictionary mapping assembly name to its four-part assembly version string.
    /// Only includes assemblies that could be successfully read.
    /// </returns>
    public Task<Dictionary<string, string>> ScanAsync(
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string? binFolder = FindOutputFolder(projectDirectory);
        if (binFolder is null)
        {
            return Task.FromResult(results);
        }

        string[] dllFiles;
        try
        {
            dllFiles = Directory.GetFiles(binFolder, "*.dll", SearchOption.TopDirectoryOnly);
        }
        catch (Exception)
        {
            return Task.FromResult(results);
        }

        foreach (string dllPath in dllFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var info = AssemblyMetadataReader.ReadAssemblyInfo(dllPath);
                if (info is null)
                {
                    // Non-.NET assembly or non-neutral culture — skip gracefully
                    continue;
                }

                string assemblyName = Path.GetFileNameWithoutExtension(dllPath);
                results[assemblyName] = info.AssemblyVersion.ToString();
            }
            catch (Exception)
            {
                // Skip assemblies that cannot be read (native DLLs, corrupted files, etc.)
            }
        }

        return Task.FromResult(results);
    }

    /// <summary>
    /// Finds the first existing output folder under the project directory.
    /// Checks bin/Debug, bin/Release, and bin/ in that order.
    /// Prefers the folder with the most recently modified DLL.
    /// </summary>
    /// <param name="projectDirectory">Path to the project directory.</param>
    /// <returns>Full path to the output folder, or <c>null</c> if none found.</returns>
    private static string? FindOutputFolder(string projectDirectory)
    {
        string? bestFolder = null;
        DateTime bestTimestamp = DateTime.MinValue;

        foreach (string subDir in OutputSubdirectories)
        {
            string candidate = Path.Combine(projectDirectory, subDir);
            if (!Directory.Exists(candidate))
            {
                continue;
            }

            try
            {
                string[] dlls = Directory.GetFiles(candidate, "*.dll", SearchOption.TopDirectoryOnly);
                if (dlls.Length == 0)
                {
                    continue;
                }

                // Pick the folder with the most recently modified DLL
                DateTime newest = dlls
                    .Select(f => File.GetLastWriteTimeUtc(f))
                    .Max();

                if (newest > bestTimestamp)
                {
                    bestTimestamp = newest;
                    bestFolder = candidate;
                }
            }
            catch (Exception)
            {
                // Access denied or other I/O error — skip this candidate
            }
        }

        return bestFolder;
    }
}
