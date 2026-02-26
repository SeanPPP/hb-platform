using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Background;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class StatisticsJobTriggerController : ControllerBase
    {
        private readonly SalesStatisticsJobService _statisticsJobService;
        private readonly ScheduledTaskLogService _taskLogService;
        private readonly ILogger<StatisticsJobTriggerController> _logger;

        public StatisticsJobTriggerController(
            SalesStatisticsJobService statisticsJobService,
            ScheduledTaskLogService taskLogService,
            ILogger<StatisticsJobTriggerController> logger
        )
        {
            _statisticsJobService = statisticsJobService;
            _taskLogService = taskLogService;
            _logger = logger;
        }

        [HttpPost("trigger-store")]
        public async Task<IActionResult> TriggerStoreStatistics(
            [FromBody] StoreJobTriggerRequest request
        )
        {
            Guid taskId = Guid.Empty;
            try
            {
                var taskLog = await _taskLogService.LogTaskStartAsync(
                    BlazorApp.Shared.Models.HBweb.TaskType.UpdateStoreStatistics,
                    new TaskParameters
                    {
                        Date = request.Date.ToString("yyyy-MM-dd"),
                        BranchCodes = request.BranchCodes,
                    },
                    TaskTrigger.Manual
                );
                taskId = taskLog.Id;

                _logger.LogInformation(
                    "分店统计任务已触发: Date={Date}, Branches={Branches}",
                    request.Date,
                    request.BranchCodes != null ? string.Join(", ", request.BranchCodes) : "All"
                );

                await _statisticsJobService.UpdateStoreStatistics(
                    request.Date,
                    request.BranchCodes
                );

                await _taskLogService.LogTaskSuccessAsync(taskId);

                _logger.LogInformation(
                    "分店统计任务执行完成: Date={Date}, Branches={Branches}",
                    request.Date,
                    request.BranchCodes != null ? string.Join(", ", request.BranchCodes) : "All"
                );

                return Ok(
                    new
                    {
                        success = true,
                        message = "分店统计任务执行完成",
                        date = request.Date,
                        branches = request.BranchCodes,
                        jobId = taskId, // 返回 JobId 供前端显示
                    }
                );
            }
            catch (Exception ex)
            {
                if (taskId != Guid.Empty)
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, ex.Message);
                }
                _logger.LogError(ex, "触发分店统计任务失败");
                return StatusCode(
                    500,
                    new { success = false, message = "触发任务失败: " + ex.Message }
                );
            }
        }

        [HttpPost("trigger-supplier")]
        public async Task<IActionResult> TriggerSupplierStatistics(
            [FromBody] SupplierJobTriggerRequest request
        )
        {
            Guid taskId = Guid.Empty;
            try
            {
                var taskLog = await _taskLogService.LogTaskStartAsync(
                    BlazorApp.Shared.Models.HBweb.TaskType.UpdateSupplierStatistics,
                    new TaskParameters
                    {
                        Date = request.Date.ToString("yyyy-MM-dd"),
                        SupplierCodes = request.SupplierCodes,
                    },
                    TaskTrigger.Manual
                );
                taskId = taskLog.Id;

                _logger.LogInformation(
                    "供应商统计任务已触发: Date={Date}, Suppliers={Suppliers}",
                    request.Date,
                    request.SupplierCodes != null ? string.Join(", ", request.SupplierCodes) : "All"
                );

                await _statisticsJobService.UpdateSupplierStatistics(
                    request.Date,
                    null,
                    request.SupplierCodes
                );

                await _taskLogService.LogTaskSuccessAsync(taskId);

                _logger.LogInformation(
                    "供应商统计任务执行完成: Date={Date}, Suppliers={Suppliers}",
                    request.Date,
                    request.SupplierCodes != null ? string.Join(", ", request.SupplierCodes) : "All"
                );

                return Ok(
                    new
                    {
                        success = true,
                        message = "供应商统计任务执行完成",
                        date = request.Date,
                        suppliers = request.SupplierCodes,
                        jobId = taskId,
                    }
                );
            }
            catch (Exception ex)
            {
                if (taskId != Guid.Empty)
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, ex.Message);
                }
                _logger.LogError(ex, "触发供应商统计任务失败");
                return StatusCode(
                    500,
                    new { success = false, message = "触发任务失败: " + ex.Message }
                );
            }
        }

        [HttpPost("trigger-daily")]
        public async Task<IActionResult> TriggerDailyStatistics(
            [FromBody] DailyJobTriggerRequest request
        )
        {
            Guid taskId = Guid.Empty;
            try
            {
                var dateStr = request.Date.HasValue
                    ? request.Date.Value.ToString("yyyy-MM-dd")
                    : null;

                var taskLog = await _taskLogService.LogTaskStartAsync(
                    BlazorApp.Shared.Models.HBweb.TaskType.UpdateDailyStatistics,
                    new TaskParameters { Date = dateStr },
                    TaskTrigger.Manual
                );
                taskId = taskLog.Id;

                _logger.LogInformation("每日统计任务已触发: Date={Date}", dateStr);

                await _statisticsJobService.UpdateDailyStatistics(dateStr);

                await _taskLogService.LogTaskSuccessAsync(taskId);

                _logger.LogInformation("每日统计任务执行完成: Date={Date}", dateStr);

                return Ok(
                    new
                    {
                        success = true,
                        message = "每日统计任务执行完成",
                        date = dateStr,
                        jobId = taskId,
                    }
                );
            }
            catch (Exception ex)
            {
                if (taskId != Guid.Empty)
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, ex.Message);
                }
                _logger.LogError(ex, "触发每日统计任务失败");
                return StatusCode(
                    500,
                    new { success = false, message = "触发任务失败: " + ex.Message }
                );
            }
        }

        [HttpPost("trigger-full-refresh")]
        public async Task<IActionResult> TriggerFullRefresh([FromBody] FullRefreshRequest request)
        {
            Guid taskId = Guid.Empty;
            try
            {
                var taskLog = await _taskLogService.LogTaskStartAsync(
                    BlazorApp.Shared.Models.HBweb.TaskType.FullRefreshPreviousDay,
                    new TaskParameters(),
                    TaskTrigger.Manual
                );
                taskId = taskLog.Id;

                _logger.LogInformation("全量刷新任务已触发");

                await _statisticsJobService.FullRefreshPreviousDay();
                await _statisticsJobService.FullRefreshCurrentDay();

                await _taskLogService.LogTaskSuccessAsync(taskId);

                _logger.LogInformation("全量刷新任务执行完成");

                return Ok(
                    new
                    {
                        success = true,
                        message = "全量刷新任务执行完成",
                        jobId = taskId,
                    }
                );
            }
            catch (Exception ex)
            {
                if (taskId != Guid.Empty)
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, ex.Message);
                }
                _logger.LogError(ex, "触发全量刷新任务失败");
                return StatusCode(
                    500,
                    new { success = false, message = "触发任务失败: " + ex.Message }
                );
            }
        }

        [HttpPost("trigger-full-refresh-current-day")]
        public async Task<IActionResult> TriggerFullRefreshCurrentDay(
            [FromBody] FullRefreshCurrentDayRequest request
        )
        {
            Guid taskId = Guid.Empty;
            try
            {
                var taskLog = await _taskLogService.LogTaskStartAsync(
                    BlazorApp.Shared.Models.HBweb.TaskType.FullRefreshCurrentDay,
                    new TaskParameters(),
                    TaskTrigger.Manual
                );
                taskId = taskLog.Id;

                _logger.LogInformation("全量刷新当天数据任务已触发");

                await _statisticsJobService.FullRefreshCurrentDay();

                await _taskLogService.LogTaskSuccessAsync(taskId);

                _logger.LogInformation("全量刷新当天数据任务执行完成");

                return Ok(
                    new
                    {
                        success = true,
                        message = "全量刷新当天数据任务执行完成",
                        jobId = taskId,
                    }
                );
            }
            catch (Exception ex)
            {
                if (taskId != Guid.Empty)
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, ex.Message);
                }
                _logger.LogError(ex, "触发全量刷新当天数据任务失败");
                return StatusCode(
                    500,
                    new { success = false, message = "触发任务失败: " + ex.Message }
                );
            }
        }

        [HttpPost("batch-update-store")]
        public async Task<IActionResult> BatchUpdateStoreStatistics(
            [FromBody] BatchStoreUpdateRequest request
        )
        {
            Guid taskId = Guid.Empty;
            try
            {
                var taskLog = await _taskLogService.LogTaskStartAsync(
                    BlazorApp.Shared.Models.HBweb.TaskType.UpdateStoreStatisticsBatch,
                    new TaskParameters
                    {
                        StartDate = request.StartDate.ToString("yyyy-MM-dd"),
                        EndDate = request.EndDate.ToString("yyyy-MM-dd"),
                        BranchCodes = request.BranchCodes,
                    },
                    TaskTrigger.Manual
                );
                taskId = taskLog.Id;

                _logger.LogInformation(
                    "批量更新分店统计任务已触发: {StartDate} 至 {EndDate}, 分店: {Branches}",
                    request.StartDate.ToString("yyyy-MM-dd"),
                    request.EndDate.ToString("yyyy-MM-dd"),
                    request.BranchCodes != null ? string.Join(", ", request.BranchCodes) : "All"
                );

                var result = await _statisticsJobService.BatchUpdateStoreStatistics(
                    request.StartDate,
                    request.EndDate,
                    request.BranchCodes
                );

                if (result.Success)
                {
                    await _taskLogService.LogTaskSuccessAsync(taskId);
                }
                else
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, result.Message);
                }

                return Ok(
                    new
                    {
                        success = result.Success,
                        message = result.Message,
                        totalDays = result.TotalDays,
                        processedDays = result.ProcessedDays,
                        failedDates = result.FailedDates,
                        jobId = taskId,
                    }
                );
            }
            catch (Exception ex)
            {
                if (taskId != Guid.Empty)
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, ex.Message);
                }
                _logger.LogError(ex, "批量更新分店统计任务失败");
                return StatusCode(
                    500,
                    new { success = false, message = "批量更新失败: " + ex.Message }
                );
            }
        }

        [HttpPost("batch-update-supplier")]
        public async Task<IActionResult> BatchUpdateSupplierStatistics(
            [FromBody] BatchSupplierUpdateRequest request
        )
        {
            Guid taskId = Guid.Empty;
            try
            {
                var taskLog = await _taskLogService.LogTaskStartAsync(
                    BlazorApp.Shared.Models.HBweb.TaskType.UpdateSupplierStatisticsBatch,
                    new TaskParameters
                    {
                        StartDate = request.StartDate.ToString("yyyy-MM-dd"),
                        EndDate = request.EndDate.ToString("yyyy-MM-dd"),
                        SupplierCodes = request.SupplierCodes,
                    },
                    TaskTrigger.Manual
                );
                taskId = taskLog.Id;

                _logger.LogInformation(
                    "批量更新供应商统计任务已触发: {StartDate} 至 {EndDate}, 供应商: {Suppliers}",
                    request.StartDate.ToString("yyyy-MM-dd"),
                    request.EndDate.ToString("yyyy-MM-dd"),
                    request.SupplierCodes != null ? string.Join(", ", request.SupplierCodes) : "All"
                );

                // var result = await _statisticsJobService.BatchUpdateSupplierStatistics(
                var result = await _statisticsJobService.BatchUpdateSupplierStatisticsConcurrent(
                    request.StartDate,
                    request.EndDate,
                    request.SupplierCodes
                );

                if (result.Success)
                {
                    await _taskLogService.LogTaskSuccessAsync(taskId);
                }
                else
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, result.Message);
                }

                return Ok(
                    new
                    {
                        success = result.Success,
                        message = result.Message,
                        totalDays = result.TotalDays,
                        processedDays = result.ProcessedDays,
                        failedDates = result.FailedDates,
                        jobId = taskId,
                    }
                );
            }
            catch (Exception ex)
            {
                if (taskId != Guid.Empty)
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, ex.Message);
                }
                _logger.LogError(ex, "批量更新供应商统计任务失败");
                return StatusCode(
                    500,
                    new { success = false, message = "批量更新失败: " + ex.Message }
                );
            }
        }

        [HttpPost("batch-update-daily")]
        public async Task<IActionResult> BatchUpdateDailyStatistics(
            [FromBody] BatchDailyUpdateRequest request
        )
        {
            Guid taskId = Guid.Empty;
            try
            {
                var taskLog = await _taskLogService.LogTaskStartAsync(
                    BlazorApp.Shared.Models.HBweb.TaskType.UpdateDailyStatisticsBatch,
                    new TaskParameters
                    {
                        StartDate = request.StartDate.ToString("yyyy-MM-dd"),
                        EndDate = request.EndDate.ToString("yyyy-MM-dd"),
                    },
                    TaskTrigger.Manual
                );
                taskId = taskLog.Id;

                _logger.LogInformation(
                    "批量更新每日统计任务已触发: {StartDate} 至 {EndDate}",
                    request.StartDate.ToString("yyyy-MM-dd"),
                    request.EndDate.ToString("yyyy-MM-dd")
                );

                var result = await _statisticsJobService.BatchUpdateDailyStatistics(
                    request.StartDate,
                    request.EndDate
                );

                if (result.Success)
                {
                    await _taskLogService.LogTaskSuccessAsync(taskId);
                }
                else
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, result.Message);
                }

                return Ok(
                    new
                    {
                        success = result.Success,
                        message = result.Message,
                        totalDays = result.TotalDays,
                        processedDays = result.ProcessedDays,
                        failedDates = result.FailedDates,
                        jobId = taskId,
                    }
                );
            }
            catch (Exception ex)
            {
                if (taskId != Guid.Empty)
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, ex.Message);
                }
                _logger.LogError(ex, "批量更新每日统计任务失败");
                return StatusCode(
                    500,
                    new { success = false, message = "批量更新失败: " + ex.Message }
                );
            }
        }

        [HttpPost("batch-update-hourly")]
        public async Task<IActionResult> BatchUpdateHourlyStatistics(
            [FromBody] BatchHourlyUpdateRequest request
        )
        {
            Guid taskId = Guid.Empty;
            try
            {
                var hourStr = request.Hour.HasValue ? $" hour {request.Hour.Value}" : " all hours";

                var taskLog = await _taskLogService.LogTaskStartAsync(
                    BlazorApp.Shared.Models.HBweb.TaskType.UpdateHourlyStatisticsBatch,
                    new TaskParameters
                    {
                        StartDate = request.StartDate.ToString("yyyy-MM-dd"),
                        EndDate = request.EndDate.ToString("yyyy-MM-dd"),
                        Hour = request.Hour,
                    },
                    TaskTrigger.Manual
                );
                taskId = taskLog.Id;

                _logger.LogInformation(
                    "批量更新分时统计任务已触发: {StartDate} 至 {EndDate}{Hour}",
                    request.StartDate.ToString("yyyy-MM-dd"),
                    request.EndDate.ToString("yyyy-MM-dd"),
                    hourStr
                );

                var result = await _statisticsJobService.BatchUpdateHourlyStatistics(
                    request.StartDate,
                    request.EndDate,
                    request.Hour
                );

                if (result.Success)
                {
                    await _taskLogService.LogTaskSuccessAsync(taskId);
                }
                else
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, result.Message);
                }

                return Ok(
                    new
                    {
                        success = result.Success,
                        message = result.Message,
                        totalDays = result.TotalDays,
                        processedDays = result.ProcessedDays,
                        failedDates = result.FailedDates,
                        jobId = taskId,
                    }
                );
            }
            catch (Exception ex)
            {
                if (taskId != Guid.Empty)
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, ex.Message);
                }
                _logger.LogError(ex, "批量更新分时统计任务失败");
                return StatusCode(
                    500,
                    new { success = false, message = "批量更新失败: " + ex.Message }
                );
            }
        }

        [HttpPost("trigger-store-supplier")]
        public async Task<IActionResult> TriggerStoreSupplierStatistics(
            [FromBody] StoreSupplierJobTriggerRequest request
        )
        {
            Guid taskId = Guid.Empty;
            try
            {
                var taskLog = await _taskLogService.LogTaskStartAsync(
                    BlazorApp.Shared.Models.HBweb.TaskType.UpdateStoreSupplierStatistics,
                    new TaskParameters
                    {
                        Date = request.Date.ToString("yyyy-MM-dd"),
                        BranchCodes = request.BranchCodes,
                        SupplierCodes = request.SupplierCodes,
                    },
                    TaskTrigger.Manual
                );
                taskId = taskLog.Id;

                _logger.LogInformation(
                    "门店供应商统计任务已触发: Date={Date}, 分店: {Branches}, 供应商: {Suppliers}",
                    request.Date,
                    request.BranchCodes != null ? string.Join(", ", request.BranchCodes) : "All",
                    request.SupplierCodes != null ? string.Join(", ", request.SupplierCodes) : "All"
                );

                await _statisticsJobService.UpdateStoreSupplierStatistics(
                    request.Date,
                    request.BranchCodes,
                    request.SupplierCodes
                );

                await _taskLogService.LogTaskSuccessAsync(taskId);

                _logger.LogInformation(
                    "门店供应商统计任务执行完成: Date={Date}, 分店: {Branches}, 供应商: {Suppliers}",
                    request.Date,
                    request.BranchCodes != null ? string.Join(", ", request.BranchCodes) : "All",
                    request.SupplierCodes != null ? string.Join(", ", request.SupplierCodes) : "All"
                );

                return Ok(
                    new
                    {
                        success = true,
                        message = "门店供应商统计任务执行完成",
                        date = request.Date,
                        branches = request.BranchCodes,
                        suppliers = request.SupplierCodes,
                        jobId = taskId,
                    }
                );
            }
            catch (Exception ex)
            {
                if (taskId != Guid.Empty)
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, ex.Message);
                }
                _logger.LogError(ex, "触发门店供应商统计任务失败");
                return StatusCode(
                    500,
                    new { success = false, message = "触发任务失败: " + ex.Message }
                );
            }
        }

        [HttpPost("batch-update-store-supplier")]
        public async Task<IActionResult> BatchUpdateStoreSupplierStatistics(
            [FromBody] BatchStoreSupplierUpdateRequest request
        )
        {
            Guid taskId = Guid.Empty;
            try
            {
                var taskLog = await _taskLogService.LogTaskStartAsync(
                    BlazorApp.Shared.Models.HBweb.TaskType.UpdateStoreSupplierStatisticsBatch,
                    new TaskParameters
                    {
                        StartDate = request.StartDate.ToString("yyyy-MM-dd"),
                        EndDate = request.EndDate.ToString("yyyy-MM-dd"),
                        BranchCodes = request.BranchCodes,
                        SupplierCodes = request.SupplierCodes,
                    },
                    TaskTrigger.Manual
                );
                taskId = taskLog.Id;

                _logger.LogInformation(
                    "批量更新门店供应商统计任务已触发: {StartDate} 至 {EndDate}, 分店: {Branches}, 供应商: {Suppliers}",
                    request.StartDate.ToString("yyyy-MM-dd"),
                    request.EndDate.ToString("yyyy-MM-dd"),
                    request.BranchCodes != null ? string.Join(", ", request.BranchCodes) : "All",
                    request.SupplierCodes != null ? string.Join(", ", request.SupplierCodes) : "All"
                );

                var result = await _statisticsJobService.BatchUpdateStoreSupplierStatistics(
                    request.StartDate,
                    request.EndDate,
                    request.BranchCodes,
                    request.SupplierCodes
                );

                if (result.Success)
                {
                    await _taskLogService.LogTaskSuccessAsync(taskId);
                }
                else
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, result.Message);
                }

                return Ok(
                    new
                    {
                        success = result.Success,
                        message = result.Message,
                        totalDays = result.TotalDays,
                        processedDays = result.ProcessedDays,
                        failedDates = result.FailedDates,
                        jobId = taskId,
                    }
                );
            }
            catch (Exception ex)
            {
                if (taskId != Guid.Empty)
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, ex.Message);
                }
                _logger.LogError(ex, "批量更新门店供应商统计任务失败");
                return StatusCode(
                    500,
                    new { success = false, message = "批量更新失败: " + ex.Message }
                );
            }
        }

        [HttpPost("batch-full-refresh-by-months")]
        public async Task<IActionResult> BatchFullRefreshByMonths(
            [FromBody] BatchFullRefreshByMonthsRequest request
        )
        {
            Guid taskId = Guid.Empty;
            try
            {
                var taskLog = await _taskLogService.LogTaskStartAsync(
                    BlazorApp.Shared.Models.HBweb.TaskType.BatchFullRefreshByMonths,
                    new TaskParameters
                    {
                        StartYearMonth = request.StartYearMonth,
                        EndYearMonth = request.EndYearMonth,
                        MaxMonths = request.MaxMonths,
                    },
                    TaskTrigger.Manual
                );
                taskId = taskLog.Id;

                _logger.LogInformation(
                    "批量按月份刷新完整数据任务已触发: {StartYearMonth} 至 {EndYearMonth}",
                    request.StartYearMonth,
                    request.EndYearMonth
                );

                var result = await _statisticsJobService.BatchFullRefreshByMonths(
                    request.StartYearMonth,
                    request.EndYearMonth,
                    request.MaxMonths
                );

                if (result.Success)
                {
                    await _taskLogService.LogTaskSuccessAsync(taskId);
                }
                else
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, result.Message);
                }

                return Ok(
                    new
                    {
                        success = result.Success,
                        message = result.Message,
                        totalDays = result.TotalDays,
                        processedDays = result.ProcessedDays,
                        failedDates = result.FailedDates,
                        totalMonths = result.TotalMonths,
                        processedMonths = result.ProcessedMonths,
                        failedMonths = result.FailedMonths,
                        jobId = taskId,
                    }
                );
            }
            catch (Exception ex)
            {
                if (taskId != Guid.Empty)
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, ex.Message);
                }
                _logger.LogError(ex, "批量按月份刷新完整数据任务失败");
                return StatusCode(
                    500,
                    new { success = false, message = "批量刷新失败: " + ex.Message }
                );
            }
        }

        [HttpPost("batch-full-refresh-concurrent")]
        public async Task<IActionResult> BatchFullRefreshConcurrent(
            [FromBody] BatchFullRefreshConcurrentRequest request
        )
        {
            Guid taskId = Guid.Empty;
            try
            {
                var taskLog = await _taskLogService.LogTaskStartAsync(
                    BlazorApp.Shared.Models.HBweb.TaskType.BatchFullRefreshConcurrent,
                    new TaskParameters
                    {
                        StartDate = request.StartDate.ToString("yyyy-MM-dd"),
                        EndDate = request.EndDate.ToString("yyyy-MM-dd"),
                        MaxConcurrency = request.MaxConcurrency,
                    },
                    TaskTrigger.Manual
                );
                taskId = taskLog.Id;

                _logger.LogInformation(
                    "批量并发刷新完整数据任务已触发: {StartDate} 至 {EndDate}, 最大并发: {MaxConcurrency}",
                    request.StartDate.ToString("yyyy-MM-dd"),
                    request.EndDate.ToString("yyyy-MM-dd"),
                    request.MaxConcurrency
                );

                var result = await _statisticsJobService.BatchFullRefreshConcurrent(
                    request.StartDate,
                    request.EndDate,
                    request.MaxConcurrency
                );

                if (result.Success)
                {
                    await _taskLogService.LogTaskSuccessAsync(taskId);
                }
                else
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, result.Message);
                }

                return Ok(
                    new
                    {
                        success = result.Success,
                        message = result.Message,
                        totalDays = result.TotalDays,
                        processedDays = result.ProcessedDays,
                        failedDates = result.FailedDates,
                        jobId = taskId,
                    }
                );
            }
            catch (Exception ex)
            {
                if (taskId != Guid.Empty)
                {
                    await _taskLogService.LogTaskFailureAsync(taskId, ex.Message);
                }
                _logger.LogError(ex, "批量并发刷新完整数据任务失败");
                return StatusCode(
                    500,
                    new { success = false, message = "批量刷新失败: " + ex.Message }
                );
            }
        }
    }

    public class StoreJobTriggerRequest
    {
        public DateTime Date { get; set; }
        public List<string>? BranchCodes { get; set; }
    }

    public class SupplierJobTriggerRequest
    {
        public DateTime Date { get; set; }
        public List<string>? SupplierCodes { get; set; }
    }

    public class DailyJobTriggerRequest
    {
        public DateTime? Date { get; set; }
    }

    public class FullRefreshRequest { }

    public class FullRefreshCurrentDayRequest { }

    public class BatchStoreUpdateRequest
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<string>? BranchCodes { get; set; }
    }

    public class BatchSupplierUpdateRequest
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<string>? SupplierCodes { get; set; }
    }

    public class BatchDailyUpdateRequest
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class BatchHourlyUpdateRequest
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int? Hour { get; set; }
    }

    public class StoreSupplierJobTriggerRequest
    {
        public DateTime Date { get; set; }
        public List<string>? BranchCodes { get; set; }
        public List<string>? SupplierCodes { get; set; }
    }

    public class BatchStoreSupplierUpdateRequest
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<string>? BranchCodes { get; set; }
        public List<string>? SupplierCodes { get; set; }
    }

    public class BatchFullRefreshByMonthsRequest
    {
        public string StartYearMonth { get; set; } = string.Empty;
        public string EndYearMonth { get; set; } = string.Empty;
        public int MaxMonths { get; set; } = 12;
    }

    public class BatchFullRefreshConcurrentRequest
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int MaxConcurrency { get; set; } = 5;
    }
}
