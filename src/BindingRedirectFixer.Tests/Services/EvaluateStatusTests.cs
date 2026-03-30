using BindingRedirectFixer.Models;
using BindingRedirectFixer.Services;

namespace BindingRedirectFixer.Tests.Services;

[TestClass]
public class EvaluateStatusTests
{
    private static AssemblyRedirectInfo CreateEntry(
        string name = "TestAssembly",
        string? resolvedVersion = null,
        string? physicalVersion = null,
        string? currentRedirectVersion = null,
        string publicKeyToken = "abc123",
        string configPublicKeyToken = "",
        string culture = "neutral")
    {
        return new AssemblyRedirectInfo
        {
            ProjectName = "TestProject",
            Name = name,
            ResolvedAssemblyVersion = resolvedVersion,
            PhysicalVersion = physicalVersion,
            CurrentRedirectVersion = currentRedirectVersion,
            PublicKeyToken = publicKeyToken,
            ConfigPublicKeyToken = configPublicKeyToken,
            Culture = culture
        };
    }

    #region Rule -1: DEPRECATED

    [TestMethod]
    [TestCategory("Unit")]
    public void Deprecated_WithRedirect_SetsRemoveRedirect()
    {
        var entry = CreateEntry(
            name: "Microsoft.Azure.Services.AppAuthentication",
            currentRedirectVersion: "1.6.2.0");

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: false);

        Assert.AreEqual(RedirectStatus.Deprecated, entry.Status);
        Assert.AreEqual(FixAction.RemoveRedirect, entry.SuggestedAction);
        Assert.IsTrue(entry.DiagnosticMessage.Contains("Azure.Identity"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Deprecated_WithoutRedirect_SetsNoAction()
    {
        var entry = CreateEntry(
            name: "Microsoft.Azure.Services.AppAuthentication",
            currentRedirectVersion: null);

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: false);

        Assert.AreEqual(RedirectStatus.Deprecated, entry.Status);
        Assert.AreEqual(FixAction.None, entry.SuggestedAction);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Deprecated_IncludesMigrationUrl()
    {
        var entry = CreateEntry(
            name: "Microsoft.Azure.Services.AppAuthentication",
            currentRedirectVersion: "1.6.2.0");

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: false);

        Assert.IsTrue(entry.DiagnosticMessage.Contains("https://"));
    }

    #endregion

    #region Rule 0: DUPLICATE

    [TestMethod]
    [TestCategory("Unit")]
    public void Duplicate_SetsRemoveDuplicate()
    {
        var entry = CreateEntry(
            resolvedVersion: "13.0.0.0",
            currentRedirectVersion: "13.0.0.0");

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: true);

        Assert.AreEqual(RedirectStatus.Duplicate, entry.Status);
        Assert.AreEqual(FixAction.RemoveDuplicate, entry.SuggestedAction);
    }

    #endregion

    #region Rule 0b: MISMATCH

    [TestMethod]
    [TestCategory("Unit")]
    public void Mismatch_BinOlderThanResolved_ConfigTargetsHigher_SetsRemoveRedirect()
    {
        var entry = CreateEntry(
            resolvedVersion: "10.0.0.0",
            physicalVersion: "8.1.2.0",
            currentRedirectVersion: "10.0.0.0");

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: false);

        Assert.AreEqual(RedirectStatus.Mismatch, entry.Status);
        Assert.AreEqual(FixAction.RemoveRedirect, entry.SuggestedAction);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Mismatch_BinNewerThanResolved_DoesNotTrigger()
    {
        // bin/ newer than resolved is normal (transitive dependency) — should not be MISMATCH
        var entry = CreateEntry(
            resolvedVersion: "8.0.0.0",
            physicalVersion: "10.0.0.0",
            currentRedirectVersion: "10.0.0.0");

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: false);

