namespace BindingRedirectFixer.ToolWindows;

using Microsoft.VisualStudio.Extensibility.UI;

/// <summary>
/// Remote UI control for the Binding Redirect Fixer tool window.
/// The XAML is embedded as a resource and rendered in the Visual Studio process.
/// </summary>
public class BindingRedirectToolWindowControl : RemoteUserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BindingRedirectToolWindowControl"/> class.
    /// </summary>
    /// <param name="dataContext">The ViewModel serving as the data context for Remote UI binding.</param>
    public BindingRedirectToolWindowControl(object? dataContext)
        : base(dataContext)
    {
    }
}
