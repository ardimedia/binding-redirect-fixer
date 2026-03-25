namespace BindingRedirectFixer.ToolWindows;

using System.Runtime.Serialization;

using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.UI;
using Microsoft.VisualStudio.ProjectSystem.Query;

using BindingRedirectFixer.Models;
using BindingRedirectFixer.Services;
using Microsoft.VisualStudio.Extensibility.Shell;

/// <summary>
/// ViewModel for the Binding Redirect Fixer tool window.
/// Uses [DataContract] for Remote UI proxy binding between the extension process and VS process.
/// </summary>
[DataContract]
public class BindingRedirectToolWindowViewModel : NotifyPropertyChangedObject
{
    private readonly VisualStudioExtensibility _extensibility;
    private readonly BindingRedirectAnalyzer _analyzer = new();
    private readonly ConfigPatcher _configPatcher = new();

    /// <summary>
    /// Stores the full unfiltered scan results so project filtering can work without re-scanning.
    /// </summary>
    private List<AssemblyRedirectInfo> _allResults = [];

    /// <summary>
    /// Stores project directory paths keyed by project name, for fix operations.
    /// </summary>
    private readonly Dictionary<string, string> _projectDirectories = new(StringComparer.OrdinalIgnoreCase);

    private string _selectedProject = "All Projects";
    private string _selectedStatus = "All";
    private string _assemblyFilter = string.Empty;
    private string _sortColumn = "Project";
    private bool _sortAscending = true;
    private AssemblyRedirectInfoViewModel? _selectedIssue;
    private bool _isScanning;
    private bool _showInfoBar = true;
    private string _statusText = "Ready. Click Analyse to examine binding redirects.";
    private bool _isIssuesTabSelected = true;
    private bool _isLearnTabSelected;
    private bool _isFeedbackTabSelected;
    private bool _createBackup = true;

    /// <summary>
    /// Fingerprint of the last analysed solution (sorted project paths hash).
    /// Used to detect when a different solution has been loaded.
    /// </summary>
    private string _lastSolutionFingerprint = string.Empty;

    private CancellationTokenSource? _monitorCts;
    private CancellationTokenSource? _filterDebounceCts;

    /// <summary>
    /// Tracks the last observed write time of bin/ folders to detect builds.
    /// </summary>
    private DateTime _lastBinFolderWriteTime;

    /// <summary>
    /// Cooldown period after an analysis completes, during which bin/ changes are ignored.
    /// Prevents duplicate analysis when VS does a background build shortly after solution open.
    /// </summary>
    private DateTime _analysisCooldownUntil = DateTime.MinValue;
    private static readonly TimeSpan AnalysisCooldown = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Initializes a new instance of the <see cref="BindingRedirectToolWindowViewModel"/> class.
    /// </summary>
    /// <param name="extensibility">The extensibility object for accessing VS services.</param>
    public BindingRedirectToolWindowViewModel(VisualStudioExtensibility extensibility)
    {
        _extensibility = extensibility;

        // Load persisted user settings
        var settings = UserSettingsService.Load();
        _createBackup = settings.CreateBackup;
        _detailSplitRatio = Math.Clamp(settings.DetailSplitRatio, 0.15, 0.85);

        Issues = [];
        Projects = ["All Projects"];
        Statuses = ["All", "Issues Only", "Stale", "Missing", "Mismatch", "Duplicate", "Conflict", "Token Lost", "Deprecated", "OK"];

        AnalyseCommand = new AsyncCommand(ExecuteAnalyseAsync);
        FixAllCommand = new AsyncCommand(ExecuteFixAllAsync);
        RefreshCommand = new AsyncCommand(ExecuteRefreshAsync);
        DismissInfoBarCommand = new AsyncCommand(ExecuteDismissInfoBarAsync);
        SwitchToIssuesTabCommand = new AsyncCommand(ExecuteSwitchToIssuesTabAsync);
        SwitchToLearnTabCommand = new AsyncCommand(ExecuteSwitchToLearnTabAsync);
        SwitchToFeedbackTabCommand = new AsyncCommand(ExecuteSwitchToFeedbackTabAsync);
        CopyToFeedbackCommand = new AsyncCommand(ExecuteCopyToFeedbackAsync);
        OpenGitHubIssuesCommand = new AsyncCommand(ExecuteOpenGitHubIssuesAsync);
        FixSelectedCommand = new AsyncCommand(ExecuteFixSelectedAsync);
        FilterByProjectCommand = new AsyncCommand(ExecuteFilterByProjectAsync);
        FilterByStatusCommand = new AsyncCommand(ExecuteFilterByStatusAsync);
        ApplyAssemblyFilterCommand = new AsyncCommand(ExecuteApplyAssemblyFilterAsync);
        ClearAssemblyFilterCommand = new AsyncCommand(ExecuteClearAssemblyFilterAsync);
        SplitLeftCommand = new AsyncCommand(ExecuteSplitLeftAsync);
        SplitRightCommand = new AsyncCommand(ExecuteSplitRightAsync);
        SortCommand = new AsyncCommand(ExecuteSortAsync);
    }

    /// <summary>
    /// The list of detected binding redirect issues displayed in the grid.
    /// </summary>
    [DataMember]
    public ObservableList<AssemblyRedirectInfoViewModel> Issues { get; }

    /// <summary>
    /// Available project names for the filter dropdown.
    /// </summary>
    [DataMember]
    public ObservableList<string> Projects { get; }

    /// <summary>
    /// Currently selected project filter. Default is "All Projects".
    /// </summary>
    [DataMember]
    public string SelectedProject
    {
        get => _selectedProject;
        set
        {
            // Guard against null/empty from ComboBox losing selection
            string effective = string.IsNullOrEmpty(value) ? "All Projects" : value;
            if (SetProperty(ref _selectedProject, effective) && _allResults.Count > 0)
            {
                ApplyFilters();
            }
        }
    }

    /// <summary>
    /// Available status filter options.
    /// </summary>
    [DataMember]
    public ObservableList<string> Statuses { get; }

