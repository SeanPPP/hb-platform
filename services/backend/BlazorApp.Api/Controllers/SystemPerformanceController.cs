using System.Security.Claims;
using System.Text.Json;
using BlazorApp.Api.Authentication;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Logging;
using BlazorApp.Api.Services.Performance;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers;

[ApiController]
[Route("api/system/performance")]
public sealed class SystemPerformanceController : ControllerBase
{
    internal const int MaxIngestRequestBodyBytes = 256 * 1024;

    private readonly PerformanceMetricService _performanceService;
    private readonly ApplicationLogService _applicationLogService;
    private readonly PerformanceClientIngestRateLimiter _clientRateLimiter;
    private readonly IClientIpResolver _clientIpResolver;
    private readonly IDeviceRegistrationService _deviceRegistrationService;
    private readonly IAuthorizationService _authorizationService;

    public SystemPerformanceController(
        PerformanceMetricService performanceService,
        ApplicationLogService applicationLogService,
        PerformanceClientIngestRateLimiter clientRateLimiter,
        IClientIpResolver clientIpResolver,
        IDeviceRegistrationService deviceRegistrationService,
        IAuthorizationService authorizationService
    )
    {
        _performanceService = performanceService;
        _applicationLogService = applicationLogService;
        _clientRateLimiter = clientRateLimiter;
        _clientIpResolver = clientIpResolver;
        _deviceRegistrationService = deviceRegistrationService;
        _authorizationService = authorizationService;
    }

    [HttpPost("client-batches")]
    [AllowAnonymous]
    [RequestSizeLimit(MaxIngestRequestBodyBytes)]
    public async Task<ActionResult<ApiResponse<PerformanceMetricIngestResultDto>>> ClientBatches(
        [FromBody] PerformanceMetricBatchV1Dto? request
    )
    {
        var projectCode = Request.Headers["X-Log-Project"].FirstOrDefault();
        var apiKey = Request.Headers["X-Log-Key"].FirstOrDefault();
        var project = await _applicationLogService.AuthenticateProjectAsync(projectCode, apiKey);
        if (project == null)
        {
            return Unauthorized(
                ApiResponse<object>.Error(
                    "性能指标项目鉴权失败",
                    "PERFORMANCE_PROJECT_UNAUTHORIZED"
                )
            );
        }

        var source = await ResolveClientSourceAsync(project.ProjectCode);
        var clientIdentity = source.Subject
            ?? $"ip:{_clientIpResolver.Resolve(HttpContext)}";
        var serializedBytes = JsonSerializer.SerializeToUtf8Bytes(request).Length;
        var rateLimit = await _clientRateLimiter.TryConsumeAsync(
            $"{project.ProjectCode}:{source.RateLimitNamespace}",
            clientIdentity,
            request?.Events?.Count ?? 0,
            serializedBytes,
            DateTime.UtcNow,
            HttpContext.RequestAborted
        );
        if (!rateLimit.Allowed)
        {
            Response.Headers.RetryAfter = rateLimit.RetryAfterSeconds.ToString();
            return StatusCode(
                StatusCodes.Status429TooManyRequests,
                ApiResponse<PerformanceMetricIngestResultDto>.Error(
                    "性能指标上报超过共享写入预算，请稍后重试",
                    "PERFORMANCE_METRIC_RATE_LIMITED"
                )
            );
        }

        return ToActionResult(
            await _performanceService.IngestAsync(
                project.ProjectCode,
                "client",
                request,
                DateTime.UtcNow,
                source.SourceType
            )
        );
    }

    private async Task<PerformanceClientSource> ResolveClientSourceAsync(string projectCode)
    {
        if (string.Equals(projectCode, "hbweb_rv", StringComparison.Ordinal))
        {
            var subject = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub")
                ?? User.FindFirstValue("userId")
                ?? User.FindFirstValue(ClaimTypes.Name);
            var hasSignedManageClaim = User.HasClaim(
                    "permission",
                    Permissions.System.ManagePerformanceBaseline
                )
                || Permissions.SuperAdminRoleNames.Any(User.IsInRole);
            var canManageBaseline = User.Identity?.IsAuthenticated == true
                && !string.IsNullOrWhiteSpace(subject)
                && hasSignedManageClaim
                && (
                    await _authorizationService.AuthorizeAsync(
                        User,
                        null,
                        Permissions.System.ManagePerformanceBaseline
                    )
                ).Succeeded;
            if (canManageBaseline)
            {
                // 浏览器项目键会进入公开前端包；只有已具备冻结权限的主体才可贡献正式基线。
                return new PerformanceClientSource(
                    "web-baseline-manager",
                    "trusted",
                    $"web:{subject!.Trim()}"
                );
            }
            return PerformanceClientSource.Public;
        }

        if (
            string.Equals(projectCode, "hbpos_ipad", StringComparison.Ordinal)
            || string.Equals(projectCode, "hbpos_handheld", StringComparison.Ordinal)
        )
        {
            var hardwareId = Request.Headers["X-HBPOS-Hardware-Id"].FirstOrDefault()?.Trim();
            var authorization = Request.Headers.Authorization.FirstOrDefault();
            var authCode = authorization?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                == true
                ? authorization["Bearer ".Length..].Trim()
                : null;
            if (
                !string.IsNullOrWhiteSpace(hardwareId)
                && !string.IsNullOrWhiteSpace(authCode)
                && await _deviceRegistrationService.ValidateDeviceAuthCodeAsync(
                    hardwareId,
                    authCode
                )
            )
            {
                // 设备授权码只用于服务端验证；限流键随后在数据库层哈希，原始身份不落库。
                return new PerformanceClientSource(
                    "pos-device-authenticated",
                    "trusted",
                    $"pos:{hardwareId}"
                );
            }
        }

        return PerformanceClientSource.Public;
    }

