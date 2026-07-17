using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.POSM;
using SqlSugar;

namespace BlazorApp.Api.Services.React;

public sealed class InstallmentOrderReactService : IInstallmentOrderReactService
{
    private readonly POSMSqlSugarContext _posmContext;
    private readonly SqlSugarContext _context;
    private readonly ILogger<InstallmentOrderReactService> _logger;

    public InstallmentOrderReactService(
        POSMSqlSugarContext posmContext,
        SqlSugarContext context,
        ILogger<InstallmentOrderReactService> logger
    )
    {
        _posmContext = posmContext;
        _context = context;
        _logger = logger;
    }

    public async Task<PagedListReactDto<InstallmentOrderSummaryDto>> GetOrderListAsync(
        InstallmentOrderQueryParams queryParams
    )
    {
        var pageNumber = Math.Max(1, queryParams.PageNumber);
        var pageSize = queryParams.PageSize <= 0 ? 20 : Math.Min(queryParams.PageSize, 1000);
        var query = _posmContext.Db.Queryable<InstallmentOrder>();

        var storeCodes = NormalizeStoreCodes(queryParams.StoreCodes);
        if (storeCodes is { Count: > 0 })
            query = query.Where(order => storeCodes.Contains(order.StoreCode));

        IReadOnlyCollection<string>? timeZoneScopeCodes = storeCodes;
        if (!string.IsNullOrWhiteSpace(queryParams.BranchCode))
        {
            var branchCode = queryParams.BranchCode.Trim();
            query = query.Where(order => order.StoreCode == branchCode);
            timeZoneScopeCodes = storeCodes is { Count: > 0 }
                ? storeCodes.Where(code => code.Equals(branchCode, StringComparison.OrdinalIgnoreCase)).ToList()
                : [branchCode];
        }

        if (queryParams.StartDate.HasValue || queryParams.EndDate.HasValue)
        {
            query = await ApplyStoreLocalDateFilterAsync(query, queryParams, timeZoneScopeCodes);
        }

        if (queryParams.Status.HasValue)
        {
            var status = (int)queryParams.Status.Value;
            query = query.Where(order => order.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(queryParams.CustomerName))
        {
            var customerName = queryParams.CustomerName.Trim();
            query = query.Where(order => order.CustomerName.Contains(customerName));
        }

        if (!string.IsNullOrWhiteSpace(queryParams.CustomerPhone))
        {
            var customerPhone = queryParams.CustomerPhone.Trim();
            query = query.Where(order => order.CustomerPhone.Contains(customerPhone));
        }

        var total = await query.CountAsync();
        var skipLong = (long)(pageNumber - 1) * pageSize;
        var skip = skipLong > int.MaxValue ? int.MaxValue : (int)skipLong;
        var orders = await query
            .OrderBy(order => order.CreatedAt, OrderByType.Desc)
            .OrderBy(order => order.InstallmentGuid, OrderByType.Asc)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync();

        var storeMetadata = await GetStoreMetadataAsync(orders.Select(order => order.StoreCode));
        return new PagedListReactDto<InstallmentOrderSummaryDto>
        {
            Items = orders.Select(order => MapSummary(order, storeMetadata)).ToList(),
            Total = total,
            PageNumber = pageNumber,
            PageSize = pageSize,
        };
    }

    public async Task<ApiResponse<InstallmentOrderDetailResponse>> GetOrderDetailAsync(
        string installmentGuid,
        IReadOnlyCollection<string>? allowedStoreCodes
    )
    {
        if (string.IsNullOrWhiteSpace(installmentGuid))
            return NotFound();

        var normalizedStoreCodes = allowedStoreCodes == null
            ? null
            : NormalizeStoreCodes(allowedStoreCodes);
        if (normalizedStoreCodes is { Count: 0 })
            return NotFound();

        var normalizedGuid = installmentGuid.Trim();
        var orderQuery = _posmContext.Db.Queryable<InstallmentOrder>()
            .Where(item => item.InstallmentGuid == normalizedGuid);
        if (normalizedStoreCodes != null)
            orderQuery = orderQuery.Where(item => normalizedStoreCodes.Contains(item.StoreCode));

        // 关键逻辑：先用门店范围过滤主单，未授权时绝不读取商品和付款明细。
        var order = await orderQuery.FirstAsync();
        if (order == null)
            return NotFound();

        var lines = await _posmContext.Db.Queryable<InstallmentOrderLine>()
            .Where(line => line.InstallmentGuid == normalizedGuid)
            .OrderBy(line => line.InstallmentLineGuid, OrderByType.Asc)
            .ToListAsync();
        var payments = await _posmContext.Db.Queryable<InstallmentPayment>()
            .Where(payment => payment.InstallmentGuid == normalizedGuid)
            .OrderBy(payment => payment.RecordedAt, OrderByType.Asc)
            .OrderBy(payment => payment.PaymentGuid, OrderByType.Asc)
            .ToListAsync();
        var storeMetadata = await GetStoreMetadataAsync([order.StoreCode]);

        return ApiResponse<InstallmentOrderDetailResponse>.OK(
            new InstallmentOrderDetailResponse
            {
                Order = MapDetail(order, storeMetadata),
                Lines = lines.Select(MapLine).ToList(),
                Payments = payments.Select(MapPayment).ToList(),
                PickupInfo = BuildPickupInfo(order),
                CancellationInfo = BuildCancellationInfo(order),
            }
        );
    }

    private async Task<ISugarQueryable<InstallmentOrder>> ApplyStoreLocalDateFilterAsync(
        ISugarQueryable<InstallmentOrder> query,
        InstallmentOrderQueryParams queryParams,
        IReadOnlyCollection<string>? requestedStoreCodes
    )
    {
        var stores = await GetStoresForTimeZoneAsync(requestedStoreCodes);
        var brisbaneCodes = stores
            .Where(pair => InstallmentOrderStoreTimeZoneResolver.Resolve(pair.Value) == InstallmentOrderStoreTimeZoneResolver.Brisbane)
            .Select(pair => pair.Key)
            .ToList();
        var melbourneCodes = stores
            .Where(pair => InstallmentOrderStoreTimeZoneResolver.Resolve(pair.Value) == InstallmentOrderStoreTimeZoneResolver.Melbourne)
            .Select(pair => pair.Key)
            .ToList();
        var predicate = Expressionable.Create<InstallmentOrder>();

        AddTimeZonePredicate(
            ref predicate,
            brisbaneCodes,
            queryParams,
            InstallmentOrderStoreTimeZoneResolver.Brisbane,
            includeUnlistedStores: false
        );
        AddTimeZonePredicate(
            ref predicate,
            melbourneCodes,
            queryParams,
            InstallmentOrderStoreTimeZoneResolver.Melbourne,
            includeUnlistedStores: false
        );

        var sydneyCodes = requestedStoreCodes is { Count: > 0 }
            ? requestedStoreCodes
                .Where(code => !brisbaneCodes.Contains(code, StringComparer.OrdinalIgnoreCase)
                    && !melbourneCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
                .ToList()
            : [];
        AddTimeZonePredicate(
            ref predicate,
            sydneyCodes,
            queryParams,
            InstallmentOrderStoreTimeZoneResolver.Sydney,
            includeUnlistedStores: requestedStoreCodes is not { Count: > 0 },
            excludedA: brisbaneCodes,
            excludedB: melbourneCodes
        );
        return query.Where(predicate.ToExpression());
    }

    private static void AddTimeZonePredicate(
        ref Expressionable<InstallmentOrder> predicate,
        IReadOnlyCollection<string> storeCodes,
        InstallmentOrderQueryParams queryParams,
        string timeZoneId,
        bool includeUnlistedStores,
        IReadOnlyCollection<string>? excludedA = null,
        IReadOnlyCollection<string>? excludedB = null
    )
    {
        if (!includeUnlistedStores && storeCodes.Count == 0)
            return;

        var codes = storeCodes.ToList();
        var excludedCodesA = excludedA?.ToList() ?? [];
        var excludedCodesB = excludedB?.ToList() ?? [];
        var (startUtc, endUtc) = InstallmentOrderStoreTimeZoneResolver.BuildUtcWindow(
            queryParams.StartDate,
            queryParams.EndDate,
            timeZoneId
        );

        if (includeUnlistedStores)
        {
            predicate = (startUtc, endUtc) switch
            {
                ({ } start, { } end) => predicate.Or(order =>
                    !excludedCodesA.Contains(order.StoreCode)
                    && !excludedCodesB.Contains(order.StoreCode)
                    && order.CreatedAt >= start
                    && order.CreatedAt < end
                ),
                ({ } start, null) => predicate.Or(order =>
                    !excludedCodesA.Contains(order.StoreCode)
                    && !excludedCodesB.Contains(order.StoreCode)
                    && order.CreatedAt >= start
                ),
                (null, { } end) => predicate.Or(order =>
                    !excludedCodesA.Contains(order.StoreCode)
                    && !excludedCodesB.Contains(order.StoreCode)
                    && order.CreatedAt < end
                ),
                _ => predicate,
            };
            return;
        }

        predicate = (startUtc, endUtc) switch
        {
            ({ } start, { } end) => predicate.Or(order =>
                codes.Contains(order.StoreCode) && order.CreatedAt >= start && order.CreatedAt < end
            ),
            ({ } start, null) => predicate.Or(order =>
                codes.Contains(order.StoreCode) && order.CreatedAt >= start
            ),
            (null, { } end) => predicate.Or(order =>
                codes.Contains(order.StoreCode) && order.CreatedAt < end
            ),
            _ => predicate,
        };
    }

    private async Task<Dictionary<string, Store>> GetStoresForTimeZoneAsync(
        IReadOnlyCollection<string>? requestedStoreCodes
    )
    {
        try
        {
            // 历史分期仍需要原门店所在地时区，软删门店不能退化为 Sydney。
            var query = _context.Db.Queryable<Store>();
            if (requestedStoreCodes is { Count: > 0 })
            {
                var codes = requestedStoreCodes.ToList();
                query = query.Where(store => codes.Contains(store.StoreCode));
            }

            var stores = await query.ToListAsync();
            return stores
                .GroupBy(store => store.StoreCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "读取分期门店时区信息失败，回退 Sydney 时区");
            return new Dictionary<string, Store>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<Dictionary<string, Store>> GetStoreMetadataAsync(
        IEnumerable<string> sourceCodes
    )
    {
        var codes = sourceCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (codes.Count == 0)
            return new Dictionary<string, Store>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var stores = await _context.Db.Queryable<Store>()
                .Where(store => !store.IsDeleted && codes.Contains(store.StoreCode))
                .ToListAsync();
            return stores
                .GroupBy(store => store.StoreCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "读取分期订单门店展示信息失败");
            return new Dictionary<string, Store>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static InstallmentOrderSummaryDto MapSummary(
        InstallmentOrder order,
        IReadOnlyDictionary<string, Store> stores
    )
    {
        stores.TryGetValue(order.StoreCode, out var store);
        return new InstallmentOrderSummaryDto
        {
            InstallmentGuid = order.InstallmentGuid,
            InstallmentNumber = order.InstallmentNumber,
            StoreCode = order.StoreCode,
            StoreName = store?.StoreName,
            ABN = store?.ABN,
            BrandName = store?.BrandName,
            CashierName = order.CashierName,
            CustomerName = order.CustomerName,
            CustomerPhone = order.CustomerPhone,
            CreatedAt = ToUtcOffset(order.CreatedAt),
            TotalAmount = order.TotalAmount,
            MinimumDownPayment = order.MinimumDownPayment,
            DownPaymentAmount = order.DownPaymentAmount,
            PaidAmount = order.PaidAmount,
            BalanceAmount = order.BalanceAmount,
            Status = (InstallmentOrderStatus)order.Status,
            UpdatedAt = ToUtcOffset(order.UpdatedAt),
        };
    }

    private static InstallmentOrderDetailDto MapDetail(
        InstallmentOrder order,
        IReadOnlyDictionary<string, Store> stores
    )
    {
        var summary = MapSummary(order, stores);
        return new InstallmentOrderDetailDto
        {
            InstallmentGuid = summary.InstallmentGuid,
            InstallmentNumber = summary.InstallmentNumber,
            StoreCode = summary.StoreCode,
            StoreName = summary.StoreName,
            ABN = summary.ABN,
            BrandName = summary.BrandName,
            DeviceCode = order.DeviceCode,
            CashierId = order.CashierId,
            CashierName = summary.CashierName,
            CustomerName = summary.CustomerName,
            CustomerPhone = summary.CustomerPhone,
            CreatedAt = summary.CreatedAt,
            TotalAmount = summary.TotalAmount,
            MinimumDownPayment = summary.MinimumDownPayment,
            DownPaymentAmount = summary.DownPaymentAmount,
            PaidAmount = summary.PaidAmount,
            BalanceAmount = summary.BalanceAmount,
            Status = summary.Status,
            UpdatedAt = summary.UpdatedAt,
            Note = order.Note,
        };
    }

    private static InstallmentPickupInfoDto? BuildPickupInfo(InstallmentOrder order) =>
        order.PickedUpAt.HasValue || !string.IsNullOrWhiteSpace(order.PickedUpBy) || !string.IsNullOrWhiteSpace(order.PickupNote)
            ? new InstallmentPickupInfoDto
            {
                PickedUpAt = ToUtcOffset(order.PickedUpAt),
                PickedUpBy = order.PickedUpBy,
                PickupNote = order.PickupNote,
            }
            : null;

    private static InstallmentCancellationInfoDto? BuildCancellationInfo(InstallmentOrder order) =>
        order.CancellationKind.HasValue || order.CancelledAt.HasValue || !string.IsNullOrWhiteSpace(order.CancelledBy) || !string.IsNullOrWhiteSpace(order.CancellationReason)
            ? new InstallmentCancellationInfoDto
            {
                CancellationKind = order.CancellationKind,
                CancelledAt = ToUtcOffset(order.CancelledAt),
                CancelledBy = order.CancelledBy,
                CancellationReason = order.CancellationReason,
            }
            : null;

    private static InstallmentOrderLineDto MapLine(InstallmentOrderLine line) =>
        new()
        {
            InstallmentLineGuid = line.InstallmentLineGuid,
            ProductCode = line.ProductCode,
            ReferenceCode = line.ReferenceCode,
            DisplayName = line.DisplayName,
            LookupCode = line.LookupCode,
            ItemNumber = line.ItemNumber,
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice,
            DiscountAmount = line.DiscountAmount,
            ActualAmount = line.ActualAmount,
        };

    private static InstallmentPaymentDto MapPayment(InstallmentPayment payment) =>
        new()
        {
            PaymentGuid = payment.PaymentGuid,
            Method = payment.Method,
            Amount = payment.Amount,
            Reference = payment.Reference,
            Status = payment.Status,
            RecordedAt = ToUtcOffset(payment.RecordedAt),
            CashierId = payment.CashierId,
            DeviceCode = payment.DeviceCode,
        };

    private static List<string>? NormalizeStoreCodes(IEnumerable<string>? source) =>
        source?
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static DateTimeOffset ToUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToUtcOffset(DateTime? value) =>
        value.HasValue ? ToUtcOffset(value.Value) : null;

    private static ApiResponse<InstallmentOrderDetailResponse> NotFound() =>
        ApiResponse<InstallmentOrderDetailResponse>.Error("分期订单不存在");
}
