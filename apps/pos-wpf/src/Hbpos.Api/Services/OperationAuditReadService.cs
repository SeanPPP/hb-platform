using System.Globalization;
using BlazorApp.Shared.Models.POSM;
using Hbpos.Api.Data;
using Hbpos.Contracts.OperationAudits;

namespace Hbpos.Api.Services;

public interface IOperationAuditReadService
{
    Task<OperationAuditReadListDto> ListAsync(
        string storeCode,
        string deviceCode,
        string? keyword,
        int limit,
        CancellationToken cancellationToken);

    Task<OperationAuditReadRecordDto?> GetAsync(
        string storeCode,
        string deviceCode,
        Guid eventId,
        CancellationToken cancellationToken);
}

public sealed class SqlSugarOperationAuditReadService(HbposSqlSugarContext dbContext)
    : IOperationAuditReadService
{
    internal const int MaximumLimit = 100;
    internal const int MaximumKeywordLength = 120;

    public async Task<OperationAuditReadListDto> ListAsync(
        string storeCode,
        string deviceCode,
        string? keyword,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceCode);

        var safeLimit = Math.Clamp(limit <= 0 ? MaximumLimit : limit, 1, MaximumLimit);
        var safeKeyword = NormalizeKeyword(keyword);
        var query = dbContext.PosmDb.Queryable<PosOperationAudit>()
            // 关键逻辑：终端审计读取始终同时绑定认证门店和设备，不能扩大到同门店其他收银机。
            .Where(row => row.StoreCode == storeCode && row.DeviceCode == deviceCode);

        if (safeKeyword is not null)
        {
            // 关键逻辑：关键词只能搜索明确列入白名单的安全展示字段，绝不触碰 properties_json、
            // payment_method 或任何支付提供方/授权材料。
            query = query.Where(row =>
                row.OperationType.Contains(safeKeyword)
                || row.Outcome.Contains(safeKeyword)
                || (row.CashierName != null && row.CashierName.Contains(safeKeyword))
                || (row.OrderGuid != null && row.OrderGuid.Contains(safeKeyword))
                || (row.ReceiptNumber != null && row.ReceiptNumber.Contains(safeKeyword))
                || (row.CorrelationId != null && row.CorrelationId.Contains(safeKeyword))
                || (row.SafeMessage != null && row.SafeMessage.Contains(safeKeyword))
                || (row.PrimaryProduct != null && row.PrimaryProduct.Contains(safeKeyword)));
        }

        var rows = await query
            .OrderBy(row => row.OccurredAtUtc, SqlSugar.OrderByType.Desc)
            .OrderBy(row => row.EventId, SqlSugar.OrderByType.Desc)
            .Take(safeLimit)
            .ToListAsync(cancellationToken);

        return new OperationAuditReadListDto
        {
            Items = rows.Select(MapRecord).ToList()
        };
    }

    public async Task<OperationAuditReadRecordDto?> GetAsync(
        string storeCode,
        string deviceCode,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storeCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceCode);
        if (eventId == Guid.Empty)
        {
            return null;
        }

        // 在同一条 SQL 中施加完整 scope，跨终端记录与不存在记录统一返回 null。
        var row = await dbContext.PosmDb.Queryable<PosOperationAudit>()
            .FirstAsync(item =>
                item.EventId == eventId
                && item.StoreCode == storeCode
                && item.DeviceCode == deviceCode,
                cancellationToken);
        if (row is null)
        {
            return null;
        }

        var items = await dbContext.PosmDb.Queryable<PosOperationAuditItem>()
            .Where(item => item.EventId == eventId)
            .OrderBy(item => item.LineIndex)
            .ToListAsync(cancellationToken);
        var result = MapRecord(row);
        result.Items = items.Select(MapItem).ToList();
        return result;
    }

    private static string? NormalizeKeyword(string? keyword)
    {
        var value = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
        return value is null
            ? null
            : value[..Math.Min(value.Length, MaximumKeywordLength)];
    }

    private static OperationAuditReadRecordDto MapRecord(PosOperationAudit row) => new()
    {
        EventId = row.EventId,
        OccurredAtIso = AsUtc(row.OccurredAtUtc).ToString("O", CultureInfo.InvariantCulture),
        OperationType = row.OperationType,
        Outcome = row.Outcome,
        CashierName = row.CashierName,
        StoreCode = row.StoreCode,
        DeviceCode = row.DeviceCode,
        OrderGuid = row.OrderGuid,
        ReceiptNumber = row.ReceiptNumber,
        CorrelationId = row.CorrelationId,
        SafeMessage = row.SafeMessage,
        PaymentAmountCents = ToCents(row.PaymentAmount),
        ProductCount = row.ProductCount,
        PrimaryProduct = row.PrimaryProduct,
        UploadState = "uploaded"
    };

    private static OperationAuditReadItemDto MapItem(PosOperationAuditItem row) => new()
    {
        LineIndex = row.LineIndex,
        ProductCode = row.ProductCode,
        DisplayName = row.DisplayName,
        QuantityDelta = row.QuantityDelta?.ToString("0.###", CultureInfo.InvariantCulture),
        ActualAmountDeltaCents = ToCents(row.ActualAmountDelta)
    };

    private static DateTimeOffset AsUtc(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static long? ToCents(decimal? value)
    {
        if (value is null)
        {
            return null;
        }

        var rounded = decimal.Round(value.Value * 100m, 0, MidpointRounding.AwayFromZero);
        return checked(decimal.ToInt64(rounded));
    }
}
