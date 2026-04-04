using BindingRedirectFixer.Services;

namespace BindingRedirectFixer.Tests.Services;

[TestClass]
public class ConfigPatcherSectionTests
{
    private readonly ConfigPatcher _patcher = new();

    private static string CreateTempConfig(string xmlContent)
    {
        string path = Path.Combine(Path.GetTempPath(), $"BRFTests_{Guid.NewGuid():N}", "app.config");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, xmlContent);
        return path;
    }

    private static string CreateTempProjectDirWithConfig(string configContent, string csprojContent)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"BRFTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "app.config"), configContent);
        File.WriteAllText(Path.Combine(dir, "TestProject.csproj"), csprojContent);
        return dir;
    }

    private static void Cleanup(string path)
    {
        string dir = File.Exists(path) ? Path.GetDirectoryName(path)! : path;
        try { Directory.Delete(dir, true); } catch { }
    }

    #region HasOnlyAssemblyBinding

    [TestMethod]
    [TestCategory("Unit")]
    public void HasOnlyAssemblyBinding_OnlyRedirects_ReturnsTrue()
    {
        string path = CreateTempConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <runtime>
                <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
                  <dependentAssembly>
                    <assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
                    <bindingRedirect oldVersion="0.0.0.0-13.0.0.0" newVersion="13.0.0.0" />
                  </dependentAssembly>
                </assemblyBinding>
              </runtime>
            </configuration>
            """);
        try
        {
            Assert.IsTrue(_patcher.HasOnlyAssemblyBinding(path));
        }
        finally { Cleanup(path); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void HasOnlyAssemblyBinding_WithAppSettings_ReturnsFalse()
    {
        string path = CreateTempConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <appSettings>
                <add key="MySetting" value="123" />
              </appSettings>
              <runtime>
                <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
                  <dependentAssembly>
                    <assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
                    <bindingRedirect oldVersion="0.0.0.0-13.0.0.0" newVersion="13.0.0.0" />
                  </dependentAssembly>
                </assemblyBinding>
              </runtime>
            </configuration>
            """);
        try
        {
            Assert.IsFalse(_patcher.HasOnlyAssemblyBinding(path));
        }
        finally { Cleanup(path); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void HasOnlyAssemblyBinding_WithConnectionStrings_ReturnsFalse()
    {
        string path = CreateTempConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <connectionStrings>
                <add name="Default" connectionString="Server=.;Database=Test" />
              </connectionStrings>
              <runtime>
                <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
                  <dependentAssembly>
                    <assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
                    <bindingRedirect oldVersion="0.0.0.0-13.0.0.0" newVersion="13.0.0.0" />
                  </dependentAssembly>
                </assemblyBinding>
              </runtime>
            </configuration>
            """);
        try
        {
            Assert.IsFalse(_patcher.HasOnlyAssemblyBinding(path));
        }
        finally { Cleanup(path); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void HasOnlyAssemblyBinding_EmptyConfig_ReturnsFalse()
    {
        string path = CreateTempConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
            </configuration>
            """);
        try
        {
            Assert.IsFalse(_patcher.HasOnlyAssemblyBinding(path));
        }
        finally { Cleanup(path); }
    }

    #endregion

    #region RemoveAssemblyBindingSection

    [TestMethod]
    [TestCategory("Unit")]
    public void RemoveAssemblyBindingSection_RemovesSection_LeavesOtherConfig()
    {
        string path = CreateTempConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <appSettings>
                <add key="MySetting" value="123" />
              </appSettings>
              <runtime>
                <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
                  <dependentAssembly>
                    <assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
                    <bindingRedirect oldVersion="0.0.0.0-13.0.0.0" newVersion="13.0.0.0" />
                  </dependentAssembly>
                </assemblyBinding>
              </runtime>
            </configuration>
            """);
        try
        {
            bool result = _patcher.RemoveAssemblyBindingSection(path);
            Assert.IsTrue(result);

            string content = File.ReadAllText(path);
            Assert.IsFalse(content.Contains("assemblyBinding"));
            Assert.IsTrue(content.Contains("appSettings"));
        }
        finally { Cleanup(path); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void RemoveAssemblyBindingSection_RemovesEmptyRuntime()
    {
        string path = CreateTempConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <runtime>
                <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
                  <dependentAssembly>
                    <assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
                    <bindingRedirect oldVersion="0.0.0.0-13.0.0.0" newVersion="13.0.0.0" />
                  </dependentAssembly>
                </assemblyBinding>
              </runtime>
            </configuration>
            """);
        try
        {
            bool result = _patcher.RemoveAssemblyBindingSection(path);
            Assert.IsTrue(result);

            string content = File.ReadAllText(path);
            Assert.IsFalse(content.Contains("runtime"));
            Assert.IsFalse(content.Contains("assemblyBinding"));
        }
        finally { Cleanup(path); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void RemoveAssemblyBindingSection_PreservesRuntimeWithOtherChildren()
    {
        string path = CreateTempConfig("""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <runtime>
                <gcServer enabled="true" />
                <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
                  <dependentAssembly>
                    <assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
                    <bindingRedirect oldVersion="0.0.0.0-13.0.0.0" newVersion="13.0.0.0" />
                  </dependentAssembly>
                </assemblyBinding>
              </runtime>
            </configuration>
            """);
        try
        {
            bool result = _patcher.RemoveAssemblyBindingSection(path);
            Assert.IsTrue(result);

            string content = File.ReadAllText(path);
            Assert.IsFalse(content.Contains("assemblyBinding"));
            Assert.IsTrue(content.Contains("runtime"));
            Assert.IsTrue(content.Contains("gcServer"));
        }
        finally { Cleanup(path); }
    }

    #endregion

    #region RemoveConfigFileAndCsprojReference

    [TestMethod]
    [TestCategory("Unit")]
    public void RemoveConfigFileAndCsprojReference_DeletesFileAndCsprojEntry()
    {
        string dir = CreateTempProjectDirWithConfig(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <runtime>
                <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
                  <dependentAssembly>
                    <assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
                    <bindingRedirect oldVersion="0.0.0.0-13.0.0.0" newVersion="13.0.0.0" />
                  </dependentAssembly>
                </assemblyBinding>
              </runtime>
            </configuration>
            """,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
              </PropertyGroup>
              <ItemGroup>
                <None Include="app.config" />
              </ItemGroup>
            </Project>
            """);
        try
        {
            string configPath = Path.Combine(dir, "app.config");
            bool result = _patcher.RemoveConfigFileAndCsprojReference(configPath, dir);

            Assert.IsTrue(result);
            Assert.IsFalse(File.Exists(configPath));

            string csproj = File.ReadAllText(Path.Combine(dir, "TestProject.csproj"));
            Assert.IsFalse(csproj.Contains("app.config", StringComparison.OrdinalIgnoreCase));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void RemoveConfigFileAndCsprojReference_SdkStyle_DeletesFileOnly()
    {
        string dir = CreateTempProjectDirWithConfig(
            """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <runtime>
                <assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">
                  <dependentAssembly>
                    <assemblyIdentity name="Newtonsoft.Json" publicKeyToken="30ad4fe6b2a6aeed" culture="neutral" />
                    <bindingRedirect oldVersion="0.0.0.0-13.0.0.0" newVersion="13.0.0.0" />
                  </dependentAssembly>
                </assemblyBinding>
              </runtime>
            </configuration>
            """,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            string configPath = Path.Combine(dir, "app.config");
            bool result = _patcher.RemoveConfigFileAndCsprojReference(configPath, dir);

            Assert.IsTrue(result);
            Assert.IsFalse(File.Exists(configPath));
        }
        finally { Cleanup(dir); }
    }

    #endregion
}
