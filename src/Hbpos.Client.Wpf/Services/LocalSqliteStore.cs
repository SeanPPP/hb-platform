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
        return connection;
    }
}
