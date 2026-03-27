using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 仓库商品服务 - React 前端专用
    /// 提供仓库商品的 CRUD 操作、批量导入、价格同步等功能
    /// </summary>
    public class ProductWarehouseReactService : IProductWarehouseReactService
    {
        private readonly SqlSugarContext _context;
        private readonly ILogger<ProductWarehouseReactService> _logger;
        private readonly IConfiguration _configuration;
        private readonly ItemBarcodeService _itemBarcodeService;

        public ProductWarehouseReactService(
            SqlSugarContext context,
            ILogger<ProductWarehouseReactService> logger,
            IConfiguration configuration,
            ItemBarcodeService itemBarcodeService
        )
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;
            _itemBarcodeService = itemBarcodeService;
        }

        /// <summary>
        /// 检测商品是否已存在于仓库中
        /// 通过 ProductCode 或 ItemNumber 进行匹配
        /// </summary>
        /// <param name="items">待检测的商品列表</param>
        /// <returns>检测结果列表</returns>
        public async Task<List<DetectionResultDto>> DetectAsync(List<DetectionItemDto> items)
        {
            var results = new List<DetectionResultDto>();
            if (items == null || items.Count == 0)
                return results;

            var productCodes = items
                .Select(i => i.ProductCode)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
                .ToList();
            var itemNumbers = items
                .Select(i => i.ItemNumber)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();

            if (!productCodes.Any() && !itemNumbers.Any())
            {
                foreach (var item in items)
                {
                    results.Add(
                        new DetectionResultDto
                        {
                            ProductCode = item.ProductCode,
                            ItemNumber = item.ItemNumber,
                            Exists = false,
                            MatchType = "none",
                        }
                    );
                }
                return results;
            }

            var query = _context
                .Db.Queryable<WarehouseProduct>()
                .LeftJoin<Product>((w, p) => w.ProductCode == p.ProductCode);

            if (productCodes.Any() && itemNumbers.Any())
            {
                query = query.Where(
                    (w, p) =>
                        productCodes.Contains(w.ProductCode)
                        || (p.ItemNumber != null && itemNumbers.Contains(p.ItemNumber))
                );
            }
            else if (productCodes.Any())
            {
                query = query.Where((w, p) => productCodes.Contains(w.ProductCode));
            }
            else if (itemNumbers.Any())
            {
                query = query.Where(
                    (w, p) => p.ItemNumber != null && itemNumbers.Contains(p.ItemNumber)
                );
            }

            var wpList = await query
                .Select(
                    (w, p) =>
                        new
                        {
                            w.ProductCode,
                            ItemNumber = p.ItemNumber,
                            p.EnglishName,
                            w.DomesticPrice,
                            w.OEMPrice,
                            w.ImportPrice,
                            w.Volume,
                            w.IsActive,
                        }
                )
                .ToListAsync();

            var byCode = wpList
                .GroupBy(x => x.ProductCode)
                .ToDictionary(g => g.Key, g => g.First());
            var byItem = wpList
                .Where(x => !string.IsNullOrWhiteSpace(x.ItemNumber))
                .GroupBy(x => x.ItemNumber!)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var item in items)
            {
                var codeMatch =
                    (
                        !string.IsNullOrWhiteSpace(item.ProductCode)
                        && byCode.TryGetValue(item.ProductCode!, out var wpByCode)
                    )
                        ? wpByCode
                        : null;
                var itemMatch =
                    (
                        !string.IsNullOrWhiteSpace(item.ItemNumber)
                        && byItem.TryGetValue(item.ItemNumber!, out var wpByItem)
                    )
                        ? wpByItem
                        : null;

                var exists = codeMatch != null || itemMatch != null;
                var matchType = "none";
                if (codeMatch != null && itemMatch != null)
                    matchType = "both";
                else if (codeMatch != null)
                    matchType = "product_code";
                else if (itemMatch != null)
                    matchType = "item_number";

                var source = codeMatch ?? itemMatch;
                results.Add(
                    new DetectionResultDto
                    {
                        ProductCode = item.ProductCode,
                        ItemNumber = item.ItemNumber,
                        Exists = exists,
                        MatchType = matchType,
                        WarehouseDomesticPrice = source?.DomesticPrice,
                        WarehouseOEMPrice = source?.OEMPrice,
                        WarehouseImportPrice = source?.ImportPrice,
                        WarehouseVolume = source?.Volume,
                        WarehouseIsActive = source?.IsActive,
                        EnglishName = source?.EnglishName,
                    }
                );
            }

            return results;
        }

        /// <summary>
        /// 批量更新仓库商品
        /// 支持通过 ProductCode 或 ItemNumber 匹配商品进行更新
        /// </summary>
        /// <param name="items">待更新的商品列表</param>
        /// <returns>批量操作结果</returns>
        public async Task<BatchOperationResultDto> BatchUpdateAsync(List<UpdateItemDto> items)
        {
            var result = new BatchOperationResultDto { Success = true, Message = "更新完成" };
            if (items == null || items.Count == 0)
                return result;

            try
            {
                // 开启事务
                _context.Db.Ado.BeginTran();

                // 收集需要查询的 ProductCode 和 ItemNumber
                var productCodes = items
                    .Select(i => i.ProductCode)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();
                var itemNumbers = items
                    .Select(i => i.ItemNumber)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .ToList();

                // 批量查询仓库商品（避免 N+1 问题）
                var wpList = new List<WarehouseProduct>();
                if (productCodes.Any())
                {
                    var wpByCodes = await _context
                        .Db.Queryable<WarehouseProduct>()
                        .Where(w => productCodes.Contains(w.ProductCode))
                        .ToListAsync();
                    wpList.AddRange(wpByCodes);
                }
                // 通过 ItemNumber 查询对应的仓库商品
                if (itemNumbers.Any())
                {
                    var codesFromItems = await _context
                        .Db.Queryable<Product>()
                        .Where(p => p.ItemNumber != null && itemNumbers.Contains(p.ItemNumber))
                        .Select(p => p.ProductCode)
                        .ToListAsync();
                    if (codesFromItems.Any())
                    {
                        var wpByItems = await _context
                            .Db.Queryable<WarehouseProduct>()
                            .Where(w => codesFromItems.Contains(w.ProductCode))
                            .ToListAsync();
                        wpList.AddRange(wpByItems);
                    }
                }
                wpList = wpList.GroupBy(w => w.ProductCode).Select(g => g.First()).ToList();

                var byCode = wpList.ToDictionary(w => w.ProductCode);
                var itemToCode = new Dictionary<string, string>();
                if (itemNumbers.Any())
                {
                    var codeMap = await _context
                        .Db.Queryable<Product>()
                        .Where(p => p.ItemNumber != null && itemNumbers.Contains(p.ItemNumber))
                        .Select(p => new { p.ItemNumber, p.ProductCode })
                        .ToListAsync();
                    foreach (var m in codeMap)
                    {
                        if (
                            !string.IsNullOrWhiteSpace(m.ItemNumber)
                            && !itemToCode.ContainsKey(m.ItemNumber!)
                        )
                        {
                            itemToCode[m.ItemNumber!] = m.ProductCode ?? string.Empty;
                        }
                    }
                }

                var toUpdateWp = new List<WarehouseProduct>();
                var toCreateWp = new List<WarehouseProduct>();
                var codesWithImportPrice = new List<string>();

                foreach (var item in items)
                {
                    WarehouseProduct? wp = null;
                    string? targetCode = null;
                    if (
                        !string.IsNullOrWhiteSpace(item.ProductCode)
                        && byCode.TryGetValue(item.ProductCode!, out var wpByCode)
                    )
                    {
                        wp = wpByCode;
                        targetCode = item.ProductCode!;
                    }
                    else if (
                        !string.IsNullOrWhiteSpace(item.ItemNumber)
                        && itemToCode.TryGetValue(item.ItemNumber!, out var mappedCode)
                        && byCode.TryGetValue(mappedCode, out var wpByItem)
                    )
                    {
                        wp = wpByItem;
                        targetCode = mappedCode;
                    }

                    if (wp == null)
                    {
                        if (string.IsNullOrWhiteSpace(targetCode))
                        {
                            if (!string.IsNullOrWhiteSpace(item.ProductCode))
                            {
                                targetCode = item.ProductCode!;
                            }
                            else if (
                                !string.IsNullOrWhiteSpace(item.ItemNumber)
                                && itemToCode.TryGetValue(item.ItemNumber!, out var mapCode2)
                            )
                            {
                                targetCode = mapCode2;
                            }
                        }
                        if (string.IsNullOrWhiteSpace(targetCode))
                        {
                            result.Errors.Add(
                                $"无法解析商品编码: ProductCode={item.ProductCode}, ItemNumber={item.ItemNumber}"
                            );
                            result.FailedCount++;
                            continue;
                        }

                        var newWp = new WarehouseProduct
                        {
                            ProductCode = targetCode!,
                            DomesticPrice = item.DomesticPrice,
                            OEMPrice = item.OEMPrice,
                            ImportPrice = item.ImportPrice,
                            Volume = item.Volume,
                            StockQuantity = 0,
                            IsActive = item.IsActive ?? true,
                            IsDeleted = false,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                        };
                        toCreateWp.Add(newWp);
                        if (item.ImportPrice.HasValue)
                        {
                            codesWithImportPrice.Add(targetCode!);
                        }
                        continue;
                    }

                    //
                    if (item.DomesticPrice.HasValue)
                        wp.DomesticPrice = item.DomesticPrice;
                    if (item.OEMPrice.HasValue)
                        wp.OEMPrice = item.OEMPrice;
                    if (item.ImportPrice.HasValue)
                        wp.ImportPrice = item.ImportPrice;
                    if (item.Volume.HasValue)
                        wp.Volume = item.Volume;
                    wp.IsActive = item.IsActive ?? true;
                    wp.UpdatedAt = DateTime.Now;
                    toUpdateWp.Add(wp);

                    if (item.ImportPrice.HasValue)
                    {
                        codesWithImportPrice.Add(wp.ProductCode);
                    }
                }

                if (toUpdateWp.Any())
                {
                    await _context
                        .Db.Updateable(toUpdateWp)
                        .UpdateColumns(w => new
                        {
                            w.DomesticPrice,
                            w.OEMPrice,
                            w.ImportPrice,
                            w.Volume,
                            w.IsActive,
                            w.UpdatedAt,
                        })
                        .ExecuteCommandAsync();
                    result.SuccessCount += toUpdateWp.Count;
                }
                if (toCreateWp.Any())
                {
                    await _context.Db.Insertable(toCreateWp).ExecuteCommandAsync();
                    result.SuccessCount += toCreateWp.Count;
                }

                if (codesWithImportPrice.Any())
                {
                    var products = await _context
                        .Db.Queryable<Product>()
                        .Where(p =>
                            p.ProductCode != null && codesWithImportPrice.Contains(p.ProductCode)
                        )
                        .ToListAsync();
                    var importDict = toUpdateWp
                        .Where(w => w.ImportPrice.HasValue)
                        .ToDictionary(w => w.ProductCode, w => w.ImportPrice!.Value);
                    foreach (var w in toCreateWp.Where(x => x.ImportPrice.HasValue))
                    {
                        if (!importDict.ContainsKey(w.ProductCode))
                        {
                            importDict[w.ProductCode] = w.ImportPrice!.Value;
                        }
                    }
                    foreach (var p in products)
                    {
                        if (
                            p.ProductCode != null
                            && importDict.TryGetValue(p.ProductCode, out var importPrice)
                        )
                        {
                            p.PurchasePrice = importPrice;
                            p.UpdatedAt = DateTime.Now;
                        }
                    }
                    if (products.Any())
                    {
                        await _context
                            .Db.Updateable(products)
                            .UpdateColumns(p => new { p.PurchasePrice, p.UpdatedAt })
                            .ExecuteCommandAsync();
                    }

                    var storeRetailPrices = await _context
                        .Db.Queryable<StoreRetailPrice>()
                        .Where(srp =>
                            srp.ProductCode != null
                            && codesWithImportPrice.Contains(srp.ProductCode)
                        )
                        .ToListAsync();

                    foreach (var srp in storeRetailPrices)
                    {
                        if (importDict.TryGetValue(srp.ProductCode!, out var importPrice))
                        {
                            srp.PurchasePrice = importPrice;
                            srp.UpdatedAt = DateTime.Now;
                        }
                    }

                    if (storeRetailPrices.Any())
                    {
                        await _context
                            .Db.Updateable(storeRetailPrices)
                            .UpdateColumns(srp => new { srp.PurchasePrice, srp.UpdatedAt })
                            .ExecuteCommandAsync();
                        _logger.LogInformation(
                            "更新了 {Count} 条分店价格记录的进货价",
                            storeRetailPrices.Count
                        );
                    }
                }

                _context.Db.Ado.CommitTran();
            }
            catch (Exception ex)
            {
                _context.Db.Ado.RollbackTran();
                _logger.LogError(ex, "批量更新失败");
                result.Success = false;
                result.Message = "批量更新失败: " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 批量创建仓库商品
        /// 支持普通商品和套装商品，自动跳过已存在的商品
        /// </summary>
        /// <param name="items">待创建的商品列表</param>
        /// <returns>批量操作结果</returns>
        public async Task<BatchOperationResultDto> BatchCreateAsync(List<CreateItemDto> items)
        {
            var result = new BatchOperationResultDto { Success = true, Message = "创建完成" };
            if (items == null || items.Count == 0)
                return result;

            try
            {
                // 开启事务
                _context.Db.Ado.BeginTran();
                var now = DateTime.Now;

                // 收集所有需要查询的 ProductCode 和 ItemNumber
                var codes = items
                    .Select(i => i.ProductCode)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct()
                    .ToList();
                var itemNumbers = items
                    .Select(i => i.ItemNumber)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .ToList();
                var queryProducts = _context.Db.Queryable<Product>();
                HashSet<string> existingCodes;
                HashSet<string> existingItems;
                Dictionary<string, string> itemToCode = new Dictionary<string, string>();
                HashSet<string> existingWpCodes = new HashSet<string>();

                // 批量查询已存在的商品（避免 N+1 问题）
                if (codes.Any() && itemNumbers.Any())
                {
                    queryProducts = queryProducts.Where(p =>
                        codes.Contains(p.ProductCode)
                        || (p.ItemNumber != null && itemNumbers.Contains(p.ItemNumber))
                    );
                    var existing = await queryProducts
                        .Select(p => new { p.ProductCode, p.ItemNumber })
                        .ToListAsync();
                    existingCodes = existing
                        .Select(p => p.ProductCode)
                        .Where(x => x != null)
                        .Select(x => x!)
                        .ToHashSet();
                    existingItems = existing
                        .Select(p => p.ItemNumber)
                        .Where(x => x != null)
                        .Select(x => x!)
                        .ToHashSet();
                    foreach (var e in existing)
                    {
                        if (
                            e.ItemNumber != null
                            && e.ProductCode != null
                            && !itemToCode.ContainsKey(e.ItemNumber)
                        )
                        {
                            itemToCode[e.ItemNumber] = e.ProductCode;
                        }
                    }
                    var mappedCodes = existing
                        .Select(p => p.ProductCode)
                        .Where(x => x != null)
                        .Select(x => x!)
                        .Distinct()
                        .ToList();
                    var wpExisting = await _context
                        .Db.Queryable<WarehouseProduct>()
                        .Where(w => mappedCodes.Contains(w.ProductCode))
                        .Select(w => w.ProductCode)
                        .ToListAsync();
                    existingWpCodes = wpExisting.ToHashSet();
                }
                else if (codes.Any())
                {
                    queryProducts = queryProducts.Where(p => codes.Contains(p.ProductCode));
                    var existing = await queryProducts
                        .Select(p => new { p.ProductCode, p.ItemNumber })
                        .ToListAsync();
                    existingCodes = existing
                        .Select(p => p.ProductCode)
                        .Where(x => x != null)
                        .Select(x => x!)
                        .ToHashSet();
                    existingItems = existing
                        .Select(p => p.ItemNumber)
                        .Where(x => x != null)
                        .Select(x => x!)
                        .ToHashSet();
                    foreach (var e in existing)
                    {
                        if (
                            e.ItemNumber != null
                            && e.ProductCode != null
                            && !itemToCode.ContainsKey(e.ItemNumber)
                        )
                        {
                            itemToCode[e.ItemNumber] = e.ProductCode;
                        }
                    }
                    var wpExisting = await _context
                        .Db.Queryable<WarehouseProduct>()
                        .Where(w => codes.Contains(w.ProductCode))
                        .Select(w => w.ProductCode)
                        .ToListAsync();
                    existingWpCodes = wpExisting.ToHashSet();
                }
                else if (itemNumbers.Any())
                {
                    queryProducts = queryProducts.Where(p =>
                        p.ItemNumber != null && itemNumbers.Contains(p.ItemNumber)
                    );
                    var existing = await queryProducts
                        .Select(p => new { p.ProductCode, p.ItemNumber })
                        .ToListAsync();
                    existingCodes = existing
                        .Select(p => p.ProductCode)
                        .Where(x => x != null)
                        .Select(x => x!)
                        .ToHashSet();
                    existingItems = existing
                        .Select(p => p.ItemNumber)
                        .Where(x => x != null)
                        .Select(x => x!)
                        .ToHashSet();
                    foreach (var e in existing)
                    {
                        if (
                            e.ItemNumber != null
                            && e.ProductCode != null
                            && !itemToCode.ContainsKey(e.ItemNumber)
                        )
                        {
                            itemToCode[e.ItemNumber] = e.ProductCode;
                        }
                    }
                    var mappedCodes = existing
                        .Select(p => p.ProductCode)
                        .Where(x => x != null)
                        .Select(x => x!)
                        .Distinct()
                        .ToList();
                    var wpExisting = await _context
                        .Db.Queryable<WarehouseProduct>()
                        .Where(w => mappedCodes.Contains(w.ProductCode))
                        .Select(w => w.ProductCode)
                        .ToListAsync();
                    existingWpCodes = wpExisting.ToHashSet();
                }
                else
                {
                    existingCodes = new HashSet<string>();
                    existingItems = new HashSet<string>();
                    existingWpCodes = new HashSet<string>();
                }

                // 待创建的商品、仓库商品、套装编码列表
                var toCreateProducts = new List<Product>();
                var toCreateWps = new List<WarehouseProduct>();
                var toCreateSetCodes = new List<ProductSetCode>();

                // 收集所有套装商品的 ProductCode（用于批量查询，避免 N+1 问题）
                var setProductCodesToQuery = new HashSet<string>();
                foreach (var item in items)
                {
                    if (!item.IsSetProduct)
                        continue;
                    var code = item.ProductCode;
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        if (
                            !string.IsNullOrWhiteSpace(item.ItemNumber)
                            && itemToCode.TryGetValue(item.ItemNumber!, out var mapped)
                        )
                        {
                            code = mapped;
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(code))
                        setProductCodesToQuery.Add(code!);
                }

                // 批量查询套装商品关联数据（一次性查询，避免 N+1）
                var setProductsByCode = setProductCodesToQuery.Any()
                    ? (
                        await _context
                            .Db.Queryable<DomesticSetProduct>()
                            .Where(sp =>
                                setProductCodesToQuery.Contains(sp.ProductCode) && !sp.IsDeleted
                            )
                            .ToListAsync()
                    )
                        .GroupBy(sp => sp.ProductCode)
                        .ToDictionary(g => g.Key, g => g.ToList())
                    : new Dictionary<string, List<DomesticSetProduct>>();

                // 遍历处理每个商品
                foreach (var item in items)
                {
                    // 验证必填字段
                    if (string.IsNullOrWhiteSpace(item.ItemNumber))
                    {
                        result.Errors.Add("ItemNumber cannot be empty");
                        result.FailedCount++;
                        continue;
                    }
                    if (item.OEMPrice <= 0)
                    {
                        result.Errors.Add($"OEM price must be greater than 0: {item.ItemNumber}");
                        result.FailedCount++;
                        continue;
                    }
                    if (item.ImportPrice <= 0)
                    {
                        result.Errors.Add(
                            $"Import price must be greater than 0: {item.ItemNumber}"
                        );
                        result.FailedCount++;
                        continue;
                    }

                    // 确定 ProductCode（如果未提供则自动生成）
                    var code = item.ProductCode;
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        if (
                            !string.IsNullOrWhiteSpace(item.ItemNumber)
                            && itemToCode.TryGetValue(item.ItemNumber!, out var mapped)
                        )
                        {
                            code = mapped;
                        }
                        else
                        {
                            code = UuidHelper.GenerateUuid7();
                        }
                    }

                    // 检查是否已存在
                    var wpExists =
                        !string.IsNullOrWhiteSpace(code) && existingWpCodes.Contains(code!);
                    var productExists =
                        existingCodes.Contains(code)
                        || (
                            !string.IsNullOrWhiteSpace(item.ItemNumber)
                            && existingItems.Contains(item.ItemNumber!)
                        );

                    // 跳过已存在的仓库商品
                    if (wpExists)
                    {
                        result.SkippedItems.Add(item.ItemNumber);
                        result.SkippedCount++;
                        continue;
                    }

                    // 创建商品记录（如果不存在）
                    if (!productExists)
                    {
                        var product = new Product
                        {
                            ProductCode = code,
                            ItemNumber = item.ItemNumber,
                            Barcode = item.Barcode,
                            LocalSupplierCode = "200",
                            ProductName =
                                !string.IsNullOrWhiteSpace(item.EnglishName) ? item.EnglishName
                                : !string.IsNullOrWhiteSpace(item.ChineseName) ? item.ChineseName
                                : item.ItemNumber,
                            EnglishName = item.EnglishName,
                            PurchasePrice = item.ImportPrice,
                            ProductImage = ProductImageUrlHelper.EnsureImageUrl(
                                item.ImageUrl,
                                item.ItemNumber ?? code
                            ),
                            IsAutoPricing = false,
                            IsActive = true,
                            IsDeleted = false,
                            CreatedAt = now,
                            UpdatedAt = now,
                        };
                        toCreateProducts.Add(product);
                    }

                    // 创建仓库商品记录
                    var wp = new WarehouseProduct
                    {
                        ProductCode = code,
                        DomesticPrice = item.DomesticPrice,
                        OEMPrice = item.OEMPrice,
                        ImportPrice = item.ImportPrice,
                        Volume = item.Volume,
                        StockQuantity = 0,
                        IsActive = true,
                        IsDeleted = false,
                        CreatedAt = now,
                        UpdatedAt = now,
                    };
                    toCreateWps.Add(wp);

                    // 处理套装商品（使用内存查找，避免 N+1）
                    if (item.IsSetProduct && !string.IsNullOrWhiteSpace(code))
                    {
                        if (setProductsByCode.TryGetValue(code!, out var setProducts))
                        {
                            foreach (var sp in setProducts)
                            {
                                var setCode = new ProductSetCode
                                {
                                    SetCodeId = sp.SetProductCode!,
                                    ProductCode = code,
                                    SetItemNumber = sp.SetProductNo,
                                    SetBarcode = sp.SetBarcode,
                                    SetPurchasePrice = sp.ImportPrice ?? item.ImportPrice,
                                    SetRetailPrice = sp.OEMPrice ?? item.OEMPrice,
                                    SetQuantity = 1,
                                    SetType = 1,
                                    IsActive = true,
                                    IsDeleted = false,
                                    CreatedAt = now,
                                    UpdatedAt = now,
                                };
                                toCreateSetCodes.Add(setCode);
                            }
                        }
                    }

                    result.SuccessCount++;
                }

                // 批量插入商品
                if (toCreateProducts.Any())
                {
                    await _context.Db.Insertable(toCreateProducts).ExecuteCommandAsync();
                }
                // 批量插入仓库商品
                if (toCreateWps.Any())
                {
                    await _context.Db.Insertable(toCreateWps).ExecuteCommandAsync();
                }
                // 批量插入套装编码
                if (toCreateSetCodes.Any())
                {
                    await _context.Db.Insertable(toCreateSetCodes).ExecuteCommandAsync();
                }

                // 同步到门店零售价和多码商品表
                var activeStores = await _context
                    .Db.Queryable<Store>()
                    .Where(s => s.IsActive == true && s.IsDeleted == false)
                    .Select(s => s.StoreCode)
                    .ToListAsync();

                if (activeStores.Any() && toCreateProducts.Any())
                {
                    var toCreateStoreRetailPrices = new List<StoreRetailPrice>();
                    var toCreateStoreMultiCodeProducts = new List<StoreMultiCodeProduct>();

                    var createdProductsDict = toCreateProducts.ToDictionary(p => p.ProductCode);

                    foreach (var product in toCreateProducts)
                    {
                        foreach (var storeCode in activeStores)
                        {
                            toCreateStoreRetailPrices.Add(
                                new StoreRetailPrice
                                {
                                    UUID = UuidHelper.GenerateUuid7(),
                                    StoreCode = storeCode,
                                    ProductCode = product.ProductCode,
                                    StoreProductCode = storeCode + product.ProductCode,
                                    SupplierCode = product.LocalSupplierCode,
                                    PurchasePrice = product.PurchasePrice,
                                    StoreRetailPriceValue = null,
                                    DiscountRate = null,
                                    IsActive = true,
                                    IsAutoPricing = false,
                                    IsSpecialProduct = false,
                                    CreatedAt = now,
                                    UpdatedAt = now,
                                }
                            );
                        }
                    }

                    foreach (var setCode in toCreateSetCodes)
                    {
                        foreach (var storeCode in activeStores)
                        {
                            toCreateStoreMultiCodeProducts.Add(
                                new StoreMultiCodeProduct
                                {
                                    UUID = UuidHelper.GenerateUuid7(),
                                    StoreCode = storeCode,
                                    ProductCode = setCode.ProductCode,
                                    MultiCodeProductCode = setCode.SetProductCode,
                                    StoreMultiCodeProductCode = storeCode + setCode.SetProductCode,
                                    MultiBarcode = setCode.SetBarcode,
                                    PurchasePrice = setCode.SetPurchasePrice,
                                    MultiCodeRetailPrice = setCode.SetRetailPrice,
                                    DiscountRate = null,
                                    IsActive = true,
                                    IsAutoPricing = false,
                                    IsSpecialProduct = false,
                                    CreatedAt = now,
                                    UpdatedAt = now,
                                }
                            );
                        }
                    }

                    if (toCreateStoreRetailPrices.Any())
                    {
                        await _context
                            .Db.Insertable(toCreateStoreRetailPrices)
                            .PageSize(1000)
                            .ExecuteCommandAsync();
                        _logger.LogInformation(
                            "创建了 {Count} 条分店价格记录",
                            toCreateStoreRetailPrices.Count
                        );
                    }

                    if (toCreateStoreMultiCodeProducts.Any())
                    {
                        await _context
                            .Db.Insertable(toCreateStoreMultiCodeProducts)
                            .PageSize(1000)
                            .ExecuteCommandAsync();
                        _logger.LogInformation(
                            "创建了 {Count} 条分店多码记录",
                            toCreateStoreMultiCodeProducts.Count
                        );
                    }
                }

                _context.Db.Ado.CommitTran();
            }
            catch (Exception ex)
            {
                _context.Db.Ado.RollbackTran();
                _logger.LogError(ex, "批量创建失败");
                return new BatchOperationResultDto
                {
                    Success = false,
                    Message = "批量创建失败: " + ex.Message,
                    Errors = new List<string> { ex.Message },
                };
            }

            return result;
        }

        /// <summary>
        /// 获取仓库商品列表（Antd Table 格式）
        /// 支持分类筛选、关键词搜索、分页
        /// 关联查询：仓库商品 + 国内商品 + 中国供应商 + 商品 + 仓库分类
        /// </summary>
        /// <param name="request">表格请求参数</param>
        /// <returns>分页后的仓库商品列表</returns>
        public async Task<
            ReactTableResponseDto<WarehouseProductReactListDto>
        > GetAntdTableDataAsync(ReactTableRequestDto request)
        {
            var resp = new ReactTableResponseDto<WarehouseProductReactListDto>();
            // 多表关联查询（使用 LeftJoin 避免 N+1 问题）
            var query = _context
                .Db.Queryable<WarehouseProduct>()
                .LeftJoin<DomesticProduct>((w, dp) => dp.ProductCode == w.ProductCode)
                .LeftJoin<ChinaSupplier>((w, dp, s) => dp.SupplierCode == s.SupplierCode)
                .InnerJoin<Product>((w, dp, s, p) => p.ProductCode == w.ProductCode)
                .LeftJoin<WarehouseCategory>(
                    (w, dp, s, p, c) => p.WarehouseCategoryGUID == c.CategoryGUID
                );

            // 分类筛选（支持包含子分类）
            if (request.CategoryGuids != null && request.CategoryGuids.Any())
            {
                var guids = request.IncludeSubCategories
                    ? GetCategoryAndSubCategories(request.CategoryGuids)
                    : request.CategoryGuids;
                query = query.Where(
                    (w, dp, s, p, c) =>
                        p.WarehouseCategoryGUID != null && guids.Contains(p.WarehouseCategoryGUID)
                );
            }

            if (!string.IsNullOrWhiteSpace(request.GlobalSearch))
            {
                var keyword = request.GlobalSearch.Trim();
                var keywordLower = keyword.ToLower();
                query = query.Where(
                    (w, dp, s, p, c) =>
                        (dp.ProductName != null && dp.ProductName.ToLower().Contains(keywordLower))
                        || (
                            dp.EnglishProductName != null
                            && dp.EnglishProductName.ToLower().Contains(keywordLower)
                        )
                        || (
                            dp.HBProductNo != null
                            && dp.HBProductNo.ToLower().Contains(keywordLower)
                        )
                        || (dp.Barcode != null && dp.Barcode.ToLower().Contains(keywordLower))
                        || (
                            c.CategoryName != null
                            && c.CategoryName.ToLower().Contains(keywordLower)
                        )
                        || (
                            s.SupplierName != null
                            && s.SupplierName.ToLower().Contains(keywordLower)
                        )
                );
            }

            if (request.Filters != null && request.Filters.Any())
            {
                foreach (var kv in request.Filters)
                {
                    var key = kv.Key?.ToLower();
                    var values =
                        kv.Value?.Where(v => !string.IsNullOrWhiteSpace(v)).ToList()
                        ?? new List<string>();
                    if (!values.Any())
                        continue;
                    switch (key)
                    {
                        case "productname":
                        case "name":
                            {
                                var lowers = values.Select(v => v.ToLower()).ToList();
                                query = query.Where(
                                    (w, dp, s, p, c) =>
                                        p.ProductName != null
                                        && lowers.Any(v => p.ProductName.ToLower().Contains(v))
                                );
                            }
                            break;
                        case "nameen":
                            {
                                var lowers = values.Select(v => v.ToLower()).ToList();
                                query = query.Where(
                                    (w, dp, s, p, c) =>
                                        p.EnglishName != null
                                        && lowers.Any(v => p.EnglishName.ToLower().Contains(v))
                                );
                            }
                            break;
                        case "itemnumber":
                            {
                                var lowers = values.Select(v => v.ToLower()).ToList();
                                query = query.Where(
                                    (w, dp, s, p, c) =>
                                        p.ItemNumber != null
                                        && lowers.Any(v => p.ItemNumber.ToLower().Contains(v))
                                );
                            }
                            break;
                        case "barcode":
                            {
                                var lowers = values.Select(v => v.ToLower()).ToList();
                                query = query.Where(
                                    (w, dp, s, p, c) =>
                                        p.Barcode != null
                                        && lowers.Any(v => p.Barcode.ToLower().Contains(v))
                                );
                            }
                            break;
                        case "categoryname":
                            {
                                var lowers = values.Select(v => v.ToLower()).ToList();
                                query = query.Where(
                                    (w, dp, s, p, c) =>
                                        c.CategoryName != null
                                        && lowers.Any(v => c.CategoryName.ToLower().Contains(v))
                                );
                            }
                            break;
                        case "suppliername":
                            {
                                var lowers = values.Select(v => v.ToLower()).ToList();
                                query = query.Where(
                                    (w, dp, s, p, c) =>
                                        s.SupplierName != null
                                        && lowers.Any(v => s.SupplierName.ToLower().Contains(v))
                                );
                            }
                            break;
                        case "suppliercode":
                            {
                                var lowers = values.Select(v => v.ToLower()).ToList();
                                query = query.Where(
                                    (w, dp, s, p, c) =>
                                        s.SupplierCode != null
                                        && lowers.Any(v => s.SupplierCode.ToLower().Contains(v))
                                );
                            }
                            break;
                        case "domesticprice":
                            query = query.Where(w =>
                                w.DomesticPrice.HasValue
                                && values.Contains(w.DomesticPrice.Value.ToString())
                            );
                            break;
                        case "oemprice":
                            query = query.Where(w =>
                                w.OEMPrice.HasValue && values.Contains(w.OEMPrice.Value.ToString())
                            );
                            break;
                        case "importprice":
                            query = query.Where(w =>
                                w.ImportPrice.HasValue
                                && values.Contains(w.ImportPrice.Value.ToString())
                            );
                            break;
                        case "volume":
                            query = query.Where(w =>
                                w.Volume.HasValue && values.Contains(w.Volume.Value.ToString())
                            );
                            break;
                        case "isactive":
                            var flags = values.Select(v => v.Trim().ToLower()).ToList();
                            if (!flags.Contains("all"))
                            {
                                query = query.Where(w =>
                                    (w.IsActive && (flags.Contains("1") || flags.Contains("true")))
                                    || (
                                        !w.IsActive
                                        && (flags.Contains("0") || flags.Contains("false"))
                                    )
                                );
                            }
                            break;
                        case "producttype":
                            query = query.Where(
                                (w, dp, s, p, c) => values.Contains(dp.ProductType.ToString())
                            );
                            break;
                    }
                }
            }

            var orderDesc = string.Equals(
                request.SortOrder,
                "descend",
                StringComparison.OrdinalIgnoreCase
            );
            if (!string.IsNullOrWhiteSpace(request.SortBy))
            {
                var sort = request.SortBy.ToLower();
                if (sort == "productname" || sort == "name")
                    query = orderDesc
                        ? query.OrderBy((w, dp, s, p, c) => p.ProductName, OrderByType.Desc)
                        : query.OrderBy((w, dp, s, p, c) => dp.ProductName, OrderByType.Asc);
                else if (sort == "nameen")
                    query = orderDesc
                        ? query.OrderBy((w, dp, s, p, c) => p.EnglishName, OrderByType.Desc)
                        : query.OrderBy((w, dp, s, p, c) => p.EnglishName, OrderByType.Asc);
                else if (sort == "itemnumber")
                    query = orderDesc
                        ? query.OrderBy((w, dp, s, p, c) => p.ItemNumber, OrderByType.Desc)
                        : query.OrderBy((w, dp, s, p, c) => p.ItemNumber, OrderByType.Asc);
                else if (sort == "barcode")
                    query = orderDesc
                        ? query.OrderBy((w, dp, s, p, c) => p.Barcode, OrderByType.Desc)
                        : query.OrderBy((w, dp, s, p, c) => p.Barcode, OrderByType.Asc);
                else if (sort == "categoryname")
                    query = orderDesc
                        ? query.OrderBy((w, dp, s, p, c) => c.CategoryName, OrderByType.Desc)
                        : query.OrderBy((w, dp, s, p, c) => c.CategoryName, OrderByType.Asc);
                else if (sort == "suppliername")
                    query = orderDesc
                        ? query.OrderBy((w, dp, s, p, c) => s.SupplierName, OrderByType.Desc)
                        : query.OrderBy((w, dp, s, p, c) => s.SupplierName, OrderByType.Asc);
                else if (sort == "suppliercode")
                    query = orderDesc
                        ? query.OrderBy((w, dp, s, p, c) => s.SupplierCode, OrderByType.Desc)
                        : query.OrderBy((w, dp, s, p, c) => s.SupplierCode, OrderByType.Asc);
                else if (sort == "domesticprice")
                    query = orderDesc
                        ? query.OrderBy(w => w.DomesticPrice, OrderByType.Desc)
                        : query.OrderBy(w => w.DomesticPrice, OrderByType.Asc);
                else if (sort == "oemprice")
                    query = orderDesc
                        ? query.OrderBy(w => w.OEMPrice, OrderByType.Desc)
                        : query.OrderBy(w => w.OEMPrice, OrderByType.Asc);
                else if (sort == "importprice")
                    query = orderDesc
                        ? query.OrderBy(w => w.ImportPrice, OrderByType.Desc)
                        : query.OrderBy(w => w.ImportPrice, OrderByType.Asc);
                else if (sort == "volume")
                    query = orderDesc
                        ? query.OrderBy(w => w.Volume, OrderByType.Desc)
                        : query.OrderBy(w => w.Volume, OrderByType.Asc);
                else if (sort == "minorderquantity")
                    query = orderDesc
                        ? query.OrderBy(w => w.MinOrderQuantity, OrderByType.Desc)
                        : query.OrderBy(w => w.MinOrderQuantity, OrderByType.Asc);
                else if (sort == "createdat")
                    query = orderDesc
                        ? query.OrderBy(w => w.CreatedAt, OrderByType.Desc)
                        : query.OrderBy(w => w.CreatedAt, OrderByType.Asc);
                else if (sort == "updatedat")
                    query = orderDesc
                        ? query.OrderBy(w => w.UpdatedAt, OrderByType.Desc)
                        : query.OrderBy(w => w.UpdatedAt, OrderByType.Asc);
                else
                    query = query.OrderBy(w => w.UpdatedAt, OrderByType.Desc);
            }
            else
            {
                query = query.OrderBy(w => w.UpdatedAt, OrderByType.Desc);
            }

            var total = await query.Clone().CountAsync();

            var items = await query
                .Select(
                    (w, dp, s, p, c) =>
                        new WarehouseProductReactListDto
                        {
                            ProductCode = w.ProductCode,
                            ProductName = p.ProductName,
                            EnglishName = p.EnglishName,
                            ItemNumber = p.ItemNumber,
                            Barcode = p.Barcode,
                            CategoryName = c.CategoryName,
                            SupplierName = s.SupplierName,
                            SupplierCode = s.SupplierCode,
                            LocalSupplierCode = p.LocalSupplierCode,
                            DomesticPrice = w.DomesticPrice,
                            OEMPrice = w.OEMPrice,
                            ImportPrice = w.ImportPrice,
                            Volume = w.Volume,
                            MinOrderQuantity = w.MinOrderQuantity,
                            IsActive = w.IsActive,
                            CreatedAt = w.CreatedAt,
                            UpdatedAt = w.UpdatedAt,
                            ProductImage = p.ProductImage,
                            ProductType = dp.ProductType,
                        }
                )
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToListAsync();

            // 查询结束后再在内存中补全图片 URL（避免 SqlSugar 翻译自定义方法）
            foreach (var dto in items)
            {
                dto.ProductImage = ProductImageUrlHelper.EnsureImageUrl(
                    dto.ProductImage,
                    dto.ItemNumber ?? dto.ProductCode
                );
            }

            resp.Items = items;
            resp.Total = total;
            return resp;
        }

        private List<string> GetCategoryAndSubCategories(List<string> categoryGuids)
        {
            var all = _context.Db.Queryable<WarehouseCategory>().ToList();
            var result = new HashSet<string>(categoryGuids.Where(g => !string.IsNullOrEmpty(g)));
            var stack = new Stack<string>(result);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                var children = all.Where(c => c.ParentGUID == cur && c.IsActive)
                    .Select(c => c.CategoryGUID)
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                foreach (var ch in children)
                {
                    if (result.Add(ch))
                        stack.Push(ch);
                }
            }
            return result.ToList();
        }

        /// <summary>
        /// 新建单个仓库商品：货号/商品编码可自动生成，支持普通/套装/一品多码，分店零售价可默认补充。
        /// </summary>
        public async Task<CreateSingleProductResponseDto> CreateSingleProductAsync(
            CreateSingleProductRequestDto request
        )
        {
            var response = new CreateSingleProductResponseDto
            {
                Success = false,
                Message = "创建失败",
            };
            var warnings = new List<string>();

            try
            {
                // 1. 货号：为空时按供应商编码自动生成
                string itemNumber;
                if (string.IsNullOrWhiteSpace(request.ItemNumber))
                {
                    var supplierCode = request.SupplierCode?.Trim();
                    if (string.IsNullOrWhiteSpace(supplierCode))
                        supplierCode = request.SupplierId?.ToString();
                    if (string.IsNullOrWhiteSpace(supplierCode))
                    {
                        response.Message = "货号为空时需提供供应商编码以自动生成";
                        return response;
                    }
                    (itemNumber, _) = await _itemBarcodeService.GenerateItemNumberAndBarcodeAsync(
                        supplierCode!,
                        request.ProductType
                    );
                }
                else
                {
                    itemNumber = request.ItemNumber;
                }

                if (request.OEMPrice <= 0)
                {
                    response.Message = "贴牌价格必须大于0";
                    return response;
                }
                if (request.ImportPrice <= 0)
                {
                    response.Message = "进口价格必须大于0";
                    return response;
                }

                // 1.1 规范化图片地址：如果为空或不是 http(s)，按货号规则生成
                var finalImageUrl = ProductImageUrlHelper.EnsureImageUrl(
                    request.ImageUrl,
                    itemNumber
                );

                // 2. 商品编码：为空时自动生成 UUID7
                var productCode = request.ProductCode;
                if (string.IsNullOrWhiteSpace(productCode))
                {
                    productCode = UuidHelper.GenerateUuid7();
                }

                // 3. 货号/条码校验：并发查询，减少往返
                Product? existingProductByItemNumber = null;
                Product? existingBarcodeProduct = null;
                var barcodeExists = false;

                var queryByItemNumber = async () =>
                {
                    using var conn = SqlSugarContext.CreateConcurrentConnection(_configuration);
                    return await conn.Queryable<Product>()
                        .Where(p => p.ItemNumber == itemNumber && !p.IsDeleted)
                        .FirstAsync();
                };
                var queryByBarcode = async () =>
                {
                    if (string.IsNullOrWhiteSpace(request.Barcode))
                        return (Product?)null;
                    using var conn = SqlSugarContext.CreateConcurrentConnection(_configuration);
                    return await conn.Queryable<Product>()
                        .Where(p => p.Barcode == request.Barcode && !p.IsDeleted)
                        .FirstAsync();
                };

                var taskByItemNumber = queryByItemNumber();
                var taskByBarcode = queryByBarcode();
                await Task.WhenAll(taskByItemNumber, taskByBarcode);
                existingProductByItemNumber = await taskByItemNumber;
                existingBarcodeProduct = await taskByBarcode;

                if (existingProductByItemNumber != null)
                {
                    response.Message = "货号已存在";
                    return response;
                }
                if (existingBarcodeProduct != null)
                {
                    barcodeExists = true;
                    warnings.Add($"条码 {request.Barcode} 已存在于系统中");
                }

                // 4. 条码：为空时按供应商编码自动生成 EAN-13
                var supplierCodeForBarcode =
                    request.SupplierCode?.Trim() ?? request.SupplierId?.ToString();
                string? barcodeToUse = request.Barcode;
                if (
                    string.IsNullOrWhiteSpace(barcodeToUse)
                    && !string.IsNullOrWhiteSpace(supplierCodeForBarcode)
                )
                {
                    (_, barcodeToUse) = await _itemBarcodeService.GenerateItemNumberAndBarcodeAsync(
                        supplierCodeForBarcode!,
                        request.ProductType
                    );
                }

                _context.Db.Ado.BeginTran();
                var now = DateTime.Now;

                // 5. 并发查询 DomesticProduct、WarehouseProduct 和活跃门店
                // 使用独立连接避免连接冲突
                var domesticProductTask = async () =>
                {
                    using var conn = _context.CreateConcurrentQueryConnection();
                    return await conn.Queryable<DomesticProduct>()
                        .Where(dp => dp.ProductCode == productCode)
                        .FirstAsync();
                };
                var warehouseProductTask = async () =>
                {
                    using var conn = _context.CreateConcurrentQueryConnection();
                    return await conn.Queryable<WarehouseProduct>()
                        .Where(wp => wp.ProductCode == productCode)
                        .FirstAsync();
                };
                var activeStoresTask = async () =>
                {
                    using var conn = _context.CreateConcurrentQueryConnection();
                    return await conn.Queryable<Store>()
                        .Where(s => s.IsActive == true && s.IsDeleted == false)
                        .Select(s => s.StoreCode)
                        .ToListAsync();
                };

                var taskDomestic = domesticProductTask();
                var taskWarehouse = warehouseProductTask();
                var taskStores = activeStoresTask();
                await Task.WhenAll(taskDomestic, taskWarehouse, taskStores);

                var domesticProduct = await taskDomestic;
                var warehouseProduct = await taskWarehouse;
                var activeStores = await taskStores;

                // 6. 插入商品主表 Product
                var product = new Product
                {
                    ProductCode = productCode,
                    ItemNumber = itemNumber,
                    Barcode = barcodeToUse,
                    LocalSupplierCode = "200",
                    ProductName = request.ChineseName,
                    EnglishName = request.EnglishName,
                    PurchasePrice = request.ImportPrice,
                    ProductImage = finalImageUrl,
                    IsAutoPricing = false,
                    IsActive = true,
                    IsDeleted = false,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                await _context.Db.Insertable(product).ExecuteCommandAsync();

                // 7. 国内商品表 DomesticProduct：无则新增，有则更新
                if (domesticProduct == null)
                {
                    domesticProduct = new DomesticProduct
                    {
                        ProductCode = productCode,
                        HBProductNo = itemNumber,
                        Barcode = barcodeToUse,
                        ProductName = request.ChineseName,
                        EnglishProductName = request.EnglishName,
                        SupplierCode = request.SupplierCode ?? request.SupplierId?.ToString(),
                        DomesticPrice = request.DomesticPrice,
                        OEMPrice = request.OEMPrice,
                        ImportPrice = request.ImportPrice,
                        UnitVolume = request.Volume,
                        ProductType = (int)request.ProductType,
                        ProductImage = finalImageUrl,
                        IsActive = true,
                        IsDeleted = false,
                        CreatedAt = now,
                        UpdatedAt = now,
                    };
                    await _context.Db.Insertable(domesticProduct).ExecuteCommandAsync();
                }
                else
                {
                    domesticProduct.ProductName = request.ChineseName;
                    domesticProduct.EnglishProductName = request.EnglishName;
                    domesticProduct.Barcode = barcodeToUse;
                    domesticProduct.DomesticPrice = request.DomesticPrice;
                    domesticProduct.OEMPrice = request.OEMPrice;
                    domesticProduct.ImportPrice = request.ImportPrice;
                    domesticProduct.UnitVolume = request.Volume;
                    domesticProduct.ProductType = (int)request.ProductType;
                    domesticProduct.UpdatedAt = now;
                    await _context
                        .Db.Updateable(domesticProduct)
                        .UpdateColumns(dp => new
                        {
                            dp.ProductName,
                            dp.EnglishProductName,
                            dp.Barcode,
                            dp.DomesticPrice,
                            dp.OEMPrice,
                            dp.ImportPrice,
                            dp.UnitVolume,
                            dp.ProductType,
                            dp.UpdatedAt,
                        })
                        .ExecuteCommandAsync();
                }

                // 8. 仓库商品表 WarehouseProduct：无则新增，有则更新
                if (warehouseProduct == null)
                {
                    warehouseProduct = new WarehouseProduct
                    {
                        ProductCode = productCode,
                        DomesticPrice = request.DomesticPrice,
                        OEMPrice = request.OEMPrice,
                        ImportPrice = request.ImportPrice,
                        Volume = request.Volume,
                        StockQuantity = 0,
                        IsActive = request.IsActive,
                        IsDeleted = false,
                        CreatedAt = now,
                        UpdatedAt = now,
                    };
                    await _context.Db.Insertable(warehouseProduct).ExecuteCommandAsync();
                }
                else
                {
                    warehouseProduct.DomesticPrice = request.DomesticPrice;
                    warehouseProduct.OEMPrice = request.OEMPrice;
                    warehouseProduct.ImportPrice = request.ImportPrice;
                    warehouseProduct.Volume = request.Volume;
                    warehouseProduct.IsActive = request.IsActive;
                    warehouseProduct.UpdatedAt = now;
                    await _context
                        .Db.Updateable(warehouseProduct)
                        .UpdateColumns(wp => new
                        {
                            wp.DomesticPrice,
                            wp.OEMPrice,
                            wp.ImportPrice,
                            wp.Volume,
                            wp.IsActive,
                            wp.UpdatedAt,
                        })
                        .ExecuteCommandAsync();
                }

                var domesticSetProducts = new List<DomesticSetProduct>();
                var productSetCodes = new List<ProductSetCode>();

                // 9. 套装商品：先删旧再批量插入 DomesticSetProduct + ProductSetCode，SetProductCode 自动生成
                if (request.ProductType == ProductTypeEnum.Set && request.SetItems?.Any() == true)
                {
                    var existingSetProducts = await _context
                        .Db.Queryable<DomesticSetProduct>()
                        .Where(sp => sp.ProductCode == productCode && !sp.IsDeleted)
                        .ToListAsync();

                    await _context
                        .Db.Deleteable<DomesticSetProduct>()
                        .Where(sp => sp.ProductCode == productCode)
                        .ExecuteCommandAsync();

                    await _context
                        .Db.Deleteable<ProductSetCode>()
                        .Where(psc => psc.ProductCode == productCode)
                        .ExecuteCommandAsync();

                    foreach (var setItem in request.SetItems)
                    {
                        var domesticSetProduct = new DomesticSetProduct
                        {
                            ProductCode = productCode,
                            SetProductCode = UuidHelper.GenerateUuid7(),
                            SetProductNo = setItem.ItemNumber,
                            SetBarcode = setItem.Barcode,
                            ImportPrice = setItem.PurchasePrice,
                            OEMPrice = setItem.RetailPrice,
                            IsDeleted = false,
                            CreatedAt = now,
                            UpdatedAt = now,
                        };

                        domesticSetProducts.Add(domesticSetProduct);
                        productSetCodes.Add(
                            new ProductSetCode
                            {
                                SetCodeId = setItem.ProductCode,
                                ProductCode = productCode,
                                SetProductCode = domesticSetProduct.SetProductCode,
                                SetItemNumber = setItem.ItemNumber,
                                SetBarcode = setItem.Barcode,
                                SetPurchasePrice = setItem.PurchasePrice ?? request.ImportPrice,
                                SetRetailPrice = setItem.RetailPrice ?? request.OEMPrice,
                                SetQuantity = (int)setItem.Quantity,
                                SetType = request.SetType.HasValue ? (int)request.SetType.Value : 1,
                                IsActive = true,
                                IsDeleted = false,
                                CreatedAt = now,
                                UpdatedAt = now,
                            }
                        );
                    }
                    if (domesticSetProducts.Any())
                    {
                        await _context.Db.Insertable(domesticSetProducts).ExecuteCommandAsync();
                        await _context.Db.Insertable(productSetCodes).ExecuteCommandAsync();
                    }
                }

                // 10. 一品多码：多码条码为空时用 ItemNumberHelper 生成；按门店批量插入 StoreMultiCodeProduct
                if (
                    request.ProductType == ProductTypeEnum.MultiCode
                    && request.MultiCodeItems?.Any() == true
                )
                {
                    var existingMultiBarcodes = await _context
                        .Db.Queryable<StoreMultiCodeProduct>()
                        .Where(mcp => mcp.ProductCode == productCode && mcp.MultiBarcode != null)
                        .Select(mcp => mcp.MultiBarcode!)
                        .ToListAsync();

                    var resolvedBarcodes = new List<string>();
                    foreach (var multiCodeItem in request.MultiCodeItems)
                    {
                        var barcode = multiCodeItem.Barcode;
                        if (string.IsNullOrWhiteSpace(barcode))
                        {
                            barcode = ItemNumberHelper.GenerateSetItemNumber(
                                itemNumber,
                                existingMultiBarcodes
                            );
                            existingMultiBarcodes.Add(barcode);
                        }
                        resolvedBarcodes.Add(barcode);
                    }

                    await _context
                        .Db.Deleteable<StoreMultiCodeProduct>()
                        .Where(mcp => mcp.ProductCode == productCode)
                        .ExecuteCommandAsync();

                    var activeStoresForMultiCode = activeStores;

                    var multiCodeProducts = new List<StoreMultiCodeProduct>();
                    for (var i = 0; i < request.MultiCodeItems.Count; i++)
                    {
                        var multiCodeItem = request.MultiCodeItems[i];
                        var barcode = resolvedBarcodes[i];
                        var multiCodeKey = productSetCodes
                            .First(psc => psc.SetBarcode == barcode)
                            .SetProductCode;
                        foreach (var storeCode in activeStoresForMultiCode)
                        {
                            multiCodeProducts.Add(
                                new StoreMultiCodeProduct
                                {
                                    UUID = UuidHelper.GenerateUuid7(),
                                    ProductCode = productCode,
                                    StoreCode = storeCode,
                                    MultiCodeProductCode = multiCodeKey,
                                    StoreMultiCodeProductCode = storeCode + multiCodeKey,
                                    MultiBarcode = barcode,
                                    MultiCodeRetailPrice = multiCodeItem.RetailPrice,
                                    PurchasePrice = multiCodeItem.PurchasePrice,
                                    DiscountRate = multiCodeItem.DiscountRate,
                                    IsAutoPricing = multiCodeItem.AutoPricing,
                                    IsSpecialProduct = multiCodeItem.IsSpecialProduct,
                                    IsActive = multiCodeItem.IsActive,
                                    IsDeleted = false,
                                    CreatedAt = now,
                                    UpdatedAt = now,
                                }
                            );
                        }
                    }
                    if (multiCodeProducts.Any())
                        await _context.Db.Insertable(multiCodeProducts).ExecuteCommandAsync();
                }

                // 10. 分店零售价：有传则按传入覆盖并设 StoreProductCode；未传则按活跃门店用默认价（OEMPrice）补充
                if (request.StorePrices?.Any() == true)
                {
                    await _context
                        .Db.Deleteable<StoreRetailPrice>()
                        .Where(srp => srp.ProductCode == productCode)
                        .ExecuteCommandAsync();

                    var storeRetailPrices = request
                        .StorePrices.Select(storePrice => new StoreRetailPrice
                        {
                            ProductCode = productCode,
                            StoreCode = storePrice.StoreId.ToString(),
                            StoreProductCode = storePrice.StoreId.ToString() + productCode,
                            SupplierCode = "200",
                            PurchasePrice = storePrice.PurchasePrice,
                            StoreRetailPriceValue = storePrice.RetailPrice,
                            DiscountRate = storePrice.DiscountRate,
                            IsAutoPricing = storePrice.AutoPricing,
                            IsSpecialProduct = storePrice.IsSpecialProduct,
                            IsActive = storePrice.IsActive,
                            IsDeleted = false,
                            CreatedAt = now,
                            UpdatedAt = now,
                        })
                        .ToList();
                    await _context.Db.Insertable(storeRetailPrices).ExecuteCommandAsync();
                }
                else
                {
                    // 未传分店价：仅对尚未有分店价的门店补充默认价（StoreProductCode = storeCode + productCode）
                    var existingStoreCodes = await _context
                        .Db.Queryable<StoreRetailPrice>()
                        .Where(srp => srp.ProductCode == productCode && !srp.IsDeleted)
                        .Select(srp => srp.StoreCode)
                        .ToListAsync();
                    var existingSet = new HashSet<string?>(
                        existingStoreCodes.Where(c => !string.IsNullOrWhiteSpace(c))
                    );
                    var toInsert = new List<StoreRetailPrice>();
                    foreach (var storeCode in activeStores)
                    {
                        if (string.IsNullOrWhiteSpace(storeCode) || existingSet.Contains(storeCode))
                            continue;
                        toInsert.Add(
                            new StoreRetailPrice
                            {
                                ProductCode = productCode,
                                StoreCode = storeCode,
                                StoreProductCode = storeCode + productCode,
                                SupplierCode = "200",
                                PurchasePrice = request.ImportPrice,
                                StoreRetailPriceValue = request.OEMPrice,
                                DiscountRate = 0,
                                IsAutoPricing = false,
                                IsSpecialProduct = false,
                                IsActive = true,
                                IsDeleted = false,
                                CreatedAt = now,
                                UpdatedAt = now,
                            }
                        );
                    }
                    if (toInsert.Any())
                        await _context.Db.Insertable(toInsert).ExecuteCommandAsync();
                }

                _context.Db.Ado.CommitTran();

                response.Success = true;
                response.Message = "商品创建成功";
                response.ProductCode = productCode;
                response.ItemNumber = itemNumber;
                response.Barcode = barcodeToUse;
                response.BarcodeExists = barcodeExists;
                response.Warnings = warnings;
            }
            catch (Exception ex)
            {
                _context.Db.Ado.RollbackTran();
                _logger.LogError(ex, "创建单个商品失败");
                response.Message = "创建失败: " + ex.Message;
            }

            return response;
        }

        public async Task<
            ReactTableResponseDto<DomesticProductNotInWarehouseDto>
        > GetDomesticProductsNotInWarehouseAsync(
            GetDomesticProductsNotInWarehouseRequestDto request
        )
        {
            var resp = new ReactTableResponseDto<DomesticProductNotInWarehouseDto>();

            var query = _context
                .Db.Queryable<DomesticProduct>()
                .LeftJoin<ChinaSupplier>(
                    (dp, s) => dp.SupplierCode == s.SupplierCode && dp.SupplierCode != null
                )
                .Where((dp, s) => !dp.IsDeleted && dp.IsActive)
                .Where(
                    (dp, s) =>
                        !SqlFunc
                            .Subqueryable<WarehouseProduct>()
                            .Where(wp => wp.ProductCode == dp.ProductCode && !wp.IsDeleted)
                            .Any()
                );

            if (request.SupplierId.HasValue)
            {
                query = query.Where(
                    (dp, s) =>
                        dp.SupplierCode != null && dp.SupplierCode == request.SupplierId.ToString()
                );
            }

            if (request.ProductType.HasValue)
            {
                query = query.Where((dp, s) => dp.ProductType == (int)request.ProductType.Value);
            }

            if (!string.IsNullOrWhiteSpace(request.GlobalSearch))
            {
                var keyword = request.GlobalSearch.Trim().ToLower();
                query = query.Where(
                    (dp, s) =>
                        (dp.ProductName != null && dp.ProductName.ToLower().Contains(keyword))
                        || (
                            dp.EnglishProductName != null
                            && dp.EnglishProductName.ToLower().Contains(keyword)
                        )
                        || (dp.HBProductNo != null && dp.HBProductNo.ToLower().Contains(keyword))
                        || (dp.Barcode != null && dp.Barcode.ToLower().Contains(keyword))
                        || (s.SupplierName != null && s.SupplierName.ToLower().Contains(keyword))
                );
            }

            if (request.Filters != null && request.Filters.Any())
            {
                foreach (var kv in request.Filters)
                {
                    var key = kv.Key?.ToLower();
                    var values =
                        kv.Value?.Where(v => !string.IsNullOrWhiteSpace(v)).ToList()
                        ?? new List<string>();
                    if (!values.Any())
                        continue;
                    switch (key)
                    {
                        case "productname":
                        case "name":
                            {
                                var lowers = values.Select(v => v.ToLower()).ToList();
                                query = query.Where(
                                    (dp, s) =>
                                        dp.ProductName != null
                                        && lowers.Any(v => dp.ProductName.ToLower().Contains(v))
                                );
                            }
                            break;
                        case "nameen":
                            {
                                var lowers = values.Select(v => v.ToLower()).ToList();
                                query = query.Where(
                                    (dp, s) =>
                                        dp.EnglishProductName != null
                                        && lowers.Any(v =>
                                            dp.EnglishProductName.ToLower().Contains(v)
                                        )
                                );
                            }
                            break;
                        case "itemnumber":
                            {
                                var lowers = values.Select(v => v.ToLower()).ToList();
                                query = query.Where(
                                    (dp, s) =>
                                        dp.HBProductNo != null
                                        && lowers.Any(v => dp.HBProductNo.ToLower().Contains(v))
                                );
                            }
                            break;
                        case "barcode":
                            {
                                var lowers = values.Select(v => v.ToLower()).ToList();
                                query = query.Where(
                                    (dp, s) =>
                                        dp.Barcode != null
                                        && lowers.Any(v => dp.Barcode.ToLower().Contains(v))
                                );
                            }
                            break;
                        case "suppliername":
                            {
                                var lowers = values.Select(v => v.ToLower()).ToList();
                                query = query.Where(
                                    (dp, s) =>
                                        s.SupplierName != null
                                        && lowers.Any(v => s.SupplierName.ToLower().Contains(v))
                                );
                            }
                            break;
                    }
                }
            }

            var orderDesc = string.Equals(
                request.SortOrder,
                "descend",
                StringComparison.OrdinalIgnoreCase
            );
            if (!string.IsNullOrWhiteSpace(request.SortBy))
            {
                var sort = request.SortBy.ToLower();
                if (sort == "productname" || sort == "name")
                    query = orderDesc
                        ? query.OrderBy((dp, s) => dp.ProductName, OrderByType.Desc)
                        : query.OrderBy((dp, s) => dp.ProductName, OrderByType.Asc);
                else if (sort == "nameen")
                    query = orderDesc
                        ? query.OrderBy((dp, s) => dp.EnglishProductName, OrderByType.Desc)
                        : query.OrderBy((dp, s) => dp.EnglishProductName, OrderByType.Asc);
                else if (sort == "itemnumber")
                    query = orderDesc
                        ? query.OrderBy((dp, s) => dp.HBProductNo, OrderByType.Desc)
                        : query.OrderBy((dp, s) => dp.HBProductNo, OrderByType.Asc);
                else if (sort == "barcode")
                    query = orderDesc
                        ? query.OrderBy((dp, s) => dp.Barcode, OrderByType.Desc)
                        : query.OrderBy((dp, s) => dp.Barcode, OrderByType.Asc);
                else if (sort == "suppliername")
                    query = orderDesc
                        ? query.OrderBy((dp, s) => s.SupplierName, OrderByType.Desc)
                        : query.OrderBy((dp, s) => s.SupplierName, OrderByType.Asc);
                else
                    query = query.OrderBy((dp, s) => dp.UpdatedAt, OrderByType.Desc);
            }
            else
            {
                query = query.OrderBy((dp, s) => dp.UpdatedAt, OrderByType.Desc);
            }

            var total = await query.Clone().CountAsync();

            var items = await query
                .Select(
                    (dp, s) =>
                        new
                        {
                            ProductCode = dp.ProductCode,
                            ItemNumber = dp.HBProductNo,
                            Barcode = dp.Barcode,
                            ProductName = dp.ProductName,
                            EnglishName = dp.EnglishProductName,
                            ProductType = (ProductTypeEnum)dp.ProductType,
                            DomesticPrice = dp.DomesticPrice,
                            OEMPrice = dp.OEMPrice ?? 0m,
                            ImportPrice = dp.ImportPrice ?? 0m,
                            Volume = dp.UnitVolume,
                            SupplierName = s.SupplierName,
                            SupplierCodeStr = dp.SupplierCode,
                            HasSetProducts = false,
                            HasMultiCodes = false,
                        }
                )
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToListAsync();

            var result = new List<DomesticProductNotInWarehouseDto>();
            foreach (var item in items)
            {
                int? supplierId = null;
                if (item.SupplierCodeStr != null && int.TryParse(item.SupplierCodeStr, out var sid))
                {
                    supplierId = sid;
                }
                result.Add(
                    new DomesticProductNotInWarehouseDto
                    {
                        ProductCode = item.ProductCode,
                        ItemNumber = item.ItemNumber,
                        Barcode = item.Barcode,
                        ProductName = item.ProductName,
                        EnglishName = item.EnglishName,
                        ProductType = item.ProductType,
                        DomesticPrice = item.DomesticPrice,
                        OEMPrice = item.OEMPrice,
                        ImportPrice = item.ImportPrice,
                        Volume = item.Volume,
                        SupplierName = item.SupplierName,
                        SupplierId = supplierId,
                        HasSetProducts = item.HasSetProducts,
                        HasMultiCodes = item.HasMultiCodes,
                    }
                );
            }

            var productCodes = result.Select(i => i.ProductCode).ToList();
            if (productCodes.Any())
            {
                var setProducts = await _context
                    .Db.Queryable<DomesticSetProduct>()
                    .Where(sp => productCodes.Contains(sp.ProductCode) && !sp.IsDeleted)
                    .Select(sp => sp.ProductCode)
                    .ToListAsync();
                var multiCodes = await _context
                    .Db.Queryable<StoreMultiCodeProduct>()
                    .Where(mcp => productCodes.Contains(mcp.ProductCode) && !mcp.IsDeleted)
                    .Select(mcp => mcp.ProductCode)
                    .ToListAsync();

                foreach (var item in result)
                {
                    item.HasSetProducts = setProducts.Contains(item.ProductCode);
                    item.HasMultiCodes = multiCodes.Contains(item.ProductCode);
                }
            }

            resp.Items = result;
            resp.Total = total;
            return resp;
        }

        /// <summary>
        /// 从国内商品导入到仓库商品
        /// 支持价格覆盖、套装商品同步、门店零售价同步、多码商品同步
        /// </summary>
        /// <param name="request">导入请求，包含商品编码列表和可选的价格覆盖</param>
        /// <returns>导入结果</returns>
        public async Task<ImportFromDomesticResponseDto> ImportFromDomesticAsync(
            ImportFromDomesticRequestDto request
        )
        {
            var response = new ImportFromDomesticResponseDto
            {
                Success = true,
                Message = "导入完成",
            };

            if (request.ProductCodes == null || !request.ProductCodes.Any())
            {
                response.Message = "请选择要导入的商品";
                response.Success = false;
                return response;
            }

            try
            {
                // 开启事务
                _context.Db.Ado.BeginTran();
                var now = DateTime.Now;
                var codes = request.ProductCodes.Distinct().ToList();

                // ===== 批量预加载数据（避免 N+1 问题）=====
                // 1. 批量查询国内商品
                var domesticProductsDict = (
                    await _context
                        .Db.Queryable<DomesticProduct>()
                        .Where(dp => codes.Contains(dp.ProductCode) && !dp.IsDeleted)
                        .ToListAsync()
                ).ToDictionary(dp => dp.ProductCode);

                // 2. 批量查询仓库商品
                var warehouseProductsDict = (
                    await _context
                        .Db.Queryable<WarehouseProduct>()
                        .Where(wp => codes.Contains(wp.ProductCode))
                        .ToListAsync()
                ).ToDictionary(wp => wp.ProductCode);

                // 3. 批量查询商品表
                var productsDict = (
                    await _context
                        .Db.Queryable<Product>()
                        .Where(p => codes.Contains(p.ProductCode))
                        .ToListAsync()
                ).ToDictionary(p => p.ProductCode);

                // 4. 批量查询套装商品关联数据
                var allSetProducts = await _context
                    .Db.Queryable<DomesticSetProduct>()
                    .Where(sp => codes.Contains(sp.ProductCode) && !sp.IsDeleted)
                    .ToListAsync();
                var setProductsByCode = allSetProducts
                    .GroupBy(sp => sp.ProductCode)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // 5. 批量查询已存在的套装编码
                var allSetCodeIds = allSetProducts
                    .Select(sp => sp.SetProductCode)
                    .Where(x => x != null)
                    .Distinct()
                    .ToList();
                var existingProductSetCodes = allSetCodeIds.Any()
                    ? (
                        await _context
                            .Db.Queryable<ProductSetCode>()
                            .Where(psc =>
                                codes.Contains(psc.ProductCode)
                                && allSetCodeIds.Contains(psc.SetCodeId)
                            )
                            .Select(psc => new { psc.ProductCode, psc.SetCodeId })
                            .ToListAsync()
                    )
                        .GroupBy(x => x.ProductCode)
                        .ToDictionary(g => g.Key, g => g.Select(x => x.SetCodeId).ToHashSet())
                    : new Dictionary<string, HashSet<string?>>();

                // 6. 批量查询活跃门店
                var activeStores = await _context
                    .Db.Queryable<Store>()
                    .Where(s => s.IsActive == true && s.IsDeleted == false)
                    .Select(s => s.StoreCode)
                    .ToListAsync();

                // 7. 批量查询已存在的多码商品
                var allSetBarcodes = allSetProducts
                    .Where(sp => sp.SetBarcode != null)
                    .Select(sp => sp.SetBarcode!)
                    .Distinct()
                    .ToList();
                var existingMultiCodeKeys = allSetBarcodes.Any() ? (
                        await _context
                            .Db.Queryable<StoreMultiCodeProduct>()
                            .Where(smc =>
                                codes.Contains(smc.ProductCode)
                                && !smc.IsDeleted
                                && allSetBarcodes.Contains(smc.MultiBarcode!)
                            )
                            .Select(smc => new
                            {
                                smc.ProductCode,
                                smc.MultiBarcode,
                                smc.StoreCode,
                            })
                            .ToListAsync()
                    ).GroupBy(x => x.ProductCode).ToDictionary(g => g.Key, g => g.Select(x => (x.MultiBarcode, x.StoreCode)).ToHashSet()) : new Dictionary<string, HashSet<(string?, string?)>>();

                // 8. 批量查询门店零售价
                var storeRetailPricesByCode = (
                    await _context
                        .Db.Queryable<StoreRetailPrice>()
                        .Where(srp => codes.Contains(srp.ProductCode) && !srp.IsDeleted)
                        .ToListAsync()
                )
                    .GroupBy(srp => srp.ProductCode)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // 待操作的列表（批量插入/更新）
                var toUpdateWarehouseProducts = new List<WarehouseProduct>();
                var toInsertWarehouseProducts = new List<WarehouseProduct>();
                var toInsertProducts = new List<Product>();
                var toUpdateDomesticProducts = new List<DomesticProduct>();
                var toInsertProductSetCodes = new List<ProductSetCode>();
                var toInsertStoreMultiCodeProducts = new List<StoreMultiCodeProduct>();
                var toInsertStoreRetailPrices = new List<StoreRetailPrice>();

                // ===== 遍历处理每个商品 =====
                foreach (var productCode in codes)
                {
                    var result = new ImportResultDetailDto { ProductCode = productCode };

                    // 检查国内商品是否存在
                    if (!domesticProductsDict.TryGetValue(productCode, out var domesticProduct))
                    {
                        result.Success = false;
                        result.Message = "商品不存在";
                        response.Results.Add(result);
                        response.FailedCount++;
                        continue;
                    }

                    // 补全图片 URL
                    var finalImageUrl = ProductImageUrlHelper.EnsureImageUrl(
                        domesticProduct.ProductImage,
                        domesticProduct.HBProductNo ?? domesticProduct.ProductCode
                    );

                    // 获取已存在的仓库商品
                    warehouseProductsDict.TryGetValue(productCode, out var existingWp);

                    // 获取价格覆盖（如果有）
                    ImportPriceOverrideDto? priceOverride = null;
                    if (
                        request.PriceOverrides != null
                        && request.PriceOverrides.TryGetValue(productCode, out var priceValue)
                    )
                    {
                        priceOverride = priceValue;
                    }

                    // 确定最终价格
                    var domesticPrice =
                        priceOverride?.DomesticPrice ?? domesticProduct.DomesticPrice;
                    var oemPrice = priceOverride?.OEMPrice ?? domesticProduct.OEMPrice;
                    var importPrice = priceOverride?.ImportPrice ?? domesticProduct.ImportPrice;

                    // 验证价格
                    if (
                        (domesticPrice ?? 0) <= 0
                        || (oemPrice ?? 0) <= 0
                        || (importPrice ?? 0) <= 0
                    )
                    {
                        result.Success = false;
                        result.Message = "国内价、贴牌价、进口价必须大于 0";
                        response.Results.Add(result);
                        response.FailedCount++;
                        continue;
                    }

                    var unitVolume = priceOverride?.Volume ?? domesticProduct.UnitVolume;
                    WarehouseProduct wp;

                    // 更新或创建仓库商品
                    if (existingWp != null)
                    {
                        existingWp.DomesticPrice = domesticPrice;
                        existingWp.OEMPrice = oemPrice;
                        existingWp.ImportPrice = importPrice;
                        existingWp.Volume = unitVolume;
                        existingWp.UpdatedAt = now;
                        toUpdateWarehouseProducts.Add(existingWp);
                        wp = existingWp;
                    }
                    else
                    {
                        wp = new WarehouseProduct
                        {
                            ProductCode = productCode,
                            DomesticPrice = domesticPrice,
                            OEMPrice = oemPrice,
                            ImportPrice = importPrice,
                            Volume = unitVolume,
                            StockQuantity = 0,
                            IsActive = true,
                            IsDeleted = false,
                            CreatedAt = now,
                            UpdatedAt = now,
                        };
                        toInsertWarehouseProducts.Add(wp);
                    }

                    // 同步更新国内商品表的价格与体积
                    domesticProduct.DomesticPrice = domesticPrice;
                    domesticProduct.OEMPrice = oemPrice;
                    domesticProduct.ImportPrice = importPrice;
                    domesticProduct.UnitVolume = unitVolume;
                    domesticProduct.ProductImage = finalImageUrl;
                    domesticProduct.UpdatedAt = now;
                    toUpdateDomesticProducts.Add(domesticProduct);

                    // 创建商品记录（如果不存在）
                    if (!productsDict.TryGetValue(productCode, out var existingProduct))
                    {
                        var product = new Product
                        {
                            ProductCode = productCode,
                            ItemNumber = domesticProduct.HBProductNo,
                            Barcode = domesticProduct.Barcode,
                            LocalSupplierCode = "200",
                            ProductType = domesticProduct.ProductType,
                            ProductName = domesticProduct.ProductName,
                            EnglishName = domesticProduct.EnglishProductName,
                            PurchasePrice = wp.ImportPrice,
                            RetailPrice = wp.OEMPrice,
                            ProductImage = ProductImageUrlHelper.EnsureImageUrl(
                                domesticProduct.ProductImage,
                                domesticProduct.HBProductNo ?? productCode
                            ),
                            IsAutoPricing = false,
                            IsActive = true,
                            IsDeleted = false,
                            CreatedAt = now,
                            UpdatedAt = now,
                        };
                        toInsertProducts.Add(product);
                    }

                    // 处理套装商品（使用内存查找，避免 N+1）
                    setProductsByCode.TryGetValue(productCode, out var setProducts);
                    setProducts ??= new List<DomesticSetProduct>();

                    var isSetProduct = domesticProduct.ProductType > 0;
                    if (isSetProduct && setProducts.Count > 0)
                    {
                        existingProductSetCodes.TryGetValue(productCode, out var existingSet);
                        existingSet ??= new HashSet<string?>();

                        foreach (var sp in setProducts)
                        {
                            if (existingSet.Contains(sp.SetProductCode))
                                continue;
                            existingSet.Add(sp.SetProductCode);
                            toInsertProductSetCodes.Add(
                                new ProductSetCode
                                {
                                    SetCodeId = sp.SetProductCode,
                                    ProductCode = productCode,
                                    SetProductCode = sp.SetProductCode,
                                    SetItemNumber = sp.SetProductNo,
                                    SetBarcode = sp.SetBarcode,
                                    SetPurchasePrice = sp.ImportPrice ?? wp.ImportPrice,
                                    SetRetailPrice = sp.OEMPrice ?? wp.OEMPrice,
                                    SetQuantity = 1,
                                    SetType = 1,
                                    IsActive = true,
                                    IsDeleted = false,
                                    CreatedAt = now,
                                    UpdatedAt = now,
                                }
                            );
                        }
                    }

                    // 同步多码商品到门店
                    if (request.SyncMultiCodes)
                    {
                        existingMultiCodeKeys.TryGetValue(productCode, out var existingKeys);
                        existingKeys ??= new HashSet<(string?, string?)>();

                        foreach (var sp in setProducts)
                        {
                            if (sp.SetBarcode == null)
                                continue;
                            foreach (var storeCode in activeStores)
                            {
                                if (existingKeys.Contains((sp.SetBarcode, storeCode)))
                                    continue;
                                existingKeys.Add((sp.SetBarcode, storeCode));
                                toInsertStoreMultiCodeProducts.Add(
                                    new StoreMultiCodeProduct
                                    {
                                        UUID = UuidHelper.GenerateUuid7(),
                                        ProductCode = productCode,
                                        StoreCode = storeCode,
                                        MultiCodeProductCode = sp.SetProductCode,
                                        StoreMultiCodeProductCode = storeCode + sp.SetProductCode,
                                        MultiBarcode = sp.SetBarcode,
                                        PurchasePrice = sp.ImportPrice,
                                        MultiCodeRetailPrice = sp.OEMPrice,
                                        DiscountRate = 0,
                                        IsAutoPricing = false,
                                        IsSpecialProduct = false,
                                        IsActive = true,
                                        IsDeleted = false,
                                        CreatedAt = now,
                                        UpdatedAt = now,
                                    }
                                );
                            }
                        }
                    }

                    // 同步门店零售价
                    if (request.SyncStorePrices)
                    {
                        if (!storeRetailPricesByCode.ContainsKey(productCode))
                        {
                            foreach (var storeCode in activeStores)
                            {
                                toInsertStoreRetailPrices.Add(
                                    new StoreRetailPrice
                                    {
                                        ProductCode = productCode,
                                        StoreCode = storeCode,
                                        StoreProductCode = storeCode + productCode,
                                        SupplierCode = "200",
                                        PurchasePrice = wp.ImportPrice,
                                        StoreRetailPriceValue = wp.OEMPrice,
                                        DiscountRate = 0,
                                        IsAutoPricing = false,
                                        IsSpecialProduct = false,
                                        IsActive = true,
                                        IsDeleted = false,
                                        CreatedAt = now,
                                        UpdatedAt = now,
                                    }
                                );
                            }
                        }
                    }

                    result.Success = true;
                    result.Message = "导入成功";
                    response.Results.Add(result);
                    response.SuccessCount++;
                }

                // ===== 批量执行数据库操作 =====
                if (toUpdateWarehouseProducts.Any())
                {
                    await _context
                        .Db.Updateable(toUpdateWarehouseProducts)
                        .UpdateColumns(wp => new
                        {
                            wp.DomesticPrice,
                            wp.OEMPrice,
                            wp.ImportPrice,
                            wp.Volume,
                            wp.UpdatedAt,
                        })
                        .ExecuteCommandAsync();
                }
                if (toInsertWarehouseProducts.Any())
                {
                    await _context.Db.Insertable(toInsertWarehouseProducts).ExecuteCommandAsync();
                }
                if (toUpdateDomesticProducts.Any())
                {
                    await _context
                        .Db.Updateable(toUpdateDomesticProducts)
                        .UpdateColumns(dp => new
                        {
                            dp.DomesticPrice,
                            dp.OEMPrice,
                            dp.ImportPrice,
                            dp.UnitVolume,
                            dp.ProductImage,
                            dp.UpdatedAt,
                        })
                        .ExecuteCommandAsync();
                }
                if (toInsertProducts.Any())
                {
                    await _context.Db.Insertable(toInsertProducts).ExecuteCommandAsync();
                }
                if (toInsertProductSetCodes.Any())
                {
                    await _context.Db.Insertable(toInsertProductSetCodes).ExecuteCommandAsync();
                }
                if (toInsertStoreMultiCodeProducts.Any())
                {
                    await _context
                        .Db.Insertable(toInsertStoreMultiCodeProducts)
                        .ExecuteCommandAsync();
                }
                if (toInsertStoreRetailPrices.Any())
                {
                    await _context.Db.Insertable(toInsertStoreRetailPrices).ExecuteCommandAsync();
                }

                _context.Db.Ado.CommitTran();

                if (response.SuccessCount == 0 && response.FailedCount > 0)
                {
                    response.Success = false;
                    var firstFailed = response.Results.FirstOrDefault(r => !r.Success);
                    response.Message =
                        firstFailed != null ? $"导入失败：{firstFailed.Message}" : "导入失败";
                }
            }
            catch (Exception ex)
            {
                _context.Db.Ado.RollbackTran();
                _logger.LogError(ex, "从国内商品导入失败");
                response.Success = false;
                response.Message = "导入失败: " + ex.Message;
            }

            return response;
        }

        /// <summary>
        /// 仓库商品完整更新：同一 db 顺序查、一次性取列表，事务内更新 DomesticProduct、Product、WarehouseProduct、StoreRetailPrice、StoreMultiCodeProduct、ProductSetCode。
        /// 分店零售价强联动：StoreRetailPriceValue / MultiCodeRetailPrice 用主表零售价（OEM）覆盖，PurchasePrice 用进口价覆盖。
        /// </summary>
        public async Task<WarehouseProductFullUpdateResultDto> FullUpdateAsync(
            string productCode,
            WarehouseProductFullUpdateDto dto
        )
        {
            var result = new WarehouseProductFullUpdateResultDto
            {
                Success = false,
                Message = "更新失败",
            };
            if (string.IsNullOrWhiteSpace(productCode) || dto == null)
            {
                result.Message = "商品编码或请求体为空";
                return result;
            }

            try
            {
                _context.Db.Ado.BeginTran();

                // 1. 顺序查询，一次性取列表（同一 db，不并行）
                var domesticProduct = await _context
                    .Db.Queryable<DomesticProduct>()
                    .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                    .FirstAsync();
                var product = await _context
                    .Db.Queryable<Product>()
                    .Where(p => p.ProductCode == productCode)
                    .FirstAsync();
                if (product == null)
                {
                    _context.Db.Ado.RollbackTran();
                    result.Message = "商品不存在（Product 表无此 ProductCode）";
                    return result;
                }
                var warehouseProduct = await _context
                    .Db.Queryable<WarehouseProduct>()
                    .Where(w => w.ProductCode == productCode)
                    .FirstAsync();
                if (warehouseProduct == null)
                {
                    _context.Db.Ado.RollbackTran();
                    result.Message = "仓库商品不存在";
                    return result;
                }
                var storeRetailPrices = await _context
                    .Db.Queryable<StoreRetailPrice>()
                    .Where(srp => srp.ProductCode == productCode && !srp.IsDeleted)
                    .ToListAsync();
                var storeMultiCodeProducts = await _context
                    .Db.Queryable<StoreMultiCodeProduct>()
                    .Where(mcp => mcp.ProductCode == productCode && !mcp.IsDeleted)
                    .ToListAsync();
                var productSetCodes = await _context
                    .Db.Queryable<ProductSetCode>()
                    .Where(psc => psc.ProductCode == productCode && !psc.IsDeleted)
                    .ToListAsync();

                var now = DateTime.Now;

                // 2. 更新 DomesticProduct（若存在）
                if (domesticProduct != null)
                {
                    if (dto.ProductName != null)
                        domesticProduct.ProductName = dto.ProductName;
                    if (dto.EnglishName != null)
                        domesticProduct.EnglishProductName = dto.EnglishName;
                    if (dto.ProductSpecification != null)
                        domesticProduct.ProductSpecification = dto.ProductSpecification;
                    domesticProduct.ProductType = dto.ProductType;
                    if (dto.DomesticPrice.HasValue)
                        domesticProduct.DomesticPrice = dto.DomesticPrice;
                    if (dto.OEMPrice.HasValue)
                        domesticProduct.OEMPrice = dto.OEMPrice;
                    if (dto.ImportPrice.HasValue)
                        domesticProduct.ImportPrice = dto.ImportPrice;
                    if (dto.PackingQuantity.HasValue)
                        domesticProduct.PackingQuantity = dto.PackingQuantity;
                    if (dto.UnitVolume.HasValue)
                        domesticProduct.UnitVolume = dto.UnitVolume;
                    if (dto.MiddlePackQuantity.HasValue)
                        domesticProduct.MiddlePackQuantity = dto.MiddlePackQuantity;
                    if (dto.ProductImage != null)
                    {
                        domesticProduct.ProductImage = ProductImageUrlHelper.EnsureImageUrl(
                            dto.ProductImage,
                            domesticProduct.HBProductNo ?? domesticProduct.ProductCode
                        );
                    }
                    domesticProduct.IsActive = dto.IsActive;
                    if (dto.SupplierCode != null)
                        domesticProduct.SupplierCode = dto.SupplierCode;
                    domesticProduct.UpdatedAt = now;
                    domesticProduct.UpdatedBy = "System";
                    await _context.Db.Updateable(domesticProduct).ExecuteCommandAsync();
                }

                // 3. 更新 Product
                if (dto.ProductName != null)
                    product.ProductName = dto.ProductName;
                if (dto.EnglishName != null)
                    product.EnglishName = dto.EnglishName;
                if (dto.ImportPrice.HasValue)
                    product.PurchasePrice = dto.ImportPrice;
                if (dto.OEMPrice.HasValue)
                    product.RetailPrice = dto.OEMPrice;
                if (dto.WarehouseCategoryGUID != null)
                    product.WarehouseCategoryGUID = dto.WarehouseCategoryGUID;
                product.ProductType = dto.ProductType;
                product.IsAutoPricing = dto.IsAutoPricing;
                if (dto.MiddlePackQuantity.HasValue)
                    product.MiddlePackageQuantity = dto.MiddlePackQuantity;
                if (dto.LocalSupplierCode != null)
                    product.LocalSupplierCode = dto.LocalSupplierCode;
                if (dto.ProductImage != null)
                {
                    product.ProductImage = ProductImageUrlHelper.EnsureImageUrl(
                        dto.ProductImage,
                        product.ItemNumber ?? product.ProductCode
                    );
                }
                product.IsActive = dto.IsActive;
                product.UpdatedAt = now;
                await _context
                    .Db.Updateable(product)
                    .UpdateColumns(p => new
                    {
                        p.ProductName,
                        p.EnglishName,
                        p.PurchasePrice,
                        p.RetailPrice,
                        p.WarehouseCategoryGUID,
                        p.ProductType,
                        p.IsAutoPricing,
                        p.MiddlePackageQuantity,
                        p.LocalSupplierCode,
                        p.ProductImage,
                        p.IsActive,
                        p.UpdatedAt,
                    })
                    .ExecuteCommandAsync();

                // 4. 更新 WarehouseProduct
                if (dto.DomesticPrice.HasValue)
                    warehouseProduct.DomesticPrice = dto.DomesticPrice;
                if (dto.OEMPrice.HasValue)
                    warehouseProduct.OEMPrice = dto.OEMPrice;
                if (dto.ImportPrice.HasValue)
                    warehouseProduct.ImportPrice = dto.ImportPrice;
                if (dto.UnitVolume.HasValue)
                    warehouseProduct.Volume = dto.UnitVolume;
                if (dto.MinOrderQuantity.HasValue)
                    warehouseProduct.MinOrderQuantity = dto.MinOrderQuantity;
                warehouseProduct.IsActive = dto.IsActive;
                warehouseProduct.UpdatedAt = now;
                await _context
                    .Db.Updateable(warehouseProduct)
                    .UpdateColumns(w => new
                    {
                        w.DomesticPrice,
                        w.OEMPrice,
                        w.ImportPrice,
                        w.Volume,
                        w.MinOrderQuantity,
                        w.IsActive,
                        w.UpdatedAt,
                    })
                    .ExecuteCommandAsync();

                // 5. 强联动：批量更新 StoreRetailPrice（主表零售价/进货价覆盖）
                var mainRetail = dto.OEMPrice ?? product.RetailPrice;
                var mainPurchase = dto.ImportPrice ?? product.PurchasePrice;
                foreach (var srp in storeRetailPrices)
                {
                    srp.StoreRetailPriceValue = mainRetail;
                    srp.PurchasePrice = mainPurchase;
                    srp.IsActive = dto.IsActive;
                    srp.UpdatedAt = now;
                }
                if (storeRetailPrices.Any())
                {
                    await _context
                        .Db.Updateable(storeRetailPrices)
                        .UpdateColumns(srp => new
                        {
                            srp.StoreRetailPriceValue,
                            srp.PurchasePrice,
                            srp.IsActive,
                            srp.UpdatedAt,
                        })
                        .ExecuteCommandAsync();
                }

                // 6. 强联动：批量更新 StoreMultiCodeProduct
                foreach (var mcp in storeMultiCodeProducts)
                {
                    mcp.MultiCodeRetailPrice = mainRetail;
                    mcp.PurchasePrice = mainPurchase;
                    mcp.IsActive = dto.IsActive;
                    mcp.UpdatedAt = now;
                }
                if (storeMultiCodeProducts.Any())
                {
                    await _context
                        .Db.Updateable(storeMultiCodeProducts)
                        .UpdateColumns(mcp => new
                        {
                            mcp.MultiCodeRetailPrice,
                            mcp.PurchasePrice,
                            mcp.IsActive,
                            mcp.UpdatedAt,
                        })
                        .ExecuteCommandAsync();
                }

                // 7. 条码价明细：按条码/SetCodeId/MultiCodeUuid 更新 ProductSetCode 与 StoreMultiCodeProduct
                if (dto.BarcodePrices != null && dto.BarcodePrices.Any())
                {
                    foreach (var item in dto.BarcodePrices)
                    {
                        if (!string.IsNullOrWhiteSpace(item.SetCodeId))
                        {
                            var setCode = productSetCodes.FirstOrDefault(psc =>
                                psc.SetCodeId == item.SetCodeId
                            );
                            if (setCode != null)
                            {
                                if (item.RetailPrice.HasValue)
                                    setCode.SetRetailPrice = item.RetailPrice;
                                if (item.PurchasePrice.HasValue)
                                    setCode.SetPurchasePrice = item.PurchasePrice;
                                setCode.UpdatedAt = now;
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(item.MultiCodeUuid))
                        {
                            var mcp = storeMultiCodeProducts.FirstOrDefault(x =>
                                x.UUID == item.MultiCodeUuid
                            );
                            if (mcp != null)
                            {
                                if (item.RetailPrice.HasValue)
                                    mcp.MultiCodeRetailPrice = item.RetailPrice;
                                if (item.PurchasePrice.HasValue)
                                    mcp.PurchasePrice = item.PurchasePrice;
                                mcp.UpdatedAt = now;
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(item.Barcode))
                        {
                            var setCode = productSetCodes.FirstOrDefault(psc =>
                                psc.SetBarcode == item.Barcode
                            );
                            if (setCode != null)
                            {
                                if (item.RetailPrice.HasValue)
                                    setCode.SetRetailPrice = item.RetailPrice;
                                if (item.PurchasePrice.HasValue)
                                    setCode.SetPurchasePrice = item.PurchasePrice;
                                setCode.UpdatedAt = now;
                            }
                            var mcp = storeMultiCodeProducts.FirstOrDefault(x =>
                                x.MultiBarcode == item.Barcode
                            );
                            if (mcp != null)
                            {
                                if (item.RetailPrice.HasValue)
                                    mcp.MultiCodeRetailPrice = item.RetailPrice;
                                if (item.PurchasePrice.HasValue)
                                    mcp.PurchasePrice = item.PurchasePrice;
                                mcp.UpdatedAt = now;
                            }
                        }
                    }
                    if (productSetCodes.Any())
                    {
                        await _context
                            .Db.Updateable(productSetCodes)
                            .UpdateColumns(psc => new
                            {
                                psc.SetRetailPrice,
                                psc.SetPurchasePrice,
                                psc.UpdatedAt,
                            })
                            .ExecuteCommandAsync();
                    }
                    if (storeMultiCodeProducts.Any())
                    {
                        await _context
                            .Db.Updateable(storeMultiCodeProducts)
                            .UpdateColumns(mcp => new
                            {
                                mcp.MultiCodeRetailPrice,
                                mcp.PurchasePrice,
                                mcp.UpdatedAt,
                            })
                            .ExecuteCommandAsync();
                    }
                }

                _context.Db.Ado.CommitTran();
                result.Success = true;
                result.Message = "更新成功";
            }
            catch (Exception ex)
            {
                _context.Db.Ado.RollbackTran();
                _logger.LogError(ex, "仓库商品完整更新失败 ProductCode={ProductCode}", productCode);
                result.Message = "更新失败: " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 获取商品条码对应套装价/进货价列表（来自 ProductSetCode + StoreMultiCodeProduct）
        /// </summary>
        public async Task<List<BarcodePriceItemDto>> GetBarcodePricesAsync(string productCode)
        {
            if (string.IsNullOrWhiteSpace(productCode))
                return new List<BarcodePriceItemDto>();

            var setCodes = await _context
                .Db.Queryable<ProductSetCode>()
                .Where(psc => psc.ProductCode == productCode && !psc.IsDeleted)
                .Select(psc => new BarcodePriceItemDto
                {
                    Barcode = psc.SetBarcode ?? "",
                    RetailPrice = psc.SetRetailPrice,
                    PurchasePrice = psc.SetPurchasePrice,
                    SetCodeId = psc.SetCodeId,
                })
                .ToListAsync();
            var multiCodes = await _context
                .Db.Queryable<StoreMultiCodeProduct>()
                .Where(mcp => mcp.ProductCode == productCode && !mcp.IsDeleted)
                .Select(mcp => new BarcodePriceItemDto
                {
                    Barcode = mcp.MultiBarcode ?? "",
                    RetailPrice = mcp.MultiCodeRetailPrice,
                    PurchasePrice = mcp.PurchasePrice,
                    MultiCodeUuid = mcp.UUID,
                })
                .ToListAsync();
            var list = new List<BarcodePriceItemDto>();
            list.AddRange(setCodes.Where(x => !string.IsNullOrWhiteSpace(x.Barcode)));
            foreach (var m in multiCodes)
            {
                if (string.IsNullOrWhiteSpace(m.Barcode))
                    continue;
                if (
                    list.Any(x =>
                        x.Barcode == m.Barcode && !string.IsNullOrWhiteSpace(x.MultiCodeUuid)
                    )
                )
                    continue;
                list.Add(m);
            }
            return list;
        }

        public async Task<
            ReactTableResponseDto<NonHotbargainProductNotInWarehouseDto>
        > GetNonHotbargainProductsNotInWarehouseAsync(
            GetNonHotbargainProductsNotInWarehouseRequestDto request
        )
        {
            var resp = new ReactTableResponseDto<NonHotbargainProductNotInWarehouseDto>();
            var query = _context
                .Db.Queryable<Product>()
                .LeftJoin<HBLocalSupplier>((p, s) => p.LocalSupplierCode == s.LocalSupplierCode)
                .Where(
                    (p, s) =>
                        p.LocalSupplierCode != "200"
                        && !p.IsDeleted
                        && p.IsActive
                        && p.ProductCode != null
                )
                .Where(
                    (p, s) =>
                        !SqlFunc
                            .Subqueryable<WarehouseProduct>()
                            .Where(wp => wp.ProductCode == p.ProductCode && !wp.IsDeleted)
                            .Any()
                );

            if (!string.IsNullOrWhiteSpace(request.GlobalSearch))
            {
                var keyword = request.GlobalSearch.Trim().ToLower();
                query = query.Where(
                    (p, s) =>
                        (p.ProductName != null && p.ProductName.ToLower().Contains(keyword))
                        || (p.EnglishName != null && p.EnglishName.ToLower().Contains(keyword))
                        || (p.ItemNumber != null && p.ItemNumber.ToLower().Contains(keyword))
                        || (p.Barcode != null && p.Barcode.ToLower().Contains(keyword))
                        || (
                            p.LocalSupplierCode != null
                            && p.LocalSupplierCode.ToLower().Contains(keyword)
                        )
                        || (s.Name != null && s.Name.ToLower().Contains(keyword))
                );
            }

            if (request.Filters != null && request.Filters.Any())
            {
                foreach (var kv in request.Filters)
                {
                    var key = kv.Key?.ToLower();
                    var values =
                        kv.Value?.Where(v => !string.IsNullOrWhiteSpace(v)).ToList()
                        ?? new List<string>();
                    if (!values.Any())
                        continue;

                    switch (key)
                    {
                        case "itemnumber":
                            {
                                var lowers = values.Select(v => v.ToLower()).ToList();
                                query = query.Where(
                                    (p, s) =>
                                        p.ItemNumber != null
                                        && lowers.Any(v => p.ItemNumber.ToLower().Contains(v))
                                );
                            }
                            break;
                        case "localsuppliercode":
                        case "suppliercode":
                            {
                                var lowers = values.Select(v => v.ToLower()).ToList();
                                query = query.Where(
                                    (p, s) =>
                                        p.LocalSupplierCode != null
                                        && lowers.Any(v =>
                                            p.LocalSupplierCode.ToLower().Contains(v)
                                        )
                                );
                            }
                            break;
                        case "localsuppliername":
                            {
                                var lowers = values.Select(v => v.ToLower()).ToList();
                                query = query.Where(
                                    (p, s) =>
                                        s.Name != null
                                        && lowers.Any(v => s.Name.ToLower().Contains(v))
                                );
                            }
                            break;
                    }
                }
            }

            var total = await query.Clone().CountAsync();
            var list = await query
                .OrderByDescending((p, s) => p.UpdatedAt)
                .Select(
                    (p, s) =>
                        new NonHotbargainProductNotInWarehouseDto
                        {
                            ProductCode = p.ProductCode!,
                            ItemNumber = p.ItemNumber ?? "",
                            Barcode = p.Barcode,
                            ProductName = p.ProductName,
                            EnglishName = p.EnglishName,
                            ProductType = (ProductTypeEnum)(p.ProductType ?? 0),
                            PurchasePrice = p.PurchasePrice,
                            RetailPrice = p.RetailPrice,
                            LocalSupplierCode = p.LocalSupplierCode,
                            LocalSupplierName = s.Name,
                            ProductImage = p.ProductImage,
                        }
                )
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToListAsync();

            // 补全图片 URL
            foreach (var item in list)
            {
                item.ProductImage = ProductImageUrlHelper.EnsureImageUrl(
                    item.ProductImage,
                    item.ItemNumber
                );
            }

            resp.Items = list;
            resp.Total = total;
            return resp;
        }

        /// <summary>
        /// 从非 Hotbargain 商品导入到仓库商品
        /// 将已有商品（Product 表）导入到仓库商品表（WarehouseProduct）
        /// </summary>
        /// <param name="request">导入请求，包含商品编码列表</param>
        /// <returns>导入结果</returns>
        public async Task<ImportFromDomesticResponseDto> ImportNonHotbargainProductsAsync(
            ImportNonHotbargainRequestDto request
        )
        {
            var response = new ImportFromDomesticResponseDto
            {
                Success = true,
                Message = "导入完成",
            };

            if (request.ProductCodes == null || !request.ProductCodes.Any())
            {
                response.Message = "请选择要导入的商品";
                response.Success = false;
                return response;
            }

            try
            {
                // 开启事务
                _context.Db.Ado.BeginTran();
                var now = DateTime.Now;
                var codes = request.ProductCodes.Distinct().ToList();

                // 批量查询商品表（避免 N+1 问题）
                var productsDict = (
                    await _context
                        .Db.Queryable<Product>()
                        .Where(p => codes.Contains(p.ProductCode))
                        .ToListAsync()
                ).ToDictionary(p => p.ProductCode);

                // 批量查询已存在的仓库商品编码（避免 N+1 问题）
                var existingWpCodes = (
                    await _context
                        .Db.Queryable<WarehouseProduct>()
                        .Where(wp => codes.Contains(wp.ProductCode))
                        .Select(wp => wp.ProductCode)
                        .ToListAsync()
                ).ToHashSet();

                // 待插入的仓库商品列表
                var toInsertWarehouseProducts = new List<WarehouseProduct>();

                // 遍历处理每个商品
                foreach (var productCode in codes)
                {
                    var result = new ImportResultDetailDto { ProductCode = productCode };

                    // 检查商品是否存在
                    if (!productsDict.TryGetValue(productCode, out var product))
                    {
                        result.Success = false;
                        result.Message = "商品不存在";
                        response.Results.Add(result);
                        response.FailedCount++;
                        continue;
                    }

                    // 检查是否已存在于仓库
                    if (existingWpCodes.Contains(productCode))
                    {
                        result.Success = false;
                        result.Message = "商品已存在于仓库中";
                        response.Results.Add(result);
                        response.FailedCount++;
                        continue;
                    }

                    // 创建仓库商品记录
                    var wp = new WarehouseProduct
                    {
                        ProductCode = productCode,
                        DomesticPrice = 0,
                        OEMPrice = 0,
                        ImportPrice = product.PurchasePrice ?? 0,
                        StockQuantity = 0,
                        IsActive = true,
                        IsDeleted = false,
                        CreatedAt = now,
                        UpdatedAt = now,
                    };
                    toInsertWarehouseProducts.Add(wp);

                    result.Success = true;
                    result.Message = "导入成功";
                    response.Results.Add(result);
                    response.SuccessCount++;
                }

                // 批量插入仓库商品
                if (toInsertWarehouseProducts.Any())
                {
                    await _context.Db.Insertable(toInsertWarehouseProducts).ExecuteCommandAsync();
                }

                // 提交事务
                _context.Db.Ado.CommitTran();

                // 全部失败时整体视为失败
                if (response.SuccessCount == 0 && response.FailedCount > 0)
                {
                    response.Success = false;
                    response.Message = "所有商品导入失败";
                }
            }
            catch (Exception ex)
            {
                _context.Db.Ado.RollbackTran();
                _logger.LogError(ex, "导入非 Hotbargain 商品失败");
                response.Success = false;
                response.Message = "导入失败: " + ex.Message;
            }

            return response;
        }
    }
}
