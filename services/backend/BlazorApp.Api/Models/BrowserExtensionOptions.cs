namespace BlazorApp.Api.Models;

public sealed class BrowserExtensionOptions
{
    public const string SectionName = "BrowserExtension";

    public string LatestVersion { get; set; } = "1.2.0";
    public string MinimumVersion { get; set; } = "1.1.0";
    public string ChromeStoreUrl { get; set; } = string.Empty;
    public string EdgeStoreUrl { get; set; } = string.Empty;
    public string ReleaseNotesZh { get; set; } = "新增 Jemark、GFA、TXK 和 Boom Up 供应商支持";
    public string ReleaseNotesEn { get; set; } = "Adds Jemark, GFA, TXK and Boom Up supplier support";
    public string ConfigVersion { get; set; } = "6";
    public bool UseBuiltInDatsProfile { get; set; } = true;
    public bool UseBuiltInSupplierProfiles { get; set; } = true;
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
            // DATS 是显示名称；HB 的供应商业务代码是 240。
            SupplierCode = "240",
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

    public static IReadOnlyList<BrowserExtensionSupplierProfileOptions> CreateSupplierDefaults() =>
        new List<BrowserExtensionSupplierProfileOptions>
        {
            new()
            {
                SupplierCode = "243",
                DisplayName = "Brazco",
                Origins = new List<string> { "https://www.brazcoint.com.au/*" },
                ListPagePatterns = new List<string>
                {
                    "https://www.brazcoint.com.au/*.html*",
                },
                CardSelector = ".product-listing-record",
                ItemNumberSource = "text",
                ItemNumberSelector = ".product-listing-code",
                ItemNumberTransforms = new List<string> { "after-colon", "trim", "uppercase" },
                MountSelector = ".product-listing-code",
                MountPosition = "afterend",
            },
            new()
            {
                SupplierCode = "227",
                DisplayName = "Malmar",
                Origins = new List<string> { "https://www.malmar.com.au/*" },
                ListPagePatterns = new List<string>
                {
                    "https://www.malmar.com.au/Products.aspx*",
                    "https://www.malmar.com.au/products/*.htm*",
                },
                CardSelector = "li.item-thumbs",
                ItemNumberSource = "text",
                ItemNumberSelector = ".p-spec strong",
                ItemNumberTransforms = new List<string> { "trim", "uppercase" },
                MountSelector = ".p-spec",
                MountPosition = "afterend",
            },
            new()
            {
                SupplierCode = "226",
                DisplayName = "Meteor Party",
                Origins = new List<string> { "https://www.meteorparty.com.au/*" },
                ListPagePatterns = new List<string>
                {
                    "https://www.meteorparty.com.au/balloons/*",
                    "https://www.meteorparty.com.au/Party*",
                    "https://www.meteorparty.com.au/Events*",
                    "https://www.meteorparty.com.au/Seasonal*",
                    "https://www.meteorparty.com.au/Tableware*",
                    "https://www.meteorparty.com.au/Candles*",
                },
                CardSelector = ".facets-item-cell-grid[data-sku]",
                ItemNumberSource = "attribute",
                ItemNumberAttribute = "data-sku",
                ItemNumberTransforms = new List<string> { "trim", "uppercase" },
                MountSelector = ".facets-item-cell-grid-title",
                MountPosition = "afterend",
            },
            new()
            {
                SupplierCode = "201",
                DisplayName = "Yatsal",
                Origins = new List<string>
                {
                    "https://yatsal.com.au/*",
                    "https://www.yatsal.com.au/*",
                },
                ListPagePatterns = new List<string>
                {
                    "https://yatsal.com.au/*",
                    "https://www.yatsal.com.au/*",
                },
                CardSelector = ".product[data-product-code]",
                ItemNumberSource = "attribute",
                ItemNumberAttribute = "data-product-code",
                ItemNumberTransforms = new List<string> { "trim", "uppercase" },
                MountSelector = ".widget-productlist-code",
                MountPosition = "afterend",
            },
            new()
            {
                SupplierCode = "203",
                DisplayName = "Windragon",
                Origins = new List<string> { "https://windragon.com.au/*" },
                ListPagePatterns = new List<string>
                {
                    "https://windragon.com.au/product-category/*",
                },
                CardSelector = "li.product",
                ItemNumberSource = "text",
                ItemNumberSelector = ".sku",
                ItemNumberTransforms = new List<string> { "trim", "uppercase" },
                MountSelector = ".sku",
                MountPosition = "afterend",
            },
            new()
            {
                SupplierCode = "225",
                DisplayName = "MNB",
                Origins = new List<string> { "https://www.mnb.com.au/*" },
                ListPagePatterns = new List<string>
                {
                    "https://www.mnb.com.au/*.html*",
                },
                CardSelector = ".product-listing-record",
                ItemNumberSource = "text",
                ItemNumberSelector = ".product-listing-code",
                ItemNumberTransforms = new List<string> { "after-colon", "trim", "uppercase" },
                MountSelector = ".product-listing-code",
                MountPosition = "afterend",
            },
            new()
            {
                SupplierCode = "218",
                DisplayName = "PJ SAS",
                Origins = new List<string> { "https://www.pjsas.com.au/*" },
                ListPagePatterns = new List<string>
                {
                    "https://www.pjsas.com.au/*.html*",
                },
                CardSelector = ".product-listing-record",
                ItemNumberSource = "text",
                ItemNumberSelector = ".product-listing-code",
                ItemNumberTransforms = new List<string> { "after-colon", "trim", "uppercase" },
                MountSelector = ".product-listing-code",
                MountPosition = "afterend",
            },
            new()
            {
                SupplierCode = "267",
                DisplayName = "Jemark",
                Origins = new List<string> { "https://www.jemark.com.au/*" },
                ListPagePatterns = new List<string>
                {
                    "https://www.jemark.com.au/category/*",
                },
                CardSelector = "ul.products li.product",
                ItemNumberSource = "text",
                ItemNumberSelector = ".model",
                ItemNumberTransforms = new List<string> { "trim", "uppercase" },
                MountSelector = ".model",
                MountPosition = "afterend",
            },
            new()
            {
                SupplierCode = "236",
                DisplayName = "GFA",
                Origins = new List<string> { "https://gfa.opmetrix.store/*" },
                ListPagePatterns = new List<string>
                {
                    "https://gfa.opmetrix.store/products/view*",
                },
                CardSelector = ".list-row[data-product]",
                ItemNumberSource = "attribute",
                ItemNumberAttribute = "data-product",
                ItemNumberTransforms = new List<string>
                {
                    "trim",
                    "uppercase",
                    "underscore-to-slash",
                },
                MountSelector = ".content > a[href*='/product/view?id=']",
                MountPosition = "afterend",
            },
            new()
            {
                SupplierCode = "SP2502280001",
                DisplayName = "TXK",
                Origins = new List<string> { "http://txkorders.inzantsales.com/*" },
                ListPagePatterns = new List<string>
                {
                    "http://txkorders.inzantsales.com/shop*",
                },
                CardSelector = ".single-product.grid-view",
                ItemNumberSource = "text",
                ItemNumberSelector = ".sku",
                ItemNumberTransforms = new List<string> { "after-sku", "trim", "uppercase" },
                MountSelector = ".price-box",
                MountPosition = "afterend",
            },
            new()
            {
                SupplierCode = "SP0101",
                DisplayName = "Boom Up",
                Origins = new List<string> { "https://boomup.com.au/*" },
                ListPagePatterns = new List<string>
                {
                    "https://boomup.com.au/shop*",
                    "https://boomup.com.au/product-category/*",
                },
                CardSelector = "main ul.products li.product",
                ItemNumberSource = "text",
                ItemNumberSelector = ".custom_sku",
                ItemNumberTransforms = new List<string> { "trim", "uppercase" },
                MountSelector = "h2.woocommerce-loop-product__title",
                MountPosition = "afterend",
            },
        };
}
