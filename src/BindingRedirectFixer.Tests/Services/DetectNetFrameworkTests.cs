using BindingRedirectFixer.Services;

namespace BindingRedirectFixer.Tests.Services;

[TestClass]
public class DetectNetFrameworkTests
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

    #region SDK-style single target

    [TestMethod]
    [TestCategory("Unit")]
    public void SdkStyle_Net10_ReturnsFalse()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsFalse(BindingRedirectAnalyzer.DetectNetFramework(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SdkStyle_Net48_ReturnsTrue()
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
            Assert.IsTrue(BindingRedirectAnalyzer.DetectNetFramework(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SdkStyle_Net80Windows_ReturnsFalse()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0-windows</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsFalse(BindingRedirectAnalyzer.DetectNetFramework(dir));
        }
        finally { Cleanup(dir); }
    }

    #endregion

    #region SDK-style multi-target

    [TestMethod]
    [TestCategory("Unit")]
    public void SdkStyle_MultiTarget_WithNet48_ReturnsTrue()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net10.0;net48</TargetFrameworks>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsTrue(BindingRedirectAnalyzer.DetectNetFramework(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SdkStyle_MultiTarget_AllModern_ReturnsFalse()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net10.0;net8.0</TargetFrameworks>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsFalse(BindingRedirectAnalyzer.DetectNetFramework(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SdkStyle_MultiTarget_WithNetstandard_ReturnsFalse()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net10.0;netstandard2.0</TargetFrameworks>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsFalse(BindingRedirectAnalyzer.DetectNetFramework(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void SdkStyle_MultiTarget_Net10WindowsAndNet48_ReturnsTrue()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net10.0-windows;net48</TargetFrameworks>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsTrue(BindingRedirectAnalyzer.DetectNetFramework(dir));
        }
        finally { Cleanup(dir); }
    }

    #endregion

    #region Legacy non-SDK project

    [TestMethod]
    [TestCategory("Unit")]
    public void Legacy_TargetFrameworkVersion_ReturnsTrue()
    {
        string dir = CreateTempProjectDir("""
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsTrue(BindingRedirectAnalyzer.DetectNetFramework(dir));
        }
        finally { Cleanup(dir); }
    }

    #endregion

    #region Edge cases

    [TestMethod]
    [TestCategory("Unit")]
    public void NoCsprojFile_ReturnsFalse()
    {
        string dir = Path.Combine(Path.GetTempPath(), "BRFTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.IsFalse(BindingRedirectAnalyzer.DetectNetFramework(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void NetcoreApp31_ReturnsFalse()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>netcoreapp3.1</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsFalse(BindingRedirectAnalyzer.DetectNetFramework(dir));
        }
        finally { Cleanup(dir); }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Net472_ReturnsTrue()
    {
        string dir = CreateTempProjectDir("""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net472</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        try
        {
            Assert.IsTrue(BindingRedirectAnalyzer.DetectNetFramework(dir));
        }
        finally { Cleanup(dir); }
    }

    #endregion
}
