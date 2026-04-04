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
    /// Dictionary mapping assembly name to a tuple of (oldVersion range, newVersion, publicKeyToken).
    /// </returns>
    public Dictionary<string, (string OldVersion, string NewVersion, string PublicKeyToken)> ReadRedirects(string configPath)
    {
        var results = new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase);

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
            string publicKeyToken = identity.Attribute("publicKeyToken")?.Value ?? string.Empty;

            if (!string.IsNullOrEmpty(name) &&
                !string.IsNullOrEmpty(oldVersion) &&
                !string.IsNullOrEmpty(newVersion))
            {
                results[name] = (oldVersion, newVersion, publicKeyToken);
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

        // Update identity attributes in case they changed.
        // If the incoming publicKeyToken is empty but the config already has one,
        // preserve the existing token — the resolved DLL may have lost strong naming
        // (e.g., unsigned preview build) but the runtime still needs the original token.
        if (!string.IsNullOrEmpty(publicKeyToken))
        {
            identity.SetAttributeValue("publicKeyToken", publicKeyToken);
        }

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
    /// Checks whether the config file contains only assembly binding redirects and no other
    /// meaningful configuration sections (appSettings, connectionStrings, custom sections, etc.).
    /// </summary>
    /// <param name="configPath">Full path to the config file.</param>
    /// <returns><c>true</c> if the file contains only assemblyBinding; otherwise <c>false</c>.</returns>
    public bool HasOnlyAssemblyBinding(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return false;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Load(configPath);
        }
        catch
        {
            return false;
        }

        XElement? root = doc.Root;
        if (root is null || !root.Name.LocalName.Equals("configuration", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check that <configuration> has no significant child elements other than <runtime>
        var rootChildren = root.Elements().ToList();
        if (rootChildren.Count == 0)
        {
            return false;
        }

        if (rootChildren.Any(e => !e.Name.LocalName.Equals("runtime", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Check that <runtime> has no child elements other than <assemblyBinding>
        XElement runtime = rootChildren[0];
        var runtimeChildren = runtime.Elements().ToList();
        return runtimeChildren.Count > 0 &&
               runtimeChildren.All(e => e.Name.LocalName.Equals("assemblyBinding", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Removes the entire <c>&lt;assemblyBinding&gt;</c> section from the config file.
    /// If the <c>&lt;runtime&gt;</c> element becomes empty after removal, it is also removed.
    /// </summary>
    /// <param name="configPath">Full path to the config file.</param>
    /// <returns><c>true</c> if the section was found and removed; otherwise <c>false</c>.</returns>
    public bool RemoveAssemblyBindingSection(string configPath)
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

        XElement? assemblyBinding = doc.Descendants(AssemblyBindingNs + "assemblyBinding").FirstOrDefault();
        if (assemblyBinding is null)
        {
            return false;
        }

        XElement? runtime = assemblyBinding.Parent;
        assemblyBinding.Remove();

        // If <runtime> is now empty, remove it too
        if (runtime is not null && !runtime.HasElements)
        {
            runtime.Remove();
        }

        doc.Save(configPath, SaveOptions.DisableFormatting);
        return true;
    }

    /// <summary>
    /// Deletes the config file and removes any reference to it from the .csproj file.
    /// For legacy (non-SDK) projects, removes <c>&lt;None Include="App.config" /&gt;</c> or
    /// <c>&lt;None Update="App.config" /&gt;</c> entries from the project file.
    /// </summary>
    /// <param name="configPath">Full path to the config file.</param>
    /// <param name="projectDirectory">Full path to the project directory.</param>
    /// <returns><c>true</c> if the file was deleted; otherwise <c>false</c>.</returns>
    public bool RemoveConfigFileAndCsprojReference(string configPath, string projectDirectory)
    {
        if (!File.Exists(configPath))
        {
            return false;
        }

        string configFileName = Path.GetFileName(configPath);
        File.Delete(configPath);

        // Remove reference from .csproj if present
        try
        {
            string[] csprojFiles = Directory.GetFiles(projectDirectory, "*.csproj", SearchOption.TopDirectoryOnly);
            if (csprojFiles.Length > 0)
            {
                var doc = XDocument.Load(csprojFiles[0], LoadOptions.PreserveWhitespace);
                XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

                var configRefs = doc.Descendants(ns + "None")
                    .Where(e =>
                    {
                        string? include = e.Attribute("Include")?.Value;
                        string? update = e.Attribute("Update")?.Value;
                        return string.Equals(include, configFileName, StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(update, configFileName, StringComparison.OrdinalIgnoreCase);
                    })
                    .ToList();

                if (configRefs.Count > 0)
                {
                    foreach (XElement configRef in configRefs)
                    {
                        configRef.Remove();
                    }

                    doc.Save(csprojFiles[0], SaveOptions.DisableFormatting);
                }
            }
        }
        catch
        {
            // Config file is already deleted; csproj cleanup is best-effort
        }

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
