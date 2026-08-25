using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Installments;

namespace Hbpos.Client.Tests;

public sealed class InstallmentHistoryApiClientTests
{
    [Fact]
    public async Task QueryHistoryAsync_sends_all_history_filters()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ApiResult<InstallmentHistoryQueryResponse>.Ok(
                new InstallmentHistoryQueryResponse([])))
        });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://hbpos.test/")
        };
        var client = new InstallmentApiClient(httpClient);

        await client.QueryHistoryAsync(new InstallmentHistoryQueryRequest(
            "S001",
            DeviceCode: "POS-02",
            Keyword: "ITEM-100",
            Take: 100,
            UpdatedFrom: DateTimeOffset.Parse("2026-08-25T00:00:00Z"),
            UpdatedTo: DateTimeOffset.Parse("2026-08-25T23:59:59Z"),
            OrderByUpdatedAt: true));

        var uri = Assert.IsType<Uri>(handler.LastRequestUri);
        Assert.Contains("api/v1/installments/history", uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("storeCode=S001", uri.Query, StringComparison.Ordinal);
        Assert.Contains("deviceCode=POS-02", uri.Query, StringComparison.Ordinal);
        Assert.Contains("keyword=ITEM-100", uri.Query, StringComparison.Ordinal);
        Assert.Contains("updatedFrom=", uri.Query, StringComparison.Ordinal);
        Assert.Contains("updatedTo=", uri.Query, StringComparison.Ordinal);
        Assert.Contains("orderByUpdatedAt=true", uri.Query, StringComparison.Ordinal);
        Assert.Contains("take=100", uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task QueryHistoryAsync_returns_stable_timeout_feedback_at_two_second_deadline()
    {
        using var httpClient = new HttpClient(new BlockingHandler())
        {
            BaseAddress = new Uri("https://hbpos.test/")
        };
        var client = new InstallmentApiClient(httpClient);
        var stopwatch = Stopwatch.StartNew();

        var exception = await Assert.ThrowsAsync<CatalogApiException>(() =>
            client.QueryHistoryAsync(new InstallmentHistoryQueryRequest("S001")));

        stopwatch.Stop();
        Assert.Equal(TimeSpan.FromSeconds(2), InstallmentApiClient.HistoryQueryTimeout);
        Assert.Equal(HttpStatusCode.RequestTimeout, exception.StatusCode);
        Assert.Equal("INSTALLMENT_HISTORY_QUERY_TIMEOUT", exception.ErrorCode);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task QueryHistoryAsync_preserves_caller_cancellation()
    {
        using var httpClient = new HttpClient(new BlockingHandler())
        {
            BaseAddress = new Uri("https://hbpos.test/")
        };
        var client = new InstallmentApiClient(httpClient);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.QueryHistoryAsync(
                new InstallmentHistoryQueryRequest("S001"),
                cancellation.Token));
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
