using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.HeldOrders;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.Tests;

/// <summary>
/// Phase 2C 测试共享设施：前缀加密 protector、canonical payload serializer、
/// 真实 SQLite repository scope、样例 canonical/contract 数据、HTTP stub 与
/// API client stub。全部为测试专用，不进入生产代码。
/// </summary>
public static class SharedHeldOrderClientTestSupport
{
    public static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    public sealed class TestPayloadProtector : ISharedHeldOrderPayloadProtector
    {
        private static readonly byte[] Prefix = "enc:"u8.ToArray();

        public byte[] Protect(byte[] plaintext)
        {
            return [.. Prefix, .. plaintext];
        }

        public byte[] Unprotect(byte[] ciphertext)
        {
            Assert.StartsWith("enc:", Encoding.UTF8.GetString(ciphertext));
            return ciphertext[Prefix.Length..];
        }
    }

    public sealed class TestPayloadSerializer : ISharedHeldOrderPayloadSerializer
    {
        private static readonly ISharedHeldOrderCanonicalSerializer Canonical =
            new SharedHeldOrderCanonicalJsonSerializer();

        public byte[] Serialize(SharedHeldOrderCanonicalPayload payload)
        {
            return Encoding.UTF8.GetBytes(Canonical.Serialize(payload));
        }

        public SharedHeldOrderCanonicalPayload Deserialize(byte[] data)
        {
            return Canonical.Deserialize(Encoding.UTF8.GetString(data));
        }
    }

    public sealed class RepositoryScope : IAsyncDisposable
    {
        public RepositoryScope(string databasePath)
        {
            DatabasePath = databasePath;
            Store = new LocalSqliteStore(databasePath);
            Schema = new LocalSchemaService(Store);
            Repository = new SharedHeldOrderRepository(
                Store,
                new TestPayloadProtector(),
                new TestPayloadSerializer());
        }

        public string DatabasePath { get; }

        public LocalSqliteStore Store { get; }

        public LocalSchemaService Schema { get; }

        public SharedHeldOrderRepository Repository { get; }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { DatabasePath, $"{DatabasePath}-wal", $"{DatabasePath}-shm" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            return ValueTask.CompletedTask;
        }
    }

