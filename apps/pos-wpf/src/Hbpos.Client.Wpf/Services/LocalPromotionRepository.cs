using System.Globalization;
using Hbpos.Contracts.Promotions;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Wpf.Services;

public interface ILocalPromotionRepository
{
    Task ReplaceStoreRulesAsync(
        string storeCode,
        PromotionRulesResponse response,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PromotionRuleDto>> GetActiveRulesAsync(
        string storeCode,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default);
}

public sealed class LocalPromotionRepository(LocalSqliteStore store) : ILocalPromotionRepository
{
    public async Task ReplaceStoreRulesAsync(
        string storeCode,
        PromotionRulesResponse response,
        CancellationToken cancellationToken = default)
    {
        var normalizedStoreCode = NormalizeStoreCode(storeCode);
        if (string.IsNullOrEmpty(normalizedStoreCode))
        {
            throw new ArgumentException("Promotion store code is required.", nameof(storeCode));
        }

        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        // 先在同一个事务里清掉当前门店旧规则，再写入新快照；只要中途失败，事务就不会提交，其他门店和旧缓存都会原样保留。
        await DeleteStoreRulesAsync(connection, transaction, normalizedStoreCode, cancellationToken);

        await using var promotionCommand = connection.CreateCommand();
        promotionCommand.Transaction = transaction;
        promotionCommand.CommandText = InsertPromotionSql;
        AddPromotionParameters(promotionCommand);
        promotionCommand.Prepare();

        await using var productCommand = connection.CreateCommand();
        productCommand.Transaction = transaction;
        productCommand.CommandText = InsertPromotionProductSql;
        AddPromotionProductParameters(productCommand);
        productCommand.Prepare();

        var syncedAt = response.GeneratedAt.ToUniversalTime();
        foreach (var rule in response.Rules)
        {
            SetPromotionParameters(promotionCommand, normalizedStoreCode, rule, syncedAt);
            await promotionCommand.ExecuteNonQueryAsync(cancellationToken);

            foreach (var product in rule.Products)
            {
                SetPromotionProductParameters(productCommand, normalizedStoreCode, rule.Id, product);
                await productCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PromotionRuleDto>> GetActiveRulesAsync(
        string storeCode,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        var normalizedStoreCode = NormalizeStoreCode(storeCode);
        if (string.IsNullOrEmpty(normalizedStoreCode))
        {
            return [];
        }

        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        var promotionRows = await ReadPromotionRowsAsync(
            connection,
            normalizedStoreCode,
            asOf.ToUniversalTime(),
            cancellationToken);
        if (promotionRows.Count == 0)
        {
            return [];
        }

        var productsByPromotionId = await ReadProductsByPromotionIdAsync(
            connection,
            normalizedStoreCode,
            promotionRows.Select(row => row.PromotionId).ToArray(),
            cancellationToken);

        return promotionRows
            .Select(row => new PromotionRuleDto(
                row.PromotionId,
                row.Name,
                row.EffectiveStart,
                row.EffectiveEnd,
                row.IsExclusive,
                row.Priority,
                row.ApplyQuantity,
                row.FixedPrice,
                row.MaxApplicationsPerOrder,
                productsByPromotionId.TryGetValue(row.PromotionId, out var products) ? products : []))
            .ToArray();
    }

    private static async Task DeleteStoreRulesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string storeCode,
        CancellationToken cancellationToken)
    {
        await using (var deleteProducts = connection.CreateCommand())
        {
            deleteProducts.Transaction = transaction;
            deleteProducts.CommandText = "DELETE FROM LocalPromotionProducts WHERE StoreCode = $StoreCode;";
            deleteProducts.Parameters.AddWithValue("$StoreCode", storeCode);
            await deleteProducts.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var deletePromotions = connection.CreateCommand();
        deletePromotions.Transaction = transaction;
        deletePromotions.CommandText = "DELETE FROM LocalPromotions WHERE StoreCode = $StoreCode;";
        deletePromotions.Parameters.AddWithValue("$StoreCode", storeCode);
        await deletePromotions.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<List<LocalPromotionRow>> ReadPromotionRowsAsync(
        SqliteConnection connection,
        string storeCode,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                PromotionId,
                Name,
                EffectiveStart,
                EffectiveEnd,
                IsExclusive,
                Priority,
                ApplyQuantity,
                FixedPrice,
                MaxApplicationsPerOrder
            FROM LocalPromotions
            WHERE StoreCode = $StoreCode
              AND EffectiveStart <= $AsOf
              AND EffectiveEnd >= $AsOf
            ORDER BY Priority DESC, EffectiveStart ASC, PromotionId ASC;
            """;
        command.Parameters.AddWithValue("$StoreCode", storeCode);
        command.Parameters.AddWithValue("$AsOf", FormatUtc(asOf));

        var rows = new List<LocalPromotionRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LocalPromotionRow(
                reader.GetString(0),
                reader.GetString(1),
                ParseUtcDateTime(reader.GetString(2)),
                ParseUtcDateTime(reader.GetString(3)),
                reader.GetInt32(4) == 1,
                reader.GetInt32(5),
                reader.GetInt32(6),
                decimal.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
                reader.IsDBNull(8) ? null : reader.GetInt32(8)));
        }

        return rows;
    }

    private static async Task<Dictionary<string, IReadOnlyList<PromotionRuleProductDto>>> ReadProductsByPromotionIdAsync(
        SqliteConnection connection,
        string storeCode,
        IReadOnlyList<string> promotionIds,
        CancellationToken cancellationToken)
    {
        if (promotionIds.Count == 0)
        {
            return [];
        }

        await using var command = connection.CreateCommand();
        var parameterNames = new List<string>(promotionIds.Count);
        for (var index = 0; index < promotionIds.Count; index++)
        {
            var parameterName = $"$PromotionId{index}";
            parameterNames.Add(parameterName);
            command.Parameters.AddWithValue(parameterName, promotionIds[index]);
        }

        command.CommandText = $"""
            SELECT PromotionId, ProductCode, UnitWeight
            FROM LocalPromotionProducts
            WHERE StoreCode = $StoreCode
              AND PromotionId IN ({string.Join(", ", parameterNames)})
            ORDER BY PromotionId ASC, ProductCode ASC;
            """;
        command.Parameters.AddWithValue("$StoreCode", storeCode);

        var rows = new List<(string PromotionId, PromotionRuleProductDto Product)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((
                reader.GetString(0),
                new PromotionRuleProductDto(
                    reader.GetString(1),
                    reader.GetInt32(2))));
        }

        return rows
            .GroupBy(row => row.PromotionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PromotionRuleProductDto>)group.Select(item => item.Product).ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static void AddPromotionParameters(SqliteCommand command)
    {
        command.Parameters.Add("$StoreCode", SqliteType.Text);
        command.Parameters.Add("$PromotionId", SqliteType.Text);
        command.Parameters.Add("$Name", SqliteType.Text);
        command.Parameters.Add("$EffectiveStart", SqliteType.Text);
        command.Parameters.Add("$EffectiveEnd", SqliteType.Text);
        command.Parameters.Add("$IsExclusive", SqliteType.Integer);
        command.Parameters.Add("$Priority", SqliteType.Integer);
        command.Parameters.Add("$ApplyQuantity", SqliteType.Integer);
        command.Parameters.Add("$FixedPrice", SqliteType.Text);
        command.Parameters.Add("$MaxApplicationsPerOrder", SqliteType.Integer);
        command.Parameters.Add("$SyncedAt", SqliteType.Text);
    }

    private static void SetPromotionParameters(
        SqliteCommand command,
        string storeCode,
        PromotionRuleDto rule,
        DateTimeOffset syncedAt)
    {
        command.Parameters["$StoreCode"].Value = storeCode;
        command.Parameters["$PromotionId"].Value = NormalizeRequiredValue(rule.Id, nameof(rule.Id));
        command.Parameters["$Name"].Value = NormalizeRequiredValue(rule.Name, nameof(rule.Name));
        command.Parameters["$EffectiveStart"].Value = FormatUtc(ToUtc(rule.EffectiveStart));
        command.Parameters["$EffectiveEnd"].Value = FormatUtc(ToUtc(rule.EffectiveEnd));
        command.Parameters["$IsExclusive"].Value = rule.IsExclusive ? 1 : 0;
        command.Parameters["$Priority"].Value = rule.Priority;
        command.Parameters["$ApplyQuantity"].Value = rule.ApplyQuantity;
        command.Parameters["$FixedPrice"].Value = rule.FixedPrice.ToString(CultureInfo.InvariantCulture);
        command.Parameters["$MaxApplicationsPerOrder"].Value = rule.MaxApplicationsPerOrder.HasValue
            ? rule.MaxApplicationsPerOrder.Value
            : DBNull.Value;
        command.Parameters["$SyncedAt"].Value = FormatUtc(syncedAt);
    }

    private static void AddPromotionProductParameters(SqliteCommand command)
    {
        command.Parameters.Add("$StoreCode", SqliteType.Text);
        command.Parameters.Add("$PromotionId", SqliteType.Text);
        command.Parameters.Add("$ProductCode", SqliteType.Text);
        command.Parameters.Add("$UnitWeight", SqliteType.Integer);
    }

    private static void SetPromotionProductParameters(
        SqliteCommand command,
        string storeCode,
        string promotionId,
        PromotionRuleProductDto product)
    {
        command.Parameters["$StoreCode"].Value = storeCode;
        command.Parameters["$PromotionId"].Value = NormalizeRequiredValue(promotionId, nameof(promotionId));
        command.Parameters["$ProductCode"].Value = NormalizeRequiredValue(product.ProductCode, nameof(product.ProductCode));
        command.Parameters["$UnitWeight"].Value = product.UnitWeight;
    }

    private static string NormalizeStoreCode(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static string NormalizeRequiredValue(string? value, string paramName)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Promotion value is required.", paramName);
        }

        return normalized;
    }

    private static DateTimeOffset ToUtc(DateTime value)
    {
        var utcValue = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return new DateTimeOffset(utcValue, TimeSpan.Zero);
    }

    private static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTime ParseUtcDateTime(string value)
    {
        return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
    }

    private sealed record LocalPromotionRow(
        string PromotionId,
        string Name,
        DateTime EffectiveStart,
        DateTime EffectiveEnd,
        bool IsExclusive,
        int Priority,
        int ApplyQuantity,
        decimal FixedPrice,
        int? MaxApplicationsPerOrder);

    private const string InsertPromotionSql = """
        INSERT INTO LocalPromotions (
            StoreCode,
            PromotionId,
            Name,
            EffectiveStart,
            EffectiveEnd,
            IsExclusive,
            Priority,
            ApplyQuantity,
            FixedPrice,
            MaxApplicationsPerOrder,
            SyncedAt)
        VALUES (
            $StoreCode,
            $PromotionId,
            $Name,
            $EffectiveStart,
            $EffectiveEnd,
            $IsExclusive,
            $Priority,
            $ApplyQuantity,
            $FixedPrice,
            $MaxApplicationsPerOrder,
            $SyncedAt);
        """;

    private const string InsertPromotionProductSql = """
        INSERT INTO LocalPromotionProducts (
            StoreCode,
            PromotionId,
            ProductCode,
            UnitWeight)
        VALUES (
            $StoreCode,
            $PromotionId,
            $ProductCode,
            $UnitWeight);
        """;
}
