using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AutoMapper;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using BlazorApp.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class WarehouseProductChangeHistoryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;
    private readonly WarehouseProductChangeHistoryService _service;

    public WarehouseProductChangeHistoryServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = _connection.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        _db.CodeFirst.InitTables(
            typeof(Product),
            typeof(WarehouseProduct),
            typeof(DomesticProduct),
            typeof(ProductLocation),
            typeof(Location)
        );
        _db.Ado.ExecuteCommand(
            """
            CREATE TABLE WarehouseProductChangeHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EventGuid TEXT NOT NULL,
                ProductCode TEXT NOT NULL,
                Action TEXT NOT NULL,
                Source TEXT NOT NULL,
                SourceReference TEXT NULL,
                BatchGuid TEXT NULL,
                ActorUserGuid TEXT NULL,
                ActorName TEXT NOT NULL,
                ActorType TEXT NOT NULL,
                OccurredAtUtc TEXT NOT NULL,
                ChangesJson TEXT NOT NULL
            )
            """
        );
        _service = new WarehouseProductChangeHistoryService(
            CreateSqlSugarContext(_db),
            NullLogger<WarehouseProductChangeHistoryService>.Instance,
            Mock.Of<ICurrentUserService>()
        );
    }

    [Fact]
    public async Task CaptureSnapshotsAsync_批量读取商品主档和仓库字段()
    {
        await SeedProductAsync();

        var snapshots = await _service.CaptureSnapshotsAsync(["P01", "missing"]);

        var snapshot = Assert.Single(snapshots);
        Assert.Equal("P01", snapshot.Key);
        Assert.Equal(0.70m, snapshot.Value.ImportPrice);
        Assert.Equal(2.50m, snapshot.Value.RetailPrice);
        Assert.Equal("SUP01", snapshot.Value.DomesticSupplierCode);
        Assert.Equal("SUP-AU", snapshot.Value.LocalSupplierCode);
        Assert.Equal("商品一", snapshot.Value.ProductName);
        Assert.Equal("Mesh Bag", snapshot.Value.EnglishName);
        Assert.Equal("6926393337100", snapshot.Value.Barcode);
        Assert.Equal("CAT-01", snapshot.Value.WarehouseCategoryGuid);
        Assert.Equal(12, snapshot.Value.PackingQuantity);
        Assert.Equal(0.016m, snapshot.Value.Volume);
    }

    [Fact]
    public async Task CaptureSnapshotsAsync_忽略软删除并处理重复主档()
    {
        await SeedProductAsync();
        await _db.Insertable(
                new Product
                {
                    UUID = "UUID-P01-DELETED",
                    ProductCode = "P01",
                    ProductName = "已删除旧记录",
                    IsDeleted = true,
                    UpdatedAt = new DateTime(2026, 8, 13, 0, 0, 0, DateTimeKind.Utc),
                }
            )
            .ExecuteCommandAsync();

        var snapshots = await _service.CaptureSnapshotsAsync(["P01"]);

        Assert.Equal("商品一", snapshots["P01"].ProductName);
    }

    [Fact]
    public async Task CaptureSnapshotsAsync_仅剩软删除行时仍可形成停用后的审计快照()
    {
        await SeedProductAsync();
        var before = await _service.CaptureSnapshotsAsync(["P01"]);
        await _db.Updateable<Product>()
            .SetColumns(item => new Product { IsDeleted = true, IsActive = false })
            .Where(item => item.ProductCode == "P01")
            .ExecuteCommandAsync();

        var after = await _service.CaptureSnapshotsAsync(["P01"]);

        Assert.True(after.ContainsKey("P01"));
        Assert.False(after["P01"].ProductSource!.IsActive);
        Assert.Equal(
            1,
            await _service.RecordChangesAsync(
                before,
                after,
                new WarehouseProductChangeHistoryContextDto
                {
                    Source = "ProductHqSync.Full",
                    ActorName = "System",
                    ActorType = "System",
                }
            )
        );
        var changes = await ReadSingleHistoryChangesAsync();
        Assert.Equal(("true", "false"), changes["isActive"]);
        Assert.Single(changes);
    }

    [Fact]
    public async Task CaptureSnapshotsAsync_超过数据库参数上限时分块读取()
    {
        var products = Enumerable.Range(0, 1005)
            .Select(index => new Product
            {
                UUID = $"UUID-BATCH-{index}",
                ProductCode = $"P-BATCH-{index:D4}",
                ProductName = $"批量商品 {index}",
                IsActive = true,
                IsDeleted = false,
            })
            .ToList();
        await _db.Insertable(products).PageSize(500).ExecuteCommandAsync();

        var snapshots = await _service.CaptureSnapshotsAsync(
            products.Select(item => item.ProductCode!)
        );

        Assert.Equal(products.Count, snapshots.Count);
        Assert.Equal("批量商品 1004", snapshots["P-BATCH-1004"].ProductName);
    }

    [Fact]
    public async Task RecordChangesAsync_只记录实际差异并保留批次和操作者()
    {
        var before = new WarehouseProductChangeSnapshotDto
        {
            ProductCode = "P01",
            ImportPrice = 0.70m,
            RetailPrice = 2.50m,
            ProductName = "旧名称",
            IsActive = true,
        };
        var after = new WarehouseProductChangeSnapshotDto
        {
            ProductCode = "P01",
            ImportPrice = 0.75m,
            RetailPrice = 2.50m,
            ProductName = "新名称",
            IsActive = true,
        };
        var batchGuid = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 8, 12, 4, 0, 0, DateTimeKind.Utc);

        var inserted = await _service.RecordChangesAsync(
            new Dictionary<string, WarehouseProductChangeSnapshotDto> { ["P01"] = before },
            new Dictionary<string, WarehouseProductChangeSnapshotDto> { ["P01"] = after },
            new WarehouseProductChangeHistoryContextDto
            {
                Action = "BatchUpdate",
                Source = "WarehouseProducts",
                SourceReference = "batch-001",
                BatchGuid = batchGuid,
                ActorUserGuid = "user-001",
                ActorName = "管理员",
                ActorType = "User",
                OccurredAtUtc = occurredAt,
            }
        );

        Assert.Equal(1, inserted);
        var history = await _db.Queryable<WarehouseProductChangeHistory>().SingleAsync();
        Assert.Equal("P01", history.ProductCode);
        Assert.Equal("BatchUpdate", history.Action);
        Assert.Equal("WarehouseProducts", history.Source);
        Assert.Equal("batch-001", history.SourceReference);
        Assert.Equal(batchGuid, history.BatchGuid);
        Assert.Equal("user-001", history.ActorUserGuid);
        Assert.Equal("管理员", history.ActorName);
        Assert.Equal("User", history.ActorType);
        Assert.Equal(occurredAt, history.OccurredAtUtc);

        using var changes = JsonDocument.Parse(history.ChangesJson);
        Assert.Equal(2, changes.RootElement.GetArrayLength());
        Assert.Equal("importPrice", changes.RootElement[0].GetProperty("fieldKey").GetString());
        Assert.Equal("0.70", changes.RootElement[0].GetProperty("beforeValue").GetString());
        Assert.Equal("0.75", changes.RootElement[0].GetProperty("afterValue").GetString());
        Assert.Equal("productName", changes.RootElement[1].GetProperty("fieldKey").GetString());

        var noOp = await _service.RecordChangesAsync(
            new Dictionary<string, WarehouseProductChangeSnapshotDto> { ["P01"] = after },
            new Dictionary<string, WarehouseProductChangeSnapshotDto> { ["P01"] = after },
            new WarehouseProductChangeHistoryContextDto
            {
                Action = "Patch",
                Source = "WarehouseProducts",
                ActorName = "管理员",
            }
        );

        Assert.Equal(0, noOp);
        Assert.Equal(1, await _db.Queryable<WarehouseProductChangeHistory>().CountAsync());
    }

    [Fact]
    public async Task RecordChangesAsync_Product字段变化不被WarehouseProduct固定优先级遮蔽()
    {
        await SeedProductAsync();
        var before = await _service.CaptureSnapshotsAsync(["P01"]);

        await _db.Updateable<Product>()
            .SetColumns(item => new Product
            {
                PurchasePrice = 0.85m,
                RetailPrice = 2.75m,
                IsActive = false,
            })
            .Where(item => item.ProductCode == "P01")
            .ExecuteCommandAsync();

        var after = await _service.CaptureSnapshotsAsync(["P01"]);
        var inserted = await _service.RecordChangesAsync(
            before,
            after,
            new WarehouseProductChangeHistoryContextDto
            {
                Source = "ProductReact.Update",
                ActorName = "商品管理员",
            }
        );

        Assert.Equal(1, inserted);
        var changes = await ReadSingleHistoryChangesAsync();
        Assert.Equal(("0.7", "0.85"), changes["importPrice"]);
        Assert.Equal(("2.5", "2.75"), changes["retailPrice"]);
        Assert.Equal(("true", "false"), changes["isActive"]);
        Assert.Equal(3, changes.Count);
    }

    [Fact]
    public async Task RecordChangesAsync_DomesticProduct字段变化不被WarehouseProduct固定优先级遮蔽()
    {
        await SeedProductAsync();
        var before = await _service.CaptureSnapshotsAsync(["P01"]);

        await _db.Updateable<DomesticProduct>()
            .SetColumns(item => new DomesticProduct
            {
                DomesticPrice = 0.55m,
                ImportPrice = 0.80m,
                OEMPrice = 2.80m,
                UnitVolume = 0.123m,
            })
            .Where(item => item.ProductCode == "P01")
            .ExecuteCommandAsync();

        var after = await _service.CaptureSnapshotsAsync(["P01"]);
        var inserted = await _service.RecordChangesAsync(
            before,
            after,
            new WarehouseProductChangeHistoryContextDto
            {
                Source = "DomesticProduct.Update",
                ActorName = "国内商品管理员",
            }
        );

        Assert.Equal(1, inserted);
        var changes = await ReadSingleHistoryChangesAsync();
        Assert.Equal(("0.5", "0.55"), changes["domesticPrice"]);
        Assert.Equal(("0.7", "0.8"), changes["importPrice"]);
        Assert.Equal(("2.5", "2.8"), changes["retailPrice"]);
        Assert.Equal(("0.099", "0.123"), changes["volume"]);
        Assert.Equal(4, changes.Count);
    }

    [Fact]
    public async Task RecordChangesAsync_WarehouseProduct装箱数变化不被DomesticProduct遮蔽()
    {
        await SeedProductAsync();
        var before = await _service.CaptureSnapshotsAsync(["P01"]);

        await _db.Updateable<WarehouseProduct>()
            .SetColumns(item => item.PackingQuantity == 9)
            .Where(item => item.ProductCode == "P01")
            .ExecuteCommandAsync();

        var after = await _service.CaptureSnapshotsAsync(["P01"]);
        var inserted = await _service.RecordChangesAsync(
            before,
            after,
            new WarehouseProductChangeHistoryContextDto
            {
                Source = "WarehouseProduct.Update",
                ActorName = "仓库管理员",
            }
        );

        Assert.Equal(1, inserted);
        var changes = await ReadSingleHistoryChangesAsync();
        Assert.Equal(("8", "9"), changes["packingQuantity"]);
        Assert.Single(changes);
    }

    [Fact]
    public async Task RecordChangesAsync_三张主档联动同一语义字段只输出一条差异()
    {
        await SeedProductAsync();
        var before = await _service.CaptureSnapshotsAsync(["P01"]);

        await _db.Updateable<WarehouseProduct>()
            .SetColumns(item => item.ImportPrice == 0.90m)
            .Where(item => item.ProductCode == "P01")
            .ExecuteCommandAsync();
        await _db.Updateable<Product>()
            .SetColumns(item => item.PurchasePrice == 0.90m)
            .Where(item => item.ProductCode == "P01")
            .ExecuteCommandAsync();
        await _db.Updateable<DomesticProduct>()
            .SetColumns(item => item.ImportPrice == 0.90m)
            .Where(item => item.ProductCode == "P01")
            .ExecuteCommandAsync();

        var after = await _service.CaptureSnapshotsAsync(["P01"]);
        Assert.Equal(
            1,
            await _service.RecordChangesAsync(
                before,
                after,
                new WarehouseProductChangeHistoryContextDto
                {
                    Source = "MirrorSync",
                    ActorName = "System",
                    ActorType = "System",
                }
            )
        );

        var changes = await ReadSingleHistoryChangesAsync();
        Assert.Equal(("0.7", "0.9"), changes["importPrice"]);
        Assert.Single(changes);
    }

    [Fact]
    public async Task RecordChangesAsync_同批次同商品跨调用只写一条事件()
    {
        var before = new Dictionary<string, WarehouseProductChangeSnapshotDto>
        {
            ["P01"] = new() { ProductCode = "P01", RetailPrice = 2.50m },
        };
        var after = new Dictionary<string, WarehouseProductChangeSnapshotDto>
        {
            ["P01"] = new() { ProductCode = "P01", RetailPrice = 2.75m },
        };
        var batchGuid = Guid.NewGuid();
        var context = new WarehouseProductChangeHistoryContextDto
        {
            Action = "BatchUpdate",
            Source = "DataSyncFull",
            BatchGuid = batchGuid,
            ActorName = "System",
            ActorType = "System",
        };

        Assert.Equal(1, await _service.RecordChangesAsync(before, after, context));
        Assert.Equal(0, await _service.RecordChangesAsync(before, after, context));
        Assert.Equal(
            1,
            await _db.Queryable<WarehouseProductChangeHistory>()
                .Where(item => item.BatchGuid == batchGuid && item.ProductCode == "P01")
                .CountAsync()
        );
    }

    [Fact]
    public async Task RecordChangesAsync_商品改码时归入新编码并记录编码前后值()
    {
        var inserted = await _service.RecordChangesAsync(
            new Dictionary<string, WarehouseProductChangeSnapshotDto>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["P-NEW"] = new WarehouseProductChangeSnapshotDto
                {
                    ProductCode = "P-OLD",
                    ProductName = "同一商品",
                },
            },
            new Dictionary<string, WarehouseProductChangeSnapshotDto>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                ["P-NEW"] = new WarehouseProductChangeSnapshotDto
                {
                    ProductCode = "P-NEW",
                    ProductName = "同一商品",
                },
            },
            new WarehouseProductChangeHistoryContextDto
            {
                Action = "Update",
                Source = "ProductReact.Update",
                ActorName = "改码操作员",
            }
        );

        Assert.Equal(1, inserted);
        var history = await _db.Queryable<WarehouseProductChangeHistory>().SingleAsync();
        Assert.Equal("P-NEW", history.ProductCode);
        using var changes = JsonDocument.Parse(history.ChangesJson);
        var change = Assert.Single(changes.RootElement.EnumerateArray());
        Assert.Equal("productCode", change.GetProperty("fieldKey").GetString());
        Assert.Equal("P-OLD", change.GetProperty("beforeValue").GetString());
        Assert.Equal("P-NEW", change.GetProperty("afterValue").GetString());
    }

    [Fact]
    public async Task RecordChangesAsync_仅有after快照时生成Create事件()
    {
        var inserted = await _service.RecordChangesAsync(
            new Dictionary<string, WarehouseProductChangeSnapshotDto>(),
            new Dictionary<string, WarehouseProductChangeSnapshotDto>
            {
                ["P01"] = new WarehouseProductChangeSnapshotDto
                {
                    ProductCode = "P01",
                    ProductName = "新商品",
                    ImportPrice = 0.70m,
                },
            },
            new WarehouseProductChangeHistoryContextDto
            {
                Source = "WarehouseProducts",
                ActorName = "创建人",
            }
        );

        var history = await _db.Queryable<WarehouseProductChangeHistory>().SingleAsync();
        Assert.Equal(1, inserted);
        Assert.Equal("Create", history.Action);
        Assert.Contains("beforeValue", history.ChangesJson);
    }

    [Fact]
    public async Task RecordChangesAsync_实际变化时同步仓库列表审计字段()
    {
        await SeedProductAsync();
        var before = await _service.CaptureSnapshotsAsync(["P01"]);
        await _db.Updateable<WarehouseProduct>()
            .SetColumns(item => new WarehouseProduct
            {
                ImportPrice = 0.75m,
                UpdatedAt = new DateTime(2026, 8, 12, 1, 0, 0, DateTimeKind.Utc),
                UpdatedBy = "临时操作者",
            })
            .Where(item => item.ProductCode == "P01")
            .ExecuteCommandAsync();
        var after = await _service.CaptureSnapshotsAsync(["P01"]);
        var occurredAtUtc = new DateTime(2026, 8, 12, 4, 0, 0, DateTimeKind.Utc);

        var inserted = await _service.RecordChangesAsync(
            before,
            after,
            new WarehouseProductChangeHistoryContextDto
            {
                Source = "WarehouseProducts",
                ActorName = "最终操作者",
                OccurredAtUtc = occurredAtUtc,
            }
        );

        var warehouse = await _db.Queryable<WarehouseProduct>()
            .SingleAsync(item => item.ProductCode == "P01");
        Assert.Equal(1, inserted);
        Assert.Equal(occurredAtUtc, warehouse.UpdatedAt);
        Assert.Equal("最终操作者", warehouse.UpdatedBy);
    }

    [Fact]
    public async Task RecordChangesAsync_无实际变化时恢复仓库列表审计字段()
    {
        await SeedProductAsync();
        var originalUpdatedAt = new DateTime(2026, 8, 11, 3, 0, 0, DateTimeKind.Utc);
        await _db.Updateable<WarehouseProduct>()
            .SetColumns(item => new WarehouseProduct
            {
                UpdatedAt = originalUpdatedAt,
                UpdatedBy = "原操作者",
            })
            .Where(item => item.ProductCode == "P01")
            .ExecuteCommandAsync();
        var before = await _service.CaptureSnapshotsAsync(["P01"]);
        await _db.Updateable<WarehouseProduct>()
            .SetColumns(item => new WarehouseProduct
            {
                UpdatedAt = new DateTime(2026, 8, 12, 4, 0, 0, DateTimeKind.Utc),
                UpdatedBy = "无变化操作者",
            })
            .Where(item => item.ProductCode == "P01")
            .ExecuteCommandAsync();
        var after = await _service.CaptureSnapshotsAsync(["P01"]);

        var inserted = await _service.RecordChangesAsync(
            before,
            after,
            new WarehouseProductChangeHistoryContextDto
            {
                Source = "WarehouseProducts",
                ActorName = "无变化操作者",
            }
        );

        var warehouse = await _db.Queryable<WarehouseProduct>()
            .SingleAsync(item => item.ProductCode == "P01");
        Assert.Equal(0, inserted);
        Assert.Equal(originalUpdatedAt, warehouse.UpdatedAt);
        Assert.Equal("原操作者", warehouse.UpdatedBy);
        Assert.Equal(0, await _db.Queryable<WarehouseProductChangeHistory>().CountAsync());
    }

    [Fact]
    public async Task RecordChangesAsync_无实际变化但after后已有新操作人时不覆盖并发审计列()
    {
        await SeedProductAsync();
        var originalUpdatedAt = new DateTime(2026, 8, 11, 3, 0, 0, DateTimeKind.Utc);
        await _db.Updateable<WarehouseProduct>()
            .SetColumns(item => new WarehouseProduct
            {
                UpdatedAt = originalUpdatedAt,
                UpdatedBy = "原操作者",
            })
            .Where(item => item.ProductCode == "P01")
            .ExecuteCommandAsync();
        var before = await _service.CaptureSnapshotsAsync(["P01"]);

        var noChangeUpdatedAt = new DateTime(2026, 8, 12, 4, 0, 0, DateTimeKind.Utc);
        await _db.Updateable<WarehouseProduct>()
            .SetColumns(item => new WarehouseProduct
            {
                UpdatedAt = noChangeUpdatedAt,
                UpdatedBy = "无变化操作者",
            })
            .Where(item => item.ProductCode == "P01")
            .ExecuteCommandAsync();
        var after = await _service.CaptureSnapshotsAsync(["P01"]);

        var concurrentUpdatedAt = new DateTime(2026, 8, 12, 5, 0, 0, DateTimeKind.Utc);
        await _db.Updateable<WarehouseProduct>()
            .SetColumns(item => new WarehouseProduct
            {
                UpdatedAt = concurrentUpdatedAt,
                UpdatedBy = "并发新操作者",
            })
            .Where(item => item.ProductCode == "P01")
            .ExecuteCommandAsync();

        Assert.Equal(
            0,
            await _service.RecordChangesAsync(
                before,
                after,
                new WarehouseProductChangeHistoryContextDto
                {
                    Source = "WarehouseProducts",
                    ActorName = "无变化操作者",
                }
            )
        );

        var warehouse = await _db.Queryable<WarehouseProduct>()
            .SingleAsync(item => item.ProductCode == "P01");
        Assert.Equal(concurrentUpdatedAt, warehouse.UpdatedAt);
        Assert.Equal("并发新操作者", warehouse.UpdatedBy);
        Assert.Equal(0, await _db.Queryable<WarehouseProductChangeHistory>().CountAsync());
    }

    [Fact]
    public async Task RecordChangesAsync_未显式身份时从当前请求补齐且无请求时使用System()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(item => item.GetCurrentUsername()).Returns("请求用户");
        currentUser.Setup(item => item.GetCurrentUserGuid()).Returns("user-guid");
        var requestService = new WarehouseProductChangeHistoryService(
            CreateSqlSugarContext(_db),
            NullLogger<WarehouseProductChangeHistoryService>.Instance,
            currentUser.Object
        );

        await requestService.RecordChangesAsync(
            new Dictionary<string, WarehouseProductChangeSnapshotDto>(),
            new Dictionary<string, WarehouseProductChangeSnapshotDto>
            {
                ["P01"] = new WarehouseProductChangeSnapshotDto { ProductName = "请求创建" },
            },
            new WarehouseProductChangeHistoryContextDto { Source = "WarehouseProducts" }
        );

        var requestHistory = await _db.Queryable<WarehouseProductChangeHistory>().SingleAsync();
        Assert.Equal("请求用户", requestHistory.ActorName);
        Assert.Equal("user-guid", requestHistory.ActorUserGuid);
        Assert.Equal("User", requestHistory.ActorType);

        var systemService = new WarehouseProductChangeHistoryService(
            CreateSqlSugarContext(_db),
            NullLogger<WarehouseProductChangeHistoryService>.Instance,
            Mock.Of<ICurrentUserService>()
        );
        await systemService.RecordChangesAsync(
            new Dictionary<string, WarehouseProductChangeSnapshotDto>(),
            new Dictionary<string, WarehouseProductChangeSnapshotDto>
            {
                ["P02"] = new WarehouseProductChangeSnapshotDto { ProductName = "系统创建" },
            },
            new WarehouseProductChangeHistoryContextDto { Source = "ScheduledTask" }
        );

        var systemHistory = await _db.Queryable<WarehouseProductChangeHistory>()
            .OrderByDescending(item => item.Id)
            .FirstAsync();
        Assert.Equal("System", systemHistory.ActorName);
        Assert.Null(systemHistory.ActorUserGuid);
        Assert.Equal("System", systemHistory.ActorType);
    }

    [Fact]
    public async Task RecordChangesAsync_后台明确System时不继承当前HTTP用户()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(item => item.GetCurrentUsername()).Returns("当前请求用户");
        currentUser.Setup(item => item.GetCurrentUserGuid()).Returns("request-user-guid");
        var service = new WarehouseProductChangeHistoryService(
            CreateSqlSugarContext(_db),
            NullLogger<WarehouseProductChangeHistoryService>.Instance,
            currentUser.Object
        );

        await service.RecordChangesAsync(
            new Dictionary<string, WarehouseProductChangeSnapshotDto>(),
            new Dictionary<string, WarehouseProductChangeSnapshotDto>
            {
                ["P-SYSTEM"] = new WarehouseProductChangeSnapshotDto
                {
                    ProductCode = "P-SYSTEM",
                    ProductName = "后台同步商品",
                },
            },
            new WarehouseProductChangeHistoryContextDto
            {
                Source = "DataSyncIncremental",
                ActorName = "System",
                ActorType = "System",
            }
        );

        var history = await _db.Queryable<WarehouseProductChangeHistory>().SingleAsync();
        Assert.Equal("System", history.ActorName);
        Assert.Equal("System", history.ActorType);
        Assert.Null(history.ActorUserGuid);
    }

    [Fact]
    public async Task RecordChangesAsync_后台有保存的用户Guid但名称缺失时仍保留用户身份()
    {
        await _service.RecordChangesAsync(
            new Dictionary<string, WarehouseProductChangeSnapshotDto>(),
            new Dictionary<string, WarehouseProductChangeSnapshotDto>
            {
                ["P-JOB"] = new()
                {
                    ProductCode = "P-JOB",
                    ProductName = "用户发起的后台任务商品",
                },
            },
            new WarehouseProductChangeHistoryContextDto
            {
                Source = "ContainerProductCreationJob",
                ActorUserGuid = "queued-user-guid",
                ActorType = "User",
            }
        );

        var history = await _db.Queryable<WarehouseProductChangeHistory>().SingleAsync();
        Assert.Equal("queued-user-guid", history.ActorUserGuid);
        Assert.Equal("queued-user-guid", history.ActorName);
        Assert.Equal("User", history.ActorType);
    }

    [Fact]
    public async Task StartupSchemaMigrator_SQLite重复执行后索引和只追加触发器均生效()
    {
        await StartupSchemaMigrator.EnsureAsync(_db, NullLogger.Instance);
        await StartupSchemaMigrator.EnsureAsync(_db, NullLogger.Instance);

        Assert.Equal(
            6,
            await _db.Ado.GetIntAsync(
                "SELECT COUNT(1) FROM sqlite_master "
                    + "WHERE type IN ('index', 'trigger') "
                    + "AND name IN ('IX_WarehouseProductChangeHistory_OccurredAtUtc_Id', "
                    + "'IX_WarehouseProductChangeHistory_ProductCode_OccurredAtUtc_Id', "
                    + "'IX_WarehouseProductChangeHistory_BatchGuid', "
                    + "'UX_WarehouseProductChangeHistory_BatchGuid_ProductCode', "
                    + "'TR_WarehouseProductChangeHistory_AppendOnly_Update', "
                    + "'TR_WarehouseProductChangeHistory_AppendOnly_Delete')"
            )
        );

        await _db.Insertable(
            new WarehouseProductChangeHistory
            {
                EventGuid = Guid.NewGuid(),
                ProductCode = "P-IMMUTABLE",
                Action = "Update",
                Source = "Test",
                ActorName = "System",
                ActorType = "System",
                OccurredAtUtc = DateTime.UtcNow,
                ChangesJson = "[]",
            }
        ).ExecuteCommandAsync();

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await _db.Updateable<WarehouseProductChangeHistory>()
                .SetColumns(item => item.ActorName == "tampered")
                .Where(item => item.ProductCode == "P-IMMUTABLE")
                .ExecuteCommandAsync()
        );
        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await _db.Deleteable<WarehouseProductChangeHistory>()
                .Where(item => item.ProductCode == "P-IMMUTABLE")
                .ExecuteCommandAsync()
        );
    }

    [Fact]
    public async Task GetChangeHistoryAsync_按时间和Id分页并返回当前商品摘要()
    {
        await SeedProductAsync();
        var before = await _service.CaptureSnapshotsAsync(["P01"]);
        var after = new WarehouseProductChangeSnapshotDto
        {
            ProductCode = "P01",
            ImportPrice = 0.71m,
            ProductName = "商品一",
        };
        await _service.RecordChangesAsync(
            before.ToDictionary(item => item.Key, item => item.Value),
            new Dictionary<string, WarehouseProductChangeSnapshotDto> { ["P01"] = after },
            new WarehouseProductChangeHistoryContextDto
            {
                Action = "Patch",
                Source = "WarehouseProducts",
                ActorName = "旧操作人",
                OccurredAtUtc = new DateTime(2026, 8, 11, 4, 0, 0, DateTimeKind.Utc),
            }
        );

        var secondBefore = new Dictionary<string, WarehouseProductChangeSnapshotDto>
        {
            ["P01"] = after,
        };
        var secondAfter = new Dictionary<string, WarehouseProductChangeSnapshotDto>
        {
            ["P01"] = after with { RetailPrice = 2.60m },
        };
        await _service.RecordChangesAsync(
            secondBefore,
            secondAfter,
            new WarehouseProductChangeHistoryContextDto
            {
                Action = "BatchUpdate",
                Source = "ContainerDetail",
                ActorName = "新操作人",
                OccurredAtUtc = new DateTime(2026, 8, 12, 4, 0, 0, DateTimeKind.Utc),
            }
        );

        var page = await _service.GetChangeHistoryAsync("P01", 1, 1);

        Assert.Equal("P01", page.ProductSummary.ProductCode);
        Assert.Equal("商品一", page.ProductSummary.ProductName);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal(1, page.PageSize);
        var eventItem = Assert.Single(page.Events);
        Assert.Equal("ContainerDetail", eventItem.Source);
        Assert.Equal("新操作人", eventItem.ActorName);
        Assert.Equal("retailPrice", Assert.Single(eventItem.Changes).FieldKey);
    }

    [Fact]
    public async Task WarehouseRetailPriceChangeQuery_SQLite按月筛选最新价格并覆盖仓库商品边界()
    {
        await SeedProductAsync();
        await SeedRetailPriceQueryProductsAsync();
        await SeedRetailPriceQueryLocationsAsync();

        var startUtc = new DateTime(2026, 7, 31, 14, 0, 0, DateTimeKind.Utc);
        var sameOccurredAtUtc = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);
        await InsertRetailPriceHistoryAsync("P01", "Create", startUtc, "1.00");
        await InsertHistoryAsync("P01", "Patch", startUtc, "[{\"fieldKey\":\"productName\",\"afterValue\":\"名称\"}]");
        await InsertRetailPriceHistoryAsync("P01", "Patch", startUtc, "2.00");
        await InsertRetailPriceHistoryAsync("P01", "Patch", sameOccurredAtUtc, "3.00");
        await InsertRetailPriceHistoryAsync("P01", "Patch", sameOccurredAtUtc, null);
        await InsertRetailPriceHistoryAsync(
            "P01",
            "Patch",
            new DateTime(2026, 8, 31, 14, 0, 0, DateTimeKind.Utc),
            "99.00"
        );
        await InsertRetailPriceHistoryAsync(
            "P02",
            "Patch",
            new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc),
            "8.50"
        );
        await InsertRetailPriceHistoryAsync(
            "P03",
            "Patch",
            new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc),
            "9.50"
        );
        await InsertRetailPriceHistoryAsync(
            "P04",
            "Patch",
            new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc),
            "10.50"
        );

        var queryService = new WarehouseRetailPriceChangeService(CreateSqlSugarContext(_db));
        var query = new WarehouseRetailPriceChangeQuery
        {
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 31),
        };

        var defaultLocationPage = await queryService.GetAsync(query);

        Assert.Equal(new DateOnly(2026, 8, 1), defaultLocationPage.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 31), defaultLocationPage.EndDate);
        Assert.True(defaultLocationPage.OnlyWithLocation);
        Assert.Equal(1, defaultLocationPage.PageNumber);
        Assert.Equal(50, defaultLocationPage.PageSize);
        Assert.Equal(1, defaultLocationPage.Total);
        var p01 = Assert.Single(defaultLocationPage.Items);
        Assert.Equal("P01", p01.ProductCode);
        Assert.Equal("image-01", p01.ProductImage);
        Assert.Equal("ITEM-01", p01.ItemNumber);
        Assert.Equal("6926393337100", p01.Barcode);
        Assert.Null(p01.LatestRetailPrice);
        Assert.Equal(sameOccurredAtUtc, p01.LastPriceChangedAtUtc);
        Assert.Equal(DateTimeKind.Utc, p01.LastPriceChangedAtUtc.Kind);

        var itemProperties = typeof(WarehouseRetailPriceChangeItem).GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name)
            .ToArray();
        Assert.Equal(
            ["Barcode", "ItemNumber", "LastPriceChangedAtUtc", "LatestRetailPrice", "ProductCode", "ProductImage"],
            itemProperties
        );

        var allLocationsPage = await queryService.GetAsync(query with { OnlyWithLocation = false });
        Assert.Equal(3, allLocationsPage.Total);
        Assert.Equal(["P03", "P02", "P01"], allLocationsPage.Items.Select(item => item.ProductCode));
        var metadataMissing = allLocationsPage.Items[0];
        Assert.Null(metadataMissing.ProductImage);
        Assert.Null(metadataMissing.ItemNumber);
        Assert.Null(metadataMissing.Barcode);
        var domesticFallback = allLocationsPage.Items[1];
        Assert.Equal("D-ITEM-02", domesticFallback.ItemNumber);
        Assert.Equal("D-BARCODE-02", domesticFallback.Barcode);
        Assert.DoesNotContain(allLocationsPage.Items, item => item.ProductCode == "P04");

        var keywordPage = await queryService.GetAsync(
            query with { Keyword = "D-BARCODE-02", OnlyWithLocation = false }
        );
        Assert.Equal("P02", Assert.Single(keywordPage.Items).ProductCode);

        var firstPage = await queryService.GetAsync(query with { OnlyWithLocation = false, PageSize = 1 });
        var secondPage = await queryService.GetAsync(
            query with { OnlyWithLocation = false, PageNumber = 2, PageSize = 1 }
        );
        var thirdPage = await queryService.GetAsync(
            query with { OnlyWithLocation = false, PageNumber = 3, PageSize = 1 }
        );
        Assert.Equal("P03", Assert.Single(firstPage.Items).ProductCode);
        Assert.Equal("P02", Assert.Single(secondPage.Items).ProductCode);
        Assert.Equal("P01", Assert.Single(thirdPage.Items).ProductCode);

        var normalizedDefault = WarehouseRetailPriceChangeService.NormalizeQuery(
            new WarehouseRetailPriceChangeQuery(),
            new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero)
        );
        Assert.Equal(new DateOnly(2026, 8, 1), normalizedDefault.StartDate);
        Assert.Equal(new DateOnly(2026, 8, 31), normalizedDefault.EndDate);
        Assert.True(normalizedDefault.OnlyWithLocation);
        Assert.Equal(1, normalizedDefault.PageNumber);
        Assert.Equal(50, normalizedDefault.PageSize);
        Assert.Equal(new DateTime(2026, 7, 31, 14, 0, 0, DateTimeKind.Utc), normalizedDefault.StartUtc);
        Assert.Equal(new DateTime(2026, 8, 31, 14, 0, 0, DateTimeKind.Utc), normalizedDefault.EndExclusiveUtc);
    }

    [Fact]
    public void WarehouseRetailPriceChangeQuery_拒绝不完整日期和无效分页范围()
    {
        var now = new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() => WarehouseRetailPriceChangeService.NormalizeQuery(
            new WarehouseRetailPriceChangeQuery { StartDate = new DateOnly(2026, 8, 1) }, now));
        Assert.Throws<ArgumentException>(() => WarehouseRetailPriceChangeService.NormalizeQuery(
            new WarehouseRetailPriceChangeQuery { EndDate = new DateOnly(2026, 8, 31) }, now));
        Assert.Throws<ArgumentException>(() => WarehouseRetailPriceChangeService.NormalizeQuery(
            new WarehouseRetailPriceChangeQuery { PageNumber = 0 }, now));
        Assert.Throws<ArgumentException>(() => WarehouseRetailPriceChangeService.NormalizeQuery(
            new WarehouseRetailPriceChangeQuery { PageSize = 101 }, now));
        Assert.Throws<ArgumentException>(() => WarehouseRetailPriceChangeService.NormalizeQuery(
            new WarehouseRetailPriceChangeQuery
            {
                StartDate = new DateOnly(2025, 1, 1),
                EndDate = new DateOnly(2026, 1, 2),
            }, now));
        Assert.Throws<ArgumentException>(() => WarehouseRetailPriceChangeService.NormalizeQuery(
            new WarehouseRetailPriceChangeQuery { PageNumber = int.MaxValue, PageSize = 100 }, now));
    }

    [Fact]
    public void WarehouseRetailPriceChangeQuery_控制器路由权限和SqlServer参数化契约固定()
    {
        var controllerType = typeof(WarehouseRetailPriceChangesController);
        var route = Assert.Single(controllerType.GetCustomAttributes(typeof(RouteAttribute), false));
        Assert.Equal("api/react/v1/warehouse-retail-price-changes", ((RouteAttribute)route).Template);
        var method = controllerType.GetMethod(nameof(WarehouseRetailPriceChangesController.Get));
        Assert.NotNull(method);
        Assert.Single(method!.GetCustomAttributes(typeof(HttpGetAttribute), false));
        var authorize = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), false));
        Assert.Equal(Permissions.Warehouse.ManageProducts, ((AuthorizeAttribute)authorize).Policy);

        var sql = WarehouseRetailPriceChangeService.BuildSqlServerPageSql();
        var countSql = WarehouseRetailPriceChangeService.BuildSqlServerCountSql();
        Assert.Contains("ISJSON([h].[ChangesJson]) = 1", sql);
        Assert.Contains("OPENJSON(CASE WHEN ISJSON([h].[ChangesJson]) = 1", sql);
        Assert.Contains("ROW_NUMBER() OVER", sql);
        Assert.Contains("CASE WHEN [p].[UUID] IS NULL", sql);
        Assert.Contains("CHARINDEX(@Keyword, [ProductCode]) > 0", sql);
        Assert.Contains("@StartUtc", sql);
        Assert.Contains("@EndExclusiveUtc", sql);
        Assert.Contains("@OnlyWithLocation", sql);
        Assert.Contains("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", sql);
        Assert.Contains("COUNT(1) AS [Total]", countSql);
        Assert.DoesNotContain("OFFSET", countSql);
    }

    [Fact]
    public async Task WarehouseRetailPriceChangeQuery_控制器成功时直接返回规范化页面()
    {
        var page = new WarehouseRetailPriceChangePage
        {
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 31),
            OnlyWithLocation = true,
            PageNumber = 1,
            PageSize = 50,
            Total = 0,
        };
        var service = new Mock<IWarehouseRetailPriceChangeService>();
        service.Setup(item => item.GetAsync(It.IsAny<WarehouseRetailPriceChangeQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);
        var controller = new WarehouseRetailPriceChangesController(
            service.Object,
            NullLogger<WarehouseRetailPriceChangesController>.Instance
        );

        var result = await controller.Get(new WarehouseRetailPriceChangeQuery(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(page, ok.Value);
    }

    [Fact]
    public void Controller_暴露修改历史读取端点并限制页大小()
    {
        var method = typeof(ReactProductWarehouseController).GetMethod(
            "GetChangeHistory"
        );

        Assert.NotNull(method);
        var route = Assert.Single(method!.GetCustomAttributes(typeof(HttpGetAttribute), false));
        Assert.Equal("{productCode}/change-history", ((HttpGetAttribute)route).Template);
        var authorize = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), false));
        Assert.Equal(Permissions.Warehouse.ManageProducts, ((AuthorizeAttribute)authorize).Policy);
        Assert.Null(((AuthorizeAttribute)authorize).Roles);
    }

    [Fact]
    public async Task Controller_历史响应使用扁平data契约()
    {
        var historyService = new Mock<IWarehouseProductChangeHistoryService>();
        historyService
            .Setup(service => service.GetChangeHistoryAsync("P01", 2, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new WarehouseProductChangeHistoryPageDto
                {
                    ProductSummary = new WarehouseProductChangeHistoryProductSummaryDto
                    {
                        ProductCode = "P01",
                        ItemNumber = "ITEM-01",
                        ProductName = "商品一",
                    },
                    PageNumber = 2,
                    PageSize = 10,
                    TotalCount = 11,
                    Events =
                    [
                        new WarehouseProductChangeHistoryEventDto
                        {
                            EventGuid = Guid.NewGuid(),
                            Action = "Update",
                            Source = "WarehouseProducts",
                        },
                    ],
                }
            );

        var controller = new ReactProductWarehouseController(
            Mock.Of<IProductWarehouseReactService>(),
            Mock.Of<IWarehouseProductHqSyncJobService>(),
            Mock.Of<IWarehouseProductBatchUpdateJobService>(),
            NullLogger<ReactProductWarehouseController>.Instance,
            Mock.Of<IDeviceRegistrationService>(),
            Mock.Of<IMapper>(),
            new TencentCloudUploadService(
                Options.Create(new TencentCloudSettings()),
                NullLogger<TencentCloudUploadService>.Instance,
                new HttpClient()
            ),
            historyService.Object,
            Mock.Of<ICurrentUserService>(),
            Mock.Of<IProductHqSyncService>()
        );

        var result = await controller.GetChangeHistory("P01", 2, 10);
        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = ok.Value!;
        var data = payload.GetType().GetProperty("data")!.GetValue(payload)!;

        Assert.Equal("P01", data.GetType().GetProperty("productCode")!.GetValue(data));
        Assert.Equal("ITEM-01", data.GetType().GetProperty("itemNumber")!.GetValue(data));
        Assert.Equal("商品一", data.GetType().GetProperty("productName")!.GetValue(data));
        Assert.Equal(2, data.GetType().GetProperty("pageNumber")!.GetValue(data));
        Assert.Equal(10, data.GetType().GetProperty("pageSize")!.GetValue(data));
        Assert.Equal(11, data.GetType().GetProperty("total")!.GetValue(data));
        Assert.NotNull(data.GetType().GetProperty("events")!.GetValue(data));
        Assert.Null(data.GetType().GetProperty("ProductSummary"));
    }

    private async Task SeedProductAsync()
    {
        await _db.Insertable(
                new Product
                {
                    UUID = "UUID-P01",
                    ProductCode = "P01",
                    ProductCategoryGUID = "CAT-PRODUCT-01",
                    WarehouseCategoryGUID = "CAT-01",
                    LocalSupplierCode = "SUP-AU",
                    ItemNumber = "ITEM-01",
                    Barcode = "6926393337100",
                    ProductName = "商品一",
                    EnglishName = "Mesh Bag",
                    ProductType = 0,
                    MiddlePackageQuantity = 12,
                    PurchasePrice = 0.70m,
                    RetailPrice = 2.50m,
                    ProductImage = "image-01",
                    IsAutoPricing = false,
                    IsActive = true,
                }
            )
            .ExecuteCommandAsync();
        await _db.Insertable(
                new WarehouseProduct
                {
                    ProductCode = "P01",
                    DomesticPrice = 0.50m,
                    ImportPrice = 0.70m,
                    OEMPrice = 2.50m,
                    MinOrderQuantity = 1,
                    IsActive = true,
                    Volume = 0.016m,
                    PackingQuantity = 8,
                }
            )
            .ExecuteCommandAsync();
        await _db.Insertable(
                new DomesticProduct
                {
                    ProductCode = "P01",
                    SupplierCode = "SUP01",
                    ProductName = "商品一",
                    EnglishProductName = "Mesh Bag",
                    HBProductNo = "ITEM-01",
                    Barcode = "6926393337100",
                    ProductType = 0,
                    DomesticPrice = 0.50m,
                    ImportPrice = 0.70m,
                    OEMPrice = 2.50m,
                    PackingQuantity = 12,
                    UnitVolume = 0.099m,
                    MiddlePackQuantity = 12,
                    ProductImage = "image-01",
                    IsActive = true,
                }
            )
            .ExecuteCommandAsync();
    }

    private async Task SeedRetailPriceQueryProductsAsync()
    {
        await _db.Insertable(new WarehouseProduct { ProductCode = "P02", IsDeleted = false, IsActive = true })
            .ExecuteCommandAsync();
        await _db.Insertable(
                new DomesticProduct
                {
                    ProductCode = "P02",
                    HBProductNo = "D-ITEM-02",
                    Barcode = "D-BARCODE-02",
                    ProductName = "国内商品二",
                    EnglishProductName = "Domestic Two",
                    IsDeleted = false,
                    IsActive = true,
                }
            )
            .ExecuteCommandAsync();
        // 上架状态和库存不参与本页“当前仓库商品”筛选。
        await _db.Insertable(
                new WarehouseProduct
                {
                    ProductCode = "P03",
                    IsDeleted = false,
                    IsActive = false,
                    StockQuantity = 0,
                }
            )
            .ExecuteCommandAsync();
        await _db.Insertable(
                new Product
                {
                    UUID = "UUID-P03-DELETED",
                    ProductCode = "P03",
                    ProductName = "已删除商品",
                    IsDeleted = true,
                    IsActive = false,
                }
            )
            .ExecuteCommandAsync();
        // 软删除仓库商品才应被排除；它即使有变更历史也不能出现在结果中。
        await _db.Insertable(
                new WarehouseProduct { ProductCode = "P04", IsDeleted = true, IsActive = true }
            )
            .ExecuteCommandAsync();
    }

    private async Task SeedRetailPriceQueryLocationsAsync()
    {
        await _db.Insertable(
                new List<Location>
                {
                    // 货位启用状态不参与 onlyWithLocation，只检查两张关联表未删除。
                    new Location { LocationGuid = "LOC-A01", LocationCode = "A-01", Status = 0, IsDeleted = false },
                    new Location { LocationGuid = "LOC-A02", LocationCode = "A-02", IsDeleted = false },
                    new Location { LocationGuid = "LOC-OLD", LocationCode = "OLD", IsDeleted = true },
                }
            )
            .ExecuteCommandAsync();
        await _db.Insertable(
                new List<ProductLocation>
                {
                    new ProductLocation
                    {
                        Guid = "MAP-P01-A01",
                        ProductCode = "P01",
                        LocationGuid = "LOC-A01",
                        IsDeleted = false,
                    },
                    new ProductLocation
                    {
                        Guid = "MAP-P01-A02",
                        ProductCode = "P01",
                        LocationGuid = "LOC-A02",
                        IsDeleted = false,
                    },
                    new ProductLocation
                    {
                        Guid = "MAP-P01-OLD",
                        ProductCode = "P01",
                        LocationGuid = "LOC-OLD",
                        IsDeleted = false,
                    },
                }
            )
            .ExecuteCommandAsync();
    }

    private Task InsertRetailPriceHistoryAsync(
        string productCode,
        string action,
        DateTime occurredAtUtc,
        string? afterValue
    )
    {
        var afterValueJson = afterValue == null ? "null" : JsonSerializer.Serialize(afterValue);
        return InsertHistoryAsync(
            productCode,
            action,
            occurredAtUtc,
            $$"""[{"fieldKey":"retailPrice","beforeValue":"3.00","afterValue":{{afterValueJson}}}]"""
        );
    }

    private Task InsertHistoryAsync(
        string productCode,
        string action,
        DateTime occurredAtUtc,
        string changesJson
    ) => _db.Insertable(
            new WarehouseProductChangeHistory
            {
                EventGuid = Guid.NewGuid(),
                ProductCode = productCode,
                Action = action,
                Source = "WarehouseProducts",
                ActorName = "测试操作人",
                ActorType = "User",
                OccurredAtUtc = occurredAtUtc,
                ChangesJson = changesJson,
            }
        )
        .ExecuteCommandAsync();

    private async Task<Dictionary<string, (string? Before, string? After)>>
        ReadSingleHistoryChangesAsync()
    {
        var history = await _db.Queryable<WarehouseProductChangeHistory>().SingleAsync();
        using var document = JsonDocument.Parse(history.ChangesJson);
        return document.RootElement
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("fieldKey").GetString()!,
                item =>
                {
                    var before = item.GetProperty("beforeValue");
                    var after = item.GetProperty("afterValue");
                    return (
                        before.ValueKind == JsonValueKind.Null ? null : before.GetString(),
                        after.ValueKind == JsonValueKind.Null ? null : after.GetString()
                    );
                }
            );
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)
            RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext)
            .GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(context, db);
        return context;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
