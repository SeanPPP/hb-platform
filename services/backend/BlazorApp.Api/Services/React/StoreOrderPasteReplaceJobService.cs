using System.Collections.Concurrent;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// Excel 粘贴导入后台 job 服务。
    /// </summary>
    public class StoreOrderPasteReplaceJobService : IStoreOrderPasteReplaceJobService
    {
        private static readonly TimeSpan DefaultCompletedRetention = TimeSpan.FromMinutes(45);

        private readonly ConcurrentDictionary<string, StoreOrderPasteReplaceJobState> _jobs = new();
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<StoreOrderPasteReplaceJobService> _logger;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _completedRetention;

        public StoreOrderPasteReplaceJobService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<StoreOrderPasteReplaceJobService> logger
        )
            : this(serviceScopeFactory, logger, TimeProvider.System, DefaultCompletedRetention) { }

        public StoreOrderPasteReplaceJobService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<StoreOrderPasteReplaceJobService> logger,
            TimeProvider? timeProvider = null,
            TimeSpan? completedRetention = null
        )
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _completedRetention = completedRetention ?? DefaultCompletedRetention;
        }

        public Task<StoreOrderPasteReplaceJobDto> StartJobAsync(
            PasteReplaceOrderLinesDto request,
            CancellationToken cancellationToken = default
        )
        {
            CleanupExpiredJobs();

            var normalizedRequest = CloneRequest(request);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var jobState = new StoreOrderPasteReplaceJobState
            {
                JobId = Guid.NewGuid().ToString("N"),
                Request = normalizedRequest,
                OrderGUID = normalizedRequest.OrderGUID,
                TargetField = normalizedRequest.TargetField,
                CreatedAt = now,
                TotalCount = normalizedRequest.Items.Count,
                ImportedCount = CountImportableItems(normalizedRequest.Items),
                Status = StoreOrderPasteReplaceJobStatusConstants.Queued,
                Message = "Excel 粘贴导入任务已提交",
            };
            jobState.SkippedCount = Math.Max(0, jobState.TotalCount - jobState.ImportedCount);

            _jobs[jobState.JobId] = jobState;

            // 关键位置：后台任务重新创建作用域，避免复用请求结束后的 scoped 服务。
            _ = Task.Run(() => ExecuteJobAsync(jobState), CancellationToken.None);
            return Task.FromResult(CreateSnapshot(jobState));
        }

        public Task<StoreOrderPasteReplaceJobDto?> GetJobAsync(
            string jobId,
            CancellationToken cancellationToken = default
        )
        {
            CleanupExpiredJobs();

            if (string.IsNullOrWhiteSpace(jobId))
            {
                return Task.FromResult<StoreOrderPasteReplaceJobDto?>(null);
            }

            return Task.FromResult(
                _jobs.TryGetValue(jobId.Trim(), out var jobState)
                    ? CreateSnapshot(jobState)
                    : null
            );
        }

        private async Task ExecuteJobAsync(StoreOrderPasteReplaceJobState jobState)
        {
            try
            {
                SetRunning(jobState);

                using var scope = _serviceScopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IStoreOrderReactService>();
                var result = await service.PasteReplaceOrderLinesAsync(jobState.Request);

                CompleteJob(
                    jobState,
                    result.Success
                        ? StoreOrderPasteReplaceJobStatusConstants.Succeeded
                        : StoreOrderPasteReplaceJobStatusConstants.Failed,
                    string.IsNullOrWhiteSpace(result.Message)
                        ? result.Success
                            ? "Excel 粘贴导入完成"
                            : "Excel 粘贴导入失败"
                        : result.Message
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行 Excel 粘贴导入 job 失败: {JobId}", jobState.JobId);
                CompleteJob(
                    jobState,
                    StoreOrderPasteReplaceJobStatusConstants.Failed,
                    string.IsNullOrWhiteSpace(ex.Message) ? "Excel 粘贴导入失败" : ex.Message
                );
            }
        }

        private void SetRunning(StoreOrderPasteReplaceJobState jobState)
        {
            lock (jobState.SyncRoot)
            {
                jobState.Status = StoreOrderPasteReplaceJobStatusConstants.Running;
                jobState.Message = "Excel 粘贴导入中";
            }
        }

        private void CompleteJob(
            StoreOrderPasteReplaceJobState jobState,
            string status,
            string message
        )
        {
            lock (jobState.SyncRoot)
            {
                jobState.Status = status;
                jobState.Message = message;
                jobState.CompletedAt = _timeProvider.GetUtcNow().UtcDateTime;
            }
        }

        private void CleanupExpiredJobs()
        {
            var threshold = _timeProvider.GetUtcNow().UtcDateTime - _completedRetention;
            foreach (var pair in _jobs)
            {
                var completedAt = pair.Value.CompletedAt;
                if (completedAt.HasValue && completedAt.Value < threshold)
                {
                    _jobs.TryRemove(pair.Key, out _);
                }
            }
        }

        private static PasteReplaceOrderLinesDto CloneRequest(PasteReplaceOrderLinesDto request)
        {
            return new PasteReplaceOrderLinesDto
            {
                OrderGUID = request.OrderGUID?.Trim() ?? string.Empty,
                TargetField = string.IsNullOrWhiteSpace(request.TargetField)
                    ? StoreOrderPasteTargetFields.Quantity
                    : request.TargetField.Trim(),
                Items = request.Items
                    .Select(item => new ProductQuantityDto
                    {
                        ProductCode = item.ProductCode?.Trim() ?? string.Empty,
                        Quantity = item.Quantity,
                        ImportPrice = item.ImportPrice,
                        Action = item.Action?.Trim(),
                    })
                    .ToList(),
            };
        }

        private static int CountImportableItems(IEnumerable<ProductQuantityDto> items)
        {
            return items.Count(item =>
                !string.IsNullOrWhiteSpace(item.ProductCode)
                // 与实际粘贴写入规则一致：0 可用于清零已有明细，负数才跳过。
                && item.Quantity >= 0
                && !string.Equals(
                    item.Action,
                    StoreOrderPasteActions.Skip,
                    StringComparison.OrdinalIgnoreCase
                )
            );
        }

        private static StoreOrderPasteReplaceJobDto CreateSnapshot(
            StoreOrderPasteReplaceJobState jobState
        )
        {
            lock (jobState.SyncRoot)
            {
                return new StoreOrderPasteReplaceJobDto
                {
                    JobId = jobState.JobId,
                    Status = jobState.Status,
                    Message = jobState.Message,
                    OrderGUID = jobState.OrderGUID,
                    TargetField = jobState.TargetField,
                    TotalCount = jobState.TotalCount,
                    ImportedCount = jobState.ImportedCount,
                    SkippedCount = jobState.SkippedCount,
                    CreatedAt = jobState.CreatedAt,
                    CompletedAt = jobState.CompletedAt,
                };
            }
        }

        private sealed class StoreOrderPasteReplaceJobState
        {
            public object SyncRoot { get; } = new();
            public string JobId { get; init; } = string.Empty;
            public PasteReplaceOrderLinesDto Request { get; init; } = new();
            public string OrderGUID { get; init; } = string.Empty;
            public string TargetField { get; init; } = StoreOrderPasteTargetFields.Quantity;
            public string Status { get; set; } = StoreOrderPasteReplaceJobStatusConstants.Queued;
            public string Message { get; set; } = string.Empty;
            public int TotalCount { get; init; }
            public int ImportedCount { get; init; }
            public int SkippedCount { get; set; }
            public DateTime CreatedAt { get; init; }
            public DateTime? CompletedAt { get; set; }
        }
    }
}
