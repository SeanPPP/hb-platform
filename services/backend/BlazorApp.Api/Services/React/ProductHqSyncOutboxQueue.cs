using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.HBweb;
using Microsoft.Extensions.Options;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

public sealed class ProductHqSyncOutboxQueue : IProductHqSyncOutboxQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISqlSugarClient _db;
    private readonly TimeProvider _timeProvider;

    public ProductHqSyncOutboxQueue(
        SqlSugarContext context,
        IOptions<ProductHqSyncOutboxOptions> options
    ) : this(context.Db, options, TimeProvider.System) { }

    internal ProductHqSyncOutboxQueue(
        ISqlSugarClient db,
        IOptions<ProductHqSyncOutboxOptions> options,
        TimeProvider timeProvider
    )
    {
        _db = db;
        _ = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<ProductHqSyncOutboxEnqueueResultDto> EnqueueAsync(
        ISqlSugarClient db,
        ProductHqSyncOutboxEnqueueRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(request);
        var normalized = NormalizeRequest(request, UtcNow());
        var ownsTransaction = db.Ado.Transaction == null;
        if (ownsTransaction)
        {
            await db.Ado.BeginTranAsync();
        }

        try
        {
            // 同一商品的不同 scope 也会写入同一 HQ 商品投影，入队排序必须共用一把商品锁。
            await AcquireMergeLockAsync(db, normalized.ProductCode);
            var duplicate = await db.Queryable<ProductHqSyncOutbox>()
                .Where(item => item.OperationKey == normalized.OperationKey)
                .FirstAsync(cancellationToken);
            if (duplicate != null)
            {
                if (ownsTransaction)
                {
                    await db.Ado.CommitTranAsync();
                }
                return Result(duplicate, wasDuplicate: true, Array.Empty<Guid>());
            }

            var latestProductRow = await db.Queryable<ProductHqSyncOutbox>()
                .Where(item => item.ProductCode == normalized.ProductCode)
                .OrderBy(item => item.CreatedAt, OrderByType.Desc)
                .FirstAsync(cancellationToken);
            if (
                latestProductRow != null
                && latestProductRow.CreatedAt >= normalized.EnqueuedAtUtc
                && latestProductRow.CreatedAt < DateTime.MaxValue
            )
            {
                // CreatedAt 是队列因果顺序；同一时钟 tick 的连续请求也必须严格递增。
                normalized = normalized with
                {
                    EnqueuedAtUtc = latestProductRow.CreatedAt.AddTicks(1),
                };
            }

            var mergeableRows = await db.Queryable<ProductHqSyncOutbox>()
                .Where(item =>
                    item.ProductCode == normalized.ProductCode
                    && item.ScopeKey == normalized.ScopeKey
                    && (
                        item.Status == ProductHqSyncOutboxStatuses.Pending
                        || item.Status == ProductHqSyncOutboxStatuses.Retrying
                    )
                )
                .OrderBy(item => item.CreatedAt, OrderByType.Desc)
                .ToListAsync(cancellationToken);

            var row = CreateRow(normalized);
            row.FieldMaskJson = SerializeStrings(
                mergeableRows.SelectMany(item => DeserializeStrings(item.FieldMaskJson))
                    .Concat(normalized.FieldMask)
            );
            row.TombstonesJson = SerializeTombstones(
                mergeableRows.SelectMany(item => DeserializeTombstones(item.TombstonesJson))
                    .Concat(normalized.Tombstones)
            );

            var supersededIds = mergeableRows.Select(item => item.Id).Distinct().ToArray();
            if (supersededIds.Length > 0)
            {
                await db.Updateable<ProductHqSyncOutbox>()
                    .SetColumns(item => new ProductHqSyncOutbox
                    {
                        Status = ProductHqSyncOutboxStatuses.Superseded,
                        SupersededById = row.Id,
                        CompletedAtUtc = normalized.EnqueuedAtUtc,
                        LeaseOwner = null,
                        LeaseToken = null,
                        LeaseExpiresAtUtc = null,
                        UpdatedAt = normalized.EnqueuedAtUtc,
                        UpdatedBy = normalized.Source,
                    })
                    .Where(item =>
                        supersededIds.Contains(item.Id)
                        && (
                            item.Status == ProductHqSyncOutboxStatuses.Pending
                            || item.Status == ProductHqSyncOutboxStatuses.Retrying
                        )
                    )
                    .ExecuteCommandAsync(cancellationToken);
            }

            using (SqlSugarAuditScope.PreserveExplicitAuditFields())
            {
                await db.Insertable(row).ExecuteCommandAsync(cancellationToken);
            }
            if (ownsTransaction)
            {
                await db.Ado.CommitTranAsync();
            }
            return Result(row, wasDuplicate: false, supersededIds);
        }
        catch
        {
            if (ownsTransaction)
            {
                try
                {
                    await db.Ado.RollbackTranAsync();
                }
                catch
                {
                    // 保留原始入队异常。
                }
            }
            throw;
        }
    }

    public async Task<ProductHqSyncOperationStatusDto?> GetStatusAsync(
        string operationId,
        CancellationToken cancellationToken = default
    )
    {
        var normalized = NormalizeRequired(operationId, 200, nameof(operationId));
        var row = await _db.Queryable<ProductHqSyncOutbox>()
            .Where(item => item.OperationKey == normalized)
            .FirstAsync(cancellationToken);
        return row == null ? null : ToOperationStatus(row);
    }

    public async Task<ProductHqSyncOutboxAccessDescriptor?> GetAccessDescriptorAsync(
        string operationId,
        CancellationToken cancellationToken = default
    )
    {
        var normalized = NormalizeRequired(operationId, 200, nameof(operationId));
        var row = await _db.Queryable<ProductHqSyncOutbox>()
            .Where(item => item.OperationKey == normalized)
            .FirstAsync(cancellationToken);
        if (row == null)
        {
            return null;
        }

        return new ProductHqSyncOutboxAccessDescriptor
        {
            Operation = ToOperationStatus(row),
            OperationKind = row.OperationKind,
            TargetStoreCodes = DeserializeNullableStrings(row.TargetStoreCodesJson),
            AuthorizedStoreCodes = DeserializeNullableStrings(row.AuthorizedStoreCodesJson),
            RequestedByUserGuid = row.RequestedByUserGuid,
            RequestedByDeviceId = row.RequestedByDeviceId,
        };
    }

    public async Task<ProductHqSyncOperationStatusDto?> RetryAsync(
        string operationId,
        string requestedBy,
        CancellationToken cancellationToken = default
    )
    {
        var normalized = NormalizeRequired(operationId, 200, nameof(operationId));
        var now = UtcNow();
        var actor = NormalizeOptional(requestedBy, 100) ?? "system";
        await _db.Updateable<ProductHqSyncOutbox>()
            .SetColumns(item => new ProductHqSyncOutbox
            {
                Status = ProductHqSyncOutboxStatuses.Retrying,
                AttemptCount = 0,
                NextAttemptAtUtc = now,
                LeaseOwner = null,
                LeaseToken = null,
                LeaseExpiresAtUtc = null,
                LastAttemptAtUtc = null,
                CompletedAtUtc = null,
                LastErrorCode = null,
                LastErrorMessage = "已安排人工重试",
                UpdatedAt = now,
                UpdatedBy = actor,
            })
            .Where(item =>
                item.OperationKey == normalized
                && item.Status == ProductHqSyncOutboxStatuses.Blocked
            )
            .ExecuteCommandAsync(cancellationToken);

        return await GetStatusAsync(normalized, cancellationToken);
    }

    internal static ProductHqSyncOutboxWorkItemDto ToWorkItem(ProductHqSyncOutbox row) =>
        new()
        {
            OutboxId = row.Id,
            OperationKey = row.OperationKey,
            OperationKind = row.OperationKind,
            ProductCode = row.ProductCode,
            ScopeKey = row.ScopeKey,
            TargetStoreCodes = DeserializeNullableStrings(row.TargetStoreCodesJson),
            FieldMask = DeserializeStrings(row.FieldMaskJson),
            PayloadJson = row.PayloadJson,
            Tombstones = DeserializeTombstones(row.TombstonesJson),
            Source = row.Source,
            OccurredAtUtc = AsUtc(row.OccurredAtUtc),
            AttemptCount = row.AttemptCount,
            LeaseOwner = row.LeaseOwner ?? string.Empty,
            LeaseToken = row.LeaseToken ?? Guid.Empty,
        };

    internal static ProductHqSyncOperationStatusDto ToOperationStatus(ProductHqSyncOutbox row)
    {
        var stores = DeserializeNullableStrings(row.TargetStoreCodesJson);
        return new ProductHqSyncOperationStatusDto
        {
            OperationId = row.OperationKey,
            Status = row.Status,
            ProductCode = row.ProductCode,
            StoreCode = stores?.Count == 1 ? stores[0] : null,
            AttemptCount = row.AttemptCount,
            NextAttemptAt = row.Status is ProductHqSyncOutboxStatuses.Pending
                or ProductHqSyncOutboxStatuses.Retrying
                ? AsUtc(row.NextAttemptAtUtc)
                : null,
            Retryable = row.Status is ProductHqSyncOutboxStatuses.Pending
                or ProductHqSyncOutboxStatuses.Processing
                or ProductHqSyncOutboxStatuses.Retrying
                or ProductHqSyncOutboxStatuses.Blocked,
            ErrorCode = row.LastErrorCode,
            Message = ResolveStatusMessage(row),
        };
    }

    private static ProductHqSyncOutboxEnqueueResultDto Result(
        ProductHqSyncOutbox row,
        bool wasDuplicate,
        IReadOnlyList<Guid> supersededIds
    ) =>
        new()
        {
            OutboxId = row.Id,
            WasDuplicate = wasDuplicate,
            SupersededOutboxIds = supersededIds,
            Operation = ToOperationStatus(row),
        };

    private static ProductHqSyncOutbox CreateRow(NormalizedRequest request) =>
        new()
        {
            Id = Guid.NewGuid(),
            OperationKey = request.OperationKey,
            OperationKind = request.OperationKind,
            ProductCode = request.ProductCode,
            ScopeKey = request.ScopeKey,
            TargetStoreCodesJson = JsonSerializer.Serialize(request.TargetStoreCodes, JsonOptions),
            AuthorizedStoreCodesJson = JsonSerializer.Serialize(
                request.AuthorizedStoreCodes,
                JsonOptions
            ),
            FieldMaskJson = SerializeStrings(request.FieldMask),
            PayloadJson = request.PayloadJson,
            TombstonesJson = SerializeTombstones(request.Tombstones),
            Source = request.Source,
            RequestedByUserGuid = request.RequestedByUserGuid,
            RequestedByDeviceId = request.RequestedByDeviceId,
            Status = ProductHqSyncOutboxStatuses.Pending,
            OccurredAtUtc = request.OccurredAtUtc,
            AttemptCount = 0,
            NextAttemptAtUtc = request.EnqueuedAtUtc,
            CreatedAt = request.EnqueuedAtUtc,
            CreatedBy = request.Source,
            UpdatedAt = request.EnqueuedAtUtc,
            UpdatedBy = request.Source,
            IsDeleted = false,
        };

    private static NormalizedRequest NormalizeRequest(
        ProductHqSyncOutboxEnqueueRequest request,
        DateTime utcNow
    )
    {
        var operationKey = NormalizeRequired(request.OperationKey, 200, nameof(request.OperationKey));
        var operationKind = NormalizeRequired(request.OperationKind, 80, nameof(request.OperationKind))
            .ToLowerInvariant();
        var productCode = NormalizeRequired(request.ProductCode, 100, nameof(request.ProductCode));
        var source = NormalizeRequired(request.Source, 100, nameof(request.Source));
        var requestedByUserGuid = NormalizeOptional(request.RequestedByUserGuid, 80);
        var requestedByDeviceId = NormalizeOptional(request.RequestedByDeviceId, 200);
        if ((requestedByUserGuid == null) == (requestedByDeviceId == null))
        {
            throw new ArgumentException(
                "RequestedByUserGuid 与 RequestedByDeviceId 必须且只能提供一个",
                nameof(request)
            );
        }
        var storeCodes = request.TargetStoreCodes == null
            ? null
            : NormalizeStrings(request.TargetStoreCodes, 100, uppercase: false);
        var authorizedStoreCodes = request.AuthorizedStoreCodes == null
            ? null
            : NormalizeStrings(request.AuthorizedStoreCodes, 100, uppercase: false);
        var fieldMask = NormalizeStrings(request.FieldMask, 120, uppercase: false);
        var payload = string.IsNullOrWhiteSpace(request.PayloadJson) ? "{}" : request.PayloadJson.Trim();
        using (JsonDocument.Parse(payload)) { }
        var tombstones = NormalizeTombstones(request.Tombstones);
        var occurredAt = request.OccurredAtUtc == default
            ? utcNow
            : AsUtc(request.OccurredAtUtc);
        return new NormalizedRequest(
            operationKey,
            operationKind,
            productCode,
            BuildScopeKey(storeCodes),
            storeCodes,
            authorizedStoreCodes,
            fieldMask,
            payload,
            tombstones,
            source,
            requestedByUserGuid,
            requestedByDeviceId,
            occurredAt,
            utcNow
        );
    }

    private static string BuildScopeKey(IReadOnlyList<string>? storeCodes) =>
        storeCodes == null
            ? "all"
            : storeCodes.Count == 0
                ? "global"
                : $"stores:{string.Join(',', storeCodes)}";

    private static List<string> NormalizeStrings(
        IEnumerable<string>? values,
        int maxLength,
        bool uppercase
    ) =>
        (values ?? Array.Empty<string>())
            .Select(value => NormalizeOptional(value, maxLength))
            .Where(value => value != null)
            .Select(value => uppercase ? value!.ToUpperInvariant() : value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<ProductHqSyncOutboxTombstoneDto> NormalizeTombstones(
        IEnumerable<ProductHqSyncOutboxTombstoneDto>? tombstones
    ) =>
        (tombstones ?? Array.Empty<ProductHqSyncOutboxTombstoneDto>())
            .Select(item => new ProductHqSyncOutboxTombstoneDto(
                NormalizeRequired(item.ResourceKind, 80, nameof(item.ResourceKind)).ToLowerInvariant(),
                NormalizeOptional(item.StoreCode, 100),
                NormalizeRequired(item.BusinessKey, 300, nameof(item.BusinessKey))
            ))
            .GroupBy(
                item => $"{item.ResourceKind}|{item.StoreCode}|{item.BusinessKey}",
                StringComparer.OrdinalIgnoreCase
            )
            .Select(group => group.First())
            .OrderBy(item => item.ResourceKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.StoreCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.BusinessKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string SerializeStrings(IEnumerable<string> values) =>
        JsonSerializer.Serialize(
            NormalizeStrings(values, 120, uppercase: false),
            JsonOptions
        );

    private static IReadOnlyList<string> DeserializeStrings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }
        return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>();
    }

    private static IReadOnlyList<string>? DeserializeNullableStrings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || string.Equals(json.Trim(), "null", StringComparison.Ordinal))
        {
            return null;
        }
        return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>();
    }

    private static string SerializeTombstones(
        IEnumerable<ProductHqSyncOutboxTombstoneDto> tombstones
    ) => JsonSerializer.Serialize(NormalizeTombstones(tombstones), JsonOptions);

    private static IReadOnlyList<ProductHqSyncOutboxTombstoneDto> DeserializeTombstones(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ProductHqSyncOutboxTombstoneDto>();
        }
        return JsonSerializer.Deserialize<List<ProductHqSyncOutboxTombstoneDto>>(json, JsonOptions)
            ?? new List<ProductHqSyncOutboxTombstoneDto>();
    }

    private static string NormalizeRequired(string? value, int maxLength, string paramName) =>
        NormalizeOptional(value, maxLength)
        ?? throw new ArgumentException($"{paramName} 不能为空", paramName);

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static string ResolveStatusMessage(ProductHqSyncOutbox row) =>
        !string.IsNullOrWhiteSpace(row.LastErrorMessage)
            ? row.LastErrorMessage!
            : row.Status switch
            {
                ProductHqSyncOutboxStatuses.Pending => "等待同步到 HQ",
                ProductHqSyncOutboxStatuses.Processing => "正在同步到 HQ",
                ProductHqSyncOutboxStatuses.Retrying => "等待自动重试",
                ProductHqSyncOutboxStatuses.Succeeded => "HQ 同步完成",
                ProductHqSyncOutboxStatuses.Blocked => "HQ 同步需要人工处理",
                ProductHqSyncOutboxStatuses.Superseded => "已由更新的同范围操作替代",
                _ => "HQ 同步状态未知",
            };

    private static async Task AcquireMergeLockAsync(
        ISqlSugarClient db,
        string productCode
    )
    {
        if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
        {
            return;
        }

        // 业务标识保留原始大小写；锁键仍按不区分大小写归一，兼容 SQL Server 常用 CI 排序规则。
        var material = Encoding.UTF8.GetBytes(productCode.Trim().ToUpperInvariant());
        var hash = Convert.ToHexString(SHA256.HashData(material))[..32];
        const string sql = """
DECLARE @Result int;
EXEC @Result = sys.sp_getapplock
    @Resource = @LockResource,
    @LockMode = N'Exclusive',
    @LockOwner = N'Transaction',
    @LockTimeout = 30000;
IF @Result < 0
    THROW 51070, N'Unable to acquire product HQ outbox merge lock.', 1;
""";
        await db.Ado.ExecuteCommandAsync(
            sql,
            new SugarParameter("@LockResource", $"ProductHqSyncOutbox:{hash}")
        );
    }

    private sealed record NormalizedRequest(
        string OperationKey,
        string OperationKind,
        string ProductCode,
        string ScopeKey,
        List<string>? TargetStoreCodes,
        List<string>? AuthorizedStoreCodes,
        List<string> FieldMask,
        string PayloadJson,
        List<ProductHqSyncOutboxTombstoneDto> Tombstones,
        string Source,
        string? RequestedByUserGuid,
        string? RequestedByDeviceId,
        DateTime OccurredAtUtc,
        DateTime EnqueuedAtUtc
    );
}
