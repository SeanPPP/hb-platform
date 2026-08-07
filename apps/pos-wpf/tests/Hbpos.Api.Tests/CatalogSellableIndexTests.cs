using System.Reflection;
using System.Text.Json;
using Hbpos.Api.Services;
using Hbpos.Contracts.Catalog;

namespace Hbpos.Api.Tests;

public sealed class CatalogSellableIndexTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Lookup_matches_normalized_lookup_exactly()
    {
        var index = CreateIndex(
            CreateItem("P01", "ABC123", "Exact item", 1m),
            CreateItem("P02", "ABC1234", "Longer item", 2m));

        var found = index.Lookup(" abc123 ", lookupCodeNormalized: null);
        var missed = index.Lookup("abc", lookupCodeNormalized: null);

        Assert.True(found.Found);
        Assert.NotNull(found.Item);
        Assert.Equal("ABC123", found.Item.LookupCodeNormalized);
        Assert.Equal("Exact item", found.Item.DisplayName);
        Assert.False(missed.Found);
        Assert.Null(missed.Item);
    }

    [Fact]
    public void Compare_returns_delete_for_local_lookup_missing_from_server()
    {
        var index = CreateIndex(CreateItem("P01", "NEW-CODE", "Current item", 1m));
        var request = new CatalogCompareRequest(
            "S01",
            [new CatalogLocalLookupVersionDto("S01", " old-code ", "OLD-CODE", UpdatedAt, "old-hash")]);

        var response = index.Compare(request);

        var deleted = Assert.Single(response.DeletedLookups);
        Assert.Equal("S01", deleted.StoreCode);
        Assert.Equal("old-code", deleted.LookupCode);
        Assert.Equal("OLD-CODE", deleted.LookupCodeNormalized);
        Assert.Empty(response.UpsertedLookups);
    }

    [Fact]
    public void Compare_only_returns_upserts_for_requested_local_lookup_page()
    {
        var index = CreateIndex(
            CreateItem("P01", "LOCAL-CODE", "Changed item", 2m),
            CreateItem("P02", "SERVER-ONLY", "Server only item", 3m));
        var request = new CatalogCompareRequest(
            "S01",
            [new CatalogLocalLookupVersionDto("S01", "local-code", "LOCAL-CODE", UpdatedAt, "stale-hash")]);

        var response = index.Compare(request);

        var upsert = Assert.Single(response.UpsertedLookups);
        Assert.Equal("LOCAL-CODE", upsert.LookupCodeNormalized);
        Assert.Empty(response.DeletedLookups);
    }

    [Fact]
    public void Compare_skips_upsert_when_hash_is_unchanged()
    {
        var index = CreateIndex(CreateItem("P01", "UNCHANGED", "Unchanged item", 1m));
        var current = Assert.Single(index.Items);
        var request = new CatalogCompareRequest(
            "S01",
            [new CatalogLocalLookupVersionDto("S01", "unchanged", "UNCHANGED", DateTimeOffset.UnixEpoch, current.RowVersion)]);

        var response = index.Compare(request);

        Assert.Empty(response.UpsertedLookups);
        Assert.Empty(response.DeletedLookups);
    }

    [Fact]
    public void ProductImage_is_returned_and_changes_row_version()
    {
        var firstIndex = CreateIndex(CreateItem(
            "P01",
            "IMAGE-CODE",
            "Image item",
            1m,
            "https://images.example/P01-a.jpg"));
        var secondIndex = CreateIndex(CreateItem(
            "P01",
            "IMAGE-CODE",
            "Image item",
            1m,
            "https://images.example/P01-b.jpg"));

        var pageItem = Assert.Single(firstIndex.GetPage(cursor: null, pageSize: 10).Items);
        var lookup = firstIndex.Lookup("image-code", lookupCodeNormalized: null);
        var changedItem = Assert.Single(secondIndex.Items);

        Assert.Equal("https://images.example/P01-a.jpg", pageItem.ProductImage);
        Assert.True(lookup.Found);
        Assert.Equal("https://images.example/P01-a.jpg", lookup.Item?.ProductImage);
        Assert.NotEqual(pageItem.RowVersion, changedItem.RowVersion);
    }

    [Fact]
    public void DiscountRate_is_returned_and_changes_row_version()
    {
        var firstIndex = CreateIndex(CreateItem(
            "P01",
            "DISCOUNT-CODE",
            "Discount item",
            1m,
            discountRate: 0.2m));
        var secondIndex = CreateIndex(CreateItem(
            "P01",
            "DISCOUNT-CODE",
            "Discount item",
            1m,
            discountRate: 0.3m));

        var pageItem = Assert.Single(firstIndex.GetPage(cursor: null, pageSize: 10).Items);
        var lookup = firstIndex.Lookup("discount-code", lookupCodeNormalized: null);
        var changedItem = Assert.Single(secondIndex.Items);

        Assert.Equal(0.2m, pageItem.DiscountRate);
        Assert.True(lookup.Found);
        Assert.Equal(0.2m, lookup.Item?.DiscountRate);
        Assert.NotEqual(pageItem.RowVersion, changedItem.RowVersion);
    }

    [Fact]
    public void IsSpecialProduct_is_returned_and_changes_row_version()
    {
        var firstIndex = CreateIndex(CreateItem(
            "P01",
            "SPECIAL-CODE",
            "Special item",
            1m,
            isSpecialProduct: false));
        var secondIndex = CreateIndex(CreateItem(
            "P01",
            "SPECIAL-CODE",
            "Special item",
            1m,
            isSpecialProduct: true));

        var pageItem = Assert.Single(firstIndex.GetPage(cursor: null, pageSize: 10).Items);
        var lookup = secondIndex.Lookup("special-code", lookupCodeNormalized: null);
        var changedItem = Assert.Single(secondIndex.Items);

        Assert.False(pageItem.IsSpecialProduct);
        Assert.True(lookup.Found);
        Assert.True(lookup.Item?.IsSpecialProduct);
        Assert.NotEqual(pageItem.RowVersion, changedItem.RowVersion);
    }

    [Fact]
    public void GetPage_uses_normalized_cursor_and_reports_next_cursor()
    {
        var index = CreateIndex(
            CreateItem("P02", "b-code", "B item", 2m),
            CreateItem("P01", "a-code", "A item", 1m),
            CreateItem("P03", "c-code", "C item", 3m));

        var firstPage = index.GetPage(cursor: null, pageSize: 2);
        var secondPage = index.GetPage(firstPage.NextCursor, pageSize: 2);

        Assert.Equal(["A-CODE", "B-CODE"], firstPage.Items.Select(x => x.LookupCodeNormalized).ToArray());
        Assert.True(firstPage.HasMore);
        Assert.Equal("B-CODE", firstPage.NextCursor);
        Assert.Equal(3, firstPage.TotalCount);
        var item = Assert.Single(secondPage.Items);
        Assert.Equal("C-CODE", item.LookupCodeNormalized);
        Assert.False(secondPage.HasMore);
        Assert.Null(secondPage.NextCursor);
        Assert.Equal(3, secondPage.TotalCount);
    }

    [Fact]
    public void GetPage_keeps_one_catalog_version_for_all_pages_in_the_same_index()
    {
        var index = CreateIndex(
            CreateItem("P01", "a-code", "A item", 1m),
            CreateItem("P02", "b-code", "B item", 2m),
            CreateItem("P03", "c-code", "C item", 3m));

        var firstPage = index.GetPage(cursor: null, pageSize: 2);
        var secondPage = index.GetPage(firstPage.NextCursor, pageSize: 2);

        Assert.StartsWith("catalog-v1:", firstPage.CatalogVersion, StringComparison.Ordinal);
        Assert.Equal(firstPage.CatalogVersion, secondPage.CatalogVersion);
        Assert.StartsWith("sha256-catalog-page-v1:", firstPage.PageChecksum, StringComparison.Ordinal);
        Assert.StartsWith("sha256-catalog-page-v1:", secondPage.PageChecksum, StringComparison.Ordinal);
        Assert.NotEqual(firstPage.PageChecksum, secondPage.PageChecksum);
    }

    [Fact]
    public void Rebuilt_index_gets_a_new_catalog_version_even_when_content_is_unchanged()
    {
        var item = CreateItem("P01", "a-code", "A item", 1m);

        var first = CreateIndex(item).GetPage(cursor: null, pageSize: 10);
        var rebuilt = CreateIndex(item).GetPage(cursor: null, pageSize: 10);

        Assert.NotEqual(first.CatalogVersion, rebuilt.CatalogVersion);
        Assert.Equal(first.PageChecksum, rebuilt.PageChecksum);
    }

    [Fact]
    public void Changed_item_changes_catalog_version_and_page_checksum()
    {
        var first = CreateIndex(CreateItem("P01", "a-code", "A item", 1m))
            .GetPage(cursor: null, pageSize: 10);
        var changed = CreateIndex(CreateItem("P01", "a-code", "Changed item", 1m))
            .GetPage(cursor: null, pageSize: 10);

        Assert.NotEqual(first.CatalogVersion, changed.CatalogVersion);
        Assert.NotEqual(first.PageChecksum, changed.PageChecksum);
    }

    [Fact]
    public void Page_checksum_uses_the_cross_platform_canonical_v1_vector()
    {
        var index = new CatalogSellableIndex(
            "S01",
            GeneratedAt,
            [
                new SellableItemDto(
                    "S01",
                    "P-001",
                    ReferenceCode: null,
                    "牛奶🥛",
                    "930000000001",
                    ItemNumber: "I-001",
                    Barcode: "source-barcode-is-not-the-offline-lookup",
                    RetailPrice: 12.34m,
                    PriceSourceKind.ProductBase,
                    "product",
                    QuantityFactor: 1m,
                    UpdatedAt: new DateTimeOffset(2026, 7, 28, 1, 2, 3, 456, TimeSpan.Zero),
                    ProductImage: null,
                    DiscountRate: null,
                    IsSpecialProduct: true)
            ]);

        var page = index.GetPage(cursor: null, pageSize: 10);

        Assert.Equal(
            "sha256-catalog-page-v1:4eb87e036003575ca8b8e9961ab6c21dbe63ed6d18482886f1da92e8f4165530",
            page.PageChecksum);
    }

    [Fact]
    public void Page_checksum_uses_the_JavaScript_observable_number_representation()
    {
        var index = new CatalogSellableIndex(
            "S01",
            GeneratedAt,
            [
                new SellableItemDto(
                    "S01",
                    "P-HIGH",
                    ReferenceCode: string.Empty,
                    "精度",
                    "HIGH",
                    ItemNumber: string.Empty,
                    Barcode: string.Empty,
                    RetailPrice: 0.1000000000000000000000000001m,
                    PriceSourceKind.ProductBase,
                    "product",
                    QuantityFactor: 12345678901234567890.123456789m,
                    UpdatedAt: null,
                    ProductImage: string.Empty,
                    DiscountRate: 0.3333333333333333333333333333m,
                    IsSpecialProduct: false)
            ]);

        var page = index.GetPage(cursor: null, pageSize: 10);

        Assert.Equal(
            "sha256-catalog-page-v1:86178b9aa03175a4dc97d8c61fa8db018507d0c29d60b43a5d65b47106681e44",
            page.PageChecksum);
    }

    [Fact]
    public void Page_checksum_v2_uses_binary64_big_endian_cross_platform_vector()
    {
        var index = new CatalogSellableIndex(
            "S01",
            GeneratedAt,
            [
                new SellableItemDto(
                    "S01",
                    "P-001",
                    ReferenceCode: null,
                    "牛奶🥛",
                    "930000000001",
                    ItemNumber: "I-001",
                    Barcode: "source-barcode-is-not-the-offline-lookup",
                    RetailPrice: 12.34m,
                    PriceSourceKind.ProductBase,
                    "product",
                    QuantityFactor: 1m,
                    UpdatedAt: new DateTimeOffset(2026, 7, 28, 1, 2, 3, 456, TimeSpan.Zero),
                    ProductImage: null,
                    DiscountRate: null,
                    IsSpecialProduct: true)
            ]);

        var page = index.GetPage(cursor: null, pageSize: 10, checksumVersion: 2);

        Assert.Equal(
            "sha256-catalog-page-v2:22181273b9791ad9664ad4f30ca2cddd3916ad9a012851490db28f7e1b229c27",
            page.PageChecksum);
    }

    [Fact]
    public void Page_checksum_v2_stabilizes_large_small_and_high_precision_numbers()
    {
        var index = new CatalogSellableIndex(
            "S01",
            GeneratedAt,
            [
                new SellableItemDto(
                    "S01",
                    "P-EDGE",
                    ReferenceCode: string.Empty,
                    "边界",
                    "EDGE",
                    ItemNumber: string.Empty,
                    Barcode: string.Empty,
                    RetailPrice: decimal.MaxValue,
                    PriceSourceKind.ProductBase,
                    "product",
                    QuantityFactor: 0.0000000000000000000000000001m,
                    UpdatedAt: null,
                    ProductImage: string.Empty,
                    DiscountRate: 0.1000000000000000000000000001m,
                    IsSpecialProduct: false)
            ]);

        var page = index.GetPage(cursor: null, pageSize: 10, checksumVersion: 2);

        Assert.Equal(
            "sha256-catalog-page-v2:c0b3e647f427c35d369335b42c512dc1b1e56fb31c1abc4154bfc1fc2498afba",
            page.PageChecksum);
    }

    [Fact]
    public void Binary64_formatter_matches_SystemTextJson_then_JavaScript_number_bits()
    {
        var formatter = typeof(CatalogSellableIndex).GetMethod(
            "FormatBinary64",
            BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(decimal)],
            modifiers: null);
        Assert.NotNull(formatter);

        decimal[] values =
        [
            12.340000m,
            decimal.MaxValue,
            0.0000000000000000000000000001m,
            0.1000000000000000000000000001m,
            -123456789.987654321m
        ];
        foreach (var value in values)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(value));
            var javascriptNumber = json.RootElement.GetDouble();
            var expected = unchecked((ulong)BitConverter.DoubleToInt64Bits(javascriptNumber))
                .ToString("x16");

            Assert.Equal(expected, formatter!.Invoke(null, [value]));
        }
    }

    [Fact]
    public void GetPage_rejects_unsupported_checksum_version()
    {
        var index = CreateIndex(CreateItem("P01", "a-code", "A item", 1m));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => index.GetPage(cursor: null, pageSize: 10, checksumVersion: 3));
    }

    [Fact]
    public void Page_checksum_changes_when_page_boundaries_change()
    {
        var index = CreateIndex(
            CreateItem("P01", "a-code", "A item", 1m),
            CreateItem("P02", "b-code", "B item", 2m));

        var oneItemPage = index.GetPage(cursor: null, pageSize: 1);
        var twoItemPage = index.GetPage(cursor: null, pageSize: 2);

        Assert.NotEqual(oneItemPage.PageChecksum, twoItemPage.PageChecksum);
    }

    [Fact]
    public void GetSpecialProductsPage_returns_only_special_items()
    {
        var index = CreateIndex(
            CreateItem("P01", "a-code", "A item", 1m, isSpecialProduct: true),
            CreateItem("P02", "b-code", "B item", 2m, isSpecialProduct: false),
            CreateItem("P03", "c-code", "C item", 3m, isSpecialProduct: true));

        var page = index.GetSpecialProductsPage(cursor: null, pageSize: 10);

        Assert.Equal(["A-CODE", "C-CODE"], page.Items.Select(x => x.LookupCodeNormalized).ToArray());
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public void GetSpecialProductsPage_deduplicates_same_product_across_lookup_codes()
    {
        // 一个商品对应多个 lookup_code（一商品多码）时，列表必须按商品去重，
        // 否则客户端下载校验会把重复 productCode 判为非法页而中止下载。
        var index = CreateIndex(
            CreateItem("P01", "a-code", "A item", 1m, isSpecialProduct: true),
            CreateItem("P01", "b-code", "A item second barcode", 2m, isSpecialProduct: true),
            CreateItem("P02", "c-code", "C item", 3m, isSpecialProduct: true));

        var page = index.GetSpecialProductsPage(cursor: null, pageSize: 10);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(["P01", "P02"], page.Items.Select(x => x.ProductCode).ToArray());
    }

    [Fact]
    public void GetPage_allows_download_batch_larger_than_one_thousand()
    {
        var items = Enumerable.Range(1, 1001)
            .Select(number => CreateItem($"P{number}", $"code-{number:0000}", $"Item {number}", number))
            .ToArray();
        var index = CreateIndex(items);

        var page = index.GetPage(cursor: null, pageSize: 5000);

        Assert.Equal(1001, page.Items.Count);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void GetDeltaPage_merges_fixed_versions_and_pages_upserts_and_deletes_by_lookup_key()
    {
        var baseIndex = new CatalogSellableIndex(
            "S01",
            GeneratedAt,
            [
                CreateItem("P01", "a-code", "Unchanged", 1m),
                CreateItem("P02", "b-code", "Removed", 2m),
                CreateItem("P03", "c-code", "Changed before", 3m)
            ],
            catalogVersion: "catalog-v1:base");
        var targetIndex = new CatalogSellableIndex(
            "S01",
            GeneratedAt.AddMinutes(1),
            [
                CreateItem("P01", "a-code", "Unchanged", 1m),
                CreateItem("P03", "c-code", "Changed after", 4m),
                CreateItem("P04", "d-code", "Added", 5m)
            ],
            catalogVersion: "catalog-v1:target");

        var first = targetIndex.GetDeltaPage(baseIndex, cursor: null, pageSize: 2);
        var second = targetIndex.GetDeltaPage(baseIndex, first.NextCursor, pageSize: 2);

        Assert.Equal("catalog-v1:base", first.BaseCatalogVersion);
        Assert.Equal("catalog-v1:target", first.TargetCatalogVersion);
        Assert.Equal(3, first.TargetTotal);
        Assert.Equal(["B-CODE"], first.DeletedLookups.Select(x => x.LookupCodeNormalized));
        Assert.Equal(["C-CODE"], first.Items.Select(x => x.LookupCodeNormalized));
        Assert.True(first.HasMore);
        Assert.Equal("C-CODE", first.NextCursor);
        Assert.StartsWith("sha256-catalog-delta-page-v1:", first.PageChecksum, StringComparison.Ordinal);

        Assert.Empty(second.DeletedLookups);
        Assert.Equal(["D-CODE"], second.Items.Select(x => x.LookupCodeNormalized));
        Assert.False(second.HasMore);
        Assert.Null(second.NextCursor);
        Assert.NotEqual(first.PageChecksum, second.PageChecksum);
    }

    [Fact]
    public void GetDeltaPageFromOperations_slices_precomputed_delta_without_remerging()
    {
        var baseline = CreateIndex(CreateItem("P01", "a", "Old", 1m));
        var target = CreateIndex(
            CreateItem("P01", "a", "Changed", 2m),
            CreateItem("P02", "b", "Added", 3m));
        var operations = target.GetDeltaOperations(baseline);

        var first = target.GetDeltaPageFromOperations(baseline, operations, cursor: null, pageSize: 1);
        var second = target.GetDeltaPageFromOperations(baseline, operations, first.NextCursor, pageSize: 1);

        Assert.Equal(["A"], first.Items.Select(item => item.LookupCodeNormalized));
        Assert.Equal(["B"], second.Items.Select(item => item.LookupCodeNormalized));
        Assert.Equal(2, operations.Count);
    }

    [Fact]
    public void Large_catalog_cursor_and_fixed_delta_page_start_after_the_exact_cursor()
    {
        const int total = 344_665;
        var items = Enumerable.Range(0, total)
            .Select(index => CreateItem($"P{index:D6}", $"K{index:D6}", "Item", 1m))
            .ToArray();
        var baseline = new CatalogSellableIndex("S01", GeneratedAt, [], "catalog-v1:base");
        var target = new CatalogSellableIndex("S01", GeneratedAt, items, "catalog-v1:target");
        var operations = target.GetDeltaOperations(baseline);

        var full = target.GetPage("K300000", pageSize: 2);
        var delta = target.GetDeltaPageFromOperations(baseline, operations, "K300000", pageSize: 2);

        Assert.Equal(["K300001", "K300002"], full.Items.Select(item => item.LookupCodeNormalized));
        Assert.Equal(["K300001", "K300002"], delta.Items.Select(item => item.LookupCodeNormalized));
    }

    private static CatalogSellableIndex CreateIndex(params SellableItemDto[] items)
    {
        return new CatalogSellableIndex("S01", GeneratedAt, items);
    }

    private static SellableItemDto CreateItem(
        string productCode,
        string lookupCode,
        string displayName,
        decimal retailPrice,
        string? productImage = null,
        decimal? discountRate = null,
        bool isSpecialProduct = false)
    {
        return new SellableItemDto(
            "S01",
            productCode,
            ReferenceCode: null,
            displayName,
            lookupCode,
            ItemNumber: null,
            Barcode: lookupCode,
            retailPrice,
            PriceSourceKind.ProductBase,
            "product",
            1m,
            UpdatedAt,
            productImage,
            discountRate,
            isSpecialProduct);
    }
}
