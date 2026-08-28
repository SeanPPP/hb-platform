using BlazorApp.Api.Data;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBSalesRecord;
using BlazorApp.Shared.Models.POSM;

namespace BlazorApp.Api.Services;

/// <summary>商品分店日统计共享的来源查询；不依赖任何业务切片。</summary>
internal static class SalesStatisticsProductStoreDailySourceQueries
{
    private const int CommandTimeoutSeconds = 1800;
    private const int HBSalesMainCheckoutDateWindowDays = 7;

    internal static async Task<List<ProductStoreDailySourceRow>> LoadSupplementalReturnRowsAsync(
        POSMSqlSugarContext posmContext,
        DateTime targetDate,
        DateTime nextDate,
        HashSet<string> detailGuidSet)
    {
        var returnTableName = posmContext.Db.EntityMaintenance.GetTableName(typeof(SalesReturnRecord));
        var hasReturnTable = posmContext.Db.DbMaintenance.GetTableInfoList(false)
            .Any(table => string.Equals(
                table.Name,
                returnTableName,
                StringComparison.OrdinalIgnoreCase
            ));
        if (!hasReturnTable)
            return [];

        var returnRows = await posmContext.Db.Queryable<SalesReturnRecord>()
            .LeftJoin<SalesOrder>((returnRow, order) =>
                returnRow.ReturnOrderGuid == order.OrderGuid)
            .LeftJoin<SalesOrderDetail>((returnRow, order, detail) =>
                returnRow.OriginalOrderDetailGuid == detail.OrderDetailGuid)
            .Where((returnRow, order, detail) =>
                order.Status != null
                && (order.Status == 1 || order.Status == 4)
                && order.OrderTime != null
                && order.OrderTime >= targetDate
                && order.OrderTime < nextDate
            )
            .Select((returnRow, order, detail) => new
            {
                returnRow.ReturnDetailGuid,
                ReturnProductCode = returnRow.ProductCode,
                ReturnQuantity = returnRow.ReturnQuantity,
                ReturnAmount = returnRow.ReturnAmount,
                ReturnCreatedTime = returnRow.CreatedTime,
                ReturnUpdatedTime = returnRow.UpdatedTime,
                order.OrderGuid,
                order.BranchCode,
                order.DeviceCode,
                order.OrderTime,
                OrderLastUploadTime = order.LastUploadTime,
                DetailProductCode = detail.ProductCode,
                detail.SupplierCode,
                detail.ProductName,
                detail.Barcode,
            })
            .ToListAsync();

        return returnRows
            .Where(row =>
                string.IsNullOrWhiteSpace(row.ReturnDetailGuid)
                || !detailGuidSet.Contains(row.ReturnDetailGuid)
            )
            .Select(row => new ProductStoreDailySourceRow
            {
                Date = row.OrderTime!.Value.Date,
                OrderGuid = row.OrderGuid,
                DetailGuid = row.ReturnDetailGuid,
                BranchCode = row.BranchCode,
                DeviceCode = row.DeviceCode,
                OrderLastUploadTime = row.OrderLastUploadTime,
                ProductCode = string.IsNullOrWhiteSpace(row.ReturnProductCode)
                    ? row.DetailProductCode
                    : row.ReturnProductCode,
                SupplierCode = row.SupplierCode,
                ProductName = row.ProductName,
                Barcode = row.Barcode,
                Quantity = -Math.Abs(row.ReturnQuantity ?? 0m),
                ActualAmount = -Math.Abs(row.ReturnAmount ?? 0m),
                DetailLastUploadTime = row.ReturnUpdatedTime ?? row.ReturnCreatedTime,
                SourceCreatedAt = row.ReturnCreatedTime,
                SourceUpdatedAt = row.ReturnUpdatedTime,
            })
            .ToList();
    }

    internal static async Task<Dictionary<string, string>> LoadDeviceBranchMapAsync(
        POSMSqlSugarContext posmContext,
        IEnumerable<string?> deviceCodes)
    {
        var targetDeviceCodes = deviceCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetDeviceCodes.Count == 0)
            return [];

        return (await posmContext.Db.Queryable<POSM_设备注册信息表>()
                .Where(device => device.系统设备编号 != null
                    && targetDeviceCodes.Contains(SqlFunc.ToUpper(device.系统设备编号.Trim())))
                .Select(device => new { device.系统设备编号, device.分店代码 })
                .ToListAsync())
            .Where(device => !string.IsNullOrWhiteSpace(device.系统设备编号))
            .GroupBy(device => device.系统设备编号.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(device => device.分店代码)
                    .FirstOrDefault(code => !string.IsNullOrWhiteSpace(code))?.Trim()
                    ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
    }

