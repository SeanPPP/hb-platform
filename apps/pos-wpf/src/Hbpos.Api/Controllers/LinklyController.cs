using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using Hbpos.Api.Auth;
using Hbpos.Api.Services;
using Hbpos.Contracts.Common;
using Hbpos.Contracts.Devices;
using Hbpos.Contracts.Linkly;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Hbpos.Api.Controllers;

[ApiController]
[Route("api/v1/linkly")]
[Authorize]
public sealed class LinklyController(
    ILinklyCloudCredentialService linklyCloudCredentialService,
    ILinklyCloudBackendAsyncService linklyCloudBackendAsyncService,
    ILinklyCloudPairingService linklyCloudPairingService,
    ILogger<LinklyController>? logger = null) : ControllerBase
{
    private const string CloudCredentialEnvironmentInvalidCode = "LINKLY_CLOUD_CREDENTIAL_ENVIRONMENT_INVALID";
    private const string CloudCredentialInvalidCode = "LINKLY_CLOUD_CREDENTIAL_REQUEST_INVALID";
    private const string CloudCredentialReadFailedCode = "LINKLY_CLOUD_CREDENTIAL_READ_FAILED";
    private const string CloudCredentialWriteFailedCode = "LINKLY_CLOUD_CREDENTIAL_WRITE_FAILED";
    private const string CloudCredentialReadFailedMessage = "Failed to load Linkly Cloud credential configuration.";
    private const string CloudCredentialWriteFailedMessage = "Failed to save Linkly Cloud credential configuration.";
    private const string CloudBackendInvalidCode = "LINKLY_CLOUD_BACKEND_REQUEST_INVALID";
    private const string CloudBackendActiveCode = "LINKLY_CLOUD_BACKEND_ACTIVE_TRANSACTION";
    private const string CloudBackendNotFoundCode = "LINKLY_CLOUD_BACKEND_SESSION_NOT_FOUND";
    private const string CloudBackendFailedCode = "LINKLY_CLOUD_BACKEND_FAILED";
    private const string CloudBackendPairInvalidCode = "LINKLY_CLOUD_BACKEND_PAIR_REQUEST_INVALID";
    private const string CloudBackendPairCredentialMissingCode = "LINKLY_CLOUD_BACKEND_PAIR_CREDENTIAL_MISSING";
    private const string CloudBackendPairInProgressCode = "LINKLY_CLOUD_BACKEND_PAIR_IN_PROGRESS";
    private const string CloudBackendPairRejectedCode = "LINKLY_CLOUD_BACKEND_PAIR_REJECTED";
    private const string CloudBackendPairUpstreamFailedCode = "LINKLY_CLOUD_BACKEND_PAIR_UPSTREAM_FAILED";
    private const string CloudBackendPairTimeoutCode = "LINKLY_CLOUD_BACKEND_PAIR_TIMEOUT";
    private const string CloudBackendPairPersistenceFailedCode = "LINKLY_CLOUD_BACKEND_PAIR_PERSISTENCE_FAILED";
    private const string CloudBackendPairPreparationFailedCode = "LINKLY_CLOUD_BACKEND_PAIR_PREPARATION_FAILED";

    [Authorize(Policy = CashierAuthorizationPolicies.PaymentSettings)]
    [HttpPost("cloud-backend/pair")]
    [LinklyCloudPairRequestModelState]
    [ProducesResponseType(typeof(ApiResult<LinklyCloudBackendTerminalCredentialResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResult<LinklyCloudBackendTerminalCredentialResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResult<LinklyCloudBackendTerminalCredentialResponse>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResult<LinklyCloudBackendTerminalCredentialResponse>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResult<LinklyCloudBackendTerminalCredentialResponse>), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(ApiResult<LinklyCloudBackendTerminalCredentialResponse>), StatusCodes.Status502BadGateway)]
    [ProducesResponseType(typeof(ApiResult<LinklyCloudBackendTerminalCredentialResponse>), StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<ApiResult<LinklyCloudBackendTerminalCredentialResponse>>> PairCloudBackend(
        [FromBody] LinklyCloudBackendPairRequest request,
        CancellationToken cancellationToken)
    {
        var scope = GetAuthenticatedDeviceScope<LinklyCloudBackendTerminalCredentialResponse>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        if (request is null)
        {
            return BadRequest(ApiResult<LinklyCloudBackendTerminalCredentialResponse>.Fail(
                CloudBackendPairInvalidCode,
                "request body is required."));
        }

        try
        {
            var response = await linklyCloudPairingService.PairAsync(
                scope.StoreCode!,
                scope.DeviceCode!,
                request,
                GetUpdatedByClaim(),
                cancellationToken);
            return Ok(ApiResult<LinklyCloudBackendTerminalCredentialResponse>.Ok(response));
        }
        catch (LinklyCloudPairingValidationException ex)
        {
            return BadRequest(ApiResult<LinklyCloudBackendTerminalCredentialResponse>.Fail(
                CloudBackendPairInvalidCode,
                ex.Message));
        }
        catch (LinklyCloudPairingCredentialMissingException ex)
        {
            return Conflict(ApiResult<LinklyCloudBackendTerminalCredentialResponse>.Fail(
                CloudBackendPairCredentialMissingCode,
                ex.Message));
        }
        catch (LinklyCloudPairingInProgressException ex)
        {
            return Conflict(ApiResult<LinklyCloudBackendTerminalCredentialResponse>.Fail(
                CloudBackendPairInProgressCode,
                ex.Message));
        }
        catch (LinklyCloudPairingRejectedException ex)
        {
            return UnprocessableEntity(ApiResult<LinklyCloudBackendTerminalCredentialResponse>.Fail(
                CloudBackendPairRejectedCode,
                ex.Message));
        }
        catch (LinklyCloudPairingUpstreamException ex)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                ApiResult<LinklyCloudBackendTerminalCredentialResponse>.Fail(
                    CloudBackendPairUpstreamFailedCode,
                    ex.Message));
        }
        catch (LinklyCloudPairingTimeoutException ex)
        {
            return StatusCode(
                StatusCodes.Status504GatewayTimeout,
                ApiResult<LinklyCloudBackendTerminalCredentialResponse>.Fail(
                    CloudBackendPairTimeoutCode,
                    ex.Message));
        }
        catch (LinklyCloudPairingPersistenceException)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResult<LinklyCloudBackendTerminalCredentialResponse>.Fail(
                    CloudBackendPairPersistenceFailedCode,
                    "Linkly Cloud pairing succeeded but the terminal credential could not be saved."));
        }
        catch (LinklyCloudPairingPreparationException ex)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResult<LinklyCloudBackendTerminalCredentialResponse>.Fail(
                    CloudBackendPairPreparationFailedCode,
                    ex.Message));
        }
        catch (Exception ex)
        {
            Log($"cloud backend pair failed error={ex.GetType().Name}");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResult<LinklyCloudBackendTerminalCredentialResponse>.Fail(
                    CloudBackendFailedCode,
                    "Failed to pair Linkly Cloud backend terminal."));
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.TakeCard)]
    [HttpGet("cloud-credential")]
    public async Task<ActionResult<ApiResult<LinklyCloudCredentialResponse>>> GetCloudCredential(
        [FromQuery] string? environment,
        CancellationToken cancellationToken)
    {
        var storeCode = User.FindFirstValue(DeviceAuthConstants.StoreCodeClaim);
        if (string.IsNullOrWhiteSpace(storeCode))
        {
            Log("cloud credential request rejected reason=missing-store-claim");
            return DeviceAuthorizationExtensions.DeviceScopeForbidden<LinklyCloudCredentialResponse>(
                "Device store scope is unavailable.");
        }

        var normalizedEnvironment = LinklyCloudCredentialService.NormalizeEnvironment(environment);
        if (normalizedEnvironment is null)
        {
            return BadRequest(ApiResult<LinklyCloudCredentialResponse>.Fail(
                CloudCredentialEnvironmentInvalidCode,
                "environment must be Production or Sandbox"));
        }

        var stopwatch = Stopwatch.StartNew();
        Log($"cloud credential request store={LogValue(storeCode)} environment={normalizedEnvironment}");
        try
        {
            var credential = await linklyCloudCredentialService.GetByStoreCodeAsync(
                storeCode,
                normalizedEnvironment,
                cancellationToken);
            stopwatch.Stop();
            if (credential is null)
            {
                Log($"cloud credential response store={LogValue(storeCode)} environment={normalizedEnvironment} status=404 elapsedMs={stopwatch.ElapsedMilliseconds}");
                return NotFound(ApiResult<LinklyCloudCredentialResponse>.Fail(
                    "LINKLY_CLOUD_CREDENTIAL_NOT_CONFIGURED",
                    "Linkly Cloud credential is not configured for this store."));
            }

            Log($"cloud credential response store={LogValue(storeCode)} environment={normalizedEnvironment} status=200 updatedAt={credential.UpdatedAt:O} elapsedMs={stopwatch.ElapsedMilliseconds}");
            return Ok(ApiResult<LinklyCloudCredentialResponse>.Ok(credential));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Log($"cloud credential response store={LogValue(storeCode)} environment={normalizedEnvironment} status=500 error={ex.GetType().Name} elapsedMs={stopwatch.ElapsedMilliseconds}");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResult<LinklyCloudCredentialResponse>.Fail(
                    CloudCredentialReadFailedCode,
                    CloudCredentialReadFailedMessage));
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.PaymentSettings)]
    [HttpPut("cloud-credential")]
    public async Task<ActionResult<ApiResult<LinklyCloudCredentialUpsertResponse>>> UpsertCloudCredential(
        [FromBody] LinklyCloudCredentialUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var storeCode = User.FindFirstValue(DeviceAuthConstants.StoreCodeClaim);
        if (string.IsNullOrWhiteSpace(storeCode))
        {
            Log("cloud credential upsert rejected reason=missing-store-claim");
            return DeviceAuthorizationExtensions.DeviceScopeForbidden<LinklyCloudCredentialUpsertResponse>(
                "Device store scope is unavailable.");
        }

        if (request is null)
        {
            return BadRequest(ApiResult<LinklyCloudCredentialUpsertResponse>.Fail(
                CloudCredentialInvalidCode,
                "request body is required."));
        }

        try
        {
            var normalizedEnvironment = LinklyCloudCredentialService.NormalizeEnvironment(request.Environment);
            if (normalizedEnvironment is null)
            {
                return BadRequest(ApiResult<LinklyCloudCredentialUpsertResponse>.Fail(
                    CloudCredentialEnvironmentInvalidCode,
                    "environment must be Production or Sandbox"));
            }

            Log($"cloud credential upsert request store={LogValue(storeCode)} environment={normalizedEnvironment}");
            var response = await linklyCloudCredentialService.UpsertAsync(
                storeCode,
                request,
                GetUpdatedByClaim(),
                cancellationToken);
            Log($"cloud credential upsert response store={LogValue(storeCode)} environment={response.Environment} status=200 updatedAt={response.UpdatedAt:O}");
            return Ok(ApiResult<LinklyCloudCredentialUpsertResponse>.Ok(response));
        }
        catch (LinklyCloudCredentialValidationException ex)
        {
            return BadRequest(ApiResult<LinklyCloudCredentialUpsertResponse>.Fail(
                CloudCredentialInvalidCode,
                ex.Message));
        }
        catch (Exception ex)
        {
            Log($"cloud credential upsert failed store={LogValue(storeCode)} error={ex.GetType().Name}");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResult<LinklyCloudCredentialUpsertResponse>.Fail(
                    CloudCredentialWriteFailedCode,
                    CloudCredentialWriteFailedMessage));
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.TakeCard)]
    [HttpPost("cloud-backend/transactions")]
    public async Task<ActionResult<ApiResult<LinklyCloudBackendSessionResponse>>> StartCloudBackendTransaction(
        [FromBody] LinklyCloudBackendTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var scope = GetAuthenticatedDeviceScope<LinklyCloudBackendSessionResponse>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        try
        {
            var response = await linklyCloudBackendAsyncService.StartTransactionAsync(
                scope.StoreCode!,
                scope.DeviceCode!,
                request,
                cancellationToken);
            return Ok(ApiResult<LinklyCloudBackendSessionResponse>.Ok(
                LinklyCardTransactionSanitizer.Attach(response)));
        }
        catch (LinklyCloudBackendActiveTransactionException ex)
        {
            return Conflict(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendActiveCode,
                string.IsNullOrWhiteSpace(ex.ActiveSessionId)
                    ? "An active Linkly Cloud transaction already exists for this terminal."
                    : $"An active Linkly Cloud transaction already exists for this terminal: {ex.ActiveSessionId}."));
        }
        catch (LinklyCloudBackendValidationException ex)
        {
            return BadRequest(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendInvalidCode,
                ex.Message));
        }
        catch (Exception ex)
        {
            Log($"cloud backend transaction failed error={ex.GetType().Name}");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                    CloudBackendFailedCode,
                    "Failed to start Linkly Cloud backend transaction."));
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.DailyCloseSave)]
    [HttpPost("cloud-backend/settlements")]
    public async Task<ActionResult<ApiResult<LinklyCloudBackendSessionResponse>>> StartCloudBackendSettlement(
        [FromBody] LinklyCloudBackendSettlementRequest request,
        CancellationToken cancellationToken)
    {
        var scope = GetAuthenticatedDeviceScope<LinklyCloudBackendSessionResponse>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        try
        {
            var response = await linklyCloudBackendAsyncService.StartSettlementAsync(
                scope.StoreCode!,
                scope.DeviceCode!,
                request,
                cancellationToken);
            return Ok(ApiResult<LinklyCloudBackendSessionResponse>.Ok(response));
        }
        catch (LinklyCloudBackendActiveTransactionException ex)
        {
            return Conflict(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendActiveCode,
                string.IsNullOrWhiteSpace(ex.ActiveSessionId)
                    ? "An active Linkly Cloud operation already exists for this terminal."
                    : $"An active Linkly Cloud operation already exists for this terminal: {ex.ActiveSessionId}."));
        }
        catch (LinklyCloudBackendValidationException ex)
        {
            return BadRequest(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendInvalidCode,
                ex.Message));
        }
        catch (Exception ex)
        {
            Log($"cloud backend settlement failed error={ex.GetType().Name}");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                    CloudBackendFailedCode,
                    "Failed to start Linkly Cloud backend settlement."));
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.PaymentSettings)]
    [HttpPut("cloud-backend/terminal")]
    public async Task<ActionResult<ApiResult<LinklyCloudBackendTerminalCredentialResponse>>> UpsertCloudBackendTerminalCredential(
        [FromBody] LinklyCloudBackendTerminalCredentialUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var scope = GetAuthenticatedDeviceScope<LinklyCloudBackendTerminalCredentialResponse>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        if (request is null)
        {
            return BadRequest(ApiResult<LinklyCloudBackendTerminalCredentialResponse>.Fail(
                CloudBackendInvalidCode,
                "request body is required."));
        }

        try
        {
            var response = await linklyCloudBackendAsyncService.UpsertTerminalCredentialAsync(
                scope.StoreCode!,
                scope.DeviceCode!,
                request,
                GetUpdatedByClaim(),
                cancellationToken);
            return Ok(ApiResult<LinklyCloudBackendTerminalCredentialResponse>.Ok(response));
        }
        catch (LinklyCloudBackendValidationException ex)
        {
            return BadRequest(ApiResult<LinklyCloudBackendTerminalCredentialResponse>.Fail(
                CloudBackendInvalidCode,
                ex.Message));
        }
        catch (Exception ex)
        {
            Log($"cloud backend terminal upsert failed error={ex.GetType().Name}");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResult<LinklyCloudBackendTerminalCredentialResponse>.Fail(
                    CloudBackendFailedCode,
                    "Failed to save Linkly Cloud backend terminal credential."));
        }
    }

    [HttpGet("cloud-backend/transactions/active")]
    public async Task<ActionResult<ApiResult<LinklyCloudBackendSessionResponse>>> GetActiveCloudBackendTransaction(
        [FromQuery] string? environment,
        CancellationToken cancellationToken)
    {
        var scope = GetAuthenticatedDeviceScope<LinklyCloudBackendSessionResponse>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        try
        {
            var response = await linklyCloudBackendAsyncService.GetActiveSessionAsync(
                scope.StoreCode!,
                scope.DeviceCode!,
                environment ?? string.Empty,
                cancellationToken);
            return response is null
                ? NotFound(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                    CloudBackendNotFoundCode,
                    "Linkly Cloud backend session was not found."))
                : Ok(ApiResult<LinklyCloudBackendSessionResponse>.Ok(response));
        }
        catch (LinklyCloudBackendValidationException ex)
        {
            return BadRequest(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendInvalidCode,
                ex.Message));
        }
    }

    [HttpGet("cloud-backend/transactions/resumable")]
    public async Task<ActionResult<ApiResult<LinklyCloudBackendSessionResponse>>> GetResumableCloudBackendTransaction(
        [FromQuery] string? environment,
        CancellationToken cancellationToken)
    {
        var scope = GetAuthenticatedDeviceScope<LinklyCloudBackendSessionResponse>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        try
        {
            var response = await linklyCloudBackendAsyncService.GetResumableSessionAsync(
                scope.StoreCode!,
                scope.DeviceCode!,
                environment ?? string.Empty,
                cancellationToken);
            return response is null
                ? NotFound(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                    CloudBackendNotFoundCode,
                    "Linkly Cloud backend session was not found."))
                : Ok(ApiResult<LinklyCloudBackendSessionResponse>.Ok(response));
        }
        catch (LinklyCloudBackendValidationException ex)
        {
            return BadRequest(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendInvalidCode,
                ex.Message));
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.DailyCloseSave)]
    [HttpGet("cloud-backend/settlements/resumable")]
    public async Task<ActionResult<ApiResult<LinklyCloudBackendSessionResponse>>> GetResumableCloudBackendSettlement(
        [FromQuery] string? environment,
        CancellationToken cancellationToken)
    {
        var scope = GetAuthenticatedDeviceScope<LinklyCloudBackendSessionResponse>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        try
        {
            var response = await linklyCloudBackendAsyncService.GetResumableSettlementSessionAsync(
                scope.StoreCode!,
                scope.DeviceCode!,
                environment ?? string.Empty,
                cancellationToken);
            return response is null
                ? NotFound(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                    CloudBackendNotFoundCode,
                    "Linkly Cloud backend settlement was not found."))
                : Ok(ApiResult<LinklyCloudBackendSessionResponse>.Ok(response));
        }
        catch (LinklyCloudBackendValidationException ex)
        {
            return BadRequest(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendInvalidCode,
                ex.Message));
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.PaymentSettings)]
    [HttpGet("cloud-backend/health")]
    public async Task<ActionResult<ApiResult<LinklyCloudBackendHealthResponse>>> GetCloudBackendHealth(
        [FromQuery] string? environment,
        CancellationToken cancellationToken)
    {
        var scope = GetAuthenticatedDeviceScope<LinklyCloudBackendHealthResponse>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        try
        {
            var response = await linklyCloudBackendAsyncService.GetHealthAsync(
                scope.StoreCode!,
                scope.DeviceCode!,
                environment ?? string.Empty,
                cancellationToken);
            return Ok(ApiResult<LinklyCloudBackendHealthResponse>.Ok(response));
        }
        catch (LinklyCloudBackendValidationException ex)
        {
            return BadRequest(ApiResult<LinklyCloudBackendHealthResponse>.Fail(
                CloudBackendInvalidCode,
                ex.Message));
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.PaymentSettings)]
    [HttpPost("cloud-backend/status-test")]
    public async Task<ActionResult<ApiResult<LinklyCloudBackendStatusTestResponse>>> RunCloudBackendStatusTest(
        [FromQuery] string? environment,
        CancellationToken cancellationToken)
    {
        var scope = GetAuthenticatedDeviceScope<LinklyCloudBackendStatusTestResponse>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        try
        {
            var response = await linklyCloudBackendAsyncService.RunStatusTestAsync(
                scope.StoreCode!,
                scope.DeviceCode!,
                environment ?? string.Empty,
                cancellationToken);
            return Ok(ApiResult<LinklyCloudBackendStatusTestResponse>.Ok(response));
        }
        catch (LinklyCloudBackendValidationException ex)
        {
            return BadRequest(ApiResult<LinklyCloudBackendStatusTestResponse>.Fail(
                CloudBackendInvalidCode,
                ex.Message));
        }
        catch (Exception ex)
        {
            Log($"cloud-backend status-test error={ex.GetType().Name} message={ex.Message}");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResult<LinklyCloudBackendStatusTestResponse>.Fail(
                    CloudBackendFailedCode,
                    "An unexpected error occurred."));
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.PaymentSettings)]
    [HttpPost("cloud-backend/logon-test")]
    public async Task<ActionResult<ApiResult<LinklyCloudBackendLogonTestResponse>>> RunCloudBackendLogonTest(
        [FromQuery] string? environment,
        CancellationToken cancellationToken)
    {
        var scope = GetAuthenticatedDeviceScope<LinklyCloudBackendLogonTestResponse>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        try
        {
            var response = await linklyCloudBackendAsyncService.RunLogonTestAsync(
                scope.StoreCode!,
                scope.DeviceCode!,
                environment ?? string.Empty,
                cancellationToken);
            return Ok(ApiResult<LinklyCloudBackendLogonTestResponse>.Ok(response));
        }
        catch (LinklyCloudBackendValidationException ex)
        {
            return BadRequest(ApiResult<LinklyCloudBackendLogonTestResponse>.Fail(
                CloudBackendInvalidCode,
                ex.Message));
        }
        catch (Exception ex)
        {
            Log($"cloud-backend logon-test error={ex.GetType().Name} message={ex.Message}");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResult<LinklyCloudBackendLogonTestResponse>.Fail(
                    CloudBackendFailedCode,
                    "An unexpected error occurred."));
        }
    }

    [HttpGet("cloud-backend/transactions/{sessionId}/status")]
    public async Task<ActionResult<ApiResult<LinklyCloudBackendSessionResponse>>> GetCloudBackendTransactionStatus(
        string sessionId,
        [FromQuery] string? environment,
        CancellationToken cancellationToken)
    {
        var scope = GetAuthenticatedDeviceScope<LinklyCloudBackendSessionResponse>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        try
        {
            var response = await linklyCloudBackendAsyncService.GetStatusAsync(
                scope.StoreCode!,
                scope.DeviceCode!,
                environment ?? string.Empty,
                sessionId,
                cancellationToken);
            return response is null
                ? NotFound(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                    CloudBackendNotFoundCode,
                    "Linkly Cloud backend session was not found."))
                : Ok(ApiResult<LinklyCloudBackendSessionResponse>.Ok(
                    LinklyCardTransactionSanitizer.Attach(response)));
        }
        catch (LinklyCloudBackendValidationException ex)
        {
            return BadRequest(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendInvalidCode,
                ex.Message));
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.DailyCloseSave)]
    [HttpGet("cloud-backend/settlements/{sessionId}/status")]
    public async Task<ActionResult<ApiResult<LinklyCloudBackendSessionResponse>>> GetCloudBackendSettlementStatus(
        string sessionId,
        [FromQuery] string? environment,
        CancellationToken cancellationToken)
    {
        var scope = GetAuthenticatedDeviceScope<LinklyCloudBackendSessionResponse>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        try
        {
            var response = await linklyCloudBackendAsyncService.GetSettlementStatusAsync(
                scope.StoreCode!,
                scope.DeviceCode!,
                environment ?? string.Empty,
                sessionId,
                cancellationToken);
            return response is null
                ? NotFound(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                    CloudBackendNotFoundCode,
                    "Linkly Cloud backend settlement was not found."))
                : Ok(ApiResult<LinklyCloudBackendSessionResponse>.Ok(response));
        }
        catch (LinklyCloudBackendValidationException ex)
        {
            return BadRequest(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendInvalidCode,
                ex.Message));
        }
    }

    [HttpPost("cloud-backend/transactions/{sessionId}/acknowledge")]
    public async Task<ActionResult<ApiResult<LinklyCloudBackendSessionResponse>>> AcknowledgeCloudBackendTransaction(
        string sessionId,
        [FromQuery] string? environment,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LinklyCloudBackendAcknowledgeRequest? request,
        CancellationToken cancellationToken)
    {
        var scope = GetAuthenticatedDeviceScope<LinklyCloudBackendSessionResponse>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        try
        {
            var response = await linklyCloudBackendAsyncService.AcknowledgeSessionAsync(
                scope.StoreCode!,
                scope.DeviceCode!,
                request?.Environment ?? environment ?? string.Empty,
                sessionId,
                cancellationToken);
            return Ok(ApiResult<LinklyCloudBackendSessionResponse>.Ok(response));
        }
        catch (LinklyCloudBackendSessionNotFoundException)
        {
            return NotFound(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendNotFoundCode,
                "Linkly Cloud backend session was not found."));
        }
        catch (LinklyCloudBackendValidationException ex)
        {
            return BadRequest(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendInvalidCode,
                ex.Message));
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.DailyCloseSave)]
    [HttpPost("cloud-backend/settlements/{sessionId}/acknowledge")]
    public async Task<ActionResult<ApiResult<LinklyCloudBackendSessionResponse>>> AcknowledgeCloudBackendSettlement(
        string sessionId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LinklyCloudBackendAcknowledgeRequest? request,
        CancellationToken cancellationToken)
    {
        var scope = GetAuthenticatedDeviceScope<LinklyCloudBackendSessionResponse>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        try
        {
            var response = await linklyCloudBackendAsyncService.AcknowledgeSettlementSessionAsync(
                scope.StoreCode!,
                scope.DeviceCode!,
                request?.Environment ?? string.Empty,
                sessionId,
                cancellationToken);
            return Ok(ApiResult<LinklyCloudBackendSessionResponse>.Ok(response));
        }
        catch (LinklyCloudBackendSessionNotFoundException)
        {
            return NotFound(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendNotFoundCode,
                "Linkly Cloud backend settlement was not found."));
        }
        catch (LinklyCloudBackendValidationException ex)
        {
            return BadRequest(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendInvalidCode,
                ex.Message));
        }
    }

    [HttpPost("cloud-backend/transactions/{sessionId}/recover")]
    public async Task<ActionResult<ApiResult<LinklyCloudBackendSessionResponse>>> RecoverCloudBackendTransaction(
        string sessionId,
        [FromBody] LinklyCloudBackendRecoverRequest request,
        CancellationToken cancellationToken)
    {
        var scope = GetAuthenticatedDeviceScope<LinklyCloudBackendSessionResponse>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        try
        {
            var response = await linklyCloudBackendAsyncService.RecoverAsync(
                scope.StoreCode!,
                scope.DeviceCode!,
                sessionId,
                request,
                cancellationToken);
            return Ok(ApiResult<LinklyCloudBackendSessionResponse>.Ok(
                LinklyCardTransactionSanitizer.Attach(response)));
        }
        catch (LinklyCloudBackendSessionNotFoundException)
        {
            return NotFound(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendNotFoundCode,
                "Linkly Cloud backend session was not found."));
        }
        catch (LinklyCloudBackendValidationException ex)
        {
            return BadRequest(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendInvalidCode,
                ex.Message));
        }
    }

    [HttpPost("cloud-backend/transactions/{sessionId}/sendkey")]
    public async Task<ActionResult<ApiResult<LinklyCloudBackendSessionResponse>>> SendCloudBackendKey(
        string sessionId,
        [FromBody] LinklyCloudBackendSendKeyRequest request,
        CancellationToken cancellationToken)
    {
        var scope = GetAuthenticatedDeviceScope<LinklyCloudBackendSessionResponse>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        try
        {
            var response = await linklyCloudBackendAsyncService.SendKeyAsync(
                scope.StoreCode!,
                scope.DeviceCode!,
                sessionId,
                request,
                cancellationToken);
            if (response.LastHttpStatus == StatusCodes.Status400BadRequest)
            {
                return BadRequest(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                    CloudBackendInvalidCode,
                    "Linkly Cloud rejected the terminal action. Continue waiting for the transaction result."));
            }

            return Ok(ApiResult<LinklyCloudBackendSessionResponse>.Ok(response));
        }
        catch (LinklyCloudBackendSessionNotFoundException)
        {
            return NotFound(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendNotFoundCode,
                "Linkly Cloud backend session was not found."));
        }
        catch (LinklyCloudBackendValidationException ex)
        {
            return BadRequest(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendInvalidCode,
                ex.Message));
        }
    }

    [HttpPost("cloud-backend/transactions/{sessionId}/receipt/printed")]
    public async Task<ActionResult<ApiResult<LinklyCloudBackendSessionResponse>>> MarkCloudBackendReceiptPrinted(
        string sessionId,
        [FromBody] LinklyCloudBackendMarkReceiptPrintedRequest request,
        CancellationToken cancellationToken)
    {
        var scope = GetAuthenticatedDeviceScope<LinklyCloudBackendSessionResponse>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        try
        {
            var response = await linklyCloudBackendAsyncService.MarkReceiptPrintedAsync(
                scope.StoreCode!,
                scope.DeviceCode!,
                sessionId,
                request,
                cancellationToken);
            return Ok(ApiResult<LinklyCloudBackendSessionResponse>.Ok(response));
        }
        catch (LinklyCloudBackendSessionNotFoundException)
        {
            return NotFound(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendNotFoundCode,
                "Linkly Cloud backend session was not found."));
        }
        catch (LinklyCloudBackendValidationException ex)
        {
            return BadRequest(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendInvalidCode,
                ex.Message));
        }
    }

    [Authorize(Policy = CashierAuthorizationPolicies.DailyClosePrint)]
    [HttpPost("cloud-backend/settlements/{sessionId}/receipt/printed")]
    public async Task<ActionResult<ApiResult<LinklyCloudBackendSessionResponse>>> MarkCloudBackendSettlementReceiptPrinted(
        string sessionId,
        [FromBody] LinklyCloudBackendMarkReceiptPrintedRequest request,
        CancellationToken cancellationToken)
    {
        var scope = GetAuthenticatedDeviceScope<LinklyCloudBackendSessionResponse>();
        if (scope.Result is not null)
        {
            return scope.Result;
        }

        try
        {
            var response = await linklyCloudBackendAsyncService.MarkSettlementReceiptPrintedAsync(
                scope.StoreCode!,
                scope.DeviceCode!,
                sessionId,
                request,
                cancellationToken);
            return Ok(ApiResult<LinklyCloudBackendSessionResponse>.Ok(response));
        }
        catch (LinklyCloudBackendSessionNotFoundException)
        {
            return NotFound(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendNotFoundCode,
                "Linkly Cloud backend settlement was not found."));
        }
        catch (LinklyCloudBackendValidationException ex)
        {
            return BadRequest(ApiResult<LinklyCloudBackendSessionResponse>.Fail(
                CloudBackendInvalidCode,
                ex.Message));
        }
    }

    [AllowAnonymous]
    [HttpPost("cloud-notifications/{environment}/{sessionId}/{type}")]
    public async Task<ActionResult<ApiResult<string>>> ReceiveCloudBackendNotification(
        string environment,
        string sessionId,
        string type,
        [FromBody] JsonElement payload,
        CancellationToken cancellationToken)
    {
        LogNotification(
            "request",
            "request",
            environment,
            sessionId,
            type,
            statusCode: null,
            request: null,
            response: null,
            callback: DescribeCallbackPayload(payload, includeCardNumber: false));
        try
        {
            await linklyCloudBackendAsyncService.ReceiveNotificationAsync(
                environment,
                sessionId,
                type,
                Request.Headers.Authorization.ToString(),
                payload,
                cancellationToken);
            var accepted = ApiResult<string>.Ok("accepted");
            LogNotification(
                "response",
                "response",
                environment,
                sessionId,
                type,
                StatusCodes.Status200OK,
                request: null,
                response: accepted,
                callback: DescribeCallbackPayload(payload, includeCardNumber: true));
            return Ok(accepted);
        }
        catch (LinklyCloudBackendNotificationUnauthorizedException)
        {
            var unauthorized = ApiResult<string>.Fail(
                "LINKLY_CLOUD_BACKEND_NOTIFICATION_UNAUTHORIZED",
                "Linkly Cloud notification authorization is invalid.");
            LogNotification(
                "response",
                "response",
                environment,
                sessionId,
                type,
                StatusCodes.Status401Unauthorized,
                request: null,
                response: unauthorized);
            return Unauthorized(unauthorized);
        }
        catch (LinklyCloudBackendValidationException ex)
        {
            var badRequest = ApiResult<string>.Fail(
                CloudBackendInvalidCode,
                ex.Message);
            LogNotification(
                "response",
                "response",
                environment,
                sessionId,
                type,
                StatusCodes.Status400BadRequest,
                request: null,
                response: badRequest);
            return BadRequest(badRequest);
        }
    }

    private (string? StoreCode, string? DeviceCode, ActionResult<ApiResult<T>>? Result) GetAuthenticatedDeviceScope<T>()
    {
        var storeCode = User.FindFirstValue(DeviceAuthConstants.StoreCodeClaim);
        var deviceCode = User.FindFirstValue(DeviceAuthConstants.DeviceCodeClaim);
        if (string.IsNullOrWhiteSpace(storeCode) || string.IsNullOrWhiteSpace(deviceCode))
        {
            Log("cloud backend request rejected reason=missing-device-claims");
            return (null, null, DeviceAuthorizationExtensions.DeviceScopeForbidden<T>(
                "Device store and terminal scope are unavailable."));
        }

        // CloudBackendAsync 所有设备 scope 只信任认证 claim，忽略 query/body 中任何门店或设备字段。
        return (storeCode.Trim(), deviceCode.Trim(), null);
    }

    private string? GetUpdatedByClaim()
    {
        var deviceCode = User.FindFirstValue(DeviceAuthConstants.DeviceCodeClaim);
        return string.IsNullOrWhiteSpace(deviceCode) ? null : $"device:{deviceCode.Trim()}";
    }

    private void Log(string message)
    {
        LogJson(BuildJsonLog(
            source: "api-linkly-controller",
            operation: InferOperation(message),
            phase: InferPhase(message),
            direction: null,
            environment: null,
            sessionId: null,
            httpStatus: null,
            request: null,
            response: null,
            details: new
            {
                message
            }));
    }

    private void LogNotification(
        string phase,
        string direction,
        string environment,
        string sessionId,
        string type,
        int? statusCode,
        object? request,
        object? response,
        object? callback = null)
    {
        LogJson(BuildJsonLog(
            source: "api-linkly-controller",
            operation: $"notification-{type}",
            phase: phase,
            direction: direction,
            environment: environment,
            sessionId: sessionId,
            httpStatus: statusCode,
            request: request,
            response: response,
            details: new
            {
                type,
                timestamp = DateTimeOffset.Now.ToString("O"),
                callback
            }));
    }

    private void LogJson(string json)
    {
        Console.WriteLine($"[HBPOS][Api][LinklyCloud] {DateTimeOffset.Now:O} {json}");
        logger?.LogInformation("[HBPOS][Api][LinklyCloud] {Message}", json);
    }

    private static object DescribeCallbackPayload(JsonElement payload, bool includeCardNumber)
    {
        // 回调 bearer 属于认证材料，日志只保留存在性和 scheme，禁止记录原文。
        return new
        {
            hasPayload = payload.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null,
            payloadKind = payload.ValueKind.ToString(),
            cardNumber = includeCardNumber
                ? LinklyReceiptTextSanitizer.FindSanitizedCardNumber(payload)
                : null
        };
    }

    private static string BuildJsonLog(
        string source,
        string operation,
        string phase,
        string? direction,
        string? environment,
        string? sessionId,
        int? httpStatus,
        object? request,
        object? response,
        object? details)
    {
        return JsonSerializer.Serialize(new
        {
            source,
            operation,
            phase,
            direction,
            environment,
            sessionId,
            httpStatus,
            success = httpStatus.HasValue ? httpStatus.Value is >= 200 and < 300 : (bool?)null,
            reason = (string?)null,
            elapsedMs = (long?)null,
            request,
            response,
            details
        });
    }

    private static string InferOperation(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "linkly";
        }

        var trimmed = message.Trim();
        var index = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return index <= 0 ? trimmed : trimmed[..index];
    }

    private static string InferPhase(string message)
    {
        if (message.Contains("rejected", StringComparison.OrdinalIgnoreCase))
        {
            return "rejected";
        }

        if (message.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            return "failed";
        }

        if (message.Contains("response", StringComparison.OrdinalIgnoreCase))
        {
            return "response";
        }

        if (message.Contains("request", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("upsert", StringComparison.OrdinalIgnoreCase))
        {
            return "request";
        }

        return "event";
    }

    private static string LogValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<null>" : value.Trim();
    }
}

