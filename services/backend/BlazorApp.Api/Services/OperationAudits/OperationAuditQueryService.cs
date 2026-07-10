using System.Security.Claims;
using BlazorApp.Api.Interfaces;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models.POSM;
using Microsoft.AspNetCore.Http;
using SqlSugar;

namespace BlazorApp.Api.Services.OperationAudits;

public enum OperationAuditDetailAccessStatus
{
    Found,
    NotFound,
    Forbidden,
}

public sealed class OperationAuditDetailQueryResult
{
    public OperationAuditDetailAccessStatus Status { get; init; }

    public OperationAuditDetailDto? Data { get; init; }
}

public sealed class OperationAuditQueryService
{
    private static readonly IReadOnlyList<string> AdminRoleAliases =
        Permissions.SuperAdminRoleNames;
    private static readonly string[] StoreManagerRoleAliases = ["StoreManager", "店长", "经理"];

    private readonly ISqlSugarClient _db;
    private readonly ICurrentUserManageableStoreScopeService _storeScopeService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public OperationAuditQueryService(
        ISqlSugarClient db,
        ICurrentUserManageableStoreScopeService storeScopeService,
        IHttpContextAccessor httpContextAccessor
    )
    {
        _db = db;
        _storeScopeService = storeScopeService;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<PagedListReactDto<OperationAuditListItemDto>> QueryAsync(
        OperationAuditQueryDto request,
        DateTime? utcNow = null
    )
    {
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 200);
        var empty = CreateEmptyPage(pageNumber, pageSize);
        var access = await ResolveAccessAsync();
        if (!access.IsAllowed)
        {
            return empty;
        }

        var now = DateTime.SpecifyKind(utcNow ?? DateTime.UtcNow, DateTimeKind.Utc);
        var fromUtc = request.FromUtc?.UtcDateTime ?? now.AddDays(-7);
        var toUtc = request.ToUtc?.UtcDateTime ?? now;
        if (fromUtc > toUtc)
        {
            return empty;
        }

        var query = _db.Queryable<PosOperationAudit>()
            .Where(item => item.OccurredAtUtc >= fromUtc && item.OccurredAtUtc <= toUtc);

        if (!access.IsAdmin)
        {
            query = query.Where(item => access.StoreCodes.Contains(item.StoreCode));
        }

        var storeCode = TrimToNull(request.StoreCode);
        if (storeCode != null)
        {
            if (!access.IsAdmin && !access.StoreCodes.Contains(storeCode, StringComparer.OrdinalIgnoreCase))
            {
                return empty;
            }

            query = query.Where(item => item.StoreCode == storeCode);
        }

        var cashierKeyword = TrimToNull(request.CashierKeyword);
        if (cashierKeyword != null)
        {
            query = query.Where(item =>
                (item.CashierId != null && item.CashierId.Contains(cashierKeyword))
                || (item.UserGuid != null && item.UserGuid.Contains(cashierKeyword))
                || (item.CashierName != null && item.CashierName.Contains(cashierKeyword))
            );
        }

        var deviceCode = TrimToNull(request.DeviceCode);
        if (deviceCode != null)
        {
            query = query.Where(item => item.DeviceCode == deviceCode);
        }

        var operationType = TrimToNull(request.OperationType);
        if (operationType != null)
        {
            query = query.Where(item => item.OperationType == operationType);
        }

        var outcome = TrimToNull(request.Outcome);
        if (outcome != null)
        {
            query = query.Where(item => item.Outcome == outcome);
        }

        var orderGuid = TrimToNull(request.OrderGuid);
        if (orderGuid != null)
        {
            query = query.Where(item => item.OrderGuid == orderGuid);
        }

        var keyword = TrimToNull(request.Keyword);
        if (keyword != null)
        {
            query = query.Where(item =>
                (item.OrderGuid != null && item.OrderGuid.Contains(keyword))
                || (item.ReceiptNumber != null && item.ReceiptNumber.Contains(keyword))
                || (item.CorrelationId != null && item.CorrelationId.Contains(keyword))
                || (item.TraceId != null && item.TraceId.Contains(keyword))
                || (item.ReasonCode != null && item.ReasonCode.Contains(keyword))
                || (item.SafeMessage != null && item.SafeMessage.Contains(keyword))
                || (item.CashierId != null && item.CashierId.Contains(keyword))
                || (item.CashierName != null && item.CashierName.Contains(keyword))
                || item.DeviceCode.Contains(keyword)
            );
        }

        var productKeyword = TrimToNull(request.ProductKeyword);
        if (productKeyword != null)
        {
            // 商品检索固定从子表 EXISTS 过滤，父事件仍只返回一条，避免多商品动作被展开。
            query = query.Where(parent =>
                SqlFunc.Subqueryable<PosOperationAuditItem>()
                    .Where(item =>
                        item.EventId == parent.EventId
                        && (
                            (item.ProductCode != null && item.ProductCode.Contains(productKeyword))
                            || (item.ItemNumber != null && item.ItemNumber.Contains(productKeyword))
                            || (item.ReferenceCode != null && item.ReferenceCode.Contains(productKeyword))
                            || (item.LookupCode != null && item.LookupCode.Contains(productKeyword))
                            || (item.DisplayName != null && item.DisplayName.Contains(productKeyword))
                        )
                    )
                    .Any()
            );
        }

        var total = await query.CountAsync();
        var rows = await query
            .OrderBy(item => item.OccurredAtUtc, OrderByType.Desc)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedListReactDto<OperationAuditListItemDto>
        {
            Items = rows.Select(MapListItem).ToList(),
            Total = total,
            PageNumber = pageNumber,
            PageSize = pageSize,
        };
    }

