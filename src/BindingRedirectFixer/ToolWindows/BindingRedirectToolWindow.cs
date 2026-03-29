namespace BindingRedirectFixer.ToolWindows;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

/// <summary>
/// Tool window provider for the Binding Redirect Fixer panel.
/// Displays a multi-source version grid with Issues and Learn tabs.
/// </summary>
[VisualStudioContribution]
public class BindingRedirectToolWindow : ToolWindow
{
    private BindingRedirectToolWindowViewModel? _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="BindingRedirectToolWindow"/> class.
    /// </summary>
    /// <param name="extensibility">The extensibility object.</param>
    public BindingRedirectToolWindow(VisualStudioExtensibility extensibility)
        : base(extensibility)
    {
        Title = "Binding Redirect Fixer";
    }

    /// <inheritdoc />
    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.DocumentWell,
    };

    /// <inheritdoc />
    public override async Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
    {
        _viewModel = new BindingRedirectToolWindowViewModel(Extensibility);
        _viewModel.Initialize();

        return new BindingRedirectToolWindowControl(_viewModel);
    }

    /// <inheritdoc />
    public override Task OnHideAsync(CancellationToken cancellationToken)
    {
        _viewModel?.Dispose();
        return base.OnHideAsync(cancellationToken);
    }
}
