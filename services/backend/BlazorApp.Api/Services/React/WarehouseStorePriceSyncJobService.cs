using System.Collections.Concurrent;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.Performance;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services.React;

/// <summary>
/// 仓库价格同步的进程内后台任务，完成结果保留 45 分钟。
/// </summary>
public sealed class WarehouseStorePriceSyncJobService : IWarehouseStorePriceSyncJobService
{
    private static readonly TimeSpan DefaultCompletedRetention = TimeSpan.FromMinutes(45);

    private readonly ConcurrentDictionary<string, JobState> _jobs = new();
    private readonly ConcurrentDictionary<string, string> _runningOperationJobIds = new();
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<WarehouseStorePriceSyncJobService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _completedRetention;
    private readonly object _jobStartSyncRoot = new();
    private readonly SemaphoreSlim _executionLock = new(1, 1);

    public WarehouseStorePriceSyncJobService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<WarehouseStorePriceSyncJobService> logger
    )
        : this(serviceScopeFactory, logger, TimeProvider.System, DefaultCompletedRetention) { }

    public WarehouseStorePriceSyncJobService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<WarehouseStorePriceSyncJobService> logger,
        TimeProvider? timeProvider,
        TimeSpan? completedRetention
    )
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _completedRetention = completedRetention ?? DefaultCompletedRetention;
    }

    public Task<WarehouseStorePriceSyncJobDto> StartJobAsync(
        WarehouseStorePriceSyncRequestDto request,
        string updatedBy,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        CleanupExpiredJobs();
        var normalizedRequest = NormalizeRequest(request);
        ValidateRequest(normalizedRequest);
        var operationId = BuildOperationId(normalizedRequest);

        lock (_jobStartSyncRoot)
        {
            var duplicate = GetRunningSnapshotNoLock(operationId);
            if (duplicate != null)
            {
                duplicate.IsDuplicateRequest = true;
                return Task.FromResult(duplicate);
            }

            var state = new JobState
            {
                JobId = Guid.NewGuid().ToString("N"),
                OperationId = operationId,
                Request = normalizedRequest,
                UpdatedBy = string.IsNullOrWhiteSpace(updatedBy) ? "system" : updatedBy.Trim(),
                CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
                Message = "仓库价格同步任务已提交",
            };
            _jobs[state.JobId] = state;
            _runningOperationJobIds[operationId] = state.JobId;
            PerformanceOperationalRunBridge.Publish(
                PerformanceOperationalRunTransition.Queued(
                    state.JobId,
                    state.Request.SyncToHq ? "hq" : "background",
                    "warehouse-store-price-sync",
                    state.CreatedAt,
                    _runningOperationJobIds.Count
                )
            );
            // 关键位置：后台重新创建 scope，避免继续使用控制器请求生命周期内的数据库连接。
            _ = Task.Run(() => ExecuteJobAsync(state), CancellationToken.None);
            return Task.FromResult(CreateSnapshot(state, false));
        }
    }

    public Task<WarehouseStorePriceSyncJobDto?> GetJobAsync(
        string jobId,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        CleanupExpiredJobs();
        if (string.IsNullOrWhiteSpace(jobId) || !_jobs.TryGetValue(jobId.Trim(), out var state))
        {
            return Task.FromResult<WarehouseStorePriceSyncJobDto?>(null);
        }

        return Task.FromResult<WarehouseStorePriceSyncJobDto?>(CreateSnapshot(state, false));
    }

    private async Task ExecuteJobAsync(JobState state)
    {
        await _executionLock.WaitAsync();
        try
        {
            lock (state.SyncRoot)
            {
                // 不同请求也必须串行，避免全量与指定范围任务同时 upsert 同一业务键。
                state.Status = WarehouseStorePriceSyncJobStatusConstants.Running;
                state.Message = "仓库价格同步处理中";
            }
            PerformanceOperationalRunBridge.Publish(
                PerformanceOperationalRunTransition.Started(
                    state.JobId,
                    state.Request.SyncToHq ? "hq" : "background",
                    "warehouse-store-price-sync",
                    _timeProvider.GetUtcNow().UtcDateTime,
                    _runningOperationJobIds.Count
                )
            );

            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IWarehouseStorePriceSyncService>();
                var response = await service.ExecuteAsync(
                    CloneRequest(state.Request),
                    state.UpdatedBy,
                    CancellationToken.None
                );
                var result = response.Data
                    ?? response.Details as WarehouseStorePriceSyncResultDto
                    ?? new WarehouseStorePriceSyncResultDto();
                var hasSkippedOrErrors = result.SkippedProductCount > 0 || result.Errors.Count > 0;
                var status = response.Success
                    ? hasSkippedOrErrors
                        ? WarehouseStorePriceSyncJobStatusConstants.PartiallySucceeded
                        : WarehouseStorePriceSyncJobStatusConstants.Succeeded
                    : result.LocalCommitted && result.HqSucceeded == false
                        ? WarehouseStorePriceSyncJobStatusConstants.PartiallySucceeded
                        : WarehouseStorePriceSyncJobStatusConstants.Failed;
                CompleteJob(state, status, result, response.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "仓库价格同步后台任务失败: {JobId}", state.JobId);
                CompleteJob(
                    state,
                    WarehouseStorePriceSyncJobStatusConstants.Failed,
                    new WarehouseStorePriceSyncResultDto
                    {
                        Errors =
                        [
                            new WarehouseStorePriceSyncErrorDto
                            {
                                Stage = "JobExecution",
                                Code = "JOB_EXECUTION_FAILED",
                                Message = "后台任务执行失败",
                            },
                        ],
                    },
                    "仓库价格同步任务执行失败"
                );
            }
        }
        finally
        {
            _executionLock.Release();
        }
    }

    private void CompleteJob(
        JobState state,
        string status,
        WarehouseStorePriceSyncResultDto result,
        string? message
    )
    {
        DateTime completedAt;
        lock (_jobStartSyncRoot)
        {
            lock (state.SyncRoot)
            {
                completedAt = _timeProvider.GetUtcNow().UtcDateTime;
                state.Status = status;
                state.Result = CloneResult(result);
                state.Message = message;
                state.CompletedAt = completedAt;
                state.ExpiresAt = completedAt.Add(_completedRetention);
            }

            if (
                _runningOperationJobIds.TryGetValue(state.OperationId, out var runningJobId)
                && string.Equals(runningJobId, state.JobId, StringComparison.OrdinalIgnoreCase)
            )
            {
                _runningOperationJobIds.TryRemove(state.OperationId, out _);
            }
        }
        PerformanceOperationalRunBridge.Publish(
            PerformanceOperationalRunTransition.Completed(
                state.JobId,
                state.Request.SyncToHq ? "hq" : "background",
                "warehouse-store-price-sync",
                status,
                completedAt
            )
        );
    }

    private WarehouseStorePriceSyncJobDto? GetRunningSnapshotNoLock(string operationId)
    {
        if (!_runningOperationJobIds.TryGetValue(operationId, out var jobId))
        {
            return null;
        }

        if (!_jobs.TryGetValue(jobId, out var state))
        {
            _runningOperationJobIds.TryRemove(operationId, out _);
            return null;
        }

        var snapshot = CreateSnapshot(state, true);
        if (
            snapshot.Status != WarehouseStorePriceSyncJobStatusConstants.Pending
            && snapshot.Status != WarehouseStorePriceSyncJobStatusConstants.Running
        )
        {
            _runningOperationJobIds.TryRemove(operationId, out _);
            return null;
        }

        return snapshot;
    }

    private void CleanupExpiredJobs()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var pair in _jobs)
        {
            if (pair.Value.ExpiresAt is { } expiresAt && expiresAt <= now)
            {
                _jobs.TryRemove(pair.Key, out _);
            }
        }
    }

    private static WarehouseStorePriceSyncJobDto CreateSnapshot(
        JobState state,
        bool isDuplicateRequest
    )
    {
        lock (state.SyncRoot)
        {
            return new WarehouseStorePriceSyncJobDto
            {
                JobId = state.JobId,
                Status = state.Status,
                IsDuplicateRequest = isDuplicateRequest,
                CreatedAt = state.CreatedAt,
                CompletedAt = state.CompletedAt,
                Message = state.Message,
                Result = CloneResult(state.Result),
            };
        }
    }

    private static WarehouseStorePriceSyncRequestDto NormalizeRequest(
        WarehouseStorePriceSyncRequestDto? request
    )
    {
        request ??= new WarehouseStorePriceSyncRequestDto();
        return new WarehouseStorePriceSyncRequestDto
        {
            ApplyToAllProducts = request.ApplyToAllProducts,
            ProductCodes = NormalizeCodes(request.ProductCodes),
            TargetStoreCodes = NormalizeCodes(request.TargetStoreCodes),
            SyncToHq = request.SyncToHq,
        };
    }

    private static void ValidateRequest(WarehouseStorePriceSyncRequestDto request)
    {
        if (
            (request.ApplyToAllProducts && request.ProductCodes.Count > 0)
            || (!request.ApplyToAllProducts && request.ProductCodes.Count == 0)
        )
        {
            throw new ArgumentException(
                "全量处理时 ProductCodes 必须为空，指定处理时 ProductCodes 必须非空"
            );
        }

        if (request.TargetStoreCodes.Count == 0)
        {
            throw new ArgumentException("目标分店不能为空");
        }
    }

    private static string BuildOperationId(WarehouseStorePriceSyncRequestDto request)
    {
        return string.Join(
            "|",
            "warehouse-store-price-sync",
            request.ApplyToAllProducts ? "ALL" : string.Join(",", request.ProductCodes
                .Select(code => code.ToUpperInvariant())
                .OrderBy(code => code, StringComparer.Ordinal)),
            string.Join(",", request.TargetStoreCodes
                .Select(code => code.ToUpperInvariant())
                .OrderBy(code => code, StringComparer.Ordinal)),
            request.SyncToHq ? "HQ" : "LOCAL"
        );
    }

    private static List<string> NormalizeCodes(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static WarehouseStorePriceSyncRequestDto CloneRequest(
        WarehouseStorePriceSyncRequestDto request
    )
    {
        return new WarehouseStorePriceSyncRequestDto
        {
            ProductCodes = request.ProductCodes.ToList(),
            ApplyToAllProducts = request.ApplyToAllProducts,
            TargetStoreCodes = request.TargetStoreCodes.ToList(),
            SyncToHq = request.SyncToHq,
        };
    }

    private static WarehouseStorePriceSyncResultDto? CloneResult(
        WarehouseStorePriceSyncResultDto? result
    )
    {
        if (result == null)
        {
            return null;
        }

        return new WarehouseStorePriceSyncResultDto
        {
            RequestedProductCount = result.RequestedProductCount,
            EligibleProductCount = result.EligibleProductCount,
            SkippedProductCount = result.SkippedProductCount,
            TargetStoreCount = result.TargetStoreCount,
            LocalCreatedCount = result.LocalCreatedCount,
            LocalUpdatedCount = result.LocalUpdatedCount,
            HqCreatedCount = result.HqCreatedCount,
            HqUpdatedCount = result.HqUpdatedCount,
            HqProvisionedProductCount = result.HqProvisionedProductCount,
            LocalCommitted = result.LocalCommitted,
            HqSucceeded = result.HqSucceeded,
            TargetStoreCodes = result.TargetStoreCodes.ToList(),
            Errors = result.Errors.Select(error => new WarehouseStorePriceSyncErrorDto
            {
                Stage = error.Stage,
                ProductCode = error.ProductCode,
                StoreCode = error.StoreCode,
                Code = error.Code,
                Message = error.Message,
            }).ToList(),
        };
    }

    private sealed class JobState
    {
        public object SyncRoot { get; } = new();
        public string JobId { get; init; } = string.Empty;
        public string OperationId { get; init; } = string.Empty;
        public WarehouseStorePriceSyncRequestDto Request { get; init; } = new();
        public string UpdatedBy { get; init; } = "system";
        public string Status { get; set; } = WarehouseStorePriceSyncJobStatusConstants.Pending;
        public DateTime CreatedAt { get; init; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? Message { get; set; }
        public WarehouseStorePriceSyncResultDto? Result { get; set; }
    }
}
