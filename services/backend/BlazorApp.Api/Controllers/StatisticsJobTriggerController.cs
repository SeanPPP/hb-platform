using BlazorApp.Api.Services;
using BlazorApp.Api.Services.Background;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace BlazorApp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class StatisticsJobTriggerController : ControllerBase
    {
        private readonly SalesStatisticsJobService _statisticsJobService;
        private readonly ScheduledTaskLogService _taskLogService;
        private readonly SqlSugarContext _context;
        private readonly ILogger<StatisticsJobTriggerController> _logger;
        private readonly ISalesDashboardCacheWarmer _cacheWarmer;
        private readonly SalesStatisticsAlignmentService _alignmentService;
        private const int MaxProductStoreDailyBatchDays = 31;
        private const int MaxAlignmentQueryDays = 62;

        public StatisticsJobTriggerController(
            SalesStatisticsJobService statisticsJobService,
            ScheduledTaskLogService taskLogService,
            SqlSugarContext context,
            ILogger<StatisticsJobTriggerController> logger,
            ISalesDashboardCacheWarmer cacheWarmer,
            SalesStatisticsAlignmentService alignmentService
        )
        {
            _statisticsJobService = statisticsJobService;
            _taskLogService = taskLogService;
            _context = context;
            _logger = logger;
            _cacheWarmer = cacheWarmer;
            _alignmentService = alignmentService;
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

        [HttpPost("trigger-product-store-daily")]
        public async Task<IActionResult> TriggerProductStoreDailyStatistics(
            [FromBody] ProductStoreDailyJobTriggerRequest request
        )
        {
            try
            {
                var result = await _statisticsJobService.SubmitProductStoreDailyRecalculationAsync(
                    new[] { request.Date.Date },
                    HttpContext?.User?.Identity?.Name
                );
                await ClearSalesDashboardCacheAfterProductStatisticSubmitAsync();

                return Ok(BuildProductStoreDailySubmitResponse(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "提交商品分店每日统计任务失败");
                return StatusCode(
                    500,
                    new { success = false, message = "触发任务失败: " + ex.Message }
                );
            }
        }

        [HttpPost("recent-product-store-daily")]
        public async Task<IActionResult> RecentProductStoreDailyStatistics(
            [FromBody] RecentProductStoreDailyJobTriggerRequest? request
        )
        {
            var days = Math.Clamp(request?.Days ?? 7, 1, MaxProductStoreDailyBatchDays);
            try
            {
                var endDate = DateTime.Now.Date;
                var dates = EnumerateDates(endDate.AddDays(-(days - 1)), endDate);
                var result = await _statisticsJobService.SubmitProductStoreDailyRecalculationAsync(
                    dates,
                    HttpContext?.User?.Identity?.Name
                );
                await ClearSalesDashboardCacheAfterProductStatisticSubmitAsync();

                return Ok(BuildProductStoreDailySubmitResponse(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "提交最近商品分店每日统计任务失败");
                return StatusCode(
                    500,
                    new { success = false, message = "触发任务失败: " + ex.Message }
                );
            }
        }

        [HttpGet("product-store-daily/states")]
        public async Task<IActionResult> GetProductStoreDailyStatisticStates(
            [FromQuery] string? statisticType,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] string? status
        )
        {
            var targetType = NormalizeProductStatisticType(statisticType);
            if (targetType == null)
            {
                return BadRequest(new { success = false, message = "不支持的统计类型" });
            }

            var targetStatus = NormalizeStatisticStatus(status);
            if (!string.IsNullOrWhiteSpace(status) && targetStatus == null)
            {
                return BadRequest(new { success = false, message = "不支持的统计状态" });
            }

            var query = _context.Db.Queryable<SalesStatisticRefreshState>()
                .Where(s => s.StatisticType == targetType);

            if (startDate.HasValue)
            {
                var start = startDate.Value.Date;
                query = query.Where(s => s.Date >= start);
            }

            if (endDate.HasValue)
            {
                var end = endDate.Value.Date;
                query = query.Where(s => s.Date <= end);
            }

            if (!string.IsNullOrWhiteSpace(targetStatus))
            {
                query = query.Where(s => s.Status == targetStatus);
            }

            var rows = await query
                .OrderBy(s => s.Date, OrderByType.Desc)
                .Select(s => new SalesStatisticRefreshStateListItemDto
                {
                    StatisticType = s.StatisticType,
                    Date = s.Date,
                    Status = s.Status,
                    LastSourceUploadTime = s.LastSourceUploadTime,
                    SourceTimeZone = s.SourceTimeZone,
                    LastAggregatedAtUtc = s.LastAggregatedAtUtc,
                    LastCheckedAtUtc = s.LastCheckedAtUtc,
                    ErrorMessage = s.ErrorMessage,
                    JobId = s.JobId,
                    RequestedAtUtc = s.RequestedAtUtc,
                    StartedAtUtc = s.StartedAtUtc,
                    CompletedAtUtc = s.CompletedAtUtc,
                })
                .ToListAsync();

            return Ok(new { success = true, data = rows });
        }

        [HttpGet("product-store-daily/{date:datetime}/summary")]
        public async Task<IActionResult> GetProductStoreDailyStatisticSummary(DateTime date)
        {
            var targetDate = date.Date;
            var state = await _context.Db.Queryable<SalesStatisticRefreshState>()
                .Where(s => s.StatisticType == SalesStatisticType.ProductStoreDaily && s.Date == targetDate)
                .FirstAsync();

            var rows = await _context.Db.Queryable<ProductStoreDailySalesStatistic>()
                .Where(s => s.Date == targetDate)
                .Select(s => new
                {
                    s.BranchCode,
                    s.TotalQuantity,
                    s.TotalAmount,
                    s.GrossProfit,
                })
                .ToListAsync();

            var storeRows = await _context.Db.Queryable<StoreSalesStatistic>()
                .Where(s => s.Date == targetDate)
                .Select(s => new
                {
                    s.BranchCode,
                    s.TotalQuantity,
                    s.TotalAmount,
                })
                .ToListAsync();

            var reconciliation = ProductStoreDailyReconciliationCalculator.Calculate(
                targetDate,
                rows.Select(row => new ProductStoreDailyBranchRollup(
                    row.BranchCode,
                    row.TotalAmount,
                    row.TotalQuantity
                )),
                storeRows.Select(row => new ProductStoreDailyBranchRollup(
                    row.BranchCode,
                    row.TotalAmount,
                    row.TotalQuantity
                ))
            );

            var summary = new ProductStoreDailyStatisticSummaryDto
            {
                Date = targetDate,
                Status = state?.Status ?? SalesStatisticRefreshStatus.Pending,
                RecordCount = rows.Count,
                TotalQuantity = reconciliation.ProductTotalQuantity,
                TotalAmount = reconciliation.ProductTotalAmount,
                GrossProfit = rows.Any(x => x.GrossProfit.HasValue)
                    ? rows.Sum(x => x.GrossProfit ?? 0m)
                    : null,
                ReconciliationStatus = ResolveProductStatisticReconciliationStatus(state?.Status, rows.Any()),
                SalesReconciliationStatus = ResolveProductStatisticSalesReconciliationStatus(
                    state?.Status,
                    reconciliation.Status
                ),
                ProductTotalAmount = reconciliation.ProductTotalAmount,
                StoreTotalAmount = reconciliation.StoreTotalAmount,
                AmountDifference = reconciliation.AmountDifference,
                ProductTotalQuantity = reconciliation.ProductTotalQuantity,
                StoreTotalQuantity = reconciliation.StoreTotalQuantity,
                QuantityDifference = reconciliation.QuantityDifference,
                UnmatchedSupplierAmount = ExtractDecimalFromMessage(state?.ErrorMessage, "未匹配供应商金额"),
                UnmatchedSupplierQuantity = ExtractIntFromMessage(state?.ErrorMessage, "未匹配供应商数量"),
                UnmatchedSupplierProductCount = ExtractIntFromMessage(state?.ErrorMessage, "未匹配商品数"),
                LastSourceUploadTime = state?.LastSourceUploadTime,
                SourceTimeZone = state?.SourceTimeZone,
                LastAggregatedAtUtc = state?.LastAggregatedAtUtc,
                LastCheckedAtUtc = state?.LastCheckedAtUtc,
                ErrorMessage = state?.ErrorMessage,
                JobId = state?.JobId,
                RequestedAtUtc = state?.RequestedAtUtc,
                StartedAtUtc = state?.StartedAtUtc,
                CompletedAtUtc = state?.CompletedAtUtc,
            };

            return Ok(new { success = true, data = summary });
        }

        private static string? NormalizeProductStatisticType(string? statisticType)
        {
            if (string.IsNullOrWhiteSpace(statisticType))
            {
                return SalesStatisticType.ProductStoreDaily;
            }

            return string.Equals(
                statisticType.Trim(),
                SalesStatisticType.ProductStoreDaily,
                StringComparison.OrdinalIgnoreCase
            )
                ? SalesStatisticType.ProductStoreDaily
                : null;
        }

        private static string? NormalizeStatisticStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return null;
            }

            var normalized = status.Trim();
            return normalized.ToLowerInvariant() switch
            {
                "queued" => SalesStatisticRefreshStatus.Queued,
                "running" => SalesStatisticRefreshStatus.Running,
                "pending" => SalesStatisticRefreshStatus.Pending,
                "fresh" => SalesStatisticRefreshStatus.Fresh,
                "stale" => SalesStatisticRefreshStatus.Stale,
                "failed" => SalesStatisticRefreshStatus.Failed,
                _ => null,
            };
        }

        private static string ResolveProductStatisticReconciliationStatus(string? status, bool hasRows)
        {
            return status switch
            {
                SalesStatisticRefreshStatus.Fresh when hasRows => "Passed",
                SalesStatisticRefreshStatus.Failed => "Failed",
                _ => "Pending",
            };
        }

        private static string ResolveProductStatisticSalesReconciliationStatus(
            string? stateStatus,
            string reconciliationStatus
        )
        {
            if (
                stateStatus != SalesStatisticRefreshStatus.Fresh
                && stateStatus != SalesStatisticRefreshStatus.Failed
            )
            {
                return "Pending";
            }

            return reconciliationStatus switch
            {
                SalesStatisticRefreshStatus.Fresh => "Passed",
                SalesStatisticRefreshStatus.Failed => "Failed",
                _ => "Pending",
            };
        }

        private static decimal? ExtractDecimalFromMessage(string? message, string marker)
        {
            var token = ExtractTokenAfterMarker(message, marker);
            return decimal.TryParse(token, out var value) ? value : null;
        }

        private static int? ExtractIntFromMessage(string? message, string marker)
        {
            var token = ExtractTokenAfterMarker(message, marker);
            return int.TryParse(token, out var value) ? value : null;
        }

        private static string? ExtractTokenAfterMarker(string? message, string marker)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            var markerIndex = message.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                return null;
            }

            var tokenStart = markerIndex + marker.Length;
            while (tokenStart < message.Length && char.IsWhiteSpace(message[tokenStart]))
            {
                tokenStart++;
            }

            var tokenEnd = tokenStart;
            while (tokenEnd < message.Length && message[tokenEnd] != ',')
            {
                tokenEnd++;
            }

            return message[tokenStart..tokenEnd].Trim();
        }

        [HttpPost("batch-product-store-daily")]
        public async Task<IActionResult> BatchProductStoreDailyStatistics(
            [FromBody] BatchProductStoreDailyUpdateRequest request
        )
        {
            try
            {
                if (request.StartDate > request.EndDate)
                {
                    return BadRequest(new { success = false, message = "开始日期不能大于结束日期" });
                }
                var requestedDays = (int)(request.EndDate.Date - request.StartDate.Date).TotalDays + 1;
                if (requestedDays > MaxProductStoreDailyBatchDays)
                {
                    return BadRequest(new
                    {
                        success = false,
                        message = $"商品分店每日统计一次最多重算 {MaxProductStoreDailyBatchDays} 天，请分段执行",
                    });
                }

                // 控制器入口先做范围兜底，避免前端传入异常并发值放大后台压力。
                var maxConcurrency = request.MaxConcurrency < 1
                    ? 3
                    : Math.Min(request.MaxConcurrency, 10);
                var result = await _statisticsJobService.SubmitProductStoreDailyRecalculationAsync(
                    EnumerateDates(request.StartDate, request.EndDate),
                    HttpContext?.User?.Identity?.Name,
                    maxConcurrency
                );
                await ClearSalesDashboardCacheAfterProductStatisticSubmitAsync();

                return Ok(BuildProductStoreDailySubmitResponse(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量提交商品分店每日统计任务失败");
                return StatusCode(
                    500,
                    new { success = false, message = "触发任务失败: " + ex.Message }
                );
            }
        }

        [HttpGet("alignment/daily")]
        public async Task<IActionResult> GetDailyStatisticsAlignment(
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate
        )
        {
            var end = (endDate ?? DateTime.Now.Date).Date;
            var start = (startDate ?? end.AddDays(-13)).Date;
            if (start > end)
            {
                return BadRequest(new { success = false, message = "开始日期不能大于结束日期" });
            }

            var days = (int)(end - start).TotalDays + 1;
            if (days > MaxAlignmentQueryDays)
            {
                return BadRequest(new
                {
                    success = false,
                    message = $"数据对齐一次最多查询 {MaxAlignmentQueryDays} 天，请缩小日期范围",
                });
            }

            var result = await _alignmentService.GetDailyAlignmentAsync(start, end);
            return Ok(new { success = true, data = result });
        }

        [HttpPost("alignment/recalculate")]
        public async Task<IActionResult> RecalculateDailyStatisticsAlignment(
            [FromBody] DailyStatisticsAlignmentRecalculateRequest request
        )
        {
            var dates = NormalizeAlignmentRecalculateDates(request);
            if (!dates.Any())
            {
                return BadRequest(new { success = false, message = "请选择需要补算的日期" });
            }

            if (dates.Count > MaxAlignmentQueryDays)
            {
                return BadRequest(new
                {
                    success = false,
                    message = $"一次最多补算 {MaxAlignmentQueryDays} 天，请分段执行",
                });
            }

            var maxConcurrency = request.MaxConcurrency < 1
                ? 3
                : Math.Min(request.MaxConcurrency, 10);
            var result = await _alignmentService.RecalculateAsync(dates, maxConcurrency);
            await ClearSalesDashboardCacheAfterProductStatisticSubmitAsync();

            return Ok(new
            {
                success = result.Success,
                message = result.Message,
                jobId = result.JobId,
                processedDates = result.ProcessedDates.Select(date => date.ToString("yyyy-MM-dd")).ToList(),
                skippedDates = result.SkippedDates.Select(date => date.ToString("yyyy-MM-dd")).ToList(),
                failedDates = result.FailedDates.Select(date => date.ToString("yyyy-MM-dd")).ToList(),
            });
        }

        private static List<DateTime> NormalizeAlignmentRecalculateDates(
            DailyStatisticsAlignmentRecalculateRequest request
        )
        {
            if (request.Dates?.Any() == true)
            {
                return request.Dates
                    .Select(date => date.Date)
                    .Distinct()
                    .OrderBy(date => date)
                    .ToList();
            }

            if (!request.StartDate.HasValue || !request.EndDate.HasValue)
            {
                return new List<DateTime>();
            }

            return EnumerateDates(request.StartDate.Value, request.EndDate.Value);
        }

        private static List<DateTime> EnumerateDates(DateTime startDate, DateTime endDate)
        {
            var dates = new List<DateTime>();
            for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
            {
                dates.Add(date);
            }
            return dates;
        }

        private async Task ClearSalesDashboardCacheAfterProductStatisticSubmitAsync()
        {
            // 商品统计重算会影响热销榜，提交后立即清理看板缓存，避免页面继续读取旧排名。
            await _cacheWarmer.ClearCacheAsync();
        }

        private static object BuildProductStoreDailySubmitResponse(
            ProductStoreDailyRecalculationSubmitResult result
        )
        {
            return new
            {
                success = true,
                message = result.Message,
                jobId = result.JobId,
                status = result.Status,
                submittedDates = result.SubmittedDates.Select(date => date.ToString("yyyy-MM-dd")).ToList(),
                skippedDates = result.SkippedDates.Select(date => date.ToString("yyyy-MM-dd")).ToList(),
            };
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
                        skippedDates = result.SkippedDates,
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
                        skippedDates = result.SkippedDates,
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
                        skippedDates = result.SkippedDates,
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
                        skippedDates = result.SkippedDates,
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
                        skippedDates = result.SkippedDates,
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
                        skippedDates = result.SkippedDates,
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

    public class ProductStoreDailyJobTriggerRequest
    {
        public DateTime Date { get; set; }
    }

    public class RecentProductStoreDailyJobTriggerRequest
    {
        public int Days { get; set; } = 7;
    }

    public class BatchProductStoreDailyUpdateRequest
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int MaxConcurrency { get; set; } = 3;
    }

    public class DailyStatisticsAlignmentRecalculateRequest
    {
        public List<DateTime>? Dates { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public int MaxConcurrency { get; set; } = 3;
    }

    public class SalesStatisticRefreshStateListItemDto
    {
        public string StatisticType { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime? LastSourceUploadTime { get; set; }
        public string? SourceTimeZone { get; set; }
        public DateTime? LastAggregatedAtUtc { get; set; }
        public DateTime? LastCheckedAtUtc { get; set; }
        public string? ErrorMessage { get; set; }
        public Guid? JobId { get; set; }
        public DateTime? RequestedAtUtc { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }

    public class ProductStoreDailyStatisticSummaryDto
    {
        public DateTime Date { get; set; }
        public string Status { get; set; } = string.Empty;
        public int RecordCount { get; set; }
        public int TotalQuantity { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal? GrossProfit { get; set; }
        public string ReconciliationStatus { get; set; } = string.Empty;
        public string SalesReconciliationStatus { get; set; } = string.Empty;
        public decimal ProductTotalAmount { get; set; }
        public decimal? StoreTotalAmount { get; set; }
        public decimal? AmountDifference { get; set; }
        public int ProductTotalQuantity { get; set; }
        public int? StoreTotalQuantity { get; set; }
        public int? QuantityDifference { get; set; }
        public decimal? UnmatchedSupplierAmount { get; set; }
        public int? UnmatchedSupplierQuantity { get; set; }
        public int? UnmatchedSupplierProductCount { get; set; }
        public DateTime? LastSourceUploadTime { get; set; }
        public string? SourceTimeZone { get; set; }
        public DateTime? LastAggregatedAtUtc { get; set; }
        public DateTime? LastCheckedAtUtc { get; set; }
        public string? ErrorMessage { get; set; }
        public Guid? JobId { get; set; }
        public DateTime? RequestedAtUtc { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
    }

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