    internal static async Task<(
        Dictionary<string, decimal> PaymentAmounts,
        Dictionary<string, decimal> DetailAmounts
    )> LoadOrderAmountMapsAsync<T>(
        POSMSqlSugarContext posmContext,
        DateTime startDate,
        DateTime endExclusive,
        IEnumerable<T> detailRows,
        Func<T, string?> orderGuidSelector,
        Func<T, decimal> detailAmountSelector)
    {
        var paymentRows = await posmContext.Db.Queryable<PaymentDetail, SalesOrder>(
                (payment, order) => payment.OrderGuid == order.OrderGuid
            )
            .Where((payment, order) =>
                order.Status != null
                && (order.Status == 1 || order.Status == 4)
                && order.OrderTime != null
                && order.OrderTime >= startDate
                && order.OrderTime < endExclusive
            )
            .GroupBy((payment, order) => payment.OrderGuid)
            .Select((payment, order) => new OrderAmountRow
            {
                OrderGuid = payment.OrderGuid,
                Amount = SqlFunc.AggregateSum(payment.Amount) ?? 0m,
            })
            .ToListAsync();

        var detailAmountRows = detailRows.Select(row => new OrderAmountRow
        {
            OrderGuid = orderGuidSelector(row),
            Amount = detailAmountSelector(row),
        });

        return (
            SalesStatisticsProductStoreDailyDomainRules.BuildOrderAmountMap(paymentRows),
            SalesStatisticsProductStoreDailyDomainRules.BuildOrderAmountMap(detailAmountRows)
        );
    }

    internal static async Task<List<HBSalesStoreAggregateRow>> LoadHBSalesStoreAggregatesAsync(
        HBSalesRecordSqlSugarContext hbSalesContext,
        DateTime targetDate,
        DateTime nextDate)
    {
        var originalCommandTimeout = hbSalesContext.Db.Ado.CommandTimeOut;
        var mainCheckoutDateWindowStart = targetDate.AddDays(
            -HBSalesMainCheckoutDateWindowDays
        );
        var mainCheckoutDateWindowEnd = nextDate.AddDays(HBSalesMainCheckoutDateWindowDays);
        hbSalesContext.Db.Ado.CommandTimeOut = Math.Max(
            originalCommandTimeout,
            CommandTimeoutSeconds
        );
        try
        {
            return await hbSalesContext.Db.Queryable<SalesOrderMain>()
                .LeftJoin<SalesOrderDetailRecord>((main, detail) =>
                    main.B销售单号 == detail.B销售单号)
                .Where((main, detail) =>
                    detail.B结账日期.HasValue
                    && detail.B结账日期.Value >= targetDate
                    && detail.B结账日期.Value < nextDate
                    && main.B结账日期.HasValue
                    && main.B结账日期.Value >= mainCheckoutDateWindowStart
                    && main.B结账日期.Value < mainCheckoutDateWindowEnd
                    && (main.B单据类型 == null || main.B单据类型.Trim() != "2")
                    && detail.B分店代码 != null
                    && detail.B分店代码.Trim() != ""
                )
                .GroupBy((main, detail) => detail.B分店代码!.Trim())
                .Select((main, detail) => new HBSalesStoreAggregateRow
                {
                    BranchCode = detail.B分店代码!.Trim(),
                    TotalAmount = SqlFunc.AggregateSum(
                        (detail.B合计金额 ?? 0m) * SqlFunc.IIF(
                            main.B单据类型 != null
                                && (main.B单据类型.Trim() == "3"
                                    || main.B单据类型.Trim() == "4"),
                            -1m,
                            1m
                        )
                    ),
                    TotalQuantity = SqlFunc.AggregateSum(
                        (detail.B数量 ?? 0m) * SqlFunc.IIF(
                            main.B单据类型 != null
                                && (main.B单据类型.Trim() == "3"
                                    || main.B单据类型.Trim() == "4"),
                            -1m,
                            1m
                        )
                    ),
                    OrderCount = SqlFunc.AggregateDistinctCount(main.B销售单号),
                })
                .ToListAsync();
        }
        finally
        {
            hbSalesContext.Db.Ado.CommandTimeOut = originalCommandTimeout;
        }
    }