/// <summary>
/// 仅统一 Linkly 配对端点的 JSON/model-binding 400；顺序早于 ApiController 的
/// ModelStateInvalidFilter，避免空 body 或畸形 JSON 落成 ProblemDetails。
/// </summary>
internal sealed class LinklyCloudPairRequestModelStateAttribute : ActionFilterAttribute
{
    public LinklyCloudPairRequestModelStateAttribute()
    {
        Order = -3_000;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid)
        {
            return;
        }

        context.Result = new BadRequestObjectResult(
            ApiResult<LinklyCloudBackendTerminalCredentialResponse>.Fail(
                "LINKLY_CLOUD_BACKEND_PAIR_REQUEST_INVALID",
                "Linkly Cloud pairing request is invalid."));
    }
}

internal static class LinklyCardTransactionSanitizer
{
    private const int MaxTxnRefLength = 64;
    private const int MaxRfnLength = 128;
    private const int MaxAuthCodeLength = 32;
    private const int MaxCardTypeLength = 48;
    private const int MaxMerchantIdLength = 64;
    private const int MaxResponseCodeLength = 16;
    private const int MaxResponseTextLength = 160;
    private const int MaxStanLength = 32;
    private const long MaxAmountCents = 999_999_999;
    private const string IdentifierPunctuation = "-_./:";
    private const string TextPunctuation = " -_./:,;()[]'&+#%!?=@";