    /// <summary>
    /// Currently selected status filter. Default is "Issues Only" (hides OK).
    /// </summary>
    [DataMember]
    public string SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            // Guard against null/empty from ComboBox losing selection
            string effective = string.IsNullOrEmpty(value) ? "All" : value;
            if (SetProperty(ref _selectedStatus, effective) && _allResults.Count > 0)
            {
                ApplyFilters();
            }
        }
    }

    /// <summary>
    /// Text filter for assembly name (contains match, case-insensitive).
    /// Debounced: filters apply after 1.5 seconds of no further input,
    /// or immediately when the ApplyAssemblyFilterCommand is invoked (Enter key).
    /// </summary>
    [DataMember]
    public string AssemblyFilter
    {
        get => _assemblyFilter;
        set
        {
            if (SetProperty(ref _assemblyFilter, value))
            {
                RaiseNotifyPropertyChangedEvent(nameof(AssemblyFilterClearVisibility));
                if (_allResults.Count > 0)
                {
                    DebounceApplyFilters();
                }
            }
        }
    }

    /// <summary>Whether the clear button on the assembly filter should be visible.</summary>
    [DataMember]
    public bool AssemblyFilterClearVisibility => !string.IsNullOrEmpty(_assemblyFilter);

    /// <summary>
    /// The currently selected issue row for the detail panel.
    /// </summary>
    [DataMember]
    public AssemblyRedirectInfoViewModel? SelectedIssue
    {
        get => _selectedIssue;
        set
        {
            if (SetProperty(ref _selectedIssue, value))
            {
                FixChangeLog = string.Empty;
                LoadConfigSnippet();
                RaiseNotifyPropertyChangedEvent(nameof(DetailPanelVisibility));
                RaiseNotifyPropertyChangedEvent(nameof(ActionButtonVisibility));
                RaiseNotifyPropertyChangedEvent(nameof(FixChangeLogVisibility));
                RaiseNotifyPropertyChangedEvent(nameof(ConfigSnippetVisibility));
            }
        }
    }

    /// <summary>
    /// Indicates whether a scan is currently in progress.
    /// </summary>
    [DataMember]
    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (SetProperty(ref _isScanning, value))
            {
                RaiseNotifyPropertyChangedEvent(nameof(ScanningVisibility));
            }
        }
    }

    /// <summary>
    /// Controls visibility of the dismissible info bar explaining version priority.
    /// </summary>
    [DataMember]
    public bool ShowInfoBar
    {
        get => _showInfoBar;
        set
        {
            if (SetProperty(ref _showInfoBar, value))
            {
                RaiseNotifyPropertyChangedEvent(nameof(InfoBarVisibility));
            }
        }
    }

    /// <summary>
    /// Whether to create a timestamped backup of config files before applying fixes.
    /// </summary>
    [DataMember]
    public bool CreateBackup
    {
        get => _createBackup;
        set
        {
            if (SetProperty(ref _createBackup, value))
            {
                SaveSettings();
            }
        }
    }

    /// <summary>
    /// Status bar text displayed at the bottom of the tool window.
    /// </summary>
    [DataMember]
    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    /// <summary>
    /// Whether the Issues tab is currently selected.
    /// </summary>
    [DataMember]
    public bool IsIssuesTabSelected
    {
        get => _isIssuesTabSelected;
        set
        {
            if (SetProperty(ref _isIssuesTabSelected, value))
            {
                RaiseNotifyPropertyChangedEvent(nameof(IssuesTabVisibility));
                RaiseNotifyPropertyChangedEvent(nameof(IssuesTabFontWeight));
                RaiseNotifyPropertyChangedEvent(nameof(IssuesTabUnderline));
                RaiseNotifyPropertyChangedEvent(nameof(IssuesTabOpacity));
            }
        }
    }

    /// <summary>
    /// Whether the Learn tab is currently selected.
    /// </summary>
    [DataMember]
    public bool IsLearnTabSelected
    {
        get => _isLearnTabSelected;
        set
        {
            if (SetProperty(ref _isLearnTabSelected, value))
            {
                RaiseNotifyPropertyChangedEvent(nameof(LearnTabVisibility));
                RaiseNotifyPropertyChangedEvent(nameof(LearnTabFontWeight));
                RaiseNotifyPropertyChangedEvent(nameof(LearnTabUnderline));
                RaiseNotifyPropertyChangedEvent(nameof(LearnTabOpacity));
            }
        }
    }

    /// <summary>
    /// Whether the Feedback tab is currently selected.
    /// </summary>
    [DataMember]
    public bool IsFeedbackTabSelected
    {
        get => _isFeedbackTabSelected;
        set
        {
            if (SetProperty(ref _isFeedbackTabSelected, value))
            {
                RaiseNotifyPropertyChangedEvent(nameof(FeedbackTabVisibility));
                RaiseNotifyPropertyChangedEvent(nameof(FeedbackTabFontWeight));
                RaiseNotifyPropertyChangedEvent(nameof(FeedbackTabUnderline));
                RaiseNotifyPropertyChangedEvent(nameof(FeedbackTabOpacity));
            }
        }
    }

    // ---- Visibility properties for XAML binding (Remote UI cannot use converters) ----

    /// <summary>WPF Visibility string for the Issues tab content.</summary>
    [DataMember]
    public string IssuesTabVisibility => _isIssuesTabSelected ? "Visible" : "Collapsed";

    /// <summary>WPF Visibility string for the Learn tab content.</summary>
    [DataMember]
    public string LearnTabVisibility => _isLearnTabSelected ? "Visible" : "Collapsed";

    /// <summary>FontWeight for the Issues tab button.</summary>
    [DataMember]
    public string IssuesTabFontWeight => _isIssuesTabSelected ? "Bold" : "Normal";

    /// <summary>FontWeight for the Learn tab button.</summary>
    [DataMember]
    public string LearnTabFontWeight => _isLearnTabSelected ? "Bold" : "Normal";

    /// <summary>Bottom border thickness for the Issues tab underline indicator.</summary>
    [DataMember]
    public string IssuesTabUnderline => _isIssuesTabSelected ? "0,0,0,2" : "0";

    /// <summary>Bottom border thickness for the Learn tab underline indicator.</summary>
    [DataMember]
    public string LearnTabUnderline => _isLearnTabSelected ? "0,0,0,2" : "0";

    /// <summary>Opacity for the Issues tab button.</summary>
    [DataMember]
    public string IssuesTabOpacity => _isIssuesTabSelected ? "1.0" : "0.6";

    /// <summary>Opacity for the Learn tab button.</summary>
    [DataMember]
    public string LearnTabOpacity => _isLearnTabSelected ? "1.0" : "0.6";

    /// <summary>WPF Visibility string for the Feedback tab content.</summary>
    [DataMember]
    public string FeedbackTabVisibility => _isFeedbackTabSelected ? "Visible" : "Collapsed";

    /// <summary>FontWeight for the Feedback tab button.</summary>
    [DataMember]
    public string FeedbackTabFontWeight => _isFeedbackTabSelected ? "Bold" : "Normal";

    /// <summary>Bottom border thickness for the Feedback tab underline indicator.</summary>
    [DataMember]
    public string FeedbackTabUnderline => _isFeedbackTabSelected ? "0,0,0,2" : "0";

    /// <summary>Opacity for the Feedback tab button.</summary>
    [DataMember]
    public string FeedbackTabOpacity => _isFeedbackTabSelected ? "1.0" : "0.6";

    /// <summary>Pre-filled issue text for the Feedback tab.</summary>
    [DataMember]
    public string FeedbackText
    {
        get => _feedbackText;
        set
        {
            if (SetProperty(ref _feedbackText, value))
            {
                RaiseNotifyPropertyChangedEvent(nameof(FeedbackTextVisibility));
            }
        }
    }

    /// <summary>WPF Visibility for the feedback text area.</summary>
    [DataMember]
    public string FeedbackTextVisibility =>
        string.IsNullOrEmpty(_feedbackText) ? "Collapsed" : "Visible";

    /// <summary>WPF Visibility for the info bar.</summary>
    [DataMember]
    public string InfoBarVisibility => _showInfoBar ? "Visible" : "Collapsed";

    /// <summary>WPF Visibility for the scanning indicator.</summary>
    [DataMember]
    public string ScanningVisibility => _isScanning ? "Visible" : "Collapsed";

    /// <summary>WPF Visibility for the detail panel.</summary>
    [DataMember]
    public string DetailPanelVisibility =>
        _selectedIssue is not null ? "Visible" : "Collapsed";

    /// <summary>WPF Visibility for the action button in the detail panel.</summary>
    [DataMember]
    public string ActionButtonVisibility =>
        _selectedIssue is not null && _selectedIssue.HasAction ? "Visible" : "Collapsed";

    private string _feedbackText = string.Empty;
    private string _fixChangeLog = string.Empty;

    /// <summary>Description of the last fix applied (file path and what changed).</summary>
    [DataMember]
    public string FixChangeLog
    {
        get => _fixChangeLog;
        set => SetProperty(ref _fixChangeLog, value);
    }

    /// <summary>WPF Visibility for the fix change log area.</summary>
    [DataMember]
    public string FixChangeLogVisibility =>
        string.IsNullOrEmpty(_fixChangeLog) ? "Collapsed" : "Visible";

    private string _configSnippet = string.Empty;

    /// <summary>Current config file XML snippet for the selected assembly.</summary>
    [DataMember]
    public string ConfigSnippet
    {
        get => _configSnippet;
        set => SetProperty(ref _configSnippet, value);
    }

    /// <summary>WPF Visibility for the config snippet area.</summary>
    [DataMember]
    public string ConfigSnippetVisibility =>
        string.IsNullOrEmpty(_configSnippet) ? "Collapsed" : "Visible";

    private string _configFilePath = string.Empty;

    /// <summary>Full path to the config file for the selected assembly's project.</summary>
    [DataMember]
    public string ConfigFilePath
    {
        get => _configFilePath;
        set => SetProperty(ref _configFilePath, value);
    }

    private double _detailSplitRatio;

    /// <summary>
    /// Star width for the left (Version flow) panel, derived from the split ratio.
    /// Expressed as a string like "35" for use in XAML GridLength binding.
    /// </summary>
    [DataMember]
    public string LeftPanelWidth => $"{Math.Round(_detailSplitRatio * 100)}*";

    /// <summary>
    /// Star width for the right (Config entry) panel.
    /// </summary>
    [DataMember]
    public string RightPanelWidth => $"{Math.Round((1 - _detailSplitRatio) * 100)}*";

    // ---- Commands ----

    /// <summary>Command to trigger a full solution analysis for binding redirect issues.</summary>
    [DataMember]
    public IAsyncCommand AnalyseCommand { get; }

    /// <summary>Command to fix all detected Stale and Missing issues in one pass.</summary>
    [DataMember]
    public IAsyncCommand FixAllCommand { get; }

    /// <summary>Command to re-analyse the solution.</summary>
    [DataMember]
    public IAsyncCommand RefreshCommand { get; }

    /// <summary>Command to dismiss the info bar.</summary>
    [DataMember]
    public IAsyncCommand DismissInfoBarCommand { get; }

    /// <summary>Command to switch to the Issues tab.</summary>
    [DataMember]
    public IAsyncCommand SwitchToIssuesTabCommand { get; }

    /// <summary>Command to switch to the Learn tab.</summary>
    [DataMember]
    public IAsyncCommand SwitchToLearnTabCommand { get; }

    /// <summary>Command to switch to the Feedback tab.</summary>
    [DataMember]
    public IAsyncCommand SwitchToFeedbackTabCommand { get; }

    /// <summary>Command to copy the selected issue details to the Feedback tab.</summary>
    [DataMember]
    public IAsyncCommand CopyToFeedbackCommand { get; }

    /// <summary>Command to open the GitHub Issues page in the default browser.</summary>
    [DataMember]
    public IAsyncCommand OpenGitHubIssuesCommand { get; }

    /// <summary>Command to fix the currently selected issue only.</summary>
    [DataMember]
    public IAsyncCommand FixSelectedCommand { get; }

    /// <summary>Command to apply the project filter from the dropdown.</summary>
    [DataMember]
    public IAsyncCommand FilterByProjectCommand { get; }

    /// <summary>Command to apply the status filter from the dropdown.</summary>
    [DataMember]
    public IAsyncCommand FilterByStatusCommand { get; }

    /// <summary>Command to immediately apply the assembly name filter (bound to Enter key).</summary>
    [DataMember]
    public IAsyncCommand ApplyAssemblyFilterCommand { get; }

    /// <summary>Command to clear the assembly name filter.</summary>
    [DataMember]
    public IAsyncCommand ClearAssemblyFilterCommand { get; }

    /// <summary>Command to shift the detail split leftward (more space for right panel).</summary>
    [DataMember]
    public IAsyncCommand SplitLeftCommand { get; }

    /// <summary>Command to shift the detail split rightward (more space for left panel).</summary>
    [DataMember]
    public IAsyncCommand SplitRightCommand { get; }

    /// <summary>Command to sort by a column. The parameter is the column name.</summary>
    [DataMember]
    public IAsyncCommand SortCommand { get; }

    /// <summary>Sort indicator text for the Project column header.</summary>
    [DataMember]
    public string SortIndicatorProject => GetSortIndicator("Project");

    /// <summary>Sort indicator text for the Assembly column header.</summary>
    [DataMember]
    public string SortIndicatorAssembly => GetSortIndicator("Assembly");

    /// <summary>Sort indicator text for the Resolved column header.</summary>
    [DataMember]
    public string SortIndicatorResolved => GetSortIndicator("Resolved");

    /// <summary>Sort indicator text for the Requested column header.</summary>
    [DataMember]
    public string SortIndicatorRequested => GetSortIndicator("Requested");

    /// <summary>Sort indicator text for the bin/ DLL column header.</summary>
    [DataMember]
    public string SortIndicatorBinDll => GetSortIndicator("BinDll");

    /// <summary>Sort indicator text for the Config column header.</summary>
    [DataMember]
    public string SortIndicatorConfig => GetSortIndicator("Config");

    /// <summary>Sort indicator text for the Status column header.</summary>
    [DataMember]
    public string SortIndicatorStatus => GetSortIndicator("Status");

    private string GetSortIndicator(string column)
        => _sortColumn == column ? (_sortAscending ? " \u25B2" : " \u25BC") : "";

    // ---- Public methods ----

    /// <summary>
    /// Runs the initial analysis when the tool window is first shown,
    /// then starts a background monitor that re-analyses when the solution changes.
    /// Called from <see cref="BindingRedirectToolWindow.GetContentAsync"/>.
    /// </summary>
    public async Task RunInitialAnalysisAsync(CancellationToken cancellationToken)
    {
        // Let the UI render first before starting analysis
        StatusText = "Preparing analysis...";
        await Task.Delay(500, cancellationToken).ConfigureAwait(false);

        // Only run analysis if a solution is already loaded;
        // otherwise the monitor will detect when one opens and trigger it
        var fingerprint = await GetSolutionFingerprintAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(fingerprint))
        {
            await ExecuteAnalyseAsync(null, cancellationToken).ConfigureAwait(false);
            _lastBinFolderWriteTime = GetBinFoldersWriteTime();
        }
        else
        {
            StatusText = "Waiting for a solution to be opened...";
        }

        StartSolutionMonitor();
    }

    /// <summary>
    /// Clears all data and stops background monitoring.
    /// Called when the tool window is closed or the solution is closed.
    /// </summary>
    public void ClearData()
    {
        _monitorCts?.Cancel();
        _filterDebounceCts?.Cancel();

        Issues.Clear();
        _allResults.Clear();
        _projectDirectories.Clear();
        _lastSolutionFingerprint = string.Empty;
        _lastBinFolderWriteTime = DateTime.MinValue;

        // Keep "All Projects" in the list to avoid ComboBox losing selection
        while (Projects.Count > 1)
        {
            Projects.RemoveAt(Projects.Count - 1);
        }

        SelectedIssue = null;
        FixChangeLog = string.Empty;

        _selectedProject = "All Projects";
        _selectedStatus = "All";
        _assemblyFilter = string.Empty;
        RaiseNotifyPropertyChangedEvent(nameof(SelectedProject));
        RaiseNotifyPropertyChangedEvent(nameof(SelectedStatus));
        RaiseNotifyPropertyChangedEvent(nameof(AssemblyFilter));
        RaiseNotifyPropertyChangedEvent(nameof(AssemblyFilterClearVisibility));

        StatusText = "Ready. Click Analyse to examine binding redirects.";
    }

    /// <summary>
    /// Loads the raw XML snippet from the config file for the currently selected assembly.
    /// Also refreshes <c>CurrentRedirectVersion</c> from the live config file to detect
    /// external changes (e.g. manual edits, source control updates).
    /// </summary>
    private void LoadConfigSnippet()
    {
        if (_selectedIssue is null ||
            !_projectDirectories.TryGetValue(_selectedIssue.ProjectName, out string? projectDir))
        {
            ConfigSnippet = string.Empty;
            ConfigFilePath = string.Empty;
            return;
        }

        string? configPath = _configPatcher.GetConfigFilePath(projectDir);
        if (configPath is null)
        {
            ConfigSnippet = "(no config file found)";
            ConfigFilePath = string.Empty;
            return;
        }

        ConfigFilePath = configPath;

        string? xml = _configPatcher.ReadRedirectXml(configPath, _selectedIssue.Name);
        ConfigSnippet = xml ?? "(no binding redirect in config)";

        // Refresh the config version from the live file to catch external changes
        var liveRedirects = _configPatcher.ReadRedirects(configPath);
        if (liveRedirects.TryGetValue(_selectedIssue.Name, out var liveRedirect))
        {
            string liveVersion = liveRedirect.NewVersion;
            if (!string.Equals(_selectedIssue.CurrentRedirectVersion, liveVersion, StringComparison.OrdinalIgnoreCase) &&
                _selectedIssue.CurrentRedirectVersion != "\u2014") // ignore if dash (no redirect)
            {
                // Update the VM
                _selectedIssue.RefreshConfigVersion(liveVersion, _selectedIssue.ResolvedAssemblyVersion);

                // Also update the underlying model
                var model = _allResults.FirstOrDefault(r =>
                    string.Equals(r.Name, _selectedIssue.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(r.ProjectName, _selectedIssue.ProjectName, StringComparison.OrdinalIgnoreCase));
                if (model is not null)
                {
                    model.CurrentRedirectVersion = liveVersion;
                }
            }
        }
    }

    /// <summary>
    /// Starts a background task that periodically checks whether the loaded solution
    /// has changed (by comparing project paths) or whether a build has completed
    /// (by checking bin/ folder timestamps). When a change is detected, a new
    /// analysis is triggered automatically.
    /// </summary>
    private void StartSolutionMonitor()
    {
        _monitorCts?.Cancel();
        _monitorCts = new CancellationTokenSource();
        var token = _monitorCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);

                    if (_isScanning)
                    {
                        continue;
                    }

                    // Check for solution change or close
                    string currentFingerprint = await GetSolutionFingerprintAsync(token).ConfigureAwait(false);

                    // Solution was closed — clear all data
                    if (string.IsNullOrEmpty(currentFingerprint) &&
                        !string.IsNullOrEmpty(_lastSolutionFingerprint))
                    {
                        ClearData();
                        // Restart monitoring so we detect when a new solution opens
                        StartSolutionMonitor();
                        return;
                    }

                    // Different solution opened — re-analyse
                    if (!string.IsNullOrEmpty(currentFingerprint) &&
                        currentFingerprint != _lastSolutionFingerprint)
                    {
                        await ExecuteAnalyseAsync(null, token).ConfigureAwait(false);
                        _lastBinFolderWriteTime = GetBinFoldersWriteTime();
                        continue;
                    }

                    // Check for bin/ folder changes (indicates a build completed)
                    // Skip if still within the cooldown period after the last analysis
                    var currentBinTime = GetBinFoldersWriteTime();
                    if (currentBinTime > _lastBinFolderWriteTime && _projectDirectories.Count > 0)
                    {
                        _lastBinFolderWriteTime = currentBinTime;

                        if (DateTime.UtcNow < _analysisCooldownUntil)
                        {
                            continue;
                        }

                        StatusText = "Build detected. Re-analysing...";
                        await ExecuteAnalyseAsync(null, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Ignore transient errors in the monitor; retry on next cycle
                }
            }
        }, token);
    }

    /// <summary>
    /// Gets the latest write time across all known project bin/ folders.
    /// Used to detect when a build has completed and bin/ DLLs may have changed.
    /// </summary>
    private DateTime GetBinFoldersWriteTime()
    {
        var latest = DateTime.MinValue;

        foreach (string projectDir in _projectDirectories.Values)
        {
            try
            {
                string binDir = Path.Combine(projectDir, "bin");
                if (Directory.Exists(binDir))
                {
                    var dirTime = Directory.GetLastWriteTimeUtc(binDir);
                    if (dirTime > latest) latest = dirTime;

                    // Also check immediate subdirectories (bin/Debug, bin/Release)
                    foreach (string subDir in Directory.EnumerateDirectories(binDir))
                    {
                        var subTime = Directory.GetLastWriteTimeUtc(subDir);
                        if (subTime > latest) latest = subTime;
                    }
                }
            }
            catch
            {
                // Ignore inaccessible directories
            }
        }

        return latest;
    }

    /// <summary>
    /// Queries the current solution's project paths and returns a fingerprint string.
    /// Returns empty if no solution is loaded.
    /// </summary>
    private async Task<string> GetSolutionFingerprintAsync(CancellationToken cancellationToken)
    {
        var projects = await _extensibility.Workspaces().QueryProjectsAsync(
            q => q.With(p => p.Path),
            cancellationToken).ConfigureAwait(false);

        var paths = projects
            .Select(p => p.Path ?? string.Empty)
            .Where(p => !string.IsNullOrEmpty(p))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count == 0) return string.Empty;

        return string.Join("|", paths);
    }

    // ---- Command implementations ----

    private async Task ExecuteAnalyseAsync(object? parameter, CancellationToken cancellationToken)
    {
        IsScanning = true;
        StatusText = "Analysing solution for binding redirect issues...";

        try
        {
            Issues.Clear();
            _allResults.Clear();
            _projectDirectories.Clear();
            SelectedIssue = null;

            // Keep "All Projects" in the list to avoid ComboBox losing selection
            while (Projects.Count > 1)
            {
                Projects.RemoveAt(Projects.Count - 1);
            }

            // Reset filters to defaults (set backing fields to avoid triggering ApplyFilters)
            _selectedProject = "All Projects";
            _selectedStatus = "All";
            _assemblyFilter = string.Empty;
            RaiseNotifyPropertyChangedEvent(nameof(SelectedProject));
            RaiseNotifyPropertyChangedEvent(nameof(SelectedStatus));
            RaiseNotifyPropertyChangedEvent(nameof(AssemblyFilter));

            // Query all projects from the solution via VS workspace APIs
            var projects = await _extensibility.Workspaces().QueryProjectsAsync(
                q => q.With(p => p.Name).With(p => p.Path),
                cancellationToken).ConfigureAwait(false);

            if (!projects.Any())
            {
                _lastSolutionFingerprint = string.Empty;
                StatusText = "No projects found in the current solution.";
                return;
            }

            // Update solution fingerprint so the monitor knows this solution was analysed
            _lastSolutionFingerprint = string.Join("|",
                projects
                    .Select(p => p.Path ?? string.Empty)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase));

            int projectCount = 0;
            int totalIssues = 0;

            foreach (var project in projects)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string projectPath = project.Path ?? string.Empty;
                string projectName = project.Name ?? Path.GetFileNameWithoutExtension(projectPath);

                if (string.IsNullOrEmpty(projectPath))
                {
                    continue;
                }

                string projectDirectory = Path.GetDirectoryName(projectPath) ?? string.Empty;
                if (string.IsNullOrEmpty(projectDirectory))
                {
                    continue;
                }

                // Only analyse projects that have a config file (web.config/app.config)
                // or packages.config — these are .NET Framework projects that use binding redirects
                bool hasConfig = _configPatcher.GetConfigFilePath(projectDirectory) is not null;
                bool hasPackagesConfig = File.Exists(Path.Combine(projectDirectory, "packages.config"));
                bool hasAssetsJson = File.Exists(Path.Combine(projectDirectory, "obj", "project.assets.json"));

                if (!hasConfig && !hasPackagesConfig && !hasAssetsJson)
                {
                    continue;
                }

                _projectDirectories[projectName] = projectDirectory;
                Projects.Add(projectName);
                projectCount++;

                StatusText = $"Analysing {projectName}...";

                var results = await _analyzer.AnalyzeProjectAsync(
                    projectName, projectDirectory, cancellationToken).ConfigureAwait(false);

                _allResults.AddRange(results);
                totalIssues += results.Count(r => r.Status != RedirectStatus.OK);
            }

            // Apply current filters (default: "Issues Only" hides OK entries)
            ApplyFilters();
            StatusText = $"Analysis complete. {projectCount} project(s) scanned, {totalIssues} issue(s) found.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Analysis cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Analysis failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
            _analysisCooldownUntil = DateTime.UtcNow + AnalysisCooldown;
        }
    }

    private async Task ExecuteFixAllAsync(object? parameter, CancellationToken cancellationToken)
    {
        IsScanning = true;
        StatusText = "Applying fixes...";

        try
        {
            int fixedCount = 0;

            // Only fix issues currently shown (respects all active filters)
            var shownFixable = Issues
                .Where(vm => vm.SuggestedAction is FixAction.UpdateRedirect or FixAction.AddRedirect or FixAction.RemoveDuplicate or FixAction.RemoveRedirect)
                .Select(vm => (vm.ProjectName, vm.Name))
                .ToHashSet();

            // Apply config patches (UpdateRedirect + AddRedirect)
            var issuesByProject = _allResults
                .Where(r => shownFixable.Contains((r.ProjectName, r.Name)))
                .GroupBy(r => r.ProjectName);

            foreach (var group in issuesByProject)
            {
                if (!_projectDirectories.TryGetValue(group.Key, out string? projectDir))
                {
                    continue;
                }

                string? configPath = _configPatcher.GetConfigFilePath(projectDir);
                if (configPath is null)
                {
                    continue;
                }

                // Create one backup per project before patching
                if (_createBackup) _configPatcher.CreateBackup(configPath);

                foreach (var issue in group)
                {
                    string targetVersion = issue.EffectiveTargetVersion ?? string.Empty;
                    bool success = issue.SuggestedAction switch
                    {
                        FixAction.UpdateRedirect => _configPatcher.UpdateRedirect(
                            configPath, issue.Name, issue.PublicKeyToken, issue.Culture,
                            targetVersion),
                        FixAction.AddRedirect => _configPatcher.AddRedirect(
                            configPath, issue.Name, issue.PublicKeyToken, issue.Culture,
                            targetVersion),
                        FixAction.RemoveDuplicate => _configPatcher.RemoveDuplicateRedirects(
                            configPath, issue.Name, targetVersion),
                        FixAction.RemoveRedirect => _configPatcher.RemoveRedirect(
                            configPath, issue.Name),
                        _ => false
                    };

                    if (success)
                    {
                        fixedCount++;

                        // Update the underlying model in-place
                        issue.CurrentRedirectVersion = issue.SuggestedAction == FixAction.RemoveRedirect
                            ? null : targetVersion;
                        issue.Status = RedirectStatus.OK;
                        issue.DiagnosticMessage = string.Empty;
                        issue.SuggestedAction = FixAction.None;

                        // Update the matching row VM in-place (no full re-analysis)
                        string displayVersion = issue.CurrentRedirectVersion ?? "\u2014";
                        var rowVm = Issues.FirstOrDefault(vm =>
                            string.Equals(vm.Name, issue.Name, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(vm.ProjectName, issue.ProjectName, StringComparison.OrdinalIgnoreCase));
                        rowVm?.MarkAsFixed(displayVersion);
                    }
                }
            }

            int issueCount = Issues.Count(i => i.Status != RedirectStatus.OK);
            StatusText = $"Fixed {fixedCount} redirect(s). Showing {Issues.Count} result(s), {issueCount} issue(s).";
        }
        catch (Exception ex)
        {
            StatusText = $"Fix failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private async Task ExecuteRefreshAsync(object? parameter, CancellationToken cancellationToken)
    {
        Issues.Clear();
        SelectedIssue = null;
        await ExecuteAnalyseAsync(parameter, cancellationToken).ConfigureAwait(false);
    }

    private Task ExecuteDismissInfoBarAsync(object? parameter, CancellationToken cancellationToken)
    {
        ShowInfoBar = false;
        return Task.CompletedTask;
    }

    private Task ExecuteSwitchToIssuesTabAsync(object? parameter, CancellationToken cancellationToken)
    {
        IsIssuesTabSelected = true;
        IsLearnTabSelected = false;
        IsFeedbackTabSelected = false;
        return Task.CompletedTask;
    }

    private Task ExecuteSwitchToLearnTabAsync(object? parameter, CancellationToken cancellationToken)
    {
        IsIssuesTabSelected = false;
        IsLearnTabSelected = true;
        IsFeedbackTabSelected = false;
        return Task.CompletedTask;
    }

    private Task ExecuteSwitchToFeedbackTabAsync(object? parameter, CancellationToken cancellationToken)
    {
        IsIssuesTabSelected = false;
        IsLearnTabSelected = false;
        IsFeedbackTabSelected = true;
        return Task.CompletedTask;
    }

    private Task ExecuteCopyToFeedbackAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (SelectedIssue is null)
        {
            return Task.CompletedTask;
        }

        var vm = SelectedIssue;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## Assembly Details");
        sb.AppendLine();
        sb.AppendLine($"- **Assembly:** {vm.Name}");
        sb.AppendLine($"- **Status:** {vm.StatusDisplay}");
        sb.AppendLine($"- **NuGet Resolved:** {vm.ResolvedAssemblyVersion}");
        sb.AppendLine($"- **Requested:** {vm.RequestedVersion}");
        sb.AppendLine($"- **bin/ DLL:** {vm.PhysicalVersion}");
        sb.AppendLine($"- **Config Redirect:** {vm.CurrentRedirectVersion}");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(vm.DiagnosticMessage))
        {
            sb.AppendLine("### Diagnostic");
            sb.AppendLine();
            sb.AppendLine(vm.DiagnosticMessage);
            sb.AppendLine();
        }
        if (!string.IsNullOrEmpty(ConfigSnippet) && ConfigSnippet != "(no binding redirect in config)")
        {
            sb.AppendLine("### Config Entry");
            sb.AppendLine();
            sb.AppendLine("```xml");
            sb.AppendLine(ConfigSnippet);
            sb.AppendLine("```");
        }
        else
        {
            string configName = !string.IsNullOrEmpty(ConfigFilePath)
                ? Path.GetFileName(ConfigFilePath)
                : "web.config";
            sb.AppendLine($"_No binding redirect entry in {configName}._");
            sb.AppendLine();
        }

        FeedbackText = sb.ToString();

        // Switch to Feedback tab
        IsIssuesTabSelected = false;
        IsLearnTabSelected = false;
        IsFeedbackTabSelected = true;

        return Task.CompletedTask;
    }

    private Task ExecuteOpenGitHubIssuesAsync(object? parameter, CancellationToken cancellationToken)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "https://github.com/ardimedia/binding-redirect-fixer/issues",
            UseShellExecute = true
        });
        return Task.CompletedTask;
    }

    private Task ExecuteFixSelectedAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (SelectedIssue is null)
        {
            return Task.CompletedTask;
        }

        StatusText = $"Fixing {SelectedIssue.Name}...";

        try
        {
            // Find the matching model from _allResults
            var model = _allResults.FirstOrDefault(r =>
                string.Equals(r.Name, SelectedIssue.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.ProjectName, SelectedIssue.ProjectName, StringComparison.OrdinalIgnoreCase));

            if (model is null || !_projectDirectories.TryGetValue(model.ProjectName, out string? projectDir))
            {
                StatusText = $"Could not find project directory for {SelectedIssue.Name}.";
                return Task.CompletedTask;
            }

            string? configPath = _configPatcher.GetConfigFilePath(projectDir);
            if (configPath is null)
            {
                StatusText = $"No config file found for project {model.ProjectName}.";
                return Task.CompletedTask;
            }

            if (_createBackup) _configPatcher.CreateBackup(configPath);

            string targetVersion = model.EffectiveTargetVersion ?? string.Empty;
            bool success = model.SuggestedAction switch
            {
                FixAction.UpdateRedirect => _configPatcher.UpdateRedirect(
                    configPath, model.Name, model.PublicKeyToken, model.Culture,
                    targetVersion),
                FixAction.AddRedirect => _configPatcher.AddRedirect(
                    configPath, model.Name, model.PublicKeyToken, model.Culture,
                    targetVersion),
                FixAction.RemoveDuplicate => _configPatcher.RemoveDuplicateRedirects(
                    configPath, model.Name, targetVersion),
                FixAction.RemoveRedirect => _configPatcher.RemoveRedirect(
                    configPath, model.Name),
                _ => false
            };

            if (success)
            {
                bool isRemoval = model.SuggestedAction == FixAction.RemoveRedirect;
                string fixedVersion = isRemoval ? "\u2014" : targetVersion;
                string oldVersion = SelectedIssue.CurrentRedirectVersion;
                string actionVerb = model.SuggestedAction switch
                {
                    FixAction.AddRedirect => "Added",
                    FixAction.RemoveDuplicate => "Deduplicated",
                    FixAction.RemoveRedirect => "Removed",
                    _ => "Updated"
                };

                // Update the underlying model
                model.CurrentRedirectVersion = isRemoval ? null : targetVersion;
                model.Status = RedirectStatus.OK;
                model.DiagnosticMessage = string.Empty;
                model.SuggestedAction = FixAction.None;

                // Update the row VM in-place (no full re-analysis)
                SelectedIssue.MarkAsFixed(fixedVersion);

                // Build change log
                FixChangeLog = actionVerb == "Removed"
                    ? $"File: {configPath}\n"
                        + $"Removed binding redirect for {model.Name}.\n"
                        + $"  (was: newVersion=\"{oldVersion}\")\n"
                        + $"  The redirect targeted a version not on disk. The runtime will load\n"
                        + $"  the {model.PhysicalVersion} DLL from bin/ without a redirect."
                    : $"File: {configPath}\n"
                        + $"{actionVerb} binding redirect for {model.Name}:\n"
                        + $"  oldVersion=\"0.0.0.0-{fixedVersion}\"\n"
                        + $"  newVersion=\"{fixedVersion}\"\n"
                        + (actionVerb == "Deduplicated" ? $"  (removed duplicate entries, kept one targeting {fixedVersion})"
                            : actionVerb == "Added" ? $"  (new entry added)"
                            : $"  (was: newVersion=\"{oldVersion}\")")
                        + (!string.IsNullOrEmpty(model.ConfigPublicKeyToken) &&
                           string.Equals(model.PublicKeyToken, model.ConfigPublicKeyToken, StringComparison.OrdinalIgnoreCase)
                            ? $"\n  Public key token preserved as \"{model.ConfigPublicKeyToken}\" (resolved DLL was unsigned)."
                            : string.Empty);
                RaiseNotifyPropertyChangedEvent(nameof(FixChangeLogVisibility));

                // Reload the config snippet to show the updated XML
                LoadConfigSnippet();
                RaiseNotifyPropertyChangedEvent(nameof(ConfigSnippetVisibility));

                // Refresh detail panel visibility (action button should hide)
                RaiseNotifyPropertyChangedEvent(nameof(DetailPanelVisibility));
                RaiseNotifyPropertyChangedEvent(nameof(ActionButtonVisibility));

                // Update status bar counts
                int issueCount = Issues.Count(i => i.Status != RedirectStatus.OK);
                StatusText = $"Fixed {SelectedIssue.Name}. Showing {Issues.Count} result(s), {issueCount} issue(s).";
            }
            else
            {
                StatusText = $"Could not apply fix for {SelectedIssue.Name}.";
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Fix failed for {SelectedIssue.Name}: {ex.Message}";
        }

        return Task.CompletedTask;
    }

    private Task ExecuteFilterByProjectAsync(object? parameter, CancellationToken cancellationToken)
    {
        ApplyFilters();
        return Task.CompletedTask;
    }

    private Task ExecuteFilterByStatusAsync(object? parameter, CancellationToken cancellationToken)
    {
        ApplyFilters();
        return Task.CompletedTask;
    }

    private async Task ExecuteApplyAssemblyFilterAsync(object? parameter, CancellationToken cancellationToken)
    {
        // Cancel any pending debounce and apply immediately
        if (_filterDebounceCts is not null)
        {
            await _filterDebounceCts.CancelAsync().ConfigureAwait(false);
        }

        ApplyFilters();
    }

    private async Task ExecuteClearAssemblyFilterAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (_filterDebounceCts is not null)
        {
            await _filterDebounceCts.CancelAsync().ConfigureAwait(false);
        }

        AssemblyFilter = string.Empty;
        ApplyFilters();
    }

    private Task ExecuteSplitLeftAsync(object? parameter, CancellationToken cancellationToken)
    {
        _detailSplitRatio = Math.Max(0.15, _detailSplitRatio - 0.10);
        RaiseNotifyPropertyChangedEvent(nameof(LeftPanelWidth));
        RaiseNotifyPropertyChangedEvent(nameof(RightPanelWidth));
        SaveSettings();
        return Task.CompletedTask;
    }

    private Task ExecuteSplitRightAsync(object? parameter, CancellationToken cancellationToken)
    {
        _detailSplitRatio = Math.Min(0.85, _detailSplitRatio + 0.10);
        RaiseNotifyPropertyChangedEvent(nameof(LeftPanelWidth));
        RaiseNotifyPropertyChangedEvent(nameof(RightPanelWidth));
        SaveSettings();
        return Task.CompletedTask;
    }

    private void SaveSettings()
    {
        UserSettingsService.Save(new UserSettings
        {
            DetailSplitRatio = _detailSplitRatio,
            CreateBackup = _createBackup
        });
    }

    /// <summary>
    /// Debounces the assembly filter: waits 1.5 seconds after the last keystroke
    /// before applying filters. Each new keystroke resets the timer.
    /// </summary>
    private void DebounceApplyFilters()
    {
        _filterDebounceCts?.Cancel();
        _filterDebounceCts = new CancellationTokenSource();
        var token = _filterDebounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500, token).ConfigureAwait(false);
                if (!token.IsCancellationRequested)
                {
                    ApplyFilters();
                }
            }
            catch (OperationCanceledException)
            {
                // Debounce was cancelled by a newer keystroke or explicit apply
            }
        }, token);
    }

    private Task ExecuteSortAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (parameter is not string column || _allResults.Count == 0)
            return Task.CompletedTask;

        if (_sortColumn == column)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = column;
            _sortAscending = true;
        }

        RaiseNotifyPropertyChangedEvent(nameof(SortIndicatorProject));
        RaiseNotifyPropertyChangedEvent(nameof(SortIndicatorAssembly));
        RaiseNotifyPropertyChangedEvent(nameof(SortIndicatorResolved));
        RaiseNotifyPropertyChangedEvent(nameof(SortIndicatorRequested));
        RaiseNotifyPropertyChangedEvent(nameof(SortIndicatorBinDll));
        RaiseNotifyPropertyChangedEvent(nameof(SortIndicatorConfig));
        RaiseNotifyPropertyChangedEvent(nameof(SortIndicatorStatus));

        ApplyFilters();
        return Task.CompletedTask;
    }

    private void ApplyFilters()
    {
        Issues.Clear();
        SelectedIssue = null;

        IEnumerable<AssemblyRedirectInfo> filtered = _allResults;

        // Project filter
        if (_selectedProject != "All Projects")
        {
            filtered = filtered.Where(r =>
                string.Equals(r.ProjectName, _selectedProject, StringComparison.OrdinalIgnoreCase));
        }

        // Assembly name filter
        if (!string.IsNullOrWhiteSpace(_assemblyFilter))
        {
            string search = _assemblyFilter.Trim();
            filtered = filtered.Where(r =>
                r.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        // Status filter
        filtered = _selectedStatus switch
        {
            "Issues Only" => filtered.Where(r => r.Status != RedirectStatus.OK),
            "Stale" => filtered.Where(r => r.Status == RedirectStatus.Stale),
            "Missing" => filtered.Where(r => r.Status == RedirectStatus.Missing),
            "Mismatch" => filtered.Where(r => r.Status == RedirectStatus.Mismatch),
            "Duplicate" => filtered.Where(r => r.Status == RedirectStatus.Duplicate),
            "Conflict" => filtered.Where(r => r.Status == RedirectStatus.Conflict),
            "Token Lost" => filtered.Where(r => r.Status == RedirectStatus.TokenLost),
            "Deprecated" => filtered.Where(r => r.Status == RedirectStatus.Deprecated),
            "OK" => filtered.Where(r => r.Status == RedirectStatus.OK),
            _ => filtered // "All"
        };

        // Sort
        Func<AssemblyRedirectInfo, string> keySelector = _sortColumn switch
        {
            "Assembly" => r => r.Name,
            "Resolved" => r => r.ResolvedAssemblyVersion ?? "",
            "Requested" => r => r.RequestedVersion ?? "",
            "BinDll" => r => r.PhysicalVersion ?? "",
            "Config" => r => r.CurrentRedirectVersion ?? "",
            "Status" => r => r.Status.ToString(),
            _ => r => r.ProjectName // "Project"
        };
        filtered = _sortAscending
            ? filtered.OrderBy(keySelector, StringComparer.OrdinalIgnoreCase)
            : filtered.OrderByDescending(keySelector, StringComparer.OrdinalIgnoreCase);

        var resultList = filtered.ToList();

        foreach (var result in resultList)
        {
            Issues.Add(new AssemblyRedirectInfoViewModel(result, vm => SelectedIssue = vm));
        }

        int issueCount = resultList.Count(r => r.Status != RedirectStatus.OK);
        StatusText = $"Showing {Issues.Count} result(s), {issueCount} issue(s).";
    }
}

/// <summary>
/// ViewModel wrapper around <see cref="AssemblyRedirectInfo"/> for display in the Remote UI grid.
/// </summary>
[DataContract]
public class AssemblyRedirectInfoViewModel : NotifyPropertyChangedObject
{
    /// <summary>
    /// Initializes a new instance from an <see cref="AssemblyRedirectInfo"/> model.
    /// </summary>
    /// <param name="model">The source model data.</param>
    /// <param name="onSelect">Callback invoked when this row is clicked/selected.</param>
    public AssemblyRedirectInfoViewModel(AssemblyRedirectInfo model, Action<AssemblyRedirectInfoViewModel>? onSelect = null)
    {
        SelectCommand = new AsyncCommand((_, _) =>
        {
            onSelect?.Invoke(this);
            return Task.CompletedTask;
        });
        ProjectName = model.ProjectName;
        Name = model.Name;
        PublicKeyToken = model.PublicKeyToken;
        Culture = model.Culture;
        ResolvedAssemblyVersion = model.ResolvedAssemblyVersion ?? "\u2014";
        ResolvedPackageVersion = model.ResolvedPackageVersion ?? "\u2014";
        RequestedVersion = model.RequestedVersion ?? "\u2014";
        PhysicalVersion = model.PhysicalVersion ?? "\u2014";
        CurrentRedirectVersion = model.CurrentRedirectVersion ?? "\u2014";
        Status = model.Status;
        DiagnosticMessage = model.DiagnosticMessage;
        SuggestedAction = model.SuggestedAction;
        StatusIcon = model.StatusIcon;
        ActionLabel = model.ActionLabel;
        ResolvedCellStatus = model.ResolvedCellStatus;
        PhysicalCellStatus = model.PhysicalCellStatus;
        ConfigCellStatus = model.ConfigCellStatus;
    }

    /// <summary>Command to select this row in the list.</summary>
    [DataMember]
    public IAsyncCommand SelectCommand { get; }

    [DataMember] public string ProjectName { get; set; }
    [DataMember] public string Name { get; set; }
    [DataMember] public string PublicKeyToken { get; set; }
    [DataMember] public string Culture { get; set; }
    [DataMember] public string ResolvedAssemblyVersion { get; set; }
    [DataMember] public string ResolvedPackageVersion { get; set; }
    [DataMember] public string RequestedVersion { get; set; }
    [DataMember] public string PhysicalVersion { get; set; }
    [DataMember] public string CurrentRedirectVersion { get; set; }
    [DataMember] public RedirectStatus Status { get; set; }
    [DataMember] public string DiagnosticMessage { get; set; }
    [DataMember] public FixAction SuggestedAction { get; set; }
    [DataMember] public string StatusIcon { get; set; }
    [DataMember] public string ActionLabel { get; set; }
    [DataMember] public string ResolvedCellStatus { get; set; }
    [DataMember] public string PhysicalCellStatus { get; set; }
    [DataMember] public string ConfigCellStatus { get; set; }

    /// <summary>Combined status icon + text for display in the grid.</summary>
    [DataMember]
    public string StatusText => $"{StatusIcon} {Status switch
    {
        RedirectStatus.Stale => "STALE",
        RedirectStatus.Missing => "MISSING",
        RedirectStatus.Conflict => "CONFLICT",
        RedirectStatus.Duplicate => "DUPLICATE",
        RedirectStatus.Mismatch => "MISMATCH",
        RedirectStatus.TokenLost => "TOKEN LOST",
        RedirectStatus.Deprecated => "DEPRECATED",
        RedirectStatus.OK => "OK",
        _ => ""
    }}";

    /// <summary>Display string for the status.</summary>
    [DataMember]
    public string StatusDisplay => Status switch
    {
        RedirectStatus.Stale => "STALE",
        RedirectStatus.Missing => "MISSING",
        RedirectStatus.Conflict => "CONFLICT",
        RedirectStatus.Duplicate => "DUPLICATE",
        RedirectStatus.Mismatch => "MISMATCH",
        RedirectStatus.TokenLost => "TOKEN LOST",
        RedirectStatus.Deprecated => "DEPRECATED",
        RedirectStatus.OK => "OK",
        _ => ""
    };

    /// <summary>Whether this issue has a detail panel to show.</summary>
    [DataMember]
    public bool HasDetail => Status != RedirectStatus.OK;

    /// <summary>Whether the action button should be visible.</summary>
    [DataMember]
    public bool HasAction => SuggestedAction != FixAction.None;

    /// <summary>
    /// Marks this row as fixed in-place: updates the config version, status, and diagnostics
    /// without requiring a full re-analysis.
    /// </summary>
    /// <param name="newConfigVersion">The version that was written to the config file.</param>
    /// <summary>
    /// Updates the displayed config version and cell status from a live config file read.
    /// </summary>
    public void RefreshConfigVersion(string liveVersion, string? resolvedVersion)
    {
        CurrentRedirectVersion = liveVersion;
        ConfigCellStatus = string.Equals(liveVersion, resolvedVersion, StringComparison.OrdinalIgnoreCase) ? "ok" : "diverge";
        RaiseNotifyPropertyChangedEvent(nameof(CurrentRedirectVersion));
        RaiseNotifyPropertyChangedEvent(nameof(ConfigCellStatus));
    }

    public void MarkAsFixed(string newConfigVersion)
    {
        CurrentRedirectVersion = newConfigVersion;
        Status = RedirectStatus.OK;
        DiagnosticMessage = string.Empty;
        SuggestedAction = FixAction.None;
        StatusIcon = "\u2713";
        ActionLabel = string.Empty;
        ConfigCellStatus = "ok";

        // Notify UI of all changed properties
        RaiseNotifyPropertyChangedEvent(nameof(CurrentRedirectVersion));
        RaiseNotifyPropertyChangedEvent(nameof(Status));
        RaiseNotifyPropertyChangedEvent(nameof(DiagnosticMessage));
        RaiseNotifyPropertyChangedEvent(nameof(SuggestedAction));
        RaiseNotifyPropertyChangedEvent(nameof(StatusIcon));
        RaiseNotifyPropertyChangedEvent(nameof(ActionLabel));
        RaiseNotifyPropertyChangedEvent(nameof(ConfigCellStatus));
        RaiseNotifyPropertyChangedEvent(nameof(StatusText));
        RaiseNotifyPropertyChangedEvent(nameof(StatusDisplay));
        RaiseNotifyPropertyChangedEvent(nameof(HasDetail));
        RaiseNotifyPropertyChangedEvent(nameof(HasAction));
    }
}
