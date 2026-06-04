using System.Collections.Concurrent;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 供应商商品图片批量更新后台任务服务。
    /// </summary>
    public class ProductSupplierImageBatchUpdateJobService
        : IProductSupplierImageBatchUpdateJobService
    {
        private static readonly TimeSpan DefaultCompletedRetention = TimeSpan.FromMinutes(45);

        // 当前部署按单 API 实例处理；多实例部署时这里需要升级为数据库或分布式锁。
        private readonly ConcurrentDictionary<string, SupplierImageJobState> _jobs = new();
        private readonly ConcurrentDictionary<string, string> _runningOperationJobIds = new();
        private readonly ConcurrentDictionary<string, string> _runningSupplierJobIds = new();
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<ProductSupplierImageBatchUpdateJobService> _logger;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _completedRetention;
        private readonly object _jobStartSyncRoot = new();

        public ProductSupplierImageBatchUpdateJobService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<ProductSupplierImageBatchUpdateJobService> logger
        )
            : this(serviceScopeFactory, logger, TimeProvider.System, DefaultCompletedRetention) { }

        public ProductSupplierImageBatchUpdateJobService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<ProductSupplierImageBatchUpdateJobService> logger,
            TimeProvider? timeProvider = null,
            TimeSpan? completedRetention = null
        )
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _completedRetention = completedRetention ?? DefaultCompletedRetention;
        }

        public Task<BatchUpdateSupplierImagesJobDto> StartJobAsync(
            BatchUpdateSupplierImagesJobRequest request,
            CancellationToken cancellationToken = default
        )
        {
            CleanupExpiredJobs();

            var normalizedRequest = CloneRequest(request);
            var operationId = NormalizeOperationId(request?.OperationId, normalizedRequest);
            var supplierKey = NormalizeSupplierKey(normalizedRequest.LocalSupplierCode);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var jobState = new SupplierImageJobState
            {
                JobId = Guid.NewGuid().ToString("N"),
                OperationId = operationId,
                SupplierKey = supplierKey,
                Request = normalizedRequest,
                CreatedAt = now,
                Status = BatchUpdateSupplierImagesJobStatusConstants.Queued,
                Message = "供应商商品图片批量更新任务已提交",
            };

            lock (_jobStartSyncRoot)
            {
                var duplicate = GetRunningJobSnapshotNoLock(operationId);
                if (duplicate != null)
                {
                    duplicate.IsDuplicateRequest = true;
                    return Task.FromResult(duplicate);
                }

                var supplierDuplicate = GetRunningSupplierJobSnapshotNoLock(supplierKey);
                if (supplierDuplicate != null)
                {
                    supplierDuplicate.IsDuplicateRequest = true;
                    return Task.FromResult(supplierDuplicate);
                }

                // 关键位置：先登记 job 和 operationId，再起后台任务，保证前端拿到 jobId 后立刻可查。
                _jobs[jobState.JobId] = jobState;
                _runningOperationJobIds[operationId] = jobState.JobId;
                _runningSupplierJobIds[supplierKey] = jobState.JobId;
            }

            // 关键位置：后台任务重新建 scope，避免请求结束后继续复用 scoped 数据库上下文。
            _ = Task.Run(() => ExecuteJobAsync(jobState), CancellationToken.None);
            return Task.FromResult(CreateSnapshot(jobState, false));
        }

        public Task<BatchUpdateSupplierImagesJobDto?> GetJobAsync(
            string jobId,
            CancellationToken cancellationToken = default
        )
        {
            CleanupExpiredJobs();
            return Task.FromResult(GetSnapshot(jobId, false));
        }

        private async Task ExecuteJobAsync(SupplierImageJobState jobState)
        {
            try
            {
                MarkJobRunning(jobState);

                using var scope = _serviceScopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IProductReactService>();
                var response = await service.BatchUpdateSupplierImagesAsync(CloneRequest(jobState.Request));

                CompleteJob(
                    jobState,
                    response.Success
                        ? BatchUpdateSupplierImagesJobStatusConstants.Succeeded
                        : BatchUpdateSupplierImagesJobStatusConstants.Failed,
                    response.Data,
                    response.Message,
                    response.Success ? null : response.Message
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行供应商商品图片批量更新 job 失败: {JobId}", jobState.JobId);
                CompleteJob(
                    jobState,
                    BatchUpdateSupplierImagesJobStatusConstants.Failed,
                    null,
                    ex.Message,
                    ex.Message
                );
            }
        }

        private void MarkJobRunning(SupplierImageJobState jobState)
        {
            lock (jobState.SyncRoot)
            {
                jobState.Status = BatchUpdateSupplierImagesJobStatusConstants.Running;
                jobState.StartedAt = _timeProvider.GetUtcNow().UtcDateTime;
                jobState.Message = "供应商商品图片批量更新任务执行中";
                jobState.ErrorMessage = null;
            }
        }

        private void CompleteJob(
            SupplierImageJobState jobState,
            string status,
            BatchUpdateSupplierImagesResult? result,
            string? message,
            string? errorMessage
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
                    jobState.ErrorMessage = errorMessage;
                }

                if (
                    _runningOperationJobIds.TryGetValue(jobState.OperationId, out var runningJobId)
                    && string.Equals(runningJobId, jobState.JobId, StringComparison.OrdinalIgnoreCase)
                )
                {
                    _runningOperationJobIds.TryRemove(jobState.OperationId, out _);
                }
                if (
                    _runningSupplierJobIds.TryGetValue(jobState.SupplierKey, out var supplierJobId)
                    && string.Equals(supplierJobId, jobState.JobId, StringComparison.OrdinalIgnoreCase)
                )
                {
                    _runningSupplierJobIds.TryRemove(jobState.SupplierKey, out _);
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
                    if (
                        _runningSupplierJobIds.TryGetValue(pair.Value.SupplierKey, out var supplierJobId)
                        && string.Equals(supplierJobId, pair.Key, StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        _runningSupplierJobIds.TryRemove(pair.Value.SupplierKey, out _);
                    }
                }
            }
        }

        private BatchUpdateSupplierImagesJobDto? GetRunningJobSnapshotNoLock(string operationId)
        {
            if (!_runningOperationJobIds.TryGetValue(operationId, out var jobId))
            {
                return null;
            }

            var snapshot = GetSnapshot(jobId, true);
            if (snapshot == null || snapshot.Status != BatchUpdateSupplierImagesJobStatusConstants.Running && snapshot.Status != BatchUpdateSupplierImagesJobStatusConstants.Queued)
            {
                _runningOperationJobIds.TryRemove(operationId, out _);
                return null;
            }

            return snapshot;
        }

        private BatchUpdateSupplierImagesJobDto? GetRunningSupplierJobSnapshotNoLock(string supplierKey)
        {
            if (string.IsNullOrWhiteSpace(supplierKey) || !_runningSupplierJobIds.TryGetValue(supplierKey, out var jobId))
            {
                return null;
            }

            var snapshot = GetSnapshot(jobId, true);
            if (snapshot == null || snapshot.Status != BatchUpdateSupplierImagesJobStatusConstants.Running && snapshot.Status != BatchUpdateSupplierImagesJobStatusConstants.Queued)
            {
                _runningSupplierJobIds.TryRemove(supplierKey, out _);
                return null;
            }

            return snapshot;
        }

        private BatchUpdateSupplierImagesJobDto? GetSnapshot(string jobId, bool isDuplicateRequest)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return null;
            }

            return _jobs.TryGetValue(jobId, out var jobState)
                ? CreateSnapshot(jobState, isDuplicateRequest)
                : null;
        }

        private static BatchUpdateSupplierImagesJobDto CreateSnapshot(
            SupplierImageJobState jobState,
            bool isDuplicateRequest
        )
        {
            lock (jobState.SyncRoot)
            {
                return new BatchUpdateSupplierImagesJobDto
                {
                    JobId = jobState.JobId,
                    OperationId = jobState.OperationId,
                    Status = jobState.Status,
                    IsDuplicateRequest = isDuplicateRequest,
                    Request = CloneRequest(jobState.Request),
                    Result = jobState.Result == null ? null : CloneResult(jobState.Result),
                    Message = jobState.Message,
                    ErrorMessage = jobState.ErrorMessage,
                    CreatedAt = jobState.CreatedAt,
                    StartedAt = jobState.StartedAt,
                    CompletedAt = jobState.CompletedAt,
                    ExpiresAt = jobState.ExpiresAt,
                };
            }
        }

        private static string NormalizeOperationId(
            string? operationId,
            BatchUpdateSupplierImagesRequest request
        )
        {
            if (!string.IsNullOrWhiteSpace(operationId))
            {
                return operationId.Trim();
            }

            return string.Join(
                "|",
                "supplier-images",
                request.LocalSupplierCode?.Trim() ?? string.Empty,
                request.UrlTemplate?.Trim() ?? string.Empty,
                request.UpdateHbweb ? "hbweb" : "no-hbweb",
                request.UpdateHq ? "hq" : "no-hq",
                request.SaveSupplierImageBaseUrl ? "save-base-url" : "no-save-base-url"
            );
        }

        private static string NormalizeSupplierKey(string? supplierCode)
        {
            return (supplierCode ?? string.Empty).Trim().ToUpperInvariant();
        }

        private static BatchUpdateSupplierImagesRequest CloneRequest(
            BatchUpdateSupplierImagesRequest? request
        )
        {
            return new BatchUpdateSupplierImagesRequest
            {
                LocalSupplierCode = request?.LocalSupplierCode ?? string.Empty,
                UrlTemplate = request?.UrlTemplate ?? string.Empty,
                UpdateHbweb = request?.UpdateHbweb ?? false,
                UpdateHq = request?.UpdateHq ?? false,
                SaveSupplierImageBaseUrl = request?.SaveSupplierImageBaseUrl ?? false,
            };
        }

        private static BatchUpdateSupplierImagesResult CloneResult(
            BatchUpdateSupplierImagesResult result
        )
        {
            return new BatchUpdateSupplierImagesResult
            {
                TotalCount = result.TotalCount,
                HbwebUpdatedCount = result.HbwebUpdatedCount,
                HbwebSkippedExistingImageCount = result.HbwebSkippedExistingImageCount,
                HqUpdatedCount = result.HqUpdatedCount,
                HqSkippedExistingImageCount = result.HqSkippedExistingImageCount,
                SkippedCount = result.SkippedCount,
                HqFailedCount = result.HqFailedCount,
                Errors = result.Errors.ToList(),
            };
        }

        private sealed class SupplierImageJobState
        {
            public object SyncRoot { get; } = new();
            public string JobId { get; init; } = string.Empty;
            public string OperationId { get; init; } = string.Empty;
            public string SupplierKey { get; init; } = string.Empty;
            public string Status { get; set; } = BatchUpdateSupplierImagesJobStatusConstants.Queued;
            public BatchUpdateSupplierImagesRequest Request { get; init; } = new();
            public BatchUpdateSupplierImagesResult? Result { get; set; }
            public string? Message { get; set; }
            public string? ErrorMessage { get; set; }
            public DateTime CreatedAt { get; init; }
            public DateTime? StartedAt { get; set; }
            public DateTime? CompletedAt { get; set; }
            public DateTime? ExpiresAt { get; set; }
        }
    }
}
