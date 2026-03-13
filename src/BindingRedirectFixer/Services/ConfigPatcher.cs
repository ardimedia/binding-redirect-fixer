using System.Xml.Linq;

namespace BindingRedirectFixer.Services;

/// <summary>
/// Reads and writes binding redirect entries in web.config / app.config files.
/// Preserves existing XML formatting and comments.
/// </summary>
public sealed class ConfigPatcher
{
    /// <summary>
    /// XML namespace for assembly binding elements.
    /// </summary>
    private static readonly XNamespace AssemblyBindingNs =
        "urn:schemas-microsoft-com:asm.v1";

    /// <summary>
    /// Finds the configuration file (web.config or app.config) in the project directory.
    /// </summary>
    /// <param name="projectDirectory">Path to the project directory.</param>
    /// <returns>Full path to the config file, or <c>null</c> if neither exists.</returns>
    public string? GetConfigFilePath(string projectDirectory)
    {
        string webConfig = Path.Combine(projectDirectory, "web.config");
        if (File.Exists(webConfig))
        {
            return webConfig;
        }

        string appConfig = Path.Combine(projectDirectory, "app.config");
        if (File.Exists(appConfig))
        {
            return appConfig;
        }

        // Also check capitalized variants
        string appConfigUpper = Path.Combine(projectDirectory, "App.config");
        if (File.Exists(appConfigUpper))
        {
            return appConfigUpper;
        }

        string webConfigUpper = Path.Combine(projectDirectory, "Web.config");
        if (File.Exists(webConfigUpper))
        {
            return webConfigUpper;
        }

        return null;
    }

    /// <summary>
    /// Reads all existing binding redirects from the specified config file.
    /// </summary>
    /// <param name="configPath">Full path to the config file.</param>
    /// <returns>
    /// Dictionary mapping assembly name to a tuple of (oldVersion range, newVersion).
    /// </returns>
    public Dictionary<string, (string OldVersion, string NewVersion)> ReadRedirects(string configPath)
    {
        var results = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(configPath))
        {
            return results;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Load(configPath, LoadOptions.PreserveWhitespace);
        }
        catch (Exception)
        {
            return results;
        }

        IEnumerable<XElement> dependentAssemblies = doc.Descendants(AssemblyBindingNs + "dependentAssembly");

        foreach (XElement dependentAssembly in dependentAssemblies)
        {
            XElement? identity = dependentAssembly.Element(AssemblyBindingNs + "assemblyIdentity");
            XElement? redirect = dependentAssembly.Element(AssemblyBindingNs + "bindingRedirect");

            if (identity is null || redirect is null)
            {
                continue;
            }

            string? name = identity.Attribute("name")?.Value;
            string? oldVersion = redirect.Attribute("oldVersion")?.Value;
            string? newVersion = redirect.Attribute("newVersion")?.Value;

            if (!string.IsNullOrEmpty(name) &&
                !string.IsNullOrEmpty(oldVersion) &&
                !string.IsNullOrEmpty(newVersion))
            {
                results[name] = (oldVersion, newVersion);
            }
        }