        Assert.AreNotEqual(RedirectStatus.Mismatch, entry.Status);
    }

    #endregion

    #region Rule 0c: ORPHANED / ORPHANED FW / TOKEN LOST

    [TestMethod]
    [TestCategory("Unit")]
    public void Orphaned_NoDll_ModernNet_SetsOrphaned()
    {
        var entry = CreateEntry(
            resolvedVersion: null,
            physicalVersion: null,
            currentRedirectVersion: "9.0.0.0",
            publicKeyToken: "",
            configPublicKeyToken: "cc7b13ffcd2ddd51");
        entry.IsNetFramework = false;

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: false);

        Assert.AreEqual(RedirectStatus.Orphaned, entry.Status);
        Assert.AreEqual(FixAction.RemoveRedirect, entry.SuggestedAction);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void OrphanedFw_NoDll_NetFramework_SetsOrphanedFramework()
    {
        var entry = CreateEntry(
            resolvedVersion: null,
            physicalVersion: null,
            currentRedirectVersion: "9.0.0.0",
            publicKeyToken: "",
            configPublicKeyToken: "cc7b13ffcd2ddd51");
        entry.IsNetFramework = true;

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: false);

        Assert.AreEqual(RedirectStatus.OrphanedFramework, entry.Status);
        Assert.AreEqual(FixAction.RemoveRedirect, entry.SuggestedAction);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Orphaned_NoDll_PreservesConfigToken()
    {
        var entry = CreateEntry(
            resolvedVersion: null,
            physicalVersion: null,
            currentRedirectVersion: "9.0.0.0",
            publicKeyToken: "",
            configPublicKeyToken: "cc7b13ffcd2ddd51");

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: false);

        Assert.AreEqual("cc7b13ffcd2ddd51", entry.PublicKeyToken);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void TokenLost_DllPresent_VersionMatches_SetsNoAction()
    {
        var entry = CreateEntry(
            resolvedVersion: "9.0.0.0",
            physicalVersion: null,
            currentRedirectVersion: "9.0.0.0",
            publicKeyToken: "",
            configPublicKeyToken: "cc7b13ffcd2ddd51");

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: false);

        Assert.AreEqual(RedirectStatus.TokenLost, entry.Status);
        Assert.AreEqual(FixAction.None, entry.SuggestedAction);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void TokenLost_DllPresent_VersionStale_SetsUpdateRedirect()
    {
        var entry = CreateEntry(
            resolvedVersion: "10.0.0.0",
            physicalVersion: null,
            currentRedirectVersion: "9.0.0.0",
            publicKeyToken: "",
            configPublicKeyToken: "cc7b13ffcd2ddd51");

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: false);

        Assert.AreEqual(RedirectStatus.TokenLost, entry.Status);
        Assert.AreEqual(FixAction.UpdateRedirect, entry.SuggestedAction);
    }

    #endregion

    #region Rule 1: MISSING

    [TestMethod]
    [TestCategory("Unit")]
    public void Missing_NoRedirect_HasConflict_InBin_SetsAddRedirect()
    {
        var entry = CreateEntry(
            resolvedVersion: "13.0.0.0",
            physicalVersion: "13.0.0.0",
            currentRedirectVersion: null);

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: true, hasDuplicateRedirects: false);

        Assert.AreEqual(RedirectStatus.Missing, entry.Status);
        Assert.AreEqual(FixAction.AddRedirect, entry.SuggestedAction);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Missing_NoRedirect_HasConflict_NotInBin_SetsOk()
    {
        // Assembly not in bin/ — no runtime binding conflict
        var entry = CreateEntry(
            resolvedVersion: "13.0.0.0",
            physicalVersion: null,
            currentRedirectVersion: null);

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: true, hasDuplicateRedirects: false);

        Assert.AreEqual(RedirectStatus.OK, entry.Status);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Missing_NoRedirect_NoConflict_SetsOk()
    {
        var entry = CreateEntry(
            resolvedVersion: "13.0.0.0",
            physicalVersion: "13.0.0.0",
            currentRedirectVersion: null);

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: false);

        Assert.AreEqual(RedirectStatus.OK, entry.Status);
    }

    #endregion

    #region Rule 2: STALE

    [TestMethod]
    [TestCategory("Unit")]
    public void Stale_RedirectDoesNotMatchTarget_SetsUpdateRedirect()
    {
        var entry = CreateEntry(
            resolvedVersion: "13.0.0.0",
            physicalVersion: "13.0.0.0",
            currentRedirectVersion: "12.0.0.0");

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: false);

        Assert.AreEqual(RedirectStatus.Stale, entry.Status);
        Assert.AreEqual(FixAction.UpdateRedirect, entry.SuggestedAction);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Stale_BinHigherThanResolved_UsesHighest()
    {
        // bin/ is 14.0.0.0, resolved is 13.0.0.0 — target should be 14.0.0.0
        var entry = CreateEntry(
            resolvedVersion: "13.0.0.0",
            physicalVersion: "14.0.0.0",
            currentRedirectVersion: "13.0.0.0");

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: false);

        Assert.AreEqual(RedirectStatus.Stale, entry.Status);
        Assert.AreEqual(FixAction.UpdateRedirect, entry.SuggestedAction);
        Assert.IsTrue(entry.DiagnosticMessage.Contains("14.0.0.0"));
    }

    #endregion

    #region Rule 3: OK with informational note

    [TestMethod]
    [TestCategory("Unit")]
    public void Ok_BinDiffersFromResolved_NoRedirect_SetsOkWithMessage()
    {
        var entry = CreateEntry(
            resolvedVersion: "13.0.0.0",
            physicalVersion: "14.0.0.0",
            currentRedirectVersion: null);

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: false);

        Assert.AreEqual(RedirectStatus.OK, entry.Status);
        Assert.AreEqual(FixAction.None, entry.SuggestedAction);
        Assert.IsFalse(string.IsNullOrEmpty(entry.DiagnosticMessage));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Ok_BinOlderThanResolved_NoRedirect_MentionsFrameworkAssembly()
    {
        var entry = CreateEntry(
            resolvedVersion: "13.0.0.0",
            physicalVersion: "8.0.0.0",
            currentRedirectVersion: null);

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: false);

        Assert.AreEqual(RedirectStatus.OK, entry.Status);
        Assert.IsTrue(entry.DiagnosticMessage.Contains("runtime/GAC"));
    }

    #endregion

    #region Rule 4: OK — all agree

    [TestMethod]
    [TestCategory("Unit")]
    public void Ok_AllVersionsMatch_SetsOk()
    {
        var entry = CreateEntry(
            resolvedVersion: "13.0.0.0",
            physicalVersion: "13.0.0.0",
            currentRedirectVersion: "13.0.0.0");

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: false);

        Assert.AreEqual(RedirectStatus.OK, entry.Status);
        Assert.AreEqual(FixAction.None, entry.SuggestedAction);
        Assert.AreEqual(string.Empty, entry.DiagnosticMessage);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Ok_NoVersionsAtAll_SetsOk()
    {
        var entry = CreateEntry(
            resolvedVersion: null,
            physicalVersion: null,
            currentRedirectVersion: null);

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: false);

        Assert.AreEqual(RedirectStatus.OK, entry.Status);
    }

    #endregion

    #region Rule Priority

    [TestMethod]
    [TestCategory("Unit")]
    public void Deprecated_TakesPriorityOverDuplicate()
    {
        var entry = CreateEntry(
            name: "WindowsAzure.Storage",
            currentRedirectVersion: "9.0.0.0");

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: true);

        Assert.AreEqual(RedirectStatus.Deprecated, entry.Status);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Duplicate_TakesPriorityOverStale()
    {
        var entry = CreateEntry(
            resolvedVersion: "13.0.0.0",
            currentRedirectVersion: "12.0.0.0");

        BindingRedirectAnalyzer.EvaluateStatus(entry, hasVersionConflict: false, hasDuplicateRedirects: true);

        Assert.AreEqual(RedirectStatus.Duplicate, entry.Status);
    }

    #endregion
}
