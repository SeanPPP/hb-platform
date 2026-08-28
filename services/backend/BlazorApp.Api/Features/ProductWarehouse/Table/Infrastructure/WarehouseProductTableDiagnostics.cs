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

internal sealed class WarehouseProductTableDiagnostics : ProductWarehouseSliceBase
{
    internal WarehouseProductTableDiagnostics(ProductWarehouseSliceContext context)
        : base(context) { }

    internal static T MeasureWarehouseProductTableStage<T>(
        string stage,
        Stopwatch totalStopwatch,
        ProductWarehouseTableTimings timings,
        ProductWarehouseTableRequestSnapshot request,
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
            throw new ProductWarehouseTableQueryException(
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

    internal static async Task<T> MeasureWarehouseProductTableStageAsync<T>(
        string stage,
        Stopwatch totalStopwatch,
        ProductWarehouseTableTimings timings,
        ProductWarehouseTableRequestSnapshot request,
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
            throw new ProductWarehouseTableQueryException(
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

    internal static ProductWarehouseTableRequestSnapshot CreateWarehouseProductTableRequestSnapshot(
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

        return new ProductWarehouseTableRequestSnapshot(
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

    internal void LogWarehouseProductTablePerformance(
        ProductWarehouseTableRequestSnapshot request,
        ProductWarehouseTableTimingSnapshot timings,
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

    internal static bool IsWarehouseCodeLikeKeyword(string keyword)
    {
        // ponytail: 纯字母词可能是英文商品名（如 PEARL），不按代码型处理；纯字母代码用列过滤更明确。
        return keyword.Length >= 3
            && !keyword.Any(char.IsWhiteSpace)
            && keyword.Any(ch => char.IsDigit(ch) || ch == '-' || ch == '_' || ch == '/');
    }
}
