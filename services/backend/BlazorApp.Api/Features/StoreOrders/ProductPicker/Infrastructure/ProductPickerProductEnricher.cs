using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.ProductPicker.Domain;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.ProductPicker.Infrastructure;

internal sealed class ProductPickerProductEnricher(SqlSugarContext context)
{
    private readonly ISqlSugarClient _db = context.Db;

    internal async Task PopulateGradesAsync(
        ISqlSugarClient db,
        List<StoreOrderProductDto> items,
        IReadOnlyList<string> normalizedGrades,
        CancellationToken cancellationToken = default
    )
    {
        var productCodes = items
            .Select(item => item.ProductCode)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (productCodes.Count == 0)
        {
            return;
        }

        var gradeRows = await db.Queryable<ProductGrade>()
            .Where(grade => productCodes.Contains(grade.ProductCode) && !grade.IsDeleted)
            .OrderBy(grade => grade.Grade)
            .Select(grade => new { grade.ProductCode, grade.Grade })
            .ToListAsync();

        cancellationToken.ThrowIfCancellationRequested();

        var gradeMap = gradeRows
            .GroupBy(row => row.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group =>
                    normalizedGrades.Count > 0
                        ? group.FirstOrDefault(row => normalizedGrades.Contains(row.Grade))?.Grade
                            ?? group.First().Grade
                        : group.First().Grade,
                StringComparer.OrdinalIgnoreCase
            );

        foreach (var item in items)
        {
            if (gradeMap.TryGetValue(item.ProductCode, out var grade))
            {
                item.Grade = grade;
            }
        }
    }

    internal async Task PopulateDomesticSuppliersForOrderPickerAsync(
        List<StoreOrderProductDto> items
    )
    {
        if (items.Count == 0)
        {
            return;
        }

        var productCodes = items
            .Select(item => ProductPickerRules.NormalizeMatchKey(item.ProductCode))
            .Where(key => key != null)
            .Select(key => key!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var itemNumbers = items
            .Select(item => ProductPickerRules.NormalizeMatchKey(item.ItemNumber))
            .Where(key => key != null)
            .Select(key => key!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var barcodes = items
            .Select(item => ProductPickerRules.NormalizeMatchKey(item.Barcode))
            .Where(key => key != null)
            .Select(key => key!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (productCodes.Count == 0 && itemNumbers.Count == 0 && barcodes.Count == 0)
        {
            return;
        }

        // 不把 C# 布尔开关放入 SqlSugar 表达式，避免 SQL Server 生成裸布尔条件。
        var supplierMatch = Expressionable.Create<DomesticProduct, ChinaSupplier>();
        if (productCodes.Count > 0)
        {
            supplierMatch = supplierMatch.Or(
                (domesticProduct, supplier) =>
                    domesticProduct.ProductCode != null
                    && productCodes.Contains(domesticProduct.ProductCode)
            );
        }

        if (itemNumbers.Count > 0)
        {
            supplierMatch = supplierMatch.Or(
                (domesticProduct, supplier) =>
                    domesticProduct.HBProductNo != null
                    && itemNumbers.Contains(domesticProduct.HBProductNo)
            );
        }

        if (barcodes.Count > 0)
        {
            supplierMatch = supplierMatch.Or(
                (domesticProduct, supplier) =>
                    domesticProduct.Barcode != null
                    && barcodes.Contains(domesticProduct.Barcode)
            );
        }

        var candidates = await _db.Queryable<DomesticProduct>()
            .InnerJoin<ChinaSupplier>(
                (domesticProduct, supplier) =>
                    domesticProduct.SupplierCode == supplier.SupplierCode
                    && !supplier.IsDeleted
            )
            .Where((domesticProduct, supplier) => !domesticProduct.IsDeleted)
            .Where(supplierMatch.ToExpression())
            .Select(
                (domesticProduct, supplier) =>
                    new DomesticSupplierCandidate
                    {
                        ProductCode = domesticProduct.ProductCode,
                        HBProductNo = domesticProduct.HBProductNo,
                        Barcode = domesticProduct.Barcode,
                        SupplierCode = domesticProduct.SupplierCode,
                        SupplierName = supplier.SupplierName,
                    }
            )
            .ToListAsync();

        var orderedCandidates = candidates
            .Where(candidate =>
                !string.IsNullOrWhiteSpace(candidate.SupplierCode)
                || !string.IsNullOrWhiteSpace(candidate.SupplierName)
            )
            .OrderBy(candidate => candidate.SupplierCode ?? string.Empty)
            .ThenBy(candidate => candidate.ProductCode ?? string.Empty)
            .ToList();

        foreach (var item in items)
        {
            var candidate = FindDomesticSupplierCandidate(item, orderedCandidates);
            if (candidate == null)
            {
                continue;
            }

            item.DomesticSupplierCode = candidate.SupplierCode;
            item.DomesticSupplierName = candidate.SupplierName;
        }
    }

    private static DomesticSupplierCandidate? FindDomesticSupplierCandidate(
        StoreOrderProductDto item,
        IReadOnlyList<DomesticSupplierCandidate> candidates
    )
    {
        return candidates.FirstOrDefault(candidate =>
                ProductPickerRules.MatchNonEmpty(candidate.ProductCode, item.ProductCode)
            )
            ?? candidates.FirstOrDefault(candidate =>
                ProductPickerRules.MatchNonEmpty(candidate.HBProductNo, item.ItemNumber)
            )
            ?? candidates.FirstOrDefault(candidate =>
                ProductPickerRules.MatchNonEmpty(candidate.Barcode, item.Barcode)
            );
    }

    private sealed class DomesticSupplierCandidate
    {
        public string? ProductCode { get; set; }

        public string? HBProductNo { get; set; }

        public string? Barcode { get; set; }

        public string? SupplierCode { get; set; }

        public string? SupplierName { get; set; }
    }
}
