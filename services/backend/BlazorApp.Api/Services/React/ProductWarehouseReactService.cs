using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Helper;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Services.React
{
    internal sealed record WarehouseProductTableTimingSnapshot(
        long CandidateMs,
        long CountMs,
        long PageMs,
        long LocationMs,
        long RowsMs,
        long MapMs,
        long TotalMs
    );

    internal sealed record WarehouseProductTableRequestSnapshot(
        int PageNumber,
        int PageSize,
        int CategoryCount,
        int FilterCount,
        string KeywordType,
        int KeywordLength,
        string SortBy,
        string SortOrder
    );

    internal sealed class WarehouseProductTableQueryException : Exception
    {
        public WarehouseProductTableQueryException(
            string failedStage,
            WarehouseProductTableTimingSnapshot timings,
            Exception innerException,
            WarehouseProductTableRequestSnapshot? request = null
        )
            : base($"仓库商品表格查询在 {failedStage} 阶段失败。", innerException)
        {
            FailedStage = failedStage;
            Timings = timings;
            Request = request;
        }

        public string FailedStage { get; }

        public WarehouseProductTableTimingSnapshot Timings { get; }

        public WarehouseProductTableRequestSnapshot? Request { get; }
    }

    internal sealed class WarehouseProductTableTimings
    {
        public long CandidateMs { get; set; }

        public long CountMs { get; set; }

        public long PageMs { get; set; }

        public long LocationMs { get; set; }

        public long RowsMs { get; set; }

        public long MapMs { get; set; }

        public WarehouseProductTableTimingSnapshot Snapshot(long totalMs) =>
            new(CandidateMs, CountMs, PageMs, LocationMs, RowsMs, MapMs, totalMs);
    }

    internal sealed class WarehouseProductCodeSearchCandidate
    {
        public string ProductCode { get; set; } = string.Empty;
    }

    /// <summary>
    /// 仓库商品服务 - React 前端专用
    /// 提供仓库商品的 CRUD 操作、批量导入、价格同步等功能
    /// </summary>
    public class ProductWarehouseReactService : IProductWarehouseReactService
    {
        private const int PickingLocationType = 1;
        private const string SystemUpdatedBy = "System";
        private const string MobileWarehousePricePatchUpdatedBy = "MobileWarehousePricePatch";

        private readonly SqlSugarContext _context;
        private readonly HqSqlSugarContext _hqContext;
        private readonly ILogger<ProductWarehouseReactService> _logger;
        private readonly IConfiguration _configuration;
        private readonly ItemBarcodeService _itemBarcodeService;
        private readonly IMapper _mapper;
        private readonly IDataSyncFullService _dataSyncFullService;
        private readonly IWarehouseProductChangeHistoryService _changeHistoryService;
        private readonly ITranslationService? _translationService;

        public ProductWarehouseReactService(
            SqlSugarContext context,
            HqSqlSugarContext hqContext,
            ILogger<ProductWarehouseReactService> logger,
            IConfiguration configuration,
            ItemBarcodeService itemBarcodeService,
            IMapper mapper,
            IDataSyncFullService dataSyncFullService,
            IWarehouseProductChangeHistoryService changeHistoryService,
            ITranslationService? translationService = null
        )
        {
            _context = context;
            _hqContext = hqContext;
            _logger = logger;
            _configuration = configuration;
            _itemBarcodeService = itemBarcodeService;
            _mapper = mapper;
            _dataSyncFullService = dataSyncFullService;
            _changeHistoryService = changeHistoryService;
            _translationService = translationService;
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
            var supplierCodes = items
                .Select(i => i.SupplierCode)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();
            var barcodes = items
                .Select(i => i.Barcode)
                .Where(b => !string.IsNullOrWhiteSpace(b))
                .Distinct()
                .ToList();

            if (!productCodes.Any() && !itemNumbers.Any() && !barcodes.Any())
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

            var wpList = new List<DetectionWarehouseSnapshot>();
            if (productCodes.Any())
            {
                wpList.AddRange(
                    await SelectWarehouseSnapshotsAsync(
                        BuildWarehouseDetectionQuery()
                            .Where((w, p) => productCodes.Contains(w.ProductCode))
                    )
                );
            }
            if (itemNumbers.Any())
            {
                var scopedItemNumbers = items
                    .Where(i => !string.IsNullOrWhiteSpace(i.ItemNumber) && !string.IsNullOrWhiteSpace(i.SupplierCode))
                    .Select(i => i.ItemNumber!)
                    .Distinct()
                    .ToList();
                var unscopedItemNumbers = items
                    .Where(i => !string.IsNullOrWhiteSpace(i.ItemNumber) && string.IsNullOrWhiteSpace(i.SupplierCode))
                    .Select(i => i.ItemNumber!)
                    .Distinct()
                    .ToList();
                if (scopedItemNumbers.Any() && supplierCodes.Any())
                {
                    // 前端传供应商时，货号候选必须限定在该本地供应商下，避免同货号跨供应商误匹配。
                    wpList.AddRange(
                        await SelectWarehouseSnapshotsAsync(
                            BuildWarehouseDetectionQuery()
                                .Where(
                                    (w, p) =>
                                        p.ItemNumber != null
                                        && scopedItemNumbers.Contains(p.ItemNumber)
                                        && p.LocalSupplierCode != null
                                        && supplierCodes.Contains(p.LocalSupplierCode)
                                )
                        )
                    );
                }
                if (unscopedItemNumbers.Any())
                {
                    wpList.AddRange(
                        await SelectWarehouseSnapshotsAsync(
                            BuildWarehouseDetectionQuery()
                                .Where(
                                    (w, p) =>
                                        p.ItemNumber != null && unscopedItemNumbers.Contains(p.ItemNumber)
                                )
                        )
                    );
                }
            }
            if (barcodes.Any())
            {
                wpList.AddRange(
                    await SelectWarehouseSnapshotsAsync(
                        BuildWarehouseDetectionQuery()
                            .Where((w, p) => p.Barcode != null && barcodes.Contains(p.Barcode))
                    )
                );
            }
            wpList = wpList
                .GroupBy(x => x.ProductCode ?? $"{x.ItemNumber}|{x.Barcode}")
                .Select(g => g.First())
                .ToList();

            var domesticList = new List<DetectionDomesticSnapshot>();
            if (productCodes.Any())
            {
                domesticList.AddRange(
                    await SelectDomesticSnapshotsAsync(
                        _context
                            .Db.Queryable<DomesticProduct>()
                            .Where(dp => !dp.IsDeleted && productCodes.Contains(dp.ProductCode))
                    )
                );
            }
            if (itemNumbers.Any())
            {
                var scopedItemNumbers = items
                    .Where(i => !string.IsNullOrWhiteSpace(i.ItemNumber) && !string.IsNullOrWhiteSpace(i.SupplierCode))
                    .Select(i => i.ItemNumber!)
                    .Distinct()
                    .ToList();
                var unscopedItemNumbers = items
                    .Where(i => !string.IsNullOrWhiteSpace(i.ItemNumber) && string.IsNullOrWhiteSpace(i.SupplierCode))
                    .Select(i => i.ItemNumber!)
                    .Distinct()
                    .ToList();
                if (scopedItemNumbers.Any() && supplierCodes.Any())
                {
                    // 国内商品货号候选同样必须限定供应商，避免同货号不同供应商串到错误国内编码。
                    domesticList.AddRange(
                        await SelectDomesticSnapshotsAsync(
                            _context
                                .Db.Queryable<DomesticProduct>()
                                .Where(
                                    dp =>
                                        !dp.IsDeleted
                                        && dp.HBProductNo != null
                                        && scopedItemNumbers.Contains(dp.HBProductNo)
                                        && dp.SupplierCode != null
                                        && supplierCodes.Contains(dp.SupplierCode)
                                )
                        )
                    );
                }
                if (unscopedItemNumbers.Any())
                {
                    domesticList.AddRange(
                        await SelectDomesticSnapshotsAsync(
                            _context
                                .Db.Queryable<DomesticProduct>()
                                .Where(
                                    dp =>
                                        !dp.IsDeleted
                                        && dp.HBProductNo != null
                                        && unscopedItemNumbers.Contains(dp.HBProductNo)
                                )
                        )
                    );
                }
            }
            if (barcodes.Any())
            {
                domesticList.AddRange(
                    await SelectDomesticSnapshotsAsync(
                        _context
                            .Db.Queryable<DomesticProduct>()
                            .Where(
                                dp =>
                                    !dp.IsDeleted
                                    && dp.Barcode != null
                                    && barcodes.Contains(dp.Barcode)
                            )
                    )
                );
            }
            domesticList = domesticList
                .GroupBy(x => x.ProductCode ?? $"{x.ItemNumber}|{x.Barcode}")
                .Select(g => g.First())
                .ToList();

            var byCode = wpList
                .Where(x => !string.IsNullOrWhiteSpace(x.ProductCode))
                .GroupBy(x => x.ProductCode!)
                .ToDictionary(g => g.Key, g => g.First());
            var byItem = wpList
                .Where(x => !string.IsNullOrWhiteSpace(x.ItemNumber))
                .GroupBy(x => x.ItemNumber!)
                .ToDictionary(g => g.Key, g => g.First());
            var bySupplierItem = wpList
                .Select(x => new { Key = BuildSupplierItemMatchKey(x.SupplierCode, x.ItemNumber), Item = x })
                .Where(x => x.Key != null)
                .GroupBy(x => x.Key!)
                .ToDictionary(g => g.Key, g => g.First().Item);
            var byBarcode = wpList
                .Where(x => !string.IsNullOrWhiteSpace(x.Barcode))
                .GroupBy(x => x.Barcode!)
                .ToDictionary(g => g.Key, g => g.First());

            var domesticByCode = domesticList
                .Where(x => !string.IsNullOrWhiteSpace(x.ProductCode))
                .GroupBy(x => x.ProductCode!)
                .ToDictionary(g => g.Key, g => g.First());
            var domesticByItem = domesticList
                .Where(x => !string.IsNullOrWhiteSpace(x.ItemNumber))
                .GroupBy(x => x.ItemNumber!)
                .ToDictionary(g => g.Key, g => g.First());
            var domesticBySupplierItem = domesticList
                .Select(x => new { Key = BuildSupplierItemMatchKey(x.SupplierCode, x.ItemNumber), Item = x })
                .Where(x => x.Key != null)
                .GroupBy(x => x.Key!)
                .ToDictionary(g => g.Key, g => g.First().Item);
            var domesticByBarcode = domesticList
                .Where(x => !string.IsNullOrWhiteSpace(x.Barcode))
                .GroupBy(x => x.Barcode!)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var item in items)
            {
                DetectionWarehouseSnapshot? codeMatch =
                    (
                        !string.IsNullOrWhiteSpace(item.ProductCode)
                        && byCode.TryGetValue(item.ProductCode!, out var wpByCode)
                    )
                        ? wpByCode
                        : null;
                var itemMatch =
                    (
                        !string.IsNullOrWhiteSpace(item.ItemNumber)
                        && !string.IsNullOrWhiteSpace(item.SupplierCode)
                        && bySupplierItem.TryGetValue(
                            BuildSupplierItemMatchKey(item.SupplierCode, item.ItemNumber)!,
                            out var wpBySupplierItem
                        )
                    )
                        ? wpBySupplierItem
                        : (
                            string.IsNullOrWhiteSpace(item.SupplierCode)
                            && !string.IsNullOrWhiteSpace(item.ItemNumber)
                            && byItem.TryGetValue(item.ItemNumber!, out var wpByItem)
                        )
                            ? wpByItem
                            : null;
                var barcodeMatch =
                    (
                        !string.IsNullOrWhiteSpace(item.Barcode)
                        && byBarcode.TryGetValue(item.Barcode!, out var wpByBarcode)
                    )
                        ? wpByBarcode
                        : null;

                var exists = codeMatch != null || itemMatch != null || barcodeMatch != null;
                var matchType = "none";
                if (codeMatch != null && itemMatch != null)
                    matchType = "both";
                else if (codeMatch != null)
                    matchType = "product_code";
                else if (itemMatch != null)
                    matchType = "item_number";
                else if (barcodeMatch != null)
                    matchType = "barcode";

                var warehouseSource = codeMatch ?? itemMatch ?? barcodeMatch;
                var domesticSource = FindDomesticMatch(
                    item,
                    domesticByCode,
                    domesticBySupplierItem,
                    domesticByItem,
                    domesticByBarcode
                );
                var localProductCode = warehouseSource?.ProductCode;
                var domesticProductCode = domesticSource?.ProductCode ?? item.ProductCode;
                var hasProductCodeConflict =
                    !string.IsNullOrWhiteSpace(localProductCode)
                    && !string.IsNullOrWhiteSpace(domesticProductCode)
                    && !string.Equals(
                        localProductCode.Trim(),
                        domesticProductCode.Trim(),
                        StringComparison.OrdinalIgnoreCase
                    );
                var effectiveOemPrice = HasPositivePrice(warehouseSource?.OEMPrice)
                    ? warehouseSource?.OEMPrice
                    : domesticSource?.OEMPrice;
                results.Add(
                    new DetectionResultDto
                    {
                        ProductCode = item.ProductCode ?? warehouseSource?.ProductCode ?? domesticSource?.ProductCode,
                        ItemNumber = item.ItemNumber ?? warehouseSource?.ItemNumber ?? domesticSource?.ItemNumber,
                        SupplierCode = item.SupplierCode ?? warehouseSource?.SupplierCode,
                        Exists = exists,
                        MatchType = matchType,
                        LocalProductCode = localProductCode,
                        DomesticProductCode = domesticProductCode,
                        HasProductCodeConflict = hasProductCodeConflict,
                        // 国内编码和本地主档编码不一致时，只能作为候选，必须由货柜页人工确认后再对齐。
                        ConflictReason = hasProductCodeConflict
                            ? "国内商品编码与本地主档商品编码不一致"
                            : null,
                        ProductName = domesticSource?.ProductName ?? warehouseSource?.ProductName,
                        EnglishName = domesticSource?.EnglishName ?? warehouseSource?.EnglishName,
                        WarehouseDomesticPrice = warehouseSource?.DomesticPrice,
                        WarehouseOEMPrice = effectiveOemPrice,
                        WarehouseImportPrice = warehouseSource?.ImportPrice,
                        WarehouseVolume = domesticSource?.UnitVolume ?? warehouseSource?.Volume,
                        PackingQuantity = domesticSource?.PackingQuantity ?? warehouseSource?.PackingQuantity,
                        DomesticPrice = domesticSource?.DomesticPrice,
                        DomesticOEMPrice = domesticSource?.OEMPrice,
                        DomesticImportPrice = domesticSource?.ImportPrice,
                        WarehouseIsActive = warehouseSource?.IsActive,
                    }
                );
            }

            return results;
        }

        private ISugarQueryable<WarehouseProduct, Product> BuildWarehouseDetectionQuery()
        {
            return _context
                .Db.Queryable<WarehouseProduct>()
                .LeftJoin<Product>((w, p) => w.ProductCode == p.ProductCode);
        }

        private static Task<List<DetectionWarehouseSnapshot>> SelectWarehouseSnapshotsAsync(
            ISugarQueryable<WarehouseProduct, Product> query
        )
        {
            return query
                .Select(
                    (w, p) =>
                        new DetectionWarehouseSnapshot
                        {
                            ProductCode = w.ProductCode,
                            ItemNumber = p.ItemNumber,
                            SupplierCode = p.LocalSupplierCode,
                            Barcode = p.Barcode,
                            ProductName = p.ProductName,
                            EnglishName = p.EnglishName,
                            DomesticPrice = w.DomesticPrice,
                            OEMPrice = w.OEMPrice,
                            ImportPrice = w.ImportPrice,
                            Volume = w.Volume,
                            PackingQuantity = w.PackingQuantity,
                            IsActive = w.IsActive,
                        }
                )
                .ToListAsync();
        }

        private static Task<List<DetectionDomesticSnapshot>> SelectDomesticSnapshotsAsync(
            ISugarQueryable<DomesticProduct> query
        )
        {
            return query
                .Select(
                    dp =>
                        new DetectionDomesticSnapshot
                        {
                            ProductCode = dp.ProductCode,
                            ItemNumber = dp.HBProductNo,
                            SupplierCode = dp.SupplierCode,
                            Barcode = dp.Barcode,
                            ProductName = dp.ProductName,
                            EnglishName = dp.EnglishProductName,
                            DomesticPrice = dp.DomesticPrice,
                            OEMPrice = dp.OEMPrice,
                            ImportPrice = dp.ImportPrice,
                            UnitVolume = dp.UnitVolume,
                            PackingQuantity = dp.PackingQuantity,
                        }
                )
                .ToListAsync();
        }

        private static bool HasPositivePrice(decimal? value)
        {
            return value.HasValue && value.Value > 0;
        }

        private async Task<Dictionary<string, ImportProductNameResolution>> ResolveImportProductNamesAsync(
            IEnumerable<DomesticProduct> domesticProducts
        )
        {
            var products = domesticProducts
                .Where(p => !string.IsNullOrWhiteSpace(p.ProductCode))
                .ToList();
            var resolutions = new Dictionary<string, ImportProductNameResolution>();
            var needTranslation = new List<DomesticProduct>();

            foreach (var product in products)
            {
                var englishName = NormalizeValidEnglishName(product.EnglishProductName);
                if (!string.IsNullOrWhiteSpace(englishName))
                {
                    resolutions[product.ProductCode] = new ImportProductNameResolution(
                        englishName,
                        englishName,
                        false
                    );
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(product.ProductName))
                {
                    needTranslation.Add(product);
                }
                else
                {
                    resolutions[product.ProductCode] = new ImportProductNameResolution(
                        product.HBProductNo ?? product.ProductCode,
                        null,
                        false
                    );
                }
            }

            var translations = new Dictionary<string, string>();
            if (_translationService != null && needTranslation.Count > 0)
            {
                translations = await _translationService.BatchTranslateToEnglishAsync(
                    needTranslation.Select(p => p.ProductName!).Distinct().ToList()
                );
            }

            foreach (var product in needTranslation)
            {
                var translatedName = translations.TryGetValue(product.ProductName!, out var value)
                    ? NormalizeValidEnglishName(value)
                    : null;

                if (!string.IsNullOrWhiteSpace(translatedName))
                {
                    resolutions[product.ProductCode] = new ImportProductNameResolution(
                        translatedName,
                        translatedName,
                        true
                    );
                    continue;
                }

                if (translations.TryGetValue(product.ProductName!, out var invalidTranslation))
                {
                    // 翻译服务失败时会返回原文；仍含中文的结果不能写入英文字段。
                    _logger.LogWarning(
                        "跳过仍包含中文的国内导入英文名称写回: ProductCode={ProductCode}, ProductName={ProductName}, Translation={Translation}",
                        product.ProductCode,
                        product.ProductName,
                        invalidTranslation
                    );
                }

                resolutions[product.ProductCode] = new ImportProductNameResolution(
                    product.ProductName!.Trim(),
                    null,
                    false
                );
            }

            return resolutions;
        }

        private string? NormalizeValidEnglishName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = value.Trim();
            return ContainsChinese(normalized) ? null : normalized;
        }

        private bool ContainsChinese(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return _translationService?.ContainsChinese(value)
                ?? value.Any(c => c >= '\u4e00' && c <= '\u9fff');
        }

        private static bool ShouldSmartFillExistingProductName(
            Product existingProduct,
            DomesticProduct domesticProduct,
            ImportProductNameResolution nameResolution
        )
        {
            if (string.IsNullOrWhiteSpace(nameResolution.EnglishName))
                return false;

            if (string.IsNullOrWhiteSpace(existingProduct.ProductName))
                return true;

            return !string.IsNullOrWhiteSpace(domesticProduct.ProductName)
                && string.Equals(
                    existingProduct.ProductName.Trim(),
                    domesticProduct.ProductName.Trim(),
                    StringComparison.Ordinal
                );
        }

        private static DetectionDomesticSnapshot? FindDomesticMatch(
            DetectionItemDto item,
            Dictionary<string, DetectionDomesticSnapshot> byCode,
            Dictionary<string, DetectionDomesticSnapshot> bySupplierItem,
            Dictionary<string, DetectionDomesticSnapshot> byItem,
            Dictionary<string, DetectionDomesticSnapshot> byBarcode
        )
        {
            if (
                !string.IsNullOrWhiteSpace(item.ProductCode)
                && byCode.TryGetValue(item.ProductCode!, out var codeMatch)
            )
            {
                return codeMatch;
            }

            if (
                !string.IsNullOrWhiteSpace(item.ItemNumber)
                && !string.IsNullOrWhiteSpace(item.SupplierCode)
                && bySupplierItem.TryGetValue(
                    BuildSupplierItemMatchKey(item.SupplierCode, item.ItemNumber)!,
                    out var supplierItemMatch
                )
            )
            {
                return supplierItemMatch;
            }

            if (
                string.IsNullOrWhiteSpace(item.SupplierCode)
                && !string.IsNullOrWhiteSpace(item.ItemNumber)
                && byItem.TryGetValue(item.ItemNumber!, out var itemMatch)
            )
            {
                return itemMatch;
            }

            if (
                !string.IsNullOrWhiteSpace(item.Barcode)
                && byBarcode.TryGetValue(item.Barcode!, out var barcodeMatch)
            )
            {
                return barcodeMatch;
            }

            return null;
        }

        private static string? BuildSupplierItemMatchKey(string? supplierCode, string? itemNumber)
        {
            var normalizedSupplierCode = supplierCode?.Trim().ToUpperInvariant();
            var normalizedItemNumber = itemNumber?.Trim().ToUpperInvariant();
            return !string.IsNullOrWhiteSpace(normalizedSupplierCode)
                && !string.IsNullOrWhiteSpace(normalizedItemNumber)
                ? $"{normalizedSupplierCode}:{normalizedItemNumber}"
                : null;
        }

        private sealed class DetectionWarehouseSnapshot
        {
            public string? ProductCode { get; set; }
            public string? ItemNumber { get; set; }
            public string? SupplierCode { get; set; }
            public string? Barcode { get; set; }
            public string? ProductName { get; set; }
            public string? EnglishName { get; set; }
            public decimal? DomesticPrice { get; set; }
            public decimal? OEMPrice { get; set; }
            public decimal? ImportPrice { get; set; }
            public decimal? Volume { get; set; }
            public decimal? PackingQuantity { get; set; }
            public bool? IsActive { get; set; }
        }

        private sealed record ImportProductNameResolution(
            string DisplayName,
            string? EnglishName,
            bool WasTranslated
        );

        private sealed class DetectionDomesticSnapshot
        {
            public string? ProductCode { get; set; }
            public string? ItemNumber { get; set; }
            public string? SupplierCode { get; set; }
            public string? Barcode { get; set; }
            public string? ProductName { get; set; }
            public string? EnglishName { get; set; }
            public decimal? DomesticPrice { get; set; }
            public decimal? OEMPrice { get; set; }
            public decimal? ImportPrice { get; set; }
            public decimal? UnitVolume { get; set; }
            public decimal? PackingQuantity { get; set; }
        }

        /// <summary>
        /// 批量更新仓库商品
        /// 支持通过 ProductCode 或 ItemNumber 匹配商品进行更新
        /// </summary>
        /// <param name="items">待更新的商品列表</param>
        /// <returns>批量操作结果</returns>
        public Task<BatchOperationResultDto> BatchUpdateAsync(List<UpdateItemDto> items)
        {
            return BatchUpdateAsync(items, SystemUpdatedBy);
        }

        public async Task<BatchOperationResultDto> BatchUpdateAsync(
            List<UpdateItemDto> items,
            string? updatedBy
        )
        {
            return await BatchUpdateAsync(
                items,
                updatedBy,
                new WarehouseProductBatchUpdateOptionsDto()
            );
        }

        public async Task<WarehouseProductBatchUpdateResultDto> BatchUpdateAsync(
            List<UpdateItemDto> items,
            string? updatedBy,
            WarehouseProductBatchUpdateOptionsDto options
        )
        {
            options ??= new WarehouseProductBatchUpdateOptionsDto();
            var result = new WarehouseProductBatchUpdateResultDto
            {
                Success = true,
                Message = "更新完成",
            };
            if (items == null || items.Count == 0)
                return result;

            if (options.SyncImageToHq && !options.GenerateImageUrls)
            {
                result.Success = false;
                result.Message = "同步 HQ 图片前必须启用图片地址生成";
                result.FailedCount = items.Count;
                result.Errors.Add(result.Message);
                return result;
            }

            string? normalizedImageBaseUrl = null;
            if (
                options.GenerateImageUrls
                && !WarehouseProductBatchImageUrlBuilder.TryNormalizeBaseUrl(
                    options.ImageBaseUrl,
                    out normalizedImageBaseUrl,
                    out var imageBaseUrlError
                )
            )
            {
                result.Success = false;
                result.Message = imageBaseUrlError;
                result.FailedCount = items.Count;
                result.Errors.Add(imageBaseUrlError);
                return result;
            }

            var effectiveUpdatedBy = ResolveUpdatedBy(updatedBy);
            var batchGuid = Guid.NewGuid();

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

                var imageProductsByCode = new Dictionary<string, Product>(
                    StringComparer.OrdinalIgnoreCase
                );
                if (options.GenerateImageUrls)
                {
                    var imageProductCodes = productCodes
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .Select(code => code!)
                        .Concat(itemToCode.Values)
                        .Where(code => !string.IsNullOrWhiteSpace(code))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    foreach (var codeBatch in imageProductCodes.Chunk(500))
                    {
                        var codes = codeBatch.ToList();
                        var products = await _context
                            .Db.Queryable<Product>()
                            .Where(product =>
                                product.ProductCode != null
                                && codes.Contains(product.ProductCode)
                                && !product.IsDeleted
                            )
                            .ToListAsync();
                        foreach (
                            var product in products
                                .Where(product => !string.IsNullOrWhiteSpace(product.ProductCode))
                                .GroupBy(
                                    product => product.ProductCode!,
                                    StringComparer.OrdinalIgnoreCase
                                )
                                .Select(group => group
                                    .OrderByDescending(product =>
                                        product.UpdatedAt ?? product.CreatedAt
                                    )
                                    .ThenByDescending(product => product.CreatedAt)
                                    .First()
                                )
                        )
                        {
                            if (
                                !string.IsNullOrWhiteSpace(product.ProductCode)
                                && !imageProductsByCode.ContainsKey(product.ProductCode)
                            )
                            {
                                imageProductsByCode[product.ProductCode] = product;
                            }
                        }
                    }
                }

                // 先按最终可解析的商品编码一次性读取旧快照；非法、重复或最终无变化项不会产生事件。
                var auditProductCodes = productCodes
                    .Where(code => !string.IsNullOrWhiteSpace(code))
                    .Select(code => code!)
                    .Concat(itemToCode.Values)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var beforeSnapshots = await _changeHistoryService.CaptureSnapshotsAsync(
                    auditProductCodes
                );

                var supplierDomesticProductsByCode = new Dictionary<string, DomesticProduct>(
                    StringComparer.OrdinalIgnoreCase
                );
                var supplierProductsByCode = new Dictionary<string, Product>(
                    StringComparer.OrdinalIgnoreCase
                );
                if (
                    items.Any(item => !string.IsNullOrWhiteSpace(item.SupplierCode))
                    && auditProductCodes.Any()
                )
                {
                    var supplierDomesticProducts = await _context
                        .Db.Queryable<DomesticProduct>()
                        .Where(product => auditProductCodes.Contains(product.ProductCode))
                        .ToListAsync();
                    supplierDomesticProductsByCode = supplierDomesticProducts
                        .GroupBy(product => product.ProductCode, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            group => group.Key,
                            group => group.FirstOrDefault(product => !product.IsDeleted) ?? group.First(),
                            StringComparer.OrdinalIgnoreCase
                        );

                    var supplierProducts = await _context
                        .Db.Queryable<Product>()
                        .Where(product =>
                            product.ProductCode != null
                            && auditProductCodes.Contains(product.ProductCode)
                            && !product.IsDeleted
                        )
                        .ToListAsync();
                    supplierProductsByCode = supplierProducts
                        .Where(product => !string.IsNullOrWhiteSpace(product.ProductCode))
                        .GroupBy(product => product.ProductCode!, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            group => group.Key,
                            group => group
                                .OrderByDescending(product => product.UpdatedAt ?? product.CreatedAt)
                                .ThenByDescending(product => product.CreatedAt)
                                .First(),
                            StringComparer.OrdinalIgnoreCase
                        );
                }

                var toUpdateWp = new List<WarehouseProduct>();
                var toCreateWp = new List<WarehouseProduct>();
                var codesWithImportPrice = new List<string>();
                var codesWithStorePurchasePrice = new List<string>();
                var packingQuantityByCode = new Dictionary<string, int>();
                var supplierCodeByProductCode = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase
                );
                var supplierWarehouseProductsByCode = new Dictionary<string, WarehouseProduct>(
                    StringComparer.OrdinalIgnoreCase
                );
                var imageUrlByCode = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase
                );
                var processedProductCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in items)
                {
                    if (
                        (item.PackingQuantity.HasValue && item.PackingQuantity.Value < 0)
                        || (item.MinOrderQuantity.HasValue && item.MinOrderQuantity.Value < 0)
                    )
                    {
                        // 服务层同时做防御性校验，确保绕过 DTO 校验的调用也不会部分写入其他字段。
                        result.Errors.Add(
                            $"装箱数和最小起订量不能为负数: ProductCode={item.ProductCode}, ItemNumber={item.ItemNumber}"
                        );
                        result.FailedCount++;
                        continue;
                    }

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

                    if (wp == null && string.IsNullOrWhiteSpace(targetCode))
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

                    if (!processedProductCodes.Add(targetCode))
                    {
                        // 同一批次按最终商品编码只处理首项，避免重复新建主键或后项覆盖首项。
                        result.Errors.Add($"批次内商品编码重复: {targetCode}");
                        result.FailedCount++;
                        continue;
                    }

                    if (options.GenerateImageUrls)
                    {
                        if (!imageProductsByCode.TryGetValue(targetCode, out var imageProduct))
                        {
                            result.Errors.Add(
                                $"未找到本地商品主档，无法生成图片地址: ProductCode={targetCode}"
                            );
                            result.FailedCount++;
                            continue;
                        }

                        if (
                            !WarehouseProductBatchImageUrlBuilder.TryBuild(
                                normalizedImageBaseUrl!,
                                imageProduct.ItemNumber,
                                out var imageUrl,
                                out var imageUrlError
                            )
                        )
                        {
                            result.Errors.Add($"{imageUrlError}: ProductCode={targetCode}");
                            result.FailedCount++;
                            continue;
                        }

                        imageUrlByCode[targetCode] = imageUrl;
                    }

                    var normalizedSupplierCode = string.IsNullOrWhiteSpace(item.SupplierCode)
                        ? null
                        : item.SupplierCode.Trim();
                    if (
                        normalizedSupplierCode != null
                        && (
                            !supplierDomesticProductsByCode.TryGetValue(
                                targetCode,
                                out var supplierDomesticProduct
                            )
                            || supplierDomesticProduct.IsDeleted
                        )
                        && !supplierProductsByCode.ContainsKey(targetCode)
                    )
                    {
                        result.Errors.Add($"未找到商品主档，无法设置国内供应商: ProductCode={targetCode}");
                        result.FailedCount++;
                        continue;
                    }

                    if (wp == null)
                    {
                        var newWp = new WarehouseProduct
                        {
                            ProductCode = targetCode!,
                            DomesticPrice = item.DomesticPrice,
                            OEMPrice = item.OEMPrice,
                            ImportPrice = item.ImportPrice,
                            Volume = item.Volume,
                            PackingQuantity = item.PackingQuantity,
                            MinOrderQuantity = item.MinOrderQuantity,
                            StockQuantity = 0,
                            IsActive = item.IsActive ?? true,
                            IsDeleted = false,
                            CreatedAt = DateTime.Now,
                            UpdatedAt = DateTime.Now,
                            CreatedBy = effectiveUpdatedBy,
                            UpdatedBy = effectiveUpdatedBy,
                        };
                        toCreateWp.Add(newWp);
                        if (normalizedSupplierCode != null)
                        {
                            supplierCodeByProductCode[targetCode] = normalizedSupplierCode;
                            supplierWarehouseProductsByCode[targetCode] = newWp;
                        }
                        if (item.PackingQuantity.HasValue)
                        {
                            // 装箱数以国内商品表为展示主来源，同时保留仓库表值供缺失时回退。
                            packingQuantityByCode[targetCode!] = item.PackingQuantity.Value;
                        }
                        if (item.ImportPrice.HasValue)
                        {
                            codesWithImportPrice.Add(targetCode!);
                            if (item.SyncStorePurchasePrice ?? true)
                            {
                                // 货柜页字段可选时可关闭分店进货价联动；旧入口不传时保持同步。
                                codesWithStorePurchasePrice.Add(targetCode!);
                            }
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
                    if (item.PackingQuantity.HasValue)
                    {
                        wp.PackingQuantity = item.PackingQuantity.Value;
                        // 只同步未删除的国内商品，避免恢复或污染历史软删除记录。
                        packingQuantityByCode[wp.ProductCode] = item.PackingQuantity.Value;
                    }
                    if (item.MinOrderQuantity.HasValue)
                        wp.MinOrderQuantity = item.MinOrderQuantity.Value;
                    if (item.IsActive.HasValue)
                    {
                        // 字段可选更新不传上下架状态时，保留已有仓库状态。
                        wp.IsActive = item.IsActive.Value;
                    }
                    wp.UpdatedAt = DateTime.Now;
                    wp.UpdatedBy = effectiveUpdatedBy;
                    toUpdateWp.Add(wp);

                    if (normalizedSupplierCode != null)
                    {
                        supplierCodeByProductCode[targetCode] = normalizedSupplierCode;
                        supplierWarehouseProductsByCode[targetCode] = wp;
                    }

                    if (item.ImportPrice.HasValue)
                    {
                        codesWithImportPrice.Add(wp.ProductCode);
                        if (item.SyncStorePurchasePrice ?? true)
                        {
                            // 货柜页字段可选时可关闭分店进货价联动；旧入口不传时保持同步。
                            codesWithStorePurchasePrice.Add(wp.ProductCode);
                        }
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
                            w.PackingQuantity,
                            w.MinOrderQuantity,
                            w.IsActive,
                            w.UpdatedAt,
                            w.UpdatedBy,
                        })
                        .ExecuteCommandAsync();
                    result.SuccessCount += toUpdateWp.Count;
                }
                if (toCreateWp.Any())
                {
                    await _context.Db.Insertable(toCreateWp).ExecuteCommandAsync();
                    result.SuccessCount += toCreateWp.Count;
                }

                if (supplierCodeByProductCode.Any())
                {
                    var supplierUpdatedAt = DateTime.Now;
                    var domesticProductsToCreate = new List<DomesticProduct>();
                    var domesticProductsToUpdate = new List<DomesticProduct>();
                    foreach (var (productCode, supplierCode) in supplierCodeByProductCode)
                    {
                        supplierDomesticProductsByCode.TryGetValue(
                            productCode,
                            out var domesticProduct
                        );
                        var shouldCreate = domesticProduct == null;
                        var shouldRefreshFromProduct = shouldCreate || domesticProduct!.IsDeleted;
                        if (shouldCreate)
                        {
                            domesticProduct = new DomesticProduct
                            {
                                ProductCode = productCode,
                                CreatedAt = supplierUpdatedAt,
                                CreatedBy = effectiveUpdatedBy,
                            };
                        }

                        if (shouldRefreshFromProduct)
                        {
                            // 选择供应商代表明确建立国内商品关系；缺失时补建，软删除时恢复并刷新主数据。
                            var product = supplierProductsByCode[productCode];
                            var warehouseProduct = supplierWarehouseProductsByCode[productCode];
                            domesticProduct!.ProductName = product.ProductName;
                            domesticProduct.EnglishProductName = product.EnglishName;
                            domesticProduct.HBProductNo = product.ItemNumber;
                            domesticProduct.Barcode = product.Barcode;
                            domesticProduct.ProductType = product.ProductType ?? 0;
                            domesticProduct.DomesticPrice = warehouseProduct.DomesticPrice;
                            domesticProduct.OEMPrice =
                                warehouseProduct.OEMPrice ?? product.RetailPrice;
                            domesticProduct.ImportPrice =
                                warehouseProduct.ImportPrice ?? product.PurchasePrice;
                            domesticProduct.PackingQuantity = warehouseProduct.PackingQuantity;
                            domesticProduct.UnitVolume = warehouseProduct.Volume;
                            domesticProduct.MiddlePackQuantity =
                                warehouseProduct.MinOrderQuantity;
                            domesticProduct.ProductImage = product.ProductImage;
                            domesticProduct.IsActive = warehouseProduct.IsActive;
                            domesticProduct.IsDeleted = false;
                        }

                        domesticProduct!.SupplierCode = supplierCode;
                        domesticProduct.UpdatedAt = supplierUpdatedAt;
                        domesticProduct.UpdatedBy = effectiveUpdatedBy;
                        if (shouldCreate)
                            domesticProductsToCreate.Add(domesticProduct);
                        else
                            domesticProductsToUpdate.Add(domesticProduct);
                    }

                    if (domesticProductsToCreate.Any())
                    {
                        await _context
                            .Db.Insertable(domesticProductsToCreate)
                            .ExecuteCommandAsync();
                    }
                    if (domesticProductsToUpdate.Any())
                    {
                        await _context
                            .Db.Updateable(domesticProductsToUpdate)
                            .ExecuteCommandAsync();
                    }
                }

                if (packingQuantityByCode.Any())
                {
                    var packingProductCodes = packingQuantityByCode.Keys.ToList();
                    var domesticProducts = await _context
                        .Db.Queryable<DomesticProduct>()
                        .Where(dp => packingProductCodes.Contains(dp.ProductCode) && !dp.IsDeleted)
                        .ToListAsync();
                    foreach (var domesticProduct in domesticProducts)
                    {
                        domesticProduct.PackingQuantity = packingQuantityByCode[
                            domesticProduct.ProductCode
                        ];
                        domesticProduct.UpdatedAt = DateTime.Now;
                        domesticProduct.UpdatedBy = effectiveUpdatedBy;
                    }

                    if (domesticProducts.Any())
                    {
                        await _context
                            .Db.Updateable(domesticProducts)
                            .UpdateColumns(dp => new
                            {
                                dp.PackingQuantity,
                                dp.UpdatedAt,
                                dp.UpdatedBy,
                            })
                            .ExecuteCommandAsync();
                    }
                }

                if (imageUrlByCode.Any())
                {
                    var imageUpdatedAt = DateTime.Now;
                    var imageProducts = imageUrlByCode.Keys
                        .Select(code => imageProductsByCode[code])
                        .ToList();
                    foreach (var product in imageProducts)
                    {
                        product.ProductImage = imageUrlByCode[product.ProductCode!];
                        product.UpdatedAt = imageUpdatedAt;
                        product.UpdatedBy = effectiveUpdatedBy;
                    }
                    foreach (var productBatch in imageProducts.Chunk(200))
                    {
                        await _context
                            .Db.Updateable(productBatch.ToList())
                            .UpdateColumns(product => new
                            {
                                product.ProductImage,
                                product.UpdatedAt,
                                product.UpdatedBy,
                            })
                            .ExecuteCommandAsync();
                    }

                    var imageProductCodes = imageUrlByCode.Keys.ToList();
                    var imageDomesticProducts = new List<DomesticProduct>();
                    foreach (var codeBatch in imageProductCodes.Chunk(500))
                    {
                        var codes = codeBatch.ToList();
                        imageDomesticProducts.AddRange(
                            await _context
                                .Db.Queryable<DomesticProduct>()
                                .Where(product =>
                                    codes.Contains(product.ProductCode) && !product.IsDeleted
                                )
                                .ToListAsync()
                        );
                    }
                    foreach (var domesticProduct in imageDomesticProducts)
                    {
                        domesticProduct.ProductImage = imageUrlByCode[
                            domesticProduct.ProductCode
                        ];
                        domesticProduct.UpdatedAt = imageUpdatedAt;
                        domesticProduct.UpdatedBy = effectiveUpdatedBy;
                    }
                    foreach (var domesticBatch in imageDomesticProducts.Chunk(200))
                    {
                        await _context
                            .Db.Updateable(domesticBatch.ToList())
                            .UpdateColumns(product => new
                            {
                                product.ProductImage,
                                product.UpdatedAt,
                                product.UpdatedBy,
                            })
                            .ExecuteCommandAsync();
                    }

                    result.ImageUpdatedCount = imageProducts.Count;
                    result.ImageUpdates = imageUrlByCode
                        .Select(pair => new ProductHqImageUpdateItemDto
                        {
                            ProductCode = pair.Key,
                            ImageUrl = pair.Value,
                        })
                        .ToList();
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
                            p.UpdatedBy = effectiveUpdatedBy;
                        }
                    }
                    if (products.Any())
                    {
                        await _context
                            .Db.Updateable(products)
                            .UpdateColumns(p => new
                            {
                                p.PurchasePrice,
                                p.UpdatedAt,
                                p.UpdatedBy,
                            })
                            .ExecuteCommandAsync();
                    }

                    var storeRetailPrices = codesWithStorePurchasePrice.Any()
                        ? await _context
                            .Db.Queryable<StoreRetailPrice>()
                            .Where(srp =>
                                srp.ProductCode != null
                                && codesWithStorePurchasePrice.Contains(srp.ProductCode)
                            )
                            .ToListAsync()
                        : new List<StoreRetailPrice>();

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

                await RecordProductChangeHistoryAsync(
                    beforeSnapshots,
                    auditProductCodes,
                    action: "BatchUpdate",
                    source: "WarehouseProducts",
                    actorName: effectiveUpdatedBy,
                    batchGuid: batchGuid
                );

                _context.Db.Ado.CommitTran();
            }
            catch (Exception ex)
            {
                _context.Db.Ado.RollbackTran();
                _logger.LogError(ex, "批量更新失败");
                result.Success = false;
                result.SuccessCount = 0;
                result.ImageUpdatedCount = 0;
                result.ImageUpdates.Clear();
                result.Message = "批量更新失败: " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// 批量创建仓库商品
        /// 支持普通商品和套装商品，自动跳过已存在的商品
        /// </summary>
        /// <param name="items">待创建的商品列表</param>
        /// <param name="useTransaction">是否由本方法自行开启事务；整柜提交会关闭它，交给外层事务统一控制</param>
        /// <returns>批量操作结果</returns>
        public Task<BatchOperationResultDto> BatchCreateAsync(
            List<CreateItemDto> items,
            bool useTransaction = true
        )
        {
            return BatchCreateAsync(items, useTransaction, SystemUpdatedBy);
        }

        public Task<BatchOperationResultDto> BatchCreateAsync(
            List<CreateItemDto> items,
            bool useTransaction,
            string? updatedBy
        )
        {
            return BatchCreateAsync(
                items,
                useTransaction,
                updatedBy,
                auditSource: "WarehouseProducts",
                sourceReference: null,
                batchGuid: null
            );
        }

        public async Task<BatchOperationResultDto> BatchCreateAsync(
            List<CreateItemDto> items,
            bool useTransaction,
            string? updatedBy,
            string auditSource,
            string? sourceReference,
            Guid? batchGuid
        )
        {
            return await BatchCreateAsync(
                items,
                useTransaction,
                updatedBy,
                auditSource,
                sourceReference,
                batchGuid,
                actorUserGuid: null
            );
        }

        public async Task<BatchOperationResultDto> BatchCreateAsync(
            List<CreateItemDto> items,
            bool useTransaction,
            string? updatedBy,
            string auditSource,
            string? sourceReference,
            Guid? batchGuid,
            string? actorUserGuid
        )
        {
            var result = new BatchOperationResultDto { Success = true, Message = "创建完成" };
            if (items == null || items.Count == 0)
                return result;

            var effectiveUpdatedBy = ResolveUpdatedBy(updatedBy);
            var effectiveBatchGuid = batchGuid ?? Guid.NewGuid();

            try
            {
                if (useTransaction)
                {
                    // 默认入口自行开事务；整柜提交会传 false，由外层统一提交/回滚，避免嵌套事务提前落库。
                    _context.Db.Ado.BeginTran();
                }
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
                        result.Errors.Add($"RRP must be greater than 0: {item.ItemNumber}");
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
                            RetailPrice = item.OEMPrice,
                            ProductImage = ProductImageUrlHelper.EnsureImageUrl(
                                item.ImageUrl,
                                item.ItemNumber ?? code
                            ),
                            WarehouseCategoryGUID = string.IsNullOrWhiteSpace(item.WarehouseCategoryGUID)
                                ? null
                                : item.WarehouseCategoryGUID.Trim(),
                            // 货柜创建套装时必须同步写 POS 商品类型，否则 POS 商品管理会把空值显示为单品。
                            ProductType = item.ProductType ?? (item.IsSetProduct ? 1 : 0),
                            IsAutoPricing = false,
                            IsActive = true,
                            IsDeleted = false,
                            CreatedAt = now,
                            UpdatedAt = now,
                            CreatedBy = effectiveUpdatedBy,
                            UpdatedBy = effectiveUpdatedBy,
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
                        CreatedBy = effectiveUpdatedBy,
                        UpdatedBy = effectiveUpdatedBy,
                    };
                    toCreateWps.Add(wp);

                    // 处理套装商品（使用内存查找，避免 N+1）
                    if (item.IsSetProduct && !string.IsNullOrWhiteSpace(code))
                    {
                        if (setProductsByCode.TryGetValue(code!, out var setProducts))
                        {
                            // 套装子码进货价按子码零售价比例分摊主项进货价，子项自身进货价只作为无法分摊时的保守回退。
                            var allocatedPurchasePrices = SetChildPurchasePriceAllocator.AllocateByRetailRatio(
                                setProducts,
                                item.ImportPrice,
                                sp => sp.SetProductCode,
                                sp => sp.OEMPrice ?? item.OEMPrice
                            );
                            foreach (var sp in setProducts)
                            {
                                var setPurchasePrice =
                                    sp.SetProductCode != null
                                    && allocatedPurchasePrices.TryGetValue(sp.SetProductCode, out var allocatedPurchasePrice)
                                        ? allocatedPurchasePrice
                                        : sp.ImportPrice ?? item.ImportPrice;
                                var setCode = new ProductSetCode
                                {
                                    SetCodeId = sp.SetProductCode!,
                                    ProductCode = code,
                                    SetProductCode = sp.SetProductCode!,
                                    SetItemNumber = sp.SetProductNo,
                                    SetBarcode = sp.SetBarcode,
                                    SetPurchasePrice = setPurchasePrice,
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

                var auditProductCodes = toCreateWps
                    .Select(item => item.ProductCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var beforeSnapshots = await _changeHistoryService.CaptureSnapshotsAsync(
                    auditProductCodes
                );

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
                var activeStores = (
                    await _context
                        .Db.Queryable<Store>()
                        .Where(s => s.IsActive == true && s.IsDeleted == false)
                        .Select(s => s.StoreCode)
                        .ToListAsync()
                )
                    .Where(storeCode => !string.IsNullOrWhiteSpace(storeCode))
                    .Select(storeCode => storeCode!)
                    .Distinct()
                    .ToList();

                if (activeStores.Any() && toCreateProducts.Any())
                {
                    var toCreateStoreRetailPrices = new List<StoreRetailPrice>();
                    var toCreateStoreMultiCodeProducts = new List<StoreMultiCodeProduct>();

                    var createdProductsDict = toCreateProducts
                        .Where(p => !string.IsNullOrWhiteSpace(p.ProductCode))
                        .GroupBy(p => p.ProductCode!)
                        .ToDictionary(g => g.Key, g => g.First());

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
                                    StoreRetailPriceValue = product.RetailPrice,
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

                await RecordProductChangeHistoryAsync(
                    beforeSnapshots,
                    auditProductCodes,
                    action: "Create",
                    source: auditSource,
                    actorName: effectiveUpdatedBy,
                    batchGuid: effectiveBatchGuid,
                    sourceReference: sourceReference,
                    actorUserGuid: actorUserGuid
                );

                if (useTransaction)
                {
                    _context.Db.Ado.CommitTran();
                }
            }
            catch (Exception ex)
            {
                if (useTransaction)
                {
                    _context.Db.Ado.RollbackTran();
                }
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
            var totalStopwatch = Stopwatch.StartNew();
            var timings = new WarehouseProductTableTimings();
            var keyword = string.IsNullOrWhiteSpace(request.GlobalSearch)
                ? null
                : request.GlobalSearch.Trim();
            var isCodeLikeKeyword = keyword != null && IsWarehouseCodeLikeKeyword(keyword);
            var requestSnapshot = CreateWarehouseProductTableRequestSnapshot(
                request,
                keyword,
                isCodeLikeKeyword
            );

            var warehouseProductQuery = MeasureWarehouseProductTableStage(
                "candidate",
                totalStopwatch,
                timings,
                requestSnapshot,
                elapsedMs => timings.CandidateMs = elapsedMs,
                () =>
                {
                    var baseQuery = _context.Db.Queryable<WarehouseProduct>();
                    if (!isCodeLikeKeyword || keyword == null)
                    {
                        return baseQuery;
                    }

                    // 代码型搜索先按单字段索引生成候选集，避免宽 OR 和相关 EXISTS 放大执行计划。
                    var candidateQuery = BuildWarehouseCodeSearchCandidateQuery(keyword);
                    return baseQuery
                        .InnerJoin(
                            candidateQuery,
                            (warehouseProduct, candidate) =>
                                warehouseProduct.ProductCode == candidate.ProductCode
                        )
                        .Select((warehouseProduct, candidate) => warehouseProduct)
                        .MergeTable();
                }
            );

            // 多表关联查询（使用 LeftJoin 避免 N+1 问题）
            var query = warehouseProductQuery
                .LeftJoin<DomesticProduct>(
                    (w, dp) => dp.ProductCode == w.ProductCode && !dp.IsDeleted
                )
                .LeftJoin<ChinaSupplier>(
                    (w, dp, s) => dp.SupplierCode == s.SupplierCode && !s.IsDeleted
                )
                .InnerJoin<Product>((w, dp, s, p) => p.ProductCode == w.ProductCode && !p.IsDeleted)
                .LeftJoin<WarehouseCategory>(
                    (w, dp, s, p, c) => p.WarehouseCategoryGUID == c.CategoryGUID && !c.IsDeleted
                )
                .LeftJoin<HBLocalSupplier>(
                    (w, dp, s, p, c, ls) =>
                        p.LocalSupplierCode == ls.LocalSupplierCode && !ls.IsDeleted
                )
                .Where(w => !w.IsDeleted);

            // 分类筛选互斥处理：先清洗具体分类，具体分类优先，未分类只在未选择具体分类时生效。
            var requestedCategoryGuids = request.CategoryGuids
                ?.Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g.Trim())
                .Distinct()
                .ToList() ?? new List<string>();
            if (requestedCategoryGuids.Any())
            {
                var guids = request.IncludeSubCategories
                    ? GetCategoryAndSubCategories(requestedCategoryGuids)
                    : requestedCategoryGuids;
                query = query.Where(
                    (w, dp, s, p, c, ls) =>
                        p.WarehouseCategoryGUID != null && guids.Contains(p.WarehouseCategoryGUID)
                );
            }
            else if (request.UncategorizedOnly)
            {
                query = query.Where(
                    (w, dp, s, p, c, ls) =>
                        p.WarehouseCategoryGUID == null || p.WarehouseCategoryGUID == string.Empty
                );
            }

            if (keyword != null && !isCodeLikeKeyword)
            {
                var globalSearchExpression = Expressionable.Create<
                    WarehouseProduct,
                    DomesticProduct,
                    ChinaSupplier,
                    Product,
                    WarehouseCategory,
                    HBLocalSupplier
                >();

                // SQL Server 默认不区分大小写；列侧不包 ToLower，保留索引可用性。
                globalSearchExpression = globalSearchExpression.Or(
                    (w, dp, s, p, c, ls) =>
                        (p.ProductName != null && p.ProductName.Contains(keyword))
                        || (p.EnglishName != null && p.EnglishName.Contains(keyword))
                        || (p.ItemNumber != null && p.ItemNumber.Contains(keyword))
                        || (p.Barcode != null && p.Barcode.Contains(keyword))
                        || (c.CategoryName != null && c.CategoryName.Contains(keyword))
                        || (s.SupplierName != null && s.SupplierName.Contains(keyword))
                        || (p.LocalSupplierCode != null && p.LocalSupplierCode.Contains(keyword))
                        || (ls.Name != null && ls.Name.Contains(keyword))
                );

                query = query.Where(
                    globalSearchExpression
                        .Or(BuildPickingLocationCodePredicate("contains", keyword))
                        .Or(BuildPickingLocationBarcodePredicate("contains", keyword))
                        .ToExpression()
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

                    // 列头筛选 token 约定：旧裸文本保持 contains；新匹配模式使用 __filter: 前缀，避免误伤旧值。
                    switch (key)
                    {
                        case "productname":
                        case "name":
                            query = ApplyWarehouseTextMatchFilter(
                                query,
                                values,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        p.ProductName != null
                                        && p.ProductName.Contains(value),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        p.ProductName != null && p.ProductName == value,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        p.ProductName != null
                                        && p.ProductName.StartsWith(value),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        p.ProductName != null
                                        && p.ProductName.EndsWith(value)
                            );
                            break;
                        case "nameen":
                            query = ApplyWarehouseTextMatchFilter(
                                query,
                                values,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        p.EnglishName != null
                                        && p.EnglishName.Contains(value),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        p.EnglishName != null && p.EnglishName == value,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        p.EnglishName != null
                                        && p.EnglishName.StartsWith(value),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        p.EnglishName != null
                                        && p.EnglishName.EndsWith(value)
                            );
                            break;
                        case "itemnumber":
                            query = ApplyWarehouseTextMatchFilter(
                                query,
                                values,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        p.ItemNumber != null
                                        && p.ItemNumber.Contains(value),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        p.ItemNumber != null && p.ItemNumber == value,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        p.ItemNumber != null
                                        && p.ItemNumber.StartsWith(value),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        p.ItemNumber != null
                                        && p.ItemNumber.EndsWith(value)
                            );
                            break;
                        case "barcode":
                            query = ApplyWarehouseTextMatchFilter(
                                query,
                                values,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        p.Barcode != null && p.Barcode.Contains(value),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        p.Barcode != null && p.Barcode == value,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        p.Barcode != null && p.Barcode.StartsWith(value),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        p.Barcode != null && p.Barcode.EndsWith(value)
                            );
                            break;
                        case "locationcodes":
                            query = ApplyPickingLocationTextMatchFilter(query, values);
                            break;
                        case "categoryname":
                            // 兼容旧客户端的分类名称文本筛选；新仓库商品页分类筛选走顶层 CategoryGuids/UncategorizedOnly。
                            query = ApplyWarehouseTextMatchFilter(
                                query,
                                values,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        c.CategoryName != null
                                        && c.CategoryName.Contains(value),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        c.CategoryName != null && c.CategoryName == value,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        c.CategoryName != null
                                        && c.CategoryName.StartsWith(value),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        c.CategoryName != null
                                        && c.CategoryName.EndsWith(value)
                            );
                            break;
                        case "suppliername":
                        case "domesticsuppliername":
                            query = ApplyWarehouseTextMatchFilter(
                                query,
                                values,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        s.SupplierName != null
                                        && s.SupplierName.Contains(value),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        s.SupplierName != null && s.SupplierName == value,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        s.SupplierName != null
                                        && s.SupplierName.StartsWith(value),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        s.SupplierName != null
                                        && s.SupplierName.EndsWith(value)
                            );
                            break;
                        case "suppliercode":
                        case "domesticsuppliercode":
                            var supplierCodes = NormalizeWarehouseExactTextFilterValues(values);
                            if (supplierCodes.Any())
                            {
                                query = query.Where(
                                    (w, dp, s, p, c, ls) =>
                                        s.SupplierCode != null && supplierCodes.Contains(s.SupplierCode)
                                );
                            }
                            break;
                        case "localsuppliercode":
                            var localSupplierCodes = NormalizeWarehouseExactTextFilterValues(values);
                            if (localSupplierCodes.Any())
                            {
                                query = query.Where(
                                    (w, dp, s, p, c, ls) =>
                                        p.LocalSupplierCode != null
                                        && localSupplierCodes.Contains(p.LocalSupplierCode)
                                );
                            }
                            break;
                        case "localsuppliername":
                            query = ApplyWarehouseTextMatchFilter(
                                query,
                                values,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        ls.Name != null && ls.Name.Contains(value),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        ls.Name != null && ls.Name == value,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        ls.Name != null && ls.Name.StartsWith(value),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        ls.Name != null && ls.Name.EndsWith(value)
                            );
                            break;
                        case "domesticprice":
                            query = ApplyWarehouseDecimalRangeFilter(
                                query,
                                values,
                                // 显式分支保持数值列比较语义：仓库值优先，仅 null 时使用国内商品值。
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        (
                                            w.DomesticPrice.HasValue
                                            && w.DomesticPrice.Value >= value
                                        )
                                        || (
                                            w.DomesticPrice == null
                                            && dp.DomesticPrice != null
                                            && dp.DomesticPrice.Value >= value
                                        ),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        (
                                            w.DomesticPrice.HasValue
                                            && w.DomesticPrice.Value <= value
                                        )
                                        || (
                                            w.DomesticPrice == null
                                            && dp.DomesticPrice != null
                                            && dp.DomesticPrice.Value <= value
                                        ),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        (
                                            w.DomesticPrice.HasValue
                                            && w.DomesticPrice.Value == value
                                        )
                                        || (
                                            w.DomesticPrice == null
                                            && dp.DomesticPrice != null
                                            && dp.DomesticPrice.Value == value
                                        )
                            );
                            break;
                        case "oemprice":
                        case "labelprice":
                            query = ApplyWarehouseDecimalRangeFilter(
                                query,
                                values,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        w.OEMPrice.HasValue && w.OEMPrice.Value >= value,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        w.OEMPrice.HasValue && w.OEMPrice.Value <= value,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        w.OEMPrice.HasValue && w.OEMPrice.Value == value
                            );
                            break;
                        case "importprice":
                            query = ApplyWarehouseDecimalRangeFilter(
                                query,
                                values,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        w.ImportPrice.HasValue && w.ImportPrice.Value >= value,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        w.ImportPrice.HasValue && w.ImportPrice.Value <= value,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        w.ImportPrice.HasValue && w.ImportPrice.Value == value
                            );
                            break;
                        case "packingqty":
                        case "packingquantity":
                            query = ApplyWarehouseIntRangeFilter(
                                query,
                                values,
                                // 展示值优先取国内商品，缺失时回退仓库商品；过滤条件必须保持相同语义。
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        (
                                            dp.PackingQuantity != null
                                            && dp.PackingQuantity.Value >= value
                                        )
                                        || (
                                            dp.PackingQuantity == null
                                            && w.PackingQuantity.HasValue
                                            && w.PackingQuantity.Value >= value
                                        ),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        (
                                            dp.PackingQuantity != null
                                            && dp.PackingQuantity.Value <= value
                                        )
                                        || (
                                            dp.PackingQuantity == null
                                            && w.PackingQuantity.HasValue
                                            && w.PackingQuantity.Value <= value
                                        ),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        (
                                            dp.PackingQuantity != null
                                            && dp.PackingQuantity.Value == value
                                        )
                                        || (
                                            dp.PackingQuantity == null
                                            && w.PackingQuantity.HasValue
                                            && w.PackingQuantity.Value == value
                                        )
                            );
                            break;
                        case "volume":
                            query = ApplyWarehouseDecimalRangeFilter(
                                query,
                                values,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        (w.Volume.HasValue && w.Volume.Value >= value)
                                        || (
                                            w.Volume == null
                                            && dp.UnitVolume != null
                                            && dp.UnitVolume.Value >= value
                                        ),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        (w.Volume.HasValue && w.Volume.Value <= value)
                                        || (
                                            w.Volume == null
                                            && dp.UnitVolume != null
                                            && dp.UnitVolume.Value <= value
                                        ),
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        (w.Volume.HasValue && w.Volume.Value == value)
                                        || (
                                            w.Volume == null
                                            && dp.UnitVolume != null
                                            && dp.UnitVolume.Value == value
                                        )
                            );
                            break;
                        case "minorderquantity":
                            query = ApplyWarehouseIntRangeFilter(
                                query,
                                values,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        w.MinOrderQuantity.HasValue
                                        && w.MinOrderQuantity.Value >= value,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        w.MinOrderQuantity.HasValue
                                        && w.MinOrderQuantity.Value <= value,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        w.MinOrderQuantity.HasValue
                                        && w.MinOrderQuantity.Value == value
                            );
                            break;
                        case "isactive":
                            var flags = ParseBooleanFilterValues(values);
                            if (flags.Count == 1)
                            {
                                var isActive = flags[0];
                                query = query.Where(w => w.IsActive == isActive);
                            }
                            break;
                        case "producttype":
                            var productTypes = ParseIntFilterValues(values);
                            if (productTypes.Any())
                            {
                                query = query.Where(
                                    (w, dp, s, p, c, ls) =>
                                        productTypes.Contains(p.ProductType ?? dp.ProductType)
                                );
                            }
                            break;
                        case "updatedat":
                            query = ApplyWarehouseDateRangeFilter(
                                query,
                                values,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        w.UpdatedAt.HasValue && w.UpdatedAt.Value >= value,
                                value =>
                                    (w, dp, s, p, c, ls) =>
                                        w.UpdatedAt.HasValue && w.UpdatedAt.Value <= value
                            );
                            break;
                        case "createdat":
                            query = ApplyWarehouseDateRangeFilter(
                                query,
                                values,
                                value => (w, dp, s, p, c, ls) => w.CreatedAt >= value,
                                value => (w, dp, s, p, c, ls) => w.CreatedAt <= value
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
                        ? query.OrderBy((w, dp, s, p, c, ls) => p.ProductName, OrderByType.Desc)
                        : query.OrderBy((w, dp, s, p, c, ls) => dp.ProductName, OrderByType.Asc);
                else if (sort == "nameen")
                    query = orderDesc
                        ? query.OrderBy((w, dp, s, p, c, ls) => p.EnglishName, OrderByType.Desc)
                        : query.OrderBy((w, dp, s, p, c, ls) => p.EnglishName, OrderByType.Asc);
                else if (sort == "itemnumber")
                    query = orderDesc
                        ? query.OrderBy((w, dp, s, p, c, ls) => p.ItemNumber, OrderByType.Desc)
                        : query.OrderBy((w, dp, s, p, c, ls) => p.ItemNumber, OrderByType.Asc);
                else if (sort == "barcode")
                    query = orderDesc
                        ? query.OrderBy((w, dp, s, p, c, ls) => p.Barcode, OrderByType.Desc)
                        : query.OrderBy((w, dp, s, p, c, ls) => p.Barcode, OrderByType.Asc);
                else if (sort == "categoryname")
                    query = orderDesc
                        ? query.OrderBy((w, dp, s, p, c, ls) => c.CategoryName, OrderByType.Desc)
                        : query.OrderBy((w, dp, s, p, c, ls) => c.CategoryName, OrderByType.Asc);
                else if (sort == "suppliername")
                    query = orderDesc
                        ? query.OrderBy((w, dp, s, p, c, ls) => s.SupplierName, OrderByType.Desc)
                        : query.OrderBy((w, dp, s, p, c, ls) => s.SupplierName, OrderByType.Asc);
                else if (sort == "suppliercode")
                    query = orderDesc
                        ? query.OrderBy((w, dp, s, p, c, ls) => s.SupplierCode, OrderByType.Desc)
                        : query.OrderBy((w, dp, s, p, c, ls) => s.SupplierCode, OrderByType.Asc);
                else if (sort == "domesticsuppliername")
                    query = orderDesc
                        ? query.OrderBy((w, dp, s, p, c, ls) => s.SupplierName, OrderByType.Desc)
                        : query.OrderBy((w, dp, s, p, c, ls) => s.SupplierName, OrderByType.Asc);
                else if (sort == "domesticsuppliercode")
                    query = orderDesc
                        ? query.OrderBy((w, dp, s, p, c, ls) => s.SupplierCode, OrderByType.Desc)
                        : query.OrderBy((w, dp, s, p, c, ls) => s.SupplierCode, OrderByType.Asc);
                else if (sort == "localsuppliercode")
                    query = orderDesc
                        ? query.OrderBy(
                            (w, dp, s, p, c, ls) => p.LocalSupplierCode,
                            OrderByType.Desc
                        )
                        : query.OrderBy(
                            (w, dp, s, p, c, ls) => p.LocalSupplierCode,
                            OrderByType.Asc
                        );
                else if (sort == "localsuppliername")
                    query = orderDesc
                        ? query.OrderBy((w, dp, s, p, c, ls) => ls.Name, OrderByType.Desc)
                        : query.OrderBy((w, dp, s, p, c, ls) => ls.Name, OrderByType.Asc);
                else if (sort == "domesticprice")
                    query = orderDesc
                        ? query.OrderBy(
                            (w, dp, s, p, c, ls) =>
                                SqlFunc.IsNull(w.DomesticPrice, dp.DomesticPrice),
                            OrderByType.Desc
                        )
                            .OrderBy(
                                (w, dp, s, p, c, ls) => w.ProductCode,
                                OrderByType.Asc
                            )
                        : query.OrderBy(
                            (w, dp, s, p, c, ls) =>
                                SqlFunc.IsNull(w.DomesticPrice, dp.DomesticPrice),
                            OrderByType.Asc
                        )
                            .OrderBy(
                                (w, dp, s, p, c, ls) => w.ProductCode,
                                OrderByType.Asc
                            );
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

            var total = await MeasureWarehouseProductTableStageAsync(
                "count",
                totalStopwatch,
                timings,
                requestSnapshot,
                elapsedMs => timings.CountMs = elapsedMs,
                () => query.Clone().CountAsync()
            );

            var pageProductCodes = await MeasureWarehouseProductTableStageAsync(
                "page",
                totalStopwatch,
                timings,
                requestSnapshot,
                elapsedMs => timings.PageMs = elapsedMs,
                () =>
                    query
                        .Clone()
                        .Select((w, dp, s, p, c, ls) => w.ProductCode)
                        .Skip((request.Page - 1) * request.PageSize)
                        .Take(request.PageSize)
                        .ToListAsync()
            );

            if (!pageProductCodes.Any())
            {
                resp.Items = new List<WarehouseProductReactListDto>();
                resp.Total = total;
                LogWarehouseProductTablePerformance(
                    requestSnapshot,
                    timings.Snapshot(totalStopwatch.ElapsedMilliseconds),
                    total,
                    0
                );
                return resp;
            }

            var pagePickingLocations = await MeasureWarehouseProductTableStageAsync(
                "location",
                totalStopwatch,
                timings,
                requestSnapshot,
                elapsedMs => timings.LocationMs = elapsedMs,
                () =>
                    _context
                        .Db.Queryable<ProductLocation>()
                        .InnerJoin<Location>((pl, l) => pl.LocationGuid == l.LocationGuid)
                        .Where(
                            (pl, l) =>
                                pl.ProductCode != null
                                && pageProductCodes.Contains(pl.ProductCode)
                                && !pl.IsDeleted
                                && !l.IsDeleted
                                && l.LocationType == PickingLocationType
                        )
                        .Select(
                            (pl, l) =>
                                new
                                {
                                    ProductCode = pl.ProductCode!,
                                    LocationCode = l.LocationCode,
                                    LocationBarcode = l.LocationBarcode,
                                }
                        )
                        .ToListAsync()
            );

            var rows = await MeasureWarehouseProductTableStageAsync(
                "rows",
                totalStopwatch,
                timings,
                requestSnapshot,
                elapsedMs => timings.RowsMs = elapsedMs,
                () =>
                    _context
                        .Db.Queryable<WarehouseProduct>()
                        .LeftJoin<DomesticProduct>(
                            (w, dp) => dp.ProductCode == w.ProductCode && !dp.IsDeleted
                        )
                        .LeftJoin<ChinaSupplier>(
                            (w, dp, s) => dp.SupplierCode == s.SupplierCode && !s.IsDeleted
                        )
                        .InnerJoin<Product>(
                            (w, dp, s, p) => p.ProductCode == w.ProductCode && !p.IsDeleted
                        )
                        .LeftJoin<WarehouseCategory>(
                            (w, dp, s, p, c) =>
                                p.WarehouseCategoryGUID == c.CategoryGUID && !c.IsDeleted
                        )
                        .LeftJoin<HBLocalSupplier>(
                            (w, dp, s, p, c, ls) =>
                                p.LocalSupplierCode == ls.LocalSupplierCode && !ls.IsDeleted
                        )
                        .Where(w => !w.IsDeleted && pageProductCodes.Contains(w.ProductCode))
                        .Select(
                            (w, dp, s, p, c, ls) =>
                                new
                                {
                                    ProductCode = w.ProductCode,
                                    ProductName = p.ProductName,
                                    EnglishName = p.EnglishName,
                                    ItemNumber = p.ItemNumber,
                                    Barcode = p.Barcode,
                                    CategoryName = c.CategoryName,
                                    SupplierName = s.SupplierName,
                                    SupplierCode = s.SupplierCode,
                                    DomesticSupplierName = s.SupplierName,
                                    DomesticSupplierCode = s.SupplierCode,
                                    LocalSupplierCode = p.LocalSupplierCode,
                                    LocalSupplierName = ls.Name ?? p.LocalSupplierCode,
                                    // 列表返回值与国内价筛选、排序保持同一兜底语义。
                                    DomesticPrice = SqlFunc.IsNull(
                                        w.DomesticPrice,
                                        dp.DomesticPrice
                                    ),
                                    OEMPrice = w.OEMPrice,
                                    ImportPrice = w.ImportPrice,
                                    WarehouseVolume = w.Volume,
                                    DomesticUnitVolume = dp.UnitVolume,
                                    DomesticPackingQuantity = dp.PackingQuantity,
                                    WarehousePackingQuantity = w.PackingQuantity,
                                    MinOrderQuantity = w.MinOrderQuantity,
                                    IsActive = w.IsActive,
                                    CreatedAt = w.CreatedAt,
                                    UpdatedAt = w.UpdatedAt,
                                    UpdatedBy = w.UpdatedBy,
                                    ProductImage = p.ProductImage,
                                    ProductType = p.ProductType ?? dp.ProductType,
                                }
                        )
                        .ToListAsync()
            );

            var items = MeasureWarehouseProductTableStage(
                "map",
                totalStopwatch,
                timings,
                requestSnapshot,
                elapsedMs => timings.MapMs = elapsedMs,
                () =>
                {
                    var pageOrderMap = pageProductCodes
                        .Select((code, index) => new { code, index })
                        .ToDictionary(x => x.code, x => x.index);

                    // 货位是一对多关系，分页后再聚合，避免主查询 count/page 被货位行数放大。
                    var pickingLocationMap = pagePickingLocations
                        .GroupBy(x => x.ProductCode, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            group => group.Key,
                            group => new
                            {
                                Codes = group
                                    .Select(x => x.LocationCode)
                                    .Where(x => !string.IsNullOrWhiteSpace(x))
                                    .Select(x => x!)
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .OrderBy(x => x)
                                    .ToList(),
                                Barcodes = group
                                    .Select(x => x.LocationBarcode)
                                    .Where(x => !string.IsNullOrWhiteSpace(x))
                                    .Select(x => x!)
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .OrderBy(x => x)
                                    .ToList(),
                            },
                            StringComparer.OrdinalIgnoreCase
                        );

                    var mappedItems = rows.OrderBy(row =>
                            pageOrderMap.TryGetValue(row.ProductCode, out var order)
                                ? order
                                : int.MaxValue
                        )
                        .Select(row => new WarehouseProductReactListDto
                        {
                            ProductCode = row.ProductCode,
                            ProductName = row.ProductName,
                            EnglishName = row.EnglishName,
                            ItemNumber = row.ItemNumber,
                            Barcode = row.Barcode,
                            CategoryName = row.CategoryName,
                            SupplierName = row.SupplierName,
                            SupplierCode = row.SupplierCode,
                            DomesticSupplierName = row.DomesticSupplierName,
                            DomesticSupplierCode = row.DomesticSupplierCode,
                            LocalSupplierCode = row.LocalSupplierCode,
                            LocalSupplierName = row.LocalSupplierName,
                            DomesticPrice = row.DomesticPrice,
                            OEMPrice = row.OEMPrice,
                            ImportPrice = row.ImportPrice,
                            Volume = row.WarehouseVolume ?? row.DomesticUnitVolume,
                            IsVolumeFallback =
                                !row.WarehouseVolume.HasValue && row.DomesticUnitVolume.HasValue,
                            PackingQuantity =
                                row.DomesticPackingQuantity ?? row.WarehousePackingQuantity,
                            IsPackingQuantityFallback =
                                !row.DomesticPackingQuantity.HasValue
                                && row.WarehousePackingQuantity.HasValue,
                            MinOrderQuantity = row.MinOrderQuantity,
                            IsActive = row.IsActive,
                            CreatedAt = row.CreatedAt,
                            UpdatedAt = row.UpdatedAt,
                            // 历史记录没有更新人时不能借用创建人，前端会按空值显示“--”。
                            UpdatedBy = string.IsNullOrWhiteSpace(row.UpdatedBy)
                                ? null
                                : row.UpdatedBy,
                            ProductImage = row.ProductImage,
                            ProductType = row.ProductType,
                            LocationCodes = pickingLocationMap.TryGetValue(
                                row.ProductCode,
                                out var pickingLocation
                            )
                                ? pickingLocation.Codes
                                : new List<string>(),
                            LocationBarcodes = pickingLocationMap.TryGetValue(
                                row.ProductCode,
                                out var pickingLocationForBarcode
                            )
                                ? pickingLocationForBarcode.Barcodes
                                : new List<string>(),
                        })
                        .ToList();

                    // 查询结束后再在内存中补全图片 URL（避免 SqlSugar 翻译自定义方法）。
                    foreach (var dto in mappedItems)
                    {
                        dto.ProductImage = ProductImageUrlHelper.EnsureImageUrl(
                            dto.ProductImage,
                            dto.ItemNumber ?? dto.ProductCode
                        );
                    }

                    return mappedItems;
                }
            );

            resp.Items = items;
            resp.Total = total;
            LogWarehouseProductTablePerformance(
                requestSnapshot,
                timings.Snapshot(totalStopwatch.ElapsedMilliseconds),
                total,
                items.Count
            );
            return resp;
        }

        private ISugarQueryable<WarehouseProductCodeSearchCandidate> BuildWarehouseCodeSearchCandidateQuery(
            string keyword
        )
        {
            var hbPrefixedKeyword = keyword.StartsWith("HB", StringComparison.OrdinalIgnoreCase)
                ? keyword
                : $"HB{keyword}";
            var isSqlServer = _context.Db.CurrentConnectionConfig.DbType == DbType.SqlServer;

            // SQL Server 的 WarehouseProduct/Location/ProductLocation 为 varchar；参数显式转成 varchar，
            // 避免优化器把这些索引列隐式转成 nvarchar 后退化为扫描。
            var warehouseProductCodeQuery = _context
                .Db.Queryable<WarehouseProduct>()
                .WhereIF(
                    isSqlServer,
                    w =>
                        !w.IsDeleted
                        && w.ProductCode != null
                        && (
                            w.ProductCode == SqlFunc.ToVarchar(keyword)
                            || w.ProductCode.StartsWith(SqlFunc.ToVarchar(keyword))
                        )
                )
                .WhereIF(
                    !isSqlServer,
                    w =>
                        !w.IsDeleted
                        && w.ProductCode != null
                        && (w.ProductCode == keyword || w.ProductCode.StartsWith(keyword))
                )
                .Select(w => new WarehouseProductCodeSearchCandidate
                {
                    ProductCode = w.ProductCode,
                });

            var itemNumberQuery = _context
                .Db.Queryable<Product>()
                .Where(p =>
                    !p.IsDeleted
                    && p.ProductCode != null
                    && p.ItemNumber != null
                    && (p.ItemNumber == keyword || p.ItemNumber.StartsWith(keyword))
                )
                .Select(p => new WarehouseProductCodeSearchCandidate
                {
                    ProductCode = p.ProductCode!,
                });

            var hbItemNumberQuery = _context
                .Db.Queryable<Product>()
                .Where(p =>
                    !p.IsDeleted
                    && p.ProductCode != null
                    && p.ItemNumber != null
                    && (
                        p.ItemNumber == hbPrefixedKeyword
                        || p.ItemNumber.StartsWith(hbPrefixedKeyword)
                    )
                )
                .Select(p => new WarehouseProductCodeSearchCandidate
                {
                    ProductCode = p.ProductCode!,
                });

            var barcodeQuery = _context
                .Db.Queryable<Product>()
                .Where(p =>
                    !p.IsDeleted
                    && p.ProductCode != null
                    && p.Barcode != null
                    && (p.Barcode == keyword || p.Barcode.StartsWith(keyword))
                )
                .Select(p => new WarehouseProductCodeSearchCandidate
                {
                    ProductCode = p.ProductCode!,
                });

            var localSupplierCodeQuery = _context
                .Db.Queryable<Product>()
                .Where(p =>
                    !p.IsDeleted
                    && p.ProductCode != null
                    && p.LocalSupplierCode != null
                    && (
                        p.LocalSupplierCode == keyword
                        || p.LocalSupplierCode.StartsWith(keyword)
                    )
                )
                .Select(p => new WarehouseProductCodeSearchCandidate
                {
                    ProductCode = p.ProductCode!,
                });

            var domesticSupplierCodeQuery = _context
                .Db.Queryable<ChinaSupplier>()
                .InnerJoin<DomesticProduct>(
                    (supplier, domesticProduct) =>
                        supplier.SupplierCode == domesticProduct.SupplierCode
                        && !domesticProduct.IsDeleted
                )
                .WhereIF(
                    isSqlServer,
                    (supplier, domesticProduct) =>
                        !supplier.IsDeleted
                        && supplier.SupplierCode != null
                        && domesticProduct.ProductCode != null
                        && (
                            supplier.SupplierCode == SqlFunc.ToVarchar(keyword)
                            || supplier.SupplierCode.StartsWith(SqlFunc.ToVarchar(keyword))
                        )
                )
                .WhereIF(
                    !isSqlServer,
                    (supplier, domesticProduct) =>
                        !supplier.IsDeleted
                        && supplier.SupplierCode != null
                        && domesticProduct.ProductCode != null
                        && (
                            supplier.SupplierCode == keyword
                            || supplier.SupplierCode.StartsWith(keyword)
                        )
                )
                .Select(
                    (supplier, domesticProduct) =>
                        new WarehouseProductCodeSearchCandidate
                        {
                            ProductCode = domesticProduct.ProductCode!,
                        }
                );

            var pickingLocationCodeSource = _context
                .Db.Queryable<Location>()
                .InnerJoin<ProductLocation>(
                    (location, productLocation) =>
                        location.LocationGuid == productLocation.LocationGuid
                        && !productLocation.IsDeleted
                )
                .WhereIF(
                    isSqlServer,
                    (location, productLocation) =>
                        !location.IsDeleted
                        && location.LocationType == PickingLocationType
                        && location.LocationCode != null
                        && productLocation.ProductCode != null
                        && (
                            location.LocationCode == SqlFunc.ToVarchar(keyword)
                            || location.LocationCode.StartsWith(SqlFunc.ToVarchar(keyword))
                        )
                )
                .WhereIF(
                    !isSqlServer,
                    (location, productLocation) =>
                        !location.IsDeleted
                        && location.LocationType == PickingLocationType
                        && location.LocationCode != null
                        && productLocation.ProductCode != null
                        && (
                            location.LocationCode == keyword
                            || location.LocationCode.StartsWith(keyword)
                        )
                );
            var pickingLocationCodeQuery = pickingLocationCodeSource
                .Select(
                    (location, productLocation) =>
                        new WarehouseProductCodeSearchCandidate
                        {
                            ProductCode = productLocation.ProductCode!,
                        }
                );

            var pickingLocationBarcodeSource = _context
                .Db.Queryable<Location>()
                .InnerJoin<ProductLocation>(
                    (location, productLocation) =>
                        location.LocationGuid == productLocation.LocationGuid
                        && !productLocation.IsDeleted
                )
                .WhereIF(
                    isSqlServer,
                    (location, productLocation) =>
                        !location.IsDeleted
                        && location.LocationType == PickingLocationType
                        && location.LocationBarcode != null
                        && productLocation.ProductCode != null
                        && (
                            location.LocationBarcode == SqlFunc.ToVarchar(keyword)
                            || location.LocationBarcode.StartsWith(SqlFunc.ToVarchar(keyword))
                        )
                )
                .WhereIF(
                    !isSqlServer,
                    (location, productLocation) =>
                        !location.IsDeleted
                        && location.LocationType == PickingLocationType
                        && location.LocationBarcode != null
                        && productLocation.ProductCode != null
                        && (
                            location.LocationBarcode == keyword
                            || location.LocationBarcode.StartsWith(keyword)
                        )
                );
            var pickingLocationBarcodeQuery = pickingLocationBarcodeSource
                .Select(
                    (location, productLocation) =>
                        new WarehouseProductCodeSearchCandidate
                        {
                            ProductCode = productLocation.ProductCode!,
                        }
                );

            var unionQuery = _context
                .Db.Union(
                    warehouseProductCodeQuery,
                    itemNumberQuery,
                    hbItemNumberQuery,
                    barcodeQuery,
                    localSupplierCodeQuery,
                    domesticSupplierCodeQuery,
                    pickingLocationCodeQuery,
                    pickingLocationBarcodeQuery
                )
                .MergeTable();

            // UNION 受 nvarchar 商品列影响会提升结果类型；SQL Server 最外层统一转回 varchar，
            // 让候选集与 WarehouseProduct.ProductCode 连接时不再转换仓库索引列。
            return isSqlServer
                ? unionQuery
                    .Select(candidate => new WarehouseProductCodeSearchCandidate
                    {
                        ProductCode = SqlFunc.ToVarchar(candidate.ProductCode),
                    })
                    .MergeTable()
                : unionQuery;
        }

        private static T MeasureWarehouseProductTableStage<T>(
            string stage,
            Stopwatch totalStopwatch,
            WarehouseProductTableTimings timings,
            WarehouseProductTableRequestSnapshot request,
            Action<long> setElapsed,
            Func<T> action
        )
        {
            var stageStopwatch = Stopwatch.StartNew();
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                setElapsed(stageStopwatch.ElapsedMilliseconds);
                throw new WarehouseProductTableQueryException(
                    stage,
                    timings.Snapshot(totalStopwatch.ElapsedMilliseconds),
                    ex,
                    request
                );
            }
            finally
            {
                setElapsed(stageStopwatch.ElapsedMilliseconds);
            }
        }

        private static async Task<T> MeasureWarehouseProductTableStageAsync<T>(
            string stage,
            Stopwatch totalStopwatch,
            WarehouseProductTableTimings timings,
            WarehouseProductTableRequestSnapshot request,
            Action<long> setElapsed,
            Func<Task<T>> action
        )
        {
            var stageStopwatch = Stopwatch.StartNew();
            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                setElapsed(stageStopwatch.ElapsedMilliseconds);
                throw new WarehouseProductTableQueryException(
                    stage,
                    timings.Snapshot(totalStopwatch.ElapsedMilliseconds),
                    ex,
                    request
                );
            }
            finally
            {
                setElapsed(stageStopwatch.ElapsedMilliseconds);
            }
        }

        private static WarehouseProductTableRequestSnapshot CreateWarehouseProductTableRequestSnapshot(
            ReactTableRequestDto request,
            string? keyword,
            bool isCodeLikeKeyword
        )
        {
            var normalizedSort = request.SortBy?.Trim().ToLowerInvariant();
            var safeSort = normalizedSort switch
            {
                "productcode" or "itemnumber" or "barcode" or "productname" or "name"
                    or "nameen" or "categoryname" or "suppliername" or "domesticsuppliername"
                    or "localsuppliercode" or "localsuppliername" or "domesticprice"
                    or "oemprice" or "importprice" or "packingquantity" or "volume"
                    or "minorderquantity" or "isactive" or "producttype" or "createdat"
                    or "updatedat" => normalizedSort,
                _ => "default",
            };
            var safeSortOrder = request.SortBy == null
                ? "descend"
                : string.Equals(request.SortOrder, "descend", StringComparison.OrdinalIgnoreCase)
                    ? "descend"
                    : "ascend";

            return new WarehouseProductTableRequestSnapshot(
                request.Page,
                request.PageSize,
                request.CategoryGuids
                    ?.Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() ?? 0,
                request.Filters?.Count(pair =>
                    pair.Value?.Any(value => !string.IsNullOrWhiteSpace(value)) == true
                ) ?? 0,
                keyword == null ? "none" : isCodeLikeKeyword ? "code" : "text",
                keyword?.Length ?? 0,
                safeSort,
                safeSortOrder
            );
        }

        private void LogWarehouseProductTablePerformance(
            WarehouseProductTableRequestSnapshot request,
            WarehouseProductTableTimingSnapshot timings,
            int total,
            int itemCount
        )
        {
            const string message =
                "[warehouse-product-table-perf] stage=done pageNumber={PageNumber} pageSize={PageSize} categoryCount={CategoryCount} filterCount={FilterCount} keywordType={KeywordType} keywordLength={KeywordLength} sortBy={SortBy} sortOrder={SortOrder} total={Total} itemCount={ItemCount} candidateMs={CandidateMs} countMs={CountMs} pageMs={PageMs} locationMs={LocationMs} rowsMs={RowsMs} mapMs={MapMs} totalMs={TotalMs}";

            if (timings.TotalMs >= 2000)
            {
                _logger.LogWarning(
                    message,
                    request.PageNumber,
                    request.PageSize,
                    request.CategoryCount,
                    request.FilterCount,
                    request.KeywordType,
                    request.KeywordLength,
                    request.SortBy,
                    request.SortOrder,
                    total,
                    itemCount,
                    timings.CandidateMs,
                    timings.CountMs,
                    timings.PageMs,
                    timings.LocationMs,
                    timings.RowsMs,
                    timings.MapMs,
                    timings.TotalMs
                );
                return;
            }

            _logger.LogInformation(
                message,
                request.PageNumber,
                request.PageSize,
                request.CategoryCount,
                request.FilterCount,
                request.KeywordType,
                request.KeywordLength,
                request.SortBy,
                request.SortOrder,
                total,
                itemCount,
                timings.CandidateMs,
                timings.CountMs,
                timings.PageMs,
                timings.LocationMs,
                timings.RowsMs,
                timings.MapMs,
                timings.TotalMs
            );
        }

        private static bool IsWarehouseCodeLikeKeyword(string keyword)
        {
            // ponytail: 纯字母词可能是英文商品名（如 PEARL），不按代码型处理；纯字母代码用列过滤更明确。
            return keyword.Length >= 3
                && !keyword.Any(char.IsWhiteSpace)
                && keyword.Any(ch => char.IsDigit(ch) || ch == '-' || ch == '_' || ch == '/');
        }

        private static ISugarQueryable<
            WarehouseProduct,
            DomesticProduct,
            ChinaSupplier,
            Product,
            WarehouseCategory,
            HBLocalSupplier
        > ApplyPickingLocationTextMatchFilter(
            ISugarQueryable<
                WarehouseProduct,
                DomesticProduct,
                ChinaSupplier,
                Product,
                WarehouseCategory,
                HBLocalSupplier
            > query,
            IEnumerable<string> values
        )
        {
            var tokens = ParseTextMatchFilterTokens(values);
            if (!tokens.Any())
            {
                return query;
            }

            var expression = Expressionable.Create<
                WarehouseProduct,
                DomesticProduct,
                ChinaSupplier,
                Product,
                WarehouseCategory,
                HBLocalSupplier
            >();
            foreach (var token in tokens)
            {
                expression = expression
                    .Or(BuildPickingLocationCodePredicate(token.Mode, token.Value))
                    .Or(BuildPickingLocationBarcodePredicate(token.Mode, token.Value));
            }

            return query.Where(expression.ToExpression());
        }

        private static Expression<
            Func<
                WarehouseProduct,
                DomesticProduct,
                ChinaSupplier,
                Product,
                WarehouseCategory,
                HBLocalSupplier,
                bool
            >
        > BuildPickingLocationCodePredicate(string mode, string value)
        {
            return mode switch
            {
                "eq" => (w, dp, s, p, c, ls) =>
                    w.ProductCode != null
                    && SqlFunc.Subqueryable<Location>()
                        .InnerJoin<ProductLocation>((l, pl) => l.LocationGuid == pl.LocationGuid)
                        .Where(
                            (l, pl) =>
                                !l.IsDeleted
                                && l.LocationType == PickingLocationType
                                && l.LocationCode != null
                                && l.LocationCode == value
                                && !pl.IsDeleted
                                && pl.ProductCode == w.ProductCode
                        )
                        .Any(),
                "starts" => (w, dp, s, p, c, ls) =>
                    w.ProductCode != null
                    && SqlFunc.Subqueryable<Location>()
                        .InnerJoin<ProductLocation>((l, pl) => l.LocationGuid == pl.LocationGuid)
                        .Where(
                            (l, pl) =>
                                !l.IsDeleted
                                && l.LocationType == PickingLocationType
                                && l.LocationCode != null
                                && l.LocationCode.StartsWith(value)
                                && !pl.IsDeleted
                                && pl.ProductCode == w.ProductCode
                        )
                        .Any(),
                "ends" => (w, dp, s, p, c, ls) =>
                    w.ProductCode != null
                    && SqlFunc.Subqueryable<Location>()
                        .InnerJoin<ProductLocation>((l, pl) => l.LocationGuid == pl.LocationGuid)
                        .Where(
                            (l, pl) =>
                                !l.IsDeleted
                                && l.LocationType == PickingLocationType
                                && l.LocationCode != null
                                && l.LocationCode.EndsWith(value)
                                && !pl.IsDeleted
                                && pl.ProductCode == w.ProductCode
                        )
                        .Any(),
                _ => (w, dp, s, p, c, ls) =>
                    w.ProductCode != null
                    && SqlFunc.Subqueryable<Location>()
                        .InnerJoin<ProductLocation>((l, pl) => l.LocationGuid == pl.LocationGuid)
                        .Where(
                            (l, pl) =>
                                !l.IsDeleted
                                && l.LocationType == PickingLocationType
                                && l.LocationCode != null
                                && l.LocationCode.Contains(value)
                                && !pl.IsDeleted
                                && pl.ProductCode == w.ProductCode
                        )
                        .Any(),
            };
        }

        private static Expression<
            Func<
                WarehouseProduct,
                DomesticProduct,
                ChinaSupplier,
                Product,
                WarehouseCategory,
                HBLocalSupplier,
                bool
            >
        > BuildPickingLocationBarcodePredicate(string mode, string value)
        {
            return mode switch
            {
                "eq" => (w, dp, s, p, c, ls) =>
                    w.ProductCode != null
                    && SqlFunc.Subqueryable<Location>()
                        .InnerJoin<ProductLocation>((l, pl) => l.LocationGuid == pl.LocationGuid)
                        .Where(
                            (l, pl) =>
                                !l.IsDeleted
                                && l.LocationType == PickingLocationType
                                && l.LocationBarcode != null
                                && l.LocationBarcode == value
                                && !pl.IsDeleted
                                && pl.ProductCode == w.ProductCode
                        )
                        .Any(),
                "starts" => (w, dp, s, p, c, ls) =>
                    w.ProductCode != null
                    && SqlFunc.Subqueryable<Location>()
                        .InnerJoin<ProductLocation>((l, pl) => l.LocationGuid == pl.LocationGuid)
                        .Where(
                            (l, pl) =>
                                !l.IsDeleted
                                && l.LocationType == PickingLocationType
                                && l.LocationBarcode != null
                                && l.LocationBarcode.StartsWith(value)
                                && !pl.IsDeleted
                                && pl.ProductCode == w.ProductCode
                        )
                        .Any(),
                "ends" => (w, dp, s, p, c, ls) =>
                    w.ProductCode != null
                    && SqlFunc.Subqueryable<Location>()
                        .InnerJoin<ProductLocation>((l, pl) => l.LocationGuid == pl.LocationGuid)
                        .Where(
                            (l, pl) =>
                                !l.IsDeleted
                                && l.LocationType == PickingLocationType
                                && l.LocationBarcode != null
                                && l.LocationBarcode.EndsWith(value)
                                && !pl.IsDeleted
                                && pl.ProductCode == w.ProductCode
                        )
                        .Any(),
                _ => (w, dp, s, p, c, ls) =>
                    w.ProductCode != null
                    && SqlFunc.Subqueryable<Location>()
                        .InnerJoin<ProductLocation>((l, pl) => l.LocationGuid == pl.LocationGuid)
                        .Where(
                            (l, pl) =>
                                !l.IsDeleted
                                && l.LocationType == PickingLocationType
                                && l.LocationBarcode != null
                                && l.LocationBarcode.Contains(value)
                                && !pl.IsDeleted
                                && pl.ProductCode == w.ProductCode
                        )
                        .Any(),
            };
        }

        private static ISugarQueryable<
            WarehouseProduct,
            DomesticProduct,
            ChinaSupplier,
            Product,
            WarehouseCategory,
            HBLocalSupplier
        > ApplyWarehouseTextMatchFilter(
            ISugarQueryable<
                WarehouseProduct,
                DomesticProduct,
                ChinaSupplier,
                Product,
                WarehouseCategory,
                HBLocalSupplier
            > query,
            IEnumerable<string> values,
            Func<
                string,
                Expression<
                    Func<
                        WarehouseProduct,
                        DomesticProduct,
                        ChinaSupplier,
                        Product,
                        WarehouseCategory,
                        HBLocalSupplier,
                        bool
                    >
                >
            > containsFactory,
            Func<
                string,
                Expression<
                    Func<
                        WarehouseProduct,
                        DomesticProduct,
                        ChinaSupplier,
                        Product,
                        WarehouseCategory,
                        HBLocalSupplier,
                        bool
                    >
                >
            > equalsFactory,
            Func<
                string,
                Expression<
                    Func<
                        WarehouseProduct,
                        DomesticProduct,
                        ChinaSupplier,
                        Product,
                        WarehouseCategory,
                        HBLocalSupplier,
                        bool
                    >
                >
            > startsFactory,
            Func<
                string,
                Expression<
                    Func<
                        WarehouseProduct,
                        DomesticProduct,
                        ChinaSupplier,
                        Product,
                        WarehouseCategory,
                        HBLocalSupplier,
                        bool
                    >
                >
            > endsFactory
        )
        {
            var tokens = ParseTextMatchFilterTokens(values);
            if (!tokens.Any())
            {
                return query;
            }

            var expression = Expressionable.Create<
                WarehouseProduct,
                DomesticProduct,
                ChinaSupplier,
                Product,
                WarehouseCategory,
                HBLocalSupplier
            >();
            foreach (var token in tokens)
            {
                var currentValue = token.Value;
                expression = token.Mode switch
                {
                    "eq" => expression.Or(equalsFactory(currentValue)),
                    "starts" => expression.Or(startsFactory(currentValue)),
                    "ends" => expression.Or(endsFactory(currentValue)),
                    _ => expression.Or(containsFactory(currentValue)),
                };
            }

            return query.Where(expression.ToExpression());
        }

        private static List<(string Mode, string Value)> ParseTextMatchFilterTokens(
            IEnumerable<string> values
        )
        {
            var tokens = new List<(string Mode, string Value)>();
            foreach (var rawValue in values)
            {
                var value = rawValue?.Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (TryParseFilterToken(value, "contains", out var containsToken, requireNamespace: true))
                {
                    tokens.Add(("contains", containsToken));
                    continue;
                }
                if (TryParseFilterToken(value, "eq", out var equalsToken, requireNamespace: true))
                {
                    tokens.Add(("eq", equalsToken));
                    continue;
                }
                if (TryParseFilterToken(value, "starts", out var startsToken, requireNamespace: true))
                {
                    tokens.Add(("starts", startsToken));
                    continue;
                }
                if (TryParseFilterToken(value, "ends", out var endsToken, requireNamespace: true))
                {
                    tokens.Add(("ends", endsToken));
                    continue;
                }

                // 兼容旧调用方：无模式前缀的文本值按 contains 处理。
                tokens.Add(("contains", value));
            }

            return tokens.Distinct().ToList();
        }

        private static List<string> NormalizeWarehouseExactTextFilterValues(IEnumerable<string> values)
        {
            // 精确 code 筛选保持列侧原值，避免 ToLower/Contains 包列导致索引不可用。
            return values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct()
                .ToList();
        }

        private static ISugarQueryable<
            WarehouseProduct,
            DomesticProduct,
            ChinaSupplier,
            Product,
            WarehouseCategory,
            HBLocalSupplier
        > ApplyWarehouseDecimalRangeFilter(
            ISugarQueryable<
                WarehouseProduct,
                DomesticProduct,
                ChinaSupplier,
                Product,
                WarehouseCategory,
                HBLocalSupplier
            > query,
            IEnumerable<string> values,
            Func<
                decimal,
                Expression<
                    Func<
                        WarehouseProduct,
                        DomesticProduct,
                        ChinaSupplier,
                        Product,
                        WarehouseCategory,
                        HBLocalSupplier,
                        bool
                    >
                >
            > minimumFactory,
            Func<
                decimal,
                Expression<
                    Func<
                        WarehouseProduct,
                        DomesticProduct,
                        ChinaSupplier,
                        Product,
                        WarehouseCategory,
                        HBLocalSupplier,
                        bool
                    >
                >
            > maximumFactory,
            Func<
                decimal,
                Expression<
                    Func<
                        WarehouseProduct,
                        DomesticProduct,
                        ChinaSupplier,
                        Product,
                        WarehouseCategory,
                        HBLocalSupplier,
                        bool
                    >
                >
            > equalsFactory
        )
        {
            var (minimum, maximum, equals) = ParseDecimalRangeTokens(values);
            var expression = Expressionable.Create<
                WarehouseProduct,
                DomesticProduct,
                ChinaSupplier,
                Product,
                WarehouseCategory,
                HBLocalSupplier
            >();
            var hasCondition = false;

            if (minimum.HasValue || maximum.HasValue)
            {
                var rangeExpression = Expressionable.Create<
                    WarehouseProduct,
                    DomesticProduct,
                    ChinaSupplier,
                    Product,
                    WarehouseCategory,
                    HBLocalSupplier
                >();
                if (minimum.HasValue)
                {
                    rangeExpression = rangeExpression.And(minimumFactory(minimum.Value));
                }
                if (maximum.HasValue)
                {
                    rangeExpression = rangeExpression.And(maximumFactory(maximum.Value));
                }

                expression = expression.Or(rangeExpression.ToExpression());
                hasCondition = true;
            }

            foreach (var value in equals)
            {
                var currentValue = value;
                expression = expression.Or(equalsFactory(currentValue));
                hasCondition = true;
            }

            return hasCondition ? query.Where(expression.ToExpression()) : query;
        }

        private static ISugarQueryable<
            WarehouseProduct,
            DomesticProduct,
            ChinaSupplier,
            Product,
            WarehouseCategory,
            HBLocalSupplier
        > ApplyWarehouseIntRangeFilter(
            ISugarQueryable<
                WarehouseProduct,
                DomesticProduct,
                ChinaSupplier,
                Product,
                WarehouseCategory,
                HBLocalSupplier
            > query,
            IEnumerable<string> values,
            Func<
                int,
                Expression<
                    Func<
                        WarehouseProduct,
                        DomesticProduct,
                        ChinaSupplier,
                        Product,
                        WarehouseCategory,
                        HBLocalSupplier,
                        bool
                    >
                >
            > minimumFactory,
            Func<
                int,
                Expression<
                    Func<
                        WarehouseProduct,
                        DomesticProduct,
                        ChinaSupplier,
                        Product,
                        WarehouseCategory,
                        HBLocalSupplier,
                        bool
                    >
                >
            > maximumFactory,
            Func<
                int,
                Expression<
                    Func<
                        WarehouseProduct,
                        DomesticProduct,
                        ChinaSupplier,
                        Product,
                        WarehouseCategory,
                        HBLocalSupplier,
                        bool
                    >
                >
            > equalsFactory
        )
        {
            var (minimum, maximum, equals) = ParseIntRangeTokens(values);
            var expression = Expressionable.Create<
                WarehouseProduct,
                DomesticProduct,
                ChinaSupplier,
                Product,
                WarehouseCategory,
                HBLocalSupplier
            >();
            var hasCondition = false;

            if (minimum.HasValue || maximum.HasValue)
            {
                var rangeExpression = Expressionable.Create<
                    WarehouseProduct,
                    DomesticProduct,
                    ChinaSupplier,
                    Product,
                    WarehouseCategory,
                    HBLocalSupplier
                >();
                if (minimum.HasValue)
                {
                    rangeExpression = rangeExpression.And(minimumFactory(minimum.Value));
                }
                if (maximum.HasValue)
                {
                    rangeExpression = rangeExpression.And(maximumFactory(maximum.Value));
                }

                expression = expression.Or(rangeExpression.ToExpression());
                hasCondition = true;
            }

            foreach (var value in equals)
            {
                var currentValue = value;
                expression = expression.Or(equalsFactory(currentValue));
                hasCondition = true;
            }

            return hasCondition ? query.Where(expression.ToExpression()) : query;
        }

        private static ISugarQueryable<
            WarehouseProduct,
            DomesticProduct,
            ChinaSupplier,
            Product,
            WarehouseCategory,
            HBLocalSupplier
        > ApplyWarehouseDateRangeFilter(
            ISugarQueryable<
                WarehouseProduct,
                DomesticProduct,
                ChinaSupplier,
                Product,
                WarehouseCategory,
                HBLocalSupplier
            > query,
            IEnumerable<string> values,
            Func<
                DateTime,
                Expression<
                    Func<
                        WarehouseProduct,
                        DomesticProduct,
                        ChinaSupplier,
                        Product,
                        WarehouseCategory,
                        HBLocalSupplier,
                        bool
                    >
                >
            > startFactory,
            Func<
                DateTime,
                Expression<
                    Func<
                        WarehouseProduct,
                        DomesticProduct,
                        ChinaSupplier,
                        Product,
                        WarehouseCategory,
                        HBLocalSupplier,
                        bool
                    >
                >
            > endFactory
        )
        {
            var (startAt, endAt, equalRanges) = ParseDateRangeTokens(values);
            var expression = Expressionable.Create<
                WarehouseProduct,
                DomesticProduct,
                ChinaSupplier,
                Product,
                WarehouseCategory,
                HBLocalSupplier
            >();
            var hasCondition = false;

            if (startAt.HasValue || endAt.HasValue)
            {
                var rangeExpression = Expressionable.Create<
                    WarehouseProduct,
                    DomesticProduct,
                    ChinaSupplier,
                    Product,
                    WarehouseCategory,
                    HBLocalSupplier
                >();
                if (startAt.HasValue)
                {
                    rangeExpression = rangeExpression.And(startFactory(startAt.Value));
                }
                if (endAt.HasValue)
                {
                    rangeExpression = rangeExpression.And(endFactory(endAt.Value));
                }

                expression = expression.Or(rangeExpression.ToExpression());
                hasCondition = true;
            }

            foreach (var (start, end) in equalRanges)
            {
                var currentStart = start;
                var currentEnd = end;
                var equalExpression = Expressionable.Create<
                    WarehouseProduct,
                    DomesticProduct,
                    ChinaSupplier,
                    Product,
                    WarehouseCategory,
                    HBLocalSupplier
                >()
                    .And(startFactory(currentStart))
                    .And(endFactory(currentEnd));
                expression = expression.Or(equalExpression.ToExpression());
                hasCondition = true;
            }

            return hasCondition ? query.Where(expression.ToExpression()) : query;
        }

        private static (decimal? Minimum, decimal? Maximum, List<decimal> ExactValues) ParseDecimalRangeTokens(
            IEnumerable<string> values
        )
        {
            decimal? minimum = null;
            decimal? maximum = null;
            var equals = new List<decimal>();
            foreach (var rawValue in values)
            {
                var value = rawValue?.Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (TryParseFilterToken(value, "gte", out var minimumToken)
                    && decimal.TryParse(
                        minimumToken,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var parsedMinimum
                    ))
                {
                    minimum = parsedMinimum;
                    continue;
                }

                if (TryParseFilterToken(value, "lte", out var maximumToken)
                    && decimal.TryParse(
                        maximumToken,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var parsedMaximum
                    ))
                {
                    maximum = parsedMaximum;
                    continue;
                }

                if (TryParseFilterToken(value, "eq", out var equalToken, requireNamespace: true)
                    && decimal.TryParse(
                        equalToken,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var parsedTokenEqual
                    ))
                {
                    equals.Add(parsedTokenEqual);
                    continue;
                }

                if (
                    decimal.TryParse(
                        value,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var parsedEqual
                    )
                )
                {
                    equals.Add(parsedEqual);
                }
            }

            return (minimum, maximum, equals.Distinct().ToList());
        }

        private static (int? Minimum, int? Maximum, List<int> ExactValues) ParseIntRangeTokens(
            IEnumerable<string> values
        )
        {
            int? minimum = null;
            int? maximum = null;
            var equals = new List<int>();
            foreach (var rawValue in values)
            {
                var value = rawValue?.Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (TryParseFilterToken(value, "gte", out var minimumToken)
                    && int.TryParse(
                        minimumToken,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsedMinimum
                    ))
                {
                    minimum = parsedMinimum;
                    continue;
                }

                if (TryParseFilterToken(value, "lte", out var maximumToken)
                    && int.TryParse(
                        maximumToken,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsedMaximum
                    ))
                {
                    maximum = parsedMaximum;
                    continue;
                }

                if (TryParseFilterToken(value, "eq", out var equalToken, requireNamespace: true)
                    && int.TryParse(
                        equalToken,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsedTokenEqual
                    ))
                {
                    equals.Add(parsedTokenEqual);
                    continue;
                }

                if (
                    int.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsedEqual
                    )
                )
                {
                    equals.Add(parsedEqual);
                }
            }

            return (minimum, maximum, equals.Distinct().ToList());
        }

        private static (DateTime? StartAt, DateTime? EndAt, List<(DateTime StartAt, DateTime EndAt)> EqualRanges) ParseDateRangeTokens(
            IEnumerable<string> values
        )
        {
            DateTime? startAt = null;
            DateTime? endAt = null;
            var equalRanges = new List<(DateTime StartAt, DateTime EndAt)>();
            foreach (var rawValue in values)
            {
                var value = rawValue?.Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (TryParseFilterToken(value, "gte", out var startToken)
                    && TryParseFilterDate(startToken, out var parsedStart))
                {
                    startAt = parsedStart;
                    continue;
                }

                if (TryParseFilterToken(value, "lte", out var endToken)
                    && TryParseFilterDate(endToken, out var parsedEnd))
                {
                    endAt = NormalizeFilterEndDate(endToken, parsedEnd);
                    continue;
                }

                if (TryParseFilterToken(value, "eq", out var equalToken, requireNamespace: true)
                    && TryParseFilterDate(equalToken, out var parsedEqual))
                {
                    // 日期等于始终按自然日匹配，避免带时间值时只命中一个瞬间。
                    equalRanges.Add((
                        parsedEqual.Date,
                        parsedEqual.Date.AddDays(1).AddTicks(-1)
                    ));
                }
            }

            return (startAt, endAt, equalRanges.Distinct().ToList());
        }

        private static List<bool> ParseBooleanFilterValues(IEnumerable<string> values)
        {
            return values
                .Select(v => v?.Trim().ToLowerInvariant())
                .Where(v => !string.IsNullOrWhiteSpace(v) && v != "all")
                .Select(
                    v =>
                        v switch
                        {
                            "1" => (bool?)true,
                            "true" => true,
                            "0" => false,
                            "false" => false,
                            _ => null,
                        }
                )
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .Distinct()
                .ToList();
        }

        private static List<int> ParseIntFilterValues(IEnumerable<string> values)
        {
            return values
                .Select(v => v?.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(
                    v =>
                        int.TryParse(
                            v,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var parsed
                        )
                            ? (int?)parsed
                            : null
                )
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .Distinct()
                .ToList();
        }

        private static bool TryParseFilterToken(
            string value,
            string token,
            out string parsedValue,
            bool requireNamespace = false
        )
        {
            var prefix = requireNamespace ? $"__filter:{token}:" : $"{token}:";
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                parsedValue = value.Substring(prefix.Length).Trim();
                return !string.IsNullOrWhiteSpace(parsedValue);
            }

            parsedValue = string.Empty;
            return false;
        }

        private static bool TryParseFilterDate(string value, out DateTime parsedValue)
        {
            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsedValue
            ) || DateTime.TryParse(value, out parsedValue);
        }

        private static DateTime NormalizeFilterEndDate(string rawValue, DateTime parsedValue)
        {
            // 纯日期的 lte 视为当天结束，避免前端只传日期时漏掉当天更新的数据。
            return rawValue.Contains('T') || rawValue.Contains(' ')
                ? parsedValue
                : parsedValue.Date.AddDays(1).AddTicks(-1);
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
        public Task<CreateSingleProductResponseDto> CreateSingleProductAsync(
            CreateSingleProductRequestDto request
        )
        {
            return CreateSingleProductAsync(request, SystemUpdatedBy);
        }

        public async Task<CreateSingleProductResponseDto> CreateSingleProductAsync(
            CreateSingleProductRequestDto request,
            string? updatedBy
        )
        {
            var response = new CreateSingleProductResponseDto
            {
                Success = false,
                Message = "创建失败",
            };
            var warnings = new List<string>();
            var effectiveUpdatedBy = ResolveUpdatedBy(updatedBy);

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
                    response.Message = "零售价必须大于0";
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
                var auditProductCode = productCode!;
                var beforeSnapshots = await _changeHistoryService.CaptureSnapshotsAsync(
                    new[] { auditProductCode }
                );

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
                    CreatedBy = effectiveUpdatedBy,
                    UpdatedBy = effectiveUpdatedBy,
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
                        CreatedBy = effectiveUpdatedBy,
                        UpdatedBy = effectiveUpdatedBy,
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
                    domesticProduct.UpdatedBy = effectiveUpdatedBy;
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
                            dp.UpdatedBy,
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
                        CreatedBy = effectiveUpdatedBy,
                        UpdatedBy = effectiveUpdatedBy,
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
                    warehouseProduct.UpdatedBy = effectiveUpdatedBy;
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
                            wp.UpdatedBy,
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

                await RecordProductChangeHistoryAsync(
                    beforeSnapshots,
                    new[] { auditProductCode },
                    action: "Create",
                    source: "WarehouseProducts",
                    actorName: effectiveUpdatedBy
                );

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
                            ProductImage = dp.ProductImage,
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

            var displayNameSources = items
                .Select(item => new DomesticProduct
                {
                    ProductCode = item.ProductCode,
                    HBProductNo = item.ItemNumber,
                    ProductName = item.ProductName,
                    EnglishProductName = item.EnglishName,
                })
                .ToList();
            var nameResolutions = await ResolveImportProductNamesAsync(displayNameSources);
            var translatedDisplayNames = displayNameSources
                .Where(source =>
                    nameResolutions.TryGetValue(source.ProductCode, out var resolution)
                    && resolution.WasTranslated
                    && !string.IsNullOrWhiteSpace(resolution.EnglishName)
                )
                .ToList();
            if (translatedDisplayNames.Count > 0)
            {
                var now = DateTime.Now;
                foreach (var source in translatedDisplayNames)
                {
                    var resolution = nameResolutions[source.ProductCode];
                    source.EnglishProductName = resolution.EnglishName;
                    source.UpdatedAt = now;
                }

                await _context
                    .Db.Updateable(translatedDisplayNames)
                    .UpdateColumns(dp => new { dp.EnglishProductName, dp.UpdatedAt })
                    .WhereColumns(dp => new { dp.ProductCode })
                    .ExecuteCommandAsync();
            }

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
                        ItemNumber = item.ItemNumber ?? string.Empty,
                        Barcode = item.Barcode ?? string.Empty,
                        // 国内导入弹窗需要图片；原始图片为空时按货号生成默认图片地址。
                        ProductImage = ProductImageUrlHelper.EnsureImageUrl(
                            item.ProductImage,
                            item.ItemNumber ?? string.Empty
                        ),
                        ProductName = nameResolutions.TryGetValue(
                            item.ProductCode,
                            out var nameResolution
                        )
                            ? nameResolution.DisplayName
                            : item.ProductName ?? string.Empty,
                        EnglishName = nameResolutions.TryGetValue(
                            item.ProductCode,
                            out var englishResolution
                        )
                            ? englishResolution.EnglishName
                            : item.EnglishName,
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
                    .Where(mcp =>
                        mcp.ProductCode != null
                        && productCodes.Contains(mcp.ProductCode)
                        && !mcp.IsDeleted
                    )
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
        public Task<ImportFromDomesticResponseDto> ImportFromDomesticAsync(
            ImportFromDomesticRequestDto request
        )
        {
            return ImportFromDomesticAsync(request, SystemUpdatedBy);
        }

        public async Task<ImportFromDomesticResponseDto> ImportFromDomesticAsync(
            ImportFromDomesticRequestDto request,
            string? updatedBy
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

            var effectiveUpdatedBy = ResolveUpdatedBy(updatedBy);
            var importBatchGuid = Guid.NewGuid();

            try
            {
                // 开启事务
                _context.Db.Ado.BeginTran();
                var now = DateTime.Now;
                var codes = request.ProductCodes.Distinct().ToList();
                var beforeSnapshots = await _changeHistoryService.CaptureSnapshotsAsync(codes);

                // ===== 批量预加载数据（避免 N+1 问题）=====
                // 1. 批量查询国内商品
                var domesticProductsDict = (
                    await _context
                        .Db.Queryable<DomesticProduct>()
                        .Where(dp => codes.Contains(dp.ProductCode) && !dp.IsDeleted)
                        .ToListAsync()
                ).ToDictionary(dp => dp.ProductCode);
                var nameResolutions = await ResolveImportProductNamesAsync(
                    domesticProductsDict.Values
                );

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
                        .Where(p => p.ProductCode != null && codes.Contains(p.ProductCode))
                        .ToListAsync()
                )
                    // 商品编码为空时不能参与字典键匹配，避免放大查询范围。
                    .Where(p => !string.IsNullOrWhiteSpace(p.ProductCode))
                    .GroupBy(p => p.ProductCode!)
                    .ToDictionary(g => g.Key, g => g.First());

                // 4. 批量查询套装商品关联数据
                var allSetProducts = await _context
                    .Db.Queryable<DomesticSetProduct>()
                    .Where(sp => codes.Contains(sp.ProductCode) && !sp.IsDeleted)
                    .ToListAsync();
                var setProductsByCode = allSetProducts
                    .Where(sp => !string.IsNullOrWhiteSpace(sp.ProductCode))
                    .GroupBy(sp => sp.ProductCode!)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // 5. 批量查询已存在的套装编码
                var allSetCodeIds = allSetProducts
                    .Select(sp => sp.SetProductCode)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!)
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
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x.ProductCode)
                            && !string.IsNullOrWhiteSpace(x.SetCodeId)
                        )
                        .GroupBy(x => x.ProductCode!)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Select(x => x.SetCodeId!).ToHashSet()
                        )
                    : new Dictionary<string, HashSet<string>>();

                // 6. 批量查询活跃门店
                var activeStores = (
                    await _context
                    .Db.Queryable<Store>()
                    .Where(s => s.IsActive == true && s.IsDeleted == false)
                    .Select(s => s.StoreCode)
                    .ToListAsync()
                )
                    .Where(storeCode => !string.IsNullOrWhiteSpace(storeCode))
                    .Select(storeCode => storeCode!)
                    .Distinct()
                    .ToList();

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
                                smc.ProductCode != null
                                && codes.Contains(smc.ProductCode)
                                && !smc.IsDeleted
                                && smc.MultiBarcode != null
                                && allSetBarcodes.Contains(smc.MultiBarcode)
                            )
                            .Select(smc => new
                            {
                                smc.ProductCode,
                                smc.MultiBarcode,
                                smc.StoreCode,
                            })
                            .ToListAsync()
                    )
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x.ProductCode)
                            && !string.IsNullOrWhiteSpace(x.MultiBarcode)
                            && !string.IsNullOrWhiteSpace(x.StoreCode)
                        )
                        .GroupBy(x => x.ProductCode!)
                        .ToDictionary(
                            g => g.Key,
                            g => g.Select(x => (x.MultiBarcode!, x.StoreCode!)).ToHashSet()
                        ) : new Dictionary<string, HashSet<(string MultiBarcode, string StoreCode)>>();

                // 8. 批量查询门店零售价
                var storeRetailPricesByCode = (
                    await _context
                        .Db.Queryable<StoreRetailPrice>()
                        .Where(srp => srp.ProductCode != null && codes.Contains(srp.ProductCode) && !srp.IsDeleted)
                        .ToListAsync()
                )
                    .Where(srp => !string.IsNullOrWhiteSpace(srp.ProductCode))
                    .GroupBy(srp => srp.ProductCode!)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // 待操作的列表（批量插入/更新）
                var toUpdateWarehouseProducts = new List<WarehouseProduct>();
                var toInsertWarehouseProducts = new List<WarehouseProduct>();
                var toInsertProducts = new List<Product>();
                var toUpdateProducts = new List<Product>();
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
                    var nameResolution = nameResolutions.TryGetValue(
                        productCode,
                        out var resolvedName
                    )
                        ? resolvedName
                        : new ImportProductNameResolution(
                            domesticProduct.ProductName ?? domesticProduct.HBProductNo ?? productCode,
                            null,
                            false
                        );

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
                        result.Message = "国内价、零售价、进口价必须大于 0";
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
                        existingWp.UpdatedBy = effectiveUpdatedBy;
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
                            CreatedBy = effectiveUpdatedBy,
                            UpdatedBy = effectiveUpdatedBy,
                        };
                        toInsertWarehouseProducts.Add(wp);
                    }

                    // 同步更新国内商品表的价格与体积
                    domesticProduct.DomesticPrice = domesticPrice;
                    domesticProduct.OEMPrice = oemPrice;
                    domesticProduct.ImportPrice = importPrice;
                    domesticProduct.UnitVolume = unitVolume;
                    domesticProduct.ProductImage = finalImageUrl;
                    if (
                        nameResolution.WasTranslated
                        && !string.IsNullOrWhiteSpace(nameResolution.EnglishName)
                    )
                    {
                        domesticProduct.EnglishProductName = nameResolution.EnglishName;
                    }
                    domesticProduct.UpdatedAt = now;
                    domesticProduct.UpdatedBy = effectiveUpdatedBy;
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
                            ProductName = nameResolution.DisplayName,
                            EnglishName = nameResolution.EnglishName,
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
                            CreatedBy = effectiveUpdatedBy,
                            UpdatedBy = effectiveUpdatedBy,
                        };
                        toInsertProducts.Add(product);
                    }
                    else if (
                        ShouldSmartFillExistingProductName(
                            existingProduct,
                            domesticProduct,
                            nameResolution
                        )
                    )
                    {
                        var translatedEnglishName = nameResolution.EnglishName;
                        if (!string.IsNullOrWhiteSpace(translatedEnglishName))
                        {
                            existingProduct.ProductName = translatedEnglishName;
                            existingProduct.EnglishName = translatedEnglishName;
                            existingProduct.UpdatedAt = now;
                            existingProduct.UpdatedBy = effectiveUpdatedBy;
                            toUpdateProducts.Add(existingProduct);
                        }
                    }

                    // 处理套装商品（使用内存查找，避免 N+1）
                    setProductsByCode.TryGetValue(productCode, out var setProducts);
                    setProducts ??= new List<DomesticSetProduct>();

                    var isSetProduct = domesticProduct.ProductType > 0;
                    if (isSetProduct && setProducts.Count > 0)
                    {
                        existingProductSetCodes.TryGetValue(productCode, out var existingSet);
                        existingSet ??= new HashSet<string>();
                        // 国内导入补套装子码时同样按零售价比例分摊主进货价，保持和货柜创建路径一致。
                        var allocatedPurchasePrices = SetChildPurchasePriceAllocator.AllocateByRetailRatio(
                            setProducts,
                            wp.ImportPrice,
                            sp => sp.SetProductCode,
                            sp => sp.OEMPrice ?? wp.OEMPrice
                        );

                        foreach (var sp in setProducts)
                        {
                            if (string.IsNullOrWhiteSpace(sp.SetProductCode))
                                continue;
                            var setProductCode = sp.SetProductCode;
                            if (existingSet.Contains(setProductCode))
                                continue;
                            existingSet.Add(setProductCode);
                            var setPurchasePrice =
                                allocatedPurchasePrices.TryGetValue(setProductCode, out var allocatedPurchasePrice)
                                    ? allocatedPurchasePrice
                                    : sp.ImportPrice ?? wp.ImportPrice;
                            toInsertProductSetCodes.Add(
                                new ProductSetCode
                                {
                                    SetCodeId = setProductCode,
                                    ProductCode = productCode,
                                    SetProductCode = setProductCode,
                                    SetItemNumber = sp.SetProductNo,
                                    SetBarcode = sp.SetBarcode,
                                    SetPurchasePrice = setPurchasePrice,
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
                        existingKeys ??= new HashSet<(string MultiBarcode, string StoreCode)>();
                        var allocatedPurchasePrices = SetChildPurchasePriceAllocator.AllocateByRetailRatio(
                            setProducts,
                            wp.ImportPrice,
                            sp => sp.SetProductCode,
                            sp => sp.OEMPrice ?? wp.OEMPrice
                        );

                        foreach (var sp in setProducts)
                        {
                            if (string.IsNullOrWhiteSpace(sp.SetBarcode))
                                continue;
                            var setBarcode = sp.SetBarcode;
                            foreach (var storeCode in activeStores)
                            {
                                if (existingKeys.Contains((setBarcode, storeCode)))
                                    continue;
                                existingKeys.Add((setBarcode, storeCode));
                                var setPurchasePrice =
                                    sp.SetProductCode != null
                                    && allocatedPurchasePrices.TryGetValue(sp.SetProductCode, out var allocatedPurchasePrice)
                                        ? allocatedPurchasePrice
                                        : sp.ImportPrice;
                                toInsertStoreMultiCodeProducts.Add(
                                    new StoreMultiCodeProduct
                                    {
                                        UUID = UuidHelper.GenerateUuid7(),
                                        ProductCode = productCode,
                                        StoreCode = storeCode,
                                        MultiCodeProductCode = sp.SetProductCode,
                                        StoreMultiCodeProductCode = storeCode + sp.SetProductCode,
                                        MultiBarcode = setBarcode,
                                        PurchasePrice = setPurchasePrice,
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
                            wp.UpdatedBy,
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
                            dp.EnglishProductName,
                            dp.UpdatedAt,
                            dp.UpdatedBy,
                        })
                        .ExecuteCommandAsync();
                }
                if (toInsertProducts.Any())
                {
                    await _context.Db.Insertable(toInsertProducts).ExecuteCommandAsync();
                }
                if (toUpdateProducts.Any())
                {
                    await _context
                        .Db.Updateable(toUpdateProducts)
                        .UpdateColumns(p => new
                        {
                            p.ProductName,
                            p.EnglishName,
                            p.UpdatedAt,
                            p.UpdatedBy,
                        })
                        .WhereColumns(p => new { p.ProductCode })
                        .ExecuteCommandAsync();
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

                var changedProductCodes = response.Results
                    .Where(item => item.Success && !string.IsNullOrWhiteSpace(item.ProductCode))
                    .Select(item => item.ProductCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                await RecordProductChangeHistoryAsync(
                    beforeSnapshots,
                    changedProductCodes,
                    action: "Import",
                    source: "DomesticImport",
                    actorName: effectiveUpdatedBy,
                    batchGuid: importBatchGuid
                );

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
                response.SuccessCount = 0;
                response.Message = "导入失败: " + ex.Message;
            }

            return response;
        }

        /// <summary>
        /// 仓库商品完整更新：同一 db 顺序查、一次性取列表，事务内更新 DomesticProduct、Product、WarehouseProduct、StoreRetailPrice、StoreMultiCodeProduct、ProductSetCode。
        /// 分店零售价强联动：StoreRetailPriceValue / MultiCodeRetailPrice 用主表零售价（OEM）覆盖，PurchasePrice 用进口价覆盖。
        /// </summary>
        public Task<WarehouseProductFullUpdateResultDto> FullUpdateAsync(
            string productCode,
            WarehouseProductFullUpdateDto dto
        )
        {
            return FullUpdateAsync(productCode, dto, SystemUpdatedBy);
        }

        public async Task<WarehouseProductFullUpdateResultDto> FullUpdateAsync(
            string productCode,
            WarehouseProductFullUpdateDto dto,
            string? updatedBy
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

            var effectiveUpdatedBy = ResolveUpdatedBy(updatedBy);

            try
            {
                _context.Db.Ado.BeginTran();

                // 1. WarehouseProduct 作为同商品写入门闩；统一先锁仓库商品，再读取国内商品与主商品。
                var warehouseProduct = await WithWarehouseProductUpdateLock(
                        _context
                            .Db.Queryable<WarehouseProduct>()
                            .Where(w => w.ProductCode == productCode)
                    )
                    .FirstAsync();
                if (warehouseProduct == null)
                {
                    _context.Db.Ado.RollbackTran();
                    result.Message = "仓库商品不存在";
                    return result;
                }
                var domesticProduct = await WithWarehouseProductUpdateLock(
                        _context
                            .Db.Queryable<DomesticProduct>()
                            .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                    )
                    .FirstAsync();
                var product = await WithWarehouseProductUpdateLock(
                        _context.Db.Queryable<Product>().Where(p => p.ProductCode == productCode)
                    )
                    .FirstAsync();
                if (product == null)
                {
                    _context.Db.Ado.RollbackTran();
                    result.Message = "商品不存在（Product 表无此 ProductCode）";
                    return result;
                }
                var beforeSnapshots = await _changeHistoryService.CaptureSnapshotsAsync(
                    new[] { productCode }
                );
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

                var shouldInsertDomesticProduct = false;
                if (
                    domesticProduct == null
                    && !string.IsNullOrWhiteSpace(dto.SupplierCode)
                )
                {
                    // 用户在仓库商品中明确选择国内供应商时，补建或恢复国内商品映射，
                    // 避免非国内导入商品出现“保存成功但供应商未落库”。
                    domesticProduct = await WithWarehouseProductUpdateLock(
                            _context
                                .Db.Queryable<DomesticProduct>()
                                .Where(p => p.ProductCode == productCode && p.IsDeleted)
                        )
                        .FirstAsync();
                    if (domesticProduct == null)
                    {
                        shouldInsertDomesticProduct = true;
                        domesticProduct = new DomesticProduct
                        {
                            ProductCode = productCode,
                            ProductName = product.ProductName,
                            EnglishProductName = product.EnglishName,
                            HBProductNo = product.ItemNumber,
                            Barcode = product.Barcode,
                            ProductType = product.ProductType ?? dto.ProductType,
                            DomesticPrice = warehouseProduct.DomesticPrice,
                            OEMPrice = warehouseProduct.OEMPrice ?? product.RetailPrice,
                            ImportPrice = warehouseProduct.ImportPrice ?? product.PurchasePrice,
                            PackingQuantity = warehouseProduct.PackingQuantity,
                            UnitVolume = warehouseProduct.Volume,
                            MiddlePackQuantity = warehouseProduct.MinOrderQuantity,
                            ProductImage = product.ProductImage,
                            IsActive = warehouseProduct.IsActive,
                            IsDeleted = false,
                            CreatedAt = now,
                            CreatedBy = effectiveUpdatedBy,
                        };
                    }
                    else
                    {
                        domesticProduct.ProductName = product.ProductName;
                        domesticProduct.EnglishProductName = product.EnglishName;
                        domesticProduct.HBProductNo = product.ItemNumber;
                        domesticProduct.Barcode = product.Barcode;
                        domesticProduct.ProductType = product.ProductType ?? dto.ProductType;
                        domesticProduct.DomesticPrice = warehouseProduct.DomesticPrice;
                        domesticProduct.OEMPrice =
                            warehouseProduct.OEMPrice ?? product.RetailPrice;
                        domesticProduct.ImportPrice =
                            warehouseProduct.ImportPrice ?? product.PurchasePrice;
                        domesticProduct.PackingQuantity = warehouseProduct.PackingQuantity;
                        domesticProduct.UnitVolume = warehouseProduct.Volume;
                        domesticProduct.MiddlePackQuantity = warehouseProduct.MinOrderQuantity;
                        domesticProduct.ProductImage = product.ProductImage;
                        domesticProduct.IsActive = warehouseProduct.IsActive;
                        domesticProduct.IsDeleted = false;
                    }
                }

                // 2. 更新 DomesticProduct；明确选择供应商时允许补建国内商品映射。
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
                    // MinOrderQuantity 与中包数量同源：仅回写有效国内商品，不改 Product.MiddlePackageQuantity。
                    if (dto.MinOrderQuantity.HasValue)
                        domesticProduct.MiddlePackQuantity = dto.MinOrderQuantity;
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
                    domesticProduct.UpdatedBy = effectiveUpdatedBy;
                    if (shouldInsertDomesticProduct)
                        await _context.Db.Insertable(domesticProduct).ExecuteCommandAsync();
                    else
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
                        product.ItemNumber ?? product.ProductCode ?? string.Empty
                    );
                }
                product.IsActive = dto.IsActive;
                product.UpdatedAt = now;
                product.UpdatedBy = effectiveUpdatedBy;
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
                        p.UpdatedBy,
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
                warehouseProduct.UpdatedBy = effectiveUpdatedBy;
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
                        w.UpdatedBy,
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

                await RecordProductChangeHistoryAsync(
                    beforeSnapshots,
                    new[] { productCode },
                    action: "Update",
                    source: "WarehouseProducts",
                    actorName: effectiveUpdatedBy
                );

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
        /// 仓库商品窄列 PATCH：一次只更新一个非负字段，事务内窄列更新并记录当前操作人。
        /// MinOrderQuantity 同步 WP.MinOrderQuantity 与有效 DP.MiddlePackQuantity；
        /// DomesticPrice 同步 WP 与有效 DP；
        /// ImportPrice 同步 WP、有效 DP、Product.PurchasePrice 与全部启用未删除分店的有效进货价（缺失补建）；
        /// OEMPrice 同步 WP、有效 DP、Product.RetailPrice 与全部启用未删除分店的零售价（缺失补建）。
        /// 不联动套装/多码/批量，不复活软删价格，另一列价格不动。
        /// </summary>
        public Task<WarehouseProductPatchResultDto?> PatchAsync(
            string productCode,
            WarehouseProductPatchDto dto
        )
        {
            return PatchAsync(productCode, dto, SystemUpdatedBy);
        }

        public async Task<WarehouseProductPatchResultDto?> PatchAsync(
            string productCode,
            WarehouseProductPatchDto dto,
            string? updatedBy
        )
        {
            if (string.IsNullOrWhiteSpace(productCode))
                throw new InvalidOperationException("商品编码不能为空");
            if (dto == null)
                throw new InvalidOperationException("请求数据不能为空");
            var validationError = WarehouseProductPatchDto.Validate(dto);
            if (validationError != null)
                throw new InvalidOperationException(validationError);

            var effectiveUpdatedBy = ResolveUpdatedBy(updatedBy);
            var now = DateTime.Now;

            await _context.Db.Ado.BeginTranAsync();
            try
            {
                // SQL Server 使用更新锁串行化同一商品的局部更新；其他数据库保持普通事务查询。
                var warehouseProduct = await WithWarehouseProductUpdateLock(
                        _context
                            .Db.Queryable<WarehouseProduct>()
                            .Where(w => w.ProductCode == productCode && !w.IsDeleted)
                    )
                    .FirstAsync();
                if (warehouseProduct == null)
                {
                    await _context.Db.Ado.RollbackTranAsync();
                    return null;
                }
                var domesticProduct = await WithWarehouseProductUpdateLock(
                        _context
                            .Db.Queryable<DomesticProduct>()
                            .Where(dp => dp.ProductCode == productCode && !dp.IsDeleted)
                    )
                    .FirstAsync();
                var product = await WithWarehouseProductUpdateLock(
                        _context
                            .Db.Queryable<Product>()
                            .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                    )
                    .FirstAsync();
                if (product == null)
                {
                    await _context.Db.Ado.RollbackTranAsync();
                    return null;
                }
                var beforeSnapshots = await _changeHistoryService.CaptureSnapshotsAsync(
                    new[] { productCode }
                );

                // 窄列更新 WarehouseProduct：只写本次请求字段与审计列，避免覆盖并发库存或价格。
                var warehouseUpdate = _context
                    .Db.Updateable<WarehouseProduct>()
                    .SetColumns(w => w.UpdatedAt == now)
                    .SetColumns(w => w.UpdatedBy == effectiveUpdatedBy)
                    .Where(w => w.ProductCode == productCode && !w.IsDeleted);
                if (dto.MinOrderQuantity.HasValue)
                {
                    warehouseUpdate = warehouseUpdate.SetColumns(w =>
                        w.MinOrderQuantity == dto.MinOrderQuantity.Value
                    );
                }
                if (dto.DomesticPrice.HasValue)
                {
                    warehouseUpdate = warehouseUpdate.SetColumns(w =>
                        w.DomesticPrice == dto.DomesticPrice.Value
                    );
                }
                if (dto.ImportPrice.HasValue)
                {
                    warehouseUpdate = warehouseUpdate.SetColumns(w =>
                        w.ImportPrice == dto.ImportPrice.Value
                    );
                }
                if (dto.OEMPrice.HasValue)
                {
                    warehouseUpdate = warehouseUpdate.SetColumns(w =>
                        w.OEMPrice == dto.OEMPrice.Value
                    );
                }
                var warehouseAffected = await warehouseUpdate.ExecuteCommandAsync();
                if (warehouseAffected <= 0)
                {
                    await _context.Db.Ado.RollbackTranAsync();
                    return null;
                }

                // 更新触发器或并发状态可能改变主商品；复读后再计算分店缺失记录的另一列价格。
                product = await WithWarehouseProductUpdateLock(
                        _context
                            .Db.Queryable<Product>()
                            .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                    )
                    .FirstAsync();
                if (product == null)
                {
                    await _context.Db.Ado.RollbackTranAsync();
                    return null;
                }

                // 有效国内商品窄列联动；不创建 DomesticProduct。
                if (domesticProduct != null)
                {
                    var domesticUpdate = _context
                        .Db.Updateable<DomesticProduct>()
                        .SetColumns(dp => dp.UpdatedAt == now)
                        .SetColumns(dp => dp.UpdatedBy == effectiveUpdatedBy)
                        .Where(dp => dp.ProductCode == productCode && !dp.IsDeleted);
                    if (dto.MinOrderQuantity.HasValue)
                    {
                        domesticUpdate = domesticUpdate.SetColumns(dp =>
                            dp.MiddlePackQuantity == dto.MinOrderQuantity.Value
                        );
                    }
                    if (dto.DomesticPrice.HasValue)
                    {
                        domesticUpdate = domesticUpdate.SetColumns(dp =>
                            dp.DomesticPrice == dto.DomesticPrice.Value
                        );
                    }
                    if (dto.ImportPrice.HasValue)
                    {
                        domesticUpdate = domesticUpdate.SetColumns(dp =>
                            dp.ImportPrice == dto.ImportPrice.Value
                        );
                    }
                    if (dto.OEMPrice.HasValue)
                    {
                        domesticUpdate = domesticUpdate.SetColumns(dp =>
                            dp.OEMPrice == dto.OEMPrice.Value
                        );
                    }
                    await domesticUpdate.ExecuteCommandAsync();
                }

                if (dto.ImportPrice.HasValue)
                {
                    // 进口价同步主表进货价与全部启用未删除分店的有效进货价；另一列零售价不动。
                    var productAffected = await _context
                        .Db.Updateable<Product>()
                        .SetColumns(p => p.UpdatedAt == now)
                        .SetColumns(p => p.UpdatedBy == effectiveUpdatedBy)
                        .SetColumns(p => p.PurchasePrice == dto.ImportPrice.Value)
                        .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                        .ExecuteCommandAsync();
                    if (productAffected <= 0)
                    {
                        await _context.Db.Ado.RollbackTranAsync();
                        return null;
                    }
                    product = await WithWarehouseProductUpdateLock(
                            _context
                                .Db.Queryable<Product>()
                                .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                        )
                        .FirstAsync();
                    if (product == null)
                    {
                        await _context.Db.Ado.RollbackTranAsync();
                        return null;
                    }
                    await UpsertActiveStoreRetailPricesAsync(
                        product,
                        purchasePrice: dto.ImportPrice,
                        retailPrice: null,
                        now,
                        effectiveUpdatedBy
                    );
                }
                if (dto.OEMPrice.HasValue)
                {
                    // 零售价同步主表零售价与全部启用未删除分店的零售价；另一列进货价不动。
                    var productAffected = await _context
                        .Db.Updateable<Product>()
                        .SetColumns(p => p.UpdatedAt == now)
                        .SetColumns(p => p.UpdatedBy == effectiveUpdatedBy)
                        .SetColumns(p => p.RetailPrice == dto.OEMPrice.Value)
                        .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                        .ExecuteCommandAsync();
                    if (productAffected <= 0)
                    {
                        await _context.Db.Ado.RollbackTranAsync();
                        return null;
                    }
                    product = await WithWarehouseProductUpdateLock(
                            _context
                                .Db.Queryable<Product>()
                                .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                        )
                        .FirstAsync();
                    if (product == null)
                    {
                        await _context.Db.Ado.RollbackTranAsync();
                        return null;
                    }
                    await UpsertActiveStoreRetailPricesAsync(
                        product,
                        purchasePrice: null,
                        retailPrice: dto.OEMPrice,
                        now,
                        effectiveUpdatedBy
                    );
                }

                await RecordProductChangeHistoryAsync(
                    beforeSnapshots,
                    new[] { productCode },
                    action: "Patch",
                    source: "WarehouseProducts",
                    actorName: effectiveUpdatedBy
                );

                await _context.Db.Ado.CommitTranAsync();
            }
            catch
            {
                await _context.Db.Ado.RollbackTranAsync();
                throw;
            }

            return new WarehouseProductPatchResultDto { Success = true, Message = "保存成功" };
        }

        private ISugarQueryable<T> WithWarehouseProductUpdateLock<T>(
            ISugarQueryable<T> queryable
        )
        {
            return _context.Db.CurrentConnectionConfig.DbType == DbType.SqlServer
                ? queryable.With(SqlWith.UpdLock)
                : queryable;
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
            // 用未删除仓库记录的左连接反查未入仓商品，避免大表相关子查询超时。
            var query = _context
                .Db.Queryable<Product>()
                .LeftJoin<WarehouseProduct>(
                    (p, wp) => p.ProductCode == wp.ProductCode && !wp.IsDeleted
                )
                .LeftJoin<HBLocalSupplier>(
                    (p, wp, s) => p.LocalSupplierCode == s.LocalSupplierCode && !s.IsDeleted
                )
                .Where(
                    (p, wp, s) =>
                        !p.IsDeleted
                        && p.IsActive
                        && p.ProductCode != null
                        && wp.ProductCode == null
                );

            if (!string.IsNullOrWhiteSpace(request.GlobalSearch))
            {
                var keyword = request.GlobalSearch.Trim();
                query = query.Where(
                    (p, wp, s) =>
                        (p.ItemNumber != null && p.ItemNumber.Contains(keyword))
                        || (p.Barcode != null && p.Barcode.Contains(keyword))
                        || (p.ProductCode != null && p.ProductCode.Contains(keyword))
                        || (p.ProductName != null && p.ProductName.Contains(keyword))
                        || (p.EnglishName != null && p.EnglishName.Contains(keyword))
                        || (
                            p.LocalSupplierCode != null
                            && p.LocalSupplierCode.Contains(keyword)
                        )
                        || (s.Name != null && s.Name.Contains(keyword))
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
                                var filters = values.Select(v => v.Trim()).ToList();
                                query = query.Where(
                                    (p, wp, s) =>
                                        p.ItemNumber != null
                                        && filters.Any(v => p.ItemNumber.Contains(v))
                                );
                            }
                            break;
                        case "localsuppliercode":
                        case "suppliercode":
                            {
                                var filters = values.Select(v => v.Trim()).ToList();
                                query = query.Where(
                                    (p, wp, s) =>
                                        p.LocalSupplierCode != null
                                        && filters.Contains(p.LocalSupplierCode)
                                );
                            }
                            break;
                        case "localsuppliername":
                            {
                                var filters = values.Select(v => v.Trim()).ToList();
                                query = query.Where(
                                    (p, wp, s) =>
                                        s.Name != null
                                        && filters.Any(v => s.Name.Contains(v))
                                );
                            }
                            break;
                    }
                }
            }

            var total = await query.Clone().CountAsync();
            var list = await query
                .OrderBy((p, wp, s) => p.ItemNumber, OrderByType.Asc)
                .OrderBy((p, wp, s) => p.ProductCode, OrderByType.Asc)
                .Select(
                    (p, wp, s) =>
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
        public Task<ImportFromDomesticResponseDto> ImportNonHotbargainProductsAsync(
            ImportNonHotbargainRequestDto request
        )
        {
            return ImportNonHotbargainProductsAsync(request, SystemUpdatedBy);
        }

        public async Task<ImportFromDomesticResponseDto> ImportNonHotbargainProductsAsync(
            ImportNonHotbargainRequestDto request,
            string? updatedBy
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

            var effectiveUpdatedBy = ResolveUpdatedBy(updatedBy);
            var importBatchGuid = Guid.NewGuid();

            try
            {
                // 开启事务
                _context.Db.Ado.BeginTran();
                var now = DateTime.Now;
                var codes = request.ProductCodes.Distinct().ToList();
                var beforeSnapshots = await _changeHistoryService.CaptureSnapshotsAsync(codes);

                // 批量查询商品表（避免 N+1 问题）
                var productsDict = (
                    await _context
                        .Db.Queryable<Product>()
                        .Where(p => p.ProductCode != null && codes.Contains(p.ProductCode))
                        .ToListAsync()
                )
                    .Where(p => !string.IsNullOrWhiteSpace(p.ProductCode))
                    .GroupBy(p => p.ProductCode!)
                    .ToDictionary(g => g.Key, g => g.First());

                // 批量查询已存在的仓库商品编码（避免 N+1 问题）
                var existingWpCodes = (
                    await _context
                        .Db.Queryable<WarehouseProduct>()
                        // 软删除的仓库记录不应阻止同商品重新导入。
                        .Where(wp => codes.Contains(wp.ProductCode) && !wp.IsDeleted)
                        .Select(wp => wp.ProductCode)
                        .ToListAsync()
                ).ToHashSet();
                var softDeletedWarehouseProducts = (
                    await _context
                        .Db.Queryable<WarehouseProduct>()
                        .Where(wp => codes.Contains(wp.ProductCode) && wp.IsDeleted)
                        .ToListAsync()
                ).ToDictionary(wp => wp.ProductCode);

                // 待插入的仓库商品列表
                var toInsertWarehouseProducts = new List<WarehouseProduct>();
                var toRestoreWarehouseProducts = new List<WarehouseProduct>();

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

                    // 软删除记录改为恢复，避免主键冲突并符合重新导入语义。
                    if (softDeletedWarehouseProducts.TryGetValue(productCode, out var deletedWp))
                    {
                        deletedWp.DomesticPrice = 0;
                        deletedWp.OEMPrice = 0;
                        deletedWp.ImportPrice = product.PurchasePrice ?? 0;
                        deletedWp.StockQuantity = 0;
                        deletedWp.MinOrderQuantity = null;
                        deletedWp.StockValue = null;
                        deletedWp.StockAlertQuantity = null;
                        deletedWp.Volume = null;
                        deletedWp.PackingQuantity = null;
                        deletedWp.IsActive = true;
                        deletedWp.IsDeleted = false;
                        deletedWp.UpdatedAt = now;
                        deletedWp.UpdatedBy = effectiveUpdatedBy;
                        toRestoreWarehouseProducts.Add(deletedWp);

                        result.Success = true;
                        result.Message = "导入成功";
                        response.Results.Add(result);
                        response.SuccessCount++;
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
                        CreatedBy = effectiveUpdatedBy,
                        UpdatedBy = effectiveUpdatedBy,
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
                if (toRestoreWarehouseProducts.Any())
                {
                    var restoredProductCodes = toRestoreWarehouseProducts
                        .Select(wp => wp.ProductCode)
                        .ToList();
                    await _context
                        .Db.Deleteable<ProductLocation>()
                        .Where(pl =>
                            pl.ProductCode != null && restoredProductCodes.Contains(pl.ProductCode)
                        )
                        .ExecuteCommandAsync();
                    await _context.Db.Updateable(toRestoreWarehouseProducts).ExecuteCommandAsync();
                }

                var changedProductCodes = response.Results
                    .Where(item => item.Success && !string.IsNullOrWhiteSpace(item.ProductCode))
                    .Select(item => item.ProductCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                await RecordProductChangeHistoryAsync(
                    beforeSnapshots,
                    changedProductCodes,
                    action: "Import",
                    source: "NonDomesticImport",
                    actorName: effectiveUpdatedBy,
                    batchGuid: importBatchGuid
                );

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
                response.SuccessCount = 0;
                response.Message = "导入失败: " + ex.Message;
            }

            return response;
        }

        public Task<BatchToggleWarehouseProductsActiveResultDto> BatchToggleActiveAsync(
            BatchToggleWarehouseProductsActiveRequestDto request
        )
        {
            return BatchToggleActiveAsync(request, SystemUpdatedBy);
        }

        public async Task<BatchToggleWarehouseProductsActiveResultDto> BatchToggleActiveAsync(
            BatchToggleWarehouseProductsActiveRequestDto request,
            string? updatedBy
        )
        {
            var result = new BatchToggleWarehouseProductsActiveResultDto
            {
                Success = false,
                Message = "上下架失败",
            };

            if (request == null || request.ProductCodes == null || !request.ProductCodes.Any())
            {
                result.Message = "商品编码不能为空";
                return result;
            }

            var effectiveUpdatedBy = ResolveUpdatedBy(updatedBy);
            var batchGuid = Guid.NewGuid();

            var productCodes = request
                .ProductCodes.Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct()
                .ToList();

            if (!productCodes.Any())
            {
                result.Message = "商品编码不能为空";
                return result;
            }

            try
            {
                _context.Db.Ado.BeginTran();

                var existingCodes = await _context
                    .Db.Queryable<WarehouseProduct>()
                    .Where(w => productCodes.Contains(w.ProductCode) && !w.IsDeleted)
                    .Select(w => w.ProductCode)
                    .ToListAsync();

                var now = DateTime.Now;
                var validWarehouseProductCodes = existingCodes.Distinct().ToList();
                var existingCodeSet = validWarehouseProductCodes.ToHashSet();
                var missingCodes = productCodes
                    .Where(code => !existingCodeSet.Contains(code))
                    .ToList();
                var beforeSnapshots = await _changeHistoryService.CaptureSnapshotsAsync(
                    validWarehouseProductCodes
                );

                if (validWarehouseProductCodes.Any())
                {
                    await _context
                        .Db.Updateable<WarehouseProduct>()
                        .SetColumns(w => w.IsActive == request.IsActive)
                        .SetColumns(w => w.UpdatedAt == now)
                        .SetColumns(w => w.UpdatedBy == effectiveUpdatedBy)
                        .Where(w => validWarehouseProductCodes.Contains(w.ProductCode) && !w.IsDeleted)
                        .ExecuteCommandAsync();

                    await _context
                        .Db.Updateable<Product>()
                        .SetColumns(p => p.IsActive == request.IsActive)
                        .SetColumns(p => p.UpdatedAt == now)
                        .SetColumns(p => p.UpdatedBy == effectiveUpdatedBy)
                        .Where(p =>
                            p.ProductCode != null && validWarehouseProductCodes.Contains(p.ProductCode)
                        )
                        .ExecuteCommandAsync();

                    await _context
                        .Db.Updateable<DomesticProduct>()
                        .SetColumns(dp => dp.IsActive == request.IsActive)
                        .SetColumns(dp => dp.UpdatedAt == now)
                        .SetColumns(dp => dp.UpdatedBy == effectiveUpdatedBy)
                        .Where(dp => validWarehouseProductCodes.Contains(dp.ProductCode) && !dp.IsDeleted)
                        .ExecuteCommandAsync();

                    await _context
                        .Db.Updateable<StoreRetailPrice>()
                        .SetColumns(srp => srp.IsActive == request.IsActive)
                        .SetColumns(srp => srp.UpdatedAt == now)
                        .Where(srp =>
                            srp.ProductCode != null
                            && validWarehouseProductCodes.Contains(srp.ProductCode)
                            && !srp.IsDeleted
                        )
                        .ExecuteCommandAsync();

                    await _context
                        .Db.Updateable<StoreMultiCodeProduct>()
                        .SetColumns(mcp => mcp.IsActive == request.IsActive)
                        .SetColumns(mcp => mcp.UpdatedAt == now)
                        .Where(mcp =>
                            mcp.ProductCode != null
                            && validWarehouseProductCodes.Contains(mcp.ProductCode)
                            && !mcp.IsDeleted
                        )
                        .ExecuteCommandAsync();
                }

                await RecordProductChangeHistoryAsync(
                    beforeSnapshots,
                    validWarehouseProductCodes,
                    action: "ToggleActive",
                    source: "WarehouseProducts",
                    actorName: effectiveUpdatedBy,
                    batchGuid: batchGuid
                );

                _context.Db.Ado.CommitTran();

                result.Success = missingCodes.Count == 0;
                result.SuccessCount = validWarehouseProductCodes.Count;
                result.FailedCount = missingCodes.Count;
                if (missingCodes.Any())
                {
                    result.Errors.AddRange(missingCodes.Select(code => $"仓库商品不存在: {code}"));
                }
                result.Message = request.IsActive
                    ? (missingCodes.Any() ? "部分商品上架成功" : "批量上架成功")
                    : (missingCodes.Any() ? "部分商品下架成功" : "批量下架成功");
            }
            catch (Exception ex)
            {
                _context.Db.Ado.RollbackTran();
                _logger.LogError(ex, "仓库商品批量上下架失败");
                result.Success = false;
                result.SuccessCount = 0;
                result.Message = "批量上下架失败: " + ex.Message;
                result.Errors.Add(ex.Message);
            }

            return result;
        }

        private static string ResolveUpdatedBy(string? updatedBy)
        {
            // 服务也会被后台任务等非 HTTP 入口调用，空操作人必须可审计地回退为 System。
            return string.IsNullOrWhiteSpace(updatedBy) ? SystemUpdatedBy : updatedBy;
        }

        private async Task RecordProductChangeHistoryAsync(
            IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto> beforeSnapshots,
            IEnumerable<string> productCodes,
            string action,
            string source,
            string actorName,
            Guid? batchGuid = null,
            string? sourceReference = null,
            string? actorUserGuid = null
        )
        {
            var normalizedCodes = productCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (normalizedCodes.Count == 0)
            {
                return;
            }

            var afterSnapshots = await _changeHistoryService.CaptureSnapshotsAsync(normalizedCodes);
            await _changeHistoryService.RecordChangesAsync(
                beforeSnapshots,
                afterSnapshots,
                new WarehouseProductChangeHistoryContextDto
                {
                    Action = action,
                    Source = source,
                    SourceReference = sourceReference,
                    BatchGuid = batchGuid,
                    ActorUserGuid = actorUserGuid,
                    ActorName = ResolveUpdatedBy(actorName),
                }
            );
        }

        /// <summary>
        /// 从 HQ 商品库存表同步到本地仓库商品表
        /// 这里统一委托给全量同步服务，避免 React 服务层保留旧的逐条增删改逻辑。
        /// </summary>
        /// <returns>同步结果</returns>
        public Task<SyncResult> SyncFromHqAsync() => SyncFromHqAsync(null, null);

        public async Task<SyncResult> SyncFromHqAsync(
            string? actorUserGuid,
            string? actorName
        )
        {
            _logger.LogInformation("[WarehouseProductSync] 开始委托全量同步仓库商品库存");
            return await _dataSyncFullService.SyncWarehouseProductsFromHqAsync(
                50000,
                10000,
                actorUserGuid,
                actorName
            );
        }

        public async Task<List<WarehouseMobileProductDto>> LookupMobileProductsAsync(string keyword)
        {
            var trimmed = keyword?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return new List<WarehouseMobileProductDto>();
            }

            var lowered = trimmed.ToLower();
            var rows = await _context
                .Db.Queryable<WarehouseProduct>()
                .LeftJoin<Product>((w, p) => p.ProductCode == w.ProductCode && !p.IsDeleted)
                .LeftJoin<DomesticProduct>((w, p, dp) => dp.ProductCode == w.ProductCode && !dp.IsDeleted)
                .LeftJoin<ChinaSupplier>((w, p, dp, s) => dp.SupplierCode == s.SupplierCode && !s.IsDeleted)
                .LeftJoin<ProductLocation>((w, p, dp, s, pl) => pl.ProductCode == w.ProductCode && !pl.IsDeleted)
                .LeftJoin<Location>((w, p, dp, s, pl, l) => l.LocationGuid == pl.LocationGuid && !l.IsDeleted)
                .LeftJoin<ProductGrade>((w, p, dp, s, pl, l, pg) => pg.ProductCode == w.ProductCode && !pg.IsDeleted)
                .Where((w, p, dp, s, pl, l, pg) =>
                    !w.IsDeleted
                    && (
                        (w.ProductCode != null && w.ProductCode.ToLower().Contains(lowered))
                        || (p.ProductName != null && p.ProductName.ToLower().Contains(lowered))
                        || (p.ItemNumber != null && p.ItemNumber.ToLower().Contains(lowered))
                        || (p.Barcode != null && p.Barcode.ToLower().Contains(lowered))
                        || (p.LocalSupplierCode != null && p.LocalSupplierCode.ToLower().Contains(lowered))
                        || (s.SupplierName != null && s.SupplierName.ToLower().Contains(lowered))
                        || (s.SupplierCode != null && s.SupplierCode.ToLower().Contains(lowered))
                        || (l.LocationCode != null && l.LocationCode.ToLower().Contains(lowered))
                        || (l.LocationBarcode != null && l.LocationBarcode.ToLower().Contains(lowered))
                    )
                )
                .OrderBy((w, p, dp, s, pl, l, pg) => p.ItemNumber)
                .Select((w, p, dp, s, pl, l, pg) => new
                {
                    w.ProductCode,
                    ProductName = p.ProductName,
                    p.ItemNumber,
                    p.Barcode,
                    ProductImage = SqlFunc.IsNullOrEmpty(p.ProductImage) ? dp.ProductImage : p.ProductImage,
                    p.ProductType,
                    p.LocalSupplierCode,
                    SupplierCode = dp.SupplierCode,
                    SupplierName = s.SupplierName,
                    Grade = pg.Grade,
                    w.IsActive,
                    p.PurchasePrice,
                    p.RetailPrice,
                    w.DomesticPrice,
                    w.OEMPrice,
                    w.ImportPrice,
                    w.StockQuantity,
                    MiddlePackageQuantity = p.MiddlePackageQuantity,
                    PackingQuantity = SqlFunc.IsNull(w.PackingQuantity, dp.PackingQuantity),
                    Volume = SqlFunc.IsNull(w.Volume, dp.UnitVolume),
                    l.LocationGuid,
                    l.LocationCode,
                    l.LocationBarcode,
                    UpdatedAt = SqlFunc.IsNull(p.UpdatedAt, w.UpdatedAt),
                })
                .Take(50)
                .ToListAsync();

            return rows
                .GroupBy(row => row.ProductCode)
                .Select(group => group.First())
                .Select(row => new WarehouseMobileProductDto
                {
                    ProductCode = row.ProductCode,
                    ProductName = row.ProductName ?? string.Empty,
                    ItemNumber = row.ItemNumber,
                    Barcode = row.Barcode,
                    ProductImage = row.ProductImage,
                    ProductType = row.ProductType,
                    ProductTypeLabel = GetProductTypeLabel(row.ProductType),
                    LocalSupplierCode = row.LocalSupplierCode,
                    SupplierCode = row.SupplierCode,
                    SupplierName = row.SupplierName,
                    Grade = row.Grade,
                    // 兼容新旧字段，移动端新字段与旧字段始终返回同值。
                    WarehouseIsActive = row.IsActive,
                    IsActive = row.IsActive,
                    PurchasePrice = row.PurchasePrice,
                    RetailPrice = row.RetailPrice,
                    DomesticPrice = row.DomesticPrice,
                    OEMPrice = row.OEMPrice,
                    ImportPrice = row.ImportPrice,
                    StockQuantity = row.StockQuantity,
                    MiddlePackageQuantity = row.MiddlePackageQuantity,
                    PackingQuantity = row.PackingQuantity,
                    Volume = row.Volume,
                    LocationGuid = row.LocationGuid,
                    LocationCode = row.LocationCode,
                    LocationBarcode = row.LocationBarcode,
                    UpdatedAt = row.UpdatedAt,
                })
                .ToList();
        }

        public async Task<WarehouseMobileProductDto?> GetMobileProductAsync(string productCode)
        {
            var trimmed = productCode?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }

            return await FindMobileProductByCodeAsync(trimmed);
        }

        public Task<WarehouseMobileProductDto?> PatchMobileProductAsync(
            string productCode,
            WarehouseMobileProductPatchDto dto
        )
        {
            return PatchMobileProductAsync(productCode, dto, SystemUpdatedBy);
        }

        public async Task<WarehouseMobileProductDto?> PatchMobileProductAsync(
            string productCode,
            WarehouseMobileProductPatchDto dto,
            string? updatedBy
        )
        {
            var product = await _context
                .Db.Queryable<Product>()
                .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                .FirstAsync();
            var warehouseProduct = await _context
                .Db.Queryable<WarehouseProduct>()
                .Where(w => w.ProductCode == productCode && !w.IsDeleted)
                .FirstAsync();

            if (product == null || warehouseProduct == null)
            {
                return null;
            }

            var effectiveUpdatedBy = ResolveUpdatedBy(updatedBy);

            var domesticProduct = await _context
                .Db.Queryable<DomesticProduct>()
                .Where(dp => dp.ProductCode == productCode && !dp.IsDeleted)
                .FirstAsync();
            var productGrade = await _context
                .Db.Queryable<ProductGrade>()
                .Where(pg => pg.ProductCode == productCode && !pg.IsDeleted)
                .FirstAsync();
            var shouldInsertProductGrade = false;

            var now = DateTime.UtcNow;
            var shouldUpdateProduct = false;
            var shouldUpdateWarehouseProduct = false;
            var shouldUpdateDomesticProduct = false;
            var shouldUpdateProductGrade = false;
            // 仅更新仓库商品状态；优先新字段，旧字段仅作兼容回退。
            var warehouseIsActive = dto.WarehouseIsActive ?? dto.IsActive;
            if (warehouseIsActive.HasValue)
            {
                warehouseProduct.IsActive = warehouseIsActive.Value;
                shouldUpdateWarehouseProduct = true;
            }

            var syncedPurchasePrice = ResolveLinkedPrice(
                dto.PurchasePrice,
                dto.ImportPrice,
                "进货价",
                "进口价"
            );
            var syncedRetailPrice = ResolveLinkedPrice(
                dto.RetailPrice,
                dto.OEMPrice,
                "零售价",
                "RRP"
            );
            var shouldSyncStorePurchasePrice = syncedPurchasePrice.HasValue;
            var shouldSyncStoreRetailPrice = dto.SyncStoreRetailPrices == true
                && syncedRetailPrice.HasValue;

            if (syncedPurchasePrice.HasValue)
            {
                product.PurchasePrice = syncedPurchasePrice;
                shouldUpdateProduct = true;
                warehouseProduct.ImportPrice = syncedPurchasePrice;
                shouldUpdateWarehouseProduct = true;
                if (domesticProduct != null)
                {
                    domesticProduct.ImportPrice = syncedPurchasePrice;
                    shouldUpdateDomesticProduct = true;
                }
            }
            if (syncedRetailPrice.HasValue)
            {
                product.RetailPrice = syncedRetailPrice;
                shouldUpdateProduct = true;
                warehouseProduct.OEMPrice = syncedRetailPrice;
                shouldUpdateWarehouseProduct = true;
                if (domesticProduct != null)
                {
                    domesticProduct.OEMPrice = syncedRetailPrice;
                    shouldUpdateDomesticProduct = true;
                }
            }
            if (dto.MiddlePackageQuantity.HasValue)
            {
                product.MiddlePackageQuantity = dto.MiddlePackageQuantity;
                shouldUpdateProduct = true;
            }
            if (dto.ProductImage != null)
            {
                product.ProductImage = dto.ProductImage;
                shouldUpdateProduct = true;
                if (domesticProduct != null)
                {
                    domesticProduct.ProductImage = dto.ProductImage;
                    shouldUpdateDomesticProduct = true;
                }
            }

            if (dto.DomesticPrice.HasValue)
            {
                warehouseProduct.DomesticPrice = dto.DomesticPrice;
                shouldUpdateWarehouseProduct = true;
                if (domesticProduct != null)
                {
                    domesticProduct.DomesticPrice = dto.DomesticPrice;
                    shouldUpdateDomesticProduct = true;
                }
            }
            if (dto.StockQuantity.HasValue)
            {
                warehouseProduct.StockQuantity = dto.StockQuantity;
                shouldUpdateWarehouseProduct = true;
            }
            if (dto.PackingQuantity.HasValue)
            {
                warehouseProduct.PackingQuantity = dto.PackingQuantity;
                shouldUpdateWarehouseProduct = true;
                if (domesticProduct != null)
                {
                    domesticProduct.PackingQuantity = dto.PackingQuantity;
                    shouldUpdateDomesticProduct = true;
                }
            }
            if (dto.Volume.HasValue)
            {
                warehouseProduct.Volume = dto.Volume;
                shouldUpdateWarehouseProduct = true;
                if (domesticProduct != null)
                {
                    domesticProduct.UnitVolume = dto.Volume;
                    shouldUpdateDomesticProduct = true;
                }
            }
            if (dto.MiddlePackageQuantity.HasValue && domesticProduct != null)
            {
                domesticProduct.MiddlePackQuantity = dto.MiddlePackageQuantity;
                shouldUpdateDomesticProduct = true;
            }

            if (dto.Grade != null)
            {
                var normalizedGrade = dto.Grade.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(normalizedGrade))
                {
                    if (productGrade != null)
                    {
                        productGrade.IsDeleted = true;
                        productGrade.UpdatedAt = now;
                        shouldUpdateProductGrade = true;
                    }
                }
                else if (productGrade != null)
                {
                    productGrade.Grade = normalizedGrade;
                    productGrade.UpdatedAt = now;
                    shouldUpdateProductGrade = true;
                }
                else
                {
                    productGrade = new ProductGrade
                    {
                        ProductCode = productCode,
                        Grade = normalizedGrade,
                        CreatedAt = now,
                        UpdatedAt = now,
                    };
                    shouldInsertProductGrade = true;
                    shouldUpdateProductGrade = true;
                }
            }

            if (shouldUpdateProduct)
            {
                product.UpdatedAt = now;
            }
            // 任一移动端可编辑字段发生变化时，都刷新仓库商品审计信息。
            // 即使实际字段位于 Product、DomesticProduct 或 ProductGrade，列表也从 WarehouseProduct 展示更新人。
            if (shouldUpdateProduct || shouldUpdateWarehouseProduct || shouldUpdateDomesticProduct || shouldUpdateProductGrade)
            {
                warehouseProduct.UpdatedAt = now;
                warehouseProduct.UpdatedBy = effectiveUpdatedBy;
                shouldUpdateWarehouseProduct = true;
            }
            if (domesticProduct != null && shouldUpdateDomesticProduct)
            {
                domesticProduct.UpdatedAt = now;
            }

            await _context.Db.Ado.BeginTranAsync();
            try
            {
                // 与批量、完整编辑和表格 PATCH 统一先锁 WarehouseProduct，避免跨表反向等待。
                var lockedWarehouseProduct = await WithWarehouseProductUpdateLock(
                        _context
                            .Db.Queryable<WarehouseProduct>()
                            .Where(w => w.ProductCode == productCode && !w.IsDeleted)
                    )
                    .FirstAsync();
                if (lockedWarehouseProduct == null)
                {
                    await _context.Db.Ado.RollbackTranAsync();
                    return null;
                }
                var beforeSnapshots = await _changeHistoryService.CaptureSnapshotsAsync(
                    new[] { productCode }
                );

                if (shouldUpdateProduct)
                {
                    // 仅回写移动端本次涉及的 Product 列，避免覆盖并发修改的基础商品字段。
                    var productUpdate = _context
                        .Db.Updateable<Product>()
                        .SetColumns(p => p.UpdatedAt == now)
                        .SetColumns(p => p.UpdatedBy == effectiveUpdatedBy)
                        .Where(p => p.UUID == product.UUID && !p.IsDeleted);
                    if (syncedPurchasePrice.HasValue)
                    {
                        productUpdate = productUpdate.SetColumns(p =>
                            p.PurchasePrice == syncedPurchasePrice.Value
                        );
                    }
                    if (syncedRetailPrice.HasValue)
                    {
                        productUpdate = productUpdate.SetColumns(p =>
                            p.RetailPrice == syncedRetailPrice.Value
                        );
                    }
                    if (dto.MiddlePackageQuantity.HasValue)
                    {
                        productUpdate = productUpdate.SetColumns(p =>
                            p.MiddlePackageQuantity == dto.MiddlePackageQuantity.Value
                        );
                    }
                    if (dto.ProductImage != null)
                    {
                        productUpdate = productUpdate.SetColumns(p => p.ProductImage == dto.ProductImage);
                    }
                    await productUpdate.ExecuteCommandAsync();
                }
                if (shouldUpdateWarehouseProduct)
                {
                    // 移动端只写本次请求涉及的仓库字段，避免读后全实体回写覆盖并发库存或价格。
                    var warehouseUpdate = _context
                        .Db.Updateable<WarehouseProduct>()
                        .SetColumns(w => w.UpdatedAt == now)
                        .SetColumns(w => w.UpdatedBy == effectiveUpdatedBy)
                        .Where(w => w.ProductCode == productCode && !w.IsDeleted);
                    if (warehouseIsActive.HasValue)
                    {
                        warehouseUpdate = warehouseUpdate.SetColumns(w =>
                            w.IsActive == warehouseIsActive.Value
                        );
                    }
                    if (syncedPurchasePrice.HasValue)
                    {
                        warehouseUpdate = warehouseUpdate.SetColumns(w =>
                            w.ImportPrice == syncedPurchasePrice.Value
                        );
                    }
                    if (syncedRetailPrice.HasValue)
                    {
                        warehouseUpdate = warehouseUpdate.SetColumns(w =>
                            w.OEMPrice == syncedRetailPrice.Value
                        );
                    }
                    if (dto.DomesticPrice.HasValue)
                    {
                        warehouseUpdate = warehouseUpdate.SetColumns(w =>
                            w.DomesticPrice == dto.DomesticPrice.Value
                        );
                    }
                    if (dto.StockQuantity.HasValue)
                    {
                        warehouseUpdate = warehouseUpdate.SetColumns(w =>
                            w.StockQuantity == dto.StockQuantity.Value
                        );
                    }
                    if (dto.PackingQuantity.HasValue)
                    {
                        warehouseUpdate = warehouseUpdate.SetColumns(w =>
                            w.PackingQuantity == dto.PackingQuantity.Value
                        );
                    }
                    if (dto.Volume.HasValue)
                    {
                        warehouseUpdate = warehouseUpdate.SetColumns(w => w.Volume == dto.Volume.Value);
                    }
                    await warehouseUpdate.ExecuteCommandAsync();
                }
                if (domesticProduct != null && shouldUpdateDomesticProduct)
                {
                    // 国内商品同样只写本次 DTO 驱动的联动列，避免全实体更新覆盖并发包装数据。
                    var domesticProductUpdate = _context
                        .Db.Updateable<DomesticProduct>()
                        .SetColumns(dp => dp.UpdatedAt == now)
                        .SetColumns(dp => dp.UpdatedBy == effectiveUpdatedBy)
                        .Where(dp => dp.ProductCode == domesticProduct.ProductCode && !dp.IsDeleted);
                    if (syncedPurchasePrice.HasValue)
                    {
                        domesticProductUpdate = domesticProductUpdate.SetColumns(dp =>
                            dp.ImportPrice == syncedPurchasePrice.Value
                        );
                    }
                    if (syncedRetailPrice.HasValue)
                    {
                        domesticProductUpdate = domesticProductUpdate.SetColumns(dp =>
                            dp.OEMPrice == syncedRetailPrice.Value
                        );
                    }
                    if (dto.ProductImage != null)
                    {
                        domesticProductUpdate = domesticProductUpdate.SetColumns(dp =>
                            dp.ProductImage == dto.ProductImage
                        );
                    }
                    if (dto.DomesticPrice.HasValue)
                    {
                        domesticProductUpdate = domesticProductUpdate.SetColumns(dp =>
                            dp.DomesticPrice == dto.DomesticPrice.Value
                        );
                    }
                    if (dto.PackingQuantity.HasValue)
                    {
                        domesticProductUpdate = domesticProductUpdate.SetColumns(dp =>
                            dp.PackingQuantity == dto.PackingQuantity.Value
                        );
                    }
                    if (dto.Volume.HasValue)
                    {
                        domesticProductUpdate = domesticProductUpdate.SetColumns(dp =>
                            dp.UnitVolume == dto.Volume.Value
                        );
                    }
                    if (dto.MiddlePackageQuantity.HasValue)
                    {
                        domesticProductUpdate = domesticProductUpdate.SetColumns(dp =>
                            dp.MiddlePackQuantity == dto.MiddlePackageQuantity.Value
                        );
                    }
                    await domesticProductUpdate.ExecuteCommandAsync();
                }
                if (productGrade != null && shouldUpdateProductGrade)
                {
                    if (shouldInsertProductGrade)
                    {
                        await _context.Db.Insertable(productGrade).ExecuteCommandAsync();
                    }
                    else
                    {
                        await _context.Db.Updateable(productGrade).ExecuteCommandAsync();
                    }
                }
                if (shouldSyncStorePurchasePrice || shouldSyncStoreRetailPrice)
                {
                    product = await _context
                        .Db.Queryable<Product>()
                        .Where(p => p.ProductCode == productCode && !p.IsDeleted)
                        .FirstAsync();
                    if (product == null)
                    {
                        await _context.Db.Ado.RollbackTranAsync();
                        return null;
                    }
                }
                if (shouldSyncStorePurchasePrice)
                {
                    await UpsertActiveStoreRetailPricesAsync(
                        product,
                        purchasePrice: syncedPurchasePrice,
                        retailPrice: null,
                        now,
                        MobileWarehousePricePatchUpdatedBy
                    );
                }
                if (shouldSyncStoreRetailPrice)
                {
                    await UpsertActiveStoreRetailPricesAsync(
                        product,
                        purchasePrice: null,
                        retailPrice: syncedRetailPrice,
                        now,
                        MobileWarehousePricePatchUpdatedBy
                    );
                }

                await RecordProductChangeHistoryAsync(
                    beforeSnapshots,
                    new[] { productCode },
                    action: "Patch",
                    source: "MobileWarehouse",
                    actorName: effectiveUpdatedBy
                );

                await _context.Db.Ado.CommitTranAsync();
            }
            catch
            {
                await _context.Db.Ado.RollbackTranAsync();
                throw;
            }

            return await GetMobileProductAsync(productCode);
        }

        private static decimal? ResolveLinkedPrice(
            decimal? masterPrice,
            decimal? warehousePrice,
            string masterLabel,
            string warehouseLabel
        )
        {
            if (masterPrice.HasValue && warehousePrice.HasValue && masterPrice.Value != warehousePrice.Value)
            {
                throw new InvalidOperationException($"{masterLabel}和{warehouseLabel}不一致");
            }

            return masterPrice ?? warehousePrice;
        }

        private async Task UpsertActiveStoreRetailPricesAsync(
            Product product,
            decimal? purchasePrice,
            decimal? retailPrice,
            DateTime now,
            string updatedBy
        )
        {
            if (string.IsNullOrWhiteSpace(product.ProductCode))
            {
                return;
            }

            var stores = await _context
                .Db.Queryable<Store>()
                .Where(s => s.IsActive && !s.IsDeleted)
                .Select(s => new { s.StoreCode })
                .ToListAsync();
            var activeStoreCodes = stores
                .Select(s => s.StoreCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct()
                .ToList();
            if (!activeStoreCodes.Any())
            {
                return;
            }

            var existingPrices = await _context
                .Db.Queryable<StoreRetailPrice>()
                .Where(srp =>
                    srp.ProductCode == product.ProductCode
                    && srp.StoreCode != null
                    && activeStoreCodes.Contains(srp.StoreCode)
                    && !srp.IsDeleted
                )
                .ToListAsync();
            var existingStoreCodes = existingPrices
                .Select(price => price.StoreCode)
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToHashSet();

            foreach (var price in existingPrices)
            {
                if (purchasePrice.HasValue)
                {
                    price.PurchasePrice = purchasePrice;
                }
                if (retailPrice.HasValue)
                {
                    price.StoreRetailPriceValue = retailPrice;
                }
                price.UpdatedAt = now;
                price.UpdatedBy = updatedBy;
            }

            if (existingPrices.Any())
            {
                var existingUuids = existingPrices.Select(price => price.UUID).ToList();
                var update = _context.Db.Updateable<StoreRetailPrice>()
                    .SetColumns(price => price.UpdatedAt == now)
                    .SetColumns(price => price.UpdatedBy == updatedBy)
                    .Where(price => existingUuids.Contains(price.UUID));
                if (purchasePrice.HasValue)
                {
                    update = update.SetColumns(price => price.PurchasePrice == purchasePrice);
                }
                if (retailPrice.HasValue)
                {
                    update = update.SetColumns(price => price.StoreRetailPriceValue == retailPrice);
                }
                await update.ExecuteCommandAsync();
            }

            var insertPrices = activeStoreCodes
                .Where(storeCode => !existingStoreCodes.Contains(storeCode))
                .Select(storeCode => new StoreRetailPrice
                {
                    UUID = UuidHelper.GenerateUuid7(),
                    StoreCode = storeCode,
                    ProductCode = product.ProductCode,
                    StoreProductCode = storeCode + product.ProductCode,
                    SupplierCode = product.LocalSupplierCode,
                    PurchasePrice = purchasePrice ?? product.PurchasePrice,
                    StoreRetailPriceValue = retailPrice ?? product.RetailPrice,
                    DiscountRate = null,
                    IsActive = product.IsActive,
                    IsAutoPricing = product.IsAutoPricing,
                    IsSpecialProduct = product.IsSpecialProduct,
                    CreatedAt = now,
                    CreatedBy = updatedBy,
                    UpdatedAt = now,
                    UpdatedBy = updatedBy,
                    IsDeleted = false,
                })
                .ToList();

            if (insertPrices.Any())
            {
                await _context.Db.Insertable(insertPrices).ExecuteCommandAsync();
            }
        }

        public async Task<WarehouseMobileProductDto?> SetMobileProductLocationAsync(
            string productCode,
            string? locationGuid
        )
        {
            var trimmedProductCode = productCode?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedProductCode))
            {
                Console.WriteLine(
                    $"[ProductWarehouseReactService.SetMobileProductLocation] 绑定失败：商品编码为空 ProductCode={productCode}, LocationGuid={locationGuid}"
                );
                return null;
            }

            Console.WriteLine(
                $"[ProductWarehouseReactService.SetMobileProductLocation] 开始绑定 ProductCode={trimmedProductCode}, LocationGuid={locationGuid}"
            );

            var warehouseProduct = await _context
                .Db.Queryable<WarehouseProduct>()
                .Where(w => w.ProductCode == trimmedProductCode && !w.IsDeleted)
                .FirstAsync();
            var product = await _context
                .Db.Queryable<Product>()
                .Where(p => p.ProductCode == trimmedProductCode && !p.IsDeleted)
                .FirstAsync();
            if (warehouseProduct == null || product == null)
            {
                Console.WriteLine(
                    $"[ProductWarehouseReactService.SetMobileProductLocation] 绑定失败：商品不存在 ProductCode={trimmedProductCode}, WarehouseProductExists={warehouseProduct != null}, ProductExists={product != null}"
                );
                return null;
            }

            Console.WriteLine(
                $"[ProductWarehouseReactService.SetMobileProductLocation] 命中商品 ProductCode={trimmedProductCode}, ItemNumber={product.ItemNumber}, Barcode={product.Barcode}"
            );

            var trimmedLocationGuid = locationGuid?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmedLocationGuid))
            {
                var location = await _context
                    .Db.Queryable<Location>()
                    .Where(l => l.LocationGuid == trimmedLocationGuid && !l.IsDeleted)
                    .FirstAsync();
                if (location == null)
                {
                    Console.WriteLine(
                        $"[ProductWarehouseReactService.SetMobileProductLocation] 绑定失败：货位不存在 ProductCode={trimmedProductCode}, LocationGuid={trimmedLocationGuid}"
                    );
                    throw new InvalidOperationException("货位不存在");
                }

                Console.WriteLine(
                    $"[ProductWarehouseReactService.SetMobileProductLocation] 命中货位 ProductCode={trimmedProductCode}, LocationGuid={trimmedLocationGuid}, LocationCode={location.LocationCode}, LocationBarcode={location.LocationBarcode}, LocationType={location.LocationType}"
                );

                var existingSameBinding = await _context
                    .Db.Queryable<ProductLocation>()
                    .AnyAsync(pl =>
                        !pl.IsDeleted
                        && pl.ProductCode == trimmedProductCode
                        && pl.LocationGuid == trimmedLocationGuid
                    );
                if (existingSameBinding)
                {
                    Console.WriteLine(
                        $"[ProductWarehouseReactService.SetMobileProductLocation] 已存在相同绑定，直接返回 ProductCode={trimmedProductCode}, LocationGuid={trimmedLocationGuid}, LocationCode={location.LocationCode}"
                    );
                    return await GetMobileProductAsync(trimmedProductCode);
                }

                if (location.LocationType != 2)
                {
                    var existingLocationProduct = await _context
                        .Db.Queryable<ProductLocation>()
                        .Where(pl =>
                            !pl.IsDeleted
                            && pl.LocationGuid == trimmedLocationGuid
                            && pl.ProductCode != trimmedProductCode
                        )
                        .FirstAsync();
                    if (existingLocationProduct != null)
                    {
                        Console.WriteLine(
                            $"[ProductWarehouseReactService.SetMobileProductLocation] 绑定失败：配货位已有商品 ProductCode={trimmedProductCode}, LocationGuid={trimmedLocationGuid}, LocationCode={location.LocationCode}, ExistingProductCode={existingLocationProduct.ProductCode}"
                        );
                        throw new InvalidOperationException("该配货位已有商品，不能继续绑定");
                    }

                    var existingPickingLocation = await _context
                        .Db.Queryable<ProductLocation, Location>((pl, l) => new JoinQueryInfos(
                            JoinType.Inner,
                            pl.LocationGuid == l.LocationGuid
                        ))
                        .Where((pl, l) =>
                            !pl.IsDeleted
                            && !l.IsDeleted
                            && pl.ProductCode == trimmedProductCode
                            && l.LocationType != 2
                        )
                        .Select((pl, l) => new { pl.LocationGuid, l.LocationCode })
                        .FirstAsync();
                    if (existingPickingLocation != null)
                    {
                        Console.WriteLine(
                            $"[ProductWarehouseReactService.SetMobileProductLocation] 商品已有配货位，将移动到新货位 ProductCode={trimmedProductCode}, ExistingLocationGuid={existingPickingLocation.LocationGuid}, ExistingLocationCode={existingPickingLocation.LocationCode}, NewLocationGuid={trimmedLocationGuid}, NewLocationCode={location.LocationCode}"
                        );
                    }
                }
            }
            else
            {
                Console.WriteLine(
                    $"[ProductWarehouseReactService.SetMobileProductLocation] 准备清空商品货位 ProductCode={trimmedProductCode}"
                );
            }

            await _context.Db.Ado.BeginTranAsync();
            try
            {
                var deletedCount = await _context
                    .Db.Deleteable<ProductLocation>()
                    .Where(pl => pl.ProductCode == trimmedProductCode)
                    .ExecuteCommandAsync();
                Console.WriteLine(
                    $"[ProductWarehouseReactService.SetMobileProductLocation] 删除旧绑定 ProductCode={trimmedProductCode}, DeletedCount={deletedCount}"
                );

                if (!string.IsNullOrWhiteSpace(trimmedLocationGuid))
                {
                    await _context
                        .Db.Insertable(new ProductLocation
                        {
                            Guid = Guid.NewGuid().ToString(),
                            ProductCode = trimmedProductCode,
                            LocationGuid = trimmedLocationGuid,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow,
                        })
                        .ExecuteCommandAsync();
                    Console.WriteLine(
                        $"[ProductWarehouseReactService.SetMobileProductLocation] 插入新绑定 ProductCode={trimmedProductCode}, LocationGuid={trimmedLocationGuid}"
                    );
                }

                await _context.Db.Ado.CommitTranAsync();
                Console.WriteLine(
                    $"[ProductWarehouseReactService.SetMobileProductLocation] 事务提交成功 ProductCode={trimmedProductCode}, LocationGuid={trimmedLocationGuid}"
                );
            }
            catch (Exception ex)
            {
                await _context.Db.Ado.RollbackTranAsync();
                Console.WriteLine(
                    $"[ProductWarehouseReactService.SetMobileProductLocation] 事务回滚 ProductCode={trimmedProductCode}, LocationGuid={trimmedLocationGuid}, Error={ex}"
                );
                throw;
            }

            return await GetMobileProductAsync(trimmedProductCode);
        }

        private async Task<WarehouseMobileProductDto?> FindMobileProductByCodeAsync(string productCode)
        {
            var row = await _context
                .Db.Queryable<WarehouseProduct>()
                .LeftJoin<Product>((w, p) => p.ProductCode == w.ProductCode && !p.IsDeleted)
                .LeftJoin<DomesticProduct>(
                    (w, p, dp) => dp.ProductCode == w.ProductCode && !dp.IsDeleted
                )
                .LeftJoin<ChinaSupplier>(
                    (w, p, dp, s) => dp.SupplierCode == s.SupplierCode && !s.IsDeleted
                )
                .LeftJoin<ProductLocation>(
                    (w, p, dp, s, pl) => pl.ProductCode == w.ProductCode && !pl.IsDeleted
                )
                .LeftJoin<Location>(
                    (w, p, dp, s, pl, l) => l.LocationGuid == pl.LocationGuid && !l.IsDeleted
                )
                .LeftJoin<ProductGrade>(
                    (w, p, dp, s, pl, l, pg) => pg.ProductCode == w.ProductCode && !pg.IsDeleted
                )
                .Where((w, p, dp, s, pl, l, pg) =>
                    !w.IsDeleted && w.ProductCode == productCode
                )
                .Select((w, p, dp, s, pl, l, pg) => new WarehouseMobileProductDto
                {
                    ProductCode = w.ProductCode,
                    ProductName = p.ProductName ?? string.Empty,
                    ItemNumber = p.ItemNumber,
                    Barcode = p.Barcode,
                    ProductImage = SqlFunc.IsNullOrEmpty(p.ProductImage) ? dp.ProductImage : p.ProductImage,
                    ProductType = p.ProductType,
                    ProductTypeLabel = p.ProductType == 1
                        ? "套装商品"
                        : p.ProductType == 2
                            ? "多码商品"
                            : "普通商品",
                    LocalSupplierCode = p.LocalSupplierCode,
                    SupplierCode = dp.SupplierCode,
                    SupplierName = s.SupplierName,
                    Grade = pg.Grade,
                    // 兼容新旧字段，移动端新字段与旧字段始终返回同值。
                    WarehouseIsActive = w.IsActive,
                    IsActive = w.IsActive,
                    PurchasePrice = p.PurchasePrice,
                    RetailPrice = p.RetailPrice,
                    DomesticPrice = w.DomesticPrice,
                    OEMPrice = w.OEMPrice,
                    ImportPrice = w.ImportPrice,
                    StockQuantity = w.StockQuantity,
                    MiddlePackageQuantity = p.MiddlePackageQuantity,
                    PackingQuantity = SqlFunc.IsNull(w.PackingQuantity, dp.PackingQuantity),
                    Volume = SqlFunc.IsNull(w.Volume, dp.UnitVolume),
                    LocationGuid = l.LocationGuid,
                    LocationCode = l.LocationCode,
                    LocationBarcode = l.LocationBarcode,
                    UpdatedAt = SqlFunc.IsNull(p.UpdatedAt, w.UpdatedAt),
                })
                .FirstAsync();

            return row;
        }

        public async Task<WarehouseProductLabelPrintDto?> GetMobileProductPrintPayloadAsync(string productCode)
        {
            var item = await GetMobileProductAsync(productCode);
            if (item == null)
            {
                return null;
            }

            return new WarehouseProductLabelPrintDto
            {
                ProductCode = item.ProductCode,
                ProductName = item.ProductName,
                ItemNumber = item.ItemNumber,
                Barcode = item.Barcode,
                SupplierName = item.SupplierName,
                RetailPrice = item.RetailPrice,
                DomesticPrice = item.DomesticPrice,
                OEMPrice = item.OEMPrice,
                ImportPrice = item.ImportPrice,
                MiddlePackageQuantity = item.MiddlePackageQuantity,
                LocationCode = item.LocationCode,
                LocationBarcode = item.LocationBarcode,
            };
        }

        public async Task<WarehouseLocationLabelPrintDto?> GetMobileLocationPrintPayloadAsync(string productCode)
        {
            var item = await GetMobileProductAsync(productCode);
            if (item == null || string.IsNullOrWhiteSpace(item.LocationGuid))
            {
                return null;
            }

            var productCount = await _context
                .Db.Queryable<ProductLocation>()
                .Where(pl => !pl.IsDeleted && pl.LocationGuid == item.LocationGuid)
                .CountAsync();

            return new WarehouseLocationLabelPrintDto
            {
                LocationGuid = item.LocationGuid,
                LocationCode = item.LocationCode,
                LocationBarcode = item.LocationBarcode,
                ItemNumber = item.ItemNumber,
                ProductName = item.ProductName,
                MiddlePackageQuantity = item.MiddlePackageQuantity,
                ProductCount = productCount,
            };
        }

        private static string GetProductTypeLabel(int? productType)
        {
            return productType switch
            {
                0 => "普通",
                1 => "套装",
                2 => "多码",
                _ => "未知",
            };
        }

        /// <summary>
        /// 安全转换 decimal? 为 int?
        /// </summary>
        private int? SafeConvertToInt(decimal? value)
        {
            if (value == null)
                return null;
            return (int)value.Value;
        }

        /// <summary>
        /// 转换 int? 为 bool
        /// 约定：1 = true, 其他 = false
        /// </summary>
        private bool ConvertToBool(int? value)
        {
            return value == 1;
        }
    }
}
