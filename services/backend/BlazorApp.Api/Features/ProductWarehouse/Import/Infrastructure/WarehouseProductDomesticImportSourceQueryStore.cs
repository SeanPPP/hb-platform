using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Features.ProductWarehouse;

internal sealed record WarehouseProductDomesticImportCoreSources(
    Dictionary<string, DomesticProduct> DomesticProducts,
    Dictionary<string, WarehouseProduct> WarehouseProducts,
    Dictionary<string, Product> Products
);

internal sealed record WarehouseProductDomesticImportRelatedSources(
    Dictionary<string, List<DomesticSetProduct>> SetProducts,
    Dictionary<string, HashSet<string>> ExistingProductSetCodes,
    List<string> ActiveStores,
    Dictionary<string, HashSet<(string MultiBarcode, string StoreCode)>> ExistingMultiCodeKeys,
    Dictionary<string, List<StoreRetailPrice>> StoreRetailPrices
);

internal sealed class WarehouseProductDomesticImportStoreGroupRow
{
    public string? StoreCode { get; set; }
    public string? ProductCode { get; set; }
}

/// <summary>
/// 国内导入源数据查询只负责锁内复读，不拥有或开启事务。
/// </summary>
internal sealed class WarehouseProductDomesticImportSourceQueryStore
{
    private readonly SqlSugarContext _context;

    internal WarehouseProductDomesticImportSourceQueryStore(SqlSugarContext context)
    {
        _context = context;
    }

    internal async Task<WarehouseProductDomesticImportCoreSources> LoadCoreAsync(
        IReadOnlyCollection<string> productCodes
    )
    {
        var codes = productCodes.ToList();
        var domesticProducts = (
            await _context
                .Db.Queryable<DomesticProduct>()
                .Where(product => codes.Contains(product.ProductCode) && !product.IsDeleted)
                .ToListAsync()
        ).ToDictionary(product => product.ProductCode);
        var warehouseProducts = (
            await _context
                .Db.Queryable<WarehouseProduct>()
                .Where(product => codes.Contains(product.ProductCode))
                .ToListAsync()
        ).ToDictionary(product => product.ProductCode);
        var products = (
            await _context
                .Db.Queryable<Product>()
                .Where(product =>
                    product.ProductCode != null && codes.Contains(product.ProductCode)
                )
                .ToListAsync()
        )
            // 商品编码为空时不能参与字典键匹配，避免放大查询范围。
            .Where(product => !string.IsNullOrWhiteSpace(product.ProductCode))
            .GroupBy(product => product.ProductCode!)
            .ToDictionary(group => group.Key, group => group.First());

        return new WarehouseProductDomesticImportCoreSources(
            domesticProducts,
            warehouseProducts,
            products
        );
    }

    internal async Task<WarehouseProductDomesticImportRelatedSources> LoadRelatedAsync(
        IReadOnlyCollection<string> productCodes
    )
    {
        var codes = productCodes.ToList();

        var allSetProducts = await _context
            .Db.Queryable<DomesticSetProduct>()
            .Where(product => codes.Contains(product.ProductCode) && !product.IsDeleted)
            .ToListAsync();
        var setProducts = allSetProducts
            .Where(product => !string.IsNullOrWhiteSpace(product.ProductCode))
            .GroupBy(product => product.ProductCode!)
            .ToDictionary(group => group.Key, group => group.ToList());

        var allSetCodeIds = allSetProducts
            .Select(product => product.SetProductCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!)
            .Distinct()
            .ToList();
        var existingProductSetCodes = allSetCodeIds.Count > 0
            ? (
                await _context
                    .Db.Queryable<ProductSetCode>()
                    .Where(setCode =>
                        codes.Contains(setCode.ProductCode)
                        && allSetCodeIds.Contains(setCode.SetCodeId)
                    )
                    .Select(setCode => new
                    {
                        setCode.ProductCode,
                        setCode.SetCodeId,
                    })
                    .ToListAsync()
            )
                .Where(row =>
                    !string.IsNullOrWhiteSpace(row.ProductCode)
                    && !string.IsNullOrWhiteSpace(row.SetCodeId)
                )
                .GroupBy(row => row.ProductCode!)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(row => row.SetCodeId!).ToHashSet()
                )
            : new Dictionary<string, HashSet<string>>();

        var activeStores = (
            await _context
                .Db.Queryable<Store>()
                .Where(store => store.IsActive == true && store.IsDeleted == false)
                .Select(store => store.StoreCode)
                .ToListAsync()
        )
            .Where(storeCode => !string.IsNullOrWhiteSpace(storeCode))
            .Select(storeCode => storeCode!)
            .Distinct()
            .ToList();

        var allSetBarcodes = allSetProducts
            .Where(product => product.SetBarcode != null)
            .Select(product => product.SetBarcode!)
            .Distinct()
            .ToList();
        var existingMultiCodeKeys = allSetBarcodes.Count > 0
            ? (
                await _context
                    .Db.Queryable<StoreMultiCodeProduct>()
                    .Where(row =>
                        row.ProductCode != null
                        && codes.Contains(row.ProductCode)
                        && !row.IsDeleted
                        && row.MultiBarcode != null
                        && allSetBarcodes.Contains(row.MultiBarcode)
                    )
                    .Select(row => new
                    {
                        row.ProductCode,
                        row.MultiBarcode,
                        row.StoreCode,
                    })
                    .ToListAsync()
            )
                .Where(row =>
                    !string.IsNullOrWhiteSpace(row.ProductCode)
                    && !string.IsNullOrWhiteSpace(row.MultiBarcode)
                    && !string.IsNullOrWhiteSpace(row.StoreCode)
                )
                .GroupBy(row => row.ProductCode!)
                .ToDictionary(
                    group => group.Key,
                    group =>
                        group
                            .Select(row => (row.MultiBarcode!, row.StoreCode!))
                            .ToHashSet()
                )
            : new Dictionary<string, HashSet<(string MultiBarcode, string StoreCode)>>();

        var storeRetailPrices = (
            await _context
                .Db.Queryable<StoreRetailPrice>()
                .Where(price =>
                    price.ProductCode != null
                    && codes.Contains(price.ProductCode)
                    && !price.IsDeleted
                )
                .ToListAsync()
        )
            .Where(price => !string.IsNullOrWhiteSpace(price.ProductCode))
            .GroupBy(price => price.ProductCode!)
            .ToDictionary(group => group.Key, group => group.ToList());

        return new WarehouseProductDomesticImportRelatedSources(
            setProducts,
            existingProductSetCodes,
            activeStores,
            existingMultiCodeKeys,
            storeRetailPrices
        );
    }
}