    private sealed record PerformanceClientSource(
        string SourceType,
        string RateLimitNamespace,
        string? Subject
    )
    {
        public static PerformanceClientSource Public { get; } = new(
            "client-public",
            "public",
            null
        );
    }

    [HttpPost("automation-batches")]
    [Authorize(
        AuthenticationSchemes = ServiceApiTokenAuthenticationDefaults.AuthenticationScheme,
        Policy = ServiceApiScopes.WritePerformanceMetrics
    )]
    [RequestSizeLimit(MaxIngestRequestBodyBytes)]
    public async Task<ActionResult<ApiResponse<PerformanceMetricIngestResultDto>>> AutomationBatches(
        [FromBody] PerformanceMetricBatchV1Dto? request
    ) =>
        ToActionResult(
            await _performanceService.IngestAsync(
                "quality-ci",
                "ci",
                request,
                DateTime.UtcNow
            )
        );

    [HttpPost("release-events")]
    [Authorize(
        AuthenticationSchemes = ServiceApiTokenAuthenticationDefaults.AuthenticationScheme,
        Policy = ServiceApiScopes.WriteReleaseEvents
    )]
    [RequestSizeLimit(MaxIngestRequestBodyBytes)]
    public async Task<ActionResult<ApiResponse<PerformanceReleaseEventRequestDto>>> ReleaseEvents(
        [FromBody] PerformanceReleaseEventRequestDto request
    ) =>
        ToActionResult(
            await _performanceService.RecordReleaseEventAsync(request, DateTime.UtcNow)
        );

    [HttpGet("overview")]
    [Authorize(Policy = Permissions.System.ViewPerformanceBaseline)]
    public async Task<ActionResult<ApiResponse<PerformanceOverviewDto>>> Overview(
        [FromQuery] PerformanceOverviewQueryDto query
    )
    {
        try
        {
            return Ok(
                ApiResponse<PerformanceOverviewDto>.OK(
                    await _performanceService.GetOverviewAsync(query, DateTime.UtcNow),
                    "性能概览查询成功"
                )
            );
        }
        catch (PerformanceOverviewQueryException ex)
        {
            return BadRequest(ApiResponse<PerformanceOverviewDto>.Error(ex.Message, ex.ErrorCode));
        }
    }

    [HttpGet("series")]
    [Authorize(Policy = Permissions.System.ViewPerformanceBaseline)]
    public async Task<ActionResult<ApiResponse<PerformanceSeriesDto>>> Series(
        [FromQuery] PerformanceOverviewQueryDto query
    )
    {
        try
        {
            return Ok(
                ApiResponse<PerformanceSeriesDto>.OK(
                    await _performanceService.GetSeriesAsync(query, DateTime.UtcNow),
                    "性能序列查询成功"
                )
            );
        }
        catch (PerformanceSeriesQueryException ex)
        {
            return BadRequest(ApiResponse<PerformanceSeriesDto>.Error(ex.Message, ex.ErrorCode));
        }
    }

    [HttpGet("slow-sql")]
    [Authorize(Policy = Permissions.System.ViewPerformanceBaseline)]
    public async Task<ActionResult<ApiResponse<List<PerformanceSlowSqlDto>>>> SlowSql(
        [FromQuery] PerformanceSlowSqlQueryDto query
    ) =>
        Ok(
            ApiResponse<List<PerformanceSlowSqlDto>>.OK(
                await _performanceService.GetSlowSqlAsync(query, DateTime.UtcNow),
                "慢 SQL 查询成功"
            )
        );

    [HttpGet("runs")]
    [Authorize(Policy = Permissions.System.ViewPerformanceBaseline)]
    public async Task<ActionResult<ApiResponse<List<PerformanceOperationalRunDto>>>> Runs(
        [FromQuery] PerformanceOverviewQueryDto query
    ) =>
        Ok(
            ApiResponse<List<PerformanceOperationalRunDto>>.OK(
                await _performanceService.GetRunsAsync(query, DateTime.UtcNow),
                "运行记录查询成功"
            )
        );

    [HttpGet("baseline")]
    [Authorize(Policy = Permissions.System.ViewPerformanceBaseline)]
    public async Task<ActionResult<ApiResponse<PerformanceBaselineDto>>> Baseline(
        [FromQuery] string environment = "Production"
    ) =>
        Ok(
            ApiResponse<PerformanceBaselineDto>.OK(
                await _performanceService.GetBaselineAsync(environment),
                "性能基线查询成功"
            )
        );

    [HttpPost("baseline/freeze")]
    [Authorize(Policy = Permissions.System.ManagePerformanceBaseline)]
    public async Task<ActionResult<ApiResponse<PerformanceBaselineStatusDto>>> FreezeBaseline(
        [FromBody] PerformanceBaselineFreezeRequestDto request
    )
    {
        var actor =
            User.FindFirstValue(ClaimTypes.Name)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? "System";
        return ToActionResult(
            await _performanceService.FreezeBaselineAsync(
                request.Environment,
                actor,
                DateTime.UtcNow
            )
        );
    }

    private ActionResult<ApiResponse<T>> ToActionResult<T>(ApiResponse<T> response)
    {
        if (response.Success)
        {
            return Ok(response);
        }
        return string.Equals(
            response.ErrorCode,
            "PERFORMANCE_RELEASE_EVENT_CONFLICT",
            StringComparison.Ordinal
        )
            ? Conflict(response)
            : BadRequest(response);
    }
}