    public async Task<OperationAuditDetailQueryResult> GetDetailAsync(Guid eventId)
    {
        var row = await _db.Queryable<PosOperationAudit>()
            .FirstAsync(item => item.EventId == eventId);
        if (row == null)
        {
            return new OperationAuditDetailQueryResult
            {
                Status = OperationAuditDetailAccessStatus.NotFound,
            };
        }

        var access = await ResolveAccessAsync();
        if (!access.CanAccess(row.StoreCode))
        {
            return new OperationAuditDetailQueryResult
            {
                Status = OperationAuditDetailAccessStatus.Forbidden,
            };
        }

        var items = await _db.Queryable<PosOperationAuditItem>()
            .Where(item => item.EventId == eventId)
            .OrderBy(item => item.LineIndex)
            .ToListAsync();
        var detail = MapDetail(row);
        detail.Items = items.Select(MapDetailItem).ToList();
        return new OperationAuditDetailQueryResult
        {
            Status = OperationAuditDetailAccessStatus.Found,
            Data = detail,
        };
    }

    private async Task<OperationAuditStoreAccess> ResolveAccessAsync()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return OperationAuditStoreAccess.Denied;
        }

        if (HasAnyRole(user, AdminRoleAliases))
        {
            return OperationAuditStoreAccess.Admin;
        }

        if (!HasAnyRole(user, StoreManagerRoleAliases))
        {
            return OperationAuditStoreAccess.Denied;
        }

        var scope = await _storeScopeService.GetScopeAsync();
        var storeCodes = scope.StoreCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return scope.IsAllowed && storeCodes.Length > 0
            ? new OperationAuditStoreAccess(true, false, storeCodes)
            : OperationAuditStoreAccess.Denied;
    }

    private static bool HasAnyRole(ClaimsPrincipal user, IEnumerable<string> aliases) =>
        user.Claims.Any(claim =>
            claim.Type == ClaimTypes.Role
            && aliases.Any(alias => alias.Equals(claim.Value, StringComparison.OrdinalIgnoreCase))
        );

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static PagedListReactDto<OperationAuditListItemDto> CreateEmptyPage(
        int pageNumber,
        int pageSize
    ) => new()
    {
        PageNumber = pageNumber,
        PageSize = pageSize,
    };

    private static OperationAuditListItemDto MapListItem(PosOperationAudit row) => new()
    {
        EventId = row.EventId,
        SchemaVersion = row.SchemaVersion,
        OccurredAtUtc = AsUtc(row.OccurredAtUtc),
        ReceivedAtUtc = AsUtc(row.ReceivedAtUtc),
        OperationType = row.OperationType,
        Outcome = row.Outcome,
        CashierId = row.CashierId,
        UserGuid = row.UserGuid,
        CashierName = row.CashierName,
        IsOfflineCached = row.IsOfflineCached,
        IsEmergencyOverride = row.IsEmergencyOverride,
        StoreCode = row.StoreCode,
        DeviceCode = row.DeviceCode,
        AppVersion = row.AppVersion,
        InstanceId = row.InstanceId,
        OrderGuid = row.OrderGuid,
        ReceiptNumber = row.ReceiptNumber,
        CorrelationId = row.CorrelationId,
        TraceId = row.TraceId,
        PaymentMethod = row.PaymentMethod,
        ReasonCode = row.ReasonCode,
        SafeMessage = row.SafeMessage,
        CurrencyCode = row.CurrencyCode,
        PaymentAmount = row.PaymentAmount,
        BeforeGross = row.BeforeGross,
        AfterGross = row.AfterGross,
        BeforeDiscount = row.BeforeDiscount,
        AfterDiscount = row.AfterDiscount,
        BeforeActual = row.BeforeActual,
        AfterActual = row.AfterActual,
        AmountDelta = row.AmountDelta,
        ProductCount = row.ProductCount,
        PrimaryProduct = row.PrimaryProduct,
    };

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static OperationAuditDetailDto MapDetail(PosOperationAudit row)
    {
        var listItem = MapListItem(row);
        return new OperationAuditDetailDto
        {
            EventId = listItem.EventId,
            SchemaVersion = listItem.SchemaVersion,
            OccurredAtUtc = listItem.OccurredAtUtc,
            ReceivedAtUtc = listItem.ReceivedAtUtc,
            OperationType = listItem.OperationType,
            Outcome = listItem.Outcome,
            CashierId = listItem.CashierId,
            UserGuid = listItem.UserGuid,
            CashierName = listItem.CashierName,
            IsOfflineCached = listItem.IsOfflineCached,
            IsEmergencyOverride = listItem.IsEmergencyOverride,
            StoreCode = listItem.StoreCode,
            DeviceCode = listItem.DeviceCode,
            AppVersion = listItem.AppVersion,
            InstanceId = listItem.InstanceId,
            OrderGuid = listItem.OrderGuid,
            ReceiptNumber = listItem.ReceiptNumber,
            CorrelationId = listItem.CorrelationId,
            TraceId = listItem.TraceId,
            PaymentMethod = listItem.PaymentMethod,
            ReasonCode = listItem.ReasonCode,
            SafeMessage = listItem.SafeMessage,
            CurrencyCode = listItem.CurrencyCode,
            PaymentAmount = listItem.PaymentAmount,
            BeforeGross = listItem.BeforeGross,
            AfterGross = listItem.AfterGross,
            BeforeDiscount = listItem.BeforeDiscount,
            AfterDiscount = listItem.AfterDiscount,
            BeforeActual = listItem.BeforeActual,
            AfterActual = listItem.AfterActual,
            AmountDelta = listItem.AmountDelta,
            ProductCount = listItem.ProductCount,
            PrimaryProduct = listItem.PrimaryProduct,
            PropertiesJson = row.PropertiesJson,
        };
    }

    private static OperationAuditDetailItemDto MapDetailItem(PosOperationAuditItem row) => new()
    {
        EventId = row.EventId,
        LineIndex = row.LineIndex,
        ProductCode = row.ProductCode,
        ItemNumber = row.ItemNumber,
        ReferenceCode = row.ReferenceCode,
        LookupCode = row.LookupCode,
        DisplayName = row.DisplayName,
        LineKind = row.LineKind,
        BeforeQuantity = row.BeforeQuantity,
        AfterQuantity = row.AfterQuantity,
        QuantityDelta = row.QuantityDelta,
        BeforeUnitPrice = row.BeforeUnitPrice,
        AfterUnitPrice = row.AfterUnitPrice,
        UnitPriceDelta = row.UnitPriceDelta,
        BeforeDiscountAmount = row.BeforeDiscountAmount,
        AfterDiscountAmount = row.AfterDiscountAmount,
        DiscountAmountDelta = row.DiscountAmountDelta,
        BeforeGrossAmount = row.BeforeGrossAmount,
        AfterGrossAmount = row.AfterGrossAmount,
        GrossAmountDelta = row.GrossAmountDelta,
        BeforeActualAmount = row.BeforeActualAmount,
        AfterActualAmount = row.AfterActualAmount,
        ActualAmountDelta = row.ActualAmountDelta,
    };

    private sealed record OperationAuditStoreAccess(
        bool IsAllowed,
        bool IsAdmin,
        IReadOnlyList<string> StoreCodes
    )
    {
        public static OperationAuditStoreAccess Denied { get; } = new(false, false, []);

        public static OperationAuditStoreAccess Admin { get; } = new(true, true, []);

        public bool CanAccess(string storeCode) =>
            IsAllowed
            && (
                IsAdmin
                || StoreCodes.Contains(storeCode, StringComparer.OrdinalIgnoreCase)
            );
    }
}
