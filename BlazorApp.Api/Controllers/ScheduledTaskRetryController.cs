using BlazorApp.Api.Services.Background;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ScheduledTaskRetryController : ControllerBase
    {
        private readonly ScheduledTaskLogService _taskLogService;
        private readonly ScheduledTaskRetryService _retryService;
        private readonly ILogger<ScheduledTaskRetryController> _logger;

        public ScheduledTaskRetryController(
            ScheduledTaskLogService taskLogService,
            ScheduledTaskRetryService retryService,
            ILogger<ScheduledTaskRetryController> logger
        )
        {
            _taskLogService = taskLogService;
            _retryService = retryService;
            _logger = logger;
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetTaskList([FromQuery] ScheduledTaskQueryDto query)
        {
            try
            {
                var result = await _taskLogService.GetPagedTasksAsync(
                    query.TaskType,
                    query.Status,
                    query.TriggeredBy,
                    query.StartDate,
                    query.EndDate,
                    query.PageNumber,
                    query.PageSize,
                    query.SortBy,
                    query.SortDirection
                );

                return Ok(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取任务列表失败");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("failed")]
        public async Task<IActionResult> GetFailedTasks(
            [FromQuery] string? taskType = null,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] int pageSize = 20,
            [FromQuery] int pageNumber = 1
        )
        {
            try
            {
                var tasks = await _taskLogService.GetFailedTasksAsync(
                    taskType,
                    startDate,
                    endDate,
                    pageSize,
                    pageNumber
                );

                return Ok(
                    new
                    {
                        success = true,
                        data = tasks,
                        message = $"获取到 {tasks.Count} 个失败任务",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取失败任务列表失败");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetTask(Guid id)
        {
            try
            {
                var task = await _taskLogService.GetTaskAsync(id);
                if (task == null)
                {
                    return NotFound(new { success = false, message = "任务不存在" });
                }

                return Ok(new { success = true, data = task });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取任务详情失败: {TaskId}", id);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("{id}")]
        public async Task<IActionResult> RetryTask(Guid id)
        {
            try
            {
                var success = await _retryService.RetryTaskAsync(id);
                if (success)
                {
                    return Ok(new { success = true, message = "任务重试已启动" });
                }
                else
                {
                    return BadRequest(
                        new { success = false, message = "任务重试失败，请检查任务状态" }
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重试任务失败: {TaskId}", id);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("retry-all")]
        public async Task<IActionResult> RetryAllFailedTasks(
            [FromQuery] string? taskType = null,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null
        )
        {
            try
            {
                var successCount = await _retryService.BatchRetryFailedTasksAsync(
                    taskType,
                    startDate,
                    endDate
                );

                return Ok(new { success = true, message = $"成功启动 {successCount} 个任务重试" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量重试任务失败");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost("retry-by-type")]
        public async Task<IActionResult> RetryFailedTasksByType(
            [FromBody] RetryByTypeRequest request
        )
        {
            try
            {
                var successCount = await _retryService.BatchRetryFailedTasksAsync(
                    request.TaskType,
                    request.StartDate,
                    request.EndDate
                );

                return Ok(new { success = true, message = $"成功启动 {successCount} 个任务重试" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "按类型重试任务失败");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("recent")]
        public async Task<IActionResult> GetRecentTasks(
            [FromQuery] int count = 50,
            [FromQuery] string? taskType = null
        )
        {
            try
            {
                var tasks = await _taskLogService.GetRecentTasksAsync(count, taskType);

                return Ok(
                    new
                    {
                        success = true,
                        data = tasks,
                        message = $"获取到 {tasks.Count} 个任务",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取最近任务列表失败");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("statistics")]
        public async Task<IActionResult> GetTaskStatistics([FromQuery] DateTime? date = null)
        {
            try
            {
                var statistics = await _taskLogService.GetTaskStatisticsAsync(date);

                return Ok(new { success = true, data = statistics });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取任务统计失败");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("tasks-by-date")]
        public async Task<IActionResult> GetTasksByDateRange(
            [FromQuery] DateTime startDate,
            [FromQuery] DateTime endDate,
            [FromQuery] string? taskType = null
        )
        {
            try
            {
                var tasks = await _taskLogService.GetTasksByDateRangeAsync(
                    startDate,
                    endDate,
                    taskType
                );

                return Ok(
                    new
                    {
                        success = true,
                        data = tasks,
                        message = $"获取到 {tasks.Count} 个任务",
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "按日期范围获取任务失败");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTask(Guid id)
        {
            try
            {
                var success = await _taskLogService.DeleteTaskAsync(id);
                if (success)
                {
                    return Ok(new { success = true, message = "任务记录已删除" });
                }
                else
                {
                    return NotFound(new { success = false, message = "任务不存在" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除任务记录失败: {TaskId}", id);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpDelete("old-tasks")]
        public async Task<IActionResult> DeleteOldTasks([FromQuery] DateTime beforeDate)
        {
            try
            {
                var count = await _taskLogService.DeleteOldTasksAsync(beforeDate);

                return Ok(new { success = true, message = $"已删除 {count} 条旧任务记录" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量删除旧任务失败");
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }

    public class RetryByTypeRequest
    {
        public string? TaskType { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    public class ScheduledTaskQueryDto
    {
        public string? TaskType { get; set; }
        public string? Status { get; set; }
        public string? TriggeredBy { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public string? SortBy { get; set; }
        public string? SortDirection { get; set; }
    }
}
