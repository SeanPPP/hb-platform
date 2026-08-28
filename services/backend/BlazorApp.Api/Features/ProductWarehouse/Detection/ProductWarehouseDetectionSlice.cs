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

namespace BlazorApp.Api.Features.ProductWarehouse;

internal sealed class ProductWarehouseDetectionSlice
    : ProductWarehouseSliceBase,
      IProductWarehouseDetectionSlice
{
    internal ProductWarehouseDetectionSlice(ProductWarehouseSliceContext context)
        : base(context) { }

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
}
