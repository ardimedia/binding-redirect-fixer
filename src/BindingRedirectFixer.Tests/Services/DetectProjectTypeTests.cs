using BindingRedirectFixer.Services;

namespace BindingRedirectFixer.Tests.Services;

[TestClass]
public class DetectProjectTypeTests
{
    private string CreateTempProjectDir(string csprojContent)
    {
        string dir = Path.Combine(Path.GetTempPath(), "BRFTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "TestProject.csproj"), csprojContent);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try { Directory.Delete(dir, true); } catch { }
    }

    #region DetectIsLibrary

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectIsLibrary_SdkStyle_NoOutputType_ReturnsTrue()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsTrue(BindingRedirectAnalyzer.DetectIsLibrary(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectIsLibrary_SdkStyle_OutputTypeLibrary_ReturnsTrue()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
                <OutputType>Library</OutputType>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsTrue(BindingRedirectAnalyzer.DetectIsLibrary(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectIsLibrary_SdkStyle_OutputTypeExe_ReturnsFalse()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsFalse(BindingRedirectAnalyzer.DetectIsLibrary(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectIsLibrary_SdkStyle_OutputTypeWinExe_ReturnsFalse()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
                <OutputType>WinExe</OutputType>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsFalse(BindingRedirectAnalyzer.DetectIsLibrary(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectIsLibrary_WebSdk_ReturnsFalse()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsFalse(BindingRedirectAnalyzer.DetectIsLibrary(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectIsLibrary_Legacy_OutputTypeLibrary_ReturnsTrue()
    {
        string dir = CreateTempProjectDir("""
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
                <OutputType>Library</OutputType>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsTrue(BindingRedirectAnalyzer.DetectIsLibrary(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectIsLibrary_Legacy_OutputTypeExe_ReturnsFalse()
    {
        string dir = CreateTempProjectDir("""
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsFalse(BindingRedirectAnalyzer.DetectIsLibrary(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectIsLibrary_NoCsprojFile_ReturnsFalse()
    {
        string dir = Path.Combine(Path.GetTempPath(), "BRFTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.IsFalse(BindingRedirectAnalyzer.DetectIsLibrary(dir));
        }
        finally { Cleanup(dir); }
    }

    #endregion

    #region DetectIsTestProject

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectIsTestProject_WithIsTestProjectTrue_ReturnsTrue()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsTrue(BindingRedirectAnalyzer.DetectIsTestProject(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectIsTestProject_WithMSTest_ReturnsTrue()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="MSTest" Version="4.0.2" />
              </ItemGroup>
            </Project>
            """);
        try
        {
            Assert.IsTrue(BindingRedirectAnalyzer.DetectIsTestProject(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectIsTestProject_WithXUnit_ReturnsTrue()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.9.0" />
              </ItemGroup>
            </Project>
            """);
        try
        {
            Assert.IsTrue(BindingRedirectAnalyzer.DetectIsTestProject(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectIsTestProject_WithNUnit_ReturnsTrue()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="NUnit" Version="4.0.0" />
              </ItemGroup>
            </Project>
            """);
        try
        {
            Assert.IsTrue(BindingRedirectAnalyzer.DetectIsTestProject(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectIsTestProject_NoTestRefs_ReturnsFalse()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
              </ItemGroup>
            </Project>
            """);
        try
        {
            Assert.IsFalse(BindingRedirectAnalyzer.DetectIsTestProject(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectIsTestProject_LegacyUnitTestFramework_ReturnsTrue()
    {
        string dir = CreateTempProjectDir("""
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="Microsoft.VisualStudio.QualityTools.UnitTestFramework" />
              </ItemGroup>
            </Project>
            """);
        try
        {
            Assert.IsTrue(BindingRedirectAnalyzer.DetectIsTestProject(dir));
        }
        finally { Cleanup(dir); }
    }

    #endregion

    #region DetectHasAppConfigForCompiler

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectHasAppConfigForCompiler_Present_ReturnsTrue()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
                <AppConfigForCompiler>app.config</AppConfigForCompiler>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsTrue(BindingRedirectAnalyzer.DetectHasAppConfigForCompiler(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectHasAppConfigForCompiler_UseVariant_ReturnsTrue()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
                <UseAppConfigForCompiler>true</UseAppConfigForCompiler>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsTrue(BindingRedirectAnalyzer.DetectHasAppConfigForCompiler(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void DetectHasAppConfigForCompiler_Absent_ReturnsFalse()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net48</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsFalse(BindingRedirectAnalyzer.DetectHasAppConfigForCompiler(dir));
        }
        finally { Cleanup(dir); }
    }

    #endregion
}
