using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class LegacyContainerProductChangeHistoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public LegacyContainerProductChangeHistoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        _connection.Open();
        _db = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = _connection.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        });
        _db.CodeFirst.InitTables(
            typeof(ContainerDetail),
            typeof(DomesticProduct),
            typeof(WarehouseProduct),
            typeof(Product)
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
    }

    [Fact]
    public async Task 旧货柜单条名称更新_应记录请求操作人且同值商品不产生事件()
    {
        await SeedDetailAsync("DETAIL-ONE", "CONTAINER-ONE", "PRODUCT-ONE");
        await SeedDetailAsync("DETAIL-NOOP", "CONTAINER-ONE", "PRODUCT-NOOP");
        await SeedDomesticProductAsync("PRODUCT-ONE", "旧中文", "Old English");
        await SeedDomesticProductAsync("PRODUCT-NOOP", "不变", "Stable English");

        var service = CreateContainerService(
            CreateRealHistory("旧接口操作人", "legacy-user-guid"),
            "旧接口操作人",
            "legacy-user-guid"
        );
        var result = await service.BatchUpdateDetailsAsync(
            [
                new UpdateContainerDetailDto
                {
                    HGUID = "DETAIL-ONE",
                    商品名称 = "新中文",
                    英文名称 = "New English",
                },
                new UpdateContainerDetailDto
                {
                    HGUID = "DETAIL-NOOP",
                    商品名称 = "不变",
                    英文名称 = "Stable English",
                },
            ]
        );

        var histories = await _db.Queryable<WarehouseProductChangeHistory>()
            .OrderBy(item => item.Id)
            .ToListAsync();
        Assert.Equal(2, result);
        var history = Assert.Single(histories);
        Assert.Equal("PRODUCT-ONE", history.ProductCode);
        Assert.Equal("BatchUpdate", history.Action);
        Assert.Equal("ContainerLegacyDetail", history.Source);
        Assert.Equal("CONTAINER-ONE", history.SourceReference);
        Assert.NotEqual(Guid.Empty, history.BatchGuid);
        Assert.Equal("legacy-user-guid", history.ActorUserGuid);
        Assert.Equal("旧接口操作人", history.ActorName);
        Assert.Equal("User", history.ActorType);
    }

    [Fact]
    public async Task 义乌货柜批量更新_应共享批次并保留无效参数的部分成功语义()
    {
        await SeedDomesticProductAsync("YIWU-ONE", "旧一", "Old One", 1m, 2m);
        await SeedDomesticProductAsync("YIWU-TWO", "旧二", "Old Two", 3m, 4m);

        var service = CreateYiwuService(
            CreateRealHistory("义乌操作人", "yiwu-user-guid"),
            "义乌操作人",
            "yiwu-user-guid"
        );
        var result = await service.BatchUpdateDomesticProductsAsync(
            [
                new DomesticProductDto
                {
                    ProductCode = "YIWU-ONE",
                    ProductName = "新一",
                    EnglishProductName = "New One",
                    OEMPrice = 5m,
                    ImportPrice = 6m,
                },
                new DomesticProductDto
                {
                    ProductCode = "YIWU-TWO",
                    ProductName = "新二",
                    EnglishProductName = "New Two",
                    OEMPrice = 7m,
                    ImportPrice = 8m,
                },
                new DomesticProductDto
                {
                    ProductCode = "yiwu-one",
                    ProductName = "重复项不应覆盖",
                    EnglishProductName = "Duplicate Must Not Win",
                    OEMPrice = 99m,
                    ImportPrice = 99m,
                },
                new DomesticProductDto { ProductCode = "" },
            ]
        );

        var histories = await _db.Queryable<WarehouseProductChangeHistory>()
            .OrderBy(item => item.ProductCode)
            .ToListAsync();
        Assert.True(result.Success);
        Assert.Equal(2, result.SuccessCount);
        Assert.Equal(2, result.FailedCount);
        Assert.Contains(result.Errors, error => error.Contains("本批次内重复", StringComparison.Ordinal));
        Assert.Equal(2, histories.Count);
        Assert.All(histories, history =>
        {
            Assert.Equal("BatchUpdate", history.Action);
            Assert.Equal("YiwuContainerBatch", history.Source);
            Assert.Equal($"Batch:{history.BatchGuid:N}", history.SourceReference);
            Assert.Equal("yiwu-user-guid", history.ActorUserGuid);
            Assert.Equal("义乌操作人", history.ActorName);
        });
        Assert.Single(histories.Select(history => history.BatchGuid).Distinct());
        var firstProduct = await _db.Queryable<DomesticProduct>()
            .SingleAsync(item => item.ProductCode == "YIWU-ONE");
        Assert.Equal("新一", firstProduct.ProductName);
        Assert.Equal(5m, firstProduct.OEMPrice);
    }

    [Fact]
    public async Task 旧货柜历史写入失败_应回滚整个有效批次()
    {
        await SeedDetailAsync("DETAIL-ROLLBACK", "CONTAINER-ROLLBACK", "PRODUCT-ROLLBACK");
        await SeedDomesticProductAsync("PRODUCT-ROLLBACK", "旧名称", "Old Name");
        var service = CreateContainerService(CreateFailingHistory());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.BatchUpdateDetailsAsync(
                [new UpdateContainerDetailDto { HGUID = "DETAIL-ROLLBACK", 商品名称 = "新名称" }]
            )
        );

        var product = await _db.Queryable<DomesticProduct>()
            .SingleAsync(item => item.ProductCode == "PRODUCT-ROLLBACK");
        Assert.Equal("旧名称", product.ProductName);
        Assert.Empty(await _db.Queryable<WarehouseProductChangeHistory>().ToListAsync());
    }

    [Fact]
    public async Task 义乌货柜历史写入失败_应回滚有效批次并清零成功数()
    {
        await SeedDomesticProductAsync("YIWU-ROLLBACK", "旧名称", "Old Name", 1m, 2m);
        var service = CreateYiwuService(CreateFailingHistory());

        var result = await service.BatchUpdateDomesticProductsAsync(
            [new DomesticProductDto
            {
                ProductCode = "YIWU-ROLLBACK",
                ProductName = "新名称",
                EnglishProductName = "New Name",
                OEMPrice = 3m,
                ImportPrice = 4m,
            }]
        );

        var product = await _db.Queryable<DomesticProduct>()
            .SingleAsync(item => item.ProductCode == "YIWU-ROLLBACK");
        Assert.False(result.Success);
        Assert.Equal(0, result.SuccessCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal("旧名称", product.ProductName);
        Assert.Equal(1m, product.OEMPrice);
        Assert.Empty(await _db.Queryable<WarehouseProductChangeHistory>().ToListAsync());
    }

    private ContainerService CreateContainerService(
        IWarehouseProductChangeHistoryService history,
        string actorName = "测试用户",
        string actorGuid = "test-user-guid") => new(
        CreateContext(),
        Mock.Of<IMapper>(),
        NullLogger<ContainerService>.Instance,
        Mock.Of<ITranslationService>(),
        history,
        CreateCurrentUser(actorName, actorGuid)
    );

    private YiwuContainerService CreateYiwuService(
        IWarehouseProductChangeHistoryService history,
        string actorName = "测试用户",
        string actorGuid = "test-user-guid") => new(
        CreateContext(),
        Mock.Of<IMapper>(),
        NullLogger<YiwuContainerService>.Instance,
        new ContainerExportService(NullLogger<ContainerExportService>.Instance, new HttpClient()),
        Mock.Of<ITranslationService>(),
        history,
        CreateCurrentUser(actorName, actorGuid)
    );

    private IWarehouseProductChangeHistoryService CreateRealHistory(string name, string guid)
    {
        return new WarehouseProductChangeHistoryService(
            CreateContext(),
            NullLogger<WarehouseProductChangeHistoryService>.Instance,
            CreateCurrentUser(name, guid)
        );
    }

    private static ICurrentUserService CreateCurrentUser(string name, string guid)
    {
        var currentUser = new Mock<ICurrentUserService>(MockBehavior.Strict);
        currentUser.Setup(item => item.GetCurrentUsername()).Returns(name);
        currentUser.Setup(item => item.GetCurrentUserGuid()).Returns(guid);
        return currentUser.Object;
    }

    private static IWarehouseProductChangeHistoryService CreateFailingHistory()
    {
        var history = new Mock<IWarehouseProductChangeHistoryService>(MockBehavior.Strict);
        history
            .Setup(item => item.CaptureSnapshotsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, WarehouseProductChangeSnapshotDto>());
        history
            .Setup(item => item.RecordChangesAsync(
                It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                It.IsAny<IReadOnlyDictionary<string, WarehouseProductChangeSnapshotDto>>(),
                It.IsAny<WarehouseProductChangeHistoryContextDto>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("forced history failure"));
        return history.Object;
    }

    private async Task SeedDetailAsync(string detailCode, string containerCode, string productCode)
    {
        await _db.Insertable(new ContainerDetail
        {
            DetailCode = detailCode,
            ContainerCode = containerCode,
            ProductCode = productCode,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task SeedDomesticProductAsync(
        string productCode,
        string productName,
        string englishName,
        decimal? oemPrice = null,
        decimal? importPrice = null)
    {
        await _db.Insertable(new DomesticProduct
        {
            ProductCode = productCode,
            ProductName = productName,
            EnglishProductName = englishName,
            OEMPrice = oemPrice,
            ImportPrice = importPrice,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private SqlSugarContext CreateContext()
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        var dbField = typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, _db);
        return context;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }
}
