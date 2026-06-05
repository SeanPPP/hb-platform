using BlazorApp.Api.Services.Logging;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/system/logs")]
    public class SystemLogsController : ControllerBase
    {
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
        public async Task<ActionResult<ApiResponse<ApplicationLogIngestResultDto>>> Ingest(
            [FromBody] ApplicationLogIngestRequestDto request
        )
        {
            var projectCode = Request.Headers["X-Log-Project"].FirstOrDefault();
            var apiKey = Request.Headers["X-Log-Key"].FirstOrDefault();
            var project = await _service.AuthenticateProjectAsync(projectCode, apiKey);
            if (project == null)
                return Unauthorized(ApiResponse<object>.Error("日志项目鉴权失败", "LOG_PROJECT_UNAUTHORIZED"));

            if (!_rateLimiter.TryConsume(project.ProjectCode, request.Logs.Count, out var rateLimitMessage))
                return StatusCode(
                    429,
                    ApiResponse<object>.Error(rateLimitMessage, "LOG_INGEST_RATE_LIMITED")
                );

            try
            {
                var result = await _service.IngestAsync(project.ProjectCode, request);
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
}
