using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Hbpos.Contracts.Catalog;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Wpf.Services;

public sealed record LocalSellableItemCompareRow(
    string StoreCode,
    string LookupCodeNormalized,
    string ContentHash,
    DateTimeOffset? SyncedAt);

public interface ILocalCatalogRepository
{
    Task ReplaceSellableItemsAsync(IEnumerable<SellableItemDto> items, CancellationToken cancellationToken = default);

    Task UpsertSellableItemsAsync(IEnumerable<SellableItemDto> items, CancellationToken cancellationToken = default);

    Task<int> DeleteByLookupCodesAsync(string storeCode, IEnumerable<string> lookupCodes, CancellationToken cancellationToken = default);

    Task<SellableItemDto?> FindByLookupCodeAsync(string storeCode, string lookupCode, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalSellableItemCompareRow>> LoadSellableItemComparePageAsync(
        string storeCode,
        string? afterLookupCodeNormalized,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SellableItemDto>> LoadSellableItemsAsync(CancellationToken cancellationToken = default);
}

public sealed class LocalCatalogRepository(LocalSqliteStore store) : ILocalCatalogRepository
{
    public Task ReplaceSellableItemsAsync(IEnumerable<SellableItemDto> items, CancellationToken cancellationToken = default)
    {
        return UpsertSellableItemsAsync(items, cancellationToken);
    }

    public async Task UpsertSellableItemsAsync(IEnumerable<SellableItemDto> items, CancellationToken cancellationToken = default)
    {
        var materializedItems = items.ToList();
        if (materializedItems.Count == 0)
        {
            return;
        }

        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        var syncedAt = DateTimeOffset.UtcNow;

        foreach (var item in materializedItems)
        {
            var storeCode = NormalizeStoreCode(item.StoreCode);
            var lookupCodeNormalized = NormalizeLookupCode(item.LookupCode);
            if (string.IsNullOrEmpty(storeCode))
            {
                throw new ArgumentException("Sellable item store code is required.", nameof(items));
            }

            if (string.IsNullOrEmpty(lookupCodeNormalized))
            {
                throw new ArgumentException("Sellable item lookup code is required.", nameof(items));
            }

            var contentHash = CreateContentHash(item, storeCode, lookupCodeNormalized);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO LocalSellableItemIndex
                (
                    StoreCode,
                    ProductCode,
                    ReferenceCode,
                    DisplayName,
                    LookupCode,
                    LookupCodeNormalized,
                    ItemNumber,
                    Barcode,
                    ProductImage,
                    DiscountRate,
                    RetailPrice,
                    PriceSource,
                    PriceSourceLabel,
                    QuantityFactor,
                    UpdatedAt,
                    ContentHash,
                    SyncedAt
                )
                VALUES
                (
                    $StoreCode,
                    $ProductCode,
                    $ReferenceCode,
                    $DisplayName,
                    $LookupCode,
                    $LookupCodeNormalized,
                    $ItemNumber,
                    $Barcode,
                    $ProductImage,
                    $DiscountRate,
                    $RetailPrice,
                    $PriceSource,
                    $PriceSourceLabel,
                    $QuantityFactor,
                    $UpdatedAt,
                    $ContentHash,
                    $SyncedAt
                )
                ON CONFLICT(StoreCode, LookupCodeNormalized) DO UPDATE SET
                    ProductCode = excluded.ProductCode,
                    ReferenceCode = excluded.ReferenceCode,
                    DisplayName = excluded.DisplayName,
                    LookupCode = excluded.LookupCode,
                    ItemNumber = excluded.ItemNumber,
                    Barcode = excluded.Barcode,
                    ProductImage = excluded.ProductImage,
                    DiscountRate = excluded.DiscountRate,
                    RetailPrice = excluded.RetailPrice,
                    PriceSource = excluded.PriceSource,
                    PriceSourceLabel = excluded.PriceSourceLabel,
                    QuantityFactor = excluded.QuantityFactor,
                    UpdatedAt = excluded.UpdatedAt,
                    ContentHash = excluded.ContentHash,
                    SyncedAt = excluded.SyncedAt;
                """;

            AddItemParameters(command, item, storeCode, lookupCodeNormalized, contentHash, syncedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> DeleteByLookupCodesAsync(
        string storeCode,
        IEnumerable<string> lookupCodes,
        CancellationToken cancellationToken = default)
    {
        var normalizedStoreCode = NormalizeStoreCode(storeCode);
        if (string.IsNullOrEmpty(normalizedStoreCode))
        {
            return 0;
        }

        var normalizedLookupCodes = lookupCodes
            .Select(NormalizeLookupCode)
            .Where(code => !string.IsNullOrEmpty(code))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalizedLookupCodes.Length == 0)
        {
            return 0;
        }

        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        var deleted = 0;

        foreach (var lookupCodeNormalized in normalizedLookupCodes)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                DELETE FROM LocalSellableItemIndex
                WHERE StoreCode = $StoreCode
                  AND LookupCodeNormalized = $LookupCodeNormalized;
                """;
            command.Parameters.AddWithValue("$StoreCode", normalizedStoreCode);
            command.Parameters.AddWithValue("$LookupCodeNormalized", lookupCodeNormalized);
            deleted += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    public async Task<SellableItemDto?> FindByLookupCodeAsync(
        string storeCode,
        string lookupCode,
        CancellationToken cancellationToken = default)
    {
        var normalizedStoreCode = NormalizeStoreCode(storeCode);
        var lookupCodeNormalized = NormalizeLookupCode(lookupCode);
        if (string.IsNullOrEmpty(normalizedStoreCode) || string.IsNullOrEmpty(lookupCodeNormalized))
        {
            return null;
        }

        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {SelectSellableItemSql}
            WHERE StoreCode = $StoreCode
              AND LookupCodeNormalized = $LookupCodeNormalized
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$StoreCode", normalizedStoreCode);
        command.Parameters.AddWithValue("$LookupCodeNormalized", lookupCodeNormalized);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSellableItem(reader) : null;
    }

    public async Task<IReadOnlyList<LocalSellableItemCompareRow>> LoadSellableItemComparePageAsync(
        string storeCode,
        string? afterLookupCodeNormalized,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var normalizedStoreCode = NormalizeStoreCode(storeCode);
        if (string.IsNullOrEmpty(normalizedStoreCode))
        {
            return [];
        }

        var cursor = string.IsNullOrWhiteSpace(afterLookupCodeNormalized)
            ? null
            : NormalizeLookupCode(afterLookupCodeNormalized);

        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT StoreCode, LookupCodeNormalized, ContentHash, SyncedAt
            FROM LocalSellableItemIndex
            WHERE StoreCode = $StoreCode
              AND ($AfterLookupCodeNormalized IS NULL OR LookupCodeNormalized > $AfterLookupCodeNormalized)
            ORDER BY StoreCode, LookupCodeNormalized
            LIMIT $PageSize;
            """;
        command.Parameters.AddWithValue("$StoreCode", normalizedStoreCode);
        command.Parameters.AddWithValue("$AfterLookupCodeNormalized", (object?)cursor ?? DBNull.Value);
        command.Parameters.AddWithValue("$PageSize", Math.Clamp(pageSize, 1, 1000));

        var rows = new List<LocalSellableItemCompareRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new LocalSellableItemCompareRow(
                ReadString(reader, "StoreCode"),
                ReadString(reader, "LookupCodeNormalized"),
                ReadString(reader, "ContentHash"),
                ReadNullableDateTimeOffset(reader, "SyncedAt")));
        }

        return rows;
    }

    public async Task<IReadOnlyList<SellableItemDto>> LoadSellableItemsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            {SelectSellableItemSql}
            ORDER BY StoreCode, LookupCodeNormalized;
            """;

        var items = new List<SellableItemDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadSellableItem(reader));
        }

        return items;
    }

    private const string SelectSellableItemSql = """
        SELECT StoreCode, ProductCode, ReferenceCode, DisplayName, LookupCode, ItemNumber, Barcode, ProductImage, DiscountRate, RetailPrice, PriceSource, PriceSourceLabel, QuantityFactor, UpdatedAt
        FROM LocalSellableItemIndex
        """;

    private static void AddItemParameters(
        SqliteCommand command,
        SellableItemDto item,
        string storeCode,
        string lookupCodeNormalized,
        string contentHash,
        DateTimeOffset syncedAt)
    {
        command.Parameters.AddWithValue("$StoreCode", storeCode);
        command.Parameters.AddWithValue("$ProductCode", item.ProductCode);
        command.Parameters.AddWithValue("$ReferenceCode", (object?)item.ReferenceCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$DisplayName", item.DisplayName);
        command.Parameters.AddWithValue("$LookupCode", item.LookupCode);
        command.Parameters.AddWithValue("$LookupCodeNormalized", lookupCodeNormalized);
        command.Parameters.AddWithValue("$ItemNumber", (object?)item.ItemNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$Barcode", (object?)item.Barcode ?? DBNull.Value);
        command.Parameters.AddWithValue("$ProductImage", (object?)item.ProductImage ?? DBNull.Value);
        command.Parameters.AddWithValue("$DiscountRate", (object?)item.DiscountRate ?? DBNull.Value);
        command.Parameters.AddWithValue("$RetailPrice", item.RetailPrice);
        command.Parameters.AddWithValue("$PriceSource", (int)item.PriceSource);
        command.Parameters.AddWithValue("$PriceSourceLabel", item.PriceSourceLabel);
        command.Parameters.AddWithValue("$QuantityFactor", item.QuantityFactor);
        command.Parameters.AddWithValue("$UpdatedAt", (object?)item.UpdatedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$ContentHash", contentHash);
        command.Parameters.AddWithValue("$SyncedAt", syncedAt.ToString("O"));
    }

    private static SellableItemDto ReadSellableItem(SqliteDataReader reader)
    {
        return new SellableItemDto(
            ReadString(reader, "StoreCode"),
            ReadString(reader, "ProductCode"),
            ReadNullableString(reader, "ReferenceCode"),
            ReadString(reader, "DisplayName"),
            ReadString(reader, "LookupCode"),
            ReadNullableString(reader, "ItemNumber"),
            ReadNullableString(reader, "Barcode"),
            ReadDecimal(reader, "RetailPrice"),
            (PriceSourceKind)ReadInt32(reader, "PriceSource"),
            ReadString(reader, "PriceSourceLabel"),
            ReadDecimal(reader, "QuantityFactor"),
            ReadNullableDateTimeOffset(reader, "UpdatedAt"),
            ReadNullableString(reader, "ProductImage"),
            ReadNullableDecimal(reader, "DiscountRate"));
    }

    private static string CreateContentHash(SellableItemDto item, string storeCode, string lookupCodeNormalized)
    {
        var builder = new StringBuilder();
        AppendCanonical(builder, storeCode);
        AppendCanonical(builder, item.ProductCode.Trim());
        AppendCanonical(builder, item.ReferenceCode?.Trim() ?? string.Empty);
        AppendCanonical(builder, item.DisplayName.Trim());
        AppendCanonical(builder, lookupCodeNormalized);
        AppendCanonical(builder, item.ItemNumber?.Trim() ?? string.Empty);
        AppendCanonical(builder, item.Barcode?.Trim() ?? string.Empty);
        AppendCanonical(builder, item.RetailPrice.ToString("0.#############################", CultureInfo.InvariantCulture));
        AppendCanonical(builder, ((int)item.PriceSource).ToString(CultureInfo.InvariantCulture));
        AppendCanonical(builder, item.PriceSourceLabel.Trim());
        AppendCanonical(builder, item.QuantityFactor.ToString("0.#############################", CultureInfo.InvariantCulture));
        AppendCanonical(builder, item.ProductImage ?? string.Empty);
        AppendCanonical(builder, FormatNullableDecimal(item.DiscountRate));

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hashBytes);
    }

    private static void AppendCanonical(StringBuilder builder, string value)
    {
        builder
            .Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append('|');
    }

    private static string NormalizeStoreCode(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static string NormalizeLookupCode(string? value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static string ReadString(SqliteDataReader reader, string name)
    {
        return reader.GetString(reader.GetOrdinal(name));
    }

    private static string? ReadNullableString(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int ReadInt32(SqliteDataReader reader, string name)
    {
        var value = reader.GetValue(reader.GetOrdinal(name));
        return value switch
        {
            int intValue => intValue,
            long longValue => (int)longValue,
            string stringValue => int.Parse(stringValue, CultureInfo.InvariantCulture),
            _ => Convert.ToInt32(value, CultureInfo.InvariantCulture)
        };
    }

    private static decimal ReadDecimal(SqliteDataReader reader, string name)
    {
        var value = reader.GetValue(reader.GetOrdinal(name));
        return value switch
        {
            decimal decimalValue => decimalValue,
            double doubleValue => Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture),
            long longValue => longValue,
            int intValue => intValue,
            string stringValue => decimal.Parse(stringValue, CultureInfo.InvariantCulture),
            _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture)
        };
    }

    private static decimal? ReadNullableDecimal(SqliteDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            decimal decimalValue => decimalValue,
            double doubleValue => Convert.ToDecimal(doubleValue, CultureInfo.InvariantCulture),
            long longValue => longValue,
            int intValue => intValue,
            string stringValue when string.IsNullOrWhiteSpace(stringValue) => null,
            string stringValue => decimal.Parse(stringValue, CultureInfo.InvariantCulture),
            _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture)
        };
    }

    private static string FormatNullableDecimal(decimal? value)
    {
        return value?.ToString("0.#############################", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(SqliteDataReader reader, string name)
    {
        var value = ReadNullableString(reader, name);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}
