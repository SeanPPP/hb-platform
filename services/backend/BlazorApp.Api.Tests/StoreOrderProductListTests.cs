using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Shared.Models.HBweb;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

[Collection("PreorderMutationLock")]
public sealed class StoreOrderProductListTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;
    private readonly List<string> _sqlLogs = new();

    public StoreOrderProductListTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _sqliteConnection = new SqliteConnection($"Data Source={_dbPath}");
        _sqliteConnection.Open();

        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _sqliteConnection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.Aop.OnLogExecuting = (sql, parameters) => _sqlLogs.Add(FormatSqlLog(parameters, sql));

        _db.CodeFirst.InitTables(
            typeof(Product),
            typeof(WarehouseProduct),
            typeof(WarehouseCategory),
            typeof(ProductGrade),
            typeof(HBLocalSupplier),
            typeof(WareHouseOrder),
            typeof(WareHouseOrderDetails),
            typeof(Store),
            typeof(DomesticProduct),
            typeof(ChinaSupplier),
            typeof(CPT_DIC_外购客户信息表),
            typeof(ProductLocation),
            typeof(Location),
            typeof(StoreOrderInvoiceEmailSendRecord),
            typeof(PreorderActivation),
            typeof(PreorderActivationStore),
            typeof(PreorderWarehouseOrder)
        );
        _db.Ado.ExecuteCommand("DROP TABLE ProductGrade");
        _db.Ado.ExecuteCommand(
            """
            CREATE TABLE ProductGrade (
                Id TEXT PRIMARY KEY NOT NULL,
                ProductCode TEXT NOT NULL,
                Grade TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                CreatedBy TEXT NULL,
                UpdatedAt TEXT NULL,
                UpdatedBy TEXT NULL,
                IsDeleted INTEGER NULL
            )
            """
        );
    }

    [Theory]
    [InlineData(1, "PREORDER_SUBMIT_ENDPOINT_REQUIRED")]
    [InlineData(2, "INVALID_ORDER_STATUS_TRANSITION")]
    public async Task Status单笔接口不允许普通分店从草稿绕过状态机(
        int newStatus,
        string expectedErrorCode
    )
    {
        await _db.Insertable(new WareHouseOrder
        {
            OrderGUID = "status-draft-single",
            StoreCode = "S01",
            OrderNo = "DRAFT-STATUS-SINGLE",
            FlowStatus = 0,
        }).ExecuteCommandAsync();

        var result = await CreateService().UpdateOrderStatusAsync(
            "status-draft-single",
            newStatus
        );

        Assert.False(result.Success);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
        var persisted = await _db.Queryable<WareHouseOrder>()
            .FirstAsync(item => item.OrderGUID == "status-draft-single");
        Assert.Equal(0, persisted.FlowStatus);
    }

    [Theory]
    [InlineData(1, "PREORDER_SUBMIT_ENDPOINT_REQUIRED")]
    [InlineData(2, "INVALID_ORDER_STATUS_TRANSITION")]
    public async Task Status批量接口遇到草稿时整批拒绝且不部分提交(
        int newStatus,
        string expectedErrorCode
    )
    {
        await _db.Insertable(new[]
        {
            new WareHouseOrder
            {
                OrderGUID = "status-draft-batch",
                StoreCode = "S01",
                OrderNo = "DRAFT-STATUS-BATCH",
                FlowStatus = 0,
            },
            new WareHouseOrder
            {
                OrderGUID = "status-completed-batch",
                StoreCode = "S01",
                OrderNo = "COMPLETED-STATUS-BATCH",
                FlowStatus = 2,
            },
        }).ExecuteCommandAsync();

        var result = await CreateService().BatchUpdateOrderStatusAsync(
            new() { "status-draft-batch", "status-completed-batch" },
            newStatus
        );

        Assert.False(result.Success);
        Assert.Equal(expectedErrorCode, result.ErrorCode);
        var orders = await _db.Queryable<WareHouseOrder>()
            .Where(item => item.OrderGUID.StartsWith("status-") && item.OrderGUID.EndsWith("-batch"))
            .ToListAsync();
        Assert.Equal(0, orders.Single(item => item.OrderGUID == "status-draft-batch").FlowStatus);
        Assert.Equal(2, orders.Single(item => item.OrderGUID == "status-completed-batch").FlowStatus);
    }

    [Fact]
    public async Task Status单笔接口允许WarehouseStaff和已授权仓库管理流程提交草稿()
    {
        await _db.Insertable(new[]
        {
            new WareHouseOrder
            {
                OrderGUID = "status-warehouse-staff",
                StoreCode = "S01",
                OrderNo = "DRAFT-WAREHOUSE-STAFF",
                FlowStatus = 0,
            },
            new WareHouseOrder
            {
                OrderGUID = "status-manage-orders",
                StoreCode = "S01",
                OrderNo = "DRAFT-MANAGE-ORDERS",
                FlowStatus = 0,
            },
        }).ExecuteCommandAsync();

        var warehouseStaffResult = await CreateService(
            "warehouse-user",
            "WarehouseStaff"
        ).UpdateOrderStatusAsync("status-warehouse-staff", 1);
        var manageOrdersResult = await CreateService().UpdateOrderStatusAsync(
            "status-manage-orders",
            1,
            bypassPreorderGate: true
        );

        Assert.True(warehouseStaffResult.Success);
        Assert.True(manageOrdersResult.Success);
        Assert.Equal(
            2,
            await _db.Queryable<WareHouseOrder>()
                .CountAsync(item =>
                    item.OrderGUID.StartsWith("status-")
                    && item.FlowStatus == 1
                )
        );
    }

    [Fact]
    public async Task Status批量接口允许已授权仓库管理流程提交草稿()
    {
        await _db.Insertable(new[]
        {
            new WareHouseOrder
            {
                OrderGUID = "status-manage-batch-a",
                StoreCode = "S01",
                OrderNo = "DRAFT-MANAGE-BATCH-A",
                FlowStatus = 0,
            },
            new WareHouseOrder
            {
                OrderGUID = "status-manage-batch-b",
                StoreCode = "S01",
                OrderNo = "DRAFT-MANAGE-BATCH-B",
                FlowStatus = 0,
            },
        }).ExecuteCommandAsync();

        var result = await CreateService().BatchUpdateOrderStatusAsync(
            new() { "status-manage-batch-a", "status-manage-batch-b" },
            1,
            bypassPreorderGate: true
        );

        Assert.True(result.Success);
        Assert.Equal(2, result.Data);
        Assert.Equal(
            2,
            await _db.Queryable<WareHouseOrder>()
                .CountAsync(item =>
                    item.OrderGUID.StartsWith("status-manage-batch-")
                    && item.FlowStatus == 1
                )
        );
    }

    [Fact]
    public async Task Status单笔和批量接口均不允许仓库绕过流程把草稿直接完成()
    {
        await _db.Insertable(new[]
        {
            new WareHouseOrder
            {
                OrderGUID = "status-bypass-complete-single",
                StoreCode = "S01",
                OrderNo = "DRAFT-BYPASS-COMPLETE-SINGLE",
                FlowStatus = 0,
            },
            new WareHouseOrder
            {
                OrderGUID = "status-bypass-complete-batch",
                StoreCode = "S01",
                OrderNo = "DRAFT-BYPASS-COMPLETE-BATCH",
                FlowStatus = 0,
            },
        }).ExecuteCommandAsync();

        var single = await CreateService("warehouse-user", "WarehouseStaff")
            .UpdateOrderStatusAsync("status-bypass-complete-single", 2);
        var batch = await CreateService().BatchUpdateOrderStatusAsync(
            new() { "status-bypass-complete-batch" },
            2,
            bypassPreorderGate: true
        );

        Assert.False(single.Success);
        Assert.Equal("INVALID_ORDER_STATUS_TRANSITION", single.ErrorCode);
        Assert.False(batch.Success);
        Assert.Equal("INVALID_ORDER_STATUS_TRANSITION", batch.ErrorCode);
        Assert.Equal(
            2,
            await _db.Queryable<WareHouseOrder>()
                .CountAsync(item =>
                    item.OrderGUID.StartsWith("status-bypass-complete-")
                    && item.FlowStatus == 0
                )
        );
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 1)]
    [InlineData(4, 2)]
    public async Task Status单笔接口拒绝Picking和其他状态进入提交或完成(
        int currentStatus,
        int newStatus
    )
    {
        await _db.Insertable(new WareHouseOrder
        {
            OrderGUID = "status-invalid-source",
            StoreCode = "S01",
            OrderNo = "INVALID-SOURCE",
            FlowStatus = currentStatus,
        }).ExecuteCommandAsync();

        var result = await CreateService().UpdateOrderStatusAsync(
            "status-invalid-source",
            newStatus
        );

        Assert.False(result.Success);
        Assert.Equal("INVALID_ORDER_STATUS_TRANSITION", result.ErrorCode);
        Assert.Equal(
            currentStatus,
            (await _db.Queryable<WareHouseOrder>()
                .FirstAsync(item => item.OrderGUID == "status-invalid-source")).FlowStatus
        );
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Status单笔CAS不覆盖读取后发生的完成或配货状态(int competingStatus)
    {
        await _db.Insertable(new WareHouseOrder
        {
            OrderGUID = "status-single-cas",
            StoreCode = "S01",
            OrderNo = "STATUS-SINGLE-CAS",
            FlowStatus = 1,
        }).ExecuteCommandAsync();
        var injected = false;
        _db.Aop.OnLogExecuting = (sql, _) =>
        {
            if (injected
                || !sql.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                || !sql.Contains("WareHouseOrder", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            injected = true;
            _db.Updateable<WareHouseOrder>()
                .SetColumns(item => item.FlowStatus == competingStatus)
                .Where(item => item.OrderGUID == "status-single-cas")
                .ExecuteCommand();
        };
        ApiResponse<bool> result;
        try
        {
            result = await CreateService().UpdateOrderStatusAsync("status-single-cas", 2);
        }
        finally
        {
            _db.Aop.OnLogExecuting = (sql, parameters) =>
                _sqlLogs.Add(FormatSqlLog(parameters, sql));
        }

        Assert.True(injected);
        Assert.False(result.Success);
        Assert.Equal("ORDER_STATUS_CONFLICT", result.ErrorCode);
        Assert.Equal(
            competingStatus,
            (await _db.Queryable<WareHouseOrder>()
                .FirstAsync(item => item.OrderGUID == "status-single-cas")).FlowStatus
        );
    }

    [Fact]
    public async Task Status批量接口遇到Picking来源时整体拒绝()
    {
        await _db.Insertable(new[]
        {
            new WareHouseOrder
            {
                OrderGUID = "status-batch-valid",
                StoreCode = "S01",
                OrderNo = "STATUS-BATCH-VALID",
                FlowStatus = 1,
            },
            new WareHouseOrder
            {
                OrderGUID = "status-batch-picking",
                StoreCode = "S01",
                OrderNo = "STATUS-BATCH-PICKING",
                FlowStatus = 3,
            },
        }).ExecuteCommandAsync();

        var result = await CreateService().BatchUpdateOrderStatusAsync(
            new() { "status-batch-valid", "status-batch-picking" },
            2
        );

        Assert.False(result.Success);
        Assert.Equal("INVALID_ORDER_STATUS_TRANSITION", result.ErrorCode);
        var orders = await _db.Queryable<WareHouseOrder>()
            .Where(item => item.OrderGUID.StartsWith("status-batch-"))
            .ToListAsync();
        Assert.Equal(1, orders.Single(item => item.OrderGUID == "status-batch-valid").FlowStatus);
        Assert.Equal(3, orders.Single(item => item.OrderGUID == "status-batch-picking").FlowStatus);
    }

    [Fact]
    public async Task Status批量CAS发现读取后状态变化时回滚整批更新()
    {
        await _db.Insertable(new[]
        {
            new WareHouseOrder
            {
                OrderGUID = "status-batch-cas-a",
                StoreCode = "S01",
                OrderNo = "STATUS-BATCH-CAS-A",
                FlowStatus = 1,
            },
            new WareHouseOrder
            {
                OrderGUID = "status-batch-cas-b",
                StoreCode = "S01",
                OrderNo = "STATUS-BATCH-CAS-B",
                FlowStatus = 1,
            },
        }).ExecuteCommandAsync();
        var injected = false;
        _db.Aop.OnLogExecuting = (sql, _) =>
        {
            if (injected
                || !sql.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                || !sql.Contains("WareHouseOrder", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            injected = true;
            _db.Updateable<WareHouseOrder>()
                .SetColumns(item => item.FlowStatus == 3)
                .Where(item => item.OrderGUID == "status-batch-cas-b")
                .ExecuteCommand();
        };
        ApiResponse<int> result;
        try
        {
            result = await CreateService().BatchUpdateOrderStatusAsync(
                new() { "status-batch-cas-a", "status-batch-cas-b", "status-batch-cas-a" },
                2
            );
        }
        finally
        {
            _db.Aop.OnLogExecuting = (sql, parameters) =>
                _sqlLogs.Add(FormatSqlLog(parameters, sql));
        }

        Assert.True(injected);
        Assert.False(result.Success);
        Assert.Equal("ORDER_STATUS_CONFLICT", result.ErrorCode);
        var orders = await _db.Queryable<WareHouseOrder>()
            .Where(item => item.OrderGUID.StartsWith("status-batch-cas-"))
            .ToListAsync();
        Assert.All(orders, item => Assert.Equal(1, item.FlowStatus));
    }

    [Fact]
    public async Task Status批量同目标状态保持兼容并按去重订单数返回()
    {
        await _db.Insertable(new WareHouseOrder
        {
            OrderGUID = "status-batch-idempotent",
            StoreCode = "S01",
            OrderNo = "STATUS-BATCH-IDEMPOTENT",
            FlowStatus = 1,
        }).ExecuteCommandAsync();

        var result = await CreateService().BatchUpdateOrderStatusAsync(
            new() { "status-batch-idempotent", "status-batch-idempotent" },
            1
        );

        Assert.True(result.Success);
        Assert.Equal(1, result.Data);
    }

    [Fact]
    public async Task StartPickingCAS不覆盖读取后完成状态()
    {
        await _db.Insertable(new WareHouseOrder
        {
            OrderGUID = "start-picking-cas",
            StoreCode = "S01",
            OrderNo = "START-PICKING-CAS",
            FlowStatus = 1,
        }).ExecuteCommandAsync();
        var injected = false;
        _db.Aop.OnLogExecuting = (sql, _) =>
        {
            if (injected
                || !sql.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                || !sql.Contains("WareHouseOrder", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            injected = true;
            _db.Updateable<WareHouseOrder>()
                .SetColumns(item => item.FlowStatus == 2)
                .Where(item => item.OrderGUID == "start-picking-cas")
                .ExecuteCommand();
        };
        ApiResponse<bool> result;
        try
        {
            result = await CreateService().StartPickingAsync("start-picking-cas");
        }
        finally
        {
            _db.Aop.OnLogExecuting = (sql, parameters) =>
                _sqlLogs.Add(FormatSqlLog(parameters, sql));
        }

        Assert.False(result.Success);
        Assert.Equal("ORDER_STATUS_CONFLICT", result.ErrorCode);
        Assert.Equal(
            2,
            (await _db.Queryable<WareHouseOrder>()
                .FirstAsync(item => item.OrderGUID == "start-picking-cas")).FlowStatus
        );
    }

    [Fact]
    public async Task CompleteOrderCAS不覆盖读取后配货状态()
    {
        await _db.Insertable(new WareHouseOrder
        {
            OrderGUID = "complete-order-cas",
            StoreCode = "S01",
            OrderNo = "COMPLETE-ORDER-CAS",
            FlowStatus = 1,
        }).ExecuteCommandAsync();
        var injected = false;
        _db.Aop.OnLogExecuting = (sql, _) =>
        {
            if (injected
                || !sql.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                || !sql.Contains("WareHouseOrder", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            injected = true;
            _db.Updateable<WareHouseOrder>()
                .SetColumns(item => item.FlowStatus == 3)
                .Where(item => item.OrderGUID == "complete-order-cas")
                .ExecuteCommand();
        };
        ApiResponse<bool> result;
        try
        {
            result = await CreateService().CompleteOrderAsync("complete-order-cas");
        }
        finally
        {
            _db.Aop.OnLogExecuting = (sql, parameters) =>
                _sqlLogs.Add(FormatSqlLog(parameters, sql));
        }

        Assert.False(result.Success);
        Assert.Equal("ORDER_STATUS_CONFLICT", result.ErrorCode);
        Assert.Equal(
            3,
            (await _db.Queryable<WareHouseOrder>()
                .FirstAsync(item => item.OrderGUID == "complete-order-cas")).FlowStatus
        );
    }

    [Fact]
    public async Task GetPagedListAsync_DoesNotDuplicateProductsWhenProductHasMultipleGrades()
    {
        await SeedProductWithGradesAsync("P001", "ITEM-001", "A", "B");

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("P001", item.ProductCode);
        Assert.Equal("ITEM-001-BAR", item.Barcode);
        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task GetPagedListAsync_GradeFilterKeepsMatchingProduct()
    {
        await SeedProductWithGradesAsync("P001", "ITEM-001", "A", "B");
        await SeedProductWithGradesAsync("P002", "ITEM-002", "C");

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            Grade = "B",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("P001", item.ProductCode);
        Assert.Equal("B", item.Grade);
        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task GetPagedListAsync_HomeSearchMatchesItemNumberAndBarcodeWithinChildCategoryAndGrade()
    {
        await SeedWarehouseCategoryAsync("CAT-PARENT", "Parent");
        await SeedWarehouseCategoryAsync("CAT-CHILD", "Child", "CAT-PARENT");
        await SeedWarehouseCategoryAsync("CAT-OTHER", "Other");
        await SeedProductAsync(
            "P-HOME-ITEM",
            "HB-HOME-001",
            barcode: "BAR-OTHER",
            warehouseCategoryGuid: "CAT-CHILD"
        );
        await SeedWarehouseProductAsync("P-HOME-ITEM");
        await SeedProductGradeAsync("P-HOME-ITEM", "A");
        await SeedProductAsync(
            "P-HOME-BAR",
            "ZZ-001",
            barcode: "BAR-HOME-001",
            warehouseCategoryGuid: "CAT-CHILD"
        );
        await SeedWarehouseProductAsync("P-HOME-BAR");
        await SeedProductGradeAsync("P-HOME-BAR", "A");
        await SeedProductAsync(
            "P-HOME-WRONG-GRADE",
            "HB-HOME-002",
            barcode: "BAR-HOME-002",
            warehouseCategoryGuid: "CAT-CHILD"
        );
        await SeedWarehouseProductAsync("P-HOME-WRONG-GRADE");
        await SeedProductGradeAsync("P-HOME-WRONG-GRADE", "B");
        await SeedProductAsync(
            "P-HOME-WRONG-CAT",
            "HB-HOME-003",
            barcode: "BAR-HOME-003",
            warehouseCategoryGuid: "CAT-OTHER"
        );
        await SeedWarehouseProductAsync("P-HOME-WRONG-CAT");
        await SeedProductGradeAsync("P-HOME-WRONG-CAT", "A");

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ItemNumber = "HOME",
            CategoryGUID = "CAT-PARENT",
            Grade = "A",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        Assert.Equal(2, result.Total);
        Assert.Equal(
            new[] { "P-HOME-BAR", "P-HOME-ITEM" },
            result.Items.Select(item => item.ProductCode).OrderBy(code => code).ToArray()
        );
        Assert.All(result.Items, item => Assert.Equal("A", item.Grade));
    }

    [Fact]
    public async Task GetPagedListAsync_HomeTwoStepPagingPreservesDefaultSortOrder()
    {
        await SeedProductAsync("P-SORT-003", "SORT-003");
        await SeedWarehouseProductAsync("P-SORT-003");
        await SeedProductAsync("P-SORT-001", "SORT-001");
        await SeedWarehouseProductAsync("P-SORT-001");
        await SeedProductAsync("P-SORT-002", "SORT-002");
        await SeedWarehouseProductAsync("P-SORT-002");

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ItemNumber = "SORT",
            PageNumber = 2,
            PageSize = 1,
            SortBy = "Default",
        });

        Assert.Equal(3, result.Total);
        var item = Assert.Single(result.Items);
        // 二段式查询先取分页 ProductCode，再回查展示字段，必须保留原分页排序。
        Assert.Equal("P-SORT-002", item.ProductCode);
    }

    [Fact]
    public async Task GetPagedListAsync_ExcludeExistingWarehouseProducts_ReturnsOnlyProductMasterRows()
    {
        await SeedLocalSupplierAsync("200", "Hot Bargain");
        await SeedProductAsync("P001", "ITEM-001", localSupplierCode: "200", purchasePrice: 2.5m);
        await SeedProductAsync("P002", "ITEM-002", localSupplierCode: "201", purchasePrice: 3.5m);
        await SeedProductAsync("P003", "ITEM-003", localSupplierCode: "200", purchasePrice: 4.5m);
        await SeedWarehouseProductAsync("P002", isDeleted: false);
        await SeedWarehouseProductAsync("P003", isDeleted: true);

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeExistingWarehouseProducts = true,
            LocalSupplierCode = "200",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        Assert.Equal(2, result.Total);
        Assert.Collection(
            result.Items,
            first =>
            {
                Assert.Equal("P001", first.ProductCode);
                Assert.Equal("ITEM-001-BAR", first.Barcode);
                Assert.Equal("200", first.LocalSupplierCode);
                Assert.Equal("Hot Bargain", first.LocalSupplierName);
                Assert.Equal(2.5m, first.ImportPrice);
                Assert.Equal(0, first.OEMPrice);
                Assert.Equal(1, first.MinOrderQuantity);
                Assert.Equal(0, first.StockQuantity);
            },
            second => Assert.Equal("P003", second.ProductCode)
        );
    }

    [Fact]
    public async Task GetPagedListAsync_ExcludeOrderGUID_RemovesProductsAlreadyInOrder()
    {
        await SeedProductAsync("P001", "ITEM-001");
        await SeedProductAsync("P002", "ITEM-002");
        await SeedDeletedOrderDetailAsync("ORDER-001", "P001", isDeleted: false);

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeExistingWarehouseProducts = true,
            ExcludeOrderGUID = "ORDER-001",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("P002", item.ProductCode);
    }

    [Fact]
    public async Task GetPagedListAsync_ExcludeExistingWarehouseProducts_AllowsSoftDeletedWarehouseRecord()
    {
        await SeedLocalSupplierAsync("200", "Hot Bargain");
        await SeedProductAsync("P010", "ITEM-010", localSupplierCode: "200", purchasePrice: 6.5m);
        await SeedWarehouseProductAsync("P010", isDeleted: true);

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeExistingWarehouseProducts = true,
            LocalSupplierCode = "200",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("P010", item.ProductCode);
        Assert.Equal("ITEM-010-BAR", item.Barcode);
        Assert.Equal(6.5m, item.ImportPrice);
        Assert.Equal(0, item.OEMPrice);
        Assert.Equal(1, item.MinOrderQuantity);
        Assert.Equal(0, item.StockQuantity);
    }

    [Fact]
    public async Task GetPagedListAsync_DefaultQueryStillReturnsWarehouseProductsOnly()
    {
        await SeedProductAsync("P001", "ITEM-001");
        await SeedProductAsync("P002", "ITEM-002");
        await SeedProductAsync("P003", "ITEM-003");
        await SeedWarehouseProductAsync("P001", isDeleted: false, oemPrice: 10m, importPrice: 7m);
        await SeedWarehouseProductAsync("P003", isDeleted: false, isActive: false);

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("P001", item.ProductCode);
        Assert.Equal("ITEM-001-BAR", item.Barcode);
        Assert.Equal(10m, item.OEMPrice);
        Assert.Equal(7m, item.ImportPrice);
    }

    [Fact]
    public async Task GetPagedListAsync_QuickAddQueryIncludesInactiveWarehouseProductsWhenFlagEnabled()
    {
        await SeedProductAsync("P-QUICK-ACTIVE", "QUICK-ACTIVE");
        await SeedWarehouseProductAsync("P-QUICK-ACTIVE");
        await SeedProductAsync("P-QUICK-INACTIVE-PRODUCT", "QUICK-INACTIVE-PRODUCT", isActive: false);
        await SeedWarehouseProductAsync("P-QUICK-INACTIVE-PRODUCT");
        await SeedProductAsync("P-QUICK-INACTIVE-WAREHOUSE", "QUICK-INACTIVE-WAREHOUSE");
        await SeedWarehouseProductAsync("P-QUICK-INACTIVE-WAREHOUSE", isActive: false);
        await SeedProductAsync("P-QUICK-DELETED-PRODUCT", "QUICK-DELETED-PRODUCT", isDeleted: true);
        await SeedWarehouseProductAsync("P-QUICK-DELETED-PRODUCT");
        await SeedProductAsync("P-QUICK-DELETED-WAREHOUSE", "QUICK-DELETED-WAREHOUSE");
        await SeedWarehouseProductAsync("P-QUICK-DELETED-WAREHOUSE", isDeleted: true);

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ItemNumber = "QUICK",
            IncludeInactiveWarehouseProducts = true,
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var productCodes = result.Items.Select(item => item.ProductCode).ToList();
        Assert.Contains("P-QUICK-ACTIVE", productCodes);
        Assert.Contains("P-QUICK-INACTIVE-PRODUCT", productCodes);
        Assert.Contains("P-QUICK-INACTIVE-WAREHOUSE", productCodes);
        Assert.DoesNotContain("P-QUICK-DELETED-PRODUCT", productCodes);
        Assert.DoesNotContain("P-QUICK-DELETED-WAREHOUSE", productCodes);
    }

    [Fact]
    public async Task GetPagedListAsync_IncludeInactiveFlagDoesNotBypassStatusOutsideQuickAddShape()
    {
        await SeedProductAsync(
            "P-ABUSE-ACTIVE",
            "ABUSE-ACTIVE",
            productName: "Inactive Search Active"
        );
        await SeedWarehouseProductAsync("P-ABUSE-ACTIVE");
        await SeedProductAsync(
            "P-ABUSE-INACTIVE",
            "ABUSE-INACTIVE",
            isActive: false,
            productName: "Inactive Search Hidden"
        );
        await SeedWarehouseProductAsync("P-ABUSE-INACTIVE");

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ItemNumber = "ABUSE",
            ProductName = "Inactive Search",
            IncludeInactiveWarehouseProducts = true,
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var productCodes = result.Items.Select(item => item.ProductCode).ToList();
        Assert.Contains("P-ABUSE-ACTIVE", productCodes);
        Assert.DoesNotContain("P-ABUSE-INACTIVE", productCodes);
    }

    [Fact]
    public async Task GetPagedListAsync_OrderPickerIncludesInactiveWarehouseProductsAndFiltersDomesticSupplier()
    {
        await SeedChinaSupplierAsync("CN001", "义乌一号");
        await SeedChinaSupplierAsync("CN002", "义乌二号");
        await SeedProductAsync("P-INACTIVE", "ITEM-INACTIVE");
        await SeedWarehouseProductAsync("P-INACTIVE", isActive: false, oemPrice: 11m, importPrice: 8m);
        await SeedDomesticProductAsync("P-INACTIVE", unitVolume: 0.1m, packingQuantity: 12, supplierCode: "CN001");
        await SeedProductAsync("P-OTHER-SUPPLIER", "ITEM-OTHER-SUPPLIER");
        await SeedWarehouseProductAsync("P-OTHER-SUPPLIER");
        await SeedDomesticProductAsync("P-OTHER-SUPPLIER", unitVolume: 0.1m, packingQuantity: 12, supplierCode: "CN002");
        await SeedProductAsync("P-IN-ORDER", "ITEM-IN-ORDER");
        await SeedWarehouseProductAsync("P-IN-ORDER");
        await SeedDomesticProductAsync("P-IN-ORDER", unitVolume: 0.1m, packingQuantity: 12, supplierCode: "CN001");
        await SeedDeletedOrderDetailAsync("ORDER-PICKER", "P-IN-ORDER", isDeleted: false);
        await SeedProductAsync("P-DELETED-WAREHOUSE", "ITEM-DELETED-WAREHOUSE");
        await SeedWarehouseProductAsync("P-DELETED-WAREHOUSE", isDeleted: true);
        await SeedDomesticProductAsync("P-DELETED-WAREHOUSE", unitVolume: 0.1m, packingQuantity: 12, supplierCode: "CN001");

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            SupplierCode = "CN001",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("P-INACTIVE", item.ProductCode);
        Assert.Equal("CN001", item.DomesticSupplierCode);
        Assert.Equal("义乌一号", item.DomesticSupplierName);
        Assert.Equal(11m, item.OEMPrice);
        Assert.Equal(8m, item.ImportPrice);
    }

    [Fact]
    public async Task GetPagedListAsync_OrderPickerResolvesDomesticSupplierByItemNumberWhenProductCodesDiffer()
    {
        await SeedChinaSupplierAsync("CN001", "义乌一号");
        await SeedChinaSupplierAsync("CN002", "义乌二号");
        await SeedProductAsync("P-HB008", "HB008-01", barcode: "9528500822001");
        await SeedWarehouseProductAsync("P-HB008");
        await SeedDomesticProductAsync(
            "DP-HB008-01",
            unitVolume: 0.1m,
            packingQuantity: 12,
            supplierCode: "CN001",
            hbProductNo: "HB008-01",
            barcode: "9528500822001"
        );

        var included = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            SupplierCode = "CN001",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var item = Assert.Single(included.Items);
        Assert.Equal("P-HB008", item.ProductCode);
        Assert.Equal("CN001", item.DomesticSupplierCode);
        Assert.Equal("义乌一号", item.DomesticSupplierName);

        var excluded = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            SupplierCode = "CN002",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        Assert.Empty(excluded.Items);
    }

    [Fact]
    public async Task GetPagedListAsync_OrderPickerResolvesDomesticSupplierByBarcodeOnly()
    {
        await SeedChinaSupplierAsync("CN001", "义乌一号");
        await SeedProductAsync("P-HB249", "HB249-001", barcode: "9528502490011");
        await SeedWarehouseProductAsync("P-HB249");
        await SeedDomesticProductAsync(
            "DP-HB249-DIFFERENT",
            unitVolume: 0.1m,
            packingQuantity: 12,
            supplierCode: "CN001",
            hbProductNo: "HB249-OTHER",
            barcode: "9528502490011"
        );

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            SupplierCode = "CN001",
            ItemNumber = "HB249-001",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("P-HB249", item.ProductCode);
        Assert.Equal("CN001", item.DomesticSupplierCode);
        Assert.Equal("义乌一号", item.DomesticSupplierName);
    }

    [Fact]
    public async Task GetPagedListAsync_OrderPickerUnifiedKeywordMatchesProductName()
    {
        await SeedProductAsync("P-NAME", "TABLE-01", productName: "Kids Chair + Table");
        await SeedWarehouseProductAsync("P-NAME");
        await SeedProductAsync("P-ITEM", "CHAIR-ITEM", productName: "Unrelated Product");
        await SeedWarehouseProductAsync("P-ITEM");

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            ItemNumber = "Kids",
            ProductName = "Kids",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("P-NAME", item.ProductCode);
        Assert.Equal("Kids Chair + Table", item.ProductName);
    }

    [Fact]
    public async Task GetPagedListAsync_OrderPickerColumnFilters_FilterByCoreColumnsAndSupplier()
    {
        await SeedChinaSupplierAsync("CN-FILTER", "义乌筛选供应商", shopNumber: "022");
        await SeedChinaSupplierAsync("CN-OTHER", "其他供应商");
        await SeedProductAsync("P-FILTER-OK", "HB-FILTER-001", barcode: "BAR-FILTER-001", productName: "Filter Chair");
        await SeedWarehouseProductAsync(
            "P-FILTER-OK",
            importPrice: 5.5m,
            stockQuantity: 8,
            minOrderQuantity: 3
        );
        await SeedDomesticProductAsync("P-FILTER-OK", 0.1m, 12, supplierCode: "CN-FILTER");
        await SeedProductAsync("P-FILTER-STOCK", "HB-FILTER-002", barcode: "BAR-FILTER-002", productName: "Filter Chair");
        await SeedWarehouseProductAsync(
            "P-FILTER-STOCK",
            importPrice: 5.5m,
            stockQuantity: 2,
            minOrderQuantity: 3
        );
        await SeedDomesticProductAsync("P-FILTER-STOCK", 0.1m, 12, supplierCode: "CN-FILTER");
        await SeedProductAsync("P-FILTER-SUPPLIER", "HB-FILTER-003", barcode: "BAR-FILTER-003", productName: "Filter Chair");
        await SeedWarehouseProductAsync(
            "P-FILTER-SUPPLIER",
            importPrice: 5.5m,
            stockQuantity: 8,
            minOrderQuantity: 3
        );
        await SeedDomesticProductAsync("P-FILTER-SUPPLIER", 0.1m, 12, supplierCode: "CN-OTHER");

        var supplierOnly = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "itemNumber",
            ColumnFilters = new StoreOrderProductColumnFiltersDto
            {
                SupplierKeyword = "CN-FILTER",
            },
        });
        Assert.True(
            supplierOnly.Items.Count == 2,
            "供应商过滤单独应命中两条 CN-FILTER 记录。最近 SQL:\n"
                + string.Join("\n---\n", _sqlLogs.TakeLast(8))
        );

        _sqlLogs.Clear();
        var shopNumberOnly = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "itemNumber",
            ColumnFilters = new StoreOrderProductColumnFiltersDto
            {
                SupplierKeyword = "022",
            },
        });
        Assert.True(
            shopNumberOnly.Items.Count == 2,
            "供应商店铺号过滤应命中两条 CN-FILTER 记录。最近 SQL:\n"
                + string.Join("\n---\n", _sqlLogs.TakeLast(8))
        );

        _sqlLogs.Clear();
        var textOnly = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "itemNumber",
            ColumnFilters = new StoreOrderProductColumnFiltersDto
            {
                ItemNumber = "HB-FILTER",
                ProductName = "chair",
                Barcode = "BAR-FILTER",
            },
        });
        Assert.True(
            textOnly.Items.Count == 3,
            "文本列过滤应先命中三条候选。实际商品："
                + string.Join(",", textOnly.Items.Select(item => item.ProductCode))
                + "。最近 SQL:\n"
                + string.Join("\n---\n", _sqlLogs.TakeLast(8))
        );

        _sqlLogs.Clear();
        var stockOnly = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "itemNumber",
            ColumnFilters = new StoreOrderProductColumnFiltersDto
            {
                StockQuantityMin = 5,
            },
        });
        Assert.True(
            stockOnly.Items.Count == 2,
            "库存下限过滤应命中两条候选。实际商品："
                + string.Join(",", stockOnly.Items.Select(item => item.ProductCode))
                + "。最近 SQL:\n"
                + string.Join("\n---\n", _sqlLogs.TakeLast(8))
        );

        _sqlLogs.Clear();
        var minOrderOnly = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "itemNumber",
            ColumnFilters = new StoreOrderProductColumnFiltersDto
            {
                MinOrderQuantityMax = 3,
            },
        });
        Assert.True(
            minOrderOnly.Items.Count == 3,
            "最小订货上限过滤应命中三条候选。实际商品："
                + string.Join(",", minOrderOnly.Items.Select(item => item.ProductCode))
                + "。最近 SQL:\n"
                + string.Join("\n---\n", _sqlLogs.TakeLast(8))
        );

        _sqlLogs.Clear();
        var importPriceOnly = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "itemNumber",
            ColumnFilters = new StoreOrderProductColumnFiltersDto
            {
                ImportPriceMin = 5m,
                ImportPriceMax = 6m,
            },
        });
        Assert.True(
            importPriceOnly.Items.Count == 3,
            "导入价范围过滤应命中三条候选。实际商品："
                + string.Join(",", importPriceOnly.Items.Select(item => item.ProductCode))
                + "。最近 SQL:\n"
                + string.Join("\n---\n", _sqlLogs.TakeLast(8))
        );

        _sqlLogs.Clear();
        var withoutSupplier = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "itemNumber",
            ColumnFilters = new StoreOrderProductColumnFiltersDto
            {
                ItemNumber = "HB-FILTER",
                ProductName = "chair",
                Barcode = "BAR-FILTER",
                StockQuantityMin = 5,
                MinOrderQuantityMax = 3,
                ImportPriceMin = 5m,
                ImportPriceMax = 6m,
            },
        });
        Assert.True(
            withoutSupplier.Items.Count == 2,
            "普通列过滤不含供应商时应命中库存和供应商两条候选。实际商品："
                + string.Join(",", withoutSupplier.Items.Select(item => item.ProductCode))
                + "。最近 SQL:\n"
                + string.Join("\n---\n", _sqlLogs.TakeLast(8))
        );

        _sqlLogs.Clear();
        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            PageNumber = 1,
            PageSize = 1,
            SortBy = "itemNumber",
            ColumnFilters = new StoreOrderProductColumnFiltersDto
            {
                ItemNumber = "HB-FILTER",
                ProductName = "chair",
                SupplierKeyword = "CN-FILTER",
                Barcode = "BAR-FILTER",
                StockQuantityMin = 5,
                MinOrderQuantityMax = 3,
                ImportPriceMin = 5m,
                ImportPriceMax = 6m,
            },
        });

        Assert.True(
            result.Items.Count == 1,
            "商品列过滤应只保留一条记录。最近 SQL:\n"
                + string.Join("\n---\n", _sqlLogs.TakeLast(8))
        );
        var item = Assert.Single(result.Items);
        Assert.Equal(1, result.Total);
        Assert.Equal("P-FILTER-OK", item.ProductCode);
        Assert.Equal("CN-FILTER", item.DomesticSupplierCode);
        Assert.Equal("义乌筛选供应商", item.DomesticSupplierName);
    }

    [Fact]
    public async Task GetPagedListAsync_OrderPickerColumnFilters_IgnoresMissingOrDeletedChinaSupplier()
    {
        await SeedChinaSupplierAsync("CN-DELETED", "已删除供应商", isDeleted: true, shopNumber: "099");
        await SeedChinaSupplierAsync("CN-DISABLED", "已禁用供应商", shopNumber: "088", status: 0);
        await SeedProductAsync("P-FILTER-MISSING-SUPPLIER", "SUP-MISSING");
        await SeedWarehouseProductAsync("P-FILTER-MISSING-SUPPLIER", importPrice: 5.5m);
        await SeedDomesticProductAsync(
            "P-FILTER-MISSING-SUPPLIER",
            0.1m,
            12,
            supplierCode: "CN-MISSING"
        );
        await SeedProductAsync("P-FILTER-DELETED-SUPPLIER", "SUP-DELETED");
        await SeedWarehouseProductAsync("P-FILTER-DELETED-SUPPLIER", importPrice: 5.5m);
        await SeedDomesticProductAsync(
            "P-FILTER-DELETED-SUPPLIER",
            0.1m,
            12,
            supplierCode: "CN-DELETED"
        );
        await SeedProductAsync("P-FILTER-DISABLED-SUPPLIER", "SUP-DISABLED");
        await SeedWarehouseProductAsync("P-FILTER-DISABLED-SUPPLIER", importPrice: 5.5m);
        await SeedDomesticProductAsync(
            "P-FILTER-DISABLED-SUPPLIER",
            0.1m,
            12,
            supplierCode: "CN-DISABLED"
        );

        var missingSupplier = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "itemNumber",
            ColumnFilters = new StoreOrderProductColumnFiltersDto
            {
                SupplierKeyword = "CN-MISSING",
            },
        });
        var deletedSupplierCode = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "itemNumber",
            ColumnFilters = new StoreOrderProductColumnFiltersDto
            {
                SupplierKeyword = "CN-DELETED",
            },
        });
        var deletedSupplierName = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "itemNumber",
            ColumnFilters = new StoreOrderProductColumnFiltersDto
            {
                SupplierKeyword = "已删除供应商",
            },
        });
        var deletedSupplierShopNumber = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "itemNumber",
            ColumnFilters = new StoreOrderProductColumnFiltersDto
            {
                SupplierKeyword = "099",
            },
        });
        var disabledSupplierShopNumber = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "itemNumber",
            ColumnFilters = new StoreOrderProductColumnFiltersDto
            {
                SupplierKeyword = "088",
            },
        });

        Assert.Empty(missingSupplier.Items);
        Assert.Empty(deletedSupplierCode.Items);
        Assert.Empty(deletedSupplierName.Items);
        Assert.Empty(deletedSupplierShopNumber.Items);
        Assert.Empty(disabledSupplierShopNumber.Items);
    }

    [Fact]
    public async Task GetPagedListAsync_DefaultWarehouseColumnFilters_FilterSupplierByShopNumber()
    {
        await SeedChinaSupplierAsync("CN-WH-SHOP", "普通仓库供应商", shopNumber: "W022");
        await SeedChinaSupplierAsync("CN-WH-DELETED", "普通仓库删除供应商", isDeleted: true, shopNumber: "W099");
        await SeedChinaSupplierAsync("CN-WH-DISABLED", "普通仓库禁用供应商", shopNumber: "W088", status: 0);
        await SeedProductAsync("P-WH-SHOP", "HB-WH-SHOP-001");
        await SeedWarehouseProductAsync("P-WH-SHOP");
        await SeedDomesticProductAsync("P-WH-SHOP", 0.1m, 12, supplierCode: "CN-WH-SHOP");
        await SeedProductAsync("P-WH-DELETED", "HB-WH-SHOP-002");
        await SeedWarehouseProductAsync("P-WH-DELETED");
        await SeedDomesticProductAsync("P-WH-DELETED", 0.1m, 12, supplierCode: "CN-WH-DELETED");
        await SeedProductAsync("P-WH-DISABLED", "HB-WH-SHOP-003");
        await SeedWarehouseProductAsync("P-WH-DISABLED");
        await SeedDomesticProductAsync("P-WH-DISABLED", 0.1m, 12, supplierCode: "CN-WH-DISABLED");

        var shopNumberResult = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            PageNumber = 1,
            PageSize = 18,
            SortBy = "itemNumber",
            ColumnFilters = new StoreOrderProductColumnFiltersDto
            {
                SupplierKeyword = "W022",
            },
        });
        var deletedShopNumberResult = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            PageNumber = 1,
            PageSize = 18,
            SortBy = "itemNumber",
            ColumnFilters = new StoreOrderProductColumnFiltersDto
            {
                SupplierKeyword = "W099",
            },
        });
        var disabledShopNumberResult = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            PageNumber = 1,
            PageSize = 18,
            SortBy = "itemNumber",
            ColumnFilters = new StoreOrderProductColumnFiltersDto
            {
                SupplierKeyword = "W088",
            },
        });

        var item = Assert.Single(shopNumberResult.Items);
        Assert.Equal("P-WH-SHOP", item.ProductCode);
        Assert.Empty(deletedShopNumberResult.Items);
        Assert.Empty(disabledShopNumberResult.Items);
    }

    [Fact]
    public async Task GetPagedListAsync_ProductMasterColumnFilters_FilterSupplierByShopNumber()
    {
        await SeedChinaSupplierAsync("CN-MASTER-SHOP", "商品主档供应商", shopNumber: "M022");
        await SeedChinaSupplierAsync("CN-MASTER-DELETED", "商品主档删除供应商", isDeleted: true, shopNumber: "M099");
        await SeedChinaSupplierAsync("CN-MASTER-DISABLED", "商品主档禁用供应商", shopNumber: "M088", status: 0);
        await SeedProductAsync("P-MASTER-SHOP", "HB-MASTER-SHOP-001", purchasePrice: 4.5m);
        await SeedDomesticProductAsync("P-MASTER-SHOP", 0.1m, 12, supplierCode: "CN-MASTER-SHOP");
        await SeedProductAsync("P-MASTER-DELETED", "HB-MASTER-SHOP-002", purchasePrice: 4.5m);
        await SeedDomesticProductAsync("P-MASTER-DELETED", 0.1m, 12, supplierCode: "CN-MASTER-DELETED");
        await SeedProductAsync("P-MASTER-DISABLED", "HB-MASTER-SHOP-003", purchasePrice: 4.5m);
        await SeedDomesticProductAsync("P-MASTER-DISABLED", 0.1m, 12, supplierCode: "CN-MASTER-DISABLED");

        var shopNumberResult = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeExistingWarehouseProducts = true,
            PageNumber = 1,
            PageSize = 18,
            SortBy = "itemNumber",
            ColumnFilters = new StoreOrderProductColumnFiltersDto
            {
                SupplierKeyword = "M022",
            },
        });
        var deletedShopNumberResult = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeExistingWarehouseProducts = true,
            PageNumber = 1,
            PageSize = 18,
            SortBy = "itemNumber",
            ColumnFilters = new StoreOrderProductColumnFiltersDto
            {
                SupplierKeyword = "M099",
            },
        });
        var disabledShopNumberResult = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeExistingWarehouseProducts = true,
            PageNumber = 1,
            PageSize = 18,
            SortBy = "itemNumber",
            ColumnFilters = new StoreOrderProductColumnFiltersDto
            {
                SupplierKeyword = "M088",
            },
        });

        var item = Assert.Single(shopNumberResult.Items);
        Assert.Equal("P-MASTER-SHOP", item.ProductCode);
        Assert.Empty(deletedShopNumberResult.Items);
        Assert.Empty(disabledShopNumberResult.Items);
    }

    [Fact]
    public async Task GetPagedListAsync_OrderPickerColumnSort_AppliesBeforePaging()
    {
        await SeedProductAsync("P-LOW", "SORT-LOW");
        await SeedWarehouseProductAsync("P-LOW", importPrice: 2m);
        await SeedProductAsync("P-HIGH", "SORT-HIGH");
        await SeedWarehouseProductAsync("P-HIGH", importPrice: 9m);
        await SeedProductAsync("P-MID", "SORT-MID");
        await SeedWarehouseProductAsync("P-MID", importPrice: 5m);

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-PICKER",
            PageNumber = 1,
            PageSize = 2,
            SortBy = "importPrice",
            SortDescending = true,
        });

        Assert.Equal(3, result.Total);
        Assert.Equal(new[] { "P-HIGH", "P-MID" }, result.Items.Select(item => item.ProductCode));
    }

    [Fact]
    public async Task ScanLookupProductsAsync_UsesSingleFieldLookupWhenBarcodeMatches()
    {
        await SeedProductAsync("P-BAR", "ITEM-BAR", barcode: "SCAN-001");
        await SeedWarehouseProductAsync("P-BAR");
        await SeedProductAsync("P-ITEM", "SCAN-001", barcode: "OTHER-BAR");
        await SeedWarehouseProductAsync("P-ITEM");
        _sqlLogs.Clear();

        var result = await CreateService().ScanLookupProductsAsync(new StoreOrderScanLookupRequestDto
        {
            Barcode = "SCAN-001",
        });

        Assert.True(result.Success, result.Message);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("P-BAR", item.ProductCode);
        Assert.Equal("barcode", result.Data.MatchType);
        Assert.DoesNotContain(
            _sqlLogs,
            sql => sql.Contains(" OR ", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task ScanLookupProductsAsync_FallsBackToItemNumberAfterBarcodeMiss()
    {
        await SeedProductAsync("P-ITEM", "SCAN-002", barcode: "OTHER-BAR");
        await SeedWarehouseProductAsync("P-ITEM");
        _sqlLogs.Clear();

        var result = await CreateService().ScanLookupProductsAsync(new StoreOrderScanLookupRequestDto
        {
            Barcode = "SCAN-002",
        });

        Assert.True(result.Success, result.Message);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("P-ITEM", item.ProductCode);
        Assert.Equal("fallback", result.Data.MatchType);
        Assert.Equal(3, _sqlLogs.Count);
        Assert.DoesNotContain(
            _sqlLogs,
            sql => sql.Contains(" OR ", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task ScanLookupProductsAsync_FallsBackToProductCodeAfterBarcodeAndItemNumberMiss()
    {
        await SeedProductAsync("SCAN-003", "ITEM-CODE", barcode: "OTHER-BAR");
        await SeedWarehouseProductAsync("SCAN-003");
        _sqlLogs.Clear();

        var result = await CreateService().ScanLookupProductsAsync(new StoreOrderScanLookupRequestDto
        {
            Barcode = "SCAN-003",
        });

        Assert.True(result.Success, result.Message);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("SCAN-003", item.ProductCode);
        Assert.Equal("fallback", result.Data.MatchType);
        Assert.Equal(4, _sqlLogs.Count);
        Assert.DoesNotContain(
            _sqlLogs,
            sql => sql.Contains(" OR ", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task ScanLookupProductsAsync_主查询不Join等级并批量补Grade()
    {
        await SeedProductWithGradesAsync("P-GRADE", "ITEM-GRADE", "A", "B");
        _sqlLogs.Clear();

        var result = await CreateService().ScanLookupProductsAsync(new StoreOrderScanLookupRequestDto
        {
            Barcode = "ITEM-GRADE-BAR",
        });

        Assert.True(result.Success, result.Message);
        var item = Assert.Single(result.Data!.Items);
        Assert.Equal("P-GRADE", item.ProductCode);
        Assert.Equal("A", item.Grade);
        Assert.NotEmpty(_sqlLogs);
        Assert.DoesNotContain(
            "ProductGrade",
            _sqlLogs[0],
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Contains(
            _sqlLogs.Skip(1),
            log => log.Contains("ProductGrade", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task ScanLookupAndAddToCartMutationAsync_单命中时查询并加购轻量返回()
    {
        await SeedProductAsync("P-SCAN-ADD", "ITEM-SCAN-ADD", barcode: "SCAN-ADD-001");
        await SeedWarehouseProductAsync("P-SCAN-ADD", oemPrice: 4m, importPrice: 2m, minOrderQuantity: 3);
        _sqlLogs.Clear();

        var result = await CreateService().ScanLookupAndAddToCartMutationAsync(
            new StoreOrderScanLookupAddRequestDto
            {
                StoreCode = "S001",
                Barcode = "SCAN-ADD-001",
            }
        );

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.True(result.Data!.Added);
        var item = Assert.Single(result.Data.Items);
        Assert.Equal("P-SCAN-ADD", item.ProductCode);
        Assert.NotNull(result.Data.Cart);
        Assert.Equal("P-SCAN-ADD", result.Data.Cart!.ProductCode);
        Assert.True(result.Data.Cart.Summary.CartRevision > 0);
        Assert.Equal(3, result.Data.Cart.Summary.TotalQuantity);
        Assert.Equal(12m, result.Data.Cart.Summary.TotalAmount);
        Assert.Equal(6m, result.Data.Cart.Summary.TotalImportAmount);
        Assert.DoesNotContain(
            "\"items\"",
            JsonSerializer.Serialize(result.Data.Cart),
            StringComparison.OrdinalIgnoreCase
        );
        Assert.Equal(0, _sqlLogs.Count(IsStandaloneProductPriceLookupSql));
        Assert.Equal(1, _sqlLogs.Count(IsCartSummaryAggregateSql));
    }

    [Fact]
    public void CartRevision_同毫秒内严格递增()
    {
        var method = typeof(StoreOrderReactService).GetMethod(
            "ResolveNextCartRevision",
            BindingFlags.Static | BindingFlags.NonPublic
        );
        Assert.NotNull(method);

        var previous = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local).AddTicks(2_000);
        var now = previous.AddTicks(1);
        var previousRevision = new DateTimeOffset(previous).ToUnixTimeMilliseconds();
        Assert.Equal(previousRevision, new DateTimeOffset(now).ToUnixTimeMilliseconds());

        var result = (ValueTuple<DateTime, long>)method.Invoke(null, new object?[] { previous, now })!;

        Assert.Equal(previousRevision + 1, result.Item2);
        Assert.Equal(result.Item2, new DateTimeOffset(result.Item1).ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task AddToCartMutationAsync_同购物车Revision单调递增()
    {
        await SeedProductAsync("P-REVISION", "ITEM-REVISION");
        await SeedWarehouseProductAsync("P-REVISION", oemPrice: 3m, importPrice: 2m);
        await SeedStoreOrderAsync("ORDER-REVISION", flowStatus: 0);
        var service = CreateService();

        var first = await service.AddToCartMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P-REVISION",
            Quantity = 1,
        });
        var second = await service.AddToCartMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P-REVISION",
            Quantity = 1,
        });

        Assert.True(first.Success, first.Message);
        Assert.True(second.Success, second.Message);
        Assert.True(first.Data!.Summary.CartRevision > 0);
        Assert.True(second.Data!.Summary.CartRevision > first.Data.Summary.CartRevision);
        Assert.Equal(2, second.Data.Summary.TotalQuantity);
    }

    [Fact]
    public async Task ScanLookupAndAddToCartMutationAsync_本地热路径2秒内完成()
    {
        const int noiseCount = 1200;
        var noiseProducts = Enumerable.Range(0, noiseCount)
            .Select(index => new Product
            {
                UUID = $"scan-noise-{index}-uuid",
                ProductCode = $"P-SCAN-NOISE-{index:D4}",
                ProductName = $"扫码噪音商品 {index:D4}",
                ItemNumber = $"ITEM-SCAN-NOISE-{index:D4}",
                Barcode = $"SCAN-NOISE-{index:D4}",
                IsActive = true,
                IsDeleted = false,
            })
            .ToList();
        var noiseWarehouseProducts = Enumerable.Range(0, noiseCount)
            .Select(index => new WarehouseProduct
            {
                ProductCode = $"P-SCAN-NOISE-{index:D4}",
                OEMPrice = 4m,
                ImportPrice = 2m,
                StockQuantity = 20,
                MinOrderQuantity = 1,
                IsActive = true,
                IsDeleted = false,
            })
            .ToList();
        await _db.Insertable(noiseProducts).ExecuteCommandAsync();
        await _db.Insertable(noiseWarehouseProducts).ExecuteCommandAsync();
        await SeedProductAsync("P-SCAN-SLA", "ITEM-SCAN-SLA", barcode: "SCAN-SLA-001");
        await SeedWarehouseProductAsync("P-SCAN-SLA", oemPrice: 4m, importPrice: 2m, minOrderQuantity: 1);

        // 本地 smoke 只防慢链路回退；生产 2 秒 SLA 仍以 shop-scan-perf 真机日志为准。
        var result = await CreateService().ScanLookupAndAddToCartMutationAsync(
            new StoreOrderScanLookupAddRequestDto
            {
                StoreCode = "S001",
                Barcode = "SCAN-SLA-001",
            }
        ).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(result.Success, result.Message);
        Assert.True(result.Data?.Added);
        Assert.Equal(1, result.Data?.Cart?.Summary.TotalQuantity);
    }

    [Fact]
    public async Task ScanLookupAndAddToCartMutationAsync_多命中时只返回候选不写购物车()
    {
        await SeedProductAsync("P-MULTI-1", "ITEM-MULTI-1", barcode: "SCAN-MULTI");
        await SeedWarehouseProductAsync("P-MULTI-1");
        await SeedProductAsync("P-MULTI-2", "ITEM-MULTI-2", barcode: "SCAN-MULTI");
        await SeedWarehouseProductAsync("P-MULTI-2");

        var result = await CreateService().ScanLookupAndAddToCartMutationAsync(
            new StoreOrderScanLookupAddRequestDto
            {
                StoreCode = "S001",
                Barcode = "SCAN-MULTI",
            }
        );

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.False(result.Data!.Added);
        Assert.Null(result.Data.Cart);
        Assert.Equal(2, result.Data.Items.Count);
        Assert.Empty(await _db.Queryable<WareHouseOrderDetails>().ToListAsync());
    }

    [Fact]
    public async Task BatchLookupProductsAsync_MatchesInactiveProductAndWarehouseRowsButKeepsDeletedRowsHidden()
    {
        await SeedProductAsync("P-INACTIVE-PRODUCT", "ITEM-INACTIVE-PRODUCT", isActive: false);
        await SeedWarehouseProductAsync("P-INACTIVE-PRODUCT");
        await SeedProductAsync("P-INACTIVE-WAREHOUSE", "ITEM-INACTIVE-WAREHOUSE");
        await SeedWarehouseProductAsync("P-INACTIVE-WAREHOUSE", isActive: false);
        await SeedProductAsync("P-DELETED-PRODUCT", "ITEM-DELETED-PRODUCT", isDeleted: true);
        await SeedWarehouseProductAsync("P-DELETED-PRODUCT");
        await SeedProductAsync("P-DELETED-WAREHOUSE", "ITEM-DELETED-WAREHOUSE");
        await SeedWarehouseProductAsync("P-DELETED-WAREHOUSE", isDeleted: true);

        var result = await CreateService().BatchLookupProductsAsync(new StoreOrderBatchLookupRequestDto
        {
            Codes = new List<string>
            {
                "ITEM-INACTIVE-PRODUCT",
                "ITEM-INACTIVE-WAREHOUSE",
                "ITEM-DELETED-PRODUCT",
                "ITEM-DELETED-WAREHOUSE",
            },
        });

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.Equal(
            "P-INACTIVE-PRODUCT",
            result.Data.Single(item => item.LookupCode == "ITEM-INACTIVE-PRODUCT").Product?.ProductCode
        );
        Assert.Equal(
            "P-INACTIVE-WAREHOUSE",
            result.Data.Single(item => item.LookupCode == "ITEM-INACTIVE-WAREHOUSE").Product?.ProductCode
        );
        Assert.Null(result.Data.Single(item => item.LookupCode == "ITEM-DELETED-PRODUCT").Product);
        Assert.Null(result.Data.Single(item => item.LookupCode == "ITEM-DELETED-WAREHOUSE").Product);
    }

    [Fact]
    public async Task PasteReplaceOrderLinesAsync_HandlesReplaceAppendSkipAndInvalidQuantities()
    {
        await SeedStoreOrderAsync("ORDER-PASTE-ACTION");
        await SeedOrderLineAsync("ORDER-PASTE-ACTION", "P-REPLACE", "ITEM-REPLACE", quantity: 5m, allocQuantity: 2m);
        await SeedOrderLineAsync("ORDER-PASTE-ACTION", "P-APPEND", "ITEM-APPEND", quantity: 4m, allocQuantity: 6m);
        await SeedOrderLineAsync("ORDER-PASTE-ACTION", "P-SKIP", "ITEM-SKIP", quantity: 3m, allocQuantity: 7m);
        await SeedOrderLineAsync("ORDER-PASTE-ACTION", "P-ZERO-EXISTING", "ITEM-ZERO-EXISTING", quantity: 4m, allocQuantity: 9m);
        await SeedOrderLineAsync("ORDER-PASTE-ACTION", "P-ZERO-DELETE", "ITEM-ZERO-DELETE", quantity: 0m, allocQuantity: 9m);
        await SeedProductAsync("P-NEW-ZERO", "ITEM-NEW-ZERO");
        await SeedWarehouseProductAsync("P-NEW-ZERO");
        await SeedProductAsync("P-NEW-VALID", "ITEM-NEW-VALID");
        await SeedWarehouseProductAsync("P-NEW-VALID");

        var result = await CreateService().PasteReplaceOrderLinesAsync(new PasteReplaceOrderLinesDto
        {
            OrderGUID = "ORDER-PASTE-ACTION",
            TargetField = StoreOrderPasteTargetFields.AllocQuantity,
            Items = new List<ProductQuantityDto>
            {
                new() { ProductCode = "P-REPLACE", Quantity = 10m, Action = "replace" },
                new() { ProductCode = "P-APPEND", Quantity = 5m, Action = "append" },
                new() { ProductCode = "P-SKIP", Quantity = 99m, Action = "skip" },
                new() { ProductCode = "P-ZERO-EXISTING", Quantity = 0m, Action = "replace" },
                new() { ProductCode = "P-ZERO-DELETE", Quantity = 0m, Action = "replace" },
                new() { ProductCode = "P-NEW-ZERO", Quantity = 0m, Action = "replace" },
                new() { ProductCode = "P-NEW-VALID", Quantity = 8m, Action = "append" },
            },
        });

        Assert.True(result.Success, result.Message);

        var rows = await _db.Queryable<WareHouseOrderDetails>()
            .Where(row => row.OrderGUID == "ORDER-PASTE-ACTION")
            .ToListAsync();

        Assert.Equal(10m, rows.Single(row => row.ProductCode == "P-REPLACE").AllocQuantity);
        Assert.Equal(11m, rows.Single(row => row.ProductCode == "P-APPEND").AllocQuantity);
        Assert.Equal(7m, rows.Single(row => row.ProductCode == "P-SKIP").AllocQuantity);
        Assert.Equal(0m, rows.Single(row => row.ProductCode == "P-ZERO-EXISTING").AllocQuantity);
        Assert.Equal(8m, rows.Single(row => row.ProductCode == "P-ZERO-EXISTING").ImportAmount);
        Assert.DoesNotContain(rows, row => row.ProductCode == "P-ZERO-DELETE");
        Assert.DoesNotContain(rows, row => row.ProductCode == "P-NEW-ZERO");
        Assert.Equal(8m, rows.Single(row => row.ProductCode == "P-NEW-VALID").AllocQuantity);
    }

    [Fact]
    public async Task PasteReplaceOrderLinesAsync_ReturnsFailureForUnknownExplicitAction()
    {
        await SeedStoreOrderAsync("ORDER-PASTE-UNKNOWN");
        await SeedOrderLineAsync("ORDER-PASTE-UNKNOWN", "P-UNKNOWN", "ITEM-UNKNOWN", quantity: 4m, allocQuantity: 6m);

        var result = await CreateService().PasteReplaceOrderLinesAsync(new PasteReplaceOrderLinesDto
        {
            OrderGUID = "ORDER-PASTE-UNKNOWN",
            TargetField = StoreOrderPasteTargetFields.AllocQuantity,
            Items = new List<ProductQuantityDto>
            {
                new() { ProductCode = "P-UNKNOWN", Quantity = 99m, Action = "mystery" },
            },
        });

        Assert.False(result.Success);
        Assert.Equal("Unsupported paste action", result.Message);

        var row = await _db.Queryable<WareHouseOrderDetails>()
            .Where(item => item.OrderGUID == "ORDER-PASTE-UNKNOWN" && item.ProductCode == "P-UNKNOWN")
            .FirstAsync();
        Assert.Equal(6m, row.AllocQuantity);
    }

    [Fact]
    public async Task PasteReplaceOrderLinesAsync_重新粘贴软删除明细_应复活并计入合计()
    {
        await SeedStoreOrderAsync("ORDER-PASTE-RESTORE");
        await SeedOrderLineAsync(
            "ORDER-PASTE-RESTORE",
            "P-RESTORE",
            "ITEM-RESTORE",
            quantity: 4m,
            allocQuantity: 6m,
            isDeleted: true
        );

        var result = await CreateService().PasteReplaceOrderLinesAsync(new PasteReplaceOrderLinesDto
        {
            OrderGUID = "ORDER-PASTE-RESTORE",
            TargetField = StoreOrderPasteTargetFields.AllocQuantity,
            Items = new List<ProductQuantityDto>
            {
                new() { ProductCode = "P-RESTORE", Quantity = 3m, Action = "replace" },
            },
        });

        Assert.True(result.Success, result.Message);

        var row = await _db.Queryable<WareHouseOrderDetails>()
            .Where(item => item.OrderGUID == "ORDER-PASTE-RESTORE" && item.ProductCode == "P-RESTORE")
            .FirstAsync();
        Assert.False(row.IsDeleted);
        Assert.Equal(3m, row.AllocQuantity);
        Assert.Equal(8m, row.ImportAmount);

        var order = await _db.Queryable<WareHouseOrder>()
            .Where(item => item.OrderGUID == "ORDER-PASTE-RESTORE")
            .FirstAsync();
        Assert.Equal(8m, order.ImportTotalAmount);
    }

    [Fact]
    public async Task PasteReplaceOrderLinesAsync_批量粘贴两百行_应保持语义且避免逐行查询()
    {
        await SeedStoreOrderAsync("ORDER-PASTE-BULK");
        await SeedOrderLineAsync("ORDER-PASTE-BULK", "P001", "ITEM-001", quantity: 3m, allocQuantity: 4m);
        await SeedOrderLineAsync("ORDER-PASTE-BULK", "P002", "ITEM-002", quantity: 5m, allocQuantity: 6m);
        await SeedOrderLineAsync("ORDER-PASTE-BULK", "P003", "ITEM-003", quantity: 7m, allocQuantity: 8m);
        await SeedOrderLineAsync("ORDER-PASTE-BULK", "P004", "ITEM-004", quantity: 9m, allocQuantity: 10m);

        for (var index = 5; index <= 210; index++)
        {
            var productCode = $"P{index:D3}";
            await SeedProductAsync(productCode, $"ITEM-{index:D3}");
            await SeedWarehouseProductAsync(productCode, oemPrice: 3m, importPrice: 2m);
        }

        var items = new List<ProductQuantityDto>
        {
            new() { ProductCode = "P001", Quantity = 5m, Action = "append" },
            new() { ProductCode = "P002", Quantity = 12m, ImportPrice = 4m, Action = "replace" },
            new() { ProductCode = "P003", Quantity = 99m, Action = "skip" },
            new() { ProductCode = "P004", Quantity = 0m, Action = "replace" },
        };
        items.AddRange(
            Enumerable.Range(5, 206)
                .Select(index => new ProductQuantityDto
                {
                    ProductCode = $"P{index:D3}",
                    Quantity = 2m,
                    ImportPrice = index == 5 ? 9.5m : null,
                    Action = "replace",
                })
        );

        _sqlLogs.Clear();
        var result = await CreateService().PasteReplaceOrderLinesAsync(new PasteReplaceOrderLinesDto
        {
            OrderGUID = "ORDER-PASTE-BULK",
            TargetField = StoreOrderPasteTargetFields.AllocQuantity,
            Items = items,
        });

        Assert.True(result.Success, result.Message);

        var rows = await _db.Queryable<WareHouseOrderDetails>()
            .Where(row => row.OrderGUID == "ORDER-PASTE-BULK")
            .ToListAsync();

        Assert.Equal(210, rows.Count);
        Assert.Equal(9m, rows.Single(row => row.ProductCode == "P001").AllocQuantity);
        Assert.Equal(12m, rows.Single(row => row.ProductCode == "P002").AllocQuantity);
        Assert.Equal(4m, rows.Single(row => row.ProductCode == "P002").ImportPrice);
        Assert.Equal(8m, rows.Single(row => row.ProductCode == "P003").AllocQuantity);
        Assert.Equal(0m, rows.Single(row => row.ProductCode == "P004").AllocQuantity);
        Assert.Equal(2m, rows.Single(row => row.ProductCode == "P005").AllocQuantity);
        Assert.Equal(9.5m, rows.Single(row => row.ProductCode == "P005").ImportPrice);
        Assert.Equal(0m, rows.Single(row => row.ProductCode == "P005").ImportAmount);

        Assert.True(
            _sqlLogs.Count <= 30,
            $"Excel 粘贴应保持批量 SQL，实际执行 {_sqlLogs.Count} 条。"
        );
    }

    [Fact]
    public async Task AddOrderLineAsync_配货中订单_允许添加商品并更新合计()
    {
        await SeedStoreOrderAsync("ORDER-PICKING-ADD", flowStatus: 3);
        await SeedProductAsync("P-PICKING-ADD", "ITEM-PICKING-ADD");
        await SeedWarehouseProductAsync("P-PICKING-ADD", oemPrice: 4m, importPrice: 2.5m);

        var result = await CreateService().AddOrderLineAsync(new AddOrderLineDto
        {
            OrderGUID = "ORDER-PICKING-ADD",
            ProductCode = "P-PICKING-ADD",
            Quantity = 6m,
        });

        Assert.True(result.Success, result.Message);

        var detail = await _db.Queryable<WareHouseOrderDetails>()
            .Where(item => item.OrderGUID == "ORDER-PICKING-ADD" && item.ProductCode == "P-PICKING-ADD")
            .FirstAsync();
        Assert.NotNull(detail);
        Assert.Equal(1m, detail.AllocQuantity);
        Assert.Equal(4m, detail.OEMAmount);
        Assert.Equal(0m, detail.ImportAmount);

        var order = await _db.Queryable<WareHouseOrder>()
            .Where(item => item.OrderGUID == "ORDER-PICKING-ADD")
            .FirstAsync();
        Assert.Equal(4m, order.OEMTotalAmount);
        Assert.Equal(0m, order.ImportTotalAmount);
    }

    [Fact]
    public async Task PasteReplaceOrderLinesAsync_配货中订单_允许Excel粘贴新增商品()
    {
        await SeedStoreOrderAsync("ORDER-PICKING-PASTE", flowStatus: 3);
        await SeedProductAsync("P-PICKING-PASTE", "ITEM-PICKING-PASTE");
        await SeedWarehouseProductAsync("P-PICKING-PASTE", oemPrice: 5m, importPrice: 3m);

        var result = await CreateService().PasteReplaceOrderLinesAsync(new PasteReplaceOrderLinesDto
        {
            OrderGUID = "ORDER-PICKING-PASTE",
            TargetField = StoreOrderPasteTargetFields.AllocQuantity,
            Items = new List<ProductQuantityDto>
            {
                new() { ProductCode = "P-PICKING-PASTE", Quantity = 7m, Action = "replace" },
            },
        });

        Assert.True(result.Success, result.Message);

        var detail = await _db.Queryable<WareHouseOrderDetails>()
            .Where(item => item.OrderGUID == "ORDER-PICKING-PASTE" && item.ProductCode == "P-PICKING-PASTE")
            .FirstAsync();
        Assert.NotNull(detail);
        Assert.Equal(7m, detail.AllocQuantity);
        Assert.Equal(35m, detail.OEMAmount);
        Assert.Equal(0m, detail.ImportAmount);
    }

    [Fact]
    public async Task AddOrderLineAsync_已完成订单_仍拒绝添加商品()
    {
        await SeedStoreOrderAsync("ORDER-COMPLETED-ADD", flowStatus: 2);
        await SeedProductAsync("P-COMPLETED-ADD", "ITEM-COMPLETED-ADD");
        await SeedWarehouseProductAsync("P-COMPLETED-ADD");

        var result = await CreateService().AddOrderLineAsync(new AddOrderLineDto
        {
            OrderGUID = "ORDER-COMPLETED-ADD",
            ProductCode = "P-COMPLETED-ADD",
            Quantity = 6m,
        });

        Assert.False(result.Success);
        Assert.Equal("Order not found or not editable", result.Message);

        var detailCount = await _db.Queryable<WareHouseOrderDetails>()
            .Where(item => item.OrderGUID == "ORDER-COMPLETED-ADD")
            .CountAsync();
        Assert.Equal(0, detailCount);
    }

    [Fact]
    public async Task 首页预热轻量查询_不执行Count且只取首屏商品()
    {
        for (var index = 1; index <= 25; index++)
        {
            var itemNumber = $"ITEM-{index:D3}";
            var productCode = $"P{index:D3}";
            await SeedProductAsync(productCode, itemNumber);
            await SeedWarehouseProductAsync(productCode, oemPrice: index, importPrice: index / 2m);
        }

        var service = CreateService();
        var warmUpMethod = typeof(StoreOrderReactService).GetMethod(
            "GetHomePageWarmUpPageAsync",
            BindingFlags.Instance | BindingFlags.Public
        );

        Assert.NotNull(warmUpMethod);

        _sqlLogs.Clear();
        var task =
            (Task<PagedListReactDto<StoreOrderProductDto>>)warmUpMethod!.Invoke(
                service,
                new object[] { 18, CancellationToken.None }
            )!;
        var result = await task;

        Assert.Equal(18, result.Items.Count);
        Assert.Equal(18, result.PageSize);
        Assert.DoesNotContain(
            _sqlLogs,
            log => log.Contains("COUNT(", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task AddToCartAsync_加购后应使用聚合重算主表金额并返回最新购物车()
    {
        await SeedProductAsync("P001", "ITEM-001");
        await SeedWarehouseProductAsync("P001", oemPrice: 3m, importPrice: 2m);
        await SeedDomesticProductAsync("P001", unitVolume: 0.12m, packingQuantity: 12);

        var service = CreateService();

        _sqlLogs.Clear();
        var result = await service.AddToCartAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P001",
            Quantity = 5,
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(5, result.Data.TotalQuantity);
        Assert.Equal(15m, result.Data.TotalAmount);
        Assert.Equal(10m, result.Data.TotalImportAmount);
        Assert.Equal(1, result.Data.TotalSKU);
        Assert.Contains(_sqlLogs, log => log.Contains("SUM", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddToCartMutationAsync_新增Sku返回摘要和变更行()
    {
        await SeedProductAsync("P001", "ITEM-001");
        await SeedWarehouseProductAsync("P001", oemPrice: 3m, importPrice: 2m);

        var result = await CreateService().AddToCartMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P001",
            Quantity = 5,
        });

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.False(result.Data!.Removed);
        Assert.Equal("P001", result.Data.ProductCode);
        Assert.Equal(5, result.Data.Summary.TotalQuantity);
        Assert.Equal(1, result.Data.Summary.TotalSku);
        Assert.Equal(15m, result.Data.Summary.TotalAmount);
        Assert.Equal(10m, result.Data.Summary.TotalImportAmount);
        Assert.NotNull(result.Data.ChangedItem);
        Assert.Equal("P001", result.Data.ChangedItem!.ProductCode);
        Assert.Equal(5m, result.Data.ChangedItem.Quantity);
        Assert.DoesNotContain(
            "\"items\"",
            JsonSerializer.Serialize(result.Data),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public async Task AddToCartMutationAsync_已有Sku累加并返回当前行()
    {
        await SeedProductAsync("P001", "ITEM-001");
        await SeedWarehouseProductAsync("P001", oemPrice: 3m, importPrice: 2m);
        var service = CreateService();

        await service.AddToCartMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P001",
            Quantity = 2,
        });
        var result = await service.AddToCartMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P001",
            Quantity = 3,
        });

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.False(result.Data!.Removed);
        Assert.Equal(5, result.Data.Summary.TotalQuantity);
        Assert.Equal(15m, result.Data.Summary.TotalAmount);
        Assert.Equal(5m, result.Data.ChangedItem?.Quantity);
    }

    [Fact]
    public async Task AddToCartMutationAsync_并发同Sku累加不丢数量且不产生重复明细()
    {
        const int scanCount = 20;
        await SeedProductAsync("P-CONCURRENT", "ITEM-CONCURRENT");
        await SeedWarehouseProductAsync("P-CONCURRENT", oemPrice: 3m, importPrice: 2m);
        await SeedStoreOrderAsync("ORDER-CONCURRENT", flowStatus: 0);
        await SeedOrderDetailOnlyAsync("ORDER-CONCURRENT", "P-CONCURRENT", quantity: 0, allocQuantity: 0);
        var service = CreateService();

        var results = await Task.WhenAll(
            Enumerable.Range(0, scanCount)
                .Select(_ => service.AddToCartMutationAsync(new AddToCartRequestDto
                {
                    StoreCode = "S001",
                    ProductCode = "P-CONCURRENT",
                    Quantity = 1,
                }))
        );

        Assert.All(results, result => Assert.True(result.Success, result.Message));
        var details = await _db.Queryable<WareHouseOrderDetails>()
            .Where(item => item.OrderGUID == "ORDER-CONCURRENT" && item.ProductCode == "P-CONCURRENT" && !item.IsDeleted)
            .ToListAsync();
        var detail = Assert.Single(details);
        Assert.Equal(scanCount, detail.Quantity);
        Assert.Equal(scanCount * 3m, detail.OEMAmount);
    }

    [Fact]
    public async Task AddToCartMutationAsync_并发同Sku无现存明细只插入一条()
    {
        const int scanCount = 20;
        await SeedProductAsync("P-CONCURRENT-NEW", "ITEM-CONCURRENT-NEW");
        await SeedWarehouseProductAsync("P-CONCURRENT-NEW", oemPrice: 3m, importPrice: 2m);
        await SeedStoreOrderAsync("ORDER-CONCURRENT-NEW", flowStatus: 0);
        var service = CreateService();

        var results = await Task.WhenAll(
            Enumerable.Range(0, scanCount)
                .Select(_ => service.AddToCartMutationAsync(new AddToCartRequestDto
                {
                    StoreCode = "S001",
                    ProductCode = "P-CONCURRENT-NEW",
                    Quantity = 1,
                }))
        );

        Assert.All(results, result => Assert.True(result.Success, result.Message));
        var details = await _db.Queryable<WareHouseOrderDetails>()
            .Where(item => item.OrderGUID == "ORDER-CONCURRENT-NEW" && item.ProductCode == "P-CONCURRENT-NEW" && !item.IsDeleted)
            .ToListAsync();
        var detail = Assert.Single(details);
        Assert.Equal(scanCount, detail.Quantity);
        Assert.Equal(scanCount * 3m, detail.OEMAmount);
    }

    [Fact]
    public void 购物车写入路径使用共享事务和SqlServer应用锁()
    {
        var source = File.ReadAllText(ResolveStoreOrderReactServicePath());
        var sharedLockBody = ExtractMethodBody(
            source,
            "private async Task<T> RunCartMutationLockedAsync<T>"
        );
        var coreBody = ExtractMethodBody(
            source,
            "private async Task<ApiResponse<StoreOrderCartMutationResultDto?>> AddToCartMutationCoreAsync"
        );
        var updateBody = ExtractMethodBody(
            source,
            "public async Task<ApiResponse<StoreOrderCartMutationResultDto?>> UpdateCartItemMutationAsync"
        );
        var submitBody = ExtractMethodBody(
            source,
            "public async Task<ApiResponse<bool>> SubmitOrderAsync"
        );
        var lockBody = ExtractMethodBody(
            source,
            "private static async Task AcquireCartMutationDatabaseLockAsync"
        );

        AssertInOrder(
            sharedLockBody,
            "BeginTranAsync",
            "AcquireCartMutationDatabaseLockAsync",
            "CommitTranAsync"
        );
        Assert.Contains("RollbackTranAsync", sharedLockBody);
        Assert.Contains("ShouldRollbackCartMutationResult", sharedLockBody);
        Assert.Contains("RunCartMutationLockedAsync(request.StoreCode", coreBody);
        Assert.Contains("RunCartMutationLockedAsync(request.StoreCode", updateBody);
        Assert.Contains("RunCartMutationLockedAsync(request.StoreCode", submitBody);
        Assert.Contains("sys.sp_getapplock", lockBody);
        Assert.Contains("@LockOwner = N'Transaction'", lockBody);
    }

    [Fact]
    public async Task UpdateCartItemMutationAsync_空车正数量回退加购不死锁()
    {
        await SeedProductAsync("P-FALLBACK", "ITEM-FALLBACK");
        await SeedWarehouseProductAsync("P-FALLBACK", oemPrice: 3m, importPrice: 2m);
        var service = CreateService();

        var result = await service.UpdateCartItemMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P-FALLBACK",
            Quantity = 2,
        }).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.Data?.Summary.TotalQuantity);
        Assert.Equal("P-FALLBACK", result.Data?.ChangedItem?.ProductCode);
    }

    [Fact]
    public async Task UpdateCartItemMutationAsync_覆盖数量并返回当前行()
    {
        await SeedProductAsync("P001", "ITEM-001");
        await SeedWarehouseProductAsync("P001", oemPrice: 3m, importPrice: 2m);
        var service = CreateService();
        await service.AddToCartMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P001",
            Quantity = 2,
        });

        var result = await service.UpdateCartItemMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P001",
            Quantity = 7,
        });

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.False(result.Data!.Removed);
        Assert.Equal(7, result.Data.Summary.TotalQuantity);
        Assert.Equal(21m, result.Data.Summary.TotalAmount);
        Assert.Equal(7m, result.Data.ChangedItem?.Quantity);
    }

    [Fact]
    public async Task UpdateCartItemMutationAsync_数量为零删除行()
    {
        await SeedProductAsync("P001", "ITEM-001");
        await SeedWarehouseProductAsync("P001", oemPrice: 3m, importPrice: 2m);
        var service = CreateService();
        await service.AddToCartMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P001",
            Quantity = 2,
        });

        var result = await service.UpdateCartItemMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P001",
            Quantity = 0,
        });

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.True(result.Data!.Removed);
        Assert.Null(result.Data.ChangedItem);
        Assert.Equal(0, result.Data.Summary.TotalQuantity);
        Assert.Equal(0, result.Data.Summary.TotalSku);
        Assert.Equal(0m, result.Data.Summary.TotalAmount);
    }

    [Fact]
    public async Task WarehouseStaffDedicatedCart_同店按当前仓库员工隔离()
    {
        await SeedProductAsync("P-SCOPE", "ITEM-SCOPE");
        await SeedWarehouseProductAsync("P-SCOPE", oemPrice: 3m, importPrice: 2m);

        await CreateService("store-user").AddToCartMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P-SCOPE",
            Quantity = 3,
        });
        await CreateService("warehouse-a", "WarehouseStaff").AddToCartMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P-SCOPE",
            Quantity = 1,
        });
        await CreateService("warehouse-b", "WarehouseStaff").AddToCartMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P-SCOPE",
            Quantity = 2,
        });

        var activeCarts = await _db.Queryable<WareHouseOrder>()
            .Where(item => item.StoreCode == "S001" && item.FlowStatus == 0 && !item.IsDeleted)
            .ToListAsync();
        Assert.Equal(3, activeCarts.Count);
        Assert.Contains(activeCarts, item => string.IsNullOrWhiteSpace(item.CartOwnerUserGuid));
        Assert.Contains(activeCarts, item => item.CartOwnerUserGuid == "warehouse-a");
        Assert.Contains(activeCarts, item => item.CartOwnerUserGuid == "warehouse-b");
        var cartQuantities = await _db.Queryable<WareHouseOrderDetails>()
            .InnerJoin<WareHouseOrder>((detail, order) => detail.OrderGUID == order.OrderGUID)
            .Where((detail, order) => order.StoreCode == "S001" && order.FlowStatus == 0 && !detail.IsDeleted)
            .Select((detail, order) => new
            {
                order.CartOwnerUserGuid,
                Quantity = detail.Quantity ?? 0,
            })
            .ToListAsync();
        Assert.Contains(cartQuantities, item => string.IsNullOrWhiteSpace(item.CartOwnerUserGuid) && item.Quantity == 3m);
        Assert.Contains(cartQuantities, item => item.CartOwnerUserGuid == "warehouse-a" && item.Quantity == 1m);
        Assert.Contains(cartQuantities, item => item.CartOwnerUserGuid == "warehouse-b" && item.Quantity == 2m);

        var storeCart = await CreateService("store-user").GetActiveCartAsync("S001");
        var staffACart = await CreateService("warehouse-a", "WarehouseStaff").GetActiveCartAsync("S001");
        var staffBCart = await CreateService("warehouse-b", "WarehouseStaff").GetActiveCartAsync("S001");

        Assert.Equal(3, storeCart.Data?.TotalQuantity);
        Assert.Equal(1, staffACart.Data?.TotalQuantity);
        Assert.Equal(2, staffBCart.Data?.TotalQuantity);
    }

    [Fact]
    public async Task SubmitOrderAsync_仓库员工只提交自己的专用购物车()
    {
        await SeedProductAsync("P-SUBMIT", "ITEM-SUBMIT");
        await SeedWarehouseProductAsync("P-SUBMIT", oemPrice: 3m, importPrice: 2m);
        await CreateService("store-user").AddToCartMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P-SUBMIT",
            Quantity = 3,
        });
        await CreateService("warehouse-a", "WarehouseStaff").AddToCartMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P-SUBMIT",
            Quantity = 1,
        });
        await CreateService("warehouse-b", "WarehouseStaff").AddToCartMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P-SUBMIT",
            Quantity = 2,
        });

        var submitResult = await CreateService("warehouse-a", "WarehouseStaff")
            .SubmitOrderAsync(new SubmitStoreOrderRequestDto
            {
                StoreCode = "S001",
                Remarks = "warehouse-a submit",
            });

        Assert.True(submitResult.Success, submitResult.Message);
        var orders = await _db.Queryable<WareHouseOrder>()
            .Where(item => item.StoreCode == "S001" && !item.IsDeleted)
            .ToListAsync();
        var storeCart = Assert.Single(orders, item => string.IsNullOrWhiteSpace(item.CartOwnerUserGuid));
        var staffAOrder = Assert.Single(orders, item => item.CartOwnerUserGuid == "warehouse-a");
        var staffBCart = Assert.Single(orders, item => item.CartOwnerUserGuid == "warehouse-b");

        Assert.Equal(0, storeCart.FlowStatus);
        Assert.Equal(1, staffAOrder.FlowStatus);
        Assert.Equal("warehouse-a submit", staffAOrder.Remarks);
        Assert.Equal("ORD-warehouse-a", staffAOrder.OrderNo);
        Assert.Equal(0, staffBCart.FlowStatus);
    }

    [Theory]
    [InlineData("Submit")]
    [InlineData("Create")]
    [InlineData("Copy")]
    public async Task 正式写入口等待StoreGate并在批次激活后重新拦截(string operation)
    {
        const string targetStoreCode = "S-GATE";
        const string targetStoreGuid = "store-gate";
        await _db.Insertable(new Store
        {
            StoreGUID = targetStoreGuid,
            StoreCode = targetStoreCode,
            StoreName = "门禁测试店",
            IsActive = true,
        }).ExecuteCommandAsync();

        if (operation == "Submit")
        {
            await _db.Insertable(new WareHouseOrder
            {
                OrderGUID = "gate-cart",
                StoreCode = targetStoreCode,
                OrderNo = "DRAFT-GATE",
                FlowStatus = 0,
            }).ExecuteCommandAsync();
            await _db.Insertable(new WareHouseOrderDetails
            {
                DetailGUID = "gate-cart-detail",
                OrderGUID = "gate-cart",
                StoreCode = targetStoreCode,
                ProductCode = "P-GATE",
                Quantity = 1,
            }).ExecuteCommandAsync();
        }
        else if (operation == "Copy")
        {
            await _db.Insertable(new WareHouseOrder
            {
                OrderGUID = "gate-source",
                StoreCode = "SOURCE",
                OrderNo = "SOURCE-GATE",
                FlowStatus = 1,
            }).ExecuteCommandAsync();
            await _db.Insertable(new WareHouseOrderDetails
            {
                DetailGUID = "gate-source-detail",
                OrderGUID = "gate-source",
                StoreCode = "SOURCE",
                ProductCode = "P-GATE",
                Quantity = 1,
            }).ExecuteCommandAsync();
        }

        var service = CreateService();
        IAsyncDisposable? heldLock = await PreorderMutationLock.AcquireProcessAsync(
            $"PreorderStoreGate:{targetStoreGuid}"
        );
        try
        {
            var writeTask = Task.Run(async () => operation switch
            {
                "Submit" => ToGateResult(await service.SubmitOrderAsync(
                    new SubmitStoreOrderRequestDto { StoreCode = targetStoreCode }
                )),
                "Create" => ToGateResult(await service.CreateOrderAsync(
                    new CreateStoreOrderDto { StoreCode = targetStoreCode }
                )),
                "Copy" => ToGateResult(await service.CopyOrderAsync(
                    new CopyOrderDto
                    {
                        SourceOrderGUID = "gate-source",
                        TargetStoreCode = targetStoreCode,
                    }
                )),
                _ => throw new InvalidOperationException($"未知写入口: {operation}"),
            });
            var firstCompleted = await Task.WhenAny(
                writeTask,
                Task.Delay(TimeSpan.FromSeconds(2))
            );
            Assert.NotSame(writeTask, firstCompleted);

            await _db.Insertable(new PreorderActivation
            {
                ActivationGuid = $"gate-activation-{operation}",
                TemplateGuid = "gate-template",
                PeriodNumber = 1,
                ActivationCode = $"PRE-GATE-{operation}",
                TemplateNameSnapshot = "原子门禁",
                SourceTemplateRevision = 1,
                StartAtUtc = DateTime.UtcNow.AddMinutes(-1),
                EndAtUtc = DateTime.UtcNow.AddHours(1),
                Status = PreorderActivationStatuses.Active,
            }).ExecuteCommandAsync();
            await _db.Insertable(new PreorderActivationStore
            {
                ActivationStoreGuid = $"gate-store-{operation}",
                ActivationGuid = $"gate-activation-{operation}",
                StoreGuid = targetStoreGuid,
                StoreCode = targetStoreCode,
                StoreName = "门禁测试店",
            }).ExecuteCommandAsync();
            await heldLock.DisposeAsync();
            heldLock = null;

            var result = await writeTask;
            Assert.False(result.Success);
            Assert.Equal("PREORDER_REQUIRED", result.ErrorCode);
        }
        finally
        {
            if (heldLock != null)
            {
                await heldLock.DisposeAsync();
            }
        }

        var targetOrders = await _db.Queryable<WareHouseOrder>()
            .Where(item => item.StoreCode == targetStoreCode && !item.IsDeleted)
            .ToListAsync();
        if (operation == "Submit")
        {
            Assert.Equal(0, Assert.Single(targetOrders).FlowStatus);
        }
        else
        {
            Assert.Empty(targetOrders);
        }

        static (bool Success, string? ErrorCode) ToGateResult<T>(ApiResponse<T> response) =>
            (response.Success, response.ErrorCode);
    }

    [Theory]
    [InlineData("Submit")]
    [InlineData("Create")]
    [InlineData("Copy")]
    public async Task 正式写入口在Preorder门禁不可用时放行普通订单(string operation)
    {
        const string targetStoreCode = "S-FAIL-OPEN";
        const string targetStoreGuid = "store-fail-open";
        await _db.Insertable(new Store
        {
            StoreGUID = targetStoreGuid,
            StoreCode = targetStoreCode,
            StoreName = "门禁降级店",
            IsActive = true,
        }).ExecuteCommandAsync();
        await _db.Insertable(new PreorderActivation
        {
            ActivationGuid = "invalid-gate-activation",
            TemplateGuid = "invalid-gate-template",
            PeriodNumber = 1,
            ActivationCode = "PRE-INVALID",
            TemplateNameSnapshot = "异常门禁",
            SourceTemplateRevision = 1,
            StartAtUtc = DateTime.UtcNow.AddMinutes(-1),
            EndAtUtc = DateTime.UtcNow.AddHours(1),
            Status = "UnexpectedStatus",
        }).ExecuteCommandAsync();
        await _db.Insertable(new PreorderActivationStore
        {
            ActivationStoreGuid = "invalid-gate-store",
            ActivationGuid = "invalid-gate-activation",
            StoreGuid = targetStoreGuid,
            StoreCode = targetStoreCode,
            StoreName = "门禁降级店",
        }).ExecuteCommandAsync();

        if (operation == "Submit")
        {
            await _db.Insertable(new WareHouseOrder
            {
                OrderGUID = "fail-open-cart",
                StoreCode = targetStoreCode,
                OrderNo = "DRAFT-FAIL-OPEN",
                FlowStatus = 0,
            }).ExecuteCommandAsync();
            await _db.Insertable(new WareHouseOrderDetails
            {
                DetailGUID = "fail-open-cart-detail",
                OrderGUID = "fail-open-cart",
                StoreCode = targetStoreCode,
                ProductCode = "P-FAIL-OPEN",
                Quantity = 1,
            }).ExecuteCommandAsync();
        }
        else if (operation == "Copy")
        {
            await _db.Insertable(new WareHouseOrder
            {
                OrderGUID = "fail-open-source",
                StoreCode = "SOURCE",
                OrderNo = "SOURCE-FAIL-OPEN",
                FlowStatus = 1,
            }).ExecuteCommandAsync();
            await _db.Insertable(new WareHouseOrderDetails
            {
                DetailGUID = "fail-open-source-detail",
                OrderGUID = "fail-open-source",
                StoreCode = "SOURCE",
                ProductCode = "P-FAIL-OPEN",
                Quantity = 1,
            }).ExecuteCommandAsync();
        }

        var service = CreateService();
        var result = operation switch
        {
            "Submit" => ToGateResult(await service.SubmitOrderAsync(
                new SubmitStoreOrderRequestDto { StoreCode = targetStoreCode }
            )),
            "Create" => ToGateResult(await service.CreateOrderAsync(
                new CreateStoreOrderDto { StoreCode = targetStoreCode }
            )),
            "Copy" => ToGateResult(await service.CopyOrderAsync(
                new CopyOrderDto
                {
                    SourceOrderGUID = "fail-open-source",
                    TargetStoreCode = targetStoreCode,
                }
            )),
            _ => throw new InvalidOperationException($"未知写入口: {operation}"),
        };

        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.Contains(
            await _db.Queryable<WareHouseOrder>()
                .Where(item => item.StoreCode == targetStoreCode && !item.IsDeleted)
                .ToListAsync(),
            item => item.FlowStatus == 1
        );

        static (bool Success, string? ErrorCode) ToGateResult<T>(ApiResponse<T> response) =>
            (response.Success, response.ErrorCode);
    }

    [Fact]
    public async Task RemoveFromCartAsync_不会用请求门店删除其他分店购物车明细()
    {
        await SeedProductAsync("P-REMOVE", "ITEM-REMOVE");
        await SeedWarehouseProductAsync("P-REMOVE", oemPrice: 3m, importPrice: 2m);
        await CreateService("store-user").AddToCartMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P-REMOVE",
            Quantity = 1,
        });
        await CreateService("store-user").AddToCartMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S002",
            ProductCode = "P-REMOVE",
            Quantity = 2,
        });
        var otherStoreDetail = await _db.Queryable<WareHouseOrderDetails>()
            .Where(item => item.StoreCode == "S002" && item.ProductCode == "P-REMOVE" && !item.IsDeleted)
            .FirstAsync();

        var result = await CreateService("store-user").RemoveFromCartAsync(new RemoveFromCartRequestDto
        {
            StoreCode = "S001",
            DetailGUID = otherStoreDetail.DetailGUID,
        });

        Assert.False(result.Success);
        var stillActiveDetail = await _db.Queryable<WareHouseOrderDetails>()
            .Where(item => item.DetailGUID == otherStoreDetail.DetailGUID)
            .FirstAsync();
        Assert.False(stillActiveDetail.IsDeleted);
    }

    [Fact]
    public async Task GetProductsDynamicDataAsync_仓库员工只返回自己的购物车数量()
    {
        await SeedProductAsync("P-DYNAMIC", "ITEM-DYNAMIC");
        await SeedWarehouseProductAsync("P-DYNAMIC", oemPrice: 3m, importPrice: 2m);
        await CreateService("store-user").AddToCartMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P-DYNAMIC",
            Quantity = 3,
        });
        await CreateService("warehouse-a", "WarehouseStaff").AddToCartMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P-DYNAMIC",
            Quantity = 1,
        });
        await CreateService("warehouse-b", "WarehouseStaff").AddToCartMutationAsync(new AddToCartRequestDto
        {
            StoreCode = "S001",
            ProductCode = "P-DYNAMIC",
            Quantity = 2,
        });
        var request = new StoreOrderDynamicDataRequestDto
        {
            StoreCode = "S001",
            ProductCodes = new List<string> { "P-DYNAMIC" },
        };

        var storeResult = await CreateService("store-user").GetProductsDynamicDataAsync(request);
        var staffAResult = await CreateService("warehouse-a", "WarehouseStaff")
            .GetProductsDynamicDataAsync(request);
        var staffBResult = await CreateService("warehouse-b", "WarehouseStaff")
            .GetProductsDynamicDataAsync(request);

        Assert.Equal(3m, Assert.Single(storeResult.Data!).CartQuantity);
        Assert.Equal(1m, Assert.Single(staffAResult.Data!).CartQuantity);
        Assert.Equal(2m, Assert.Single(staffBResult.Data!).CartQuantity);
    }

    [Fact]
    public async Task GetActiveCartSummaryAsync_ReturnsTotalsWithoutItems()
    {
        await SeedStoreOrderAsync("ORDER-CART-SUMMARY", flowStatus: 0);
        await SeedDomesticProductAsync("P001", unitVolume: 1.2m, packingQuantity: 6);
        await SeedDomesticProductAsync("P002", unitVolume: 0.9m, packingQuantity: 3);
        await _db.Insertable(new WareHouseOrderDetails
        {
            DetailGUID = "ORDER-CART-SUMMARY-P001",
            OrderGUID = "ORDER-CART-SUMMARY",
            StoreCode = "S001",
            ProductCode = "P001",
            Quantity = 5m,
            AllocQuantity = 2m,
            ImportPrice = 3m,
            ImportAmount = null,
            OEMPrice = 6m,
            OEMAmount = 30m,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new WareHouseOrderDetails
        {
            DetailGUID = "ORDER-CART-SUMMARY-P002",
            OrderGUID = "ORDER-CART-SUMMARY",
            StoreCode = "S001",
            ProductCode = "P002",
            Quantity = 2m,
            AllocQuantity = 1m,
            ImportPrice = 4m,
            ImportAmount = 11m,
            OEMPrice = 8m,
            OEMAmount = 16m,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Insertable(new WareHouseOrderDetails
        {
            DetailGUID = "ORDER-CART-SUMMARY-DELETED",
            OrderGUID = "ORDER-CART-SUMMARY",
            StoreCode = "S001",
            ProductCode = "P003",
            Quantity = 99m,
            AllocQuantity = 99m,
            ImportPrice = 9m,
            ImportAmount = 999m,
            OEMPrice = 9m,
            OEMAmount = 891m,
            IsDeleted = true,
        }).ExecuteCommandAsync();

        _sqlLogs.Clear();
        var result = await CreateService().GetActiveCartSummaryAsync("S001");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(7, result.Data.TotalQuantity);
        Assert.Equal(3, result.Data.TotalAllocQuantity);
        Assert.Equal(2, result.Data.TotalSKU);
        Assert.Equal(26m, result.Data.TotalImportAmount);
        Assert.Equal(1.6m, result.Data.TotalVolume, 10);
        Assert.Equal(1.6m, result.Data.TotalOrderVolume, 10);
        Assert.Equal(0.7m, result.Data.TotalAllocVolume, 10);
        Assert.Equal("测试地址", result.Data.StoreAddress);
        Assert.Empty(result.Data.Items);
        Assert.DoesNotContain(
            _sqlLogs,
            log => log.Contains("WarehouseProduct", StringComparison.OrdinalIgnoreCase)
        );
        Assert.DoesNotContain(
            _sqlLogs,
            log => log.Contains("ProductGrade", StringComparison.OrdinalIgnoreCase)
        );
        Assert.DoesNotContain(
            _sqlLogs,
            log => log.Contains("ProductImage", StringComparison.OrdinalIgnoreCase)
        );
        Assert.DoesNotContain(
            _sqlLogs,
            log => log.Contains("ProductName", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task GetActiveCartSummaryAsync_EmptyCartReturnsNull()
    {
        await SeedStoreOrderAsync("ORDER-NON-CART", flowStatus: 1);

        var result = await CreateService().GetActiveCartSummaryAsync("S001");

        Assert.True(result.Success);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task GetProductsDynamicDataAsync_返回购物车数量和每个商品最近历史订单()
    {
        await SeedStoreOrderAsync("ORDER-OLD", flowStatus: 1, orderDate: new DateTime(2026, 5, 1));
        await SeedStoreOrderAsync("ORDER-NEW", flowStatus: 1, orderDate: new DateTime(2026, 6, 1), insertStore: false);
        await SeedStoreOrderAsync("ORDER-CART", flowStatus: 0, orderDate: new DateTime(2026, 6, 2), insertStore: false);
        await SeedOrderDetailOnlyAsync("ORDER-OLD", "P001", quantity: 2m, allocQuantity: 1m);
        await SeedOrderDetailOnlyAsync("ORDER-NEW", "P001", quantity: 7m, allocQuantity: 3m);
        await SeedOrderDetailOnlyAsync("ORDER-CART", "P001", quantity: 5m, allocQuantity: 0m);

        var result = await CreateService().GetProductsDynamicDataAsync(new StoreOrderDynamicDataRequestDto
        {
            StoreCode = "S001",
            ProductCodes = new List<string> { "P001", "P002" },
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Collection(
            result.Data,
            first =>
            {
                Assert.Equal("P001", first.ProductCode);
                Assert.Equal(5m, first.CartQuantity);
                Assert.Equal(new DateTime(2026, 6, 1), first.LastOrderDate);
                Assert.Equal(7m, first.LastQuantity);
                Assert.Equal(3m, first.LastAllocQuantity);
            },
            second =>
            {
                Assert.Equal("P002", second.ProductCode);
                Assert.Equal(0m, second.CartQuantity);
                Assert.Null(second.LastOrderDate);
            }
        );
    }

    [Fact]
    public async Task GetProductsDynamicDataAsync_动态数据查询应在数据库侧聚合购物车和最近订单()
    {
        await SeedStoreOrderAsync(
            "ORDER-CART-1",
            flowStatus: 0,
            orderDate: new DateTime(2026, 6, 1)
        );
        await SeedStoreOrderAsync(
            "ORDER-CART-2",
            flowStatus: 0,
            orderDate: new DateTime(2026, 6, 2),
            insertStore: false
        );
        await SeedOrderDetailOnlyAsync("ORDER-CART-1", "P001", quantity: 2m, allocQuantity: 0m);
        await SeedOrderDetailOnlyAsync("ORDER-CART-2", "P001", quantity: 3m, allocQuantity: 0m);

        for (var index = 1; index <= 20; index++)
        {
            var orderGuid = $"ORDER-HISTORY-{index:D2}";
            await SeedStoreOrderAsync(
                orderGuid,
                flowStatus: 1,
                orderDate: new DateTime(2026, 5, index),
                insertStore: false
            );
            await SeedOrderDetailOnlyAsync(orderGuid, "P001", quantity: index, allocQuantity: index - 1);
        }

        _sqlLogs.Clear();

        var result = await CreateService().GetProductsDynamicDataAsync(new StoreOrderDynamicDataRequestDto
        {
            StoreCode = "S001",
            ProductCodes = new List<string> { "P001" },
        });

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!);
        Assert.Equal(5m, item.CartQuantity);
        Assert.Equal(new DateTime(2026, 5, 20), item.LastOrderDate);
        Assert.Equal(20m, item.LastQuantity);
        Assert.Equal(19m, item.LastAllocQuantity);
        Assert.Contains(
            _sqlLogs,
            log =>
                log.Contains("SUM", StringComparison.OrdinalIgnoreCase)
                && log.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)
        );
        Assert.Contains(
            _sqlLogs,
            log =>
                log.Contains("MAX", StringComparison.OrdinalIgnoreCase)
                && log.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task GetProductsDynamicDataAsync_最近订单跨度大时不应回查宽日期窗口()
    {
        await SeedStoreOrderAsync(
            "ORDER-P001-OLD",
            flowStatus: 1,
            orderDate: new DateTime(2025, 1, 1)
        );
        await SeedOrderDetailOnlyAsync("ORDER-P001-OLD", "P001", quantity: 1m, allocQuantity: 1m);

        for (var index = 1; index <= 30; index++)
        {
            var orderGuid = $"ORDER-P002-{index:D2}";
            await SeedStoreOrderAsync(
                orderGuid,
                flowStatus: 1,
                orderDate: new DateTime(2026, 5, index),
                insertStore: false
            );
            await SeedOrderDetailOnlyAsync(orderGuid, "P002", quantity: index, allocQuantity: index);
        }

        _sqlLogs.Clear();

        var result = await CreateService().GetProductsDynamicDataAsync(new StoreOrderDynamicDataRequestDto
        {
            StoreCode = "S001",
            ProductCodes = new List<string> { "P001", "P002" },
        });

        Assert.True(result.Success);
        Assert.Collection(
            result.Data!,
            first => Assert.Equal(new DateTime(2025, 1, 1), first.LastOrderDate),
            second =>
            {
                Assert.Equal("P002", second.ProductCode);
                Assert.Equal(new DateTime(2026, 5, 30), second.LastOrderDate);
                Assert.Equal(30m, second.LastQuantity);
            }
        );
        Assert.DoesNotContain(
            _sqlLogs,
            log =>
                log.Contains(">=", StringComparison.Ordinal)
                && log.Contains("OrderDate", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task GetPagedListAsync_ExcludeOrderGUID_AlsoAppliesToDefaultWarehouseQuery()
    {
        await SeedProductAsync("P001", "ITEM-001");
        await SeedProductAsync("P002", "ITEM-002");
        await SeedWarehouseProductAsync("P001");
        await SeedWarehouseProductAsync("P002");
        await SeedDeletedOrderDetailAsync("ORDER-001", "P001", isDeleted: false);

        var result = await CreateService().GetPagedListAsync(new StoreOrderFilterDto
        {
            ExcludeOrderGUID = "ORDER-001",
            PageNumber = 1,
            PageSize = 18,
            SortBy = "Default",
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("P002", item.ProductCode);
    }

    [Fact]
    public async Task GetOrderDetailAsync_ReturnsRequestedPageAndKeepsWholeOrderTotals()
    {
        await SeedStoreOrderAsync("ORDER-001");
        await SeedOrderLineAsync("ORDER-001", "P001", "ITEM-001", quantity: 10m, allocQuantity: 4m);
        await SeedOrderLineAsync("ORDER-001", "P002", "ITEM-002", quantity: 20m, allocQuantity: 8m);
        await SeedOrderLineAsync("ORDER-001", "P003", "ITEM-003", quantity: 30m, allocQuantity: 12m);

        var result = await CreateService().GetOrderDetailAsync(
            "ORDER-001",
            new StoreOrderDetailQueryDto
            {
                PageNumber = 1,
                PageSize = 2,
                SortBy = "itemNumber",
            }
        );

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.Items.Count);
        Assert.Equal(3, result.Data.ItemsTotal);
        Assert.Equal(1, result.Data.PageNumber);
        Assert.Equal(2, result.Data.PageSize);
        Assert.Equal(60, result.Data.TotalQuantity);
        Assert.Equal(24, result.Data.TotalAllocQuantity);
        Assert.Equal(3, result.Data.TotalSKU);
        Assert.Equal(new[] { "ITEM-001", "ITEM-002" }, result.Data.Items.Select(item => item.ItemNumber));
    }

    [Fact]
    public async Task GetOrderDetailAsync_SeparatesOrderAndAllocatedImportAmountsWhenStoredImportAmountIsStale()
    {
        await SeedStoreOrderAsync("ORDER-INVOICE-SUBTOTAL");
        await SeedProductAsync("P001", "ITEM-001");
        await SeedWarehouseProductAsync("P001", importPrice: 0.46m);
        await SeedDomesticProductAsync("P001", unitVolume: 1m, packingQuantity: 1);
        await _db.Insertable(new WareHouseOrderDetails
        {
            DetailGUID = "ORDER-INVOICE-SUBTOTAL-P001",
            OrderGUID = "ORDER-INVOICE-SUBTOTAL",
            StoreCode = "S001",
            ProductCode = "P001",
            Quantity = 12m,
            AllocQuantity = 0m,
            ImportPrice = 0.46m,
            ImportAmount = 5.52m,
            OEMPrice = 1m,
            OEMAmount = 12m,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        var result = await CreateService().GetOrderDetailAsync("ORDER-INVOICE-SUBTOTAL");

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.Equal(5.52m, result.Data!.TotalImportAmount);
        Assert.Equal(0m, result.Data.TotalAllocatedImportAmount);
        var item = Assert.Single(result.Data.Items);
        Assert.Equal(0m, item.AllocQuantity);
        Assert.Equal(5.52m, item.ImportAmount);
        Assert.Equal(0m, item.AllocatedImportAmount);
    }

    [Fact]
    public async Task GetOrderDetailAsync_DeduplicatesLocationCodesForCurrentPage()
    {
        await SeedStoreOrderAsync("ORDER-002");
        await SeedOrderLineAsync("ORDER-002", "P001", "ITEM-001", quantity: 1m, allocQuantity: 1m);
        await SeedLocationAsync("P001", "L-A", "A-01");
        await SeedLocationAsync("P001", "L-B", "B-01");
        await SeedLocationAsync("P001", "L-A-DUP", "A-01");

        var result = await CreateService().GetOrderDetailAsync(
            "ORDER-002",
            new StoreOrderDetailQueryDto { PageNumber = 1, PageSize = 50 }
        );

        Assert.NotNull(result.Data);
        var item = Assert.Single(result.Data.Items);
        Assert.Equal("A-01, B-01", item.LocationCode);
    }

    [Fact]
    public async Task GetOrderDetailAsync_NormalizesDefaultAndMaximumPageSize()
    {
        await SeedStoreOrderAsync("ORDER-PAGE-SIZE");
        await SeedOrderLineAsync("ORDER-PAGE-SIZE", "P001", "ITEM-001", quantity: 1m, allocQuantity: 1m);

        var defaultResult = await CreateService().GetOrderDetailAsync("ORDER-PAGE-SIZE");
        var maxResult = await CreateService().GetOrderDetailAsync(
            "ORDER-PAGE-SIZE",
            new StoreOrderDetailQueryDto { PageNumber = 1, PageSize = 1000 }
        );
        var clampedResult = await CreateService().GetOrderDetailAsync(
            "ORDER-PAGE-SIZE",
            new StoreOrderDetailQueryDto { PageNumber = 1, PageSize = 2000 }
        );

        Assert.NotNull(defaultResult.Data);
        Assert.Equal(200, defaultResult.Data.PageSize);
        Assert.NotNull(maxResult.Data);
        Assert.Equal(1000, maxResult.Data.PageSize);
        Assert.NotNull(clampedResult.Data);
        Assert.Equal(1000, clampedResult.Data.PageSize);
    }

    [Fact]
    public async Task GetOrderDetailAsync_SortsByLocationCodeWithEmptyLocationsFirst()
    {
        await SeedStoreOrderAsync("ORDER-LOCATION-SORT");
        await SeedOrderLineAsync("ORDER-LOCATION-SORT", "P-B", "ITEM-B", quantity: 1m, allocQuantity: 1m);
        await SeedOrderLineAsync("ORDER-LOCATION-SORT", "P-A2", "ITEM-A-002", quantity: 1m, allocQuantity: 1m);
        await SeedOrderLineAsync("ORDER-LOCATION-SORT", "P-NO", "ITEM-NO", quantity: 1m, allocQuantity: 1m);
        await SeedOrderLineAsync("ORDER-LOCATION-SORT", "P-A1", "ITEM-A-001", quantity: 1m, allocQuantity: 1m);
        await SeedLocationAsync("P-B", "L-B", "B-01");
        await SeedLocationAsync("P-A2", "L-A2", "A-01");
        await SeedLocationAsync("P-A2", "L-A2-Z", "Z-99");
        await SeedLocationAsync("P-A1", "L-A1", "A-01");

        _sqlLogs.Clear();
        var sorted = await CreateService().GetOrderDetailAsync(
            "ORDER-LOCATION-SORT",
            new StoreOrderDetailQueryDto
            {
                PageNumber = 1,
                PageSize = 10,
                SortBy = "locationCode",
            }
        );

        Assert.True(sorted.Success, sorted.Message);
        Assert.NotNull(sorted.Data);
        Assert.Equal(4, sorted.Data.ItemsTotal);
        Assert.Equal(1, sorted.Data.PageNumber);
        Assert.Equal(10, sorted.Data.PageSize);
        Assert.Equal(
            new[] { "P-NO", "P-A1", "P-A2", "P-B" },
            sorted.Data.Items.Select(item => item.ProductCode)
        );
        Assert.Equal(
            new[] { null, "A-01", "A-01, Z-99", "B-01" },
            sorted.Data.Items.Select(item => item.LocationCode)
        );
        Assert.Contains(
            _sqlLogs,
            log =>
                log.Contains("ProductLocation", StringComparison.OrdinalIgnoreCase)
                && log.Contains("Location", StringComparison.OrdinalIgnoreCase)
                && log.Contains("MIN", StringComparison.OrdinalIgnoreCase)
                && log.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)
                && log.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)
                && !log.Contains("COUNT(1)", StringComparison.OrdinalIgnoreCase)
        );

        _sqlLogs.Clear();
        var paged = await CreateService().GetOrderDetailAsync(
            "ORDER-LOCATION-SORT",
            new StoreOrderDetailQueryDto
            {
                PageNumber = 3,
                PageSize = 1,
                SortBy = "locationCode",
            }
        );

        Assert.NotNull(paged.Data);
        Assert.Equal(4, paged.Data.ItemsTotal);
        Assert.Equal(3, paged.Data.PageNumber);
        Assert.Equal(1, paged.Data.PageSize);
        Assert.Equal("P-A2", Assert.Single(paged.Data.Items).ProductCode);
        Assert.Equal("A-01, Z-99", paged.Data.Items.Single().LocationCode);
        Assert.Contains(
            _sqlLogs,
            log =>
                log.Contains("ProductLocation", StringComparison.OrdinalIgnoreCase)
                && log.Contains("MIN", StringComparison.OrdinalIgnoreCase)
                && log.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)
                && !log.Contains("COUNT(1)", StringComparison.OrdinalIgnoreCase)
        );

        var descending = await CreateService().GetOrderDetailAsync(
            "ORDER-LOCATION-SORT",
            new StoreOrderDetailQueryDto
            {
                PageNumber = 2,
                PageSize = 1,
                SortBy = "locationCode",
                SortDescending = true,
            }
        );

        Assert.NotNull(descending.Data);
        Assert.Equal("P-B", Assert.Single(descending.Data.Items).ProductCode);
    }

    [Fact]
    public async Task GetOrderDetailAsync_UsesDatabasePaging_AndSupportsInactiveStatFilter()
    {
        await SeedStoreOrderAsync("ORDER-003");
        await SeedOrderLineAsync("ORDER-003", "P001", "ITEM-001", quantity: 1m, allocQuantity: 1m);
        await SeedOrderLineAsync(
            "ORDER-003",
            "P002",
            "ITEM-002",
            quantity: 2m,
            allocQuantity: 1m,
            isActive: true,
            warehouseIsActive: false
        );
        await SeedOrderLineAsync("ORDER-003", "P003", "ITEM-003", quantity: 3m, allocQuantity: 1m);

        _sqlLogs.Clear();
        var result = await CreateService().GetOrderDetailAsync(
            "ORDER-003",
            new StoreOrderDetailQueryDto
            {
                PageNumber = 2,
                PageSize = 1,
                SortBy = "productCode",
                SortDescending = false,
            }
        );

        Assert.NotNull(result.Data);
        Assert.Equal("P002", Assert.Single(result.Data.Items).ProductCode);
        Assert.Contains(
            _sqlLogs,
            log =>
                log.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)
                && !log.Contains("COUNT(1)", StringComparison.OrdinalIgnoreCase)
        );

        var inactiveResult = await CreateService().GetOrderDetailAsync(
            "ORDER-003",
            new StoreOrderDetailQueryDto
            {
                StatFilter = "inactive",
                PageNumber = 1,
                PageSize = 50,
            }
        );

        Assert.NotNull(inactiveResult.Data);
        var inactiveItem = Assert.Single(inactiveResult.Data.Items);
        Assert.Equal("P002", inactiveItem.ProductCode);
        Assert.False(inactiveItem.IsActive);

        var activeResult = await CreateService().GetOrderDetailAsync(
            "ORDER-003",
            new StoreOrderDetailQueryDto
            {
                StatFilter = "active",
                PageNumber = 1,
                PageSize = 50,
            }
        );

        Assert.NotNull(activeResult.Data);
        Assert.DoesNotContain(activeResult.Data.Items, item => item.ProductCode == "P002");
    }

    [Fact]
    public async Task GetOrderDetailAsync_FiltersDetailColumnsBeforePaging()
    {
        await SeedStoreOrderAsync("ORDER-DETAIL-FILTER");
        await SeedOrderLineAsync(
            "ORDER-DETAIL-FILTER",
            "P-FIRST-NONMATCH",
            "AA-FIRST",
            quantity: 12m,
            allocQuantity: 3m,
            productName: "Different Product",
            barcode: "BAR-MATCH-9528",
            importPrice: 2.25m
        );
        await SeedLocationAsync("P-FIRST-NONMATCH", "L-FIRST-NONMATCH", "A-01-01");
        await SeedOrderLineAsync(
            "ORDER-DETAIL-FILTER",
            "P-MATCH",
            "ZZ-HB043-MATCH",
            quantity: 12m,
            allocQuantity: 3m,
            productName: "Money Tin Match",
            barcode: "BAR-MATCH-9528",
            importPrice: 2.25m
        );
        await SeedLocationAsync("P-MATCH", "L-MATCH", "A-01-01");
        await SeedOrderLineAsync(
            "ORDER-DETAIL-FILTER",
            "P-NAME-MISS",
            "ZZ-HB043-NAME",
            quantity: 12m,
            allocQuantity: 3m,
            productName: "Different Product",
            barcode: "BAR-MATCH-9528",
            importPrice: 2.25m
        );
        await SeedLocationAsync("P-NAME-MISS", "L-NAME-MISS", "A-01-01");
        await SeedOrderLineAsync(
            "ORDER-DETAIL-FILTER",
            "P-STATUS-MISS",
            "ZZ-HB043-STATUS",
            quantity: 12m,
            allocQuantity: 3m,
            warehouseIsActive: false,
            productName: "Money Tin Match",
            barcode: "BAR-MATCH-9528",
            importPrice: 2.25m
        );
        await SeedLocationAsync("P-STATUS-MISS", "L-STATUS-MISS", "A-01-01");

        var result = await CreateService().GetOrderDetailAsync(
            "ORDER-DETAIL-FILTER",
            new StoreOrderDetailQueryDto
            {
                PageNumber = 1,
                PageSize = 1,
                ItemNumber = "HB043",
                ProductName = "Money",
                Barcode = "9528",
                LocationCode = "A-01",
                QuantityMin = 10m,
                QuantityMax = 15m,
                AllocQuantityMin = 2m,
                AllocQuantityMax = 4m,
                ImportPriceMin = 2m,
                ImportPriceMax = 3m,
                IsActive = true,
            }
        );

        Assert.NotNull(result.Data);
        Assert.Equal(1, result.Data.ItemsTotal);
        Assert.Equal("P-MATCH", Assert.Single(result.Data.Items).ProductCode);
    }

    [Theory]
    [InlineData("itemNumber")]
    [InlineData("productName")]
    [InlineData("barcode")]
    [InlineData("locationCode")]
    [InlineData("quantityMin")]
    [InlineData("quantityMax")]
    [InlineData("allocQuantityMin")]
    [InlineData("allocQuantityMax")]
    [InlineData("importPriceMin")]
    [InlineData("importPriceMax")]
    [InlineData("isActive")]
    public async Task GetOrderDetailAsync_AppliesEachDetailColumnFilter(string differingField)
    {
        await SeedStoreOrderAsync($"ORDER-DETAIL-FILTER-{differingField}");
        await SeedOrderLineAsync(
            $"ORDER-DETAIL-FILTER-{differingField}",
            $"P-MATCH-{differingField}",
            "HB043-MATCH",
            quantity: 12m,
            allocQuantity: 3m,
            productName: "Money Tin Match",
            barcode: "BAR-MATCH-9528",
            importPrice: 2.25m
        );
        await SeedLocationAsync($"P-MATCH-{differingField}", $"L-MATCH-{differingField}", "A-01-01");

        var missProductCode = $"P-MISS-{differingField}";
        var missItemNumber = differingField == "itemNumber" ? "NO-MATCH" : "HB043-MISS";
        var missProductName = differingField == "productName" ? "Different Product" : "Money Tin Match";
        var missBarcode = differingField == "barcode" ? "BAR-NOPE" : "BAR-MATCH-9528";
        var missQuantity = differingField == "quantityMin" ? 9m : differingField == "quantityMax" ? 16m : 12m;
        var missAllocQuantity =
            differingField == "allocQuantityMin" ? 1m : differingField == "allocQuantityMax" ? 5m : 3m;
        var missImportPrice =
            differingField == "importPriceMin" ? 1.5m : differingField == "importPriceMax" ? 3.5m : 2.25m;
        var missIsActive = differingField != "isActive";
        var missLocationCode = differingField == "locationCode" ? "B-99-01" : "A-01-01";

        await SeedOrderLineAsync(
            $"ORDER-DETAIL-FILTER-{differingField}",
            missProductCode,
            missItemNumber,
            quantity: missQuantity,
            allocQuantity: missAllocQuantity,
            warehouseIsActive: missIsActive,
            productName: missProductName,
            barcode: missBarcode,
            importPrice: missImportPrice
        );
        await SeedLocationAsync(missProductCode, $"L-MISS-{differingField}", missLocationCode);

        var result = await CreateService().GetOrderDetailAsync(
            $"ORDER-DETAIL-FILTER-{differingField}",
            new StoreOrderDetailQueryDto
            {
                PageNumber = 1,
                PageSize = 10,
                ItemNumber = "HB043",
                ProductName = "Money",
                Barcode = "9528",
                LocationCode = "A-01",
                QuantityMin = 10m,
                QuantityMax = 15m,
                AllocQuantityMin = 2m,
                AllocQuantityMax = 4m,
                ImportPriceMin = 2m,
                ImportPriceMax = 3m,
                IsActive = true,
            }
        );

        Assert.NotNull(result.Data);
        Assert.Equal(1, result.Data.ItemsTotal);
        Assert.Equal($"P-MATCH-{differingField}", Assert.Single(result.Data.Items).ProductCode);
    }

    [Theory]
    [InlineData("itemNumber", "P-HIGH")]
    [InlineData("productName", "P-HIGH")]
    [InlineData("barcode", "P-HIGH")]
    [InlineData("locationCode", "P-HIGH")]
    [InlineData("quantity", "P-LOW")]
    [InlineData("allocQuantity", "P-LOW")]
    [InlineData("importPrice", "P-HIGH")]
    [InlineData("isActive", "P-MID")]
    public async Task GetOrderDetailAsync_SortsSupportedDetailColumnsBeforePaging(
        string sortBy,
        string expectedFirstProductCode
    )
    {
        await SeedStoreOrderAsync("ORDER-DETAIL-SORT");
        await SeedOrderLineAsync(
            "ORDER-DETAIL-SORT",
            "P-LOW",
            "C-ITEM",
            quantity: 1m,
            allocQuantity: 1m,
            productName: "Gamma Product",
            barcode: "C-BAR",
            importPrice: 3m
        );
        await SeedLocationAsync("P-LOW", "L-C", "C-03");
        await SeedOrderLineAsync(
            "ORDER-DETAIL-SORT",
            "P-MID",
            "B-ITEM",
            quantity: 5m,
            allocQuantity: 3m,
            warehouseIsActive: false,
            productName: "Beta Product",
            barcode: "B-BAR",
            importPrice: 2m
        );
        await SeedLocationAsync("P-MID", "L-B", "B-02");
        await SeedOrderLineAsync(
            "ORDER-DETAIL-SORT",
            "P-HIGH",
            "A-ITEM",
            quantity: 9m,
            allocQuantity: 5m,
            productName: "Alpha Product",
            barcode: "A-BAR",
            importPrice: 1m
        );
        await SeedLocationAsync("P-HIGH", "L-A", "A-01");

        var result = await CreateService().GetOrderDetailAsync(
            "ORDER-DETAIL-SORT",
            new StoreOrderDetailQueryDto
            {
                PageNumber = 1,
                PageSize = 1,
                SortBy = sortBy,
                SortDescending = false,
            }
        );

        Assert.NotNull(result.Data);
        Assert.Equal(3, result.Data.ItemsTotal);
        Assert.Equal(expectedFirstProductCode, Assert.Single(result.Data.Items).ProductCode);
    }

    [Fact]
    public async Task GetOrderDetailAsync_SortsOrderAndAllocatedImportAmountsSeparately()
    {
        await SeedStoreOrderAsync("ORDER-DETAIL-SORT-IMPORT-AMOUNT");
        await SeedProductAsync("P-COMPUTED-LOW", "A-ITEM");
        await SeedWarehouseProductAsync("P-COMPUTED-LOW", importPrice: 10m);
        await SeedDomesticProductAsync("P-COMPUTED-LOW", unitVolume: 1m, packingQuantity: 1);
        await SeedProductAsync("P-COMPUTED-HIGH", "B-ITEM");
        await SeedWarehouseProductAsync("P-COMPUTED-HIGH", importPrice: 1m);
        await SeedDomesticProductAsync("P-COMPUTED-HIGH", unitVolume: 1m, packingQuantity: 1);
        await _db.Insertable(new[]
        {
            new WareHouseOrderDetails
            {
                DetailGUID = "ORDER-DETAIL-SORT-IMPORT-AMOUNT-LOW",
                OrderGUID = "ORDER-DETAIL-SORT-IMPORT-AMOUNT",
                StoreCode = "S001",
                ProductCode = "P-COMPUTED-LOW",
                Quantity = 12m,
                AllocQuantity = 0m,
                ImportPrice = 10m,
                ImportAmount = 99m,
                OEMPrice = 1m,
                OEMAmount = 12m,
                IsDeleted = false,
            },
            new WareHouseOrderDetails
            {
                DetailGUID = "ORDER-DETAIL-SORT-IMPORT-AMOUNT-HIGH",
                OrderGUID = "ORDER-DETAIL-SORT-IMPORT-AMOUNT",
                StoreCode = "S001",
                ProductCode = "P-COMPUTED-HIGH",
                Quantity = 2m,
                AllocQuantity = 2m,
                ImportPrice = 1m,
                ImportAmount = 1m,
                OEMPrice = 1m,
                OEMAmount = 2m,
                IsDeleted = false,
            },
        }).ExecuteCommandAsync();

        var allocatedSortResult = await CreateService().GetOrderDetailAsync(
            "ORDER-DETAIL-SORT-IMPORT-AMOUNT",
            new StoreOrderDetailQueryDto
            {
                PageNumber = 1,
                PageSize = 1,
                SortBy = "allocatedImportAmount",
                SortDescending = false,
            }
        );

        Assert.True(allocatedSortResult.Success, allocatedSortResult.Message);
        Assert.NotNull(allocatedSortResult.Data);
        var allocatedSortItem = Assert.Single(allocatedSortResult.Data!.Items);
        Assert.Equal("P-COMPUTED-LOW", allocatedSortItem.ProductCode);
        Assert.Equal(99m, allocatedSortItem.ImportAmount);
        Assert.Equal(0m, allocatedSortItem.AllocatedImportAmount);

        var orderSortResult = await CreateService().GetOrderDetailAsync(
            "ORDER-DETAIL-SORT-IMPORT-AMOUNT",
            new StoreOrderDetailQueryDto
            {
                PageNumber = 1,
                PageSize = 1,
                SortBy = "importAmount",
                SortDescending = false,
            }
        );

        Assert.True(orderSortResult.Success, orderSortResult.Message);
        Assert.NotNull(orderSortResult.Data);
        var orderSortItem = Assert.Single(orderSortResult.Data!.Items);
        Assert.Equal("P-COMPUTED-HIGH", orderSortItem.ProductCode);
        Assert.Equal(1m, orderSortItem.ImportAmount);
        Assert.Equal(2m, orderSortItem.AllocatedImportAmount);
    }

    [Fact]
    public void GetOrderDetailAsync_ImportAmountSqlServerExpressionsCanBeTranslated()
    {
        using var sqlServerDb = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = "Server=127.0.0.1;Database=HB;User Id=test;Password=test;TrustServerCertificate=True;",
            DbType = DbType.SqlServer,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        });

        var detailQuery = sqlServerDb.Queryable<WareHouseOrderDetails>()
            .LeftJoin<Product>((d, p) => d.ProductCode == p.ProductCode)
            .LeftJoin<WarehouseProduct>((d, p, wp) => d.ProductCode == wp.ProductCode)
            .LeftJoin<DomesticProduct>((d, p, wp, dp) => wp.ProductCode == dp.ProductCode)
            .Where(d => d.OrderGUID == "ORDER-SQLSERVER" && !d.IsDeleted);

        var orderImportSortSql = detailQuery
            .OrderBy(
                (d, p, wp, dp) =>
                    d.ImportAmount
                    ?? ((d.ImportPrice ?? (wp.ImportPrice ?? 0)) * (d.Quantity ?? 0)),
                OrderByType.Asc
            )
            .ToSql()
            .Key;
        var allocatedImportSortSql = detailQuery
            .OrderBy(
                (d, p, wp, dp) => (d.ImportPrice ?? (wp.ImportPrice ?? 0)) * (d.AllocQuantity ?? 0),
                OrderByType.Asc
            )
            .ToSql()
            .Key;
        var summarySql = detailQuery
            .Select(
                (d, p, wp, dp) =>
                    new
                    {
                        TotalImportAmount = SqlFunc.AggregateSum(
                            d.ImportAmount
                                ?? ((d.ImportPrice ?? (wp.ImportPrice ?? 0)) * (d.Quantity ?? 0))
                        ),
                        TotalAllocatedImportAmount = SqlFunc.AggregateSum(
                            (d.ImportPrice ?? (wp.ImportPrice ?? 0)) * (d.AllocQuantity ?? 0)
                        ),
                    }
            )
            .ToSql()
            .Key;

        Assert.Contains("ORDER BY", orderImportSortSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ImportAmount", orderImportSortSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Quantity", orderImportSortSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AllocQuantity", allocatedImportSortSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SUM", summarySql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ImportAmount", summarySql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Quantity", summarySql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AllocQuantity", summarySql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetOrderListAsync_ReturnsOutboundDate()
    {
        var outboundDate = new DateTime(2026, 6, 7);
        await SeedStoreOrderAsync("ORDER-OUT-LIST", outboundDate: outboundDate);
        await SeedOrderLineAsync("ORDER-OUT-LIST", "P001", "ITEM-001", quantity: 3m, allocQuantity: 2m);

        var result = await CreateService().GetOrderListAsync(new StoreOrderListFilterDto
        {
            PageNumber = 1,
            PageSize = 20,
            StatusList = new List<int> { 1 },
        });

        var item = Assert.Single(result.Items);
        Assert.Equal(outboundDate, item.OutboundDate);
    }

    [Fact]
    public async Task GetOrderListAsync_CreatedAtDescendingSortsBeforePaging()
    {
        for (var index = 1; index <= 25; index++)
        {
            await SeedStoreOrderAsync(
                $"ORDER-CREATED-{index:D3}",
                flowStatus: 1,
                insertStore: index == 1,
                createdAt: new DateTime(2026, 6, 1).AddMinutes(index)
            );
        }

        var result = await CreateService().GetOrderListAsync(new StoreOrderListFilterDto
        {
            PageNumber = 1,
            PageSize = 20,
            StatusList = new List<int> { 1 },
            SortBy = "createdAt",
            SortDescending = true,
        });

        Assert.Equal(25, result.Total);
        Assert.Equal(20, result.Items.Count);
        Assert.Equal(
            Enumerable.Range(6, 20).Reverse().Select(index => $"ORDER-CREATED-{index:D3}"),
            result.Items.Select(item => item.OrderGUID)
        );
    }

    [Fact]
    public async Task GetOrderListAsync_FiltersByDetailItemNumber()
    {
        await SeedStoreOrderAsync("ORDER-MATCH", flowStatus: 1, insertStore: true);
        await SeedOrderLineAsync("ORDER-MATCH", "P-MATCH", "HB-ITEM-889", quantity: 3m, allocQuantity: 2m);
        await SeedStoreOrderAsync("ORDER-OTHER", flowStatus: 1, insertStore: false);
        await SeedOrderLineAsync("ORDER-OTHER", "P-OTHER", "HB-ITEM-100", quantity: 3m, allocQuantity: 2m);

        var result = await CreateService().GetOrderListAsync(new StoreOrderListFilterDto
        {
            Keyword = "ITEM-889",
            PageNumber = 1,
            PageSize = 20,
            StatusList = new List<int> { 1 },
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("ORDER-MATCH", item.OrderGUID);
    }

    [Fact]
    public async Task GetOrderListAsync_DoesNotDuplicateOrdersWhenMultipleDetailsMatchItemNumber()
    {
        await SeedStoreOrderAsync("ORDER-DUP", flowStatus: 1, insertStore: true);
        await SeedOrderLineAsync("ORDER-DUP", "P-DUP-1", "DUP-ITEM", quantity: 1m, allocQuantity: 1m);
        await SeedOrderLineAsync("ORDER-DUP", "P-DUP-2", "DUP-ITEM", quantity: 2m, allocQuantity: 2m);

        var result = await CreateService().GetOrderListAsync(new StoreOrderListFilterDto
        {
            Keyword = "DUP-ITEM",
            PageNumber = 1,
            PageSize = 20,
            StatusList = new List<int> { 1 },
        });

        Assert.Equal(1, result.Total);
        Assert.Equal("ORDER-DUP", Assert.Single(result.Items).OrderGUID);
    }

    [Fact]
    public async Task GetOrderListAsync_IgnoresDeletedDetailAndDeletedProductWhenFilteringByItemNumber()
    {
        await SeedStoreOrderAsync("ORDER-DELETED-DETAIL", flowStatus: 1, insertStore: true);
        await SeedOrderLineAsync(
            "ORDER-DELETED-DETAIL",
            "P-DELETED-DETAIL",
            "REMOVED-ITEM",
            quantity: 1m,
            allocQuantity: 1m,
            isDeleted: true
        );
        await SeedStoreOrderAsync("ORDER-DELETED-PRODUCT", flowStatus: 1, insertStore: false);
        await SeedOrderLineAsync(
            "ORDER-DELETED-PRODUCT",
            "P-DELETED-PRODUCT",
            "REMOVED-ITEM",
            quantity: 1m,
            allocQuantity: 1m,
            productIsDeleted: true
        );

        var result = await CreateService().GetOrderListAsync(new StoreOrderListFilterDto
        {
            Keyword = "REMOVED-ITEM",
            PageNumber = 1,
            PageSize = 20,
            StatusList = new List<int> { 1 },
        });

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetUsedBranchesAsync_SortsByStoreNameThenCode()
    {
        await SeedStoreAsync("store-z", "S010", "Zulu");
        await SeedStoreAsync("store-a2", "S002", "Alpha");
        await SeedStoreAsync("store-a1", "S001", "Alpha");
        await SeedStoreOrderAsync("ORDER-Z", storeCode: "S010", insertStore: false);
        await SeedStoreOrderAsync("ORDER-A2", storeCode: "S002", insertStore: false);
        await SeedStoreOrderAsync("ORDER-A1", storeCode: "S001", insertStore: false);

        var result = await CreateService().GetUsedBranchesAsync();

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.Equal(new[] { "S001", "S002", "S010" }, result.Data.Select(item => item.Code));
    }

    [Fact]
    public async Task GetUnmatchedStoreOrderGroupsAsync_GroupsOnlyOrdersWithoutLocalStoreMatch()
    {
        const string hqGuid = "11111111-1111-1111-1111-111111111111";
        await SeedStoreAsync("local-store-guid", "S001", "本地一店");
        await SeedStoreAsync("local-store-guid-2", "S002", "本地二店");
        await SeedStoreOrderAsync("ORDER-CODE", storeCode: "S001", insertStore: false);
        await SeedStoreOrderAsync("ORDER-GUID", storeCode: "local-store-guid-2", insertStore: false);
        await SeedStoreOrderAsync("ORDER-HQ-1", storeCode: hqGuid, insertStore: false, orderDate: new DateTime(2026, 6, 10));
        await SeedStoreOrderAsync("ORDER-HQ-2", storeCode: hqGuid, insertStore: false, orderDate: new DateTime(2026, 6, 12));
        await SeedStoreOrderAsync("ORDER-UNKNOWN", storeCode: "UNKNOWN-GUID", insertStore: false, orderDate: new DateTime(2026, 6, 11));
        await SeedExternalCustomerAsync(hqGuid, "Ada - Tas - Kingston");

        var result = await CreateService().GetUnmatchedStoreOrderGroupsAsync();

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.Equal(new[] { hqGuid, "UNKNOWN-GUID" }, result.Data.Select(item => item.SourceStoreCode));
        var hqGroup = Assert.Single(result.Data, item => item.SourceStoreCode == hqGuid);
        Assert.Equal("Ada - Tas - Kingston", hqGroup.SourceStoreName);
        Assert.Equal(2, hqGroup.OrderCount);
        Assert.Equal(new DateTime(2026, 6, 12), hqGroup.LatestOrderDate);
    }

    [Fact]
    public async Task BatchMapStoreOrderStoreCodeAsync_UpdatesOnlyUnmatchedOrderHeaders()
    {
        const string hqGuid = "22222222-2222-2222-2222-222222222222";
        await SeedStoreAsync("target-store-guid", "S100", "目标分店");
        await SeedStoreAsync("matched-store-guid", "S200", "已匹配分店");
        await SeedStoreOrderAsync("ORDER-HQ-1", storeCode: hqGuid, insertStore: false);
        await SeedStoreOrderAsync("ORDER-HQ-2", storeCode: hqGuid, insertStore: false);
        await SeedStoreOrderAsync("ORDER-MATCHED", storeCode: "S200", insertStore: false);
        await SeedDeletedOrderDetailAsync("ORDER-HQ-1", "P-DETAIL", storeCode: hqGuid, isDeleted: false);

        var result = await CreateService().BatchMapStoreOrderStoreCodeAsync(
            new BatchMapStoreOrderStoreCodeDto
            {
                Mappings = new List<StoreOrderStoreCodeMappingDto>
                {
                    new() { SourceStoreCode = hqGuid, TargetStoreCode = "S100" },
                    new() { SourceStoreCode = "S200", TargetStoreCode = "S100" },
                },
            }
        );

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.UpdatedCount);
        Assert.Equal(1, result.Data.SkippedCount);

        var fixedOrders = await _db.Queryable<WareHouseOrder>()
            .Where(item => new[] { "ORDER-HQ-1", "ORDER-HQ-2" }.Contains(item.OrderGUID))
            .ToListAsync();
        Assert.All(fixedOrders, item => Assert.Equal("S100", item.StoreCode));

        var matchedOrder = await _db.Queryable<WareHouseOrder>()
            .Where(item => item.OrderGUID == "ORDER-MATCHED")
            .FirstAsync();
        Assert.Equal("S200", matchedOrder.StoreCode);

        var untouchedDetail = await _db.Queryable<WareHouseOrderDetails>()
            .Where(item => item.OrderGUID == "ORDER-HQ-1")
            .FirstAsync();
        Assert.Equal(hqGuid, untouchedDetail.StoreCode);
    }

    [Fact]
    public async Task BatchMapStoreOrderStoreCodeAsync_FailsWhenTargetStoreMissing()
    {
        const string hqGuid = "33333333-3333-3333-3333-333333333333";
        await SeedStoreOrderAsync("ORDER-HQ", storeCode: hqGuid, insertStore: false);

        var result = await CreateService().BatchMapStoreOrderStoreCodeAsync(
            new BatchMapStoreOrderStoreCodeDto
            {
                Mappings = new List<StoreOrderStoreCodeMappingDto>
                {
                    new() { SourceStoreCode = hqGuid, TargetStoreCode = "MISSING" },
                },
            }
        );

        Assert.False(result.Success);
        Assert.Contains("目标分店不存在", result.Message);

        var order = await _db.Queryable<WareHouseOrder>()
            .Where(item => item.OrderGUID == "ORDER-HQ")
            .FirstAsync();
        Assert.Equal(hqGuid, order.StoreCode);
    }

    [Fact]
    public async Task BatchMapStoreOrderStoreCodeAsync_AllowsInactiveTargetStoreForHistoricalRepair()
    {
        const string hqGuid = "44444444-4444-4444-4444-444444444444";
        await SeedStoreAsync("inactive-target-store-guid", "S300", "停用目标分店", isActive: false);
        await SeedStoreOrderAsync("ORDER-INACTIVE-TARGET", storeCode: hqGuid, insertStore: false);

        var result = await CreateService().BatchMapStoreOrderStoreCodeAsync(
            new BatchMapStoreOrderStoreCodeDto
            {
                Mappings = new List<StoreOrderStoreCodeMappingDto>
                {
                    new() { SourceStoreCode = hqGuid, TargetStoreCode = "S300" },
                },
            }
        );

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.Equal(1, result.Data.UpdatedCount);
        Assert.Equal(0, result.Data.SkippedCount);

        var order = await _db.Queryable<WareHouseOrder>()
            .Where(item => item.OrderGUID == "ORDER-INACTIVE-TARGET")
            .FirstAsync();
        Assert.Equal("S300", order.StoreCode);
    }

    [Theory]
    [InlineData("totalOrderAmount", true, "ORDER-HIGH", "ORDER-MID")]
    [InlineData("totalOrderAmount", false, "ORDER-LOW", "ORDER-MID")]
    [InlineData("totalQuantity", true, "ORDER-HIGH", "ORDER-MID")]
    [InlineData("totalQuantity", false, "ORDER-LOW", "ORDER-MID")]
    [InlineData("totalAllocQuantity", true, "ORDER-HIGH", "ORDER-MID")]
    [InlineData("totalAllocQuantity", false, "ORDER-LOW", "ORDER-MID")]
    public async Task GetOrderListAsync_SortsAggregateFieldsWithoutRawSql(
        string sortBy,
        bool sortDescending,
        string firstOrderGuid,
        string secondOrderGuid
    )
    {
        await SeedStoreOrderAsync("ORDER-LOW", flowStatus: 1, insertStore: true);
        await SeedOrderLineAsync("ORDER-LOW", "P-LOW", "ITEM-LOW", quantity: 1m, allocQuantity: 1m);
        await SeedStoreOrderAsync("ORDER-MID", flowStatus: 1, insertStore: false);
        await SeedOrderLineAsync("ORDER-MID", "P-MID", "ITEM-MID", quantity: 3m, allocQuantity: 2m);
        await SeedStoreOrderAsync("ORDER-HIGH", flowStatus: 1, insertStore: false);
        await SeedOrderLineAsync("ORDER-HIGH", "P-HIGH", "ITEM-HIGH", quantity: 5m, allocQuantity: 4m);

        _sqlLogs.Clear();
        var result = await CreateService().GetOrderListAsync(new StoreOrderListFilterDto
        {
            PageNumber = 1,
            PageSize = 2,
            StatusList = new List<int> { 1 },
            SortBy = sortBy,
            SortDescending = sortDescending,
        });

        Assert.Equal(3, result.Total);
        Assert.Collection(
            result.Items,
            first => Assert.Equal(firstOrderGuid, first.OrderGUID),
            second => Assert.Equal(secondOrderGuid, second.OrderGUID)
        );
        Assert.DoesNotContain(
            _sqlLogs,
            log => log.Contains("WareHouseOrder.OrderGUID", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task GetOrderListAsync_AggregateSortIgnoresDeletedDetails()
    {
        await SeedStoreOrderAsync("ORDER-DELETED-TOTAL", flowStatus: 1, insertStore: true);
        await SeedOrderLineAsync(
            "ORDER-DELETED-TOTAL",
            "P-DELETED-TOTAL",
            "ITEM-DELETED-TOTAL",
            quantity: 100m,
            allocQuantity: 100m,
            isDeleted: true
        );
        await SeedOrderLineAsync(
            "ORDER-DELETED-TOTAL",
            "P-DELETED-ACTIVE",
            "ITEM-DELETED-ACTIVE",
            quantity: 1m,
            allocQuantity: 1m
        );
        await SeedStoreOrderAsync("ORDER-ACTIVE-TOTAL", flowStatus: 1, insertStore: false);
        await SeedOrderLineAsync(
            "ORDER-ACTIVE-TOTAL",
            "P-ACTIVE-TOTAL",
            "ITEM-ACTIVE-TOTAL",
            quantity: 3m,
            allocQuantity: 3m
        );

        var result = await CreateService().GetOrderListAsync(new StoreOrderListFilterDto
        {
            PageNumber = 1,
            PageSize = 2,
            StatusList = new List<int> { 1 },
            SortBy = "importTotalAmount",
            SortDescending = true,
        });

        Assert.Collection(
            result.Items,
            first =>
            {
                Assert.Equal("ORDER-ACTIVE-TOTAL", first.OrderGUID);
                Assert.Equal(3, first.TotalQuantity);
            },
            second =>
            {
                Assert.Equal("ORDER-DELETED-TOTAL", second.OrderGUID);
                Assert.Equal(1, second.TotalQuantity);
            }
        );
    }

    [Fact]
    public async Task GetOrderListAsync_ChunksAggregateDetailLookupForLargeResultSets()
    {
        await SeedStoreAsync("STORE-GUID-001", "S001", "测试门店");
        await SeedStoreOrdersForAggregateSortAsync(1201);

        _sqlLogs.Clear();
        var result = await CreateService().GetOrderListAsync(new StoreOrderListFilterDto
        {
            PageNumber = 1,
            PageSize = 1,
            StatusList = new List<int> { 1 },
            SortBy = "totalQuantity",
            SortDescending = true,
        });

        Assert.Equal(1201, result.Total);
        Assert.Equal("ORDER-1200", Assert.Single(result.Items).OrderGUID);
        Assert.True(
            _sqlLogs.Count(log =>
                log.Contains("FROM `WareHouseOrderDetails`", StringComparison.OrdinalIgnoreCase)
            ) >= 4
        );
    }

    [Fact]
    public async Task GetOrderListAsync_FiltersMainColumnFields()
    {
        var outboundDate = new DateTime(2026, 6, 10);
        var createdAt = new DateTime(2026, 6, 1, 8, 0, 0);
        var updatedAt = new DateTime(2026, 6, 12, 9, 30, 0);
        await SeedStoreOrderAsync(
            "ORDER-COLUMN-MATCH",
            flowStatus: 1,
            outboundDate: outboundDate,
            insertStore: true,
            remarks: "Fragile gift bags",
            createdAt: createdAt,
            updatedBy: "stephanie",
            updatedAt: updatedAt
        );
        await SeedOrderLineAsync(
            "ORDER-COLUMN-MATCH",
            "P-COLUMN-MATCH",
            "ITEM-COLUMN-MATCH",
            quantity: 3m,
            allocQuantity: 2m
        );
        await SeedStoreOrderAsync(
            "ORDER-COLUMN-OTHER",
            flowStatus: 1,
            outboundDate: outboundDate.AddDays(5),
            insertStore: false,
            remarks: "Other note",
            createdAt: createdAt.AddDays(-10),
            updatedBy: "dong",
            updatedAt: updatedAt.AddDays(5)
        );
        await SeedOrderLineAsync(
            "ORDER-COLUMN-OTHER",
            "P-COLUMN-OTHER",
            "ITEM-COLUMN-OTHER",
            quantity: 3m,
            allocQuantity: 2m
        );

        var result = await CreateService().GetOrderListAsync(new StoreOrderListFilterDto
        {
            PageNumber = 1,
            PageSize = 20,
            StatusList = new List<int> { 1 },
            ColumnFilters = new StoreOrderListColumnFilterDto
            {
                OrderNo = "MATCH",
                OutboundDateStart = outboundDate,
                OutboundDateEnd = outboundDate,
                Remarks = "gift",
                CreatedAtStart = createdAt.Date,
                CreatedAtEnd = createdAt.Date,
                UpdatedBy = "steph",
                UpdatedAtStart = updatedAt.Date,
                UpdatedAtEnd = updatedAt.Date,
            },
        });

        var item = Assert.Single(result.Items);
        Assert.Equal("ORDER-COLUMN-MATCH", item.OrderGUID);
        Assert.Equal(1, result.Total);
    }

    [Fact]
    public async Task GetOrderListAsync_FiltersAggregateAndVolumeColumnsBeforePaging()
    {
        await SeedStoreOrderAsync("ORDER-LOW-FILTER", flowStatus: 1, insertStore: true);
        await SeedOrderLineAsync(
            "ORDER-LOW-FILTER",
            "P-LOW-FILTER",
            "ITEM-LOW-FILTER",
            quantity: 1m,
            allocQuantity: 1m
        );
        await SeedStoreOrderAsync("ORDER-MID-FILTER", flowStatus: 1, insertStore: false);
        await SeedOrderLineAsync(
            "ORDER-MID-FILTER",
            "P-MID-FILTER",
            "ITEM-MID-FILTER",
            quantity: 3m,
            allocQuantity: 2m
        );
        await SeedStoreOrderAsync("ORDER-HIGH-FILTER", flowStatus: 1, insertStore: false);
        await SeedOrderLineAsync(
            "ORDER-HIGH-FILTER",
            "P-HIGH-FILTER",
            "ITEM-HIGH-FILTER",
            quantity: 5m,
            allocQuantity: 4m
        );

        var result = await CreateService().GetOrderListAsync(new StoreOrderListFilterDto
        {
            PageNumber = 1,
            PageSize = 1,
            StatusList = new List<int> { 1 },
            SortBy = "totalQuantity",
            SortDescending = true,
            ColumnFilters = new StoreOrderListColumnFilterDto
            {
                TotalQuantityMin = 3m,
                TotalQuantityMax = 5m,
                TotalOrderAmountMin = 6m,
                TotalOrderAmountMax = 10m,
                TotalOrderVolumeMin = 3m,
                TotalOrderVolumeMax = 5m,
                TotalAllocVolumeMin = 2m,
                TotalAllocVolumeMax = 4m,
                TotalAllocQuantityMin = 2m,
                TotalAllocQuantityMax = 4m,
                ImportTotalAmountMin = 4m,
                ImportTotalAmountMax = 8m,
            },
        });

        Assert.Equal(2, result.Total);
        Assert.Equal("ORDER-HIGH-FILTER", Assert.Single(result.Items).OrderGUID);
    }

    [Fact]
    public async Task GetOrderDetailAsync_ReturnsOutboundDate()
    {
        var outboundDate = new DateTime(2026, 6, 8);
        await SeedStoreOrderAsync("ORDER-OUT-DETAIL", outboundDate: outboundDate);
        await SeedOrderLineAsync("ORDER-OUT-DETAIL", "P001", "ITEM-001", quantity: 3m, allocQuantity: 2m);

        var result = await CreateService().GetOrderDetailAsync(
            "ORDER-OUT-DETAIL",
            new StoreOrderDetailQueryDto { PageNumber = 1, PageSize = 20 }
        );

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(outboundDate, result.Data.OutboundDate);
    }

    [Fact]
    public async Task UpdateOrderOutboundDateAsync_CanCompletePickingOrderAndOnlyUpdatesOutboundFields()
    {
        var originalOrderDate = new DateTime(2026, 6, 1);
        var outboundDate = new DateTime(2026, 6, 9);
        await SeedStoreOrderAsync(
            "ORDER-OUT-UPDATE",
            flowStatus: 3,
            orderDate: originalOrderDate,
            outboundDate: new DateTime(2026, 6, 2)
        );

        var result = await CreateService().UpdateOrderOutboundDateAsync(new UpdateOrderOutboundDateDto
        {
            OrderGuid = "ORDER-OUT-UPDATE",
            OutboundDate = outboundDate,
            CompleteOrder = true,
        });

        Assert.True(result.Success, result.Message);
        var order = await _db.Queryable<WareHouseOrder>()
            .Where(item => item.OrderGUID == "ORDER-OUT-UPDATE")
            .FirstAsync();
        Assert.Equal(outboundDate, order.OutboundDate);
        Assert.Equal(2, order.FlowStatus);
        Assert.Equal(originalOrderDate, order.OrderDate);
    }

    [Fact]
    public async Task UpdateOrderOutboundDateAsync_RejectsCompleteOrderWhenStatusIsNotSubmittedOrPicking()
    {
        var originalOutboundDate = new DateTime(2026, 6, 2);
        await SeedStoreOrderAsync(
            "ORDER-OUT-INVALID-COMPLETE",
            flowStatus: 0,
            outboundDate: originalOutboundDate
        );

        var result = await CreateService().UpdateOrderOutboundDateAsync(new UpdateOrderOutboundDateDto
        {
            OrderGuid = "ORDER-OUT-INVALID-COMPLETE",
            OutboundDate = new DateTime(2026, 6, 10),
            CompleteOrder = true,
        });

        Assert.False(result.Success);
        Assert.Equal("只有已提交或配货中状态的订单才能标记为完成", result.Message);
        var order = await _db.Queryable<WareHouseOrder>()
            .Where(item => item.OrderGUID == "ORDER-OUT-INVALID-COMPLETE")
            .FirstAsync();
        Assert.Equal(originalOutboundDate, order.OutboundDate);
        Assert.Equal(0, order.FlowStatus);
    }

    [Fact]
    public async Task UpdateOrderOutboundDateAsync_ReturnsFailureWhenOrderDoesNotExist()
    {
        var result = await CreateService().UpdateOrderOutboundDateAsync(new UpdateOrderOutboundDateDto
        {
            OrderGuid = "ORDER-MISSING",
            OutboundDate = new DateTime(2026, 6, 10),
            CompleteOrder = true,
        });

        Assert.False(result.Success);
        Assert.Equal("Order not found", result.Message);
    }

    [Fact]
    public async Task RefreshOrderLineImportPricesAsync_已完成订单按仓库表刷新整单并重算汇总()
    {
        await SeedStoreOrderAsync("ORDER-REFRESH-ALL", flowStatus: 2);
        await SeedOrderLineAsync("ORDER-REFRESH-ALL", "P-REFRESH-1", "ITEM-REFRESH-1", quantity: 3m, allocQuantity: 4m);
        await SeedOrderLineAsync("ORDER-REFRESH-ALL", "P-REFRESH-2", "ITEM-REFRESH-2", quantity: 5m, allocQuantity: 6m);
        await SeedOrderLineAsync("ORDER-REFRESH-ALL", "P-REFRESH-3", "ITEM-REFRESH-3", quantity: 7m, allocQuantity: 8m);
        await _db.Updateable<WareHouseOrderDetails>()
            .SetColumns(item => new WareHouseOrderDetails { ImportAmount = 1m })
            .Where(item => item.DetailGUID == "ORDER-REFRESH-ALL-P-REFRESH-2")
            .ExecuteCommandAsync();
        await _db.Updateable<WarehouseProduct>()
            .SetColumns(item => new WarehouseProduct { ImportPrice = 5m })
            .Where(item => item.ProductCode == "P-REFRESH-1")
            .ExecuteCommandAsync();
        await _db.Updateable<WarehouseProduct>()
            .SetColumns(item => new WarehouseProduct { ImportPrice = 0m })
            .Where(item => item.ProductCode == "P-REFRESH-3")
            .ExecuteCommandAsync();

        var result = await CreateService().RefreshOrderLineImportPricesAsync(
            new RefreshStoreOrderImportPricesDto { OrderGUID = "ORDER-REFRESH-ALL" }
        );

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data!.UpdatedCount);
        Assert.Equal(0, result.Data.UnchangedCount);
        Assert.Equal(1, result.Data.SkippedCount);
        Assert.Equal(1, result.Data.MissingWarehousePriceCount);

        var refreshed = await _db.Queryable<WareHouseOrderDetails>()
            .Where(item => item.DetailGUID == "ORDER-REFRESH-ALL-P-REFRESH-1")
            .FirstAsync();
        var skipped = await _db.Queryable<WareHouseOrderDetails>()
            .Where(item => item.DetailGUID == "ORDER-REFRESH-ALL-P-REFRESH-3")
            .FirstAsync();
        var correctedAmount = await _db.Queryable<WareHouseOrderDetails>()
            .Where(item => item.DetailGUID == "ORDER-REFRESH-ALL-P-REFRESH-2")
            .FirstAsync();
        var order = await _db.Queryable<WareHouseOrder>()
            .Where(item => item.OrderGUID == "ORDER-REFRESH-ALL")
            .FirstAsync();

        Assert.Equal(5m, refreshed.ImportPrice);
        Assert.Equal(15m, refreshed.ImportAmount);
        Assert.Equal(2m, correctedAmount.ImportPrice);
        Assert.Equal(10m, correctedAmount.ImportAmount);
        Assert.Equal(2m, skipped.ImportPrice);
        Assert.Equal(39m, order.ImportTotalAmount);
    }

    [Fact]
    public async Task RefreshOrderLineImportPricesAsync_传入明细时只刷新选中行()
    {
        await SeedStoreOrderAsync("ORDER-REFRESH-SELECTED");
        await SeedOrderLineAsync("ORDER-REFRESH-SELECTED", "P-REFRESH-A", "ITEM-REFRESH-A", quantity: 1m, allocQuantity: 2m);
        await SeedOrderLineAsync("ORDER-REFRESH-SELECTED", "P-REFRESH-B", "ITEM-REFRESH-B", quantity: 3m, allocQuantity: 4m);
        await _db.Updateable<WarehouseProduct>()
            .SetColumns(item => new WarehouseProduct { ImportPrice = 5m })
            .Where(item => item.ProductCode == "P-REFRESH-A" || item.ProductCode == "P-REFRESH-B")
            .ExecuteCommandAsync();

        var result = await CreateService().RefreshOrderLineImportPricesAsync(
            new RefreshStoreOrderImportPricesDto
            {
                OrderGUID = "ORDER-REFRESH-SELECTED",
                DetailGUIDs = new List<string> { "ORDER-REFRESH-SELECTED-P-REFRESH-B" },
            }
        );

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, result.Data?.UpdatedCount);

        var untouched = await _db.Queryable<WareHouseOrderDetails>()
            .Where(item => item.DetailGUID == "ORDER-REFRESH-SELECTED-P-REFRESH-A")
            .FirstAsync();
        var refreshed = await _db.Queryable<WareHouseOrderDetails>()
            .Where(item => item.DetailGUID == "ORDER-REFRESH-SELECTED-P-REFRESH-B")
            .FirstAsync();

        Assert.Equal(2m, untouched.ImportPrice);
        Assert.Equal(5m, refreshed.ImportPrice);
        Assert.Equal(15m, refreshed.ImportAmount);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private async Task SeedProductWithGradesAsync(
        string productCode,
        string itemNumber,
        params string[] grades
    )
    {
        await SeedProductAsync(productCode, itemNumber);
        await SeedWarehouseProductAsync(productCode);

        foreach (var grade in grades)
        {
            await _db.Insertable(new ProductGrade
            {
                Id = $"{productCode}-{grade}",
                ProductCode = productCode,
                Grade = grade,
                IsDeleted = false,
            }).ExecuteCommandAsync();
        }
    }

    private async Task SeedProductAsync(
        string productCode,
        string itemNumber,
        string? barcode = null,
        string? localSupplierCode = null,
        decimal? purchasePrice = null,
        bool isActive = true,
        bool isDeleted = false,
        string? productName = null,
        string? warehouseCategoryGuid = null
    )
    {
        await _db.Insertable(new Product
        {
            UUID = $"{productCode}-uuid",
            ProductCode = productCode,
            ProductName = productName ?? $"商品 {productCode}",
            ItemNumber = itemNumber,
            Barcode = barcode ?? $"{itemNumber}-BAR",
            LocalSupplierCode = localSupplierCode,
            PurchasePrice = purchasePrice,
            IsActive = isActive,
            IsDeleted = isDeleted,
            WarehouseCategoryGUID = warehouseCategoryGuid,
        }).ExecuteCommandAsync();
    }

    private async Task SeedWarehouseCategoryAsync(
        string categoryGuid,
        string categoryName,
        string? parentGuid = null
    )
    {
        await _db.Insertable(new WarehouseCategory
        {
            CategoryGUID = categoryGuid,
            CategoryName = categoryName,
            ParentGUID = parentGuid,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedProductGradeAsync(string productCode, string grade)
    {
        await _db.Insertable(new ProductGrade
        {
            Id = $"{productCode}-{grade}",
            ProductCode = productCode,
            Grade = grade,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedWarehouseProductAsync(
        string productCode,
        bool isDeleted = false,
        decimal oemPrice = 10m,
        decimal importPrice = 7m,
        bool isActive = true,
        int stockQuantity = 20,
        int minOrderQuantity = 1
    )
    {
        await _db.Insertable(new WarehouseProduct
        {
            ProductCode = productCode,
            OEMPrice = oemPrice,
            ImportPrice = importPrice,
            StockQuantity = stockQuantity,
            MinOrderQuantity = minOrderQuantity,
            IsActive = isActive,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreOrderAsync(
        string orderGuid,
        int flowStatus = 1,
        DateTime? orderDate = null,
        DateTime? outboundDate = null,
        bool insertStore = true,
        string storeCode = "S001",
        string? remarks = null,
        DateTime? createdAt = null,
        string? updatedBy = null,
        DateTime? updatedAt = null,
        string? cartOwnerUserGuid = null
    )
    {
        if (insertStore)
        {
            await SeedStoreAsync("STORE-GUID-001", storeCode, "测试门店");
        }

        await _db.Insertable(new WareHouseOrder
        {
            OrderGUID = orderGuid,
            StoreCode = storeCode,
            OrderNo = $"{orderGuid}-NO",
            OrderDate = orderDate ?? new DateTime(2026, 6, 1),
            OutboundDate = outboundDate,
            FlowStatus = flowStatus,
            Remarks = remarks,
            CreatedAt = createdAt ?? new DateTime(2026, 6, 1),
            UpdatedBy = updatedBy,
            UpdatedAt = updatedAt,
            CartOwnerUserGuid = cartOwnerUserGuid,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreAsync(string storeGuid, string storeCode, string storeName, bool isActive = true)
    {
        await _db.Insertable(new Store
        {
            StoreGUID = storeGuid,
            StoreCode = storeCode,
            StoreName = storeName,
            Address = "测试地址",
            IsActive = isActive,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedStoreOrdersForAggregateSortAsync(int count)
    {
        var orders = Enumerable.Range(0, count)
            .Select(index => new WareHouseOrder
            {
                OrderGUID = $"ORDER-{index:0000}",
                StoreCode = "S001",
                OrderNo = $"ORDER-{index:0000}-NO",
                OrderDate = new DateTime(2026, 6, 1),
                FlowStatus = 1,
                IsDeleted = false,
            })
            .ToList();

        var details = Enumerable.Range(0, count)
            .Select(index => new WareHouseOrderDetails
            {
                DetailGUID = $"ORDER-{index:0000}-P{index:0000}",
                OrderGUID = $"ORDER-{index:0000}",
                StoreCode = "S001",
                ProductCode = $"P{index:0000}",
                Quantity = index,
                AllocQuantity = index,
                ImportPrice = 2m,
                ImportAmount = index * 2m,
                OEMPrice = 3m,
                OEMAmount = index * 3m,
                IsDeleted = false,
            })
            .ToList();

        await _db.Insertable(orders).ExecuteCommandAsync();
        await _db.Insertable(details).ExecuteCommandAsync();
    }

    private async Task SeedOrderLineAsync(
        string orderGuid,
        string productCode,
        string itemNumber,
        decimal quantity,
        decimal allocQuantity,
        bool isActive = true,
        bool warehouseIsActive = true,
        bool isDeleted = false,
        bool productIsDeleted = false,
        string? productName = null,
        string? barcode = null,
        decimal importPrice = 2m
    )
    {
        await SeedProductAsync(
            productCode,
            itemNumber,
            barcode: barcode,
            isActive: isActive,
            isDeleted: productIsDeleted,
            productName: productName
        );
        await SeedWarehouseProductAsync(productCode, importPrice: importPrice, isActive: warehouseIsActive);
        await _db.Insertable(new DomesticProduct
        {
            ProductCode = productCode,
            UnitVolume = 1m,
            PackingQuantity = 1,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        await _db.Insertable(new WareHouseOrderDetails
        {
            DetailGUID = $"{orderGuid}-{productCode}",
            OrderGUID = orderGuid,
            StoreCode = "S001",
            ProductCode = productCode,
            Quantity = quantity,
            AllocQuantity = allocQuantity,
            ImportPrice = importPrice,
            ImportAmount = quantity * importPrice,
            OEMPrice = 3m,
            OEMAmount = quantity * 3m,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
    }

    private async Task SeedOrderDetailOnlyAsync(
        string orderGuid,
        string productCode,
        decimal quantity,
        decimal allocQuantity
    )
    {
        await _db.Insertable(new WareHouseOrderDetails
        {
            DetailGUID = $"{orderGuid}-{productCode}",
            OrderGUID = orderGuid,
            StoreCode = "S001",
            ProductCode = productCode,
            Quantity = quantity,
            AllocQuantity = allocQuantity,
            ImportPrice = 2m,
            ImportAmount = quantity * 2m,
            OEMPrice = 3m,
            OEMAmount = quantity * 3m,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedDomesticProductAsync(
        string productCode,
        decimal unitVolume,
        int packingQuantity,
        string? supplierCode = null,
        string? hbProductNo = null,
        string? barcode = null
    )
    {
        await _db.Insertable(new DomesticProduct
        {
            ProductCode = productCode,
            SupplierCode = supplierCode,
            HBProductNo = hbProductNo,
            Barcode = barcode,
            UnitVolume = unitVolume,
            PackingQuantity = packingQuantity,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedChinaSupplierAsync(
        string supplierCode,
        string supplierName,
        bool isDeleted = false,
        string? shopNumber = null,
        int status = 1
    )
    {
        await _db.Insertable(new ChinaSupplier
        {
            Guid = $"{supplierCode}-guid",
            SupplierCode = supplierCode,
            SupplierName = supplierName,
            ShopNumber = shopNumber,
            Status = status,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
    }

    private async Task SeedLocationAsync(string productCode, string locationGuid, string locationCode)
    {
        await _db.Insertable(new Location
        {
            LocationGuid = locationGuid,
            LocationCode = locationCode,
            LocationType = 1,
            Status = 1,
            IsDeleted = false,
        }).ExecuteCommandAsync();

        await _db.Insertable(new ProductLocation
        {
            Guid = $"{productCode}-{locationGuid}",
            ProductCode = productCode,
            LocationGuid = locationGuid,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private static string FormatSqlLog(SugarParameter[]? parameters, string sql)
    {
        if (parameters == null || parameters.Length == 0)
        {
            return sql;
        }

        var result = sql;
        foreach (var parameter in parameters.OrderByDescending(item => item.ParameterName.Length))
        {
            var value = parameter.Value?.ToString() ?? "NULL";
            result = result.Replace(parameter.ParameterName, value, StringComparison.Ordinal);
        }

        return result;
    }

    private static bool IsStandaloneProductPriceLookupSql(string sql)
    {
        // 扫码合并接口单命中时已拿到价格，不能再执行 AddToCartMutation 的独立价格查询。
        return sql.Contains("Product", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("WarehouseProduct", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("OEMPrice", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("ImportPrice", StringComparison.OrdinalIgnoreCase)
            && !sql.Contains("ProductName", StringComparison.OrdinalIgnoreCase)
            && !sql.Contains("WarehouseCategory", StringComparison.OrdinalIgnoreCase)
            && !sql.Contains("WareHouseOrderDetails", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCartSummaryAggregateSql(string sql)
    {
        // 一次加购后摘要聚合应由 UpdateOrderTotalAsync 产出，payload 阶段不再重复聚合。
        return sql.Contains("WareHouseOrderDetails", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("SUM", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("COUNT", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("TotalQuantity", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("TotalSku", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveStoreOrderReactServicePath([CallerFilePath] string testFilePath = "")
    {
        var testDirectory = Path.GetDirectoryName(testFilePath)!;
        return Path.GetFullPath(
            Path.Combine(
                testDirectory,
                "..",
                "BlazorApp.Api",
                "Services",
                "React",
                "StoreOrderReactService.cs"
            )
        );
    }

    private static string ExtractMethodBody(string source, string methodSignature)
    {
        var methodIndex = source.IndexOf(methodSignature, StringComparison.Ordinal);
        Assert.True(methodIndex >= 0, $"找不到方法 {methodSignature}");
        var bodyStart = source.IndexOf('{', methodIndex);
        Assert.True(bodyStart >= 0, $"找不到方法 {methodSignature} 的方法体");

        var depth = 0;
        for (var index = bodyStart; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[bodyStart..(index + 1)];
                }
            }
        }

        throw new InvalidOperationException($"方法 {methodSignature} 的方法体未闭合");
    }

    private static void AssertInOrder(string source, params string[] fragments)
    {
        var cursor = 0;
        foreach (var fragment in fragments)
        {
            var index = source.IndexOf(fragment, cursor, StringComparison.Ordinal);
            Assert.True(index >= 0, $"找不到顺序片段 {fragment}");
            cursor = index + fragment.Length;
        }
    }

    private async Task SeedLocalSupplierAsync(string code, string name)
    {
        await _db.Insertable(new HBLocalSupplier
        {
            Guid = $"{code}-guid",
            LocalSupplierCode = code,
            Name = name,
            Status = 1,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedDeletedOrderDetailAsync(
        string orderGuid,
        string productCode,
        bool isDeleted,
        string? storeCode = null
    )
    {
        await _db.Insertable(new WareHouseOrderDetails
        {
            DetailGUID = $"{orderGuid}-{productCode}",
            OrderGUID = orderGuid,
            ProductCode = productCode,
            StoreCode = storeCode,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
    }

    private async Task SeedExternalCustomerAsync(string hguid, string customerName)
    {
        await _db.Insertable(new CPT_DIC_外购客户信息表
        {
            HGUID = hguid,
            客户名称 = customerName,
            状态 = 1,
        }).ExecuteCommandAsync();
    }

    private StoreOrderReactService CreateService(
        string userGuid = "user-1",
        params string[] roleNames
    )
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        var dbField = typeof(SqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, _db);

        var httpContextAccessor = new HttpContextAccessor();
        if (roleNames.Length > 0)
        {
            var claims = new List<Claim>
            {
                new("userId", userGuid),
                new("userGuid", userGuid),
                new(ClaimTypes.NameIdentifier, userGuid),
                new(ClaimTypes.Name, userGuid),
            };
            claims.AddRange(roleNames.Select(roleName => new Claim(ClaimTypes.Role, roleName)));
            httpContextAccessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
            };
        }
        else
        {
            httpContextAccessor.HttpContext = null;
        }

        var orderNumberGenerator = new Mock<IOrderNumberGenerator>();
        orderNumberGenerator
            .Setup(item => item.GetNextOrderNoAsync())
            .ReturnsAsync($"ORD-{userGuid}");

        var service = new StoreOrderReactService(
            context,
            NullLogger<StoreOrderReactService>.Instance,
            httpContextAccessor,
            orderNumberGenerator.Object,
            new ConfigurationBuilder().Build(),
            Mock.Of<IMapper>(),
            Mock.Of<IInvoiceEmailService>()
        );

        var hqField = typeof(StoreOrderReactService).GetField(
            "_createHqConnection",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        hqField!.SetValue(service, () => _db);

        return service;
    }
}
