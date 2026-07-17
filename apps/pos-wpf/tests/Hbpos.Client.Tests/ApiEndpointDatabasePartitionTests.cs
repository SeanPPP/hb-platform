using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class ApiEndpointDatabasePartitionTests
{
    [Fact]
    public async Task Concurrent_resolvers_publish_one_complete_legacy_binding_and_follow_the_winner()
    {
        for (var round = 0; round < 32; round++)
        {
            var root = CreateTempDirectory();
            using var start = new ManualResetEventSlim();
            try
            {
                var addresses = Enumerable.Range(0, 96)
                    .Select(index => index % 2 == 0
                        ? "https://first.example.com/pos-api/"
                        : "https://second.example.com/pos-api/")
                    .ToArray();
                var tasks = addresses
                    .Select(address => Task.Run(() =>
                    {
                        start.Wait();
                        return new ApiEndpointDatabasePartitionResolver(root, address);
                    }))
                    .ToArray();

                start.Set();
                var resolvers = await Task.WhenAll(tasks);
                var legacyPath = Path.Combine(root, "hbpos_client.db");
                var winnerIndexes = resolvers
                    .Select(resolver => resolver.GetDatabasePath(addresses[0]) == legacyPath ? 0 : 1)
                    .ToArray();

                Assert.Single(winnerIndexes.Distinct());
                Assert.All(resolvers, resolver =>
                {
                    var legacyCount = addresses.Take(2)
                        .Count(address => resolver.GetDatabasePath(address) == legacyPath);
                    Assert.Equal(1, legacyCount);
                });
                Assert.Empty(Directory.GetFiles(root, "server-data-map.json.tmp-*"));
            }
            finally
            {
                start.Set();
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Resolver_binds_legacy_database_to_initial_endpoint_and_restores_each_partition()
    {
        var root = CreateTempDirectory();
        try
        {
            var resolver = new ApiEndpointDatabasePartitionResolver(root, "https://first.example.com/pos-api");

            var first = resolver.GetDatabasePath("https://FIRST.example.com/pos-api/");
            var second = resolver.GetDatabasePath("https://second.example.com/pos-api/");
            var firstAgain = resolver.GetDatabasePath("https://first.example.com/pos-api/");

            Assert.Equal(Path.Combine(root, "hbpos_client.db"), first);
            Assert.Equal(first, firstAgain);
            Assert.NotEqual(first, second);
            Assert.StartsWith(Path.Combine(root, "hbpos_client-"), second, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".db", second, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(root, "server-data-map.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Local_store_switches_and_rolls_back_active_database_path()
    {
        var root = CreateTempDirectory();
        try
        {
            var state = new ApiRuntimeEndpointState("https://first.example.com/pos-api/");
            var resolver = new ApiEndpointDatabasePartitionResolver(root, state.CurrentAddress.AbsoluteUri);
            var store = new LocalSqliteStore(state, resolver);
            var original = store.ActiveDatabasePath;

            var prepared = await store.PrepareSwitchAsync("https://second.example.com/pos-api/", CancellationToken.None);
            store.Switch(prepared);

            Assert.NotEqual(original, store.ActiveDatabasePath);

            store.Rollback(prepared);

            Assert.Equal(original, store.ActiveDatabasePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Local_store_transition_waits_for_open_connection_and_blocks_new_connections()
    {
        var root = CreateTempDirectory();
        try
        {
            var state = new ApiRuntimeEndpointState("https://first.example.com/pos-api/");
            var resolver = new ApiEndpointDatabasePartitionResolver(root, state.CurrentAddress.AbsoluteUri);
            var store = new LocalSqliteStore(state, resolver);
            await using var oldConnection = await store.OpenConnectionAsync();
            var prepared = await store.PrepareSwitchAsync("https://second.example.com/pos-api/");

            var begin = store.BeginTransitionAsync(prepared, CancellationToken.None);
            await Task.Delay(50);

            Assert.False(begin.IsCompleted);
            await Assert.ThrowsAsync<LocalDatabaseTransitionException>(() => store.OpenConnectionAsync());
            await oldConnection.DisposeAsync();
            var transition = await begin.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.NotEqual(prepared.TargetDatabasePath, store.ActiveDatabasePath);

            store.Publish(transition);
            Assert.Equal(prepared.TargetDatabasePath, store.ActiveDatabasePath);
            await Assert.ThrowsAsync<LocalDatabaseTransitionException>(() => store.OpenConnectionAsync());
            store.Complete(transition);
            await using var newConnection = await store.OpenConnectionAsync();
            Assert.Equal(prepared.TargetDatabasePath, newConnection.DataSource);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "hbpos-endpoint-db-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
