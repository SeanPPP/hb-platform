namespace Hbpos.Client.Wpf.Services;

public interface ILocalAppSettingsRepository
{
    Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default);

    async Task<LocalAppSettingEntry?> GetEntryAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var value = await GetValueAsync(key, cancellationToken);
        return value is null ? null : new LocalAppSettingEntry(value, null);
    }

    Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default);

    Task SetValuesAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default);

    Task DeleteValueAsync(string key, CancellationToken cancellationToken = default);
}

public sealed record LocalAppSettingEntry(string Value, DateTimeOffset? UpdatedAt);

public sealed class LocalAppSettingsRepository(LocalSqliteStore store) : ILocalAppSettingsRepository
{
    public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        var keySnapshot = key;
        return Task.Run(async () =>
        {
            await using var connection = await store.OpenConnectionAsync(cancellationToken);
            await EnsureAppSettingsTableAsync(connection, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Value
                FROM AppSettings
                WHERE Key = $Key;
                """;
            command.Parameters.AddWithValue("$Key", keySnapshot);

            return await command.ExecuteScalarAsync(cancellationToken) as string;
        }, cancellationToken);
    }

    public Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        return SetValuesAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [key] = value,
            },
            cancellationToken);
    }

    public Task<LocalAppSettingEntry?> GetEntryAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var keySnapshot = key;
        return Task.Run(async () =>
        {
            await using var connection = await store.OpenConnectionAsync(cancellationToken);
            await EnsureAppSettingsTableAsync(connection, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Value, UpdatedAt
                FROM AppSettings
                WHERE Key = $Key;
                """;
            command.Parameters.AddWithValue("$Key", keySnapshot);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var value = reader.GetString(0);
            var updatedAtText = reader.GetString(1);
            var updatedAt = DateTimeOffset.TryParse(
                updatedAtText,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var parsedUpdatedAt)
                ? parsedUpdatedAt
                : (DateTimeOffset?)null;
            return new LocalAppSettingEntry(value, updatedAt);
        }, cancellationToken);
    }

    public Task SetValuesAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        var valuesSnapshot = values
            .Select(pair =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
                return new KeyValuePair<string, string>(pair.Key, pair.Value);
            })
            .ToArray();
        if (valuesSnapshot.Length == 0)
        {
            return Task.CompletedTask;
        }

        return Task.Run(async () =>
        {
            await using var connection = await store.OpenConnectionAsync(cancellationToken);
            await EnsureAppSettingsTableAsync(connection, cancellationToken);
            using var transaction = connection.BeginTransaction();
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO AppSettings (Key, Value, UpdatedAt)
                VALUES ($Key, $Value, $UpdatedAt)
                ON CONFLICT(Key) DO UPDATE SET
                    Value = excluded.Value,
                    UpdatedAt = excluded.UpdatedAt;
                """;
            var keyParameter = command.Parameters.Add("$Key", Microsoft.Data.Sqlite.SqliteType.Text);
            var valueParameter = command.Parameters.Add("$Value", Microsoft.Data.Sqlite.SqliteType.Text);
            var updatedAtParameter = command.Parameters.Add("$UpdatedAt", Microsoft.Data.Sqlite.SqliteType.Text);
            var updatedAt = DateTimeOffset.UtcNow.ToString("O");

            foreach (var (key, value) in valuesSnapshot)
            {
                keyParameter.Value = key;
                valueParameter.Value = value;
                updatedAtParameter.Value = updatedAt;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }, cancellationToken);
    }

    public Task DeleteValueAsync(string key, CancellationToken cancellationToken = default)
    {
        var keySnapshot = key;
        return Task.Run(async () =>
        {
            await using var connection = await store.OpenConnectionAsync(cancellationToken);
            await EnsureAppSettingsTableAsync(connection, cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                DELETE FROM AppSettings
                WHERE Key = $Key;
                """;
            command.Parameters.AddWithValue("$Key", keySnapshot);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }, cancellationToken);
    }

    private static async Task EnsureAppSettingsTableAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """;

        // 设置读取可能早于完整本地库初始化，先保证自己的轻量配置表存在。
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
