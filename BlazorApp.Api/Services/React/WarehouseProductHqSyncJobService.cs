using System.Collections.Concurrent;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 仓库商品 HQ 同步后台任务服务。
    /// </summary>
    public class WarehouseProductHqSyncJobService : IWarehouseProductHqSyncJobService
    {
        private static readonly TimeSpan DefaultCompletedRetention = TimeSpan.FromMinutes(45);

        private readonly ConcurrentDictionary<string, WarehouseProductHqSyncJobState> _jobs = new();
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<WarehouseProductHqSyncJobService> _logger;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _completedRetention;
        private readonly object _jobStartSyncRoot = new();
        private string? _activeJobId;

        public WarehouseProductHqSyncJobService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<WarehouseProductHqSyncJobService> logger
        )
            : this(serviceScopeFactory, logger, TimeProvider.System, DefaultCompletedRetention) { }

        public WarehouseProductHqSyncJobService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<WarehouseProductHqSyncJobService> logger,
            TimeProvider? timeProvider = null,
            TimeSpan? completedRetention = null
        )
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _completedRetention = completedRetention ?? DefaultCompletedRetention;
        }

        public Task<WarehouseProductHqSyncJobDto> StartJobAsync(
            WarehouseProductHqSyncJobRequestDto request,
            CancellationToken cancellationToken = default
        )
        {
            CleanupExpiredJobs();

            var normalizedOperationId = NormalizeOperationId(request.OperationId);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var jobState = new WarehouseProductHqSyncJobState
            {
                JobId = Guid.NewGuid().ToString("N"),
                OperationId = normalizedOperationId,
                CreatedAt = now,
                Status = WarehouseProductHqSyncJobStatusConstants.Running,
                Message = "仓库商品同步任务已提交",
            };

            lock (_jobStartSyncRoot)
            {
                var activeJob = GetActiveJobSnapshotNoLock();
                if (activeJob != null)
                {
                    activeJob.IsDuplicateRequest = true;
                    return Task.FromResult(activeJob);
                }

                // 关键位置：先登记 job，再启动后台任务，避免查询时看不到刚提交的任务。
                _jobs[jobState.JobId] = jobState;
                _activeJobId = jobState.JobId;
            }

            // 关键位置：后台任务重新创建作用域，避免复用请求结束后的 scoped 服务。
            _ = Task.Run(() => ExecuteJobAsync(jobState), CancellationToken.None);
            return Task.FromResult(CreateSnapshot(jobState, false));
        }

        public Task<WarehouseProductHqSyncJobDto?> GetJobAsync(
            string jobId,
            CancellationToken cancellationToken = default
        )
        {
            CleanupExpiredJobs();
            return Task.FromResult(GetSnapshot(jobId, false));
        }

        private async Task ExecuteJobAsync(WarehouseProductHqSyncJobState jobState)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<IProductWarehouseReactService>();
                _logger.LogInformation(
                    "执行仓库商品 HQ 同步 job: {JobId}, operationId={OperationId}",
                    jobState.JobId,
                    jobState.OperationId
                );

                var result = await syncService.SyncFromHqAsync();
                CompleteJob(
                    jobState,
                    result.IsSuccess
                        ? WarehouseProductHqSyncJobStatusConstants.Succeeded
                        : WarehouseProductHqSyncJobStatusConstants.Failed,
                    result
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行仓库商品 HQ 同步 job 失败: {JobId}", jobState.JobId);
                CompleteJob(
                    jobState,
                    WarehouseProductHqSyncJobStatusConstants.Failed,
                    new SyncResult
                    {
                        IsSuccess = false,
                        Message = ex.Message,
                        ErrorCount = 1,
                        ErrorCode = "WAREHOUSE_PRODUCT_HQ_SYNC_JOB_FAILED",
                    }
                );
            }
        }

        private void CompleteJob(
            WarehouseProductHqSyncJobState jobState,
            string status,
            SyncResult result
        )
        {
            lock (_jobStartSyncRoot)
            {
                lock (jobState.SyncRoot)
                {
                    var completedAt = _timeProvider.GetUtcNow().UtcDateTime;
                    jobState.Status = status;
                    jobState.CompletedAt = completedAt;
                    jobState.ExpiresAt = completedAt.Add(_completedRetention);
                    jobState.Result = result;
                    jobState.Message = result.Message;
                }

                if (string.Equals(_activeJobId, jobState.JobId, StringComparison.OrdinalIgnoreCase))
                {
                    _activeJobId = null;
                }
            }
        }

        private void CleanupExpiredJobs()
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            foreach (var pair in _jobs)
            {
                var state = pair.Value;
                var expiresAt = state.ExpiresAt;
                if (expiresAt.HasValue && expiresAt.Value <= now)
                {
                    _jobs.TryRemove(pair.Key, out _);
                }
            }
        }

        private WarehouseProductHqSyncJobDto? GetActiveJobSnapshotNoLock()
        {
            if (string.IsNullOrWhiteSpace(_activeJobId))
            {
                return null;
            }

            if (!_jobs.TryGetValue(_activeJobId, out var activeState))
            {
                _activeJobId = null;
                return null;
            }

            lock (activeState.SyncRoot)
            {
                if (activeState.Status != WarehouseProductHqSyncJobStatusConstants.Running)
                {
                    _activeJobId = null;
                    return null;
                }
            }

            return CreateSnapshot(activeState, true);
        }

        private WarehouseProductHqSyncJobDto? GetSnapshot(string jobId, bool isDuplicateRequest)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return null;
            }

            return _jobs.TryGetValue(jobId, out var jobState)
                ? CreateSnapshot(jobState, isDuplicateRequest)
                : null;
        }

        private static WarehouseProductHqSyncJobDto CreateSnapshot(
            WarehouseProductHqSyncJobState jobState,
            bool isDuplicateRequest
        )
        {
            lock (jobState.SyncRoot)
            {
                return new WarehouseProductHqSyncJobDto
                {
                    JobId = jobState.JobId,
                    OperationId = jobState.OperationId,
                    Status = jobState.Status,
                    IsDuplicateRequest = isDuplicateRequest,
                    CreatedAt = jobState.CreatedAt,
                    CompletedAt = jobState.CompletedAt,
                    ExpiresAt = jobState.ExpiresAt,
                    Message = jobState.Message,
                    Result = jobState.Result,
                };
            }
        }

        private static string NormalizeOperationId(string? operationId)
        {
            return string.IsNullOrWhiteSpace(operationId)
                ? "warehouse-products-hq-sync"
                : operationId.Trim();
        }

        private sealed class WarehouseProductHqSyncJobState
        {
            public object SyncRoot { get; } = new();
            public string JobId { get; init; } = string.Empty;
            public string OperationId { get; init; } = string.Empty;
            public string Status { get; set; } = WarehouseProductHqSyncJobStatusConstants.Running;
            public DateTime CreatedAt { get; init; }
            public DateTime? CompletedAt { get; set; }
            public DateTime? ExpiresAt { get; set; }
            public string? Message { get; set; }
            public SyncResult? Result { get; set; }
        }
    }
}