    internal static LinklyCloudBackendSessionResponse Attach(
        LinklyCloudBackendSessionResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response with { CardTransaction = Sanitize(response) };
    }

    internal static LinklyCloudBackendCardTransactionDto? Sanitize(
        LinklyCloudBackendSessionResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        foreach (var notification in (response.Notifications ?? []).Reverse())
        {
            if (!string.Equals(notification.Type, "transaction", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(notification.PayloadJson))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(notification.PayloadJson);
                var root = document.RootElement;
                var providerResponse = ReadResponse(root);
                if (!MatchesProtectedResult(response, providerResponse))
                {
                    continue;
                }

                // 安全边界：只从固定白名单字段构造新合同，绝不复制原始 notification、receipt 或 token。
                return new LinklyCloudBackendCardTransactionDto(
                    SanitizeIdentifier(
                        ReadScalar(providerResponse, "TxnRef") ?? response.TxnRef,
                        MaxTxnRefLength),
                    SanitizeIdentifier(ReadRfn(root, providerResponse), MaxRfnLength),
                    SanitizeIdentifier(ReadScalar(providerResponse, "AuthCode"), MaxAuthCodeLength),
                    SanitizeText(ReadScalar(providerResponse, "CardType"), MaxCardTypeLength),
                    MaskPan(ReadScalar(providerResponse, "Pan")),
                    SanitizeIdentifier(ReadScalar(providerResponse, "Caid"), MaxMerchantIdLength),
                    SanitizeIdentifier(
                        response.ResponseCode ?? ReadScalar(providerResponse, "ResponseCode"),
                        MaxResponseCodeLength),
                    SanitizeText(
                        response.ResponseText ?? ReadScalar(providerResponse, "ResponseText"),
                        MaxResponseTextLength),
                    SanitizeIdentifier(ReadScalar(providerResponse, "Stan"), MaxStanLength),
                    ReadBankDateTime(providerResponse),
                    ReadAbsoluteAmountCents(providerResponse));
            }
            catch (JsonException)
            {
                // 损坏的历史 notification 不得阻止状态/恢复接口返回；证据保持 null。
            }
        }

        return null;
    }

