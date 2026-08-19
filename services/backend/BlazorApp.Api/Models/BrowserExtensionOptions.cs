namespace BlazorApp.Api.Models;

public sealed class BrowserExtensionOptions
{
    public const string SectionName = "BrowserExtension";

    public string LatestVersion { get; set; } = "1.0.0";
    public string MinimumVersion { get; set; } = "1.0.0";
    public string ChromeStoreUrl { get; set; } = string.Empty;
    public string EdgeStoreUrl { get; set; } = string.Empty;
    public string ReleaseNotesZh { get; set; } = "首个内部测试版本";
    public string ReleaseNotesEn { get; set; } = "Initial internal release";
    public string ConfigVersion { get; set; } = "1";
    public bool UseBuiltInDatsProfile { get; set; } = true;
    public List<BrowserExtensionSupplierProfileOptions> SupplierProfiles { get; set; } = new();
}

public sealed class BrowserExtensionSupplierProfileOptions
{
    public string SupplierCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public List<string> Origins { get; set; } = new();
    public List<string> ListPagePatterns { get; set; } = new();
    public string CardSelector { get; set; } = string.Empty;
    public string ItemNumberSource { get; set; } = "attribute";
    public string? ItemNumberSelector { get; set; }
    public string? ItemNumberAttribute { get; set; }
    public List<string> ItemNumberTransforms { get; set; } = new();
    public string MountSelector { get; set; } = string.Empty;
    public string MountPosition { get; set; } = "afterend";

    public static BrowserExtensionSupplierProfileOptions CreateDatsDefault() =>
        new()
        {
            SupplierCode = "DATS",
            DisplayName = "DATS",
            Origins = new List<string> { "https://www.dats.com.au/*" },
            ListPagePatterns = new List<string> { "https://www.dats.com.au/*" },
            CardSelector = ".product[data-product-code]",
            ItemNumberSource = "attribute",
            ItemNumberAttribute = "data-product-code",
            ItemNumberTransforms = new List<string> { "trim", "uppercase" },
            MountSelector = ".widget-productlist-code",
            MountPosition = "afterend",
        };
}
