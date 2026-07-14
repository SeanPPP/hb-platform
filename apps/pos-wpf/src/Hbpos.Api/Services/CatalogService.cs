using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using Hbpos.Api.Data;
using Hbpos.Contracts.Catalog;
using SqlSugar;

namespace Hbpos.Api.Services;

public interface ICatalogService
{
    Task<IReadOnlyList<StoreDto>> GetStoresAsync(CancellationToken cancellationToken);

    Task<SellableItemsResponse?> GetSellableItemsAsync(
        string storeCode,
        DateTimeOffset? since,
        CancellationToken cancellationToken);

    Task<CatalogSyncPageResponse?> GetSellableItemsPageAsync(
        string storeCode,
        DateTimeOffset? since,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken);

    Task<CatalogCompareResponse?> CompareSellableItemsAsync(
        CatalogCompareRequest request,
        CancellationToken cancellationToken);

    Task<CatalogPromotionsResponse?> GetPromotionRulesAsync(
        string storeCode,
        CancellationToken cancellationToken);

    Task<CatalogLookupResponse?> LookupSellableItemAsync(
        string storeCode,
        string? lookupCode,
        string? lookupCodeNormalized,
        CancellationToken cancellationToken);

    Task<CatalogSpecialProductsPageResponse?> GetSpecialProductsPageAsync(
        string storeCode,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken);

    Task<CatalogSpecialProductMarkServiceResult> MarkSpecialProductAsync(
        CatalogSpecialProductMarkRequest request,
        string updatedBy,
        CancellationToken cancellationToken);
}

public sealed record CatalogSpecialProductMarkServiceResult(
    bool Success,
    CatalogSpecialProductMarkResponse? Response,
    string? ErrorCode,
    string? Message)
{
    public static CatalogSpecialProductMarkServiceResult Ok(CatalogSpecialProductMarkResponse response) =>
        new(true, response, null, null);

    public static CatalogSpecialProductMarkServiceResult Fail(string errorCode, string message) =>
        new(false, null, errorCode, message);
}

