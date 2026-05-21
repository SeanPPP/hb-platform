using Hbpos.Contracts.Catalog;

namespace Hbpos.Api.Services;

public interface IPriceIndexBuilder
{
    IReadOnlyList<SellableItemDto> Build(string storeCode, PriceIndexInput input);
}

public sealed class PriceIndexBuilder : IPriceIndexBuilder
{
    public IReadOnlyList<SellableItemDto> Build(string storeCode, PriceIndexInput input)
    {
        var storePrices = input.StoreRetailPrices
            .Where(x => HasText(x.ProductCode) && x.StoreRetailPriceValue.HasValue)
            .GroupBy(x => x.ProductCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(p => p.UpdatedAt).First(), StringComparer.OrdinalIgnoreCase);

        var multiBySetCode = input.StoreMultiCodeProducts
            .Where(x => HasText(x.MultiCodeProductCode) && x.MultiCodeRetailPrice.HasValue)
            .GroupBy(x => x.MultiCodeProductCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(p => p.UpdatedAt).First(), StringComparer.OrdinalIgnoreCase);

        var products = input.Products
            .Where(x => HasText(x.ProductCode))
            .GroupBy(x => x.ProductCode!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var items = new List<SellableItemDto>();

        foreach (var clearance in input.StoreClearancePrices.Where(x => HasText(x.ClearanceBarcode) && x.ClearancePrice.HasValue))
        {
            products.TryGetValue(clearance.ProductCode ?? string.Empty, out var product);
            items.Add(CreateItem(
                storeCode,
                product,
                clearance.ProductCode,
                clearance.ClearanceBarcode!,
                clearance.ClearancePrice!.Value,
                PriceSourceKind.StoreClearancePrice,
                "clearance",
                clearance.UpdatedAt));
        }

        foreach (var multi in input.StoreMultiCodeProducts.Where(x => HasText(x.MultiBarcode) && x.MultiCodeRetailPrice.HasValue))
        {
            products.TryGetValue(multi.ProductCode ?? string.Empty, out var product);
            items.Add(CreateItem(
                storeCode,
                product,
                multi.ProductCode,
                multi.MultiBarcode!,
                multi.MultiCodeRetailPrice!.Value,
                PriceSourceKind.StoreMultiCodeProduct,
                "multi-code",
                multi.UpdatedAt));
        }

        foreach (var set in input.ProductSetCodes.Where(x => HasText(x.SetBarcode)))
        {
            products.TryGetValue(set.ProductCode, out var product);
            var hasStoreMultiPrice = multiBySetCode.TryGetValue(set.SetProductCode, out var storeMultiPrice);
            var price = hasStoreMultiPrice
                ? storeMultiPrice!.MultiCodeRetailPrice!.Value
                : set.SetRetailPrice ?? 0m;
            var source = hasStoreMultiPrice
                ? PriceSourceKind.StoreMultiCodeProduct
                : PriceSourceKind.ProductSetCode;
            var updatedAt = Latest(set.UpdatedAt, storeMultiPrice?.UpdatedAt);

            items.Add(CreateItem(
                storeCode,
                product,
                set.ProductCode,
                set.SetBarcode!,
                price,
                source,
                hasStoreMultiPrice ? "set-store-multi-code" : "set",
                updatedAt));
        }

        foreach (var product in input.Products.Where(x => HasText(x.ProductCode)))
        {
            storePrices.TryGetValue(product.ProductCode!, out var storePrice);
            var price = storePrice?.StoreRetailPriceValue ?? product.RetailPrice ?? 0m;
            var source = storePrice?.StoreRetailPriceValue is null
                ? PriceSourceKind.ProductBase
                : PriceSourceKind.StoreRetailPrice;
            var updatedAt = Latest(product.UpdatedAt, storePrice?.UpdatedAt);

            AddProductLookup(items, storeCode, product, product.Barcode, price, source, updatedAt);
            if (!StringComparer.OrdinalIgnoreCase.Equals(product.Barcode, product.ItemNumber))
            {
                AddProductLookup(items, storeCode, product, product.ItemNumber, price, source, updatedAt);
            }
        }

        return items
            .Where(x => input.Since is null || x.UpdatedAt is null || x.UpdatedAt >= input.Since)
            .GroupBy(x => NormalizeLookupKey(x.LookupCode), StringComparer.Ordinal)
            .Select(x => x
                .OrderByDescending(i => i.PriceSource)
                .ThenByDescending(i => i.UpdatedAt ?? DateTimeOffset.MinValue)
                .First())
            .OrderBy(x => NormalizeLookupKey(x.LookupCode), StringComparer.Ordinal)
            .ToList();
    }

    private static void AddProductLookup(
        List<SellableItemDto> items,
        string storeCode,
        ProductPriceRecord product,
        string? lookupCode,
        decimal price,
        PriceSourceKind source,
        DateTimeOffset? updatedAt)
    {
        if (!HasText(lookupCode))
        {
            return;
        }

        items.Add(CreateItem(
            storeCode,
            product,
            product.ProductCode,
            lookupCode!,
            price,
            source,
            source == PriceSourceKind.StoreRetailPrice ? "store-retail" : "product",
            updatedAt));
    }

    private static SellableItemDto CreateItem(
        string storeCode,
        ProductPriceRecord? product,
        string? productCode,
        string lookupCode,
        decimal retailPrice,
        PriceSourceKind source,
        string label,
        DateTimeOffset? updatedAt)
    {
        var trimmedLookupCode = lookupCode.Trim();

        return new SellableItemDto(
            storeCode,
            productCode ?? product?.ProductCode ?? string.Empty,
            null,
            product?.DisplayName ?? productCode ?? lookupCode,
            trimmedLookupCode,
            product?.ItemNumber,
            product?.Barcode,
            retailPrice,
            source,
            label,
            1m,
            updatedAt,
            product?.ProductImage);
    }

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string NormalizeLookupKey(string value) => value.Trim().ToUpperInvariant();

    private static DateTimeOffset? Latest(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return left > right ? left : right;
    }
}

public sealed record PriceIndexInput(
    DateTimeOffset? Since,
    IReadOnlyList<ProductPriceRecord> Products,
    IReadOnlyList<StoreRetailPriceRecord> StoreRetailPrices,
    IReadOnlyList<StoreMultiCodeProductRecord> StoreMultiCodeProducts,
    IReadOnlyList<StoreClearancePriceRecord> StoreClearancePrices,
    IReadOnlyList<ProductSetCodeRecord> ProductSetCodes);

public sealed record ProductPriceRecord(
    string? ProductCode,
    string DisplayName,
    string? ItemNumber,
    string? Barcode,
    decimal? RetailPrice,
    DateTimeOffset? UpdatedAt,
    string? ProductImage = null);

public sealed record StoreRetailPriceRecord(
    string? ProductCode,
    decimal? StoreRetailPriceValue,
    DateTimeOffset? UpdatedAt);

public sealed record StoreMultiCodeProductRecord(
    string? ProductCode,
    string? MultiCodeProductCode,
    string? MultiBarcode,
    decimal? MultiCodeRetailPrice,
    DateTimeOffset? UpdatedAt);

public sealed record StoreClearancePriceRecord(
    string? ProductCode,
    string? ClearanceBarcode,
    decimal? ClearancePrice,
    DateTimeOffset? UpdatedAt);

public sealed record ProductSetCodeRecord(
    string ProductCode,
    string SetProductCode,
    string? SetBarcode,
    decimal? SetRetailPrice,
    DateTimeOffset? UpdatedAt);
