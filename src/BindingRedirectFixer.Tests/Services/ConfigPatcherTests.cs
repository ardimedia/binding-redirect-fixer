using BindingRedirectFixer.Services;

namespace BindingRedirectFixer.Tests.Services;

[TestClass]
public class ConfigPatcherTests
{
    private readonly ConfigPatcher _patcher = new();

    private static readonly string SampleConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <runtime>
            <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
              <dependentAssembly>
                <assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
                <bindingRedirect oldVersion="0.0.0.0-12.0.0.0" newVersion="12.0.0.0" />
              </dependentAssembly>
              <dependentAssembly>
                <assemblyIdentity name="System.Memory" publicKeyToken="cc7b13ffcd2ddd51" culture="neutral" />
                <bindingRedirect oldVersion="0.0.0.0-4.0.1.2" newVersion="4.0.1.2" />
              </dependentAssembly>
            </assemblyBinding>
          </runtime>
        </configuration>
        """;

    private static readonly string DuplicateConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <runtime>
            <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
              <dependentAssembly>
                <assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
                <bindingRedirect oldVersion="0.0.0.0-12.0.0.0" newVersion="12.0.0.0" />
              </dependentAssembly>
              <dependentAssembly>
                <assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
                <bindingRedirect oldVersion="0.0.0.0-13.0.0.0" newVersion="13.0.0.0" />
              </dependentAssembly>
            </assemblyBinding>
          </runtime>
        </configuration>
        """;

