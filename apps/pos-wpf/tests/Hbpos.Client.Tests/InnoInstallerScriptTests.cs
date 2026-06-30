namespace Hbpos.Client.Tests;

public sealed class InnoInstallerScriptTests
{
    [Fact]
    public void Inno_script_uninstalls_legacy_msi_by_configured_product_codes()
    {
        var script = ReadRepoFile("apps/pos-wpf/installer/inno/Hbpos.Client.Wpf.iss");

        Assert.Contains("#ifndef LegacyMsiProductCodes", script);
        Assert.Contains("function UninstallLegacyMsiProductCode", script);
        Assert.Contains("Exec('msiexec.exe'", script);
        Assert.Contains("function PrepareToInstall", script);
    }

    [Fact]
    public void Inno_script_uses_conservative_registry_fallback_for_legacy_msi()
    {
        var script = ReadRepoFile("apps/pos-wpf/installer/inno/Hbpos.Client.Wpf.iss");

        Assert.Contains("function IsLegacyHbPosMsiEntry", script);
        Assert.Contains("DisplayName", script);
        Assert.Contains("Publisher", script);
        Assert.Contains("InstallLocation", script);
        Assert.Contains("FindAndUninstallLegacyMsiEntries", script);
        Assert.Contains("{autopf32}\\HB POS", script);
    }

    [Fact]
    public void Build_script_passes_legacy_msi_product_codes_to_inno()
    {
        var script = ReadRepoFile("apps/pos-wpf/scripts/Build-WpfInnoInstaller.ps1");

        Assert.Contains("[string[]]$LegacyMsiProductCode", script);
        Assert.Contains("$legacyMsiProductCodePattern", script);
        Assert.Contains("Legacy MSI ProductCode must be a GUID", script);
        Assert.Contains("/DLegacyMsiProductCodes=", script);
    }

    [Fact]
    public void Build_script_requires_explicit_opt_in_for_noncommercial_inno_builds()
    {
        var script = ReadRepoFile("apps/pos-wpf/scripts/Build-WpfInnoInstaller.ps1");

        Assert.Contains("[switch]$AllowNonCommercialBuild", script);
        Assert.Contains("AllowNonCommercialBuild", script);
        Assert.Contains("throw \"Inno Setup commercial license key is not active", script);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var repoRoot = FindRepoRoot();
        return File.ReadAllText(Path.Combine(repoRoot, relativePath));
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "apps", "pos-wpf")) &&
                Directory.Exists(Path.Combine(current.FullName, "services", "backend")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