public sealed class CatalogService(
    HbposSqlSugarContext dbContext,
    IPriceIndexBuilder priceIndexBuilder,
    ICatalogIndexCache catalogIndexCache) : ICatalogService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> StoreRetailPriceEnsureLocks = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<StoreDto>> GetStoresAsync(CancellationToken cancellationToken)
    {
        var stores = await dbContext.MainDb.Queryable<Store>()
            .Where(x => x.IsActive && !x.IsDeleted)
            .OrderBy(x => x.StoreName)
            .OrderBy(x => x.StoreCode)
            .ToListAsync(cancellationToken);

        return stores
            .Select(x => new StoreDto(x.StoreCode, x.StoreName, x.IsActive))
            .ToArray();
    }

    public async Task<SellableItemsResponse?> GetSellableItemsAsync(
        string storeCode,
        DateTimeOffset? since,
        CancellationToken cancellationToken)
    {
        var index = await BuildSellableIndexAsync(storeCode, since, cancellationToken);
        return index is null
            ? null
            : new SellableItemsResponse(index.StoreCode, index.GeneratedAt, index.SellableItems);
    }

    public async Task<CatalogSyncPageResponse?> GetSellableItemsPageAsync(
        string storeCode,
        DateTimeOffset? since,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var index = await BuildSellableIndexAsync(storeCode, since, cancellationToken);
        return index?.CatalogIndex.GetPage(cursor, pageSize);
    }

    public async Task<CatalogCompareResponse?> CompareSellableItemsAsync(
        CatalogCompareRequest request,
        CancellationToken cancellationToken)
    {
        var index = await BuildSellableIndexAsync(request.StoreCode, since: null, cancellationToken);
        return index?.CatalogIndex.Compare(request);
    }

    public async Task<CatalogPromotionsResponse?> GetPromotionRulesAsync(
        string storeCode,
        CancellationToken cancellationToken)
    {
        var normalizedStoreCode = NormalizeStoreCode(storeCode);
        if (string.IsNullOrEmpty(normalizedStoreCode))
        {
            return null;
        }

        var store = await dbContext.MainDb.Queryable<Store>()
            .FirstAsync(x => x.StoreCode == normalizedStoreCode && x.IsActive && !x.IsDeleted, cancellationToken);
        if (store is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var promotions = await dbContext.MainDb.Queryable<Promotion>()
            .Where(p =>
                !p.IsDeleted &&
                p.IsEnabled &&
                p.ApplyQuantity > 0 &&
                p.FixedPrice >= 0m &&
                p.EffectiveStart <= now &&
                p.EffectiveEnd >= now)
            .Where(p =>
                SqlFunc
                    .Subqueryable<PromotionStore>()
                    .Where(ps =>
                        !ps.IsDeleted &&
                        ps.PromotionId == p.Id &&
                        ps.StoreCode == normalizedStoreCode)
                    .Any())
            .OrderByDescending(p => p.IsExclusive)
            .OrderByDescending(p => p.Priority)
            .ToListAsync(cancellationToken);

        var promotionIds = promotions.Select(p => p.Id).ToArray();
        var products = promotionIds.Length == 0
            ? new List<PromotionProduct>()
            : await dbContext.MainDb.Queryable<PromotionProduct>()
                .Where(product => !product.IsDeleted && promotionIds.Contains(product.PromotionId))
                .ToListAsync(cancellationToken);
        var productsByPromotion = products
            .GroupBy(product => product.PromotionId)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        // 只同步有商品明细的有效规则；收银端离线计算不再访问后端评估接口。
        var rules = promotions
            .Select(promotion => new CatalogPromotionRuleDto(
                promotion.Id,
                promotion.Name,
                promotion.IsExclusive,
                promotion.Priority,
                promotion.ApplyQuantity,
                promotion.FixedPrice,
                promotion.MaxApplicationsPerOrder,
                ToOffset(promotion.EffectiveStart) ?? DateTimeOffset.MinValue,
                ToOffset(promotion.EffectiveEnd) ?? DateTimeOffset.MinValue,
                promotion.UpdatedAt.HasValue ? ToOffset(promotion.UpdatedAt.Value) : null,
                productsByPromotion.TryGetValue(promotion.Id, out var promotionProducts)
                    ? promotionProducts
                        .Select(product => new CatalogPromotionProductDto(
                            product.ProductCode,
                            Math.Max(1, product.UnitWeight)))
                        .ToArray()
                    : []))
            .Where(rule => rule.Products.Count > 0)
            .ToArray();

        return new CatalogPromotionsResponse(store.StoreCode, DateTimeOffset.UtcNow, rules);
    }

    public async Task<CatalogLookupResponse?> LookupSellableItemAsync(
        string storeCode,
        string? lookupCode,
        string? lookupCodeNormalized,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        Log($"lookup service start store={storeCode} lookupCode={lookupCode ?? "<null>"} lookupCodeNormalized={lookupCodeNormalized ?? "<null>"}");
        var directResult = await LookupSellableItemDirectAsync(
            storeCode,
            lookupCode,
            lookupCodeNormalized,
            cancellationToken);
        if (directResult is null)
        {
            stopwatch.Stop();
            Log($"lookup service completed store={storeCode} status=store-not-found elapsedMs={stopwatch.ElapsedMilliseconds}");
            return null;
        }

        var response = directResult.Response;
        if (response is { Found: true, Item.PriceSource: PriceSourceKind.ProductBase })
        {
            response = await EnsureStoreRetailPriceAndLookupAsync(
                response.StoreCode,
                lookupCode,
                lookupCodeNormalized,
                response,
                directResult.Product,
                cancellationToken);
        }

        stopwatch.Stop();
        Log($"lookup service completed store={storeCode} status=ok found={response.Found} lookupCodeNormalized={response.LookupCodeNormalized} productCode={response.Item?.ProductCode ?? "<null>"} elapsedMs={stopwatch.ElapsedMilliseconds}");
        return response;
    }

    private async Task<CatalogDirectLookupResult?> LookupSellableItemDirectAsync(
        string storeCode,
        string? lookupCode,
        string? lookupCodeNormalized,
        CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var normalizedStoreCode = NormalizeStoreCode(storeCode);
        var lookupCandidates = BuildLookupCandidates(lookupCode, lookupCodeNormalized);

        var store = await dbContext.MainDb.Queryable<Store>()
            .FirstAsync(x => x.StoreCode == normalizedStoreCode && x.IsActive && !x.IsDeleted, cancellationToken);
        if (store is null)
        {
            totalStopwatch.Stop();
            Log($"lookup direct store not found store={normalizedStoreCode} elapsedMs={totalStopwatch.ElapsedMilliseconds}");
            return null;
        }

        var candidates = new List<CatalogLookupCandidate>();
        if (lookupCandidates.Count > 0)
        {
            // 四类扫码来源投影为同一形状，一次 UNION 查询即可保留全部价格候选。
            var clearanceQuery = dbContext.MainDb.Queryable<StoreClearancePrice>()
                .Where(x =>
                    x.StoreCode == normalizedStoreCode &&
                    !x.IsDeleted &&
                    x.ClearanceBarcode != null &&
                    lookupCandidates.Contains(x.ClearanceBarcode))
                .Select(x => new CatalogLookupCandidate
                {
                    SourceKind = (int)PriceSourceKind.StoreClearancePrice,
                    ProductCode = x.ProductCode,
                    RelatedCode = null,
                    LookupCode = x.ClearanceBarcode,
                    RetailPrice = x.ClearancePrice,
                    DiscountRate = null,
                    ReferenceCode = x.UUID,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt
                });
            var multiQuery = dbContext.MainDb.Queryable<StoreMultiCodeProduct>()
                .Where(x =>
                    x.StoreCode == normalizedStoreCode &&
                    x.IsActive &&
                    !x.IsDeleted &&
                    x.MultiBarcode != null &&
                    lookupCandidates.Contains(x.MultiBarcode))
                .Select(x => new CatalogLookupCandidate
                {
                    SourceKind = (int)PriceSourceKind.StoreMultiCodeProduct,
                    ProductCode = x.ProductCode,
                    RelatedCode = x.MultiCodeProductCode,
                    LookupCode = x.MultiBarcode,
                    RetailPrice = x.MultiCodeRetailPrice,
                    DiscountRate = x.DiscountRate,
                    ReferenceCode = x.UUID,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt
                });
            var setQuery = dbContext.MainDb.Queryable<ProductSetCode>()
                .Where(x =>
                    x.IsActive &&
                    !x.IsDeleted &&
                    x.SetBarcode != null &&
                    lookupCandidates.Contains(x.SetBarcode))
                .Select(x => new CatalogLookupCandidate
                {
                    SourceKind = (int)PriceSourceKind.ProductSetCode,
                    ProductCode = x.ProductCode,
                    RelatedCode = x.SetProductCode,
                    LookupCode = x.SetBarcode,
                    RetailPrice = x.SetRetailPrice,
                    DiscountRate = null,
                    ReferenceCode = x.SetCodeId,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt
                });
            var productQuery = dbContext.MainDb.Queryable<Product>()
                .Where(x =>
                    x.IsActive &&
                    !x.IsDeleted &&
                    ((x.Barcode != null && lookupCandidates.Contains(x.Barcode)) ||
                     (x.ItemNumber != null && lookupCandidates.Contains(x.ItemNumber))))
                .Select(x => new CatalogLookupCandidate
                {
                    SourceKind = (int)PriceSourceKind.ProductBase,
                    ProductCode = x.ProductCode,
                    RelatedCode = null,
                    LookupCode = x.Barcode,
                    RetailPrice = x.RetailPrice,
                    DiscountRate = null,
                    ReferenceCode = x.UUID,
                    CreatedAt = x.CreatedAt,
                    UpdatedAt = x.UpdatedAt
                });

            candidates = await dbContext.MainDb
                .UnionAll(clearanceQuery, multiQuery, setQuery, productQuery)
                .ToListAsync(cancellationToken);
        }

        var setProductCodes = candidates
            .Where(x => x.SourceKind == (int)PriceSourceKind.ProductSetCode)
            .Select(x => x.RelatedCode)
            .Where(HasText)
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var setMultiCodeProductEntities = setProductCodes.Length == 0
            ? []
            : await dbContext.MainDb.Queryable<StoreMultiCodeProduct>()
                .Where(x =>
                    x.StoreCode == normalizedStoreCode &&
                    x.IsActive &&
                    !x.IsDeleted &&
                    x.MultiCodeProductCode != null &&
                    setProductCodes.Contains(x.MultiCodeProductCode))
                .ToListAsync(cancellationToken);

        var relatedProductCodes = candidates
            .Select(x => x.ProductCode)
            .Concat(setMultiCodeProductEntities.Select(x => x.ProductCode))
            .Where(HasText)
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var productEntities = relatedProductCodes.Length == 0
            ? []
            : await dbContext.MainDb.Queryable<Product>()
                .Where(x =>
                    x.IsActive &&
                    !x.IsDeleted &&
                    x.ProductCode != null &&
                    relatedProductCodes.Contains(x.ProductCode))
                .ToListAsync(cancellationToken);

        var productCodes = productEntities
            .Select(x => x.ProductCode)
            .Where(HasText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var storeRetailPriceEntities = productCodes.Length == 0
            ? []
            : await dbContext.MainDb.Queryable<StoreRetailPrice>()
                .Where(x =>
                    x.StoreCode == normalizedStoreCode &&
                    x.IsActive &&
                    !x.IsDeleted &&
                    x.ProductCode != null &&
                    productCodes.Contains(x.ProductCode))
                .ToListAsync(cancellationToken);

        var multiCodeRecords = candidates
            .Where(x => x.SourceKind == (int)PriceSourceKind.StoreMultiCodeProduct)
            .Select(x => new StoreMultiCodeProductRecord(
                x.ProductCode,
                x.RelatedCode,
                x.LookupCode,
                x.RetailPrice,
                ToOffset(x.UpdatedAt ?? x.CreatedAt),
                x.ReferenceCode,
                x.DiscountRate))
            .Concat(setMultiCodeProductEntities.Select(x => new StoreMultiCodeProductRecord(
                x.ProductCode,
                x.MultiCodeProductCode,
                x.MultiBarcode,
                x.MultiCodeRetailPrice,
                ToOffset(x.UpdatedAt ?? x.CreatedAt),
                x.UUID,
                x.DiscountRate)))
            .GroupBy(x => x.ReferenceCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        var input = new PriceIndexInput(
            Since: null,
            productEntities
                .Select(x => new ProductPriceRecord(
                    x.ProductCode,
                    x.ProductName,
                    x.ItemNumber,
                    x.Barcode,
                    x.RetailPrice,
                    ToOffset(x.UpdatedAt ?? x.CreatedAt),
                    x.ProductImage,
                    x.UUID))
                .ToList(),
            storeRetailPriceEntities
                .Select(x => new StoreRetailPriceRecord(
                    x.ProductCode,
                    x.StoreRetailPriceValue,
                    ToOffset(x.UpdatedAt ?? x.CreatedAt),
                    x.UUID,
                    x.DiscountRate,
                    x.IsSpecialProduct))
                .ToList(),
            multiCodeRecords,
            candidates
                .Where(x => x.SourceKind == (int)PriceSourceKind.StoreClearancePrice)
                .Select(x => new StoreClearancePriceRecord(
                    x.ProductCode,
                    x.LookupCode,
                    x.RetailPrice,
                    ToOffset(x.UpdatedAt ?? x.CreatedAt),
                    x.ReferenceCode))
                .ToList(),
            candidates
                .Where(x => x.SourceKind == (int)PriceSourceKind.ProductSetCode)
                .Select(x => new ProductSetCodeRecord(
                    x.ProductCode ?? string.Empty,
                    x.RelatedCode ?? string.Empty,
                    x.LookupCode,
                    x.RetailPrice,
                    ToOffset(x.UpdatedAt ?? x.CreatedAt),
                    x.ReferenceCode))
                .ToList());

        var generatedAt = DateTimeOffset.UtcNow;
        var items = priceIndexBuilder.Build(store.StoreCode, input);
        var response = new CatalogSellableIndex(store.StoreCode, generatedAt, items)
            .Lookup(lookupCode, lookupCodeNormalized);

        totalStopwatch.Stop();
        Log($"lookup direct completed store={store.StoreCode} found={response.Found} candidates={candidates.Count} products={productEntities.Count} storePrices={storeRetailPriceEntities.Count} elapsedMs={totalStopwatch.ElapsedMilliseconds}");
        var matchedProduct = response.Item is null
            ? null
            : productEntities.FirstOrDefault(x =>
                StringComparer.OrdinalIgnoreCase.Equals(x.ProductCode, response.Item.ProductCode));
        return new CatalogDirectLookupResult(response, matchedProduct);
    }

    public async Task<CatalogSpecialProductsPageResponse?> GetSpecialProductsPageAsync(
        string storeCode,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var index = await BuildSellableIndexAsync(storeCode, since: null, cancellationToken);
        return index?.CatalogIndex.GetSpecialProductsPage(cursor, pageSize);
    }

    public async Task<CatalogSpecialProductMarkServiceResult> MarkSpecialProductAsync(
        CatalogSpecialProductMarkRequest request,
        string updatedBy,
        CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();
        var normalizedStoreCode = NormalizeStoreCode(request.StoreCode);
        var normalizedProductCode = NormalizeProductCode(request.ProductCode);
        Log($"mark special product start store={normalizedStoreCode} product={normalizedProductCode} isSpecialProduct={request.IsSpecialProduct}");
        if (string.IsNullOrEmpty(normalizedStoreCode))
        {
            totalStopwatch.Stop();
            Log($"mark special product failed store={normalizedStoreCode} product={normalizedProductCode} reason=store-code-required totalElapsedMs={totalStopwatch.ElapsedMilliseconds}");
            return CatalogSpecialProductMarkServiceResult.Fail("STORE_CODE_REQUIRED", "storeCode is required");
        }

        if (string.IsNullOrEmpty(normalizedProductCode))
        {
            totalStopwatch.Stop();
            Log($"mark special product failed store={normalizedStoreCode} product={normalizedProductCode} reason=product-code-required totalElapsedMs={totalStopwatch.ElapsedMilliseconds}");
            return CatalogSpecialProductMarkServiceResult.Fail("PRODUCT_CODE_REQUIRED", "productCode is required");
        }

        var storeStopwatch = Stopwatch.StartNew();
        var store = await dbContext.MainDb.Queryable<Store>()
            .FirstAsync(x => x.StoreCode == normalizedStoreCode && x.IsActive && !x.IsDeleted, cancellationToken);
        storeStopwatch.Stop();
        Log($"mark special product store query store={normalizedStoreCode} found={store is not null} elapsedMs={storeStopwatch.ElapsedMilliseconds}");
        if (store is null)
        {
            totalStopwatch.Stop();
            Log($"mark special product failed store={normalizedStoreCode} product={normalizedProductCode} reason=store-not-found totalElapsedMs={totalStopwatch.ElapsedMilliseconds}");
            return CatalogSpecialProductMarkServiceResult.Fail("STORE_NOT_FOUND", "store was not found or inactive");
        }

        var productStopwatch = Stopwatch.StartNew();
        var product = await dbContext.MainDb.Queryable<Product>()
            .FirstAsync(x => x.ProductCode == normalizedProductCode && x.IsActive && !x.IsDeleted, cancellationToken);
        productStopwatch.Stop();
        Log($"mark special product product query store={normalizedStoreCode} product={normalizedProductCode} found={product is not null} elapsedMs={productStopwatch.ElapsedMilliseconds}");
        if (product is null)
        {
            totalStopwatch.Stop();
            Log($"mark special product failed store={normalizedStoreCode} product={normalizedProductCode} reason=product-not-found totalElapsedMs={totalStopwatch.ElapsedMilliseconds}");
            return CatalogSpecialProductMarkServiceResult.Fail("PRODUCT_NOT_FOUND", "product was not found or inactive");
        }

        var now = DateTime.UtcNow;
        var actor = string.IsNullOrWhiteSpace(updatedBy) ? "pos-device" : updatedBy.Trim();

        var transactionStopwatch = Stopwatch.StartNew();
        var retailQueryElapsedMs = 0L;
        var writeElapsedMs = 0L;
        var writeAction = "unknown";
        await dbContext.MainDb.Ado.BeginTranAsync();
        try
        {
            var retailQueryStopwatch = Stopwatch.StartNew();
            var storeRetailPrice = await dbContext.MainDb.Queryable<StoreRetailPrice>()
                .FirstAsync(x =>
                    x.StoreCode == normalizedStoreCode &&
                    x.ProductCode == normalizedProductCode &&
                    !x.IsDeleted,
                    cancellationToken);
            retailQueryStopwatch.Stop();
            retailQueryElapsedMs = retailQueryStopwatch.ElapsedMilliseconds;

            if (storeRetailPrice is null)
            {
                writeAction = "insert";
                storeRetailPrice = new StoreRetailPrice
                {
                    UUID = UuidHelper.GenerateUuid7(),
                    StoreCode = normalizedStoreCode,
                    ProductCode = normalizedProductCode,
                    StoreProductCode = UuidHelper.GenerateUuid7(),
                    SupplierCode = product.LocalSupplierCode,
                    PurchasePrice = product.PurchasePrice,
                    StoreRetailPriceValue = product.RetailPrice,
                    IsActive = true,
                    IsAutoPricing = product.IsAutoPricing,
                    IsSpecialProduct = request.IsSpecialProduct,
                    CreatedAt = now,
                    UpdatedAt = now,
                    CreatedBy = actor,
                    UpdatedBy = actor,
                    IsDeleted = false
                };

                var writeStopwatch = Stopwatch.StartNew();
                await dbContext.MainDb.Insertable(storeRetailPrice).ExecuteCommandAsync();
                writeStopwatch.Stop();
                writeElapsedMs = writeStopwatch.ElapsedMilliseconds;
            }
            else
            {
                writeAction = "update";
                storeRetailPrice.IsSpecialProduct = request.IsSpecialProduct;
                storeRetailPrice.UpdatedAt = now;
                storeRetailPrice.UpdatedBy = actor;
                var writeStopwatch = Stopwatch.StartNew();
                await dbContext.MainDb.Updateable(storeRetailPrice).ExecuteCommandAsync();
                writeStopwatch.Stop();
                writeElapsedMs = writeStopwatch.ElapsedMilliseconds;
            }

            await dbContext.MainDb.Ado.CommitTranAsync();
            transactionStopwatch.Stop();
            Log($"mark special product transaction store={normalizedStoreCode} product={normalizedProductCode} action={writeAction} retailQueryElapsedMs={retailQueryElapsedMs} writeElapsedMs={writeElapsedMs} transactionElapsedMs={transactionStopwatch.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            transactionStopwatch.Stop();
            await dbContext.MainDb.Ado.RollbackTranAsync();
            Log($"mark special product transaction failed store={normalizedStoreCode} product={normalizedProductCode} action={writeAction} retailQueryElapsedMs={retailQueryElapsedMs} writeElapsedMs={writeElapsedMs} transactionElapsedMs={transactionStopwatch.ElapsedMilliseconds} error={ex.Message}");
            throw;
        }

        var invalidateStopwatch = Stopwatch.StartNew();
        catalogIndexCache.InvalidateStore(normalizedStoreCode);
        invalidateStopwatch.Stop();

        var indexStopwatch = Stopwatch.StartNew();
        var index = await BuildSellableIndexAsync(normalizedStoreCode, since: null, cancellationToken);
        indexStopwatch.Stop();

        var itemFilterStopwatch = Stopwatch.StartNew();
        var items = index?.CatalogIndex.Items
            .Where(x => string.Equals(x.ProductCode, normalizedProductCode, StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];
        itemFilterStopwatch.Stop();
        totalStopwatch.Stop();
        Log($"mark special product completed store={normalizedStoreCode} product={normalizedProductCode} isSpecialProduct={request.IsSpecialProduct} items={items.Length} storeQueryElapsedMs={storeStopwatch.ElapsedMilliseconds} productQueryElapsedMs={productStopwatch.ElapsedMilliseconds} retailQueryElapsedMs={retailQueryElapsedMs} writeElapsedMs={writeElapsedMs} transactionElapsedMs={transactionStopwatch.ElapsedMilliseconds} cacheInvalidateElapsedMs={invalidateStopwatch.ElapsedMilliseconds} indexElapsedMs={indexStopwatch.ElapsedMilliseconds} itemFilterElapsedMs={itemFilterStopwatch.ElapsedMilliseconds} totalElapsedMs={totalStopwatch.ElapsedMilliseconds}");

        return CatalogSpecialProductMarkServiceResult.Ok(new CatalogSpecialProductMarkResponse(
            normalizedStoreCode,
            normalizedProductCode,
            request.IsSpecialProduct,
            index?.GeneratedAt ?? DateTimeOffset.UtcNow,
            items));
    }

    private async Task<CatalogIndexBuildResult?> BuildSellableIndexAsync(
        string storeCode,
        DateTimeOffset? since,
        CancellationToken cancellationToken)
    {
        var normalizedStoreCode = NormalizeStoreCode(storeCode);
        return await catalogIndexCache.GetOrBuildAsync(
            normalizedStoreCode,
            since,
            token => BuildSellableIndexCoreAsync(normalizedStoreCode, since, token),
            cancellationToken);
    }

    private async Task<CatalogIndexBuildResult?> BuildSellableIndexCoreAsync(
        string normalizedStoreCode,
        DateTimeOffset? since,
        CancellationToken cancellationToken)
    {
        var totalStopwatch = Stopwatch.StartNew();
        Log($"build index start store={normalizedStoreCode} since={since?.ToString("O") ?? "<null>"}");

        var stepStopwatch = Stopwatch.StartNew();
        var store = await dbContext.MainDb.Queryable<Store>()
            .FirstAsync(x => x.StoreCode == normalizedStoreCode && x.IsActive && !x.IsDeleted, cancellationToken);
        stepStopwatch.Stop();
        Log($"store query store={normalizedStoreCode} found={store is not null} elapsedMs={stepStopwatch.ElapsedMilliseconds}");

        if (store is null)
        {
            totalStopwatch.Stop();
            Log($"build index store not found store={normalizedStoreCode} elapsedMs={totalStopwatch.ElapsedMilliseconds}");
            return null;
        }

        stepStopwatch.Restart();
        const int productBatchSize = 20_000;
        var products = new List<ProductPriceRecord>();
        string? lastProductCode = null;
        string? lastProductUuid = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchStopwatch = Stopwatch.StartNew();
            var productQuery = dbContext.MainDb.Queryable<Product>()
                .Where(x => x.IsActive && !x.IsDeleted && x.ProductCode != null && x.UUID != null);

            if (lastProductCode is not null && lastProductUuid is not null)
            {
                // 按 ProductCode + UUID 键集翻页，避免完整目录后段产生 OFFSET 扫描。
                productQuery = productQuery.Where(
                    "(([ProductCode] > @lastProductCode) OR ([ProductCode] = @lastProductCode AND [UUID] > @lastUuid))",
                    new { lastProductCode, lastUuid = lastProductUuid });
            }

            var productBatch = await productQuery
                .OrderBy(x => x.ProductCode)
                .OrderBy(x => x.UUID)
                .Select(x => new Product
                {
                    ProductCode = x.ProductCode,
                    ProductName = x.ProductName,
                    ItemNumber = x.ItemNumber,
                    Barcode = x.Barcode,
                    RetailPrice = x.RetailPrice,
                    UpdatedAt = x.UpdatedAt,
                    CreatedAt = x.CreatedAt,
                    ProductImage = x.ProductImage,
                    UUID = x.UUID
                })
                .Take(productBatchSize)
                .ToListAsync(cancellationToken);

            products.AddRange(productBatch.Select(x => new ProductPriceRecord(
                x.ProductCode,
                x.ProductName,
                x.ItemNumber,
                x.Barcode,
                x.RetailPrice,
                ToOffset(x.UpdatedAt ?? x.CreatedAt),
                x.ProductImage,
                x.UUID)));
            batchStopwatch.Stop();
            Log($"products batch query store={normalizedStoreCode} rows={productBatch.Count} total={products.Count} elapsedMs={batchStopwatch.ElapsedMilliseconds}");

            if (productBatch.Count < productBatchSize)
            {
                break;
            }

            var lastProduct = productBatch[^1];
            lastProductCode = lastProduct.ProductCode;
            lastProductUuid = lastProduct.UUID;
        }

        stepStopwatch.Stop();
        Log($"products query store={normalizedStoreCode} count={products.Count} elapsedMs={stepStopwatch.ElapsedMilliseconds}");

        stepStopwatch.Restart();
        const int storeRetailPriceBatchSize = 20_000;
        var storeRetailPrices = new List<StoreRetailPriceRecord>();
        lastProductCode = null;
        string? lastUuid = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchStopwatch = Stopwatch.StartNew();
            var storeRetailPriceQuery = dbContext.MainDb.Queryable<StoreRetailPrice>()
                .Where(x => x.StoreCode == normalizedStoreCode
                    && x.IsActive
                    && !x.IsDeleted
                    && x.ProductCode != null);

            if (lastProductCode is not null && lastUuid is not null)
            {
                // 沿用现有复合索引顺序做键集分页，避免越往后的 OFFSET 扫描越慢。
                storeRetailPriceQuery = storeRetailPriceQuery.Where(
                    "(([ProductCode] > @lastProductCode) OR ([ProductCode] = @lastProductCode AND [UUID] > @lastUuid))",
                    new { lastProductCode, lastUuid });
            }

            // 只从数据库读取目录构建实际使用的字段，减少大门店的数据传输和实体映射成本。
            var storeRetailPriceBatch = await storeRetailPriceQuery
                .OrderBy(x => x.ProductCode)
                .OrderBy(x => x.UUID)
                .Select(x => new StoreRetailPrice
                {
                    ProductCode = x.ProductCode,
                    StoreRetailPriceValue = x.StoreRetailPriceValue,
                    UpdatedAt = x.UpdatedAt,
                    CreatedAt = x.CreatedAt,
                    UUID = x.UUID,
                    DiscountRate = x.DiscountRate,
                    IsSpecialProduct = x.IsSpecialProduct
                })
                .Take(storeRetailPriceBatchSize)
                .ToListAsync(cancellationToken);

            storeRetailPrices.AddRange(storeRetailPriceBatch.Select(x => new StoreRetailPriceRecord(
                x.ProductCode,
                x.StoreRetailPriceValue,
                ToOffset(x.UpdatedAt ?? x.CreatedAt),
                x.UUID,
                x.DiscountRate,
                x.IsSpecialProduct)));
            batchStopwatch.Stop();
            Log($"store retail prices batch query store={normalizedStoreCode} rows={storeRetailPriceBatch.Count} total={storeRetailPrices.Count} elapsedMs={batchStopwatch.ElapsedMilliseconds}");

            if (storeRetailPriceBatch.Count < storeRetailPriceBatchSize)
            {
                break;
            }

            var lastStoreRetailPrice = storeRetailPriceBatch[^1];
            lastProductCode = lastStoreRetailPrice.ProductCode;
            lastUuid = lastStoreRetailPrice.UUID;
        }

        stepStopwatch.Stop();
        Log($"store retail prices query store={normalizedStoreCode} count={storeRetailPrices.Count} elapsedMs={stepStopwatch.ElapsedMilliseconds}");

        stepStopwatch.Restart();
        var multiCodeProductEntities = await dbContext.MainDb.Queryable<StoreMultiCodeProduct>()
            .Where(x => x.StoreCode == normalizedStoreCode && x.IsActive && !x.IsDeleted)
            .Select(x => new StoreMultiCodeProduct
            {
                ProductCode = x.ProductCode,
                MultiCodeProductCode = x.MultiCodeProductCode,
                MultiBarcode = x.MultiBarcode,
                MultiCodeRetailPrice = x.MultiCodeRetailPrice,
                UpdatedAt = x.UpdatedAt,
                CreatedAt = x.CreatedAt,
                UUID = x.UUID,
                DiscountRate = x.DiscountRate
            })
            .ToListAsync(cancellationToken);
        stepStopwatch.Stop();
        Log($"multi code products query store={normalizedStoreCode} count={multiCodeProductEntities.Count} elapsedMs={stepStopwatch.ElapsedMilliseconds}");
        var multiCodeProducts = multiCodeProductEntities
            .Select(x => new StoreMultiCodeProductRecord(
                x.ProductCode,
                x.MultiCodeProductCode,
                x.MultiBarcode,
                x.MultiCodeRetailPrice,
                ToOffset(x.UpdatedAt ?? x.CreatedAt),
                x.UUID,
                x.DiscountRate))
            .ToList();

        stepStopwatch.Restart();
        var clearancePriceEntities = await dbContext.MainDb.Queryable<StoreClearancePrice>()
            .Where(x => x.StoreCode == normalizedStoreCode && !x.IsDeleted)
            .Select(x => new StoreClearancePrice
            {
                ProductCode = x.ProductCode,
                ClearanceBarcode = x.ClearanceBarcode,
                ClearancePrice = x.ClearancePrice,
                UpdatedAt = x.UpdatedAt,
                CreatedAt = x.CreatedAt,
                UUID = x.UUID
            })
            .ToListAsync(cancellationToken);
        stepStopwatch.Stop();
        Log($"clearance prices query store={normalizedStoreCode} count={clearancePriceEntities.Count} elapsedMs={stepStopwatch.ElapsedMilliseconds}");
        var clearancePrices = clearancePriceEntities
            .Select(x => new StoreClearancePriceRecord(
                x.ProductCode,
                x.ClearanceBarcode,
                x.ClearancePrice,
                ToOffset(x.UpdatedAt ?? x.CreatedAt),
                x.UUID))
            .ToList();

        stepStopwatch.Restart();
        var setCodeEntities = await dbContext.MainDb.Queryable<ProductSetCode>()
            .Where(x => x.IsActive && !x.IsDeleted)
            .Select(x => new ProductSetCode
            {
                ProductCode = x.ProductCode,
                SetProductCode = x.SetProductCode,
                SetBarcode = x.SetBarcode,
                SetRetailPrice = x.SetRetailPrice,
                UpdatedAt = x.UpdatedAt,
                CreatedAt = x.CreatedAt,
                SetCodeId = x.SetCodeId
            })
            .ToListAsync(cancellationToken);
        stepStopwatch.Stop();
        Log($"set codes query store={normalizedStoreCode} count={setCodeEntities.Count} elapsedMs={stepStopwatch.ElapsedMilliseconds}");
        var setCodes = setCodeEntities
            .Select(x => new ProductSetCodeRecord(
                x.ProductCode,
                x.SetProductCode,
                x.SetBarcode,
                x.SetRetailPrice,
                ToOffset(x.UpdatedAt ?? x.CreatedAt),
                x.SetCodeId))
            .ToList();

        var input = new PriceIndexInput(
            since,
            products,
            storeRetailPrices,
            multiCodeProducts,
            clearancePrices,
            setCodes);

        var generatedAt = DateTimeOffset.UtcNow;
        stepStopwatch.Restart();
        var items = priceIndexBuilder.Build(store.StoreCode, input);
        stepStopwatch.Stop();
        totalStopwatch.Stop();
        Log($"build index completed store={store.StoreCode} items={items.Count} buildElapsedMs={stepStopwatch.ElapsedMilliseconds} totalElapsedMs={totalStopwatch.ElapsedMilliseconds}");
        return new CatalogIndexBuildResult(
            store.StoreCode,
            generatedAt,
            items,
            new CatalogSellableIndex(store.StoreCode, generatedAt, items));
    }

    private async Task<CatalogLookupResponse> EnsureStoreRetailPriceAndLookupAsync(
        string normalizedStoreCode,
        string? lookupCode,
        string? lookupCodeNormalized,
        CatalogLookupResponse currentResponse,
        Product? product,
        CancellationToken cancellationToken)
    {
        var productCode = NormalizeProductCode(currentResponse.Item?.ProductCode);
        if (string.IsNullOrEmpty(productCode))
        {
            return currentResponse;
        }

        if (product is null)
        {
            Log($"lookup store retail ensure skipped store={normalizedStoreCode} product={productCode} reason=product-not-loaded");
            return currentResponse;
        }

        var ensureLock = StoreRetailPriceEnsureLocks.GetOrAdd(
            StoreRetailPriceEnsureLockKey(normalizedStoreCode, productCode),
            _ => new SemaphoreSlim(1, 1));
        await ensureLock.WaitAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            var writeAction = "none";
            StoreRetailPrice? storeRetailPrice = null;
            await dbContext.MainDb.Ado.BeginTranAsync();
            try
            {
                storeRetailPrice = await dbContext.MainDb.Queryable<StoreRetailPrice>()
                    .FirstAsync(x =>
                        x.StoreCode == normalizedStoreCode &&
                        x.ProductCode == productCode &&
                        !x.IsDeleted,
                        cancellationToken);

                if (storeRetailPrice is null)
                {
                    writeAction = "insert";
                    // 本地 lookup 命中商品主档但没有分店价时，立即复制主档价格创建分店价，供 POS 本次扫码使用。
                    storeRetailPrice = new StoreRetailPrice
                    {
                        UUID = UuidHelper.GenerateUuid7(),
                        StoreCode = normalizedStoreCode,
                        ProductCode = productCode,
                        StoreProductCode = UuidHelper.GenerateUuid7(),
                        SupplierCode = product.LocalSupplierCode,
                        PurchasePrice = product.PurchasePrice,
                        StoreRetailPriceValue = product.RetailPrice ?? 0m,
                        IsActive = true,
                        IsAutoPricing = product.IsAutoPricing,
                        IsSpecialProduct = product.IsSpecialProduct,
                        CreatedAt = now,
                        UpdatedAt = now,
                        CreatedBy = "pos-device",
                        UpdatedBy = "pos-device",
                        IsDeleted = false
                    };

                    await dbContext.MainDb.Insertable(storeRetailPrice).ExecuteCommandAsync();
                }
                else if (!storeRetailPrice.IsActive || storeRetailPrice.StoreRetailPriceValue is null)
                {
                    writeAction = "update";
                    // 已有分店价记录但不可用于索引时，补齐主档价格并重新启用，避免继续返回 ProductBase。
                    storeRetailPrice.SupplierCode ??= product.LocalSupplierCode;
                    storeRetailPrice.PurchasePrice ??= product.PurchasePrice;
                    storeRetailPrice.StoreRetailPriceValue = product.RetailPrice ?? 0m;
                    storeRetailPrice.IsActive = true;
                    storeRetailPrice.IsAutoPricing = product.IsAutoPricing;
                    storeRetailPrice.IsSpecialProduct = product.IsSpecialProduct;
                    storeRetailPrice.UpdatedAt = now;
                    storeRetailPrice.UpdatedBy = "pos-device";

                    await dbContext.MainDb.Updateable(storeRetailPrice).ExecuteCommandAsync();
                }
                else
                {
                    // 缓存可能仍停在 ProductBase；即使数据库已有可用分店价，也要重建该店索引后再返回。
                    writeAction = "refresh";
                }

                await dbContext.MainDb.Ado.CommitTranAsync();
            }
            catch
            {
                await dbContext.MainDb.Ado.RollbackTranAsync();
                throw;
            }

            if (writeAction == "none")
            {
                return currentResponse;
            }

            catalogIndexCache.InvalidateStore(normalizedStoreCode);
            if (storeRetailPrice is null)
            {
                return currentResponse;
            }

            // 复用本次 lookup 已读取的商品和刚写入的门店价，避免再次执行候选 UNION。
            var input = new PriceIndexInput(
                Since: null,
                [new ProductPriceRecord(
                    product.ProductCode,
                    product.ProductName,
                    product.ItemNumber,
                    product.Barcode,
                    product.RetailPrice,
                    ToOffset(product.UpdatedAt ?? product.CreatedAt),
                    product.ProductImage,
                    product.UUID)],
                [new StoreRetailPriceRecord(
                    storeRetailPrice.ProductCode,
                    storeRetailPrice.StoreRetailPriceValue,
                    ToOffset(storeRetailPrice.UpdatedAt ?? storeRetailPrice.CreatedAt),
                    storeRetailPrice.UUID,
                    storeRetailPrice.DiscountRate,
                    storeRetailPrice.IsSpecialProduct)],
                [],
                [],
                []);
            var generatedAt = DateTimeOffset.UtcNow;
            var items = priceIndexBuilder.Build(normalizedStoreCode, input);
            var refreshedResponse = new CatalogSellableIndex(normalizedStoreCode, generatedAt, items)
                .Lookup(lookupCode, lookupCodeNormalized);
            Log($"lookup store retail ensured store={normalizedStoreCode} product={productCode} action={writeAction} refreshed={refreshedResponse.Found}");
            return refreshedResponse;
        }
        finally
        {
            ensureLock.Release();
        }
    }

    private static DateTimeOffset? ToOffset(DateTime? value)
    {
        return value is null
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
    }

    private static string NormalizeStoreCode(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static string NormalizeProductCode(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static bool HasText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    private static IReadOnlyList<string> BuildLookupCandidates(string? lookupCode, string? lookupCodeNormalized)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        AddLookupCandidate(candidates, lookupCode);
        AddLookupCandidate(candidates, lookupCodeNormalized);
        return candidates.ToArray();
    }

    private static void AddLookupCandidate(HashSet<string> candidates, string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return;
        }

        candidates.Add(trimmed);
        candidates.Add(trimmed.ToUpperInvariant());
        candidates.Add(trimmed.ToLowerInvariant());
    }

    private sealed record CatalogDirectLookupResult(
        CatalogLookupResponse Response,
        Product? Product);

    private sealed class CatalogLookupCandidate
    {
        public int SourceKind { get; set; }

        public string? ProductCode { get; set; }

        public string? RelatedCode { get; set; }

        public string? LookupCode { get; set; }

        public decimal? RetailPrice { get; set; }

        public decimal? DiscountRate { get; set; }

        public string? ReferenceCode { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }

    private static string StoreRetailPriceEnsureLockKey(string storeCode, string productCode)
    {
        return string.Concat(
            NormalizeStoreCode(storeCode).ToUpperInvariant(),
            "|",
            NormalizeProductCode(productCode).ToUpperInvariant());
    }

    private static void Log(string message)
    {
        Console.WriteLine($"[HBPOS][Api][CatalogService] {DateTimeOffset.Now:O} {message}");
    }

}

public sealed class CatalogSellableIndex
{
    private const int MaxPageSize = 5000;
    private readonly IReadOnlyDictionary<string, CatalogLookupItemDto> _itemsByNormalizedLookup;

    public CatalogSellableIndex(
        string storeCode,
        DateTimeOffset generatedAt,
        IEnumerable<SellableItemDto> items)
    {
        StoreCode = NormalizeStoreCode(storeCode);
        GeneratedAt = generatedAt;

        Items = items
            .Select(ToLookupItem)
            .Where(x => HasText(x.StoreCode) && HasText(x.LookupCodeNormalized))
            .GroupBy(x => x.LookupCodeNormalized, StringComparer.Ordinal)
            .Select(x => x
                .OrderByDescending(item => item.PriceSource)
                .ThenByDescending(item => item.UpdatedAt ?? DateTimeOffset.MinValue)
                .ThenBy(item => item.LookupCode, StringComparer.Ordinal)
                .First())
            .OrderBy(x => x.LookupCodeNormalized, StringComparer.Ordinal)
            .ToArray();

        _itemsByNormalizedLookup = Items.ToDictionary(
            x => x.LookupCodeNormalized,
            StringComparer.Ordinal);
    }

    public string StoreCode { get; }

    public DateTimeOffset GeneratedAt { get; }

    public IReadOnlyList<CatalogLookupItemDto> Items { get; }

    public CatalogSyncPageResponse GetPage(string? cursor, int pageSize)
    {
        var normalizedCursor = NormalizeLookupCode(cursor);
        var take = Math.Clamp(pageSize, 1, MaxPageSize);
        var pageCandidates = Items
            .Where(x => string.IsNullOrEmpty(normalizedCursor)
                || string.Compare(x.LookupCodeNormalized, normalizedCursor, StringComparison.Ordinal) > 0)
            .Take(take + 1)
            .ToArray();

        var pageItems = pageCandidates.Take(take).ToArray();
        var hasMore = pageCandidates.Length > take;
        var nextCursor = hasMore && pageItems.Length > 0
            ? pageItems[^1].LookupCodeNormalized
            : null;

        return new CatalogSyncPageResponse(
            StoreCode,
            GeneratedAt,
            string.IsNullOrEmpty(normalizedCursor) ? null : normalizedCursor,
            pageItems,
            [],
            nextCursor,
            hasMore,
            Items.Count);
    }

    public CatalogCompareResponse Compare(CatalogCompareRequest request)
    {
        var localByLookup = new Dictionary<string, CatalogLocalLookupVersionDto>(StringComparer.Ordinal);

        foreach (var local in request.LocalLookups ?? [])
        {
            var normalizedLookup = NormalizeLookupCode(
                HasText(local.LookupCodeNormalized) ? local.LookupCodeNormalized : local.LookupCode);
            if (string.IsNullOrEmpty(normalizedLookup))
            {
                continue;
            }

            localByLookup.TryAdd(normalizedLookup, local);
        }

        var upserts = new List<CatalogLookupItemDto>();
        var deletes = new List<DeletedLookupDto>();

        foreach (var (normalizedLookup, local) in localByLookup)
        {
            if (!_itemsByNormalizedLookup.TryGetValue(normalizedLookup, out var current))
            {
                deletes.Add(new DeletedLookupDto(
                    StoreCode,
                    GetDeleteLookupCode(local, normalizedLookup),
                    normalizedLookup,
                    GeneratedAt));
                continue;
            }

            if (!HasMatchingVersion(local, current))
            {
                upserts.Add(current);
            }
        }

        return new CatalogCompareResponse(
            StoreCode,
            GeneratedAt,
            upserts,
            deletes,
            NextCursor: null,
            HasMore: false);
    }

    public CatalogLookupResponse Lookup(string? lookupCode, string? lookupCodeNormalized)
    {
        var normalizedLookup = NormalizeLookupCode(
            HasText(lookupCodeNormalized) ? lookupCodeNormalized : lookupCode);
        _itemsByNormalizedLookup.TryGetValue(normalizedLookup, out var item);

        return new CatalogLookupResponse(
            StoreCode,
            GetRequestedLookupCode(lookupCode, lookupCodeNormalized, normalizedLookup),
            normalizedLookup,
            item is not null,
            item);
    }

    public CatalogSpecialProductsPageResponse GetSpecialProductsPage(string? cursor, int pageSize)
    {
        var normalizedCursor = NormalizeLookupCode(cursor);
        var take = Math.Clamp(pageSize, 1, MaxPageSize);
        var specialItems = Items
            .Where(x => x.IsSpecialProduct)
            .ToArray();
        var pageCandidates = specialItems
            .Where(x => string.IsNullOrEmpty(normalizedCursor)
                || string.Compare(x.LookupCodeNormalized, normalizedCursor, StringComparison.Ordinal) > 0)
            .Take(take + 1)
            .ToArray();

        var pageItems = pageCandidates.Take(take).ToArray();
        var hasMore = pageCandidates.Length > take;
        var nextCursor = hasMore && pageItems.Length > 0
            ? pageItems[^1].LookupCodeNormalized
            : null;

        return new CatalogSpecialProductsPageResponse(
            StoreCode,
            GeneratedAt,
            string.IsNullOrEmpty(normalizedCursor) ? null : normalizedCursor,
            pageItems,
            nextCursor,
            hasMore,
            specialItems.Length);
    }

    public static string NormalizeLookupCode(string? value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static CatalogLookupItemDto ToLookupItem(SellableItemDto item)
    {
        var storeCode = NormalizeStoreCode(item.StoreCode);
        var lookupCode = (item.LookupCode ?? string.Empty).Trim();
        var lookupCodeNormalized = NormalizeLookupCode(lookupCode);

        return new CatalogLookupItemDto(
            storeCode,
            item.ProductCode.Trim(),
            item.ReferenceCode?.Trim(),
            item.DisplayName.Trim(),
            lookupCode,
            lookupCodeNormalized,
            item.ItemNumber?.Trim(),
            item.Barcode?.Trim(),
            item.RetailPrice,
            item.PriceSource,
            item.PriceSourceLabel.Trim(),
            item.QuantityFactor,
            item.UpdatedAt,
            CreateRowVersion(
                storeCode,
                item.ProductCode.Trim(),
                item.ReferenceCode?.Trim() ?? string.Empty,
                item.DisplayName.Trim(),
                lookupCodeNormalized,
                item.ItemNumber?.Trim() ?? string.Empty,
                item.Barcode?.Trim() ?? string.Empty,
                item.RetailPrice,
                item.PriceSource,
                item.PriceSourceLabel.Trim(),
                item.QuantityFactor,
                item.ProductImage ?? string.Empty,
                item.DiscountRate,
                item.IsSpecialProduct),
            item.ProductImage,
            item.DiscountRate,
            item.IsSpecialProduct);
    }

    private static string CreateRowVersion(
        string storeCode,
        string productCode,
        string referenceCode,
        string displayName,
        string lookupCodeNormalized,
        string itemNumber,
        string barcode,
        decimal retailPrice,
        PriceSourceKind priceSource,
        string priceSourceLabel,
        decimal quantityFactor,
        string productImage,
        decimal? discountRate,
        bool isSpecialProduct)
    {
        var builder = new StringBuilder();
        AppendCanonical(builder, storeCode);
        AppendCanonical(builder, productCode);
        AppendCanonical(builder, referenceCode);
        AppendCanonical(builder, displayName);
        AppendCanonical(builder, lookupCodeNormalized);
        AppendCanonical(builder, itemNumber);
        AppendCanonical(builder, barcode);
        AppendCanonical(builder, retailPrice.ToString("0.#############################", CultureInfo.InvariantCulture));
        AppendCanonical(builder, ((int)priceSource).ToString(CultureInfo.InvariantCulture));
        AppendCanonical(builder, priceSourceLabel);
        AppendCanonical(builder, quantityFactor.ToString("0.#############################", CultureInfo.InvariantCulture));
        AppendCanonical(builder, productImage);
        AppendCanonical(builder, FormatNullableDecimal(discountRate));
        AppendCanonical(builder, isSpecialProduct ? "1" : "0");

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hashBytes);
    }

    private static bool HasMatchingVersion(CatalogLocalLookupVersionDto local, CatalogLookupItemDto current)
    {
        var rowVersion = local.RowVersion?.Trim();
        if (!string.IsNullOrEmpty(rowVersion))
        {
            return string.Equals(rowVersion, current.RowVersion, StringComparison.OrdinalIgnoreCase);
        }

        return local.UpdatedAt.HasValue
            && current.UpdatedAt.HasValue
            && local.UpdatedAt.Value.ToUniversalTime() == current.UpdatedAt.Value.ToUniversalTime();
    }

    private static string GetDeleteLookupCode(CatalogLocalLookupVersionDto local, string normalizedLookup)
    {
        var lookupCode = local.LookupCode?.Trim();
        return string.IsNullOrEmpty(lookupCode) ? normalizedLookup : lookupCode;
    }

    private static string GetRequestedLookupCode(
        string? lookupCode,
        string? lookupCodeNormalized,
        string normalizedLookup)
    {
        var requestedLookupCode = lookupCode?.Trim();
        if (!string.IsNullOrEmpty(requestedLookupCode))
        {
            return requestedLookupCode;
        }

        var requestedLookupCodeNormalized = lookupCodeNormalized?.Trim();
        return !string.IsNullOrEmpty(requestedLookupCodeNormalized)
            ? requestedLookupCodeNormalized
            : normalizedLookup;
    }

    private static void AppendCanonical(StringBuilder builder, string value)
    {
        builder
            .Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append('|');
    }

    private static string FormatNullableDecimal(decimal? value)
    {
        return value?.ToString("0.#############################", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string NormalizeStoreCode(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static bool HasText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }
}
