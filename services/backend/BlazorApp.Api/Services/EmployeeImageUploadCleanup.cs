using BlazorApp.Api.Data;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Services
{
    public static class EmployeeImageUploadCleanup
    {
        public static async Task CleanupExpiredAsync(
            ISqlSugarClient db,
            TencentCloudUploadService storage,
            ILogger logger,
            DateTime utcNow,
            CancellationToken cancellationToken
        )
        {
            var stageTimeout = utcNow.AddMinutes(-15);
            var tickets = await db.Queryable<EmployeeImageUploadTicket>()
                .Where(item =>
                    (item.Status == EmployeeImageUploadStatus.Pending && item.ExpiresAt <= utcNow)
                    || ((item.Status == EmployeeImageUploadStatus.Processing
                            || item.Status == EmployeeImageUploadStatus.Promoted
                            || item.Status == EmployeeImageUploadStatus.Cleaning)
                        && item.StageChangedAt != null
                        && item.StageChangedAt <= stageTimeout)
                    || (item.Status == EmployeeImageUploadStatus.Completed
                        && (item.PreviousObjectCleanupStatus == EmployeeImageObjectCleanupStatus.Pending
                            || (item.PreviousObjectCleanupStatus == EmployeeImageObjectCleanupStatus.Processing
                                && item.PreviousObjectCleanupStartedAt != null
                                && item.PreviousObjectCleanupStartedAt <= stageTimeout))))
                .Take(100)
                .ToListAsync(cancellationToken);
            foreach (var candidate in tickets)
            {
                await using var mediaLock = await EmployeeProfileMediaLock.AcquireAsync(
                    db,
                    candidate.UserGUID,
                    candidate.Kind,
                    logger,
                    cancellationToken
                );
                // 取锁后必须重读，候选列表只能用于定位，不能作为删除依据。
                var ticket = await db.Queryable<EmployeeImageUploadTicket>()
                    .FirstAsync(item => item.PendingObjectKey == candidate.PendingObjectKey);
                if (ticket is null)
                {
                    continue;
                }
                var profile = await db.Queryable<EmployeeProfile>()
                    .FirstAsync(item => item.UserGUID == ticket.UserGUID && !item.IsDeleted);

                if (ticket.Status == EmployeeImageUploadStatus.Completed)
                {
                    await CleanupPreviousObjectAsync(
                        db,
                        storage,
                        logger,
                        ticket,
                        profile,
                        utcNow,
                        cancellationToken
                    );
                    continue;
                }

                var isExpired = ticket.Status == EmployeeImageUploadStatus.Pending
                    ? ticket.ExpiresAt <= utcNow
                    : ticket.StageChangedAt is DateTime changedAt && changedAt <= stageTimeout;
                if (!isExpired)
                {
                    continue;
                }

                var finalIsReferenced = IsObjectReferenced(
                    profile,
                    storage,
                    ticket.Kind,
                    ticket.FinalObjectKey
                );
                if (finalIsReferenced
                    && (ticket.Status == EmployeeImageUploadStatus.Promoted
                        || ticket.Status == EmployeeImageUploadStatus.Processing
                        || ticket.Status == EmployeeImageUploadStatus.Cleaning))
                {
                    // promote 后进程中断，但数据库已稳定关联时只需完成票据。
                    var recovered = await db.Updateable<EmployeeImageUploadTicket>()
                        .SetColumns(item => new EmployeeImageUploadTicket
                        {
                            Status = EmployeeImageUploadStatus.Completed,
                            CompletedAt = utcNow,
                            StageChangedAt = utcNow,
                        })
                        .Where(item => item.PendingObjectKey == ticket.PendingObjectKey
                            && item.Status == ticket.Status)
                        .ExecuteCommandAsync(cancellationToken);
                    if (recovered == 1)
                    {
                        _ = await storage.DeleteObjectAsync(ticket.PendingObjectKey, cancellationToken);
                        ticket.Status = EmployeeImageUploadStatus.Completed;
                        await CleanupPreviousObjectAsync(
                            db,
                            storage,
                            logger,
                            ticket,
                            profile,
                            utcNow,
                            cancellationToken
                        );
                    }
                    continue;
                }

                if (ticket.Status != EmployeeImageUploadStatus.Cleaning)
                {
                    var claimed = await db.Updateable<EmployeeImageUploadTicket>()
                        .SetColumns(item => new EmployeeImageUploadTicket
                        {
                            Status = EmployeeImageUploadStatus.Cleaning,
                            StageChangedAt = utcNow,
                        })
                        .Where(item => item.PendingObjectKey == ticket.PendingObjectKey
                            && item.Status == ticket.Status)
                        .ExecuteCommandAsync(cancellationToken);
                    if (claimed != 1)
                    {
                        continue;
                    }
                    ticket.Status = EmployeeImageUploadStatus.Cleaning;
                }

                var pendingResult = await storage.DeleteObjectAsync(
                    ticket.PendingObjectKey,
                    cancellationToken
                );
                var finalResult = ApiResponse<bool>.OK(true);
                if (!finalIsReferenced && !string.IsNullOrWhiteSpace(ticket.FinalObjectKey))
                {
                    // Processing 可能已复制 COS 但尚未落 Promoted，恢复时必须同时检查正式孤儿。
                    finalResult = await storage.DeleteObjectAsync(
                        ticket.FinalObjectKey,
                        cancellationToken
                    );
                }
                if (!pendingResult.Success || !finalResult.Success)
                {
                    logger.LogWarning(
                        "过期员工图片对象清理失败。UserGUID: {UserGUID}, ObjectKey: {ObjectKey}, FinalObjectKey: {FinalObjectKey}",
                        ticket.UserGUID,
                        ticket.PendingObjectKey,
                        ticket.FinalObjectKey
                    );
                    continue;
                }
                await db.Updateable<EmployeeImageUploadTicket>()
                    .SetColumns(item => new EmployeeImageUploadTicket
                    {
                        Status = EmployeeImageUploadStatus.Failed,
                        FailedAt = utcNow,
                        StageChangedAt = utcNow,
                    })
                    .Where(item => item.PendingObjectKey == ticket.PendingObjectKey
                        && item.Status == EmployeeImageUploadStatus.Cleaning)
                    .ExecuteCommandAsync(cancellationToken);
            }
        }

        private static async Task CleanupPreviousObjectAsync(
            ISqlSugarClient db,
            TencentCloudUploadService storage,
            ILogger logger,
            EmployeeImageUploadTicket ticket,
            EmployeeProfile? profile,
            DateTime utcNow,
            CancellationToken cancellationToken
        )
        {
            if (string.IsNullOrWhiteSpace(ticket.PreviousObjectKey))
            {
                return;
            }
            var expectedStatus = ticket.PreviousObjectCleanupStatus;
            var expectedStartedAt = ticket.PreviousObjectCleanupStartedAt;
            if (expectedStatus != EmployeeImageObjectCleanupStatus.Pending
                && (expectedStatus != EmployeeImageObjectCleanupStatus.Processing
                    || expectedStartedAt is null
                    || expectedStartedAt > utcNow.AddMinutes(-15)))
            {
                return;
            }
            // 关键逻辑：Processing lease 超时后用状态+旧时间戳 CAS 重领，避免崩溃后永久卡死。
            var claim = db.Updateable<EmployeeImageUploadTicket>()
                .SetColumns(item => new EmployeeImageUploadTicket
                {
                    PreviousObjectCleanupStatus = EmployeeImageObjectCleanupStatus.Processing,
                    PreviousObjectCleanupStartedAt = utcNow,
                })
                .Where(item => item.PendingObjectKey == ticket.PendingObjectKey
                    && item.Status == EmployeeImageUploadStatus.Completed
                    && item.PreviousObjectCleanupStatus == expectedStatus);
            if (expectedStatus == EmployeeImageObjectCleanupStatus.Processing)
            {
                claim = claim.Where(item =>
                    item.PreviousObjectCleanupStartedAt == expectedStartedAt);
            }
            var claimed = await claim.ExecuteCommandAsync(cancellationToken);
            if (claimed != 1)
            {
                return;
            }
            if (IsObjectReferenced(profile, storage, ticket.Kind, ticket.PreviousObjectKey))
            {
                await MarkPreviousCleanupAsync(
                    db,
                    ticket.PendingObjectKey,
                    EmployeeImageObjectCleanupStatus.Completed,
                    cancellationToken
                );
                return;
            }
            var result = await storage.DeleteObjectAsync(ticket.PreviousObjectKey, cancellationToken);
            if (!result.Success)
            {
                logger.LogWarning(
                    "员工历史图片恢复清理失败。UserGUID: {UserGUID}, ObjectKey: {ObjectKey}",
                    ticket.UserGUID,
                    ticket.PreviousObjectKey
                );
                await MarkPreviousCleanupAsync(
                    db,
                    ticket.PendingObjectKey,
                    EmployeeImageObjectCleanupStatus.Pending,
                    cancellationToken
                );
                return;
            }
            await MarkPreviousCleanupAsync(
                db,
                ticket.PendingObjectKey,
                EmployeeImageObjectCleanupStatus.Completed,
                cancellationToken
            );
        }

        private static Task<int> MarkPreviousCleanupAsync(
            ISqlSugarClient db,
            string pendingObjectKey,
            EmployeeImageObjectCleanupStatus status,
            CancellationToken cancellationToken
        ) => db.Updateable<EmployeeImageUploadTicket>()
            .SetColumns(item => new EmployeeImageUploadTicket
            {
                PreviousObjectCleanupStatus = status,
                PreviousObjectCleanupStartedAt = null,
            })
            .Where(item => item.PendingObjectKey == pendingObjectKey
                && item.PreviousObjectCleanupStatus == EmployeeImageObjectCleanupStatus.Processing)
            .ExecuteCommandAsync(cancellationToken);

        private static bool IsObjectReferenced(
            EmployeeProfile? profile,
            TencentCloudUploadService storage,
            string kind,
            string? objectKey
        )
        {
            if (profile is null || string.IsNullOrWhiteSpace(objectKey))
            {
                return false;
            }
            if (kind == "identity")
            {
                return string.Equals(
                    profile.IdentityPhotoObjectKey,
                    objectKey,
                    StringComparison.Ordinal
                );
            }
            return storage.TryGetPublicObjectKey(profile.AvatarUrl, out var currentObjectKey)
                && string.Equals(currentObjectKey, objectKey, StringComparison.Ordinal);
        }
    }

    public sealed class EmployeeImageUploadCleanupBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<EmployeeImageUploadCleanupBackgroundService> _logger;

        public EmployeeImageUploadCleanupBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<EmployeeImageUploadCleanupBackgroundService> logger
        )
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<SqlSugarContext>();
                    var storage = scope.ServiceProvider.GetRequiredService<TencentCloudUploadService>();
                    await EmployeeImageUploadCleanup.CleanupExpiredAsync(
                        context.Db,
                        storage,
                        _logger,
                        DateTime.UtcNow,
                        stoppingToken
                    );
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "清理过期员工图片上传失败");
                }
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }
    }
}
