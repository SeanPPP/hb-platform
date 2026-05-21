namespace Hbpos.Client.Wpf.Services;

public interface ISyncQueueRepository
{
    Task<int> CountPendingAsync(CancellationToken cancellationToken = default);
}

public sealed class SyncQueueRepository(LocalSqliteStore store) : ISyncQueueRepository
{
    public async Task<int> CountPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await store.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SyncQueue WHERE Status = 'Pending';";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }
}
