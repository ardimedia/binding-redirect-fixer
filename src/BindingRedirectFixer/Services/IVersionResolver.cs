namespace BindingRedirectFixer.Services;

/// <summary>
/// Abstraction for resolving assembly versions from NuGet packages.
/// Two implementations: <see cref="AssetsJsonVersionResolver"/> (PackageReference)
/// and <see cref="PackagesConfigVersionResolver"/> (packages.config).
/// </summary>
public interface IVersionResolver
{
    /// <summary>
    /// Resolves all assembly versions for packages in the given project.
    /// </summary>
    /// <param name="projectDirectory">Path to the project directory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Dictionary mapping assembly name to resolved info
    /// (assemblyVersion, packageVersion, publicKeyToken, culture).
    /// </returns>
    Task<Dictionary<string, ResolvedAssemblyInfo>> ResolveAsync(
        string projectDirectory,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of resolving a single assembly's version info from NuGet.
/// </summary>
/// <param name="AssemblyName">Simple assembly name (e.g., "Newtonsoft.Json").</param>
/// <param name="AssemblyVersion">Four-part assembly version from metadata (e.g., "13.0.0.0").</param>
/// <param name="PackageVersion">NuGet package version (e.g., "13.0.3").</param>
/// <param name="PublicKeyToken">Hex public key token from the assembly strong name.</param>
/// <param name="Culture">Assembly culture, typically "neutral".</param>
public record ResolvedAssemblyInfo(
    string AssemblyName,
    string AssemblyVersion,
    string PackageVersion,
    string PublicKeyToken,
    string Culture
);
