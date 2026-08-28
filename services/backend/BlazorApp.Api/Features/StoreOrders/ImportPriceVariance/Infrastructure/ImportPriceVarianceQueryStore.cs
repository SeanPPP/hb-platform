using BlazorApp.Api.Data;
using BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Domain;
using BlazorApp.Shared.DTOs;
using SqlSugar;

namespace BlazorApp.Api.Features.StoreOrders.ImportPriceVariance.Infrastructure;

internal sealed class ImportPriceVarianceQueryStore(SqlSugarContext context)
{
    private readonly ISqlSugarClient _db = context.Db;

    internal async Task<StoreOrderImportPriceVarianceResultDto> GetSummaryAsync(
        ImportPriceVarianceQueryInput input,
        IReadOnlyList<string> storeCodes
    )
    {
        var sql = ImportPriceVarianceSqlBuilder.BuildSummary(
            input.Query,
            storeCodes,
            input.Page,
            _db.CurrentConnectionConfig.DbType == DbType.Sqlite
        );
        var summaryRow = (
            await _db.Ado.SqlQueryAsync<ImportPriceVarianceSummarySqlRow>(
                sql.SummarySql,
                sql.Parameters.ToArray()
            )
        ).FirstOrDefault() ?? new ImportPriceVarianceSummarySqlRow();
        var pageItems = await _db.Ado.SqlQueryAsync<StoreOrderImportPriceVarianceItemDto>(
            sql.PagedSql,
            sql.Parameters.ToArray()
        );
        var supplierSummaries =
            await _db.Ado.SqlQueryAsync<StoreOrderImportPriceVarianceSupplierSummaryDto>(
                sql.SupplierSummarySql,
                sql.Parameters.ToArray()
            );

        return new StoreOrderImportPriceVarianceResultDto
        {
            Items = pageItems,
            Total = summaryRow.TotalRows,
            PageNumber = input.Page.PageNumber,
            PageSize = input.Page.PageSize,
            Summary = CreateSummary(summaryRow),
            SupplierSummaries = supplierSummaries,
        };
    }

    internal async Task<StoreOrderImportPriceVarianceDetailResultDto> GetDetailsAsync(
        ImportPriceVarianceDetailQueryInput input,
        IReadOnlyList<string> storeCodes
    )
    {
        var sql = ImportPriceVarianceSqlBuilder.BuildDetails(
            input.Query,
            input.ProductCode!,
            storeCodes,
            input.Page,
            _db.CurrentConnectionConfig.DbType == DbType.Sqlite
        );
        var summaryRow = (
            await _db.Ado.SqlQueryAsync<ImportPriceVarianceSummarySqlRow>(
                sql.SummarySql,
                sql.Parameters.ToArray()
            )
        ).FirstOrDefault() ?? new ImportPriceVarianceSummarySqlRow();
        var pageItems =
            await _db.Ado.SqlQueryAsync<StoreOrderImportPriceVarianceDetailItemDto>(
                sql.PagedSql,
                sql.Parameters.ToArray()
            );

        return new StoreOrderImportPriceVarianceDetailResultDto
        {
            Items = pageItems,
            Total = summaryRow.TotalRows,
            PageNumber = input.Page.PageNumber,
            PageSize = input.Page.PageSize,
            Summary = CreateSummary(summaryRow),
        };
    }

    private static StoreOrderImportPriceVarianceSummaryDto CreateSummary(
        ImportPriceVarianceSummarySqlRow summaryRow
    )
    {
        return new StoreOrderImportPriceVarianceSummaryDto
        {
            TotalRows = summaryRow.TotalRows,
            OriginalImportAmountTotal = summaryRow.OriginalImportAmountTotal,
            BaselineImportAmountTotal = summaryRow.BaselineImportAmountTotal,
            VarianceAmountTotal = summaryRow.VarianceAmountTotal,
        };
    }

    private sealed class ImportPriceVarianceSummarySqlRow
    {
        public int TotalRows { get; set; }

        public decimal OriginalImportAmountTotal { get; set; }

        public decimal BaselineImportAmountTotal { get; set; }

        public decimal VarianceAmountTotal { get; set; }
    }
}
