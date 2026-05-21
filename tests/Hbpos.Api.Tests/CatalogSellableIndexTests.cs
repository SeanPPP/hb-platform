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
        var item = Assert.Single(secondPage.Items);
        Assert.Equal("C-CODE", item.LookupCodeNormalized);
        Assert.False(secondPage.HasMore);
        Assert.Null(secondPage.NextCursor);
    }

    private static CatalogSellableIndex CreateIndex(params SellableItemDto[] items)
    {
        return new CatalogSellableIndex("S01", GeneratedAt, items);
    }

    private static SellableItemDto CreateItem(
        string productCode,
        string lookupCode,
        string displayName,
        decimal retailPrice)
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
            UpdatedAt);
    }
}
