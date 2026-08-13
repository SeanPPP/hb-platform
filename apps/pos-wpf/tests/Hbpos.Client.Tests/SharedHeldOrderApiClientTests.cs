using System.Net;
using System.Text;
using Hbpos.Client.Wpf.Services;
using Hbpos.Contracts.HeldOrders;
using static Hbpos.Client.Tests.SharedHeldOrderClientTestSupport;

namespace Hbpos.Client.Tests;

/// <summary>
/// Http API adapter：严格解析 ApiResult envelope（状态/Guid/revision/时间/summary/payload），
/// 错误稳定分类 disabled/retryable/conflict/forbidden/invalid；任何路径不把 payload 写入日志/异常消息。
/// </summary>
public sealed class SharedHeldOrderApiClientTests
{
    [Fact]
    public async Task GetCapabilitiesAsync_parses_envelope_and_endpoint()
    {
        var client = CreateApiClient(new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/v1/held-orders/capabilities", request.RequestUri?.PathAndQuery);
            return Task.FromResult(JsonResponse(new
            {
                success = true,
                data = new
                {
                    enabled = false,
                    payloadVersion = 1,
                    preparedTtlSeconds = 120,
                    forceReleaseSupported = true
                }
            }));
        }));

        var capabilities = await client.GetCapabilitiesAsync();

        Assert.False(capabilities.Enabled);
        Assert.Equal(1, capabilities.PayloadVersion);
        Assert.Equal(120, capabilities.PreparedTtlSeconds);
        Assert.True(capabilities.ForceReleaseSupported);
        Assert.Equal([1], capabilities.SupportedPayloadVersions);
        Assert.Equal(1, capabilities.PreferredPayloadVersion);
    }

    [Fact]
    public async Task PublishAsync_uses_post_and_parses_revision_and_remote_time()
    {
        var client = CreateApiClient(new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v1/held-orders", request.RequestUri?.PathAndQuery);
            return Task.FromResult(JsonResponse(new
            {
                success = true,
                data = new
                {
                    holdGuid = "11111111-1111-1111-1111-111111111111",
                    status = 1,
                    revision = 7L,
                    createdAtUtc = "2026-07-28T01:02:03.456Z",
                    alreadyExists = false
                }
            }));
        }));

        var response = await client.PublishAsync(new SharedHeldOrderPublishRequest(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "S001",
            "POS-01",
            SampleSaleCartV1(),
            "idem-1"));

        Assert.Equal(7L, response.Revision);
        Assert.Equal(SharedHeldOrderStatus.Pending, response.Status);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-28T01:02:03.456Z"),
            response.CreatedAtUtc);
    }

    [Fact]
    public async Task CancelAsync_uses_post_and_parses_cancel_response()
    {
        var holdGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var client = CreateApiClient(new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                "/api/v1/held-orders/11111111-1111-1111-1111-111111111111/cancel",
                request.RequestUri?.PathAndQuery);
            return Task.FromResult(JsonResponse(new
            {
                success = true,
                data = new
                {
                    holdGuid,
                    status = (int)SharedHeldOrderStatus.Cancelled,
                    revision = 8L,
                    updatedAtUtc = "2026-08-11T01:02:03.456Z",
                    alreadyCancelled = false
                }
            }));
        }));

        var response = await client.CancelAsync(holdGuid);

        Assert.Equal(holdGuid, response.HoldGuid);
        Assert.Equal(SharedHeldOrderStatus.Cancelled, response.Status);
        Assert.Equal(8L, response.Revision);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-11T01:02:03.456Z"),
            response.UpdatedAtUtc);
        Assert.False(response.AlreadyCancelled);
    }

    [Fact]
    public async Task CancelAsync_waits_until_publication_gate_is_released()
    {
        var holdGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var gate = new SharedHeldOrderPublicationGate();
        var publicationEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePublication = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var publicationTask = gate.RunExclusiveAsync(async () =>
        {
            publicationEntered.SetResult();
            await releasePublication.Task;
            return true;
        });
        await publicationEntered.Task;

        var cancelSent = false;
        var client = CreateApiClient(new StubHttpMessageHandler((_, _) =>
        {
            cancelSent = true;
            return Task.FromResult(JsonResponse(new
            {
                success = true,
                data = new
                {
                    holdGuid,
                    status = (int)SharedHeldOrderStatus.Cancelled,
                    revision = 8L,
                    updatedAtUtc = "2026-08-11T01:02:03.456Z",
                    alreadyCancelled = false
                }
            }));
        }), gate);

        var cancelTask = client.CancelAsync(holdGuid);
        await Task.Yield();

        Assert.False(cancelSent);
        Assert.False(cancelTask.IsCompleted);

        releasePublication.SetResult();
        await publicationTask;
        await cancelTask;
        Assert.True(cancelSent);
    }

    [Fact]
    public async Task PrepareAsync_returns_decrypted_payload_and_claim_fields()
    {
        var client = CreateApiClient(new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal(
                "/api/v1/held-orders/11111111-1111-1111-1111-111111111111/claims/prepare?supportedPayloadVersions=1&supportedPayloadVersions=2",
                request.RequestUri?.PathAndQuery);
            return Task.FromResult(JsonResponse(new
            {
                success = true,
                data = new
                {
                    holdGuid = "11111111-1111-1111-1111-111111111111",
                    claimGuid = "22222222-2222-2222-2222-222222222222",
                    status = 1,
                    payload = SampleSaleCartV1(),
                    claimantDeviceCode = "POS-01",
                    claimantCashierId = "cashier-1",
                    claimantCashierName = "Cashier One",
                    createdAtUtc = "2026-07-28T01:02:03.456Z",
                    expiresAtUtc = "2026-07-28T01:04:03.456Z",
                    revision = 3L,
                    alreadyExists = false
                }
            }));
        }));

        var response = await client.PrepareAsync(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            new SharedHeldOrderClaimPrepareRequest(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "prepare-1"));

        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), response.ClaimGuid);
        Assert.Equal(SharedHeldOrderClaimStatus.Prepared, response.Status);
        Assert.Equal(3L, response.Revision);
        Assert.NotNull(response.Payload);
        var payload = Assert.IsType<SharedSaleCartV1>(response.Payload);
        Assert.Equal(1100L, payload.PricingState.Lines[0].UnitPriceCents);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-28T01:04:03.456Z"),
            response.ExpiresAtUtc);
    }

    [Fact]
    public async Task ForceReleaseAsync_posts_required_reason_to_the_force_release_endpoint()
    {
        var holdGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var claimGuid = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var client = CreateApiClient(new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                "/api/v1/held-orders/11111111-1111-1111-1111-111111111111/claims/22222222-2222-2222-2222-222222222222/force-release",
                request.RequestUri?.PathAndQuery);

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Contains("\"reason\":\"主管确认客人仍在店内\"", body, StringComparison.Ordinal);
            return JsonResponse(new
            {
                success = true,
                data = new
                {
                    holdGuid,
                    claimGuid,
                    status = (int)SharedHeldOrderClaimStatus.Released,
                    storeCode = "S001",
                    claimantDeviceCode = "POS-02",
                    claimantCashierId = "cashier-2",
                    claimantCashierName = "Cashier Two",
                    createdAtUtc = "2026-07-28T01:02:03.456Z",
                    updatedAtUtc = "2026-07-28T01:03:03.456Z",
                    expiresAtUtc = (string?)null,
                    activatedAtUtc = "2026-07-28T01:02:30.000Z",
                    releasedAtUtc = "2026-07-28T01:03:03.456Z",
                    forceReleased = true,
                    forceReleaseReason = "主管确认客人仍在店内",
                    forceReleaseCashierId = "SUP-1",
                    forceReleaseCashierName = "Supervisor",
                    forceReleasedAtUtc = "2026-07-28T01:03:03.456Z",
                    revision = 5L,
                    alreadyExists = false
                }
            });
        }));

        var response = await client.ForceReleaseAsync(
            holdGuid,
            claimGuid,
            new SharedHeldOrderForceReleaseRequest("主管确认客人仍在店内"));

        Assert.True(response.ForceReleased);
        Assert.Equal("主管确认客人仍在店内", response.ForceReleaseReason);
        Assert.Equal(SharedHeldOrderClaimStatus.Released, response.Status);
    }

    [Fact]
    public async Task ListPendingAsync_parses_summaries_without_payload_field()
    {
        var client = CreateApiClient(new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(
                "/api/v1/held-orders?supportedPayloadVersions=1&supportedPayloadVersions=2",
                request.RequestUri?.PathAndQuery);
            return Task.FromResult(JsonResponse(new
            {
                success = true,
                data = new[]
                {
                    new
                    {
                        holdGuid = "11111111-1111-1111-1111-111111111111",
                        storeCode = "S001",
                        deviceCode = "POS-01",
                        heldByCashierId = "cashier-1",
                        heldByCashierName = "Cashier One",
                        heldAtUtc = "2026-07-28T01:02:03.456Z",
                        updatedAtUtc = "2026-07-28T01:02:04.000Z",
                        lineCount = 2,
                        totalCents = 2200L,
                        discountCents = 100L,
                        actualCents = 2100L,
                        revision = 9L
                    }
                }
            }));
        }));

        var items = await client.ListPendingAsync();

        var item = Assert.Single(items);
        Assert.Equal(2, item.LineCount);
        Assert.Equal(2200L, item.TotalCents);
        Assert.Equal(100L, item.DiscountCents);
        Assert.Equal(2100L, item.ActualCents);
        Assert.Equal(9L, item.Revision);
    }

    [Fact]
    public async Task ClaimsMineAsync_parses_recovery_claims_with_payload()
    {
        var client = CreateApiClient(new StubHttpMessageHandler((request, _) =>
        {
            Assert.Equal(
                "/api/v1/held-orders/claims/mine?supportedPayloadVersions=1&supportedPayloadVersions=2",
                request.RequestUri?.PathAndQuery);
            return Task.FromResult(JsonResponse(new
            {
                success = true,
                data = new[]
                {
                    new
                    {
                        holdGuid = "11111111-1111-1111-1111-111111111111",
                        claimGuid = "22222222-2222-2222-2222-222222222222",
                        status = 2,
                        storeCode = "S001",
                        claimantDeviceCode = "POS-01",
                        claimantCashierId = "cashier-1",
                        claimantCashierName = "Cashier One",
                        payload = SampleSaleCartV1(),
                        createdAtUtc = "2026-07-28T01:02:03.456Z",
                        updatedAtUtc = "2026-07-28T01:03:03.456Z",
                        expiresAtUtc = (string?)null,
                        activatedAtUtc = "2026-07-28T01:03:03.456Z",
                        revision = 4L
                    }
                }
            }));
        }));

        var claims = await client.ClaimsMineAsync();

        var claim = Assert.Single(claims);
        Assert.Equal(SharedHeldOrderClaimStatus.Active, claim.Status);
        Assert.Equal(4L, claim.Revision);
        Assert.NotNull(claim.Payload);
    }

    [Fact]
    public async Task Disabled_error_is_classified_disabled()
    {
        var client = CreateApiClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(ApiErrorResponse(
                "SHARED_HELD_ORDER_DISABLED",
                "feature is disabled",
                HttpStatusCode.Conflict))));

        var exception = await Assert.ThrowsAsync<SharedHeldOrderApiException>(
            () => client.GetCapabilitiesAsync());

        Assert.Equal(SharedHeldOrderApiErrorKind.Disabled, exception.Kind);
        Assert.Equal("SHARED_HELD_ORDER_DISABLED", exception.ErrorCode);
    }

    [Fact]
    public async Task Busy_and_http_500_are_retryable()
    {
        var busy = CreateApiClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(ApiErrorResponse(
                "SHARED_HELD_ORDER_BUSY",
                "concurrent",
                HttpStatusCode.Conflict))));
        var serverError = CreateApiClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))));

        Assert.Equal(
            SharedHeldOrderApiErrorKind.Retryable,
            (await Assert.ThrowsAsync<SharedHeldOrderApiException>(
                () => busy.PublishAsync(AnyPublishRequest()))).Kind);
        Assert.Equal(
            SharedHeldOrderApiErrorKind.Retryable,
            (await Assert.ThrowsAsync<SharedHeldOrderApiException>(
                () => serverError.GetCapabilitiesAsync())).Kind);
    }

    [Fact]
    public async Task Network_failure_is_retryable_and_does_not_leak_request_body()
    {
        var client = CreateApiClient(new StubHttpMessageHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("connection refused"))));

        var exception = await Assert.ThrowsAsync<SharedHeldOrderApiException>(
            () => client.PublishAsync(AnyPublishRequest()));

        Assert.Equal(SharedHeldOrderApiErrorKind.Retryable, exception.Kind);
        Assert.DoesNotContain("Product 1", exception.Message);
        Assert.DoesNotContain("CODE-1", exception.Message);
    }

    [Fact]
    public async Task Mismatch_and_expired_are_conflict()
    {
        var mismatch = CreateApiClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(ApiErrorResponse(
                "SHARED_HELD_ORDER_MISMATCH",
                "different payload",
                HttpStatusCode.Conflict))));
        var expired = CreateApiClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(ApiErrorResponse(
                "SHARED_HELD_ORDER_CLAIM_EXPIRED",
                "claim expired",
                HttpStatusCode.Conflict))));

        Assert.Equal(
            SharedHeldOrderApiErrorKind.Conflict,
            (await Assert.ThrowsAsync<SharedHeldOrderApiException>(
                () => mismatch.PrepareAsync(Guid.NewGuid(), AnyPrepareRequest()))).Kind);
        Assert.Equal(
            SharedHeldOrderApiErrorKind.Conflict,
            (await Assert.ThrowsAsync<SharedHeldOrderApiException>(
                () => expired.ActivateAsync(Guid.NewGuid(), Guid.NewGuid()))).Kind);
    }

    [Fact]
    public async Task Permission_and_auth_errors_are_forbidden()
    {
        var permission = CreateApiClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(ApiErrorResponse(
                "SHARED_HELD_ORDER_PERMISSION_DENIED",
                "no permission",
                HttpStatusCode.Forbidden))));
        var auth = CreateApiClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(ApiErrorResponse(
                "CASHIER_AUTH_REQUIRED",
                "auth required",
                HttpStatusCode.Unauthorized))));
        var crossStore = CreateApiClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(ApiErrorResponse(
                "SHARED_HELD_ORDER_CROSS_STORE",
                "cross store",
                HttpStatusCode.Forbidden))));

        Assert.Equal(
            SharedHeldOrderApiErrorKind.Forbidden,
            (await Assert.ThrowsAsync<SharedHeldOrderApiException>(
                () => permission.ListPendingAsync())).Kind);
        Assert.Equal(
            SharedHeldOrderApiErrorKind.Forbidden,
            (await Assert.ThrowsAsync<SharedHeldOrderApiException>(
                () => auth.ClaimsMineAsync())).Kind);
        Assert.Equal(
            SharedHeldOrderApiErrorKind.Forbidden,
            (await Assert.ThrowsAsync<SharedHeldOrderApiException>(
                () => crossStore.PublishAsync(AnyPublishRequest()))).Kind);
    }

    [Fact]
    public async Task Invalid_and_not_found_are_invalid()
    {
        var invalid = CreateApiClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(ApiErrorResponse(
                "SHARED_HELD_ORDER_INVALID",
                "bad request",
                HttpStatusCode.BadRequest))));
        var notFound = CreateApiClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(ApiErrorResponse(
                "SHARED_HELD_ORDER_NOT_FOUND",
                "missing",
                HttpStatusCode.NotFound))));

        Assert.Equal(
            SharedHeldOrderApiErrorKind.Invalid,
            (await Assert.ThrowsAsync<SharedHeldOrderApiException>(
                () => invalid.PrepareAsync(Guid.NewGuid(), AnyPrepareRequest()))).Kind);
        Assert.Equal(
            SharedHeldOrderApiErrorKind.Invalid,
            (await Assert.ThrowsAsync<SharedHeldOrderApiException>(
                () => notFound.ListPendingAsync())).Kind);
    }

    [Fact]
    public async Task Success_without_data_is_invalid()
    {
        var client = CreateApiClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(new
            {
                success = true,
                data = (object?)null
            }))));

        var exception = await Assert.ThrowsAsync<SharedHeldOrderApiException>(
            () => client.GetCapabilitiesAsync());

        Assert.Equal(SharedHeldOrderApiErrorKind.Invalid, exception.Kind);
    }

    [Fact]
    public async Task Malformed_json_is_invalid_and_payload_is_never_captured_in_message()
    {
        var client = CreateApiClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{not-json", Encoding.UTF8, "application/json")
            })));

        var exception = await Assert.ThrowsAsync<SharedHeldOrderApiException>(
            () => client.PublishAsync(AnyPublishRequest()));

        Assert.Equal(SharedHeldOrderApiErrorKind.Invalid, exception.Kind);
        Assert.DoesNotContain("Product 1", exception.Message);
        Assert.DoesNotContain("CODE-1", exception.Message);
    }

    private static SharedHeldOrderPublishRequest AnyPublishRequest()
    {
        return new SharedHeldOrderPublishRequest(
            Guid.NewGuid(),
            "S001",
            "POS-01",
            SampleSaleCartV1(quantity: 2m, unitPriceCents: 9999, discountMode: "manual-amount", discountCents: 250),
            "idem-any");
    }

    private static SharedHeldOrderClaimPrepareRequest AnyPrepareRequest()
    {
        return new SharedHeldOrderClaimPrepareRequest(Guid.NewGuid(), "prepare-any");
    }
}
