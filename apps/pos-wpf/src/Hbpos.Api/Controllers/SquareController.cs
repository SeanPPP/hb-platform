using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Square;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/v1/square")]
[Authorize]
public sealed class SquareController(ISquareTokenService squareTokenService) : ControllerBase
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

    [HttpGet("token")]
    public async Task<ActionResult<ApiResult<SquareTokenStatusResponse>>> GetToken(
        [FromQuery] string environment,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateEnvironmentAndTokenAsync<SquareTokenStatusResponse>(environment, includeTokenPayload: true, cancellationToken);
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

    [HttpGet("locations")]
    public async Task<ActionResult<ApiResult<IReadOnlyList<SquareLocationDto>>>> GetLocations(
        [FromQuery] string environment,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateEnvironmentAndTokenAsync<IReadOnlyList<SquareLocationDto>>(environment, includeTokenPayload: false, cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        return await ExecuteBackendAsync(
            backendService => backendService.GetLocationsAsync(validation.Environment!, cancellationToken));
    }

    [HttpGet("devices")]
    public async Task<ActionResult<ApiResult<IReadOnlyList<SquareDeviceDto>>>> GetDevices(
        [FromQuery] string environment,
        [FromQuery] string locationId,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateEnvironmentAndTokenAsync<IReadOnlyList<SquareDeviceDto>>(environment, includeTokenPayload: false, cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        return await ExecuteBackendAsync(
            backendService => backendService.GetDevicesAsync(validation.Environment!, locationId, cancellationToken));
    }

    [HttpGet("device-codes")]
    public async Task<ActionResult<ApiResult<IReadOnlyList<SquareDeviceCodeDto>>>> GetDeviceCodes(
        [FromQuery] string environment,
        [FromQuery] string locationId,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateEnvironmentAndTokenAsync<IReadOnlyList<SquareDeviceCodeDto>>(environment, includeTokenPayload: false, cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        return await ExecuteBackendAsync(
            backendService => backendService.GetDeviceCodesAsync(validation.Environment!, locationId, cancellationToken));
    }

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

        var validation = await ValidateEnvironmentAndTokenAsync<SquareDeviceCodeDto>(request.Environment, includeTokenPayload: false, cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        return await ExecuteBackendAsync(
            backendService => backendService.CreateDeviceCodeAsync(request with { Environment = validation.Environment! }, cancellationToken));
    }

    [HttpGet("device-codes/{deviceCodeId}")]
    public async Task<ActionResult<ApiResult<SquareDeviceCodeDto?>>> GetDeviceCode(
        string deviceCodeId,
        [FromQuery] string environment,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateEnvironmentAndTokenAsync<SquareDeviceCodeDto?>(environment, includeTokenPayload: false, cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        return await ExecuteBackendAsync(
            backendService => backendService.GetDeviceCodeAsync(validation.Environment!, deviceCodeId, cancellationToken));
    }

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

        var validation = await ValidateEnvironmentAndTokenAsync<SquareCheckoutStatusResponse>(request.Environment, includeTokenPayload: false, cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        return await ExecuteBackendAsync(
            backendService => backendService.CreateCheckoutAsync(request with { Environment = validation.Environment! }, cancellationToken));
    }

    [HttpGet("checkouts/{checkoutId}")]
    public async Task<ActionResult<ApiResult<SquareCheckoutStatusResponse?>>> GetCheckout(
        string checkoutId,
        [FromQuery] string environment,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateEnvironmentAndTokenAsync<SquareCheckoutStatusResponse?>(environment, includeTokenPayload: false, cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        return await ExecuteBackendAsync(
            backendService => backendService.GetCheckoutAsync(validation.Environment!, checkoutId, cancellationToken));
    }

    [HttpGet("payments/{paymentId}")]
    public async Task<ActionResult<ApiResult<SquarePaymentStatusDto?>>> GetPayment(
        string paymentId,
        [FromQuery] string environment,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateEnvironmentAndTokenAsync<SquarePaymentStatusDto?>(environment, includeTokenPayload: false, cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        return await ExecuteBackendAsync(
            backendService => backendService.GetPaymentAsync(validation.Environment!, paymentId, cancellationToken));
    }

    [HttpPost("checkouts/{checkoutId}/cancel")]
    public async Task<ActionResult<ApiResult<SquareCheckoutStatusResponse>>> CancelCheckout(
        string checkoutId,
        [FromBody] SquareCheckoutActionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateEnvironmentAndTokenAsync<SquareCheckoutStatusResponse>(request.Environment, includeTokenPayload: false, cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        return await ExecuteBackendAsync(
            backendService => backendService.CancelCheckoutAsync(checkoutId, request with { Environment = validation.Environment! }, cancellationToken));
    }

    [HttpPost("checkouts/{checkoutId}/dismiss")]
    public async Task<ActionResult<ApiResult<SquareCheckoutStatusResponse>>> DismissCheckout(
        string checkoutId,
        [FromBody] SquareCheckoutActionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateEnvironmentAndTokenAsync<SquareCheckoutStatusResponse>(request.Environment, includeTokenPayload: false, cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        return await ExecuteBackendAsync(
            backendService => backendService.DismissCheckoutAsync(checkoutId, request with { Environment = validation.Environment! }, cancellationToken));
    }

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

        var validation = await ValidateEnvironmentAndTokenAsync<SquareRefundResponse>(request.Environment, includeTokenPayload: false, cancellationToken);
        if (validation.Result is not null)
        {
            return validation.Result;
        }

        return await ExecuteBackendAsync(
            backendService => backendService.CreateRefundAsync(request with { Environment = validation.Environment! }, cancellationToken));
    }

    [AllowAnonymous]
    [HttpPost("webhooks")]
    public async Task<ActionResult<ApiResult<SquareWebhookAcceptedResponse>>> ReceiveWebhook(
        CancellationToken cancellationToken)
    {
        var backendService = HttpContext.RequestServices.GetService<ISquareTerminalBackendService>();
        if (backendService is null)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResult<SquareWebhookAcceptedResponse>.Fail(
                    WebhookNotImplementedCode,
                    WebhookNotImplementedMessage));
        }

        // 先把原始 body、签名头和回调 URL 原样交给后续服务，便于后面补真实验签逻辑。
        var webhookRequest = await BuildWebhookRequestAsync(cancellationToken);

        try
        {
            var response = await backendService.AcceptWebhookAsync(webhookRequest, cancellationToken);
            return Ok(ApiResult<SquareWebhookAcceptedResponse>.Ok(response));
        }
        catch (NotImplementedException)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResult<SquareWebhookAcceptedResponse>.Fail(
                    WebhookNotImplementedCode,
                    WebhookNotImplementedMessage));
        }
        catch (SquareTerminalBackendException exception)
        {
            return StatusCode(
                (int)exception.StatusCode,
                ApiResult<SquareWebhookAcceptedResponse>.Fail(exception.Code, exception.Message));
        }
        catch (SquareTerminalRestException)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                ApiResult<SquareWebhookAcceptedResponse>.Fail(UpstreamRequestFailedCode, UpstreamRequestFailedMessage));
        }
    }

    // 统一先校验环境和 token，保证新增骨架接口在服务未接入前也不会泄露任何 Square access token。
    private async Task<SquareRequestValidation<T>> ValidateEnvironmentAndTokenAsync<T>(
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
        catch
        {
            return new SquareRequestValidation<T>(
                normalizedEnvironment,
                null,
                StatusCode(
                    StatusCodes.Status500InternalServerError,
                    ApiResult<T>.Fail(TokenReadFailedCode, TokenReadFailedMessage)));
        }
    }

    private Task<ActionResult<ApiResult<T>>> ExecuteBackendAsync<T>(
        Func<ISquareTerminalBackendService, Task<T>> executeAsync)
    {
        var backendService = HttpContext.RequestServices.GetService<ISquareTerminalBackendService>();
        if (backendService is null)
        {
            return Task.FromResult<ActionResult<ApiResult<T>>>(StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResult<T>.Fail(BackendNotImplementedCode, BackendNotImplementedMessage)));
        }

        return ExecuteBackendCoreAsync(backendService, executeAsync);
    }

    private async Task<ActionResult<ApiResult<T>>> ExecuteBackendCoreAsync<T>(
        ISquareTerminalBackendService backendService,
        Func<ISquareTerminalBackendService, Task<T>> executeAsync)
    {
        try
        {
            var response = await executeAsync(backendService);
            return Ok(ApiResult<T>.Ok(response));
        }
        catch (NotImplementedException)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                ApiResult<T>.Fail(BackendNotImplementedCode, BackendNotImplementedMessage));
        }
        catch (SquareTerminalBackendException exception)
        {
            var message = (int)exception.StatusCode >= 500
                ? BackendRequestFailedMessage
                : exception.Message;
            return StatusCode(
                (int)exception.StatusCode,
                ApiResult<T>.Fail(exception.Code, message));
        }
        catch (SquareTerminalRestException)
        {
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
