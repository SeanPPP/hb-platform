using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBSalesRecord;
using BlazorApp.Shared.Models.POSM;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>日快照来源边界：只运行在现有隔离 SQL Server，覆盖一次性全店全商品读取和来源围栏。</summary>
public sealed partial class BatchProductSalesAnalysisSqlServerIntegrationTests
{
    [BatchSalesSqlServerFact]
    public async Task SourceReader_CapturePreparedAsync完成后恢复三库Ado令牌且后续HBweb写入不继承已取消令牌()
    {
        var day = new DateTime(2026, 1, 9);
        var reader = new BatchProductSalesDiscountSnapshotSourceReader(_catalog!, _posm!, _hbs!);
        _catalog.Ado.RemoveCancellationToken();
        _posm.Ado.RemoveCancellationToken();
        _hbs.Ado.RemoveCancellationToken();
        using var readCancellation = new CancellationTokenSource();

        await reader.CapturePreparedAsync(day, readCancellation.Token);
        readCancellation.Cancel();

        Assert.Null(_catalog.Ado.CancellationToken);
        Assert.Null(_posm.Ado.CancellationToken);
        Assert.Null(_hbs.Ado.CancellationToken);
        Assert.Equal(1, await _catalog.Ado.GetIntAsync("SELECT 1"));
    }

    [BatchSalesSqlServerFact]
    public async Task SourceReader_CapturePreparedAsync已取消或查询失败时仍恢复三库Ado令牌()
    {
        var day = new DateTime(2026, 1, 10);
        var reader = new BatchProductSalesDiscountSnapshotSourceReader(_catalog!, _posm!, _hbs!);
        _catalog.Ado.RemoveCancellationToken();
        _posm.Ado.RemoveCancellationToken();
        _hbs.Ado.RemoveCancellationToken();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.CapturePreparedAsync(day, cancelled.Token));