    internal static async Task<Posm2025DailySnapshot> Load2025PosmDailySnapshotAsync(
        POSMSqlSugarContext posmContext,
        DateTime date)
    {
        var targetDate = date.Date;
        if (targetDate.Year != 2025)
            throw new ArgumentException("POSM 预载入口只接受 2025 日期", nameof(date));

        var nextDate = targetDate.AddDays(1);
        var orderRows = await posmContext.Db.Queryable<SalesOrder>()
            .Where(order =>
                order.Status != null
                && (order.Status == 1 || order.Status == 4)
                && order.OrderTime != null
                && order.OrderTime >= targetDate
                && order.OrderTime < nextDate)
            .Select(order => new StoreStatisticOrderRow
            {
                OrderGuid = order.OrderGuid,
                BranchCode = order.BranchCode,
                DeviceCode = order.DeviceCode,
                OrderTime = order.OrderTime,
                Status = order.Status,
                LastUploadTime = order.LastUploadTime,
                CreatedAt = order.CreatedTime,
                UpdatedAt = order.UpdatedTime,
            })
            .ToListAsync();
        var detailRows = await posmContext.Db.Queryable<SalesOrder>()
            .LeftJoin<SalesOrderDetail>((order, detail) => order.OrderGuid == detail.OrderGuid)
            .Where(order =>
                order.Status != null
                && (order.Status == 1 || order.Status == 4)
                && order.OrderTime != null
                && order.OrderTime >= targetDate
                && order.OrderTime < nextDate)
            .Select((order, detail) => new ProductStoreDailySourceRow
            {
                Date = order.OrderTime!.Value.Date,
                OrderGuid = order.OrderGuid,
                DetailGuid = detail.OrderDetailGuid,
                BranchCode = order.BranchCode,
                DeviceCode = order.DeviceCode,
                OrderLastUploadTime = order.LastUploadTime,
                ProductCode = detail.ProductCode,
                SupplierCode = detail.SupplierCode,
                ProductName = detail.ProductName,
                Barcode = detail.Barcode,
                Quantity = detail.Quantity ?? 0m,
                ActualAmount = detail.ActualAmount ?? 0m,
                DetailLastUploadTime = detail.LastUploadTime,
                SourceCreatedAt = detail.CreatedTime,
                SourceUpdatedAt = detail.UpdatedTime,
            })
            .ToListAsync();
        var paymentRows = await posmContext.Db.Queryable<PaymentDetail, SalesOrder>(
                (payment, order) => payment.OrderGuid == order.OrderGuid)
            .Where((payment, order) =>
                order.Status != null
                && (order.Status == 1 || order.Status == 4)
                && order.OrderTime != null
                && order.OrderTime >= targetDate
                && order.OrderTime < nextDate)
            .Select((payment, order) => new StoreStatisticPaymentRow
            {
                PaymentGuid = payment.PaymentGuid,
                OrderGuid = payment.OrderGuid,
                BranchCode = order.BranchCode,
                DeviceCode = order.DeviceCode,
                Amount = payment.Amount ?? 0m,
                CreatedAt = payment.CreatedTime,
                UpdatedAt = payment.UpdatedTime,
                LastUploadTime = payment.LastUploadTime,
            })
            .ToListAsync();
        var detailGuidSet = detailRows
            .Select(row => row.DetailGuid)
            .Where(guid => !string.IsNullOrWhiteSpace(guid))
            .Select(guid => guid!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var supplementalReturnRows = await LoadSupplementalReturnRowsAsync(
            posmContext,
            targetDate,
            nextDate,
            detailGuidSet
        );
        var deviceBranchMap = await LoadDeviceBranchMapAsync(
            posmContext,
            detailRows.Select(row => row.DeviceCode)
                .Concat(paymentRows.Select(row => row.DeviceCode))
                .Concat(orderRows.Select(row => row.DeviceCode))
                .Concat(supplementalReturnRows.Select(row => row.DeviceCode))
        );
        var signature = SalesStatisticsProductStoreDailyDomainRules
            .CreatePosm2025DailySnapshotSignature(
                targetDate,
                orderRows,
                detailRows,
                paymentRows,
                supplementalReturnRows
            );
        return new Posm2025DailySnapshot(
            detailRows,
            supplementalReturnRows,
            paymentRows,
            orderRows,
            deviceBranchMap,
            signature
        );
    }
}
