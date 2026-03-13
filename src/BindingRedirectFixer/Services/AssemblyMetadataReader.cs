using System.Reflection;

namespace BindingRedirectFixer.Services;

/// <summary>
/// Reads assembly metadata (version, public key token, culture) from a DLL
/// without locking the file, using <see cref="MetadataLoadContext"/>.
/// </summary>
public static class AssemblyMetadataReader
{
    /// <summary>
    /// Information extracted from an assembly's metadata.
    /// </summary>
    /// <param name="AssemblyVersion">Four-part assembly version.</param>
    /// <param name="PublicKeyToken">Hex-encoded public key token, or empty string if unsigned.</param>
    /// <param name="Culture">Assembly culture string; "neutral" when not culture-specific.</param>
    public record AssemblyInfo(Version AssemblyVersion, string PublicKeyToken, string Culture);

    /// <summary>
    /// Reads assembly version, public key token, and culture from the specified DLL
    /// using <see cref="MetadataLoadContext"/> to avoid file locks.
    /// </summary>
    /// <param name="dllPath">Full path to the assembly DLL.</param>
    /// <returns>
    /// An <see cref="AssemblyInfo"/> instance, or <c>null</c> if the assembly
    /// has a non-neutral culture or cannot be read.
    /// </returns>
    public static AssemblyInfo? ReadAssemblyInfo(string dllPath)
    {
        if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
        {
            return null;
        }

        try
        {
            return ReadWithMetadataLoadContext(dllPath);
        }
        catch (FileLoadException)
        {
            return null;
        }
        catch (IOException)
        {
            // Fall back to AssemblyName which may briefly lock the file
            return ReadWithAssemblyName(dllPath);
        }
        catch (BadImageFormatException)
        {
            // Not a valid .NET assembly (native DLL, etc.)
            return null;
        }
    }

    /// <summary>
    /// Primary reader using <see cref="MetadataLoadContext"/> — does not lock the file.
    /// </summary>
    private static AssemblyInfo? ReadWithMetadataLoadContext(string dllPath)
    {
        string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        string[] runtimeAssemblies = Directory.GetFiles(runtimeDirectory, "*.dll");

        // Include the target DLL's directory so its dependencies can be found if needed
        string targetDirectory = Path.GetDirectoryName(dllPath)!;
        string[] targetAssemblies = Directory.GetFiles(targetDirectory, "*.dll");

        var paths = new HashSet<string>(runtimeAssemblies, StringComparer.OrdinalIgnoreCase);
        foreach (string path in targetAssemblies)
        {
            paths.Add(path);
        }

        // Ensure the target DLL itself is in the resolver paths
        paths.Add(dllPath);

        var resolver = new PathAssemblyResolver(paths);
        using var context = new MetadataLoadContext(resolver);

        Assembly assembly = context.LoadFromAssemblyPath(dllPath);
        AssemblyName name = assembly.GetName();

        string culture = string.IsNullOrEmpty(name.CultureName) ? "neutral" : name.CultureName;

        // Only process neutral-culture assemblies
        if (!string.Equals(culture, "neutral", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Version version = name.Version ?? new Version(0, 0, 0, 0);
        string publicKeyToken = FormatPublicKeyToken(name.GetPublicKeyToken());

        return new AssemblyInfo(version, publicKeyToken, culture);
    }

    /// <summary>
    /// Fallback reader using <see cref="System.Reflection.AssemblyName.GetAssemblyName(string)"/>.
    /// May briefly lock the file but works when <see cref="MetadataLoadContext"/> fails.
    /// </summary>
    private static AssemblyInfo? ReadWithAssemblyName(string dllPath)
    {
        try
        {
            AssemblyName name = System.Reflection.AssemblyName.GetAssemblyName(dllPath);

            string culture = string.IsNullOrEmpty(name.CultureName) ? "neutral" : name.CultureName;

            if (!string.Equals(culture, "neutral", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            Version version = name.Version ?? new Version(0, 0, 0, 0);
            string publicKeyToken = FormatPublicKeyToken(name.GetPublicKeyToken());

            return new AssemblyInfo(version, publicKeyToken, culture);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a public key token byte array to a lowercase hex string.
    /// </summary>
    private static string FormatPublicKeyToken(byte[]? token)
    {
        if (token is null || token.Length == 0)
        {
            return string.Empty;
        }

        return Convert.ToHexString(token).ToLowerInvariant();
    }
}
