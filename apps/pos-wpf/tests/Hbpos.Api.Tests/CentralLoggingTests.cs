using System.Diagnostics;
using System.Net;
using System.Text.Json;
using BlazorApp.Shared.DTOs;
using Hbpos.Api.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Hbpos.Api.Tests;

public sealed class CentralLoggingTests
{
    [Fact]
    public void Options_use_safe_defaults_and_stay_disabled_without_endpoint_or_key()
    {
        var options = CentralLoggingOptions.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.False(options.Enabled);
        Assert.False(options.IsConfigured);
        Assert.Equal("hbpos_api", options.ProjectCode);
        Assert.Equal("Production", options.Environment);
        Assert.Equal("Backend", options.SourceType);
        Assert.Equal("Hbpos.Api", options.ServiceName);
        Assert.Equal(LogLevel.Warning, options.MinimumLevel);
        Assert.Equal(1_000, options.QueueCapacity);
        Assert.Equal(100, options.BatchSize);
        Assert.Equal(15, options.HttpTimeoutSeconds);
    }

    [Fact]
    public void Options_environment_provider_overrides_appsettings_values()
    {
        const string environmentKey = "HBPOSTEST_CentralLogging__ProjectCode";
        Environment.SetEnvironmentVariable(environmentKey, "from-env");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CentralLogging:Enabled"] = "true",
                    ["CentralLogging:IngestUrl"] = "https://logs.example/ingest",
                    ["CentralLogging:ApiKey"] = "secret",
                    ["CentralLogging:ProjectCode"] = "from-appsettings"
                })
                .AddEnvironmentVariables("HBPOSTEST_")
                .Build();

            var options = CentralLoggingOptions.FromConfiguration(configuration);

            Assert.True(options.IsConfigured);
            Assert.Equal("from-env", options.ProjectCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentKey, null);
        }
    }

    [Fact]
    public void Logger_ignores_levels_below_configured_minimum()
    {
        var (provider, queue) = CreateProvider();
        var logger = provider.CreateLogger("Hbpos.Api.Controllers.OrdersController");

        logger.LogInformation("not uploaded");

        Assert.Empty(queue.TakeBatch(100));
    }

    [Fact]
    public void Logger_maps_warning_request_trace_and_exception_without_structured_properties()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/orders";
        context.Request.QueryString = new QueryString("?token=secret");
        context.Request.Method = HttpMethods.Post;
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Request.Headers.Authorization = "Bearer secret";
        context.SetEndpoint(CreateRouteEndpoint("/api/v1/orders/{orderId}"));
        var accessor = new HttpContextAccessor { HttpContext = context };
        var (provider, queue) = CreateProvider(accessor);
        var logger = provider.CreateLogger("Hbpos.Api.Controllers.OrdersController");
        using var activity = new Activity("central-log-test").Start();
        var exception = CreateThrownException("payment-secret");
        var state = new Dictionary<string, object?>
        {
            ["OrderId"] = "sensitive-order",
            ["Password"] = "sensitive-password",
            ["{OriginalFormat}"] = "order failed"
        };

        logger.Log(LogLevel.Warning, new EventId(42, "OrderFailed"), state, exception, (_, _) => "order failed");

        var item = Assert.Single(queue.TakeBatch(100));
        Assert.Equal("Warning", item.Level);
        Assert.Equal("order failed", item.Message);
        Assert.Equal("Hbpos.Api.Controllers.OrdersController", item.Category);
        Assert.Equal("OrderFailed", item.EventId);
        Assert.Equal(activity.TraceId.ToString(), item.TraceId);
        Assert.Equal("/api/v1/orders/{orderId}", item.RequestPath);
        Assert.Equal("POST", item.RequestMethod);
        Assert.Equal(503, item.StatusCode);
        Assert.Equal(typeof(InvalidOperationException).FullName, item.ExceptionType);
        Assert.Null(item.ExceptionMessage);
        Assert.Equal(exception.StackTrace, item.StackTrace);
        Assert.Null(item.Properties);

        var json = JsonSerializer.Serialize(item, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("token=secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-order", json, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-password", json, StringComparison.Ordinal);
        Assert.DoesNotContain("payment-secret", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Logger_uses_only_route_pattern_and_omits_unmatched_actual_paths()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/vouchers/secret-voucher/sessions/secret-session/devices/secret-device";
        context.SetEndpoint(CreateRouteEndpoint("/api/v1/vouchers/{voucherCode}/sessions/{sessionId}/devices/{deviceId}"));
        var (provider, queue) = CreateProvider(new HttpContextAccessor { HttpContext = context });

        provider.CreateLogger("Hbpos.Api.Controllers.VouchersController").LogWarning("route test");

        var matched = Assert.Single(queue.TakeBatch(100));
        Assert.Equal("/api/v1/vouchers/{voucherCode}/sessions/{sessionId}/devices/{deviceId}", matched.RequestPath);
        Assert.DoesNotContain("secret-voucher", JsonSerializer.Serialize(matched), StringComparison.Ordinal);

        context.SetEndpoint(null);
        provider.CreateLogger("Hbpos.Api.Controllers.VouchersController").LogWarning("unmatched route");
        Assert.Null(Assert.Single(queue.TakeBatch(100)).RequestPath);
    }

    [Fact]
    public void Logger_redacts_sensitive_values_from_non_structured_messages()
    {
        const string unsafeMessage =
            "GET https://host/path?token=query-secret token=token-secret secret=secret-value " +
            "password=password-value apiKey=api-key-value authorization=Bearer auth-value " +
            "voucher=voucher-value card=4111111111111111 Server=db;Database=main;User Id=user;Password=db-password";
        var (provider, queue) = CreateProvider();
        var logger = provider.CreateLogger("Hbpos.Api.Services.LegacyService");

        logger.Log(LogLevel.Warning, default, unsafeMessage, null, (state, _) => state);

        var message = Assert.Single(queue.TakeBatch(100)).Message;
        Assert.DoesNotContain("query-secret", message, StringComparison.Ordinal);
        Assert.DoesNotContain("token-secret", message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", message, StringComparison.Ordinal);
        Assert.DoesNotContain("password-value", message, StringComparison.Ordinal);
        Assert.DoesNotContain("api-key-value", message, StringComparison.Ordinal);
        Assert.DoesNotContain("auth-value", message, StringComparison.Ordinal);
        Assert.DoesNotContain("voucher-value", message, StringComparison.Ordinal);
        Assert.DoesNotContain("4111111111111111", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Server=db", message, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Logger_redacts_payment_credentials_and_json_secrets_from_non_structured_messages()
    {
        const string unsafeMessage =
            "payment pan=5555555555554444 cvv=123 pin=9876 credential=credential-value " +
            "raw-card 4111111111111111 json={\"token\":\"json-token\",\"clientSecret\":\"json-secret\"}";
        var (provider, queue) = CreateProvider();

        provider.CreateLogger("Hbpos.Api.Services.LegacyPaymentService")
            .Log(LogLevel.Error, default, unsafeMessage, null, (state, _) => state);

        var item = Assert.Single(queue.TakeBatch(100));
        var wirePayload = JsonSerializer.Serialize(item, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("5555555555554444", wirePayload, StringComparison.Ordinal);
        Assert.DoesNotContain("4111111111111111", wirePayload, StringComparison.Ordinal);
        Assert.DoesNotContain("cvv=123", item.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("pin=9876", item.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("credential-value", wirePayload, StringComparison.Ordinal);
        Assert.DoesNotContain("json-token", wirePayload, StringComparison.Ordinal);
        Assert.DoesNotContain("json-secret", wirePayload, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", item.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logger_redacts_grouped_pan_before_uploader_serializes_wire_payload()
    {
        var options = CreateOptions();
        var queue = new CentralLogQueue(options.QueueCapacity);
        using var provider = new CentralLoggerProvider(options, queue, new HttpContextAccessor());
        provider.CreateLogger("Hbpos.Api.Services.LegacyPaymentService")
            .LogWarning("payment failed PAN: 4111 1111 1111 1111");
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var uploader = new CentralLogUploader(
            options,
            queue,
            new TrackingHttpClientFactory(handler),
            NullLogger<CentralLogUploadDiagnostic>.Instance,
            new RecordingDelay(),
            TimeProvider.System);

        await uploader.FlushOnceAsync(CancellationToken.None);

        var wirePayload = Assert.Single(handler.Requests).Content;
        Assert.DoesNotContain("4111 1111 1111 1111", wirePayload, StringComparison.Ordinal);
        Assert.DoesNotContain("1111 1111 1111", wirePayload, StringComparison.Ordinal);
        Assert.DoesNotContain("]]", wirePayload, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(wirePayload);
        Assert.Equal(
            "payment failed PAN: [REDACTED]",
            document.RootElement.GetProperty("logs")[0].GetProperty("message").GetString());
    }

    [Fact]
    public void Logger_preserves_sensitive_named_placeholders_in_structured_templates()
    {
        var (provider, queue) = CreateProvider();

        provider.CreateLogger("Hbpos.Api.Services.PaymentService")
            .LogWarning("PAN={Pan} CVV={Cvv} token={Token}", "4111111111111111", "123", "secret-token");

        var item = Assert.Single(queue.TakeBatch(100));
        Assert.Equal("PAN={Pan} CVV={Cvv} token={Token}", item.Message);
        var wirePayload = JsonSerializer.Serialize(item, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("4111111111111111", wirePayload, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", wirePayload, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("order 1234567890123456 remains visible")]
    [InlineData("reference 1234567890123 remains visible")]
    public void Logger_does_not_redact_non_luhn_business_identifiers(string safeMessage)
    {
        var (provider, queue) = CreateProvider();

        provider.CreateLogger("Hbpos.Api.Services.LegacyPaymentService")
            .Log(LogLevel.Warning, default, safeMessage, null, (state, _) => state);

        Assert.Equal(safeMessage, Assert.Single(queue.TakeBatch(100)).Message);
    }

    [Fact]
    public void Logger_uses_exception_type_as_safe_message_when_the_formatted_message_is_empty()
    {
        var (provider, queue) = CreateProvider();
        var exception = CreateThrownException("must-not-leak");

        provider.CreateLogger("Hbpos.Api.Services.EmptyMessageService")
            .Log(LogLevel.Error, default, string.Empty, exception, (_, _) => string.Empty);

        var item = Assert.Single(queue.TakeBatch(100));
        Assert.Equal("InvalidOperationException", item.Message);
        Assert.Null(item.ExceptionMessage);
        Assert.DoesNotContain("must-not-leak", JsonSerializer.Serialize(item), StringComparison.Ordinal);
    }

    [Fact]
    public void Logger_uploads_the_message_template_without_structured_parameter_values()
    {
        var (provider, queue) = CreateProvider();
        var logger = provider.CreateLogger("Hbpos.Api.Services.VoucherService");

        logger.LogWarning("voucher {VoucherCode}", "secret-voucher-code");

        var item = Assert.Single(queue.TakeBatch(100));
        Assert.Equal("voucher {VoucherCode}", item.Message);
        Assert.DoesNotContain("secret-voucher-code", JsonSerializer.Serialize(item), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Hbpos.Api.Logging.CentralLogUploader")]
    [InlineData("Microsoft.Hosting.Lifetime")]
    [InlineData("System.Net.Http.HttpClient.HbposCentralLogging.LogicalHandler")]
    public void Logger_excludes_internal_and_http_client_categories(string category)
    {
        var (provider, queue) = CreateProvider();

        provider.CreateLogger(category).LogWarning("must stay local");

        Assert.Empty(queue.TakeBatch(100));
    }

    [Fact]
    public void Logger_assigns_a_unique_client_event_id_to_every_item()
    {
        var (provider, queue) = CreateProvider();
        var logger = provider.CreateLogger("Hbpos.Api.Services.OrderService");

        logger.LogWarning("first");
        logger.LogError("second");

        var items = queue.TakeBatch(100);
        Assert.All(items, item => Assert.NotNull(item.ClientEventId));
        Assert.Equal(2, items.Select(item => item.ClientEventId).Distinct().Count());
    }

    [Fact]
    public void Queue_drops_oldest_item_and_counts_the_drop()
    {
        var queue = new CentralLogQueue(2);
        queue.Enqueue(CreateItem("first"));
        queue.Enqueue(CreateItem("second"));

        queue.Enqueue(CreateItem("third"));

        Assert.Equal(1, queue.DroppedOldestCount);
        Assert.Equal(["second", "third"], queue.TakeBatch(100).Select(item => item.Message));
    }

    [Fact]
    public void Queue_stop_accepting_rejects_new_business_items_but_allows_internal_requeue()
    {
        var queue = new CentralLogQueue(2);
        var original = CreateItem("original");
        Assert.True(queue.Enqueue(original));

        queue.StopAccepting();

        Assert.False(queue.Enqueue(CreateItem("late")));
        Assert.Equal(original.ClientEventId, Assert.Single(queue.TakeBatch(100)).ClientEventId);
        queue.Requeue([original]);
        Assert.Equal(original.ClientEventId, Assert.Single(queue.TakeBatch(100)).ClientEventId);
    }

    [Fact]
    public async Task Uploader_sends_expected_headers_and_at_most_one_hundred_items()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var (uploader, queue, _) = CreateUploader(handler);
        for (var index = 0; index < 120; index++)
        {
            queue.Enqueue(CreateItem($"message-{index}"));
        }

        await uploader.FlushOnceAsync(CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("hbpos_api", request.ProjectHeader);
        Assert.Equal("test-key", request.ApiKeyHeader);
        using var document = JsonDocument.Parse(request.Content);
        Assert.Equal(100, document.RootElement.GetProperty("logs").GetArrayLength());
        Assert.Equal(20, queue.Count);
    }

    [Fact]
    public async Task Uploader_serializes_only_the_approved_wire_fields()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var (uploader, queue, _) = CreateUploader(handler);
        var item = CreateItem("wire fields");
        item.ServiceName = "Hbpos.Api";
        item.Category = "Hbpos.Api.Services.OrderService";
        item.EventId = "OrderFailed";
        item.TraceId = "trace-id";
        item.RequestPath = "/api/v1/orders";
        item.RequestMethod = "POST";
        item.StatusCode = 503;
        item.ExceptionType = typeof(InvalidOperationException).FullName;
        item.ExceptionMessage = "failed";
        item.StackTrace = "safe-stack";
        item.InstanceId = "forbidden-instance";
        item.StoreCode = "forbidden-store";
        item.DeviceCode = "forbidden-device";
        item.AppVersion = "forbidden-version";
        item.UserId = "forbidden-user";
        item.UserName = "forbidden-name";
        item.ClientIp = "forbidden-ip";
        item.Properties = new Dictionary<string, object?> { ["VoucherCode"] = "forbidden-voucher" };
        queue.Enqueue(item);

        await uploader.FlushOnceAsync(CancellationToken.None);

        using var document = JsonDocument.Parse(Assert.Single(handler.Requests).Content);
        var actualFields = document.RootElement
            .GetProperty("logs")[0]
            .EnumerateObject()
            .Select(property => property.Name)
            .Order()
            .ToArray();
        string[] expectedFields =
        [
            "category", "clientEventId", "environment", "eventId", "exceptionType", "level",
            "message", "projectCode", "requestMethod", "requestPath",
            "serviceName", "sourceType", "stackTrace", "statusCode", "timestampUtc", "traceId"
        ];
        Assert.Equal(expectedFields.Order(), actualFields);
        Assert.DoesNotContain("forbidden-", Assert.Single(handler.Requests).Content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Uploader_drops_permanent_http_failures_without_throwing(HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler(statusCode);
        var (uploader, queue, delay) = CreateUploader(handler);
        queue.Enqueue(CreateItem("permanent"));

        var exception = await Record.ExceptionAsync(() => uploader.FlushOnceAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.Equal(0, queue.Count);
        Assert.Equal(
            statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? [TimeSpan.FromMinutes(5)]
                : [],
            delay.Delays);
    }

    [Fact]
    public async Task Uploader_retries_429_and_5xx_with_bounded_backoff_until_success()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK);
        var (uploader, queue, delay) = CreateUploader(handler);
        queue.Enqueue(CreateItem("retry"));

        var exception = await Record.ExceptionAsync(() => uploader.FlushOnceAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)], delay.Delays);
    }

    [Fact]
    public async Task Uploader_retries_timeout_without_throwing()
    {
        var handler = new RecordingHandler(
            new TaskCanceledException("timeout"),
            HttpStatusCode.OK);
        var (uploader, queue, delay) = CreateUploader(handler);
        queue.Enqueue(CreateItem("timeout"));

        var exception = await Record.ExceptionAsync(() => uploader.FlushOnceAsync(CancellationToken.None));

        Assert.Null(exception);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal([TimeSpan.FromSeconds(1)], delay.Delays);
    }

    [Fact]
    public async Task Uploader_requeues_after_four_backoffs_and_allows_the_next_batch_to_run()
    {
        var handler = new RecordingHandler(
            new HttpRequestException("network-1"),
            new HttpRequestException("network-2"),
            new HttpRequestException("network-3"),
            new HttpRequestException("network-4"),
            new HttpRequestException("network-5"),
            HttpStatusCode.OK);
        var (uploader, queue, delay) = CreateUploader(handler, configure: options => options.BatchSize = 1);
        var first = CreateItem("first failing batch");
        var second = CreateItem("second batch");
        queue.Enqueue(first);
        queue.Enqueue(second);

        await uploader.FlushOnceAsync(CancellationToken.None);

        Assert.Equal(5, handler.Requests.Count);
        Assert.Equal(
            [
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(30)
            ],
            delay.Delays);
        await uploader.FlushOnceAsync(CancellationToken.None);
        Assert.Equal("first failing batch", Assert.Single(queue.TakeBatch(100)).Message);
    }

    [Fact]
    public async Task Uploader_requeues_the_same_events_when_retry_delay_is_cancelled()
    {
        var options = CreateOptions();
        var queue = new CentralLogQueue(options.QueueCapacity);
        var delay = new BlockingDelay();
        var handler = new RecordingHandler(HttpStatusCode.TooManyRequests);
        var uploader = new CentralLogUploader(
            options,
            queue,
            new TrackingHttpClientFactory(handler),
            NullLogger<CentralLogUploadDiagnostic>.Instance,
            delay,
            TimeProvider.System);
        var item = CreateItem("cancel retry");
        queue.Enqueue(item);
        using var cancellation = new CancellationTokenSource();

        var flush = uploader.FlushOnceAsync(cancellation.Token);
        await delay.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flush);

        Assert.Equal(item.ClientEventId, Assert.Single(queue.TakeBatch(100)).ClientEventId);
    }

    [Fact]
    public async Task Uploader_accepts_the_real_api_contract_and_does_not_retry_rejected_items()
    {
        var eventId = Guid.NewGuid();
        var responseJson = JsonSerializer.Serialize(new
        {
            success = true,
            data = new
            {
                acceptedCount = 0,
                rejectedCount = 1,
                duplicateCount = 0,
                results = new[]
                {
                    new { clientEventId = eventId, status = "rejected", errorCode = "INVALID_LOG_ITEM" }
                }
            }
        });
        var handler = new RecordingHandler(new ResponseOutcome(HttpStatusCode.OK, responseJson));
        var logger = new RecordingLogger<CentralLogUploadDiagnostic>();
        var (uploader, queue, _) = CreateUploader(handler, logger: logger);
        var item = CreateItem("rejected item");
        item.ClientEventId = eventId;
        queue.Enqueue(item);

        await uploader.FlushOnceAsync(CancellationToken.None);

        Assert.Single(handler.Requests);
        Assert.Empty(queue.TakeBatch(100));
        Assert.Contains(logger.Messages, message =>
            message.Contains("Accepted=0", StringComparison.Ordinal) &&
            message.Contains("Rejected=1", StringComparison.Ordinal) &&
            message.Contains("Duplicate=0", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"success\":false,\"data\":{\"acceptedCount\":1,\"rejectedCount\":0,\"duplicateCount\":0,\"results\":[]}}")]
    [InlineData("{\"success\":true}")]
    public async Task Uploader_requeues_2xx_responses_that_do_not_match_the_success_contract(string responseJson)
    {
        var handler = new RecordingHandler(
            new ResponseOutcome(HttpStatusCode.OK, responseJson),
            new ResponseOutcome(HttpStatusCode.OK, responseJson),
            new ResponseOutcome(HttpStatusCode.OK, responseJson),
            new ResponseOutcome(HttpStatusCode.OK, responseJson),
            new ResponseOutcome(HttpStatusCode.OK, responseJson));
        var (uploader, queue, delay) = CreateUploader(handler);
        var item = CreateItem("invalid protocol response");
        queue.Enqueue(item);

        await uploader.FlushOnceAsync(CancellationToken.None);

        Assert.Equal(5, handler.Requests.Count);
        Assert.Equal(4, delay.Delays.Count);
        Assert.Equal(item.ClientEventId, Assert.Single(queue.TakeBatch(100)).ClientEventId);
    }

    [Fact]
    public async Task Uploader_creates_and_disposes_a_named_http_client_for_every_attempt()
    {
        var options = CreateOptions();
        var queue = new CentralLogQueue(options.QueueCapacity);
        var handler = new RecordingHandler(
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK);
        var factory = new TrackingHttpClientFactory(handler);
        var uploader = new CentralLogUploader(
            options,
            queue,
            factory,
            NullLogger<CentralLogUploadDiagnostic>.Instance,
            new RecordingDelay(),
            TimeProvider.System);
        queue.Enqueue(CreateItem("factory lifecycle"));

        await uploader.FlushOnceAsync(CancellationToken.None);

        Assert.Equal(3, factory.ClientNames.Count);
        Assert.All(factory.ClientNames, name => Assert.Equal(CentralLogUploader.HttpClientName, name));
        Assert.Equal(3, factory.DisposedCount);
    }

    [Fact]
    public async Task Uploader_disposes_the_attempt_client_before_entering_retry_delay()
    {
        var options = CreateOptions();
        var queue = new CentralLogQueue(options.QueueCapacity);
        var handler = new RecordingHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        var factory = new TrackingHttpClientFactory(handler);
        var delay = new DisposalObservingDelay(() => factory.DisposedCount);
        var uploader = new CentralLogUploader(
            options,
            queue,
            factory,
            NullLogger<CentralLogUploadDiagnostic>.Instance,
            delay,
            TimeProvider.System);
        queue.Enqueue(CreateItem("dispose before delay"));

        await uploader.FlushOnceAsync(CancellationToken.None);

        Assert.Equal(1, delay.DisposedClientsWhenCalled);
        Assert.Equal(2, factory.DisposedCount);
    }

    [Fact]
    public async Task StopAsync_requeues_the_cancelled_in_flight_batch_and_drains_more_than_one_batch()
    {
        var handler = new CancelFirstThenRecordSuccessHandler();
        var (uploader, queue, _) = CreateUploader(handler);
        await uploader.StartAsync(CancellationToken.None);
        for (var index = 0; index < 250; index++)
        {
            queue.Enqueue(CreateItem($"shutdown-{index}"));
        }

        await handler.FirstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await uploader.StopAsync(CancellationToken.None);

        Assert.Equal(250, handler.SuccessfulMessageCount);
        Assert.Equal(0, queue.Count);
        uploader.Dispose();
    }

    [Fact]
    public async Task StopAsync_uses_one_five_second_budget_for_base_stop_and_queue_drain()
    {
        var timeProvider = new ManualTimeProvider();
        var handler = new SharedShutdownBudgetHandler();
        var (uploader, queue, _) = CreateUploader(handler, timeProvider);
        await uploader.StartAsync(CancellationToken.None);
        queue.Enqueue(CreateItem("in-flight"));
        await handler.FirstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        queue.Enqueue(CreateItem("queued-for-drain"));

        var stopTask = uploader.StopAsync(CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(3));
        handler.ReleaseFirstRequest.TrySetResult();
        await handler.SecondRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        Assert.False(stopTask.IsCompleted);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await stopTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([TimeSpan.FromSeconds(5)], timeProvider.InitialDueTimes);
        Assert.Equal(2, queue.Count);
        uploader.Dispose();
    }

    [Fact]
    public async Task StopAsync_stops_concurrent_producers_before_draining_the_queue()
    {
        var handler = new CancelFirstThenRecordSuccessHandler();
        var (uploader, queue, _) = CreateUploader(handler);
        await uploader.StartAsync(CancellationToken.None);
        Assert.True(queue.Enqueue(CreateItem("in-flight")));
        await handler.FirstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var producerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = 0;
        var producer = Task.Run(async () =>
        {
            producerStarted.TrySetResult();
            for (var index = 0; index < 200; index++)
            {
                if (queue.Enqueue(CreateItem($"producer-{index}")))
                {
                    Interlocked.Increment(ref accepted);
                }

                await Task.Yield();
            }
        });
        await producerStarted.Task;

        await uploader.StopAsync(CancellationToken.None);
        await producer;

        Assert.False(queue.Enqueue(CreateItem("after-stop")));
        Assert.Equal(accepted + 1, handler.SuccessfulMessageCount);
        Assert.Empty(queue.TakeBatch(100));
        uploader.Dispose();
    }

    [Fact]
    public async Task Recording_delay_honors_cancellation()
    {
        var delay = new RecordingDelay();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            delay.DelayAsync(TimeSpan.FromSeconds(1), cancellation.Token));
    }

    private static (CentralLoggerProvider Provider, CentralLogQueue Queue) CreateProvider(
        IHttpContextAccessor? accessor = null)
    {
        var options = CreateOptions();
        var queue = new CentralLogQueue(options.QueueCapacity);
        return (new CentralLoggerProvider(options, queue, accessor ?? new HttpContextAccessor()), queue);
    }

    private static (CentralLogUploader Uploader, CentralLogQueue Queue, RecordingDelay Delay) CreateUploader(
        HttpMessageHandler handler,
        TimeProvider? timeProvider = null,
        Action<CentralLoggingOptions>? configure = null,
        ILogger<CentralLogUploadDiagnostic>? logger = null)
    {
        var options = CreateOptions();
        configure?.Invoke(options);
        var queue = new CentralLogQueue(options.QueueCapacity);
        var delay = new RecordingDelay();
        var uploader = new CentralLogUploader(
            options,
            queue,
            new TrackingHttpClientFactory(handler),
            logger ?? NullLogger<CentralLogUploadDiagnostic>.Instance,
            delay,
            timeProvider ?? TimeProvider.System);
        return (uploader, queue, delay);
    }

    private static CentralLoggingOptions CreateOptions()
    {
        return new CentralLoggingOptions
        {
            Enabled = true,
            IngestUrl = "https://logs.example/api/system/logs/ingest",
            ApiKey = "test-key"
        };
    }

    private static RouteEndpoint CreateRouteEndpoint(string pattern)
    {
        return new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(pattern),
            order: 0,
            EndpointMetadataCollection.Empty,
            displayName: pattern);
    }

    private static InvalidOperationException CreateThrownException(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    private static ApplicationLogIngestItemDto CreateItem(string message)
    {
        return new ApplicationLogIngestItemDto
        {
            ClientEventId = Guid.NewGuid(),
            Level = "Warning",
            Message = message,
            TimestampUtc = DateTime.UtcNow,
            ProjectCode = "hbpos_api",
            Environment = "Production",
            SourceType = "Backend"
        };
    }

    private static HttpResponseMessage CreateAcceptedResponse(string requestContent)
    {
        using var request = JsonDocument.Parse(requestContent);
        var eventIds = request.RootElement.GetProperty("logs")
            .EnumerateArray()
            .Select(item => item.GetProperty("clientEventId").GetGuid())
            .ToArray();
        var content = JsonSerializer.Serialize(new
        {
            success = true,
            data = new
            {
                acceptedCount = eventIds.Length,
                rejectedCount = 0,
                duplicateCount = 0,
                results = eventIds.Select(eventId => new
                {
                    clientEventId = eventId,
                    status = "accepted",
                    errorCode = (string?)null
                })
            }
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingDelay : ICentralLogDelay
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingDelay : ICentralLogDelay
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            _ = delay;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class DisposalObservingDelay(Func<int> getDisposedCount) : ICentralLogDelay
    {
        public int DisposedClientsWhenCalled { get; private set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            _ = delay;
            cancellationToken.ThrowIfCancellationRequested();
            DisposedClientsWhenCalled = getDisposedCount();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<object> outcomes;

        public RecordingHandler(params object[] outcomes)
        {
            this.outcomes = new Queue<object>(outcomes);
        }

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Headers.GetValues("X-Log-Project").Single(),
                request.Headers.GetValues("X-Log-Key").Single(),
                await request.Content!.ReadAsStringAsync(cancellationToken)));
            var outcome = outcomes.Dequeue();
            if (outcome is Exception exception)
            {
                throw exception;
            }

            if (outcome is ResponseOutcome explicitResponse)
            {
                return CreateResponse(explicitResponse.StatusCode, explicitResponse.Content, Requests[^1].Content);
            }

            var statusCode = (HttpStatusCode)outcome;
            return CreateResponse(statusCode, content: null, Requests[^1].Content);
        }

        private static HttpResponseMessage CreateResponse(
            HttpStatusCode statusCode,
            string? content,
            string requestContent)
        {
            if (content is null && (int)statusCode is >= 200 and < 300)
            {
                using var request = JsonDocument.Parse(requestContent);
                var eventIds = request.RootElement.GetProperty("logs")
                    .EnumerateArray()
                    .Select(item => item.GetProperty("clientEventId").GetGuid())
                    .ToArray();
                content = JsonSerializer.Serialize(new
                {
                    success = true,
                    data = new
                    {
                        acceptedCount = eventIds.Length,
                        rejectedCount = 0,
                        duplicateCount = 0,
                        results = eventIds.Select(eventId => new
                        {
                            clientEventId = eventId,
                            status = "accepted",
                            errorCode = (string?)null
                        })
                    }
                });
            }

            var response = new HttpResponseMessage(statusCode);
            if (content is not null)
            {
                response.Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json");
            }

            return response;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _ = logLevel;
            _ = eventId;
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class TrackingHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        private int disposedCount;

        public List<string> ClientNames { get; } = [];

        public int DisposedCount => Volatile.Read(ref disposedCount);

        public HttpClient CreateClient(string name)
        {
            ClientNames.Add(name);
            return new TrackingHttpClient(handler, () => Interlocked.Increment(ref disposedCount));
        }

        private sealed class TrackingHttpClient(HttpMessageHandler handler, Action onDispose)
            : HttpClient(handler, disposeHandler: false)
        {
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    onDispose();
                }

                base.Dispose(disposing);
            }
        }
    }

    private sealed class CancelFirstThenRecordSuccessHandler : HttpMessageHandler
    {
        private int requestCount;
        private int successfulMessageCount;

        public TaskCompletionSource FirstRequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SuccessfulMessageCount => Volatile.Read(ref successfulMessageCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var content = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(content);
            var messageCount = document.RootElement.GetProperty("logs").GetArrayLength();
            if (Interlocked.Increment(ref requestCount) == 1)
            {
                FirstRequestStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            Interlocked.Add(ref successfulMessageCount, messageCount);
            return CreateAcceptedResponse(content);
        }
    }

    private sealed class SharedShutdownBudgetHandler : HttpMessageHandler
    {
        private int requestCount;

        public TaskCompletionSource FirstRequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource SecondRequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = request;
            if (Interlocked.Increment(ref requestCount) == 1)
            {
                FirstRequestStarted.TrySetResult();
                // 模拟底层调用暂时不响应停止令牌，base.StopAsync 必须消耗同一关闭预算。
                await ReleaseFirstRequest.Task;
                var content = await request.Content!.ReadAsStringAsync(CancellationToken.None);
                return CreateAcceptedResponse(content);
            }

            SecondRequestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object syncRoot = new();
        private readonly List<ManualTimer> timers = [];

        public List<TimeSpan> InitialDueTimes { get; } = [];

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime, period);
            lock (syncRoot)
            {
                timers.Add(timer);
                InitialDueTimes.Add(dueTime);
            }

            return timer;
        }

        public void Advance(TimeSpan elapsed)
        {
            ManualTimer[] snapshot;
            lock (syncRoot)
            {
                snapshot = timers.ToArray();
            }

            foreach (var timer in snapshot)
            {
                timer.Advance(elapsed);
            }
        }

        private sealed class ManualTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period) : ITimer
        {
            private readonly object syncRoot = new();
            private TimeSpan remaining = dueTime;
            private TimeSpan repeat = period;
            private bool disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (syncRoot)
                {
                    if (disposed)
                    {
                        return false;
                    }

                    remaining = dueTime;
                    repeat = period;
                    return true;
                }
            }

            public void Advance(TimeSpan elapsed)
            {
                var shouldInvoke = false;
                lock (syncRoot)
                {
                    if (disposed || remaining == Timeout.InfiniteTimeSpan)
                    {
                        return;
                    }

                    remaining -= elapsed;
                    if (remaining <= TimeSpan.Zero)
                    {
                        shouldInvoke = true;
                        remaining = repeat;
                    }
                }

                if (shouldInvoke)
                {
                    callback(state);
                }
            }

            public void Dispose()
            {
                lock (syncRoot)
                {
                    disposed = true;
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed record RecordedRequest(string ProjectHeader, string ApiKeyHeader, string Content);

    private sealed record ResponseOutcome(HttpStatusCode StatusCode, string? Content);
}