        Assert.Null(_catalog.Ado.CancellationToken);
        Assert.Null(_posm.Ado.CancellationToken);
        Assert.Null(_hbs.Ado.CancellationToken);
        Assert.Equal(1, await _catalog.Ado.GetIntAsync("SELECT 1"));
    }

    [BatchSalesSqlServerFact]
    public async Task SourceReader_ReadPreparedDayAsync嵌套Capture后恢复外层三库Ado令牌()
    {
        var day = new DateTime(2026, 1, 11);
        var reader = new BatchProductSalesDiscountSnapshotSourceReader(_catalog!, _posm!, _hbs!);
        using var outer = new CancellationTokenSource();
        using var inner = new CancellationTokenSource();
        _catalog.Ado.CancellationToken = outer.Token;
        _posm.Ado.CancellationToken = outer.Token;
        _hbs.Ado.CancellationToken = outer.Token;
        try
        {
            var prepared = await reader.CapturePreparedAsync(day, inner.Token);
            await reader.ReadPreparedDayAsync(prepared, inner.Token);

            Assert.Equal(outer.Token, _catalog.Ado.CancellationToken);
            Assert.Equal(outer.Token, _posm.Ado.CancellationToken);
            Assert.Equal(outer.Token, _hbs.Ado.CancellationToken);
        }
        finally
        {
            _catalog.Ado.RemoveCancellationToken();
            _posm.Ado.RemoveCancellationToken();
            _hbs.Ado.RemoveCancellationToken();
        }
    }

    [BatchSalesSqlServerFact]
    public async Task SourceReader_ReadDayAsync_全天全店全商品支付分摊同日退货排重及2025别名保持事实口径()
    {
        var day = new DateTime(2025, 6, 10);
        await SeedProductsAsync(("P1", "001", null), ("P2", "002", null), ("P3", "003", null));
        await _catalog!.Insertable(new ProductSetCode
        {
            ProductCode = "P3", SetProductCode = "P3", SetItemNumber = "P3-ALIAS", SetBarcode = "HBS-ALIAS",
            IsDeleted = false, IsActive = true,
        }).ExecuteCommandAsync();
        await _posm!.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = "source-reader-device", 系统设备编号 = "D1", 分店代码 = "S1",
            设备类型 = "POS", 设备系统 = "Windows", 设备状态 = 1, 是否允许交易 = true,
            设备授权码 = "source-reader", 创建时间 = day,
        }).ExecuteCommandAsync();
        await _posm.Insertable(new[]
        {
            new SalesOrder { OrderGuid = "O1", OrderTime = day.AddHours(9), BranchCode = "", DeviceCode = "D1", Status = 1 },
            new SalesOrder { OrderGuid = "O2", OrderTime = day.AddHours(10), BranchCode = "S2", Status = 1 },
            new SalesOrder { OrderGuid = "R1", OrderTime = day.AddHours(11), BranchCode = "", DeviceCode = "D1", Status = 1 },
        }).ExecuteCommandAsync();
        await _posm.Insertable(new[]
        {
            new PaymentDetail { PaymentGuid = "PAY-O1", OrderGuid = "O1", Amount = 15m },
            new PaymentDetail { PaymentGuid = "PAY-O2", OrderGuid = "O2", Amount = 10m },
            new PaymentDetail { PaymentGuid = "PAY-R1", OrderGuid = "R1", Amount = -5m },
        }).ExecuteCommandAsync();
        await _posm.Insertable(new[]
        {
            // 实付 14.99 只是分母的一部分；P1 统计金额必须使用 15 * 14.99 / (14.99 + 5.01)。
            new SalesOrderDetail { OrderDetailGuid = "D1", OrderGuid = "O1", ProductCode = "P1", Quantity = 1, Price = 20m, Subtotal = 20m, ActualAmount = 14.99m, DiscountAmount = 5.01m },
            new SalesOrderDetail { OrderDetailGuid = "D2", OrderGuid = "O1", ProductCode = "P2", Quantity = 1, Price = 10m, Subtotal = 10m, ActualAmount = 5.01m, DiscountAmount = 4.99m },
            new SalesOrderDetail { OrderDetailGuid = "D3", OrderGuid = "O2", ProductCode = "P2", Quantity = 1, Price = 10m, Subtotal = 10m, ActualAmount = 10m, DiscountAmount = 0m },
            new SalesOrderDetail { OrderDetailGuid = "RET-P1", OrderGuid = "R1", ProductCode = "P1", Quantity = -1, Price = 20m, Subtotal = 20m, ActualAmount = -5m, DiscountAmount = 15m },
        }).ExecuteCommandAsync();
        await _posm.Insertable(new[]
        {
            // 已在 R1 明细中出现：同日全部 DetailGuid 集合必须排除它。
            new SalesReturnRecord { ReturnDetailGuid = "RET-P1", ReturnOrderGuid = "R1", OriginalOrderGuid = "O1", OriginalOrderDetailGuid = "D1", ProductCode = "P1", ReturnQuantity = 1m, ReturnAmount = 5m },
            // 不在当天明细：仍须作为补充退货计入 P2。
            new SalesReturnRecord { ReturnDetailGuid = "SUP-P2", ReturnOrderGuid = "R1", OriginalOrderGuid = "O1", OriginalOrderDetailGuid = "D2", ProductCode = "P2", ReturnQuantity = 1m, ReturnAmount = 5m },
        }).ExecuteCommandAsync();
        await _hbs!.Insertable(new SalesOrderMain
        {
            ID = 1, B销售单号 = "H1", B单据类型 = "1", B结账日期 = day,
        }).ExecuteCommandAsync();
        await _hbs.Insertable(new SalesOrderDetailRecord
        {
            ID = 1, B销售单号 = "H1", B分店代码 = "S1", B结账日期 = day,
            B条形码 = "HBS-ALIAS", B数量 = 2m, B单价 = 10m, B原价合计金额 = 20m, B合计金额 = 20m,
        }).ExecuteCommandAsync();

        var rows = await new BatchProductSalesDiscountSnapshotSourceReader(_catalog, _posm, _hbs)
            .ReadDayAsync(day, CancellationToken.None);

        Assert.Contains(rows, row => row.BranchCode == "S1" && row.ProductCode == "P1");
        Assert.Contains(rows, row => row.BranchCode == "S2" && row.ProductCode == "P2");
        var p1 = rows.Where(row => row.BranchCode == "S1" && row.ProductCode == "P1").ToList();
        Assert.Equal(0m, p1.Sum(row => row.Quantity));
        Assert.Equal(6.2425m, p1.Sum(row => row.SalesAmount));
        Assert.Equal(1m, p1.Sum(row => row.ReturnQuantity));
        Assert.Equal(1, p1.Sum(row => row.UnknownRowCount));
        var p2S1 = rows.Where(row => row.BranchCode == "S1" && row.ProductCode == "P2").ToList();
        Assert.Equal(0m, p2S1.Sum(row => row.Quantity));
        Assert.Equal(-1.2425m, p2S1.Sum(row => row.SalesAmount));
        Assert.Equal(1m, p2S1.Sum(row => row.ReturnQuantity));
        var hbsAlias = Assert.Single(rows.Where(row => row.BranchCode == "S1" && row.ProductCode == "P3"));
        Assert.Equal(2m, hbsAlias.RegularQuantity);
        Assert.Equal(20m, hbsAlias.SalesAmount);
    }

    [BatchSalesSqlServerFact]
    public async Task SourceReader_GetSourceVersionAsync_HBS当天普通明细折扣率修正必须失效()
    {
        var day = new DateTime(2025, 2, 3);
        await SeedProductsAsync(("P1", "001", null));
        await _hbs!.Insertable(new SalesOrderMain
        {
            ID = 31, B销售单号 = "H-RATE", B单据类型 = "1", B结账日期 = day,
        }).ExecuteCommandAsync();
        await _hbs.Insertable(new SalesOrderDetailRecord
        {
            ID = 31, B销售单号 = "H-RATE", B分店代码 = "S1", B结账日期 = day, B产品编号 = "P1",
            B数量 = 1m, B单价 = 10m, B原价合计金额 = 10m, B合计金额 = 10m, B折扣率 = 0m,
        }).ExecuteCommandAsync();
        var reader = new BatchProductSalesDiscountSnapshotSourceReader(_catalog!, _posm!, _hbs);

        var before = await reader.GetSourceVersionAsync(day, CancellationToken.None);
        await _hbs.Updateable<SalesOrderDetailRecord>().SetColumns(row => row.B折扣率 == 0.5m)
            .Where(row => row.ID == 31).ExecuteCommandAsync();

        Assert.NotEqual(before, await reader.GetSourceVersionAsync(day, CancellationToken.None));
    }

    [BatchSalesSqlServerFact]
    public async Task SourceReader_GetSourceVersionAsync_HBS明细BigintId物化后保持签名且SQL不生成NvarcharMaxCast()
    {
        var day = new DateTime(2025, 2, 4);
        const long detailId = (long)int.MaxValue + 1;
        await SeedProductsAsync(("P1", "001", null));
        await _hbs!.Insertable(new SalesOrderMain
        {
            ID = 32, B销售单号 = "H-LONG-ID", B单据类型 = "1", B结账日期 = day,
        }).ExecuteCommandAsync();
        // CodeFirst 为隔离表创建了 int 主键；生产列为 bigint，先精确重建该测试库的主键以复现读取边界。
        await _hbs.Ado.ExecuteCommandAsync("""
            ALTER TABLE dbo.[B销售清单详情表副本] DROP CONSTRAINT [PK_B销售清单详情表副本_ID];
            ALTER TABLE dbo.[B销售清单详情表副本] ALTER COLUMN [ID] bigint NOT NULL;
            ALTER TABLE dbo.[B销售清单详情表副本] ADD CONSTRAINT [PK_B销售清单详情表副本_ID] PRIMARY KEY ([ID]);
            """);
        await _hbs.Ado.ExecuteCommandAsync($"""
            INSERT INTO dbo.[B销售清单详情表副本]
                ([ID], [B销售单号], [B分店代码], [B结账日期], [B产品编号], [B数量], [B单价], [B原价合计金额], [B合计金额], [B折扣率])
            VALUES ({detailId}, N'H-LONG-ID', N'S1', '{day:yyyy-MM-dd}', N'P1', 1, 10, 10, 10, 0)
            """);
        var reads = new List<string>();
        _hbs.Aop.OnLogExecuting = (sql, _) =>
        {
            if (sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("B销售清单详情表副本", StringComparison.Ordinal))
                reads.Add(sql);
        };
        try
        {
            var reader = new BatchProductSalesDiscountSnapshotSourceReader(_catalog!, _posm!, _hbs);
            var before = await reader.GetSourceVersionAsync(day, CancellationToken.None);
            await _hbs.Ado.ExecuteCommandAsync($"UPDATE dbo.[B销售清单详情表副本] SET [B折扣率] = 0.5 WHERE [ID] = {detailId}");
            var after = await reader.GetSourceVersionAsync(day, CancellationToken.None);

            Assert.NotEqual(before, after);
        }
        finally
        {
            _hbs.Aop.OnLogExecuting = null;
        }
        Assert.NotEmpty(reads);
        Assert.DoesNotContain(reads, sql => sql.Contains("nvarchar(max)", StringComparison.OrdinalIgnoreCase));
    }

    [BatchSalesSqlServerFact]
    public async Task SourceReader_GetSourceVersionAsync_支付折扣和设备映射的无时间戳修正必须失效()
    {
        var day = new DateTime(2025, 1, 2);
        await SeedProductsAsync(("P1", "001", null));
        await _posm!.Insertable(new POSM_设备注册信息表
        {
            设备硬件识别码 = "version-device", 系统设备编号 = "D1", 分店代码 = "S1",
            设备类型 = "POS", 设备系统 = "Windows", 设备状态 = 1, 是否允许交易 = true,
            设备授权码 = "version", 创建时间 = day,
        }).ExecuteCommandAsync();
        await _posm.Insertable(new[]
        {
            new SalesOrder { OrderGuid = "O1", OrderTime = day, BranchCode = "", DeviceCode = "D1", Status = 1 },
            new SalesOrder { OrderGuid = "R1", OrderTime = day.AddHours(1), BranchCode = "", DeviceCode = "D1", Status = 1 },
            // 原单不属于目标日；补充退货的折扣判定仍会读取这条明细。
            new SalesOrder { OrderGuid = "ORIGINAL", OrderTime = day.AddDays(-1), BranchCode = "S1", DeviceCode = "D1", Status = 1 },
        }).ExecuteCommandAsync();
        await _posm.Insertable(new PaymentDetail { PaymentGuid = "PAY-1", OrderGuid = "O1", Amount = 15m }).ExecuteCommandAsync();
        await _posm.Insertable(new[]
        {
            new SalesOrderDetail
            {
                OrderDetailGuid = "D1", OrderGuid = "O1", ProductCode = "P1", Quantity = 1,
                Price = 20m, Subtotal = 20m, ActualAmount = 14.99m, DiscountAmount = 5.01m,
            },
            new SalesOrderDetail
            {
                OrderDetailGuid = "ORIGINAL-D1", OrderGuid = "ORIGINAL", ProductCode = "P1", Quantity = 1,
                SupplierCode = "ORIGINAL-A", Price = 20m, Subtotal = 20m, ActualAmount = 20m, DiscountAmount = 0m, DiscountRate = 0m,
            },
        }).ExecuteCommandAsync();
        await _posm.Insertable(new SalesReturnRecord
        {
            ReturnDetailGuid = "SUP-R1", ReturnOrderGuid = "R1", OriginalOrderGuid = "ORIGINAL",
            OriginalOrderDetailGuid = "ORIGINAL-D1", ProductCode = "P1", ReturnQuantity = 1m, ReturnAmount = 20m,
        }).ExecuteCommandAsync();
        // 退货主单的原销售单号和原单详情故意带相反侧的空格，保持与 FactReader LTRIM/RTRIM 关联一致。
        await _hbs!.Insertable(new[]
        {
            new SalesOrderMain { ID = 10, B销售单号 = "H-RETURN", B原销售单号 = " H-ORIGINAL", B单据类型 = "3", B结账日期 = day },
            new SalesOrderMain { ID = 11, B销售单号 = "H-ORIGINAL ", B单据类型 = "1", B结账日期 = day.AddDays(-1) },
        }).ExecuteCommandAsync();
        await _hbs.Insertable(new[]
        {
            new SalesOrderDetailRecord
            {
                ID = 10, B销售单号 = "H-RETURN", B分店代码 = "S1", B结账日期 = day, B产品编号 = "P1",
                B数量 = 1m, B单价 = 20m, B原价合计金额 = 20m, B合计金额 = 20m, B折扣率 = 0m,
            },
            new SalesOrderDetailRecord
            {
                ID = 11, B销售单号 = "H-ORIGINAL ", B分店代码 = "S1", B结账日期 = day.AddDays(-1), B产品编号 = "P1",
                B数量 = 1m, B单价 = 20m, B原价合计金额 = 20m, B合计金额 = 20m, B折扣率 = 0m,
            },
        }).ExecuteCommandAsync();
        var reader = new BatchProductSalesDiscountSnapshotSourceReader(_catalog, _posm, _hbs!);

        var first = await reader.GetSourceVersionAsync(day, CancellationToken.None);
        Assert.Equal(first, await reader.GetSourceVersionAsync(day, CancellationToken.None));
        await _posm.Updateable<PaymentDetail>().SetColumns(row => row.Amount == 16m)
            .Where(row => row.PaymentGuid == "PAY-1").ExecuteCommandAsync();
        var afterPayment = await reader.GetSourceVersionAsync(day, CancellationToken.None);
        Assert.NotEqual(first, afterPayment);
        await _posm.Updateable<SalesOrderDetail>().SetColumns(row => row.DiscountAmount == 4.01m)
            .Where(row => row.OrderDetailGuid == "D1").ExecuteCommandAsync();
        var afterDiscount = await reader.GetSourceVersionAsync(day, CancellationToken.None);
        Assert.NotEqual(afterPayment, afterDiscount);
        await _posm.Updateable<POSM_设备注册信息表>().SetColumns(row => row.分店代码 == "S9")
            .Where(row => row.系统设备编号 == "D1").ExecuteCommandAsync();
        var afterDevice = await reader.GetSourceVersionAsync(day, CancellationToken.None);
        Assert.NotEqual(afterDiscount, afterDevice);
        // FactReader 以规范化的明细编码与映射表原键相等关联；前导空格映射不会命中，
        // 因而不能让无关的脏数据误触发日快照重算。
        await _posm.Insertable(new PosmProductSupplierMapping { ProductCode = " P1", LocalSupplierCode = "IGNORED" })
            .ExecuteCommandAsync();
        Assert.Equal(afterDevice, await reader.GetSourceVersionAsync(day, CancellationToken.None));
        // 映射表没有可靠的来源更新时间：空映射后插入及随后修改供应商值都必须使日快照失效。
        await _posm.Insertable(new PosmProductSupplierMapping { ProductCode = "P1", LocalSupplierCode = "LOCAL-A" })
            .ExecuteCommandAsync();
        var afterSupplierMappingInsert = await reader.GetSourceVersionAsync(day, CancellationToken.None);
        Assert.NotEqual(afterDevice, afterSupplierMappingInsert);
        await _posm.Updateable<PosmProductSupplierMapping>().SetColumns(row => row.LocalSupplierCode == "LOCAL-B")
            .Where(row => row.ProductCode == "P1").ExecuteCommandAsync();
        var afterSupplierMappingChange = await reader.GetSourceVersionAsync(day, CancellationToken.None);
        Assert.NotEqual(afterSupplierMappingInsert, afterSupplierMappingChange);
        // 该原单没有被“当天明细” canonical 签名覆盖；供应商会改变退货的 supplier 分组截断和 round，
        // 因而无时间戳修正也必须让日快照重算。
        await _posm.Updateable<SalesOrderDetail>().SetColumns(row => row.SupplierCode == "ORIGINAL-B")
            .Where(row => row.OrderDetailGuid == "ORIGINAL-D1").ExecuteCommandAsync();
        var afterPosmOriginalSupplier = await reader.GetSourceVersionAsync(day, CancellationToken.None);
        Assert.NotEqual(afterSupplierMappingChange, afterPosmOriginalSupplier);
        // 同一原单的折扣率也必须继续参与折扣语义版本。
        await _posm.Updateable<SalesOrderDetail>().SetColumns(row => row.DiscountRate == 0.5m)
            .Where(row => row.OrderDetailGuid == "ORIGINAL-D1").ExecuteCommandAsync();
        var afterPosmOriginalEvidence = await reader.GetSourceVersionAsync(day, CancellationToken.None);
        Assert.NotEqual(afterPosmOriginalSupplier, afterPosmOriginalEvidence);
        await _hbs.Updateable<SalesOrderDetailRecord>().SetColumns(row => row.B折扣率 == 0.5m)
            .Where(row => row.ID == 11).ExecuteCommandAsync();
        var afterHbsOriginalEvidence = await reader.GetSourceVersionAsync(day, CancellationToken.None);
        Assert.NotEqual(afterPosmOriginalEvidence, afterHbsOriginalEvidence);

        // Fresh 检查可直接复用同一 PreparedDay 给事实读取；读取后仍必须重做来源围栏。
        var prepared = await reader.CapturePreparedAsync(day, CancellationToken.None);
        Assert.Equal(afterHbsOriginalEvidence, prepared.SourceVersion);
        await _posm.Updateable<PaymentDetail>().SetColumns(row => row.Amount == 17m)
            .Where(row => row.PaymentGuid == "PAY-1").ExecuteCommandAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reader.ReadPreparedDayAsync(prepared, CancellationToken.None));
        Assert.Contains("来源在读取期间发生变化", error.Message);
    }

    [BatchSalesSqlServerFact]
    public async Task SourceReader_2024HBSales窗口起点_HBSOnly退货与原单折扣修正必须进入事实和版本()
    {
        // 历史窗口的起点也必须可读：POSM 没有任何行时，不能把真实 HBSales 销售写成零日快照。
        var day = new DateTime(2024, 9, 14);
        await SeedProductsAsync(("P1", "001", null));
        await _hbs!.Insertable(new[]
        {
            new SalesOrderMain { ID = 201, B销售单号 = "H24-SALE", B单据类型 = "1", B结账日期 = day },
            new SalesOrderMain { ID = 202, B销售单号 = "H24-RETURN", B原销售单号 = "H24-SALE", B单据类型 = "3", B结账日期 = day },
        }).ExecuteCommandAsync();
        await _hbs.Insertable(new[]
        {
            new SalesOrderDetailRecord
            {
                ID = 201, B销售单号 = "H24-SALE", B分店代码 = "S1", B结账日期 = day,
                B产品编号 = "P1", B退货码 = "R24", B数量 = 2m, B单价 = 10m,
                B原价合计金额 = 20m, B合计金额 = 20m, B折扣率 = 0m,
            },
            new SalesOrderDetailRecord
            {
                ID = 202, B销售单号 = "H24-RETURN", B分店代码 = "S1", B结账日期 = day,
                B产品编号 = "P1", B退货码 = "R24", B数量 = 1m, B单价 = 10m,
                B原价合计金额 = 10m, B合计金额 = 10m, B折扣率 = 0m,
            },
        }).ExecuteCommandAsync();

        var reader = new BatchProductSalesDiscountSnapshotSourceReader(_catalog!, _posm!, _hbs);
        var rows = await reader.ReadDayAsync(day, CancellationToken.None);
        var aggregate = Assert.Single(rows.Where(row => row.BranchCode == "S1" && row.ProductCode == "P1"));
        Assert.Equal(1m, aggregate.Quantity);
        Assert.Equal(1m, aggregate.RegularQuantity);
        Assert.Equal(1m, aggregate.ReturnQuantity);
        Assert.Equal(10m, aggregate.SalesAmount);
        Assert.Equal(0, aggregate.UnknownRowCount);

        var version = await reader.GetSourceVersionAsync(day, CancellationToken.None);
        // 原单不在 POSM 日签名中；即使没有修改时间，HBS 原单折扣字段也必须触发重算。
        await _hbs.Updateable<SalesOrderDetailRecord>().SetColumns(row => row.B折扣率 == 0.5m)
            .Where(row => row.ID == 201).ExecuteCommandAsync();
        Assert.NotEqual(version, await reader.GetSourceVersionAsync(day, CancellationToken.None));
    }
}
