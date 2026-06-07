using System.Collections.Concurrent;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 商品推送 HQ 后台任务服务。
    /// </summary>
    public class ProductPushToHqJobService : IProductPushToHqJobService
    {
        private static readonly TimeSpan DefaultCompletedRetention = TimeSpan.FromMinutes(45);

        private readonly ConcurrentDictionary<string, ProductPushToHqJobState> _jobs = new();
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<ProductPushToHqJobService> _logger;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _completedRetention;
        private readonly object _jobStartSyncRoot = new();

        public ProductPushToHqJobService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<ProductPushToHqJobService> logger
        )
            : this(serviceScopeFactory, logger, TimeProvider.System, DefaultCompletedRetention) { }

        public ProductPushToHqJobService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<ProductPushToHqJobService> logger,
            TimeProvider? timeProvider = null,
            TimeSpan? completedRetention = null
        )
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _completedRetention = completedRetention ?? DefaultCompletedRetention;
        }

        public Task<PushProductsToHqJobDto> StartJobAsync(
            PushProductsToHqRequest request,
            CancellationToken cancellationToken = default
        )
        {
            CleanupExpiredJobs();

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var jobState = new ProductPushToHqJobState
            {
                JobId = Guid.NewGuid().ToString("N"),
                CreatedAt = now,
                Status = ProductPushToHqJobStatusConstants.Running,
                Message = "商品推送 HQ 任务已提交",
                Request = request,
            };

            lock (_jobStartSyncRoot)
            {
                // 关键位置：先登记 job，再启动后台任务，保证前端提交后可以立即查到状态。
                _jobs[jobState.JobId] = jobState;
            }

            // 关键位置：后台任务重新创建作用域，避免复用已结束请求里的 scoped 服务。
            _ = Task.Run(() => ExecuteJobAsync(jobState), CancellationToken.None);
            return Task.FromResult(CreateSnapshot(jobState, false));
        }

        public Task<PushProductsToHqJobDto?> GetJobAsync(
            string jobId,
            CancellationToken cancellationToken = default
        )
        {
            CleanupExpiredJobs();
            return Task.FromResult(GetSnapshot(jobId, false));
        }

        private async Task ExecuteJobAsync(ProductPushToHqJobState jobState)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<IProductHqSyncService>();
                _logger.LogInformation("执行商品推送 HQ job: {JobId}", jobState.JobId);

                var response = await syncService.PushToHqAsync(jobState.Request);
                var result = response.Data ?? response.Details as PushProductsToHqResult;
                if (result == null)
                {
                    result = new PushProductsToHqResult
                    {
                        FailedCount = 1,
                        TotalCount = 1,
                    };
                    if (!string.IsNullOrWhiteSpace(response.Message))
                    {
                        result.Errors.Add(response.Message);
                    }
                }

                CompleteJob(
                    jobState,
                    response.Success
                        ? ProductPushToHqJobStatusConstants.Succeeded
                        : ProductPushToHqJobStatusConstants.Failed,
                    response.Message,
                    result
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行商品推送 HQ job 失败: {JobId}", jobState.JobId);
                CompleteJob(
                    jobState,
                    ProductPushToHqJobStatusConstants.Failed,
                    ex.Message,
                    new PushProductsToHqResult
                    {
                        FailedCount = 1,
                        TotalCount = 1,
                        Errors = new List<string> { ex.Message },
                    }
                );
            }
        }

        private void CompleteJob(
            ProductPushToHqJobState jobState,
            string status,
            string? message,
            PushProductsToHqResult result
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
                    jobState.Message = message;
                    jobState.Result = result;
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

        private PushProductsToHqJobDto? GetSnapshot(string jobId, bool isDuplicateRequest)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return null;
            }

            return _jobs.TryGetValue(jobId, out var jobState)
                ? CreateSnapshot(jobState, isDuplicateRequest)
                : null;
        }

        private static PushProductsToHqJobDto CreateSnapshot(
            ProductPushToHqJobState jobState,
            bool isDuplicateRequest
        )
        {
            lock (jobState.SyncRoot)
            {
                return new PushProductsToHqJobDto
                {
                    JobId = jobState.JobId,
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

        private sealed class ProductPushToHqJobState
        {
            public object SyncRoot { get; } = new();
            public string JobId { get; init; } = string.Empty;
            public string Status { get; set; } = ProductPushToHqJobStatusConstants.Running;
            public DateTime CreatedAt { get; init; }
            public DateTime? CompletedAt { get; set; }
            public DateTime? ExpiresAt { get; set; }
            public string? Message { get; set; }
            public PushProductsToHqResult? Result { get; set; }
            public PushProductsToHqRequest Request { get; init; } = new();
        }
    }
}
