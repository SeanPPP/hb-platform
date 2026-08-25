using BlazorApp.Shared.Models.POSM;
using Hbpos.Api.Data;
using Hbpos.Contracts.Linkly;
using Hbpos.Contracts.Orders;
using SqlSugar;

namespace Hbpos.Api.Services;

public interface IOrderHistoryService
{
    Task<OrderHistoryQueryResponse> QueryAsync(
        OrderHistoryQueryRequest request,
        CancellationToken cancellationToken);

    Task<OrderHistoryDetailsDto?> GetDetailsAsync(
        Guid orderGuid,
        CancellationToken cancellationToken);
}

public sealed class OrderHistoryService(IOrderHistoryRepository repository) : IOrderHistoryService
{
    public Task<OrderHistoryQueryResponse> QueryAsync(
        OrderHistoryQueryRequest request,
        CancellationToken cancellationToken)
    {
        return repository.QueryAsync(request, cancellationToken);
    }

    public Task<OrderHistoryDetailsDto?> GetDetailsAsync(
        Guid orderGuid,
        CancellationToken cancellationToken)
    {
        return repository.GetDetailsAsync(orderGuid, cancellationToken);
    }
}

public interface IOrderHistoryRepository
{
    Task<OrderHistoryQueryResponse> QueryAsync(
        OrderHistoryQueryRequest request,
        CancellationToken cancellationToken);

    Task<OrderHistoryDetailsDto?> GetDetailsAsync(
        Guid orderGuid,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, OrderHistoryDetailsDto>> GetDetailsByOrderGuidsAsync(
        IReadOnlyCollection<Guid> orderGuids,
        CancellationToken cancellationToken);
}

public sealed class SqlSugarOrderHistoryRepository(HbposSqlSugarContext dbContext) : IOrderHistoryRepository
{
    public async Task<OrderHistoryQueryResponse> QueryAsync(
        OrderHistoryQueryRequest request,
        CancellationToken cancellationToken)
    {
        var storeCode = request.StoreCode.Trim();
        var query = dbContext.PosmDb.Queryable<SalesOrder>()
            .Where(x => x.BranchCode == storeCode);

        if (!string.IsNullOrWhiteSpace(request.DeviceCode))
        {
            var deviceCode = request.DeviceCode.Trim();
            query = query.Where(x => x.DeviceCode == deviceCode);
        }

        if (request.SoldFrom is not null)
        {
            var soldFrom = request.SoldFrom.Value.UtcDateTime;
            query = query.Where(x => x.OrderTime >= soldFrom);
        }

        if (request.SoldTo is not null)
        {
            var soldTo = request.SoldTo.Value.UtcDateTime;
            query = query.Where(x => x.OrderTime <= soldTo);
        }

        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            var normalizedGuidKeyword = keyword.Replace("-", string.Empty, StringComparison.Ordinal);
            // 仅由连字符组成的输入既不能搜索空串，也不能用 "-" 匹配所有标准格式 GUID。
            var canSearchOrderGuid = normalizedGuidKeyword.Length > 0;
            var itemNumberMarker = $"itemNo={keyword}";
            var itemNumberSuffix = $";{itemNumberMarker}";

            // 明细条件必须关联到已按门店、终端和日期收窄的订单，避免先扫描并物化全库明细 GUID。
            query = query.Where(x =>
                // 订单号子串搜索按字面量执行，避免 %, _ 等字符把 LIKE 扩大为全表命中。
                (canSearchOrderGuid && x.OrderGuid != null && SqlFunc.CharIndexNew(x.OrderGuid, keyword) > 0)
                || (canSearchOrderGuid && x.OrderGuid != null && SqlFunc.CharIndexNew(x.OrderGuid, normalizedGuidKeyword) > 0)
                || SqlFunc.Subqueryable<SalesOrderDetail>()
                    .Where(line => line.OrderGuid == x.OrderGuid
                        && (line.Barcode == keyword
                            || line.ProductCode == keyword
                            || (line.Remark != null
                                // 用参数化的末尾位置等值比较，避免 EndsWith 翻译成未转义 LIKE。
                                && (line.Remark == itemNumberMarker
                                    || SqlFunc.CharIndexNew(line.Remark, itemNumberSuffix)
                                        == SqlFunc.Length(line.Remark) - itemNumberSuffix.Length + 1))))
                    .Any());
        }

        var take = Math.Clamp(request.Take, 1, 200);
        var orders = await query
            .OrderByDescending(x => x.OrderTime)
            .Take(take)
            .ToListAsync(cancellationToken);
        var orderGuids = orders.Select(x => x.OrderGuid).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        var payments = orderGuids.Count == 0
            ? new List<PaymentDetail>()
            : await dbContext.PosmDb.Queryable<PaymentDetail>()
                .Where(x => orderGuids.Contains(x.OrderGuid))
                .ToListAsync(cancellationToken);

