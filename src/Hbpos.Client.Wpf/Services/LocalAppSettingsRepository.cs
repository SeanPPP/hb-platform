namespace Hbpos.Client.Wpf.Services;

public interface ILocalAppSettingsRepository
{
    Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default);

    Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default);
}

public sealed class LocalAppSettingsRepository(LocalSqliteStore store) : ILocalAppSettingsRepository
{
    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Value
            FROM AppSettings
            WHERE Key = $Key;
            """;
        command.Parameters.AddWithValue("$Key", key);

        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AppSettings (Key, Value, UpdatedAt)
            VALUES ($Key, $Value, $UpdatedAt)
            ON CONFLICT(Key) DO UPDATE SET
                Value = excluded.Value,
                UpdatedAt = excluded.UpdatedAt;
            """;
        command.Parameters.AddWithValue("$Key", key);
        command.Parameters.AddWithValue("$Value", value);
        command.Parameters.AddWithValue("$UpdatedAt", DateTimeOffset.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
