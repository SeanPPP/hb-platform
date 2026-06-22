using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Orders;
using Microsoft.Extensions.Configuration;

namespace Hbpos.Client.Tests;

[Collection(ConsoleLogGlobalStateTestCollection.Name)]
public sealed class CentralLoggingTests
{
    [Fact]
    public void ApplicationLogOptions_from_configuration_reads_appsettings_and_environment_override()
    {
        using var variables = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["HBPOS_LOG_CENTER_API_KEY"] = "env-key",
            ["HBPOS_LOG_CENTER_QUEUE_CAPACITY"] = "25"
        });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CentralLogging:Enabled"] = "true",
                ["CentralLogging:ApiKey"] = "appsettings-key",
                ["CentralLogging:ProjectCode"] = "hbpos_win",
                ["CentralLogging:Environment"] = "staging",
                ["CentralLogging:SourceType"] = "POS",
                ["CentralLogging:ServiceName"] = "Hbpos.Client.Wpf",
                ["CentralLogging:BatchSize"] = "12",
                ["CentralLogging:QueueCapacity"] = "80",
                ["CentralLogging:LocalBufferPath"] = "C:\\temp\\hbpos-center-buffer.jsonl"
            })
            .AddEnvironmentVariables()
            .Build();

        var options = ApplicationLogOptions.FromConfiguration(
            configuration,
            new Uri("https://pos-api.example.com/"));

        Assert.True(options.Enabled);
        Assert.Equal("env-key", options.ApiKey);
        Assert.Equal("hbpos_win", options.ProjectCode);
        Assert.Equal("staging", options.Environment);
        Assert.Equal("POS", options.SourceType);
        Assert.Equal("Hbpos.Client.Wpf", options.ServiceName);
        Assert.Equal(12, options.BatchSize);
        Assert.Equal(25, options.QueueCapacity);
        Assert.Equal("C:\\temp\\hbpos-center-buffer.jsonl", options.LocalBufferPath);
        Assert.Equal(new Uri("https://pos-api.example.com/api/system/logs/ingest"), options.IngestUri);
    }

    [Fact]
    public void ConsoleLog_write_enqueues_center_log_entry()
    {
        var sink = new FakeApplicationLogSink();
        ConsoleLog.ConfigureCenterDefaults(ApplicationLogDefaults.Default);
        ConsoleLog.ConfigureCenterSink(sink);

        try
        {
            ConsoleLog.Write("OrderSync", "upload completed orderGuid=abc");

            var entry = Assert.Single(sink.Entries.Where(item =>
                item.Category == "OrderSync" &&
                item.Message == "upload completed orderGuid=abc"));
            Assert.Equal("Information", entry.Level);
            Assert.Equal("upload completed orderGuid=abc", entry.Message);
            Assert.Equal("hbpos_win", entry.ProjectCode);
            Assert.Equal("POS", entry.SourceType);
            Assert.Equal("OrderSync", entry.Category);
            Assert.Equal("OrderSync", entry.ServiceName);
            Assert.NotNull(entry.Properties);
            Assert.Equal("OrderSync", entry.Properties!["category"]);
        }
        finally
        {
            ConsoleLog.ConfigureCenterSink(null);
            ConsoleLog.ConfigureCenterDefaults(ApplicationLogDefaults.Default);
        }
    }

    [Fact]
    public async Task OrderSyncApiClient_http_failure_enqueues_structured_center_log()
    {
        var sink = new FakeApplicationLogSink();
        ConsoleLog.ConfigureCenterDefaults(ApplicationLogDefaults.Default);
        ConsoleLog.ConfigureCenterSink(sink);

        try
        {
            var client = new OrderSyncApiClient(new HttpClient(new StubHttpMessageHandler((_, _) =>
                new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("""
                    {"success":false,"message":"sync failed","errorCode":"ORDER_SYNC_FAILED"}
                    """, Encoding.UTF8, "application/json")
                }))
            {
                BaseAddress = new Uri("https://pos-api.example.com/")
            });

            var request = new OrderSyncRequest(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                "S001",
                "POS-01",
                "C001",
                "Alice",
                DateTimeOffset.Parse("2026-06-05T00:00:00Z"),
                10m,
                0m,
                10m,
                [],
                []);

            await Assert.ThrowsAsync<CatalogApiException>(() => client.SyncAsync(request));

            var entry = Assert.Single(sink.Entries.Where(item =>
                item.Level == "Error" &&
                item.ServiceName == "OrderSync" &&
                item.Message == "Order sync request failed."));
            Assert.Equal("Order sync request failed.", entry.Message);
            Assert.Equal("OrderSync", entry.ServiceName);
            Assert.Equal("POST", entry.RequestMethod);
            Assert.Equal("/api/v1/orders/sync", entry.RequestPath);
            Assert.Equal(500, entry.StatusCode);
            Assert.NotNull(entry.Properties);
            Assert.Equal("S001", entry.Properties!["storeCode"]);
            Assert.Equal("POS-01", entry.Properties["deviceCode"]);
            Assert.Equal("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", entry.TraceId);
        }
        finally
        {
            ConsoleLog.ConfigureCenterSink(null);
            ConsoleLog.ConfigureCenterDefaults(ApplicationLogDefaults.Default);
        }
    }

    private sealed class FakeApplicationLogSink : IApplicationLogSink
    {
        private readonly ConcurrentQueue<ApplicationLogEntry> _entries = new();

        public IReadOnlyList<ApplicationLogEntry> Entries => _entries.ToArray();

        public void Enqueue(ApplicationLogEntry entry)
        {
            _entries.Enqueue(entry);
        }
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request, cancellationToken));
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originalValues = new(StringComparer.OrdinalIgnoreCase);

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var entry in values)
            {
                _originalValues[entry.Key] = Environment.GetEnvironmentVariable(entry.Key);
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }

        public void Dispose()
        {
            foreach (var entry in _originalValues)
            {
                Environment.SetEnvironmentVariable(entry.Key, entry.Value);
            }
        }
    }
}
