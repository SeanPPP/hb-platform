using System.Collections.Concurrent;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorApp.Api.Services.React
{
    /// <summary>
    /// 分店订货缺失订单同步 job 服务。
    /// 使用进程内字典保存运行状态，并按用户 + 分店集合做运行中去重。
    /// </summary>
    public class StoreOrderSyncJobService : IStoreOrderSyncJobService
    {
        private static readonly TimeSpan DefaultCompletedRetention = TimeSpan.FromMinutes(45);

        private readonly ConcurrentDictionary<string, StoreOrderSyncJobState> _jobs = new();
        private readonly ConcurrentDictionary<string, string> _runningJobKeys = new();
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<StoreOrderSyncJobService> _logger;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _completedRetention;
        private readonly object _jobStartSyncRoot = new();
        private string? _activeWriteJobId;

        public StoreOrderSyncJobService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<StoreOrderSyncJobService> logger
        )
            : this(serviceScopeFactory, logger, TimeProvider.System, DefaultCompletedRetention) { }

        public StoreOrderSyncJobService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<StoreOrderSyncJobService> logger,
            TimeProvider? timeProvider = null,
            TimeSpan? completedRetention = null
        )
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _completedRetention = completedRetention ?? DefaultCompletedRetention;
        }

        public Task<StoreOrderSyncJobDto> StartJobAsync(
            string userId,
            SyncMissingOrdersRequestDto? request,
            CancellationToken cancellationToken = default
        )
        {
            CleanupExpiredJobs();

            var normalizedRequest = NormalizeRequest(request);
            var dedupeKey = BuildDedupeKey(
                userId,
                mode: null,
                normalizedRequest.StoreCodes,
                startDate: null,
                endDate: null
            );
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var jobState = new StoreOrderSyncJobState
            {
                JobId = Guid.NewGuid().ToString("N"),
                UserId = userId,
                DedupeKey = dedupeKey,
                Request = normalizedRequest,
                StoreCodes = normalizedRequest.StoreCodes?.ToList() ?? new List<string>(),
                CreatedAt = now,
                Status = StoreOrderSyncJobStatusConstants.Running,
            };

            lock (_jobStartSyncRoot)
            {
                var activeWriteJob = GetActiveWriteJobSnapshotNoLock(dedupeKey);
                if (activeWriteJob != null)
                {
                    return Task.FromResult(activeWriteJob);
                }

                if (_runningJobKeys.TryGetValue(dedupeKey, out var existingJobId))
                {
                    var existingJob = GetSnapshot(existingJobId, true);
                    if (existingJob?.Status == StoreOrderSyncJobStatusConstants.Running)
                    {
                        return Task.FromResult(existingJob);
                    }

                    _runningJobKeys.TryRemove(dedupeKey, out _);
                }

                // 关键位置：先登记 job，再登记运行中 key，避免并发请求看见 key 却拿不到 job 快照。
                _jobs[jobState.JobId] = jobState;
                _runningJobKeys[dedupeKey] = jobState.JobId;
                _activeWriteJobId = jobState.JobId;
            }

            // 关键位置：后台任务必须重新创建作用域，避免复用请求结束后的 scoped 服务。
            _ = Task.Run(() => ExecuteJobAsync(jobState), CancellationToken.None);
            return Task.FromResult(CreateSnapshot(jobState, false));
        }

        public Task<StoreOrderSyncJobDto> StartHqSyncJobAsync(
            string userId,
            StoreOrderHqSyncMode mode,
            StoreOrderHqSyncRequestDto? request,
            CancellationToken cancellationToken = default
        )
        {
            CleanupExpiredJobs();

            var normalizedRequest = NormalizeHqRequest(request);
            var dedupeKey = BuildDedupeKey(
                userId,
                mode.ToString(),
                normalizedRequest.StoreCodes,
                normalizedRequest.StartDate,
                normalizedRequest.EndDate,
                mode == StoreOrderHqSyncMode.Full ? null : normalizedRequest.ConflictStrategy
            );
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var jobState = new StoreOrderSyncJobState
            {
                JobId = Guid.NewGuid().ToString("N"),
                UserId = userId,
                DedupeKey = dedupeKey,
                HqRequest = normalizedRequest,
                HqMode = mode,
                StoreCodes = normalizedRequest.StoreCodes?.ToList() ?? new List<string>(),
                StartDate = normalizedRequest.StartDate,
                EndDate = normalizedRequest.EndDate,
                ConflictStrategy = mode == StoreOrderHqSyncMode.Full
                    ? null
                    : normalizedRequest.ConflictStrategy,
                CreatedAt = now,
                Status = StoreOrderSyncJobStatusConstants.Running,
            };

            lock (_jobStartSyncRoot)
            {
                var activeWriteJob = GetActiveWriteJobSnapshotNoLock(dedupeKey);
                if (activeWriteJob != null)
                {
                    return Task.FromResult(activeWriteJob);
                }

                if (_runningJobKeys.TryGetValue(dedupeKey, out var existingJobId))
                {
                    var existingJob = GetSnapshot(existingJobId, true);
                    if (existingJob?.Status == StoreOrderSyncJobStatusConstants.Running)
                    {
                        return Task.FromResult(existingJob);
                    }

                    _runningJobKeys.TryRemove(dedupeKey, out _);
                }

                _jobs[jobState.JobId] = jobState;
                _runningJobKeys[dedupeKey] = jobState.JobId;
                _activeWriteJobId = jobState.JobId;
            }

            _ = Task.Run(() => ExecuteJobAsync(jobState), CancellationToken.None);
            return Task.FromResult(CreateSnapshot(jobState, false));
        }

        public Task<StoreOrderSyncJobDto?> GetJobAsync(
            string jobId,
            CancellationToken cancellationToken = default
        )
        {
            CleanupExpiredJobs();
            return Task.FromResult(GetSnapshot(jobId, false));
        }

        private async Task ExecuteJobAsync(StoreOrderSyncJobState jobState)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                SyncMissingOrdersResultDto result;
                if (jobState.HqMode.HasValue)
                {
                    var syncService = scope.ServiceProvider.GetRequiredService<IStoreOrderHqSyncService>();
                    _logger.LogInformation(
                        "执行分店订货 HQ 同步 job: {JobId}, 模式 {Mode}",
                        jobState.JobId,
                        jobState.HqMode.Value
                    );
                    result = await syncService.SyncAsync(
                        jobState.HqMode.Value,
                        jobState.HqRequest,
                        jobState.JobId
                    );
                }
                else
                {
                    var syncService = scope.ServiceProvider.GetRequiredService<IStoreOrderReactService>();
                    result = await syncService.SyncMissingOrdersFromHqAsync(jobState.Request);
                }

                CompleteJob(
                    jobState,
                    result.Success
                        ? StoreOrderSyncJobStatusConstants.Succeeded
                        : StoreOrderSyncJobStatusConstants.Failed,
                    result
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行分店订货缺失订单同步 job 失败: {JobId}", jobState.JobId);
                CompleteJob(
                    jobState,
                    StoreOrderSyncJobStatusConstants.Failed,
                    new SyncMissingOrdersResultDto
                    {
                        Success = false,
                        Message = ex.Message,
                    }
                );
            }
        }

        private void CompleteJob(
            StoreOrderSyncJobState jobState,
            string status,
            SyncMissingOrdersResultDto result
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
                }

                if (
                    _runningJobKeys.TryGetValue(jobState.DedupeKey, out var runningJobId)
                    && runningJobId == jobState.JobId
                )
                {
                    _runningJobKeys.TryRemove(jobState.DedupeKey, out _);
                }

                if (string.Equals(_activeWriteJobId, jobState.JobId, StringComparison.OrdinalIgnoreCase))
                {
                    _activeWriteJobId = null;
                }
            }
        }

        private void CleanupExpiredJobs()
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            foreach (var pair in _jobs)
            {
                var jobState = pair.Value;
                DateTime? expiresAt;
                string status;

                lock (jobState.SyncRoot)
                {
                    expiresAt = jobState.ExpiresAt;
                    status = jobState.Status;
                }

                if (
                    status != StoreOrderSyncJobStatusConstants.Running
                    && expiresAt.HasValue
                    && expiresAt.Value <= now
                )
                {
                    _jobs.TryRemove(pair.Key, out _);
                    if (string.Equals(_activeWriteJobId, pair.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        _activeWriteJobId = null;
                    }
                }
            }
        }

        private StoreOrderSyncJobDto? GetActiveWriteJobSnapshotNoLock(string currentDedupeKey)
        {
            if (string.IsNullOrWhiteSpace(_activeWriteJobId))
            {
                return null;
            }

            if (!_jobs.TryGetValue(_activeWriteJobId, out var activeState))
            {
                _activeWriteJobId = null;
                return null;
            }

            lock (activeState.SyncRoot)
            {
                if (activeState.Status != StoreOrderSyncJobStatusConstants.Running)
                {
                    _activeWriteJobId = null;
                    return null;
                }

                if (
                    !string.Equals(
                        activeState.DedupeKey,
                        currentDedupeKey,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    throw new InvalidOperationException("已有分店订货同步任务正在运行，请稍后再试");
                }

                // 分店订货主明细写入属于同一互斥族：同一请求返回已有 job，不同请求直接阻止。
                return CreateSnapshot(activeState, isDuplicateRequest: true);
            }
        }

        private StoreOrderSyncJobDto? GetSnapshot(string jobId, bool isDuplicateRequest)
        {
            return _jobs.TryGetValue(jobId, out var jobState)
                ? CreateSnapshot(jobState, isDuplicateRequest)
                : null;
        }

        private static SyncMissingOrdersRequestDto NormalizeRequest(
            SyncMissingOrdersRequestDto? request
        )
        {
            var normalizedStoreCodes = (request?.StoreCodes ?? new List<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (
                normalizedStoreCodes.Count == 0
                && !string.IsNullOrWhiteSpace(request?.StoreCode)
            )
            {
                normalizedStoreCodes.Add(request.StoreCode.Trim());
            }

            return new SyncMissingOrdersRequestDto
            {
                StoreCode = normalizedStoreCodes.Count == 1 ? normalizedStoreCodes[0] : null,
                StoreCodes = normalizedStoreCodes,
            };
        }

        private static StoreOrderHqSyncRequestDto NormalizeHqRequest(StoreOrderHqSyncRequestDto? request)
        {
            var normalizedStoreCodes = (request?.StoreCodes ?? new List<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (
                normalizedStoreCodes.Count == 0
                && !string.IsNullOrWhiteSpace(request?.StoreCode)
            )
            {
                normalizedStoreCodes.Add(request.StoreCode.Trim());
            }

            var normalizedConflictStrategy = NormalizeConflictStrategy(request?.ConflictStrategy);

            return new StoreOrderHqSyncRequestDto
            {
                StoreCode = normalizedStoreCodes.Count == 1 ? normalizedStoreCodes[0] : null,
                StoreCodes = normalizedStoreCodes,
                StartDate = request?.StartDate,
                EndDate = request?.EndDate,
                // 关键位置：全量同步行为和去重忽略策略，归一化值仅用于保留请求快照。
                ConflictStrategy = normalizedConflictStrategy,
            };
        }

        private static string BuildDedupeKey(
            string userId,
            string? mode,
            List<string>? storeCodes,
            DateTime? startDate,
            DateTime? endDate,
            StoreOrderHqSyncConflictStrategy? conflictStrategy = null
        )
        {
            var normalizedUserId = string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId.Trim();
            var normalizedStores = storeCodes?.Count > 0 ? string.Join("|", storeCodes) : "__ALL__";
            var normalizedMode = string.IsNullOrWhiteSpace(mode) ? "Missing" : mode.Trim();
            var normalizedStart = startDate?.ToUniversalTime().ToString("O") ?? "__NO_START__";
            var normalizedEnd = endDate?.ToUniversalTime().ToString("O") ?? "__NO_END__";
            var normalizedConflictStrategy = conflictStrategy?.ToString() ?? "__NO_CONFLICT_STRATEGY__";
            return $"{normalizedUserId}::{normalizedMode}::{normalizedStores}::{normalizedStart}::{normalizedEnd}::{normalizedConflictStrategy}";
        }

        private static StoreOrderSyncJobDto CreateSnapshot(
            StoreOrderSyncJobState jobState,
            bool isDuplicateRequest
        )
        {
            lock (jobState.SyncRoot)
            {
                return new StoreOrderSyncJobDto
                {
                    JobId = jobState.JobId,
                    Status = jobState.Status,
                    Mode = jobState.HqMode?.ToString(),
                    ConflictStrategy = jobState.ConflictStrategy,
                    StoreCodes = jobState.StoreCodes.ToList(),
                    StartDate = jobState.StartDate,
                    EndDate = jobState.EndDate,
                    IsDuplicateRequest = isDuplicateRequest,
                    CreatedAt = jobState.CreatedAt,
                    CompletedAt = jobState.CompletedAt,
                    ExpiresAt = jobState.ExpiresAt,
                    Result = jobState.Result == null
                        ? null
                        : new SyncMissingOrdersResultDto
                        {
                            Success = jobState.Result.Success,
                            Message = jobState.Result.Message,
                            OrdersSynced = jobState.Result.OrdersSynced,
                            DetailsSynced = jobState.Result.DetailsSynced,
                            OrdersUpdated = jobState.Result.OrdersUpdated,
                            DetailsUpdated = jobState.Result.DetailsUpdated,
                            OrdersSoftDeleted = jobState.Result.OrdersSoftDeleted,
                            DetailsSoftDeleted = jobState.Result.DetailsSoftDeleted,
                            SkippedOrdersBecauseLocalNewer = jobState.Result.SkippedOrdersBecauseLocalNewer,
                            SkippedDetailsBecauseLocalNewer = jobState.Result.SkippedDetailsBecauseLocalNewer,
                            HqOrderCount = jobState.Result.HqOrderCount,
                            HqDetailCount = jobState.Result.HqDetailCount,
                            ShadowRowCount = jobState.Result.ShadowRowCount,
                            DurationMs = jobState.Result.DurationMs,
                            Mode = jobState.Result.Mode,
                            ConflictStrategy = jobState.Result.ConflictStrategy,
                            RunId = jobState.Result.RunId,
                            Errors = jobState.Result.Errors.ToList(),
                        },
                };
            }
        }

        private static StoreOrderHqSyncConflictStrategy NormalizeConflictStrategy(
            StoreOrderHqSyncConflictStrategy? conflictStrategy
        )
        {
            return conflictStrategy.HasValue
                && Enum.IsDefined(typeof(StoreOrderHqSyncConflictStrategy), conflictStrategy.Value)
                ? conflictStrategy.Value
                : StoreOrderHqSyncConflictStrategy.LatestWins;
        }

        private sealed class StoreOrderSyncJobState
        {
            public object SyncRoot { get; } = new();

            public string JobId { get; set; } = string.Empty;

            public string UserId { get; set; } = string.Empty;

            public string DedupeKey { get; set; } = string.Empty;

            public SyncMissingOrdersRequestDto Request { get; set; } =
                new SyncMissingOrdersRequestDto();

            public StoreOrderHqSyncRequestDto? HqRequest { get; set; }

            public StoreOrderHqSyncMode? HqMode { get; set; }

            public List<string> StoreCodes { get; set; } = new();

            public DateTime? StartDate { get; set; }

            public DateTime? EndDate { get; set; }

            public StoreOrderHqSyncConflictStrategy? ConflictStrategy { get; set; }

            public string Status { get; set; } = StoreOrderSyncJobStatusConstants.Running;

            public DateTime CreatedAt { get; set; }

            public DateTime? CompletedAt { get; set; }

            public DateTime? ExpiresAt { get; set; }

            public SyncMissingOrdersResultDto? Result { get; set; }
        }
    }
}