        var paymentLabels = payments
            .Where(x => !string.IsNullOrWhiteSpace(x.OrderGuid))
            .GroupBy(x => x.OrderGuid)
            .ToDictionary(
                x => x.Key!,
                x => string.Join(", ", x.Select(payment => ((PaymentMethodKind)payment.PaymentMethod).ToString()).Distinct()));

        return new OrderHistoryQueryResponse(orders.Select(order => new OrderHistorySummaryDto(
            ParseGuid(order.OrderGuid),
            order.BranchCode ?? string.Empty,
            order.DeviceCode ?? string.Empty,
            order.CashierName ?? string.Empty,
            ToDateTimeOffset(order.OrderTime),
            Amount(order.TotalAmount),
            Amount(order.DiscountAmount),
            Amount(order.ActualAmount),
            Count(order.ItemCount),
            order.OrderGuid is not null && paymentLabels.TryGetValue(order.OrderGuid, out var paymentSummary) ? paymentSummary : string.Empty,
            FormatStatus(order.Status ?? 0))).ToList());
    }

    public async Task<OrderHistoryDetailsDto?> GetDetailsAsync(
        Guid orderGuid,
        CancellationToken cancellationToken)
    {
        var orders = await GetDetailsByOrderGuidsAsync([orderGuid], cancellationToken);
        return orders.GetValueOrDefault(orderGuid);
    }