    private string WriteTempConfig(string content)
    {
        string dir = Path.Combine(Path.GetTempPath(), "BRFTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "app.config");
        File.WriteAllText(path, content);
        return path;
    }

    private static void CleanupTempConfig(string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (dir is not null && Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
        catch { }
    }

    #region ReadRedirects

    [TestMethod]
    [TestCategory("Unit")]
    public void ReadRedirects_ReturnsAllEntries()
    {
        string path = WriteTempConfig(SampleConfig);
        try
        {
            var redirects = _patcher.ReadRedirects(path);

            Assert.AreEqual(2, redirects.Count);
            Assert.IsTrue(redirects.ContainsKey("Newtonsoft.Json"));
            Assert.IsTrue(redirects.ContainsKey("System.Memory"));
            Assert.AreEqual("12.0.0.0", redirects["Newtonsoft.Json"].NewVersion);
            Assert.AreEqual("4.0.1.2", redirects["System.Memory"].NewVersion);
        }
        finally
        {
            CleanupTempConfig(path);
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ReadRedirects_ReturnsPublicKeyToken()
    {
        string path = WriteTempConfig(SampleConfig);
        try
        {
            var redirects = _patcher.ReadRedirects(path);
            Assert.AreEqual("30ad4fe6b2a6aeed", redirects["Newtonsoft.Json"].PublicKeyToken);
        }
        finally
        {
            CleanupTempConfig(path);
        }
    }

    #endregion

    #region DetectDuplicateRedirects

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectDuplicateRedirects_FindsDuplicates()
    {
        string path = WriteTempConfig(DuplicateConfig);
        try
        {
            var duplicates = _patcher.DetectDuplicateRedirects(path);

            Assert.AreEqual(1, duplicates.Count);
            Assert.IsTrue(duplicates.Contains("Newtonsoft.Json"));
        }
        finally
        {
            CleanupTempConfig(path);
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectDuplicateRedirects_NoDuplicates_ReturnsEmpty()
    {
        string path = WriteTempConfig(SampleConfig);
        try
        {
            var duplicates = _patcher.DetectDuplicateRedirects(path);
            Assert.AreEqual(0, duplicates.Count);
        }
        finally
        {
            CleanupTempConfig(path);
        }
    }

    #endregion

    #region UpdateRedirect

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateRedirect_ChangesVersion()
    {
        string path = WriteTempConfig(SampleConfig);
        try
        {
            bool result = _patcher.UpdateRedirect(path, "Newtonsoft.Json", "30ad4fe6b2a6aeed", "neutral", "13.0.0.0");

            Assert.IsTrue(result);

            var redirects = _patcher.ReadRedirects(path);
            Assert.AreEqual("13.0.0.0", redirects["Newtonsoft.Json"].NewVersion);
            Assert.AreEqual("0.0.0.0-13.0.0.0", redirects["Newtonsoft.Json"].OldVersion);
        }
        finally
        {
            CleanupTempConfig(path);
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UpdateRedirect_NonExistentAssembly_ReturnsFalse()
    {
        string path = WriteTempConfig(SampleConfig);
        try
        {
            bool result = _patcher.UpdateRedirect(path, "NonExistent.Assembly", "abc123", "neutral", "1.0.0.0");
            Assert.IsFalse(result);
        }
        finally
        {
            CleanupTempConfig(path);
        }
    }

    #endregion

    #region AddRedirect

    [TestMethod]
    [TestCategory("Unit")]
    public void AddRedirect_AddsNewEntry()
    {
        string path = WriteTempConfig(SampleConfig);
        try
        {
            bool result = _patcher.AddRedirect(path, "System.Buffers", "cc7b13ffcd2ddd51", "neutral", "4.0.4.0");

            Assert.IsTrue(result);

            var redirects = _patcher.ReadRedirects(path);
            Assert.AreEqual(3, redirects.Count);
            Assert.IsTrue(redirects.ContainsKey("System.Buffers"));
            Assert.AreEqual("4.0.4.0", redirects["System.Buffers"].NewVersion);
        }
        finally
        {
            CleanupTempConfig(path);
        }
    }

    #endregion

    #region RemoveRedirect

    [TestMethod]
    [TestCategory("Unit")]
    public void RemoveRedirect_RemovesEntry()
    {
        string path = WriteTempConfig(SampleConfig);
        try
        {
            bool result = _patcher.RemoveRedirect(path, "System.Memory");

            Assert.IsTrue(result);

            var redirects = _patcher.ReadRedirects(path);
            Assert.AreEqual(1, redirects.Count);
            Assert.IsFalse(redirects.ContainsKey("System.Memory"));
            Assert.IsTrue(redirects.ContainsKey("Newtonsoft.Json"));
        }
        finally
        {
            CleanupTempConfig(path);
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void RemoveRedirect_NonExistentAssembly_ReturnsFalse()
    {
        string path = WriteTempConfig(SampleConfig);
        try
        {
            bool result = _patcher.RemoveRedirect(path, "NonExistent.Assembly");
            Assert.IsFalse(result);
        }
        finally
        {
            CleanupTempConfig(path);
        }
    }

    #endregion

    #region RemoveDuplicateRedirects

    [TestMethod]
    [TestCategory("Unit")]
    public void RemoveDuplicateRedirects_KeepsCorrectVersion()
    {
        string path = WriteTempConfig(DuplicateConfig);
        try
        {
            bool result = _patcher.RemoveDuplicateRedirects(path, "Newtonsoft.Json", "13.0.0.0");

            Assert.IsTrue(result);

            var redirects = _patcher.ReadRedirects(path);
            Assert.AreEqual(1, redirects.Count);
            Assert.AreEqual("13.0.0.0", redirects["Newtonsoft.Json"].NewVersion);

            var duplicates = _patcher.DetectDuplicateRedirects(path);
            Assert.AreEqual(0, duplicates.Count);
        }
        finally
        {
            CleanupTempConfig(path);
        }
    }

    #endregion

    #region ReadRedirectXml

    [TestMethod]
    [TestCategory("Unit")]
    public void ReadRedirectXml_ReturnsXmlSnippet()
    {
        string path = WriteTempConfig(SampleConfig);
        try
        {
            string? xml = _patcher.ReadRedirectXml(path, "Newtonsoft.Json");

            Assert.IsNotNull(xml);
            Assert.IsTrue(xml.Contains("Newtonsoft.Json"));
            Assert.IsTrue(xml.Contains("12.0.0.0"));
        }
        finally
        {
            CleanupTempConfig(path);
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ReadRedirectXml_NonExistentAssembly_ReturnsNull()
    {
        string path = WriteTempConfig(SampleConfig);
        try
        {
            string? xml = _patcher.ReadRedirectXml(path, "NonExistent.Assembly");
            Assert.IsNull(xml);
        }
        finally
        {
            CleanupTempConfig(path);
        }
    }

    #endregion

    #region GetConfigFilePath

    [TestMethod]
    [TestCategory("Unit")]
    public void GetConfigFilePath_FindsAppConfig()
    {
        string dir = Path.Combine(Path.GetTempPath(), "BRFTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string configPath = Path.Combine(dir, "app.config");
        File.WriteAllText(configPath, SampleConfig);

        try
        {
            string? result = _patcher.GetConfigFilePath(dir);
            Assert.IsNotNull(result);
            Assert.IsTrue(result.EndsWith("app.config", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void GetConfigFilePath_FindsWebConfig()
    {
        string dir = Path.Combine(Path.GetTempPath(), "BRFTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string configPath = Path.Combine(dir, "web.config");
        File.WriteAllText(configPath, SampleConfig);

        try
        {
            string? result = _patcher.GetConfigFilePath(dir);
            Assert.IsNotNull(result);
            Assert.IsTrue(result.EndsWith("web.config", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void GetConfigFilePath_NoConfig_ReturnsNull()
    {
        string dir = Path.Combine(Path.GetTempPath(), "BRFTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            string? result = _patcher.GetConfigFilePath(dir);
            Assert.IsNull(result);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    #endregion

    #region CreateBackup

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateBackup_CreatesBackupFile()
    {
        string path = WriteTempConfig(SampleConfig);
        try
        {
            string? backupPath = _patcher.CreateBackup(path);

            Assert.IsNotNull(backupPath);
            Assert.IsTrue(File.Exists(backupPath));

            string original = File.ReadAllText(path);
            string backup = File.ReadAllText(backupPath);
            Assert.AreEqual(original, backup);
        }
        finally
        {
            CleanupTempConfig(path);
        }
    }

    #endregion
}
