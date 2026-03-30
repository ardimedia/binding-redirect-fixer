using BindingRedirectFixer.Services;

namespace BindingRedirectFixer.Tests.Services;

[TestClass]
public class DeprecatedPackageRegistryTests
{
    [TestMethod]
    [TestCategory("Unit")]
    public void TryGetDeprecation_KnownPackage_ReturnsTrue()
    {
        bool result = DeprecatedPackageRegistry.TryGetDeprecation(
            "Microsoft.Azure.Services.AppAuthentication", out var info);

        Assert.IsTrue(result);
        Assert.IsNotNull(info);
        Assert.AreEqual("Azure.Identity", info.ReplacementPackage);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void TryGetDeprecation_KnownPackage_HasMigrationUrl()
    {
        DeprecatedPackageRegistry.TryGetDeprecation(
            "Microsoft.Azure.Services.AppAuthentication", out var info);

        Assert.IsNotNull(info?.MigrationUrl);
        Assert.IsTrue(info.MigrationUrl.StartsWith("https://"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void TryGetDeprecation_UnknownPackage_ReturnsFalse()
    {
        bool result = DeprecatedPackageRegistry.TryGetDeprecation(
            "Newtonsoft.Json", out var info);

        Assert.IsFalse(result);
        Assert.IsNull(info);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void TryGetDeprecation_CaseInsensitive()
    {
        bool result = DeprecatedPackageRegistry.TryGetDeprecation(
            "microsoft.azure.services.appauthentication", out var info);

        Assert.IsTrue(result);
        Assert.IsNotNull(info);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void TryGetDeprecation_WindowsAzureStorage_ReturnsCorrectReplacement()
    {
        DeprecatedPackageRegistry.TryGetDeprecation("WindowsAzure.Storage", out var info);

        Assert.IsNotNull(info);
        Assert.AreEqual("Azure.Storage.Blobs", info.ReplacementPackage);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void TryGetDeprecation_AllRegisteredPackages_HaveReplacements()
    {
        string[] knownPackages =
        [
            "Microsoft.Azure.Services.AppAuthentication",
            "WindowsAzure.Storage",
            "Microsoft.Azure.KeyVault",
            "Microsoft.Azure.ServiceBus",
            "Microsoft.Azure.EventHubs",
            "Microsoft.Azure.DocumentDB.Core"
        ];

        foreach (string package in knownPackages)
        {
            bool result = DeprecatedPackageRegistry.TryGetDeprecation(package, out var info);
            Assert.IsTrue(result, $"Expected {package} to be registered as deprecated");
            Assert.IsFalse(string.IsNullOrEmpty(info?.ReplacementPackage),
                $"Expected {package} to have a replacement package");
        }
    }
}