    public static async Task<RepositoryScope> CreateRepositoryScopeAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-shared-held-2c-{Guid.NewGuid():N}.db");
        var scope = new RepositoryScope(databasePath);
        await scope.Schema.InitializeAsync();
        return scope;
    }

    public static PosSessionState Session(string storeCode = "S001", string deviceCode = "POS-01")
    {
        return new PosSessionState(
            "WPF-POS",
            storeCode,
            "Store One",
            deviceCode,
            "cashier-1",
            "Cashier One",
            IsOnline: true,
            PendingSyncCount: 0);
    }

    public static SharedHeldOrderCanonicalPayload SampleCanonical(
        int revision = 1,
        decimal quantity = 1m,
        long unitPriceCents = 1100,
        string? discountMode = null,
        long? discountCents = null,
        int? basisPoints = null)
    {
        IReadOnlyList<SharedHeldOrderPromotionDefinition> promotions = [];
        IReadOnlyList<string>? promotionIds = null;
        if (string.Equals(discountMode, SharedHeldOrderCanonicalConstants.DiscountPromotion, StringComparison.Ordinal))
        {
            promotions =
            [
                new SharedHeldOrderPromotionDefinition(
                    "PROMO-1",
                    "Buy 2 Save 5",
                    "2026-07-01T00:00:00.000Z",
                    "2026-12-31T00:00:00.000Z",
                    IsExclusive: false,
                    Priority: 10,
                    ApplyQuantity: 2,
                    FixedPriceCents: 1500,
                    MaxApplicationsPerOrder: 1,
                    Products: [new SharedHeldOrderPromotionProduct("P-1", 1m)])
            ];
            promotionIds = ["PROMO-1"];
        }

        SharedHeldOrderDiscountState discountState = discountMode switch
        {
            SharedHeldOrderCanonicalConstants.DiscountManualAmount =>
                new SharedHeldOrderDiscountState(discountMode, Cents: discountCents),
            SharedHeldOrderCanonicalConstants.DiscountManualPercent =>
                new SharedHeldOrderDiscountState(discountMode, BasisPoints: basisPoints),
            SharedHeldOrderCanonicalConstants.DiscountPromotion =>
                new SharedHeldOrderDiscountState(discountMode, Cents: discountCents, PromotionIds: promotionIds),
            _ => new SharedHeldOrderDiscountState(SharedHeldOrderCanonicalConstants.DiscountNone)
        };
        return new SharedHeldOrderCanonicalPayload(
            1,
            new SharedHeldOrderPricingState(
                revision,
                SharedHeldOrderCanonicalConstants.SaleMode,
                "2026-07-28T00:00:00.000Z",
                promotions,
                [
                    new SharedHeldOrderPricingLine(
                        "line-1",
                        "P-1",
                        "ITEM-1",
                        "CODE-1",
                        "Product 1",
                        quantity,
                        unitPriceCents,
                        SharedHeldOrderCanonicalConstants.BasePriceSourceCatalog,
                        new SharedHeldOrderLineSyncProvenance("REF-1", (int)PriceSourceKind.StoreRetailPrice),
                        SharedHeldOrderCanonicalConstants.LineKindSale,
                        null,
                        null,
                        null,
                        discountState)
                ]));
    }

    public static SharedSaleCartV1 SampleSaleCartV1(
        int revision = 1,
        decimal quantity = 1m,
        long unitPriceCents = 1100,
        string? discountMode = null,
        long? discountCents = null,
        int? basisPoints = null)
    {
        IReadOnlyList<SharedPromotionV1> promotions = [];
        IReadOnlyList<string>? promotionIds = null;
        if (string.Equals(discountMode, SharedSaleCartV1Constants.DiscountModePromotion, StringComparison.Ordinal))
        {
            promotions =
            [
                new SharedPromotionV1(
                    "PROMO-1",
                    "Buy 2 Save 5",
                    "2026-07-01T00:00:00.000Z",
                    "2026-12-31T00:00:00.000Z",
                    IsExclusive: false,
                    Priority: 10,
                    ApplyQuantity: 2,
                    FixedPriceCents: 1500,
                    MaxApplicationsPerOrder: 1,
                    Products: [new SharedPromotionProductV1("P-1", 1m)])
            ];
            promotionIds = ["PROMO-1"];
        }

        SharedLineDiscountStateV1 discountState = discountMode switch
        {
            SharedSaleCartV1Constants.DiscountModeManualAmount =>
                new SharedLineDiscountStateV1(discountMode, Cents: discountCents),
            SharedSaleCartV1Constants.DiscountModeManualPercent =>
                new SharedLineDiscountStateV1(discountMode, BasisPoints: basisPoints),
            SharedSaleCartV1Constants.DiscountModePromotion =>
                new SharedLineDiscountStateV1(discountMode, Cents: discountCents, PromotionIds: promotionIds),
            _ => new SharedLineDiscountStateV1(SharedSaleCartV1Constants.DiscountModeNone)
        };
        return new SharedSaleCartV1(
            SharedSaleCartV1Constants.PayloadVersion,
            new SharedPricingStateV1(
                revision,
                SharedSaleCartV1Constants.PricingModeSale,
                "2026-07-28T00:00:00.000Z",
                promotions,
                [
                    new SharedSaleLineV1(
                        "line-1",
                        "P-1",
                        "ITEM-1",
                        "CODE-1",
                        "Product 1",
                        quantity,
                        unitPriceCents,
                        SharedSaleCartV1Constants.PriceSourceCatalog,
                        new SharedLineSyncProvenanceV1("REF-1", PriceSourceKind.StoreRetailPrice),
                        SharedSaleCartV1Constants.LineKindSale,
                        null,
                        null,
                        null,
                        discountState)
                ]));
    }

    public sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public IReadOnlyList<HttpRequestMessage> Requests { get; } = new List<HttpRequestMessage>();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ((List<HttpRequestMessage>)Requests).Add(request);
            return _handler(request, cancellationToken);
        }
    }

    public static HttpResponseMessage JsonResponse(
        object body,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(body, WebJsonOptions);
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    public static HttpResponseMessage ApiErrorResponse(
        string errorCode,
        string message,
        HttpStatusCode statusCode)
    {
        return JsonResponse(
            new
            {
                success = false,
                data = (object?)null,
                errorCode,
                message
            },
            statusCode);
    }

    public static SharedHeldOrderApiClient CreateApiClient(
        StubHttpMessageHandler handler,
        ISharedHeldOrderPublicationGate? publicationGate = null)
    {
        return new SharedHeldOrderApiClient(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("http://test.local")
            },
            publicationGate ?? new SharedHeldOrderPublicationGate());
    }

    public sealed class StubSharedHeldOrderApiClient : ISharedHeldOrderApiClient
    {
        public Func<CancellationToken, Task<SharedHeldOrderCapabilitiesResponse>>? Capabilities { get; set; }

        public Func<SharedHeldOrderPublishRequest, CancellationToken, Task<SharedHeldOrderPublishResponse>>? Publish { get; set; }

        public Func<Guid, CancellationToken, Task<SharedHeldOrderCancelResponse>>? Cancel { get; set; }

        public Func<CancellationToken, Task<IReadOnlyList<SharedHeldOrderListItemDto>>>? ListPending { get; set; }

        public Func<Guid, SharedHeldOrderClaimPrepareRequest, CancellationToken, Task<SharedHeldOrderClaimPrepareResponse>>? Prepare { get; set; }

        public Func<Guid, Guid, CancellationToken, Task<SharedHeldOrderClaimDto>>? Activate { get; set; }

        public Func<Guid, Guid, CancellationToken, Task<SharedHeldOrderClaimDto>>? Release { get; set; }

        public Func<Guid, Guid, SharedHeldOrderForceReleaseRequest, CancellationToken, Task<SharedHeldOrderClaimDto>>? ForceRelease { get; set; }

        public Func<CancellationToken, Task<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>>>? ClaimsMine { get; set; }

        public Task<SharedHeldOrderCapabilitiesResponse> GetCapabilitiesAsync(
            CancellationToken cancellationToken = default)
        {
            return Capabilities?.Invoke(cancellationToken)
                ?? throw new InvalidOperationException("Capabilities stub not configured.");
        }

        public Task<SharedHeldOrderPublishResponse> PublishAsync(
            SharedHeldOrderPublishRequest request,
            CancellationToken cancellationToken = default)
        {
            return Publish?.Invoke(request, cancellationToken)
                ?? throw new InvalidOperationException("Publish stub not configured.");
        }

        public Task<SharedHeldOrderCancelResponse> CancelAsync(
            Guid holdGuid,
            CancellationToken cancellationToken = default)
        {
            return Cancel?.Invoke(holdGuid, cancellationToken)
                ?? throw new InvalidOperationException("Cancel stub not configured.");
        }

        public Task<IReadOnlyList<SharedHeldOrderListItemDto>> ListPendingAsync(
            CancellationToken cancellationToken = default)
        {
            return ListPending?.Invoke(cancellationToken)
                ?? throw new InvalidOperationException("ListPending stub not configured.");
        }

        public Task<SharedHeldOrderClaimPrepareResponse> PrepareAsync(
            Guid holdGuid,
            SharedHeldOrderClaimPrepareRequest request,
            CancellationToken cancellationToken = default)
        {
            return Prepare?.Invoke(holdGuid, request, cancellationToken)
                ?? throw new InvalidOperationException("Prepare stub not configured.");
        }

        public Task<SharedHeldOrderClaimDto> ActivateAsync(
            Guid holdGuid,
            Guid claimGuid,
            CancellationToken cancellationToken = default)
        {
            return Activate?.Invoke(holdGuid, claimGuid, cancellationToken)
                ?? throw new InvalidOperationException("Activate stub not configured.");
        }

        public Task<SharedHeldOrderClaimDto> ReleaseAsync(
            Guid holdGuid,
            Guid claimGuid,
            CancellationToken cancellationToken = default)
        {
            return Release?.Invoke(holdGuid, claimGuid, cancellationToken)
                ?? throw new InvalidOperationException("Release stub not configured.");
        }

        public Task<SharedHeldOrderClaimDto> ForceReleaseAsync(
            Guid holdGuid,
            Guid claimGuid,
            SharedHeldOrderForceReleaseRequest request,
            CancellationToken cancellationToken = default)
        {
            return ForceRelease?.Invoke(holdGuid, claimGuid, request, cancellationToken)
                ?? throw new InvalidOperationException("ForceRelease stub not configured.");
        }

        public Task<IReadOnlyList<SharedHeldOrderRecoveryClaimDto>> ClaimsMineAsync(
            CancellationToken cancellationToken = default)
        {
            return ClaimsMine?.Invoke(cancellationToken)
                ?? throw new InvalidOperationException("ClaimsMine stub not configured.");
        }
    }
}
