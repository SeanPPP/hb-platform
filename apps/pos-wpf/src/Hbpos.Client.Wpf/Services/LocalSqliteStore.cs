using System.IO;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Wpf.Services;

public sealed class LocalSqliteStore
{
    private readonly string _connectionString;

    public LocalSqliteStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hbpos.Client",
            "hbpos_client.db"))
    {
    }

    public LocalSqliteStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString();
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await ConfigureConnectionAsync(connection, cancellationToken);
        return connection;
    }

    private static async Task ConfigureConnectionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await ExecutePragmaAsync(connection, "PRAGMA busy_timeout = 5000;", cancellationToken);
        await ExecutePragmaAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);
    }

    private static async Task ExecutePragmaAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
