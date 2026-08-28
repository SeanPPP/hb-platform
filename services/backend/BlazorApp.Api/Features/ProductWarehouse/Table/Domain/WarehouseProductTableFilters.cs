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

internal static class WarehouseProductTableFilters
{
    private const int PickingLocationType = 1;

    internal static ISugarQueryable<
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

    internal static Expression<
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

    internal static Expression<
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

    internal static ISugarQueryable<
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

    internal static List<(string Mode, string Value)> ParseTextMatchFilterTokens(
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

    internal static List<string> NormalizeWarehouseExactTextFilterValues(IEnumerable<string> values)
    {
        // 精确 code 筛选保持列侧原值，避免 ToLower/Contains 包列导致索引不可用。
        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct()
            .ToList();
    }

    internal static ISugarQueryable<
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

    internal static ISugarQueryable<
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

    internal static ISugarQueryable<
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

    internal static (decimal? Minimum, decimal? Maximum, List<decimal> ExactValues) ParseDecimalRangeTokens(
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

    internal static (int? Minimum, int? Maximum, List<int> ExactValues) ParseIntRangeTokens(
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

    internal static (DateTime? StartAt, DateTime? EndAt, List<(DateTime StartAt, DateTime EndAt)> EqualRanges) ParseDateRangeTokens(
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

    internal static List<bool> ParseBooleanFilterValues(IEnumerable<string> values)
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

    internal static List<int> ParseIntFilterValues(IEnumerable<string> values)
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

    internal static bool TryParseFilterToken(
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

    internal static bool TryParseFilterDate(string value, out DateTime parsedValue)
    {
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out parsedValue
        ) || DateTime.TryParse(value, out parsedValue);
    }

    internal static DateTime NormalizeFilterEndDate(string rawValue, DateTime parsedValue)
    {
        // 纯日期的 lte 视为当天结束，避免前端只传日期时漏掉当天更新的数据。
        return rawValue.Contains('T') || rawValue.Contains(' ')
            ? parsedValue
            : parsedValue.Date.AddDays(1).AddTicks(-1);
    }
}
