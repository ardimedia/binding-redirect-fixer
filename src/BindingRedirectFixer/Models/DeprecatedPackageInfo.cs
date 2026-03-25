namespace BindingRedirectFixer.Models;

/// <summary>
/// Information about a deprecated NuGet package and its modern replacement.
/// </summary>
/// <param name="DeprecatedPackage">The deprecated package/assembly name.</param>
/// <param name="ReplacementPackage">The recommended replacement package.</param>
/// <param name="MigrationUrl">Optional URL to official migration documentation.</param>
public sealed record DeprecatedPackageInfo(
    string DeprecatedPackage,
    string ReplacementPackage,
    string? MigrationUrl = null);