    private static bool MatchesProtectedResult(
        LinklyCloudBackendSessionResponse response,
        JsonElement providerResponse)
    {
        var protectedCode = NormalizeForComparison(response.ResponseCode);
        if (protectedCode is not null &&
            !string.Equals(
                protectedCode,
                NormalizeForComparison(ReadScalar(providerResponse, "ResponseCode")),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var protectedText = NormalizeForComparison(response.ResponseText);
        return protectedText is null ||
            string.Equals(
                protectedText,
                NormalizeForComparison(ReadScalar(providerResponse, "ResponseText")),
                StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadRfn(JsonElement root, JsonElement providerResponse)
    {
        var purchaseAnalysisData = ReadValue(providerResponse, "PurchaseAnalysisData");
        return ReadScalar(purchaseAnalysisData, "RFN") ??
            ReadScalar(providerResponse, "RFN") ??
            ReadScalar(root, "RFN");
    }

    private static DateTimeOffset? ReadBankDateTime(JsonElement providerResponse)
    {
        var value = ReadScalar(providerResponse, "BankDateTime");
        if (value is null || value.Length > 64 || !HasExplicitOffset(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var parsed)
                ? parsed
                : null;
    }

    private static bool HasExplicitOffset(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.EndsWith('Z') || trimmed.EndsWith('z'))
        {
            return true;
        }

        var timeSeparator = Math.Max(
            trimmed.IndexOf('T', StringComparison.Ordinal),
            trimmed.IndexOf(' ', StringComparison.Ordinal));
        return timeSeparator >= 0 &&
            (trimmed.IndexOf('+', timeSeparator) >= 0 ||
                trimmed.IndexOf('-', timeSeparator) >= 0);
    }

    private static long? ReadAbsoluteAmountCents(JsonElement providerResponse)
    {
        var amount = ReadInt64(providerResponse, "AmtPurchase");
        if (amount is null || amount == long.MinValue)
        {
            return null;
        }

        var absolute = Math.Abs(amount.Value);
        return absolute <= MaxAmountCents ? absolute : null;
    }

    private static long? ReadInt64(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var numeric))
        {
            return numeric;
        }

        return value.ValueKind == JsonValueKind.String &&
            long.TryParse(
                value.GetString(),
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out numeric)
                    ? numeric
                    : null;
    }

    private static string? MaskPan(string? pan)
    {
        var value = NormalizeForComparison(pan);
        if (value is null)
        {
            return null;
        }

        var compact = new string(value
            .Where(character => character is not ' ' and not '-')
            .ToArray());
        if (compact.Length is < 8 or > 19 ||
            compact.Any(character =>
                !char.IsAsciiDigit(character) &&
                character is not '*' &&
                character is not 'x' &&
                character is not 'X') ||
            compact[^4..].Any(character => !char.IsAsciiDigit(character)))
        {
            return null;
        }

        var hasMask = compact.Any(character => character is '*' or 'x' or 'X');
        if (!hasMask && compact.Length < 12)
        {
            return null;
        }

        var visiblePrefixLength = compact.Length >= 12 ? Math.Min(6, compact.Length - 4) : 0;
        var suffixStart = compact.Length - 4;
        var masked = compact
            .Select((character, index) =>
                char.IsAsciiDigit(character) &&
                (index < visiblePrefixLength || index >= suffixStart)
                    ? character
                    : '*')
            .ToArray();
        return new string(masked);
    }

    private static string? SanitizeIdentifier(string? value, int maxLength)
    {
        var normalized = NormalizeForComparison(value);
        if (normalized is null ||
            normalized.Length > maxLength ||
            normalized.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                !IdentifierPunctuation.Contains(character)))
        {
            return null;
        }

        return normalized;
    }

    private static string? SanitizeText(string? value, int maxLength)
    {
        var normalized = NormalizeForComparison(value);
        if (normalized is null ||
            normalized.Length > maxLength ||
            normalized.Any(character =>
                !char.IsLetterOrDigit(character) &&
                !TextPunctuation.Contains(character)))
        {
            return null;
        }

        return normalized;
    }

    private static string? NormalizeForComparison(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static JsonElement ReadResponse(JsonElement root)
    {
        return TryGetProperty(root, "Response", out var response) &&
            response.ValueKind == JsonValueKind.Object
                ? response
                : root;
    }

    private static JsonElement ReadValue(JsonElement element, string propertyName)
    {
        return TryGetProperty(element, propertyName, out var value) ? value : default;
    }

    private static string? ReadScalar(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => NormalizeForComparison(value.GetString()),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