        return results;
    }

    /// <summary>
    /// Detects assembly names that have multiple binding redirect entries in the config file.
    /// </summary>
    /// <param name="configPath">Full path to the config file.</param>
    /// <returns>
    /// Set of assembly names that appear more than once as dependentAssembly entries.
    /// </returns>
    public HashSet<string> DetectDuplicateRedirects(string configPath)
    {
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(configPath))
        {
            return duplicates;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Load(configPath);
        }
        catch
        {
            return duplicates;
        }

        foreach (XElement dependentAssembly in doc.Descendants(AssemblyBindingNs + "dependentAssembly"))
        {
            XElement? identity = dependentAssembly.Element(AssemblyBindingNs + "assemblyIdentity");
            string? name = identity?.Attribute("name")?.Value;

            if (!string.IsNullOrEmpty(name))
            {
                if (!seen.Add(name))
                {
                    duplicates.Add(name);
                }
            }
        }

        return duplicates;
    }

    /// <summary>
    /// Removes duplicate binding redirect entries for the specified assembly,
    /// keeping only one entry with the specified target version.
    /// </summary>
    /// <param name="configPath">Full path to the config file.</param>
    /// <param name="assemblyName">Assembly name to deduplicate.</param>
    /// <param name="targetVersion">The newVersion to keep. If no entry matches, the last entry is kept.</param>
    /// <returns><c>true</c> if duplicates were removed; <c>false</c> if no duplicates found.</returns>
    public bool RemoveDuplicateRedirects(string configPath, string assemblyName, string targetVersion)
    {
        if (!File.Exists(configPath))
        {
            return false;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Load(configPath, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            return false;
        }

        var matches = doc.Descendants(AssemblyBindingNs + "dependentAssembly")
            .Where(da =>
            {
                XElement? identity = da.Element(AssemblyBindingNs + "assemblyIdentity");
                string? name = identity?.Attribute("name")?.Value;
                return string.Equals(name, assemblyName, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (matches.Count <= 1)
        {
            return false;
        }

        // Find the entry to keep: prefer the one with the correct target version
        XElement? keeper = matches.FirstOrDefault(da =>
        {
            XElement? redirect = da.Element(AssemblyBindingNs + "bindingRedirect");
            string? newVersion = redirect?.Attribute("newVersion")?.Value;
            return string.Equals(newVersion, targetVersion, StringComparison.OrdinalIgnoreCase);
        }) ?? matches.Last(); // fall back to last entry

        // Update the keeper's version to the target if it doesn't match
        XElement? keeperRedirect = keeper.Element(AssemblyBindingNs + "bindingRedirect");
        if (keeperRedirect is not null)
        {
            keeperRedirect.SetAttributeValue("newVersion", targetVersion);
            string oldVersionRange = $"0.0.0.0-{targetVersion}";
            keeperRedirect.SetAttributeValue("oldVersion", oldVersionRange);
        }

        // Remove all other entries
        foreach (XElement duplicate in matches.Where(m => m != keeper))
        {
            duplicate.Remove();
        }

        doc.Save(configPath, SaveOptions.DisableFormatting);
        return true;
    }

    /// <summary>
    /// Reads the raw XML snippet of the dependentAssembly element for the specified assembly.
    /// Returns the indented XML string, or <c>null</c> if no redirect exists.
    /// </summary>
    /// <param name="configPath">Full path to the config file.</param>
    /// <param name="assemblyName">Assembly name to look up.</param>
    /// <returns>The XML snippet as a string, or <c>null</c> if not found.</returns>
    public string? ReadRedirectXml(string configPath, string assemblyName)
    {
        if (!File.Exists(configPath))
        {
            return null;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Load(configPath);
        }
        catch
        {
            return null;
        }

        foreach (XElement dependentAssembly in doc.Descendants(AssemblyBindingNs + "dependentAssembly"))
        {
            XElement? identity = dependentAssembly.Element(AssemblyBindingNs + "assemblyIdentity");
            string? name = identity?.Attribute("name")?.Value;

            if (string.Equals(name, assemblyName, StringComparison.OrdinalIgnoreCase))
            {
                return dependentAssembly.ToString();
            }
        }

        return null;
    }

    /// <summary>
    /// Updates an existing binding redirect for the specified assembly to a new version.
    /// </summary>
    /// <param name="configPath">Full path to the config file.</param>
    /// <param name="assemblyName">Assembly name to update.</param>
    /// <param name="publicKeyToken">Public key token of the assembly.</param>
    /// <param name="culture">Culture of the assembly (typically "neutral").</param>
    /// <param name="newVersion">New version to redirect to.</param>
    /// <returns><c>true</c> if the redirect was found and updated; otherwise <c>false</c>.</returns>
    public bool UpdateRedirect(
        string configPath,
        string assemblyName,
        string publicKeyToken,
        string culture,
        string newVersion)
    {
        if (!File.Exists(configPath))
        {
            return false;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Load(configPath, LoadOptions.PreserveWhitespace);
        }
        catch (Exception)
        {
            return false;
        }

        XElement? targetDependentAssembly = FindDependentAssembly(doc, assemblyName);
        if (targetDependentAssembly is null)
        {
            return false;
        }

        XElement? identity = targetDependentAssembly.Element(AssemblyBindingNs + "assemblyIdentity");
        XElement? redirect = targetDependentAssembly.Element(AssemblyBindingNs + "bindingRedirect");

        if (identity is null || redirect is null)
        {
            return false;
        }

        // Update identity attributes in case they changed
        identity.SetAttributeValue("publicKeyToken", publicKeyToken);
        identity.SetAttributeValue("culture", culture);

        // Update the redirect version range and target
        redirect.SetAttributeValue("oldVersion", $"0.0.0.0-{newVersion}");
        redirect.SetAttributeValue("newVersion", newVersion);

        doc.Save(configPath, SaveOptions.DisableFormatting);
        return true;
    }

    /// <summary>
    /// Adds a new binding redirect element for the specified assembly.
    /// Creates the assemblyBinding section if it does not exist.
    /// </summary>
    /// <param name="configPath">Full path to the config file.</param>
    /// <param name="assemblyName">Assembly name to add a redirect for.</param>
    /// <param name="publicKeyToken">Public key token of the assembly.</param>
    /// <param name="culture">Culture of the assembly (typically "neutral").</param>
    /// <param name="newVersion">Version to redirect to.</param>
    /// <returns><c>true</c> if the redirect was added; otherwise <c>false</c>.</returns>
    public bool AddRedirect(
        string configPath,
        string assemblyName,
        string publicKeyToken,
        string culture,
        string newVersion)
    {
        if (!File.Exists(configPath))
        {
            return false;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Load(configPath, LoadOptions.PreserveWhitespace);
        }
        catch (Exception)
        {
            return false;
        }

        if (doc.Root is null)
        {
            return false;
        }

        // Ensure <runtime> element exists
        XElement? runtime = doc.Root.Element("runtime");
        if (runtime is null)
        {
            runtime = new XElement("runtime");
            doc.Root.Add(runtime);
        }

        // Ensure <assemblyBinding> element exists
        XElement? assemblyBinding = runtime.Element(AssemblyBindingNs + "assemblyBinding");
        if (assemblyBinding is null)
        {
            assemblyBinding = new XElement(
                AssemblyBindingNs + "assemblyBinding",
                new XAttribute("xmlns", AssemblyBindingNs.NamespaceName));
            runtime.Add(assemblyBinding);
        }

        // Create the new dependentAssembly element
        var dependentAssembly = new XElement(
            AssemblyBindingNs + "dependentAssembly",
            new XElement(
                AssemblyBindingNs + "assemblyIdentity",
                new XAttribute("name", assemblyName),
                new XAttribute("publicKeyToken", publicKeyToken),
                new XAttribute("culture", culture)),
            new XElement(
                AssemblyBindingNs + "bindingRedirect",
                new XAttribute("oldVersion", $"0.0.0.0-{newVersion}"),
                new XAttribute("newVersion", newVersion)));

        assemblyBinding.Add(dependentAssembly);

        doc.Save(configPath, SaveOptions.DisableFormatting);
        return true;
    }

    /// <summary>
    /// Creates a timestamped backup of the configuration file.
    /// The backup file name is <c>{configPath}.{yyyy-MM-dd-HHmm}.bak</c>.
    /// </summary>
    /// <param name="configPath">Full path to the config file to back up.</param>
    /// <returns>Full path to the created backup file, or <c>null</c> if the backup failed.</returns>
    public string? CreateBackup(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmm");
            string backupPath = $"{configPath}.{timestamp}.bak";

            File.Copy(configPath, backupPath, overwrite: true);
            return backupPath;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Removes the binding redirect entry for the specified assembly from the config file.
    /// </summary>
    /// <param name="configPath">Full path to the config file.</param>
    /// <param name="assemblyName">Assembly name whose redirect entry should be removed.</param>
    /// <returns><c>true</c> if the entry was found and removed; otherwise <c>false</c>.</returns>
    public bool RemoveRedirect(string configPath, string assemblyName)
    {
        if (!File.Exists(configPath))
        {
            return false;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Load(configPath, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            return false;
        }

        XElement? target = FindDependentAssembly(doc, assemblyName);
        if (target is null)
        {
            return false;
        }

        target.Remove();
        doc.Save(configPath, SaveOptions.DisableFormatting);
        return true;
    }

    /// <summary>
    /// Finds the dependentAssembly element matching the given assembly name.
    /// </summary>
    private static XElement? FindDependentAssembly(XDocument doc, string assemblyName)
    {
        IEnumerable<XElement> dependentAssemblies =
            doc.Descendants(AssemblyBindingNs + "dependentAssembly");

        foreach (XElement da in dependentAssemblies)
        {
            XElement? identity = da.Element(AssemblyBindingNs + "assemblyIdentity");
            string? name = identity?.Attribute("name")?.Value;

            if (string.Equals(name, assemblyName, StringComparison.OrdinalIgnoreCase))
            {
                return da;
            }
        }

        return null;
    }
}