    public async Task<IReadOnlyDictionary<Guid, OrderHistoryDetailsDto>> GetDetailsByOrderGuidsAsync(
        IReadOnlyCollection<Guid> orderGuids,
        CancellationToken cancellationToken)
    {
        var orderGuidTexts = orderGuids
            .Where(orderGuid => orderGuid != Guid.Empty)
            .Distinct()
            .Select(orderGuid => orderGuid.ToString("D"))
            .ToList();
        if (orderGuidTexts.Count == 0)
        {
            return new Dictionary<Guid, OrderHistoryDetailsDto>();
        }

        var orders = await dbContext.PosmDb.Queryable<SalesOrder>()
            .Where(order => order.OrderGuid != null && orderGuidTexts.Contains(order.OrderGuid))
            .ToListAsync(cancellationToken);
        if (orders.Count == 0)
        {
            return new Dictionary<Guid, OrderHistoryDetailsDto>();
        }

        var foundOrderGuidTexts = orders
            .Select(order => order.OrderGuid)
            .Where(orderGuid => !string.IsNullOrWhiteSpace(orderGuid))
            .Select(orderGuid => orderGuid!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var lines = await dbContext.PosmDb.Queryable<SalesOrderDetail>()
            .Where(line => foundOrderGuidTexts.Contains(line.OrderGuid))
            .ToListAsync(cancellationToken);
        var payments = await dbContext.PosmDb.Queryable<PaymentDetail>()
            .Where(payment => payment.OrderGuid != null && foundOrderGuidTexts.Contains(payment.OrderGuid))
            .ToListAsync(cancellationToken);
        var bankTransactions = await dbContext.PosmDb.Queryable<BankTransaction>()
            .Where(transaction => transaction.OrderGuid != null && foundOrderGuidTexts.Contains(transaction.OrderGuid))
            .ToListAsync(cancellationToken);

        var linesByOrder = lines
            .Where(line => !string.IsNullOrWhiteSpace(line.OrderGuid))
            .GroupBy(line => line.OrderGuid!)
            .ToDictionary(
                group => group.Key,
                group => group.ToList(),
                StringComparer.OrdinalIgnoreCase);
        var paymentsByOrder = payments
            .Where(payment => !string.IsNullOrWhiteSpace(payment.OrderGuid))
            .GroupBy(payment => payment.OrderGuid!)
            .ToDictionary(
                group => group.Key,
                group => group.ToList(),
                StringComparer.OrdinalIgnoreCase);
        var bankTransactionsByOrder = bankTransactions
            .Where(transaction => !string.IsNullOrWhiteSpace(transaction.OrderGuid))
            .GroupBy(transaction => transaction.OrderGuid!)
            .ToDictionary(
                group => group.Key,
                group => group.ToList(),
                StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<Guid, OrderHistoryDetailsDto>();
        foreach (var order in orders)
        {
            if (!Guid.TryParse(order.OrderGuid, out var orderGuid))
            {
                continue;
            }

            var orderGuidText = order.OrderGuid!;
            result[orderGuid] = MapDetails(
                orderGuid,
                order,
                linesByOrder.GetValueOrDefault(orderGuidText) ?? [],
                paymentsByOrder.GetValueOrDefault(orderGuidText) ?? [],
                bankTransactionsByOrder.GetValueOrDefault(orderGuidText) ?? []);
        }

        return result;
    }

    private static OrderHistoryDetailsDto MapDetails(
        Guid orderGuid,
        SalesOrder order,
        IReadOnlyList<SalesOrderDetail> lines,
        IReadOnlyList<PaymentDetail> payments,
        IReadOnlyList<BankTransaction> bankTransactions)
    {
        var bankTransactionsByPayment = bankTransactions
            .Where(transaction => !string.IsNullOrWhiteSpace(transaction.PaymentGuid))
            .GroupBy(transaction => transaction.PaymentGuid!)
            .ToDictionary(
                group => group.Key,
                group => group.ToList(),
                StringComparer.OrdinalIgnoreCase);

        return new OrderHistoryDetailsDto(
            orderGuid,
            order.BranchCode ?? string.Empty,
            order.DeviceCode ?? string.Empty,
            order.CashierName ?? string.Empty,
            ToDateTimeOffset(order.OrderTime),
            Amount(order.TotalAmount),
            Amount(order.DiscountAmount),
            Amount(order.ActualAmount),
            lines.Select(line => new OrderHistoryLineDto(
                ParseGuid(line.OrderDetailGuid),
                line.ProductCode ?? string.Empty,
                line.ReferenceGUID,
                line.ProductName ?? string.Empty,
                line.Barcode ?? string.Empty,
                ExtractItemNo(line.Remark),
                Count(line.Quantity),
                Amount(line.Price),
                Amount(line.DiscountAmount),
                Amount(line.ActualAmount),
                OrderLineKind.Sale,
                null,
                null,
                null)).ToList(),
            payments.Select(payment => new OrderHistoryPaymentDto(
                ParseGuid(payment.PaymentGuid),
                (PaymentMethodKind)payment.PaymentMethod,
                Amount(payment.Amount),
                payment.Reference,
                payment.PaymentGuid is not null && bankTransactionsByPayment.TryGetValue(payment.PaymentGuid, out var cardTransactions)
                    ? cardTransactions.Select(transaction => ToCardTransactionDto(payment.Reference, transaction)).ToList()
                    : null)).ToList());
    }

    private static CardTransactionDto ToCardTransactionDto(string? paymentReference, BankTransaction transaction)
    {
        return new CardTransactionDto(
            InferCardProcessor(paymentReference),
            transaction.TxnRef,
            transaction.AuthCode,
            transaction.CardType,
            transaction.CardBIN,
            transaction.CardNumber,
            transaction.Caid,
            transaction.ResponseCode,
            transaction.ResponseText,
            transaction.Stan,
            transaction.BankDateTime is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(transaction.BankDateTime.Value, DateTimeKind.Utc)),
            Amount(transaction.Amount),
            transaction.ReceiptText,
            TryGetLinklyRefundReference(paymentReference));
    }

    private static string? TryGetLinklyRefundReference(string? paymentReference)
    {
        var backendRefundReference = LinklyBackendPaymentReference.TryGetRefundReference(paymentReference);
        if (!string.IsNullOrWhiteSpace(backendRefundReference))
        {
            return backendRefundReference;
        }

        var parts = paymentReference?.Trim().Split(':', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];
        return parts.Length >= 3 &&
            string.Equals(parts[0], "ANZCLOUD", StringComparison.OrdinalIgnoreCase)
                ? parts[2]
                : null;
    }

    private static string InferCardProcessor(string? paymentReference)
    {
        var displayReference = CardRefundReference.GetDisplayReference(paymentReference);
        if (displayReference?.StartsWith("ANZ:", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "ANZ";
        }

        return displayReference?.StartsWith("SQ", StringComparison.OrdinalIgnoreCase) == true
            ? "Square"
            : "Card";
    }

    private static Guid ParseGuid(string? value)
    {
        return Guid.TryParse(value, out var guid) ? guid : Guid.Empty;
    }

    private static string? ExtractItemNo(string? remark)
    {
        if (string.IsNullOrWhiteSpace(remark))
        {
            return null;
        }

        const string marker = "itemNo=";
        var index = remark.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var start = index + marker.Length;
        var end = remark.IndexOf(';', start);
        return end < 0 ? remark[start..].Trim() : remark[start..end].Trim();
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime? value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value ?? DateTime.MinValue, DateTimeKind.Utc));
    }

    private static decimal Amount(decimal? value)
    {
        return value ?? 0m;
    }

    private static int Count(int? value)
    {
        return value ?? 0;
    }

    private static string FormatStatus(int status)
    {
        return status switch
        {
            1 => "Completed",
            2 => "Voided",
            _ => status.ToString()
        };
    }
}
