using Hbpos.Api.Auth;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.Square;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/v1/square")]
[Authorize]
public sealed class SquareController(
    ISquareTokenService squareTokenService,
    ISquareTerminalBackendService backendService,
    ILogger<SquareController> logger,
    ISquareCheckoutSessionRepository? checkoutSessionRepository = null) : ControllerBase
{
    private const string InvalidEnvironmentCode = "SQUARE_ENVIRONMENT_INVALID";
    private const string InvalidEnvironmentMessage = "environment must be Production or Sandbox";
    private const string IdempotencyKeyRequiredCode = "SQUARE_IDEMPOTENCY_KEY_REQUIRED";
    private const string IdempotencyKeyRequiredMessage = "idempotencyKey is required.";
    private const string TokenNotConfiguredCode = "SQUARE_TOKEN_NOT_CONFIGURED";
    private const string TokenNotConfiguredMessage = "Square token is not configured for this environment.";
    private const string TokenReadFailedCode = "SQUARE_TOKEN_READ_FAILED";
    private const string TokenReadFailedMessage = "Failed to load Square token configuration.";
    private const string BackendNotImplementedCode = "SQUARE_BACKEND_NOT_IMPLEMENTED";
    private const string BackendNotImplementedMessage = "Square backend service is not implemented.";
    private const string BackendRequestFailedMessage = "Square backend request failed.";
    private const string UpstreamRequestFailedCode = "SQUARE_UPSTREAM_REQUEST_FAILED";
    private const string UpstreamRequestFailedMessage = "Square upstream request failed.";
    private const string WebhookNotImplementedCode = "SQUARE_WEBHOOK_NOT_IMPLEMENTED";
    private const string WebhookNotImplementedMessage = "Square webhook processing is not implemented.";
    private static readonly Regex BearerTokenRegex = new(
        @"\bBearer\s+[A-Za-z0-9._~+/=-]{12,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex SquareTokenRegex = new(
        @"\b(?:EAAA[A-Za-z0-9._~-]{8,}|sq0(?:atp|csp|idp)-[A-Za-z0-9._~-]{8,})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex TokenValueRegex = new(
        @"\b(token|access_token|authorization)\s+[^,\s;]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    [Authorize(Policy = CashierAuthorizationPolicies.PaymentSettings)]
    [HttpGet("token")]
    public async Task<ActionResult<ApiResult<SquareTokenStatusResponse>>> GetToken(
        [FromQuery] string environment,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateEnvironmentAndTokenAsync<SquareTokenStatusResponse>(
            "token-status",
            environment,
            includeTokenPayload: true,
            cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        // 只返回 token 配置状态，避免后端 access token 再被设备端读取。
        return Ok(ApiResult<SquareTokenStatusResponse>.Ok(new SquareTokenStatusResponse(
            validation.Environment!,
            Configured: true,
            Enabled: true,
            validation.Token!.UpdatedAt)));
    }

    [Authorize(Policy = CashierAuthorizationPolicies.PaymentSettings)]
    [HttpGet("locations")]
    public async Task<ActionResult<ApiResult<IReadOnlyList<SquareLocationDto>>>> GetLocations(
        [FromQuery] string environment,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateEnvironmentAndTokenAsync<IReadOnlyList<SquareLocationDto>>(
            "locations",
            environment,
            includeTokenPayload: false,
            cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        return await ExecuteBackendAsync(
            "locations",
            backendService => backendService.GetLocationsAsync(validation.Environment!, cancellationToken));
    }

    [Authorize(Policy = CashierAuthorizationPolicies.PaymentSettings)]
    [HttpGet("devices")]
    public async Task<ActionResult<ApiResult<IReadOnlyList<SquareDeviceDto>>>> GetDevices(
        [FromQuery] string environment,
        [FromQuery] string locationId,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateEnvironmentAndTokenAsync<IReadOnlyList<SquareDeviceDto>>(
            "devices",
            environment,
            includeTokenPayload: false,
            cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        return await ExecuteBackendAsync(
            "devices",
            backendService => backendService.GetDevicesAsync(validation.Environment!, locationId, cancellationToken));
    }

    [Authorize(Policy = CashierAuthorizationPolicies.PaymentSettings)]
    [HttpGet("device-codes")]
    public async Task<ActionResult<ApiResult<IReadOnlyList<SquareDeviceCodeDto>>>> GetDeviceCodes(
        [FromQuery] string environment,
        [FromQuery] string locationId,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateEnvironmentAndTokenAsync<IReadOnlyList<SquareDeviceCodeDto>>(
            "device-codes",
            environment,
            includeTokenPayload: false,
            cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        return await ExecuteBackendAsync(
            "device-codes",
            backendService => backendService.GetDeviceCodesAsync(validation.Environment!, locationId, cancellationToken));
    }

    [Authorize(Policy = CashierAuthorizationPolicies.PaymentSettings)]
    [HttpPost("device-codes")]
    public async Task<ActionResult<ApiResult<SquareDeviceCodeDto>>> CreateDeviceCode(
        [FromBody] SquareCreateDeviceCodeRequest request,
        CancellationToken cancellationToken)
    {
        var idempotencyValidation = ValidateIdempotencyKey<SquareDeviceCodeDto>(request.IdempotencyKey);
        if (idempotencyValidation is not null)
        {
            return idempotencyValidation;
        }

        var validation = await ValidateEnvironmentAndTokenAsync<SquareDeviceCodeDto>(
            "create-device-code",
            request.Environment,
            includeTokenPayload: false,
            cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        return await ExecuteBackendAsync(
            "create-device-code",
            backendService => backendService.CreateDeviceCodeAsync(request with { Environment = validation.Environment! }, cancellationToken));
    }

    [Authorize(Policy = CashierAuthorizationPolicies.PaymentSettings)]
    [HttpGet("device-codes/{deviceCodeId}")]
    public async Task<ActionResult<ApiResult<SquareDeviceCodeDto?>>> GetDeviceCode(
        string deviceCodeId,
        [FromQuery] string environment,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateEnvironmentAndTokenAsync<SquareDeviceCodeDto?>(
            "device-code",
            environment,
            includeTokenPayload: false,
            cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        return await ExecuteBackendAsync(
            "device-code",
            backendService => backendService.GetDeviceCodeAsync(validation.Environment!, deviceCodeId, cancellationToken));
    }

    [Authorize(Policy = CashierAuthorizationPolicies.TakeCard)]
    [HttpPost("checkouts")]
    public async Task<ActionResult<ApiResult<SquareCheckoutStatusResponse>>> CreateCheckout(
        [FromBody] SquareCreateCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        var idempotencyValidation = ValidateIdempotencyKey<SquareCheckoutStatusResponse>(request.IdempotencyKey);
        if (idempotencyValidation is not null)
        {
            return idempotencyValidation;
        }

        var scope = GetAuthenticatedDeviceScope<SquareCheckoutStatusResponse>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        var validation = await ValidateEnvironmentAndTokenAsync<SquareCheckoutStatusResponse>(
            "create-checkout",
            request.Environment,
            includeTokenPayload: false,
            cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        return await ExecuteBackendAsync(
            "create-checkout",
            async backendService =>
            {
                var response = await backendService.CreateCheckoutAsync(
                    request with { Environment = validation.Environment! },
                    cancellationToken);
                await PersistCheckoutOriginAsync(
                    response,
                    scope.StoreCode!,
                    scope.DeviceCode!,
                    cancellationToken);
                return response;
            });
    }

    [HttpGet("checkouts/{checkoutId}")]
    public async Task<ActionResult<ApiResult<SquareCheckoutStatusResponse?>>> GetCheckout(
        string checkoutId,
        [FromQuery] string environment,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateEnvironmentAndTokenAsync<SquareCheckoutStatusResponse?>(
            "checkout",
            environment,
            includeTokenPayload: false,
            cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        var scopeResult = await ValidateCheckoutDeviceScopeAsync<SquareCheckoutStatusResponse?>(
            validation.Environment!,
            checkoutId,
            paymentId: null,
            cancellationToken);
        if (scopeResult is not null)
        {
            return scopeResult;
        }

        return await ExecuteBackendAsync(
            "checkout",
            backendService => backendService.GetCheckoutAsync(validation.Environment!, checkoutId, cancellationToken));
    }

    [HttpGet("payments/{paymentId}")]
    public async Task<ActionResult<ApiResult<SquarePaymentStatusDto?>>> GetPayment(
        string paymentId,
        [FromQuery] string environment,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateEnvironmentAndTokenAsync<SquarePaymentStatusDto?>(
            "payment",
            environment,
            includeTokenPayload: false,
            cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        var scopeResult = await ValidateCheckoutDeviceScopeAsync<SquarePaymentStatusDto?>(
            validation.Environment!,
            checkoutId: null,
            paymentId,
            cancellationToken);
        if (scopeResult is not null)
        {
            return scopeResult;
        }

        return await ExecuteBackendAsync(
            "payment",
            backendService => backendService.GetPaymentAsync(validation.Environment!, paymentId, cancellationToken));
    }

    [HttpPost("checkouts/{checkoutId}/cancel")]
    public async Task<ActionResult<ApiResult<SquareCheckoutStatusResponse>>> CancelCheckout(
        string checkoutId,
        [FromBody] SquareCheckoutActionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateEnvironmentAndTokenAsync<SquareCheckoutStatusResponse>(
            "cancel-checkout",
            request.Environment,
            includeTokenPayload: false,
            cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        var scopeResult = await ValidateCheckoutDeviceScopeAsync<SquareCheckoutStatusResponse>(
            validation.Environment!,
            checkoutId,
            paymentId: null,
            cancellationToken);
        if (scopeResult is not null)
        {
            return scopeResult;
        }

        return await ExecuteBackendAsync(
            "cancel-checkout",
            backendService => backendService.CancelCheckoutAsync(checkoutId, request with { Environment = validation.Environment! }, cancellationToken));
    }

    [HttpPost("checkouts/{checkoutId}/dismiss")]
    public async Task<ActionResult<ApiResult<SquareCheckoutStatusResponse>>> DismissCheckout(
        string checkoutId,
        [FromBody] SquareCheckoutActionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateEnvironmentAndTokenAsync<SquareCheckoutStatusResponse>(
            "dismiss-checkout",
            request.Environment,
            includeTokenPayload: false,
            cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        var scopeResult = await ValidateCheckoutDeviceScopeAsync<SquareCheckoutStatusResponse>(
            validation.Environment!,
            checkoutId,
            paymentId: null,
            cancellationToken);
        if (scopeResult is not null)
        {
            return scopeResult;
        }

        return await ExecuteBackendAsync(
            "dismiss-checkout",
            backendService => backendService.DismissCheckoutAsync(checkoutId, request with { Environment = validation.Environment! }, cancellationToken));
    }

    [Authorize(Policy = CashierAuthorizationPolicies.Returns)]
    [HttpPost("refunds")]
    public async Task<ActionResult<ApiResult<SquareRefundResponse>>> CreateRefund(
        [FromBody] SquareRefundRequest request,
        CancellationToken cancellationToken)
    {
        var idempotencyValidation = ValidateIdempotencyKey<SquareRefundResponse>(request.IdempotencyKey);
        if (idempotencyValidation is not null)
        {
            return idempotencyValidation;
        }

        var validation = await ValidateEnvironmentAndTokenAsync<SquareRefundResponse>(
            "refund",
            request.Environment,
            includeTokenPayload: false,
            cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        return await ExecuteBackendAsync(
            "refund",
            backendService => backendService.CreateRefundAsync(request with { Environment = validation.Environment! }, cancellationToken));
    }

    [AllowAnonymous]
    [HttpPost("webhooks")]
    public async Task<ActionResult<ApiResult<SquareWebhookAcceptedResponse>>> ReceiveWebhook(
        CancellationToken cancellationToken)
    {
        // 先把原始 body、签名头和回调 URL 原样交给后续服务，便于后面补真实验签逻辑。
        var webhookRequest = await BuildWebhookRequestAsync(cancellationToken);

        try
        {
            var response = await backendService.AcceptWebhookAsync(webhookRequest, cancellationToken);
            return Ok(ApiResult<SquareWebhookAcceptedResponse>.Ok(response));
        }
        catch (NotImplementedException exception)
        {
            LogSquareBackendFailure(
                "webhook",
                WebhookNotImplementedCode,
                StatusCodes.Status503ServiceUnavailable,
                exception);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResult<SquareWebhookAcceptedResponse>.Fail(
                    WebhookNotImplementedCode,
                    WebhookNotImplementedMessage));
        }
        catch (SquareTerminalBackendException exception)
        {
            var message = (int)exception.StatusCode >= 500
                ? BackendRequestFailedMessage
                : exception.Message;
            LogSquareBackendFailure(
                "webhook",
                exception.Code,
                (int)exception.StatusCode,
                exception);
            return StatusCode(
                (int)exception.StatusCode,
                ApiResult<SquareWebhookAcceptedResponse>.Fail(exception.Code, message));
        }
        catch (SquareTerminalRestException exception)
        {
            LogSquareUpstreamFailure("webhook", exception);
            return StatusCode(
                StatusCodes.Status502BadGateway,
                ApiResult<SquareWebhookAcceptedResponse>.Fail(UpstreamRequestFailedCode, UpstreamRequestFailedMessage));
        }
    }

    // 统一先校验环境和 token，保证新增骨架接口在服务未接入前也不会泄露任何 Square access token。
    private async Task<SquareRequestValidation<T>> ValidateEnvironmentAndTokenAsync<T>(
        string operationName,
        string? environment,
        bool includeTokenPayload,
        CancellationToken cancellationToken)
    {
        var normalizedEnvironment = SquareTokenService.NormalizeEnvironment(environment);
        if (normalizedEnvironment is null)
        {
            return new SquareRequestValidation<T>(
                null,
                null,
                BadRequest(ApiResult<T>.Fail(InvalidEnvironmentCode, InvalidEnvironmentMessage)));
        }

        try
        {
            var token = await squareTokenService.GetActiveTokenAsync(normalizedEnvironment, cancellationToken);
            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                return new SquareRequestValidation<T>(
                    normalizedEnvironment,
                    null,
                    NotFound(ApiResult<T>.Fail(TokenNotConfiguredCode, TokenNotConfiguredMessage)));
            }

            return new SquareRequestValidation<T>(
                normalizedEnvironment,
                includeTokenPayload ? token : null,
                null);
        }
        catch (Exception exception)
        {
            LogSquareBackendFailure(
                operationName,
                TokenReadFailedCode,
                StatusCodes.Status500InternalServerError,
                exception);
            return new SquareRequestValidation<T>(
                normalizedEnvironment,
                null,
                StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResult<T>.Fail(TokenReadFailedCode, TokenReadFailedMessage)));
        }
    }

    private Task<ActionResult<ApiResult<T>>> ExecuteBackendAsync<T>(
        string operationName,
        Func<ISquareTerminalBackendService, Task<T>> executeAsync)
    {
        return ExecuteBackendCoreAsync(operationName, backendService, executeAsync);
    }

    private (string? StoreCode, string? DeviceCode, ActionResult<ApiResult<T>>? Result) GetAuthenticatedDeviceScope<T>()
    {
        var storeCode = User.FindFirstValue(DeviceAuthConstants.StoreCodeClaim);
        var deviceCode = User.FindFirstValue(DeviceAuthConstants.DeviceCodeClaim);
        if (string.IsNullOrWhiteSpace(storeCode) || string.IsNullOrWhiteSpace(deviceCode))
        {
            return (null, null, DeviceAuthorizationExtensions.DeviceScopeForbidden<T>(
                "Device store and terminal scope are unavailable."));
        }

        // Square 会话来源只相信设备认证声明，不读取请求中的门店或设备字段。
        return (storeCode.Trim(), deviceCode.Trim(), null);
    }

    private async Task PersistCheckoutOriginAsync(
        SquareCheckoutStatusResponse checkout,
        string originStoreCode,
        string originDeviceCode,
        CancellationToken cancellationToken)
    {
        if (checkoutSessionRepository is null)
        {
            throw new InvalidOperationException("Square checkout session repository is unavailable.");
        }

        var paymentIds = checkout.PaymentIds?
            .Where(paymentId => !string.IsNullOrWhiteSpace(paymentId))
            .Select(paymentId => paymentId.Trim())
            .ToArray() ?? [];
        await checkoutSessionRepository.UpsertCheckoutSessionAsync(
            new SquareCheckoutSessionRecord
            {
                Environment = checkout.Environment,
                CheckoutId = checkout.CheckoutId,
                Status = checkout.Status ?? string.Empty,
                Amount = checkout.AmountMoney?.Amount,
                Currency = checkout.AmountMoney?.Currency,
                DeviceId = checkout.DeviceId,
                LocationId = checkout.LocationId,
                OriginStoreCode = originStoreCode,
                OriginDeviceCode = originDeviceCode,
                PaymentId = checkout.Payment?.PaymentId ?? paymentIds.FirstOrDefault(),
                PaymentIdsJson = paymentIds.Length == 0 ? null : JsonSerializer.Serialize(paymentIds),
                RawCheckoutJson = "{}",
                UpdatedAt = checkout.UpdatedAt ?? DateTimeOffset.UtcNow
            },
            cancellationToken);

        var boundSession = await checkoutSessionRepository.BindCheckoutOriginAsync(
            checkout.Environment,
            checkout.CheckoutId,
            originStoreCode,
            originDeviceCode,
            cancellationToken);
        if (boundSession is null ||
            !string.Equals(boundSession.OriginStoreCode, originStoreCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(boundSession.OriginDeviceCode, originDeviceCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new SquareTerminalBackendException(
                "SQUARE_CHECKOUT_SCOPE_CONFLICT",
                "Square checkout is already bound to a different POS device.",
                HttpStatusCode.Forbidden);
        }
    }

    private async Task<ActionResult<ApiResult<T>>?> ValidateCheckoutDeviceScopeAsync<T>(
        string environment,
        string? checkoutId,
        string? paymentId,
        CancellationToken cancellationToken)
    {
        var scope = GetAuthenticatedDeviceScope<T>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        if (checkoutSessionRepository is null)
        {
            return DeviceAuthorizationExtensions.DeviceScopeForbidden<T>(
                "Square checkout scope is unavailable.");
        }

        var session = checkoutId is not null
            ? await checkoutSessionRepository.GetCheckoutSessionAsync(environment, checkoutId, cancellationToken)
            : await checkoutSessionRepository.GetCheckoutSessionByPaymentIdAsync(environment, paymentId!, cancellationToken);

        // 历史无来源行必须关闭续接权限；不能用当前收银员身份猜测或补写来源。
        if (session is null ||
            string.IsNullOrWhiteSpace(session.OriginStoreCode) ||
            string.IsNullOrWhiteSpace(session.OriginDeviceCode) ||
            !string.Equals(session.OriginStoreCode, scope.StoreCode, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(session.OriginDeviceCode, scope.DeviceCode, StringComparison.OrdinalIgnoreCase))
        {
            return DeviceAuthorizationExtensions.DeviceScopeForbidden<T>(
                "Square checkout belongs to a different POS device or has no trusted origin scope.");
        }

        return null;
    }

    private async Task<ActionResult<ApiResult<T>>> ExecuteBackendCoreAsync<T>(
        string operationName,
        ISquareTerminalBackendService backendService,
        Func<ISquareTerminalBackendService, Task<T>> executeAsync)
    {
        try
        {
            var response = await executeAsync(backendService);
            return Ok(ApiResult<T>.Ok(response));
        }
        catch (NotImplementedException exception)
        {
            LogSquareBackendFailure(
                operationName,
                BackendNotImplementedCode,
                StatusCodes.Status503ServiceUnavailable,
                exception);
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResult<T>.Fail(BackendNotImplementedCode, BackendNotImplementedMessage));
        }
        catch (SquareTerminalBackendException exception)
        {
            var message = (int)exception.StatusCode >= 500
                ? BackendRequestFailedMessage
                : exception.Message;
            LogSquareBackendFailure(
                operationName,
                exception.Code,
                (int)exception.StatusCode,
                exception);
            return StatusCode(
                (int)exception.StatusCode,
                ApiResult<T>.Fail(exception.Code, message));
        }
        catch (SquareTerminalRestException exception)
        {
            LogSquareUpstreamFailure(operationName, exception);
            return StatusCode(
                StatusCodes.Status502BadGateway,
                ApiResult<T>.Fail(UpstreamRequestFailedCode, UpstreamRequestFailedMessage));
        }
    }

    private async Task<SquareWebhookRequest> BuildWebhookRequestAsync(CancellationToken cancellationToken)
    {
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        Request.Body.Position = 0;

        return new SquareWebhookRequest(
            rawBody,
            NormalizeHeaderValue(Request.Headers["x-square-hmacsha256-signature"].ToString()),
            NormalizeHeaderValue(Request.Headers["square-environment"].ToString()),
            BuildNotificationUrl());
    }

    private string BuildNotificationUrl()
    {
        return $"{Request.Scheme}://{Request.Host}{Request.PathBase}{Request.Path}{Request.QueryString}";
    }

    private static string? NormalizeHeaderValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private void LogSquareBackendFailure(
        string operationName,
        string errorCode,
        int statusCode,
        Exception exception)
    {
        logger.LogError(
            "Square backend operation {Operation} failed with {ErrorCode} HTTP {StatusCode}. ExceptionType={ExceptionType}; Detail={Detail}",
            operationName,
            errorCode,
            statusCode,
            exception.GetType().Name,
            SanitizeLogDetail(exception.Message));
    }

    private void LogSquareUpstreamFailure(
        string operationName,
        SquareTerminalRestException exception)
    {
        logger.LogError(
            "Square upstream operation {Operation} failed with {ErrorCode} HTTP {StatusCode}. ExceptionType={ExceptionType}; Detail={Detail}",
            operationName,
            UpstreamRequestFailedCode,
            (int)exception.StatusCode,
            exception.GetType().Name,
            SanitizeLogDetail(exception.Message));
    }

    private static string SanitizeLogDetail(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return string.Empty;
        }

        // 日志只保留脱敏后的诊断文本，避免 token 通过异常消息进入集中日志。
        var sanitized = BearerTokenRegex.Replace(detail, "Bearer [REDACTED]");
        sanitized = SquareTokenRegex.Replace(sanitized, "[REDACTED]");
        return TokenValueRegex.Replace(sanitized, "$1 [REDACTED]");
    }

    private ActionResult<ApiResult<T>>? ValidateIdempotencyKey<T>(string? idempotencyKey)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        // Controller 先做轻量校验，Service 层仍保留同样防线，避免空 key 触发 token/backend 调用。
        return BadRequest(ApiResult<T>.Fail(
            IdempotencyKeyRequiredCode,
            IdempotencyKeyRequiredMessage));
    }

    private sealed record SquareRequestValidation<T>(
        string? Environment,
        SquareTokenResponse? Token,
        ActionResult<ApiResult<T>>? Result);
}
