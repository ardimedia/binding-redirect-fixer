namespace BindingRedirectFixer;

using Microsoft.VisualStudio.Extensibility;

/// <summary>
/// Entry point for the Binding Redirect Fixer extension.
/// </summary>
[VisualStudioContribution]
public class BindingRedirectFixerExtension : Extension
{
    /// <inheritdoc />
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
            id: "BindingRedirectFixer.A1B2C3D4-E5F6-7890-ABCD-EF1234567890",
            version: ExtensionAssemblyVersion,
            publisherName: "Ardimedia",
            displayName: "Binding Redirect Fixer",
            description: "Automatically detects and repairs stale or missing assembly binding redirects in .NET Framework projects.")
        {
            DotnetTargetVersions = [".net10.0"],
            Icon = ImageMoniker.Custom("Images/BindingRedirectFixer.128.128.png"),
        },
    };
}
