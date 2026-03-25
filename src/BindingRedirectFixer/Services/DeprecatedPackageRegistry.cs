using System.Collections.Frozen;
using BindingRedirectFixer.Models;

namespace BindingRedirectFixer.Services;

/// <summary>
/// Built-in registry of deprecated NuGet packages that should be replaced
/// rather than having their binding redirects fixed.
/// </summary>
public static class DeprecatedPackageRegistry
{
    private static readonly FrozenDictionary<string, DeprecatedPackageInfo> Registry =
        new Dictionary<string, DeprecatedPackageInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft.Azure.Services.AppAuthentication"] = new(
                "Microsoft.Azure.Services.AppAuthentication",
                "Azure.Identity",
                "https://learn.microsoft.com/dotnet/api/overview/azure/app-auth-migration"),

            ["WindowsAzure.Storage"] = new(
                "WindowsAzure.Storage",
                "Azure.Storage.Blobs",
                "https://learn.microsoft.com/dotnet/azure/sdk/packages#deprecated-packages"),

            ["Microsoft.Azure.KeyVault"] = new(
                "Microsoft.Azure.KeyVault",
                "Azure.Security.KeyVault.Secrets",
                "https://learn.microsoft.com/dotnet/azure/sdk/packages#deprecated-packages"),

            ["Microsoft.Azure.ServiceBus"] = new(
                "Microsoft.Azure.ServiceBus",
                "Azure.Messaging.ServiceBus",
                "https://learn.microsoft.com/dotnet/azure/sdk/packages#deprecated-packages"),

            ["Microsoft.Azure.EventHubs"] = new(
                "Microsoft.Azure.EventHubs",
                "Azure.Messaging.EventHubs",
                "https://learn.microsoft.com/dotnet/azure/sdk/packages#deprecated-packages"),

            ["Microsoft.Azure.DocumentDB.Core"] = new(
                "Microsoft.Azure.DocumentDB.Core",
                "Microsoft.Azure.Cosmos",
                "https://learn.microsoft.com/dotnet/azure/sdk/packages#deprecated-packages"),
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks whether the given assembly name belongs to a deprecated package.
    /// </summary>
    /// <param name="assemblyName">The assembly name to look up.</param>
    /// <param name="info">The deprecation info if found; null otherwise.</param>
    /// <returns>True if the assembly is deprecated.</returns>
    public static bool TryGetDeprecation(string assemblyName, out DeprecatedPackageInfo? info)
    {
        return Registry.TryGetValue(assemblyName, out info);
    }
}
