using BlazorApp.Api.Services.Logging;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/system/logs")]
    public class SystemLogsController : ControllerBase
    {
        internal const int MaxIngestRequestBodyBytes = 4 * 1024 * 1024;
        internal static readonly object AuthenticatedProjectContextKey = new();
        internal static readonly object RequestBudgetDebitedContextKey = new();
        private readonly ApplicationLogService _service;
        private readonly ApplicationLogRateLimiter _rateLimiter;
        private readonly ILogger<SystemLogsController> _logger;

        public SystemLogsController(
            ApplicationLogService service,
            ApplicationLogRateLimiter rateLimiter,
            ILogger<SystemLogsController> logger
        )
        {
            _service = service;
            _rateLimiter = rateLimiter;
            _logger = logger;
        }

        [HttpPost("ingest")]
        [AllowAnonymous]
        [RequestSizeLimit(MaxIngestRequestBodyBytes)]
        [TypeFilter(typeof(ApplicationLogIngestResourceFilter))]
        public async Task<ActionResult<ApiResponse<ApplicationLogIngestResultDto>>> Ingest(
            [FromBody] ApplicationLogIngestRequestDto? request
        )
        {
            var project = HttpContext.Items[AuthenticatedProjectContextKey]
                as ApplicationLoggingProjectOptions;
            if (project == null)
            {
                // 直接调用 action 的单元测试不会执行资源过滤器，保留等价的鉴权回退。
                var projectCode = Request.Headers["X-Log-Project"].FirstOrDefault();
                var apiKey = Request.Headers["X-Log-Key"].FirstOrDefault();
                project = await _service.AuthenticateProjectAsync(projectCode, apiKey);
            }
            if (project == null)
                return Unauthorized(ApiResponse<object>.Error("日志项目鉴权失败", "LOG_PROJECT_UNAUTHORIZED"));

            var actualPayloadBytes = Request.ContentLength is >= 0 and <= MaxIngestRequestBodyBytes
                ? Request.ContentLength.Value
                : (long?)null;
            if (!HttpContext.Items.ContainsKey(RequestBudgetDebitedContextKey))
            {
                long requestBudgetBytes;
                if (actualPayloadBytes.HasValue)
                    requestBudgetBytes = actualPayloadBytes.Value;
                else if (
                    !_rateLimiter.TryMeasureCanonicalRequestBytes(
                        request,
                        out requestBudgetBytes
                    )
                )
                    requestBudgetBytes = MaxIngestRequestBodyBytes;

                if (
                    !_rateLimiter.TryConsumeRequestBudget(
                        project.ProjectCode,
                        requestBudgetBytes,
                        out var requestRateLimitMessage
                    )
                )
                    return StatusCode(
                        429,
                        ApiResponse<object>.Error(
                            requestRateLimitMessage,
                            "LOG_INGEST_RATE_LIMITED"
                        )
                    );
            }

            // request/bytes 已扣除；再做无状态校验，非法请求不会消耗日志条数额度。
            if (
                !_rateLimiter.TryValidateIngestRequest(
                    request,
                    actualPayloadBytes,
                    out _,
                    out var validationMessage
                )
            )
                return BadRequest(ApiResponse<object>.Error(validationMessage, "LOG_INGEST_INVALID"));

            if (
                !_rateLimiter.TryConsumeLogBudget(
                    project.ProjectCode,
                    request!.Logs.Count,
                    out var rateLimitMessage
                )
            )
                return StatusCode(
                    429,
                    ApiResponse<object>.Error(rateLimitMessage, "LOG_INGEST_RATE_LIMITED")
                );

            try
            {
                // 外部请求声明的 ClientIp 不可信，统一使用服务端连接信息入库。
                var trustedClientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
                foreach (var log in request.Logs)
                {
                    // 即使连接地址不可用也必须清空客户端自报值，不能回退信任请求体。
                    if (log != null)
                        log.ClientIp = null;
                }
                var result = await _service.IngestAsync(
                    project.ProjectCode,
                    request,
                    trustedClientIp
                );
                return Ok(ApiResponse<ApplicationLogIngestResultDto>.OK(result, "日志写入成功"));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiResponse<object>.Error(ex.Message, "LOG_INGEST_INVALID"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "外部日志写入失败: {ProjectCode}", project.ProjectCode);
                return StatusCode(
                    500,
                    ApiResponse<object>.Error("日志写入失败", "LOG_INGEST_FAILED")
                );
            }
        }

        [HttpGet]
        [Authorize(Policy = Permissions.System.ViewLogs)]
        public async Task<ActionResult<ApiResponse<PagedResult<ApplicationLogDto>>>> Query(
            [FromQuery] ApplicationLogQueryDto query
        )
        {
            var result = await _service.QueryAsync(query);
            return Ok(ApiResponse<PagedResult<ApplicationLogDto>>.OK(result, "查询成功"));
        }

        [HttpGet("{id:guid}")]
        [Authorize(Policy = Permissions.System.ViewLogs)]
        public async Task<ActionResult<ApiResponse<ApplicationLogDto>>> Detail(Guid id)
        {
            var result = await _service.GetAsync(id);
            if (result == null)
                return NotFound(ApiResponse<object>.Error("日志不存在", "LOG_NOT_FOUND"));
            return Ok(ApiResponse<ApplicationLogDto>.OK(result, "查询成功"));
        }

        [HttpGet("summary")]
        [Authorize(Policy = Permissions.System.ViewLogs)]
        public async Task<ActionResult<ApiResponse<ApplicationLogSummaryDto>>> Summary(
            [FromQuery] ApplicationLogQueryDto query
        )
        {
            var result = await _service.GetSummaryAsync(query);
            return Ok(ApiResponse<ApplicationLogSummaryDto>.OK(result, "查询成功"));
        }
    }

    /// <summary>
    /// 在 MVC 模型绑定读取请求体前完成项目鉴权与 request/bytes 预算扣减。
    /// </summary>
    public sealed class ApplicationLogIngestResourceFilter : IAsyncResourceFilter
    {
        private readonly ApplicationLogService _service;
        private readonly ApplicationLogRateLimiter _rateLimiter;

        public ApplicationLogIngestResourceFilter(
            ApplicationLogService service,
            ApplicationLogRateLimiter rateLimiter
        )
        {
            _service = service;
            _rateLimiter = rateLimiter;
        }

        public async Task OnResourceExecutionAsync(
            ResourceExecutingContext context,
            ResourceExecutionDelegate next
        )
        {
            var request = context.HttpContext.Request;
            var projectCode = request.Headers["X-Log-Project"].FirstOrDefault();
            var apiKey = request.Headers["X-Log-Key"].FirstOrDefault();
            var project = await _service.AuthenticateProjectAsync(projectCode, apiKey);
            if (project == null)
            {
                context.Result = new UnauthorizedObjectResult(
                    ApiResponse<object>.Error(
                        "日志项目鉴权失败",
                        "LOG_PROJECT_UNAUTHORIZED"
                    )
                );
                return;
            }

            // 分块请求尚未读取，保守按入口最大体积扣费，避免畸形 JSON 获得免费解析预算。
            var payloadBytes =
                request.ContentLength is >= 0 and <= SystemLogsController.MaxIngestRequestBodyBytes
                    ? request.ContentLength.Value
                    : SystemLogsController.MaxIngestRequestBodyBytes;
            if (
                !_rateLimiter.TryConsumeRequestBudget(
                    project.ProjectCode,
                    payloadBytes,
                    out var rateLimitMessage
                )
            )
            {
                context.Result = new ObjectResult(
                    ApiResponse<object>.Error(
                        rateLimitMessage,
                        "LOG_INGEST_RATE_LIMITED"
                    )
                )
                {
                    StatusCode = StatusCodes.Status429TooManyRequests,
                };
                return;
            }

            context.HttpContext.Items[SystemLogsController.AuthenticatedProjectContextKey] = project;
            context.HttpContext.Items[SystemLogsController.RequestBudgetDebitedContextKey] = true;
            await next();
        }
    }
}
