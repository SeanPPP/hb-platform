using System.Drawing;

namespace Hbpos.Client.Tests;

public sealed class InnoInstallerScriptTests
{
    [Fact]
    public void Inno_script_launches_hb_pos_after_silent_update()
    {
        var script = ReadRepoFile("apps/pos-wpf/installer/inno/Hbpos.Client.Wpf.iss");

        Assert.Contains("[Run]", script);
        Assert.Contains("Filename: \"{app}\\{#AppExeName}\"", script);
        Assert.Contains("Flags: nowait runasoriginaluser", script);
        Assert.DoesNotContain("skipifsilent", script.ToLowerInvariant());
    }

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

    [Fact]
    public void Inno_script_uses_versioned_icon_file_for_shell_shortcuts()
    {
        var script = ReadRepoFile("apps/pos-wpf/installer/inno/Hbpos.Client.Wpf.iss");
        const string versionedIconPath = "{app}\\AppIcon-{#AppVersion}.ico";

        Assert.Contains(
            "Source: \"..\\..\\src\\Hbpos.Client.Wpf\\Resources\\AppIcon.ico\"; DestDir: \"{app}\"; DestName: \"AppIcon-{#AppVersion}.ico\"",
            script);
        Assert.Equal(2, script.Split($"IconFilename: \"{versionedIconPath}\"", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("IconFilename: \"{app}\\{#AppExeName}\"", script);
    }

    [Fact]
    public void Wpf_app_icon_matches_startup_brand_frame()
    {
        var iconBytes = File.ReadAllBytes(Path.Combine(
            FindRepoRoot(),
            "apps",
            "pos-wpf",
            "src",
            "Hbpos.Client.Wpf",
            "Resources",
            "AppIcon.ico"));
        var imageCount = BitConverter.ToUInt16(iconBytes, 4);
        var entries = Enumerable.Range(0, imageCount)
            .Select(index =>
            {
                var entryOffset = 6 + (index * 16);
                var width = iconBytes[entryOffset] == 0 ? 256 : iconBytes[entryOffset];
                return new
                {
                    Width = width,
                    ResourceLength = BitConverter.ToUInt32(iconBytes, entryOffset + 8),
                    ResourceOffset = BitConverter.ToUInt32(iconBytes, entryOffset + 12)
                };
            })
            .ToArray();

        Assert.Equal(new[] { 16, 24, 32, 48, 64, 128, 256 }, entries.Select(entry => entry.Width));

        var largestFrame = Assert.Single(entries, entry => entry.Width == 256);
        using var frameStream = new MemoryStream(
            iconBytes,
            checked((int)largestFrame.ResourceOffset),
            checked((int)largestFrame.ResourceLength),
            writable: false);
        using var bitmap = new Bitmap(frameStream);

        Assert.Equal(256, bitmap.Width);
        Assert.Equal(256, bitmap.Height);
        AssertColorNear(Color.FromArgb(0, 0, 0, 0), bitmap.GetPixel(0, 0));
        AssertColorNear(Color.FromArgb(255, 232, 240, 254), bitmap.GetPixel(32, 128));
        AssertColorNear(Color.FromArgb(255, 232, 240, 254), bitmap.GetPixel(128, 24));
        AssertColorNear(Color.FromArgb(255, 238, 88, 53), bitmap.GetPixel(64, 64));
    }

    private static void AssertColorNear(Color expected, Color actual, int tolerance = 4)
    {
        Assert.InRange(actual.A, Math.Max(0, expected.A - tolerance), Math.Min(255, expected.A + tolerance));
        Assert.InRange(actual.R, Math.Max(0, expected.R - tolerance), Math.Min(255, expected.R + tolerance));
        Assert.InRange(actual.G, Math.Max(0, expected.G - tolerance), Math.Min(255, expected.G + tolerance));
        Assert.InRange(actual.B, Math.Max(0, expected.B - tolerance), Math.Min(255, expected.B + tolerance));
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
