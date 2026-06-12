using System.Collections.Concurrent;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorApp.Api.Services.React
{
    public sealed class ProductStoreSyncJobConcurrencyLimitExceededException : InvalidOperationException
    {
        public ProductStoreSyncJobConcurrencyLimitExceededException(string message)
            : base(message) { }

        public string ErrorCode { get; } = "PRODUCT_STORE_SYNC_JOB_LIMIT_EXCEEDED";
    }

    /// <summary>
    /// 商品同步到分店后台任务服务。
    /// 这是单实例进程内轻量 job，只用于当前 API 进程内去重和限流，不保证服务重启后恢复，也不保证多实例之间互斥。
    /// </summary>
    public class ProductStoreSyncJobService : IProductStoreSyncJobService
    {
        private static readonly TimeSpan DefaultCompletedRetention = TimeSpan.FromMinutes(45);
        private const int MaxConcurrentRunningOperations = 2;
        private const string JobExecutionFailedMessage = "商品同步任务执行失败，请稍后重试或联系管理员";

        private readonly ConcurrentDictionary<string, ProductStoreSyncJobState> _jobs = new();
        private readonly ConcurrentDictionary<string, string> _runningOperationJobIds = new();
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<ProductStoreSyncJobService> _logger;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _completedRetention;
        private readonly object _jobStartSyncRoot = new();

        public ProductStoreSyncJobService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<ProductStoreSyncJobService> logger
        )
            : this(serviceScopeFactory, logger, TimeProvider.System, DefaultCompletedRetention) { }

        public ProductStoreSyncJobService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<ProductStoreSyncJobService> logger,
            TimeProvider? timeProvider = null,
            TimeSpan? completedRetention = null
        )
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _completedRetention = completedRetention ?? DefaultCompletedRetention;
        }

        public Task<SyncProductsToStoresJobDto> StartJobAsync(
            SyncProductsToStoresRequest request,
            CancellationToken cancellationToken = default
        )
        {
            CleanupExpiredJobs();

            var normalizedRequest = CloneRequest(request);
            normalizedRequest.NormalizeFieldSelection();
            var operationId = BuildOperationId(normalizedRequest);
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var jobState = new ProductStoreSyncJobState
            {
                JobId = Guid.NewGuid().ToString("N"),
                OperationId = operationId,
                Request = normalizedRequest,
                CreatedAt = now,
                Message = "商品同步到分店任务已提交",
            };

            lock (_jobStartSyncRoot)
            {
                var duplicate = GetRunningJobSnapshotNoLock(operationId);
                if (duplicate != null)
                {
                    duplicate.IsDuplicateRequest = true;
                    return Task.FromResult(duplicate);
                }

                if (_runningOperationJobIds.Count >= MaxConcurrentRunningOperations)
                {
                    throw new ProductStoreSyncJobConcurrencyLimitExceededException(
                        "商品同步到分店任务正在处理较多请求，请稍后重试"
                    );
                }

                // 关键位置：先登记 job 和 operationId，再启动后台任务，避免前端重复点击创建多个写任务。
                _jobs[jobState.JobId] = jobState;
                _runningOperationJobIds[operationId] = jobState.JobId;
            }

            // 关键位置：后台任务重新创建 scope，避免请求结束后继续复用 scoped 服务。
            _ = Task.Run(() => ExecuteJobAsync(jobState), CancellationToken.None);
            return Task.FromResult(CreateSnapshot(jobState, false));
        }

        public Task<SyncProductsToStoresJobDto?> GetJobAsync(
            string jobId,
            CancellationToken cancellationToken = default
        )
        {
            CleanupExpiredJobs();
            return Task.FromResult(GetSnapshot(jobId, false));
        }

        private async Task ExecuteJobAsync(ProductStoreSyncJobState jobState)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IProductStoreSyncService>();
                var response = await service.SyncProductsToStoresAsync(CloneRequest(jobState.Request));

                var status = response.Success
                    ? ProductStoreSyncJobStatusConstants.Succeeded
                    : ProductStoreSyncJobStatusConstants.Failed;
                CompleteJob(jobState, status, BuildResult(response), response.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行商品同步到分店 job 失败: {JobId}", jobState.JobId);
                var safeMessage = $"{JobExecutionFailedMessage}，JobId: {jobState.JobId}";
                CompleteJob(
                    jobState,
                    ProductStoreSyncJobStatusConstants.Failed,
                    new SyncProductsToStoresResult
                    {
                        FailedCount = 1,
                        Errors = [safeMessage],
                    },
                    safeMessage
                );
            }
        }

        private void CompleteJob(
            ProductStoreSyncJobState jobState,
            string status,
            SyncProductsToStoresResult result,
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

        private SyncProductsToStoresJobDto? GetRunningJobSnapshotNoLock(string operationId)
        {
            if (!_runningOperationJobIds.TryGetValue(operationId, out var jobId))
            {
                return null;
            }

            var snapshot = GetSnapshot(jobId, true);
            if (snapshot == null || snapshot.Status != ProductStoreSyncJobStatusConstants.Running)
            {
                _runningOperationJobIds.TryRemove(operationId, out _);
                return null;
            }

            return snapshot;
        }

        private SyncProductsToStoresJobDto? GetSnapshot(string jobId, bool isDuplicateRequest)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return null;
            }

            return _jobs.TryGetValue(jobId, out var jobState)
                ? CreateSnapshot(jobState, isDuplicateRequest)
                : null;
        }

        private static SyncProductsToStoresJobDto CreateSnapshot(
            ProductStoreSyncJobState jobState,
            bool isDuplicateRequest
        )
        {
            lock (jobState.SyncRoot)
            {
                return new SyncProductsToStoresJobDto
                {
                    JobId = jobState.JobId,
                    OperationId = jobState.OperationId,
                    Status = jobState.Status,
                    ProductCodes = jobState.Request.ProductCodes.ToList(),
                    StoreCodes = jobState.Request.StoreCodes.ToList(),
                    IsDuplicateRequest = isDuplicateRequest,
                    Result = jobState.Result,
                    Message = jobState.Message,
                    CreatedAt = jobState.CreatedAt,
                    CompletedAt = jobState.CompletedAt,
                    ExpiresAt = jobState.ExpiresAt,
                };
            }
        }

        private static SyncProductsToStoresResult BuildResult(
            ApiResponse<SyncProductsToStoresResult> response
        )
        {
            var result = response.Data ?? response.Details as SyncProductsToStoresResult ?? new SyncProductsToStoresResult();
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

        private static string BuildOperationId(SyncProductsToStoresRequest request)
        {
            return string.Join(
                "|",
                "sync-products-to-stores",
                JoinSorted(request.ProductCodes),
                JoinSorted(request.StoreCodes),
                request.SyncPurchasePrice,
                request.SyncRetailPrice,
                request.SyncIsAutoPricing,
                request.SyncIsSpecialProduct,
                request.SyncDiscountRate
            );
        }

        private static string JoinSorted(IEnumerable<string>? values)
        {
            return string.Join(",", NormalizeValues(values));
        }

        private static List<string> NormalizeValues(IEnumerable<string>? values)
        {
            return (values ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value)
                .ToList();
        }

        private static SyncProductsToStoresRequest CloneRequest(SyncProductsToStoresRequest request)
        {
            return new SyncProductsToStoresRequest
            {
                ProductCodes = request.ProductCodes?.ToList() ?? new List<string>(),
                StoreCodes = request.StoreCodes?.ToList() ?? new List<string>(),
                Fields = request.Fields?.ToList() ?? new List<string>(),
                SyncPurchasePrice = request.SyncPurchasePrice,
                SyncRetailPrice = request.SyncRetailPrice,
                SyncIsAutoPricing = request.SyncIsAutoPricing,
                SyncIsSpecialProduct = request.SyncIsSpecialProduct,
                SyncDiscountRate = request.SyncDiscountRate,
            };
        }

        private sealed class ProductStoreSyncJobState
        {
            public object SyncRoot { get; } = new();
            public string JobId { get; init; } = string.Empty;
            public string OperationId { get; init; } = string.Empty;
            public SyncProductsToStoresRequest Request { get; init; } = new();
            public string Status { get; set; } = ProductStoreSyncJobStatusConstants.Running;
            public SyncProductsToStoresResult? Result { get; set; }
            public string? Message { get; set; }
            public DateTime CreatedAt { get; init; }
            public DateTime? CompletedAt { get; set; }
            public DateTime? ExpiresAt { get; set; }
        }
    }
}
