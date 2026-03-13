namespace BindingRedirectFixer.Commands;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;
using Microsoft.VisualStudio.Extensibility.Shell;

using BindingRedirectFixer.ToolWindows;

/// <summary>
/// Command that triggers a binding redirect scan and opens the tool window.
/// Placed in the Tools menu as "Fix Binding Redirects".
/// </summary>
[VisualStudioContribution]
public class ScanCommand : Command
{
    /// <inheritdoc />
    #pragma warning disable CEE0027 // String not localized — single-language extension
    public override CommandConfiguration CommandConfiguration => new("Binding Redirect Fixer")
    {
        TooltipText = "Analyse all projects for stale or missing assembly binding redirects",
        Icon = new(ImageMoniker.Custom("BindingRedirectFixer"), IconSettings.IconAndText),
        Placements = [CommandPlacement.KnownPlacements.ToolsMenu],
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ScanCommand"/> class.
    /// </summary>
    /// <param name="extensibility">The extensibility object.</param>
    public ScanCommand(VisualStudioExtensibility extensibility)
        : base(extensibility)
    {
    }

    /// <inheritdoc />
    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        // Show the tool window; the tool window's ViewModel handles scanning on activation.
        await Extensibility.Shell().ShowToolWindowAsync<BindingRedirectToolWindow>(activate: true, cancellationToken);
    }
}
