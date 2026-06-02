using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Mappings.Profiles.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class StoreOrderHqSyncTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hqDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hqConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarClient _hqDb;
    private readonly IMapper _mapper;

    public StoreOrderHqSyncTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hqDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _hqConnection = new SqliteConnection($"Data Source={_hqDbPath}");
        _localConnection.Open();
        _hqConnection.Open();

        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _hqDb = new SqlSugarClient(CreateConnectionConfig(_hqConnection.ConnectionString));
        _mapper = CreateMapper();

        _localDb.CodeFirst.InitTables(typeof(Store), typeof(WareHouseOrder), typeof(WareHouseOrderDetails));
        _hqDb.CodeFirst.InitTables(
            typeof(CBP_RED_分店订货单主表Store),
            typeof(CBP_RED_分店订单详情表Store),
            typeof(CPT_DIC_外购客户信息表)
        );
    }

    [Fact]
    public async Task SyncMissingOrdersFromHqAsync_恢复软删订单时_只同步目标订单明细()
    {
        var oldTime = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var hqTime = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc);
        await SeedLocalOrderAsync("order-a", "S001", oldTime, isDeleted: true);
        await SeedLocalOrderAsync("order-b", "S999", oldTime, isDeleted: true);
        await SeedHqOrderAsync("order-a", "S001", hqTime);
        await SeedHqDetailAsync("detail-a", "order-a", "S001", hqTime, quantity: 8);
        await SeedHqDetailAsync("detail-b", "order-b", "S999", hqTime, quantity: 99);

        var result = await CreateService().SyncMissingOrdersFromHqAsync(
            new SyncMissingOrdersRequestDto { StoreCodes = new List<string> { "S001" } }
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.OrdersSynced);
        Assert.Equal(1, result.DetailsSynced);

        var restoredOrder = await _localDb.Queryable<WareHouseOrder>()
            .FirstAsync(item => item.OrderGUID == "order-a");
        var untouchedOrder = await _localDb.Queryable<WareHouseOrder>()
            .FirstAsync(item => item.OrderGUID == "order-b");
        var details = await _localDb.Queryable<WareHouseOrderDetails>().ToListAsync();

        Assert.False(restoredOrder.IsDeleted);
        Assert.True(untouchedOrder.IsDeleted);
        var detail = Assert.Single(details);
        Assert.Equal("detail-a", detail.DetailGUID);
        Assert.Equal("order-a", detail.OrderGUID);
    }

    [Fact]
    public async Task SyncMissingOrdersFromHqAsync_没有目标订单时_不查询完整明细()
    {
        var syncTime = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc);
        var hqSqlLogs = new List<string>();
        await SeedLocalOrderAsync("order-no-target", "S001", syncTime, isDeleted: false);
        await SeedLocalDetailAsync(
            "detail-no-target",
            "order-no-target",
            "S001",
            syncTime,
            quantity: 6,
            isDeleted: false,
            storeProductCode: "S001-P001",
            productCode: "P001",
            allocQuantity: 7,
            lastCost: 2,
            importPrice: 3,
            importAmount: 18,
            oemPrice: 4,
            oemAmount: 24
        );
        await SeedHqOrderAsync("order-no-target", "S001", syncTime);
        await SeedHqDetailAsync("detail-no-target", "order-no-target", "S001", syncTime, quantity: 6);

        var result = await CreateService(hqSqlLogs).SyncMissingOrdersFromHqAsync(
            new SyncMissingOrdersRequestDto { StoreCodes = new List<string> { "S001" } }
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(0, result.OrdersSynced);
        Assert.Equal(0, result.OrdersUpdated);
        Assert.Equal(0, result.DetailsSynced);
        Assert.Equal(0, result.DetailsUpdated);
        Assert.DoesNotContain(GetFullDetailQueryLogs(hqSqlLogs), _ => true);
    }

    [Fact]
    public async Task SyncMissingOrdersFromHqAsync_有目标订单时_只查询目标订单完整明细()
    {
        var localTime = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var changedTime = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc);
        var stableTime = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc);
        var hqSqlLogs = new List<string>();

        await SeedLocalOrderAsync("order-target", "S001", localTime, isDeleted: false);
        await SeedLocalDetailAsync(
            "detail-target",
            "order-target",
            "S001",
            localTime,
            quantity: 1,
            isDeleted: false,
            storeProductCode: "S001-P001",
            productCode: "P001",
            allocQuantity: 2,
            lastCost: 2,
            importPrice: 3,
            importAmount: 3,
            oemPrice: 4,
            oemAmount: 4
        );
        await SeedLocalOrderAsync("order-stable", "S001", stableTime, isDeleted: false);
        await SeedLocalDetailAsync(
            "detail-stable",
            "order-stable",
            "S001",
            stableTime,
            quantity: 6,
            isDeleted: false,
            storeProductCode: "S001-P001",
            productCode: "P001",
            allocQuantity: 7,
            lastCost: 2,
            importPrice: 3,
            importAmount: 18,
            oemPrice: 4,
            oemAmount: 24
        );

        await SeedHqOrderAsync("order-target", "S001", localTime);
        await SeedHqDetailAsync("detail-target", "order-target", "S001", changedTime, quantity: 9);
        await SeedHqOrderAsync("order-stable", "S001", stableTime);
        await SeedHqDetailAsync("detail-stable", "order-stable", "S001", stableTime, quantity: 6);

        var result = await CreateService(hqSqlLogs).SyncMissingOrdersFromHqAsync(
            new SyncMissingOrdersRequestDto { StoreCodes = new List<string> { "S001" } }
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.DetailsUpdated);

        var fullDetailQueryLog = Assert.Single(GetFullDetailQueryLogs(hqSqlLogs));
        Assert.Contains("order-target", fullDetailQueryLog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("order-stable", fullDetailQueryLog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SyncMissingOrdersFromHqAsync_Hq只更新明细时_应该更新本地明细()
    {
        var localTime = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var detailTime = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc);
        await SeedLocalOrderAsync("order-detail-update", "S001", localTime, isDeleted: false);
        await SeedLocalDetailAsync(
            "detail-update",
            "order-detail-update",
            "S001",
            localTime,
            quantity: 1,
            isDeleted: false
        );
        await SeedHqOrderAsync("order-detail-update", "S001", localTime);
        await SeedHqDetailAsync(
            "detail-update",
            "order-detail-update",
            "S001",
            detailTime,
            quantity: 12
        );

        var result = await CreateService().SyncMissingOrdersFromHqAsync(
            new SyncMissingOrdersRequestDto { StoreCodes = new List<string> { "S001" } }
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(0, result.OrdersUpdated);
        Assert.Equal(1, result.DetailsUpdated);

        var detail = await _localDb.Queryable<WareHouseOrderDetails>()
            .FirstAsync(item => item.DetailGUID == "detail-update");
        Assert.Equal(12, detail.Quantity);
        Assert.Equal("S001-P001", detail.StoreProductCode);
        Assert.False(detail.IsDeleted);
    }

    [Fact]
    public async Task SyncMissingOrdersFromHqAsync_Hq明细字段变化但更新时间未推进_应该更新本地明细()
    {
        var sameTime = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedLocalOrderAsync("order-detail-fingerprint", "S001", sameTime, isDeleted: false);
        await SeedLocalDetailAsync(
            "detail-fingerprint",
            "order-detail-fingerprint",
            "S001",
            sameTime,
            quantity: 1,
            isDeleted: false,
            storeProductCode: "S001-P001",
            productCode: "P001",
            allocQuantity: 2,
            lastCost: 2,
            importPrice: 3,
            importAmount: 3,
            oemPrice: 4,
            oemAmount: 4
        );
        await SeedHqOrderAsync("order-detail-fingerprint", "S001", sameTime);
        await SeedHqDetailAsync(
            "detail-fingerprint",
            "order-detail-fingerprint",
            "S001",
            sameTime,
            quantity: 12
        );

        var result = await CreateService().SyncMissingOrdersFromHqAsync(
            new SyncMissingOrdersRequestDto { StoreCodes = new List<string> { "S001" } }
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(0, result.OrdersUpdated);
        Assert.Equal(1, result.DetailsUpdated);

        var detail = await _localDb.Queryable<WareHouseOrderDetails>()
            .FirstAsync(item => item.DetailGUID == "detail-fingerprint");
        Assert.Equal(12, detail.Quantity);
        Assert.Equal(13, detail.AllocQuantity);
        Assert.Equal(36, detail.ImportAmount);
        Assert.Equal(48, detail.OEMAmount);
    }

    [Fact]
    public async Task SyncMissingOrdersFromHqAsync_Hq明细字段变化但订单级聚合不变_应该更新本地明细()
    {
        var sameTime = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedLocalOrderAsync("order-detail-collision", "S001", sameTime, isDeleted: false);
        await SeedLocalDetailAsync(
            "detail-collision-a",
            "order-detail-collision",
            "S001",
            sameTime,
            quantity: 10,
            isDeleted: false,
            storeProductCode: "S001-P001",
            productCode: "P001",
            allocQuantity: 11,
            lastCost: 2,
            importPrice: 3,
            importAmount: 30,
            oemPrice: 4,
            oemAmount: 40
        );
        await SeedLocalDetailAsync(
            "detail-collision-b",
            "order-detail-collision",
            "S001",
            sameTime,
            quantity: 20,
            isDeleted: false,
            storeProductCode: "S001-P001",
            productCode: "P001",
            allocQuantity: 21,
            lastCost: 2,
            importPrice: 3,
            importAmount: 60,
            oemPrice: 4,
            oemAmount: 80
        );
        await SeedHqOrderAsync("order-detail-collision", "S001", sameTime);
        await SeedHqDetailAsync(
            "detail-collision-a",
            "order-detail-collision",
            "S001",
            sameTime,
            quantity: 11
        );
        await SeedHqDetailAsync(
            "detail-collision-b",
            "order-detail-collision",
            "S001",
            sameTime,
            quantity: 19
        );

        var result = await CreateService().SyncMissingOrdersFromHqAsync(
            new SyncMissingOrdersRequestDto { StoreCodes = new List<string> { "S001" } }
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.DetailsUpdated);

        var detailA = await _localDb.Queryable<WareHouseOrderDetails>()
            .FirstAsync(item => item.DetailGUID == "detail-collision-a");
        var detailB = await _localDb.Queryable<WareHouseOrderDetails>()
            .FirstAsync(item => item.DetailGUID == "detail-collision-b");
        Assert.Equal(11, detailA.Quantity);
        Assert.Equal(19, detailB.Quantity);
    }

    [Fact]
    public async Task SyncMissingOrdersFromHqAsync_Hq明细更新时间为空且字段一致时_不重复更新()
    {
        var localTime = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedLocalOrderAsync("order-detail-same", "S001", localTime, isDeleted: false);
        await SeedLocalDetailAsync(
            "detail-same",
            "order-detail-same",
            "S001",
            localTime,
            quantity: 6,
            isDeleted: false,
            storeProductCode: "S001-P001",
            productCode: "P001",
            allocQuantity: 7,
            lastCost: 2,
            importPrice: 3,
            importAmount: 18,
            oemPrice: 4,
            oemAmount: 24
        );
        await SeedHqOrderAsync("order-detail-same", "S001", localTime);
        await SeedHqDetailAsync("detail-same", "order-detail-same", "S001", null, quantity: 6);

        var result = await CreateService().SyncMissingOrdersFromHqAsync(
            new SyncMissingOrdersRequestDto { StoreCodes = new List<string> { "S001" } }
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(0, result.DetailsUpdated);
        var detail = await _localDb.Queryable<WareHouseOrderDetails>()
            .FirstAsync(item => item.DetailGUID == "detail-same");
        Assert.Equal(localTime, detail.UpdatedAt);
    }

    [Fact]
    public async Task SyncMissingOrdersFromHqAsync_明细写入失败时_应该回滚主表()
    {
        var hqTime = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc);
        await SeedHqOrderAsync("order-rollback", "S001", hqTime);
        await SeedHqDetailAsync("detail-rollback", "order-rollback", "S001", hqTime, quantity: 5);
        await _localDb.Ado.ExecuteCommandAsync(
            """
            CREATE TRIGGER block_store_order_detail_insert
            BEFORE INSERT ON WareHouseOrderDetails
            BEGIN
                SELECT RAISE(ABORT, 'detail insert blocked');
            END;
            """
        );

        var result = await CreateService().SyncMissingOrdersFromHqAsync(
            new SyncMissingOrdersRequestDto { StoreCodes = new List<string> { "S001" } }
        );

        Assert.False(result.Success);
        Assert.Contains("detail insert blocked", result.Message);
        Assert.Equal(0, await _localDb.Queryable<WareHouseOrder>().CountAsync());
        Assert.Equal(0, await _localDb.Queryable<WareHouseOrderDetails>().CountAsync());
    }

    [Fact]
    public async Task SyncMissingOrdersFromHqAsync_多门店请求_只同步请求门店集合()
    {
        var hqTime = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc);
        await SeedHqOrderAsync("order-s001", "S001", hqTime);
        await SeedHqOrderAsync("order-s002", "S002", hqTime);
        await SeedHqOrderAsync("order-s003", "S003", hqTime);
        await SeedHqDetailAsync("detail-s001", "order-s001", "S001", hqTime, quantity: 1);
        await SeedHqDetailAsync("detail-s002", "order-s002", "S002", hqTime, quantity: 2);
        await SeedHqDetailAsync("detail-s003", "order-s003", "S003", hqTime, quantity: 3);

        var result = await CreateService().SyncMissingOrdersFromHqAsync(
            new SyncMissingOrdersRequestDto { StoreCodes = new List<string> { "S001", "S002" } }
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.OrdersSynced);
        Assert.Equal(2, result.DetailsSynced);

        var orderGuids = await _localDb.Queryable<WareHouseOrder>()
            .OrderBy(item => item.OrderGUID)
            .Select(item => item.OrderGUID)
            .ToListAsync();
        Assert.Equal(new List<string> { "order-s001", "order-s002" }, orderGuids);
    }

    [Fact]
    public async Task SyncMissingOrdersFromHqAsync_外购客户Hguid_应该保留为订单分店标识()
    {
        var hqTime = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc);
        var externalCustomerGuid = "669C0A86-31BC-4EDF-9D4C-216E9E312CB1";
        await SeedHqOrderAsync("order-external", externalCustomerGuid, hqTime);
        await SeedHqDetailAsync("detail-external", "order-external", externalCustomerGuid, hqTime, quantity: 3);

        var result = await CreateService().SyncMissingOrdersFromHqAsync(
            new SyncMissingOrdersRequestDto { StoreCodes = new List<string> { externalCustomerGuid } }
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.OrdersSynced);
        Assert.Equal(1, result.DetailsSynced);

        var order = await _localDb.Queryable<WareHouseOrder>()
            .FirstAsync(item => item.OrderGUID == "order-external");
        var detail = await _localDb.Queryable<WareHouseOrderDetails>()
            .FirstAsync(item => item.DetailGUID == "detail-external");

        Assert.Equal(externalCustomerGuid, order.StoreCode);
        Assert.Equal(externalCustomerGuid, detail.StoreCode);
    }

    [Fact]
    public async Task StoreOrderHqSyncService_全量同步_忽略筛选并软删Hq缺失主明细()
    {
        var oldTime = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var hqTime = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc);
        await SeedLocalOrderAsync("order-local-only", "S001", oldTime, isDeleted: false);
        await SeedLocalDetailAsync(
            "detail-local-only",
            "order-local-only",
            "S001",
            oldTime,
            quantity: 1,
            isDeleted: false
        );
        await SeedHqOrderAsync("order-s001", "S001", hqTime);
        await SeedHqOrderAsync("order-s999", "S999", hqTime);
        await SeedHqDetailAsync("detail-s001", "order-s001", "S001", hqTime, quantity: 3);
        await SeedHqDetailAsync("detail-s999", "order-s999", "S999", hqTime, quantity: 4);

        var result = await CreateHqSyncService().SyncAsync(
            StoreOrderHqSyncMode.Full,
            new StoreOrderHqSyncRequestDto { StoreCodes = new List<string> { "S001" } },
            "job-full"
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal("Full", result.Mode);
        Assert.Equal(2, result.OrdersSynced);
        Assert.Equal(2, result.DetailsSynced);
        Assert.Equal(1, result.OrdersSoftDeleted);
        Assert.Equal(1, result.DetailsSoftDeleted);

        var allOrders = await _localDb.Queryable<WareHouseOrder>()
            .OrderBy(item => item.OrderGUID)
            .ToListAsync();
        Assert.Contains(allOrders, item => item.OrderGUID == "order-s999" && !item.IsDeleted);
        Assert.Contains(allOrders, item => item.OrderGUID == "order-local-only" && item.IsDeleted);
    }

    [Fact]
    public async Task StoreOrderHqSyncService_增量同步_不再扫描当前Guid集合也不软删Hq物理删除()
    {
        var oldTime = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var hqTime = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc);
        var hqSqlLogs = new List<string>();
        await SeedLocalOrderAsync("order-physical-delete", "S001", oldTime, isDeleted: false);
        await SeedLocalDetailAsync(
            "detail-physical-delete",
            "order-physical-delete",
            "S001",
            oldTime,
            quantity: 1,
            isDeleted: false
        );
        await SeedHqOrderAsync("order-current", "S001", hqTime);
        await SeedHqDetailAsync("detail-current", "order-current", "S001", hqTime, quantity: 8);

        var result = await CreateHqSyncService(hqSqlLogs).SyncAsync(
            StoreOrderHqSyncMode.Incremental,
            new StoreOrderHqSyncRequestDto
            {
                StoreCodes = new List<string> { "S001" },
                StartDate = hqTime.AddDays(-1),
                EndDate = hqTime.AddDays(1),
            },
            "job-inc"
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal("Incremental", result.Mode);
        Assert.Equal(1, result.OrdersSynced);
        Assert.Equal(1, result.DetailsSynced);
        Assert.Equal(0, result.OrdersSoftDeleted);
        Assert.Equal(0, result.DetailsSoftDeleted);

        var deletedOrder = await _localDb.Queryable<WareHouseOrder>()
            .FirstAsync(item => item.OrderGUID == "order-physical-delete");
        var deletedDetail = await _localDb.Queryable<WareHouseOrderDetails>()
            .FirstAsync(item => item.DetailGUID == "detail-physical-delete");
        Assert.False(deletedOrder.IsDeleted);
        Assert.False(deletedDetail.IsDeleted);
        Assert.DoesNotContain(GetCurrentGuidQueryLogs(hqSqlLogs), _ => true);
    }

    [Fact]
    public async Task StoreOrderHqSyncService_增量同步_日期命中时恢复本地软删主明细()
    {
        var oldTime = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var hqTime = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc);
        await SeedLocalOrderAsync("order-restore-incremental", "S001", oldTime, isDeleted: true);
        await SeedLocalDetailAsync(
            "detail-restore-incremental",
            "order-restore-incremental",
            "S001",
            oldTime,
            quantity: 1,
            isDeleted: true
        );
        await SeedHqOrderAsync("order-restore-incremental", "S001", hqTime);
        await SeedHqDetailAsync(
            "detail-restore-incremental",
            "order-restore-incremental",
            "S001",
            hqTime,
            quantity: 8
        );

        var result = await CreateHqSyncService().SyncAsync(
            StoreOrderHqSyncMode.Incremental,
            new StoreOrderHqSyncRequestDto
            {
                StoreCodes = new List<string> { "S001" },
                StartDate = hqTime.AddDays(-1),
                EndDate = hqTime.AddDays(1),
            },
            "job-inc-restore"
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.OrdersSynced);
        Assert.Equal(1, result.DetailsSynced);
        Assert.Equal(0, result.OrdersSoftDeleted);
        Assert.Equal(0, result.DetailsSoftDeleted);

        var restoredOrder = await _localDb.Queryable<WareHouseOrder>()
            .FirstAsync(item => item.OrderGUID == "order-restore-incremental");
        var restoredDetail = await _localDb.Queryable<WareHouseOrderDetails>()
            .FirstAsync(item => item.DetailGUID == "detail-restore-incremental");
        Assert.False(restoredOrder.IsDeleted);
        Assert.False(restoredDetail.IsDeleted);
        Assert.Equal(8, restoredDetail.Quantity);
    }

    [Fact]
    public async Task GetUsedBranchesAsync_同时返回本地分店和外购客户()
    {
        var externalCustomerGuid = "669C0A86-31BC-4EDF-9D4C-216E9E312CB1";
        var now = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc);
        await SeedStoreAsync("store-guid-s001", "S001", "Lakehaven");
        await SeedExternalCustomerAsync(externalCustomerGuid, "外购客户A");
        await SeedLocalOrderAsync("order-store", "S001", now, isDeleted: false);
        await SeedLocalOrderAsync("order-external", externalCustomerGuid, now, isDeleted: false);

        var result = await CreateService().GetUsedBranchesAsync();

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.Contains(result.Data!, item => item.Code == "S001" && item.Name == "Lakehaven");
        Assert.Contains(
            result.Data!,
            item => item.Code == externalCustomerGuid && item.Name == "外购客户A"
        );
    }

    public void Dispose()
    {
        _localDb.Dispose();
        _hqDb.Dispose();
        _localConnection.Dispose();
        _hqConnection.Dispose();

        if (File.Exists(_localDbPath))
        {
            SqliteTempFileCleanup.DeleteIfExists(_localDbPath);
        }

        if (File.Exists(_hqDbPath))
        {
            SqliteTempFileCleanup.DeleteIfExists(_hqDbPath);
        }
    }

    private StoreOrderReactService CreateService(List<string>? hqSqlLogs = null)
    {
        var service = new StoreOrderReactService(
            CreateSqlSugarContext(_localDb),
            NullLogger<StoreOrderReactService>.Instance,
            new HttpContextAccessor(),
            new StubOrderNumberGenerator(),
            CreateHqConfiguration(_hqConnection.ConnectionString),
            _mapper
        );

        var factoryField = typeof(StoreOrderReactService).GetField(
            "_createHqConnection",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        factoryField!.SetValue(
            service,
            () =>
            {
                var client = new SqlSugarClient(CreateConnectionConfig(_hqConnection.ConnectionString));
                if (hqSqlLogs != null)
                {
                    // 记录 HQ SQL，确保同步只读取目标订单的完整明细。
                    client.Aop.OnLogExecuting = (sql, parameters) =>
                    {
                        hqSqlLogs.Add(FormatSqlLog(sql, parameters));
                    };
                }

                return client;
            }
        );
        return service;
    }

    private StoreOrderHqSyncService CreateHqSyncService(List<string>? hqSqlLogs = null)
    {
        var service = new StoreOrderHqSyncService(
            CreateSqlSugarContext(_localDb),
            _mapper,
            CreateHqConfiguration(_hqConnection.ConnectionString),
            NullLogger<StoreOrderHqSyncService>.Instance
        );

        var factoryField = typeof(StoreOrderHqSyncService).GetField(
            "_createHqConnection",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        factoryField!.SetValue(
            service,
            () =>
            {
                var client = new SqlSugarClient(CreateConnectionConfig(_hqConnection.ConnectionString));
                if (hqSqlLogs != null)
                {
                    // 记录 HQ SQL，确保增量同步不再为物理删除检测扫描当前全量 GUID。
                    client.Aop.OnLogExecuting = (sql, parameters) =>
                    {
                        hqSqlLogs.Add(FormatSqlLog(sql, parameters));
                    };
                }

                return client;
            }
        );
        return service;
    }

    private static List<string> GetFullDetailQueryLogs(List<string> hqSqlLogs)
    {
        return hqSqlLogs
            .Where(log =>
                log.Contains("CBP_RED_分店订单详情表", StringComparison.OrdinalIgnoreCase)
                && log.Contains("FGC_Creator", StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
    }

    private static List<string> GetCurrentGuidQueryLogs(List<string> hqSqlLogs)
    {
        return hqSqlLogs
            .Where(log =>
                (
                    log.Contains("CBP_RED_分店订货单主表", StringComparison.OrdinalIgnoreCase)
                    || log.Contains("CBP_RED_分店订单详情表", StringComparison.OrdinalIgnoreCase)
                )
                && log.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                && log.Contains("HGUID", StringComparison.OrdinalIgnoreCase)
                && !log.Contains("FGC_CreateDate", StringComparison.OrdinalIgnoreCase)
                && !log.Contains("FGC_LastModifyDate", StringComparison.OrdinalIgnoreCase)
                && !log.Contains("主表GUID", StringComparison.OrdinalIgnoreCase)
            )
            .ToList();
    }

    private static string FormatSqlLog(string sql, SugarParameter[]? parameters)
    {
        if (parameters == null || parameters.Length == 0)
        {
            return sql;
        }

        var formattedParameters = string.Join(
            ", ",
            parameters.Select(parameter => $"{parameter.ParameterName}={parameter.Value}")
        );
        return $"{sql} || {formattedParameters}";
    }

    private async Task SeedLocalOrderAsync(
        string orderGuid,
        string storeCode,
        DateTime updatedAt,
        bool isDeleted
    )
    {
        await _localDb.Insertable(
            new WareHouseOrder
            {
                OrderGUID = orderGuid,
                StoreCode = storeCode,
                OrderNo = $"NO-{orderGuid}",
                OrderDate = updatedAt.Date,
                FlowStatus = 1,
                InboundStatus = 0,
                CreatedAt = updatedAt,
                UpdatedAt = updatedAt,
                CreatedBy = "local",
                UpdatedBy = "local",
                IsDeleted = isDeleted,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedStoreAsync(string storeGuid, string storeCode, string storeName)
    {
        await _localDb.Insertable(
            new Store
            {
                StoreGUID = storeGuid,
                StoreCode = storeCode,
                StoreName = storeName,
                IsActive = true,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedExternalCustomerAsync(string hguid, string customerName)
    {
        await _hqDb.Insertable(
            new CPT_DIC_外购客户信息表
            {
                HGUID = hguid,
                客户名称 = customerName,
                状态 = 1,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedLocalDetailAsync(
        string detailGuid,
        string orderGuid,
        string storeCode,
        DateTime updatedAt,
        decimal quantity,
        bool isDeleted = true,
        string? storeProductCode = null,
        string? productCode = null,
        decimal? allocQuantity = null,
        decimal? lastCost = null,
        decimal? importPrice = null,
        decimal? importAmount = null,
        decimal? oemPrice = null,
        decimal? oemAmount = null
    )
    {
        await _localDb.Insertable(
            new WareHouseOrderDetails
            {
                DetailGUID = detailGuid,
                OrderGUID = orderGuid,
                StoreCode = storeCode,
                StoreProductCode = storeProductCode ?? $"{storeCode}-OLD",
                ProductCode = productCode ?? "P-OLD",
                Quantity = quantity,
                AllocQuantity = allocQuantity ?? quantity,
                LastCost = lastCost ?? 1,
                ImportPrice = importPrice ?? 1,
                ImportAmount = importAmount ?? quantity,
                OEMPrice = oemPrice ?? 1,
                OEMAmount = oemAmount ?? quantity,
                CreatedAt = updatedAt,
                UpdatedAt = updatedAt,
                CreatedBy = "local",
                UpdatedBy = "local",
                IsDeleted = isDeleted,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedHqOrderAsync(string orderGuid, string storeCode, DateTime updatedAt)
    {
        await _hqDb.Insertable(
            new CBP_RED_分店订货单主表Store
            {
                HGUID = orderGuid,
                分店代码 = storeCode,
                订单号 = $"HQ-{orderGuid}",
                订单日期 = updatedAt.Date,
                流程状态 = 1,
                入库状态 = 0,
                进口总金额 = 10,
                贴牌总金额 = 20,
                FGC_Creator = "hq",
                FGC_CreateDate = updatedAt.AddDays(-1),
                FGC_LastModifier = "hq",
                FGC_LastModifyDate = updatedAt,
            }
        ).ExecuteCommandAsync();
    }

    private async Task SeedHqDetailAsync(
        string detailGuid,
        string orderGuid,
        string storeCode,
        DateTime? updatedAt,
        decimal quantity
    )
    {
        await _hqDb.Insertable(
            new CBP_RED_分店订单详情表Store
            {
                HGUID = detailGuid,
                主表GUID = orderGuid,
                分店代码 = storeCode,
                分店商品编码 = $"{storeCode}-P001",
                商品编码 = "P001",
                数量 = quantity,
                配货数量 = quantity + 1,
                上次成本 = 2,
                进口价格 = 3,
                合计进口金额 = quantity * 3,
                贴牌价格 = 4,
                合计贴牌金额 = quantity * 4,
                FGC_Creator = "hq",
                FGC_CreateDate = updatedAt?.AddDays(-1),
                FGC_LastModifier = "hq",
                FGC_LastModifyDate = updatedAt,
            }
        ).ExecuteCommandAsync();
    }

    private static ConnectionConfig CreateConnectionConfig(string connectionString) =>
        new()
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        };

    private static IMapper CreateMapper()
    {
        var configuration = new MapperConfiguration(
            cfg =>
            {
                cfg.AddProfile<ReactWareHouseOrderMappingProfile>();
                cfg.AddProfile<ReactWareHouseOrderDetailMappingProfile>();
            },
            NullLoggerFactory.Instance
        );
        return configuration.CreateMapper();
    }

    private static IConfiguration CreateHqConfiguration(string connectionString)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:StoreHzgHQConnection"] = connectionString,
                }
            )
            .Build();
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        var dbField = typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, db);
        return context;
    }

    private sealed class StubOrderNumberGenerator : IOrderNumberGenerator
    {
        public Task<string> GetNextOrderNoAsync()
        {
            return Task.FromResult("TEST-ORDER-NO");
        }
    }
}
