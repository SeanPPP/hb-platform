using System.Collections.Concurrent;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// HQ/本地分店价格与多码双向同步后台任务服务。
    /// 这是进程内轻量 job：用于前端轮询、运行中去重和 45 分钟结果保留，服务重启不恢复任务。
    /// </summary>
    public class StorePriceTransferJobService : IStorePriceTransferJobService
    {
        private static readonly TimeSpan DefaultCompletedRetention = TimeSpan.FromMinutes(45);
        private const string JobExecutionFailedMessage = "分店价格同步任务执行失败，请稍后重试或联系管理员";

        private readonly ConcurrentDictionary<string, StorePriceTransferJobState> _jobs = new();
        private readonly ConcurrentDictionary<string, string> _runningOperationJobIds = new();
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<StorePriceTransferJobService> _logger;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _completedRetention;
        private readonly object _jobStartSyncRoot = new();

        public StorePriceTransferJobService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<StorePriceTransferJobService> logger
        )
            : this(serviceScopeFactory, logger, TimeProvider.System, DefaultCompletedRetention) { }

        public StorePriceTransferJobService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<StorePriceTransferJobService> logger,
            TimeProvider? timeProvider = null,
            TimeSpan? completedRetention = null
        )
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _completedRetention = completedRetention ?? DefaultCompletedRetention;
        }

        public Task<StorePriceTransferJobDto> StartJobAsync(
            StorePriceTransferRequest request,
            string updatedBy,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            CleanupExpiredJobs();

            var normalizedRequest = CloneRequest(request);
            var operationId = BuildOperationId(normalizedRequest);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var jobState = new StorePriceTransferJobState
            {
                JobId = Guid.NewGuid().ToString("N"),
                OperationId = operationId,
                Request = normalizedRequest,
                CreatedAt = now,
                StartedAt = now,
                UpdatedBy = string.IsNullOrWhiteSpace(updatedBy) ? "system" : updatedBy.Trim(),
                Message = "分店价格同步任务已提交",
            };

            lock (_jobStartSyncRoot)
            {
                var duplicate = GetRunningJobSnapshotNoLock(operationId);
                if (duplicate != null)
                {
                    duplicate.IsDuplicateRequest = true;
                    return Task.FromResult(duplicate);
                }

                // 关键位置：按目标分店登记运行中任务，避免不同来源同时写同一个目标。
                _jobs[jobState.JobId] = jobState;
                _runningOperationJobIds[operationId] = jobState.JobId;
            }

            // 关键位置：后台任务重新创建 scope，避免请求生命周期结束后继续持有 scoped DbContext。
            _ = Task.Run(() => ExecuteJobAsync(jobState), CancellationToken.None);
            return Task.FromResult(CreateSnapshot(jobState, false));
        }

        public Task<StorePriceTransferJobDto?> GetJobAsync(
            string jobId,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            CleanupExpiredJobs();
            return Task.FromResult(GetSnapshot(jobId, false));
        }

        private async Task ExecuteJobAsync(StorePriceTransferJobState jobState)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IStorePriceTransferService>();
                var response = await service.TransferAsync(CloneRequest(jobState.Request), jobState.UpdatedBy);
                var status = response.Success
                    ? StorePriceTransferJobStatusConstants.Succeeded
                    : StorePriceTransferJobStatusConstants.Failed;
                CompleteJob(jobState, status, BuildResult(response), response.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行分店价格同步 job 失败: {JobId}", jobState.JobId);
                var safeMessage = $"{JobExecutionFailedMessage}，JobId: {jobState.JobId}";
                CompleteJob(
                    jobState,
                    StorePriceTransferJobStatusConstants.Failed,
                    new StorePriceTransferResult
                    {
                        FailedCount = 1,
                        Errors = [safeMessage],
                    },
                    safeMessage
                );
            }
        }

        private void CompleteJob(
            StorePriceTransferJobState jobState,
            string status,
            StorePriceTransferResult result,
            string? message
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
                    jobState.Message = message;
                }

                if (
                    _runningOperationJobIds.TryGetValue(jobState.OperationId, out var runningJobId)
                    && string.Equals(runningJobId, jobState.JobId, StringComparison.OrdinalIgnoreCase)
                )
                {
                    _runningOperationJobIds.TryRemove(jobState.OperationId, out _);
                }
            }
        }

        private void CleanupExpiredJobs()
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            foreach (var pair in _jobs)
            {
                var expiresAt = pair.Value.ExpiresAt;
                if (expiresAt.HasValue && expiresAt.Value <= now)
                {
                    _jobs.TryRemove(pair.Key, out _);
                }
            }
        }

        private StorePriceTransferJobDto? GetRunningJobSnapshotNoLock(string operationId)
        {
            if (!_runningOperationJobIds.TryGetValue(operationId, out var jobId))
            {
                return null;
            }

            var snapshot = GetSnapshot(jobId, true);
            if (snapshot == null || snapshot.Status != StorePriceTransferJobStatusConstants.Running)
            {
                _runningOperationJobIds.TryRemove(operationId, out _);
                return null;
            }

            return snapshot;
        }

        private StorePriceTransferJobDto? GetSnapshot(string jobId, bool isDuplicateRequest)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return null;
            }

            return _jobs.TryGetValue(jobId, out var jobState)
                ? CreateSnapshot(jobState, isDuplicateRequest)
                : null;
        }

        private static StorePriceTransferJobDto CreateSnapshot(
            StorePriceTransferJobState jobState,
            bool isDuplicateRequest
        )
        {
            lock (jobState.SyncRoot)
            {
                return new StorePriceTransferJobDto
                {
                    JobId = jobState.JobId,
                    OperationId = jobState.OperationId,
                    Status = jobState.Status,
                    IsDuplicateRequest = isDuplicateRequest,
                    Request = CloneRequest(jobState.Request),
                    Result = jobState.Result,
                    Message = jobState.Message,
                    CreatedAt = jobState.CreatedAt,
                    StartedAt = jobState.StartedAt,
                    CompletedAt = jobState.CompletedAt,
                    ExpiresAt = jobState.ExpiresAt,
                    Errors = jobState.Result?.Errors.ToList() ?? new List<string>(),
                };
            }
        }

        private static StorePriceTransferResult BuildResult(
            ApiResponse<StorePriceTransferResult> response
        )
        {
            var result =
                response.Data
                ?? response.Details as StorePriceTransferResult
                ?? new StorePriceTransferResult();

            if (!response.Success)
            {
                if (result.FailedCount == 0)
                {
                    result.FailedCount = 1;
                }

                if (
                    !string.IsNullOrWhiteSpace(response.Message)
                    && !result.Errors.Contains(response.Message, StringComparer.Ordinal)
                )
                {
                    result.Errors.Add(response.Message);
                }
            }

            return result;
        }

        private static string BuildOperationId(StorePriceTransferRequest request)
        {
            return string.Join(
                "|",
                "store-price-transfer-target",
                NormalizeCode(request.Direction)?.ToUpperInvariant() ?? string.Empty,
                NormalizeCode(request.TargetStoreCode)?.ToUpperInvariant() ?? string.Empty
            );
        }

        private static StorePriceTransferRequest CloneRequest(StorePriceTransferRequest request)
        {
            return new StorePriceTransferRequest
            {
                Direction = NormalizeCode(request.Direction) ?? StorePriceTransferDirectionConstants.HqToLocal,
                SourceStoreCode = NormalizeCode(request.SourceStoreCode),
                TargetStoreCode = NormalizeCode(request.TargetStoreCode),
                SyncRetailPrices = request.SyncRetailPrices,
                SyncMultiCodePrices = request.SyncMultiCodePrices,
                SyncPurchasePrice = request.SyncPurchasePrice,
                SyncRetailPrice = request.SyncRetailPrice,
                SyncDiscountRate = request.SyncDiscountRate,
                SyncIsAutoPricing = request.SyncIsAutoPricing,
                SyncIsSpecialProduct = request.SyncIsSpecialProduct,
            };
        }

        private static string? NormalizeCode(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private sealed class StorePriceTransferJobState
        {
            public object SyncRoot { get; } = new();
            public string JobId { get; init; } = string.Empty;
            public string OperationId { get; init; } = string.Empty;
            public StorePriceTransferRequest Request { get; init; } = new();
            public string UpdatedBy { get; init; } = "system";
            public string Status { get; set; } = StorePriceTransferJobStatusConstants.Running;
            public StorePriceTransferResult? Result { get; set; }
            public string? Message { get; set; }
            public DateTime CreatedAt { get; init; }
            public DateTime StartedAt { get; init; }
            public DateTime? CompletedAt { get; set; }
            public DateTime? ExpiresAt { get; set; }
        }
    }
}
