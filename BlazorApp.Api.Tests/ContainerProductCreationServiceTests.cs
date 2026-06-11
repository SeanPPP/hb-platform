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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ContainerProductCreationServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;

    public ContainerProductCreationServiceTests()
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

        _db.CodeFirst.InitTables(
            typeof(Container),
            typeof(ContainerDetail),
            typeof(DomesticProduct),
            typeof(DomesticSetProduct),
            typeof(Product),
            typeof(ProductSetCode),
            typeof(WarehouseProduct),
            typeof(Store),
            typeof(StoreRetailPrice),
            typeof(StoreMultiCodeProduct),
            typeof(ChinaSupplier),
            typeof(ProductLocation),
            typeof(Location),
            typeof(ProductGrade)
        );
    }

    [Fact]
    public async Task ExecuteAsync_CreatesNormalProductAndStoreRetailPrices()
    {
        await InsertActiveStoreAsync("S001");
        await InsertContainerDetailAsync("D001", "C001", "P001", "普通商品", 1.2m, 3.4m);
        await InsertDomesticProductAsync("P001", "HB001", "商品一", "Product One", 0);

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-1",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D001" },
            }
        );

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Contains(result.Created, item => item.ProductCode == "P001" && item.DetailHguid == "D001");
        var product = await _db.Queryable<Product>().SingleAsync(p => p.ProductCode == "P001");
        var warehouseProduct = await _db.Queryable<WarehouseProduct>().SingleAsync(p => p.ProductCode == "P001");
        var storeRetailPrice = await _db.Queryable<StoreRetailPrice>().SingleAsync(p => p.ProductCode == "P001");

        Assert.Equal(1.2m, product.PurchasePrice);
        Assert.Equal(3.4m, product.RetailPrice);
        Assert.Equal(1.2m, warehouseProduct.ImportPrice);
        Assert.Equal(3.4m, warehouseProduct.OEMPrice);
        Assert.Equal(1.2m, storeRetailPrice.PurchasePrice);
        Assert.Equal(3.4m, storeRetailPrice.StoreRetailPriceValue);
    }

    [Fact]
    public async Task ExecuteAsync_CreatesSetProductWhenRelationIsComplete()
    {
        await InsertActiveStoreAsync("S001");
        await InsertContainerDetailAsync("D-SET-OK", "C001", "P-SET-OK", "套装商品", 2.2m, 5.5m);
        await InsertDomesticProductAsync("P-SET-OK", "HB-SET-OK", "完整套装", "Complete Set", 1);
        await InsertDomesticSetProductAsync("P-SET-OK", "SET-CODE-1", "HB-SET-OK-A");

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-set-ok",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-SET-OK" },
            }
        );

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);

        var setCode = await _db.Queryable<ProductSetCode>()
            .FirstAsync(item => item.ProductCode == "P-SET-OK");
        Assert.NotNull(setCode);
        Assert.Equal("SET-CODE-1", setCode.SetProductCode);

        var storeMultiCode = await _db.Queryable<StoreMultiCodeProduct>()
            .FirstAsync(item => item.ProductCode == "P-SET-OK");
        Assert.NotNull(storeMultiCode);
        Assert.Equal("SET-CODE-1", storeMultiCode.MultiCodeProductCode);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsRowsWithMissingEnglishNameAndSetChild()
    {
        await InsertContainerDetailAsync("D-MISSING-EN", "C001", "P-MISSING-EN", "普通商品", 1.2m, 3.4m);
        await InsertDomesticProductAsync("P-MISSING-EN", "HB-MISSING-EN", "缺英文", null, 0);
        await InsertContainerDetailAsync("D-SET-CHILD", "C001", "P-SET-CHILD", "套装子商品", 1.2m, 3.4m);
        await InsertDomesticProductAsync("P-SET-CHILD", "HB-SET-CHILD", "套装子", "Set Child", 0);

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-2",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-MISSING-EN", "D-SET-CHILD" },
            }
        );

        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(2, result.SkippedCount);
        Assert.Contains(result.Skipped, item => item.DetailHguid == "D-MISSING-EN" && item.ReasonCode == "MISSING_ENGLISH_NAME");
        Assert.Contains(result.Skipped, item => item.DetailHguid == "D-SET-CHILD" && item.ReasonCode == "MISSING_SET_RELATION");
    }

    [Fact]
    public async Task ExecuteAsync_UsesChineseNameWrittenByBatchUpdateDetails()
    {
        await InsertActiveStoreAsync("S001");
        await InsertContainerDetailAsync("D-NAME-SAVED", "C001", "P-NAME-SAVED", "普通商品", 1.2m, 3.4m);
        await InsertDomesticProductAsync("P-NAME-SAVED", "HB-NAME-SAVED", null, "Saved Belt", 0);
        var containerReactService = CreateContainerReactService();
        await containerReactService.BatchUpdateDetailsAsync(
            new List<UpdateContainerDetailDto>
            {
                new() { HGUID = "D-NAME-SAVED", 商品名称 = "保存后的皮带" },
            }
        );
        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-name-saved",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-NAME-SAVED" },
            }
        );

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.DoesNotContain(result.Skipped, item => item.ReasonCode == "MISSING_CHINESE_NAME");
        var domesticProduct = await _db.Queryable<DomesticProduct>().SingleAsync(p => p.ProductCode == "P-NAME-SAVED");
        Assert.Equal("保存后的皮带", domesticProduct.ProductName);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsRowsWithInvalidRetailPrice()
    {
        await InsertActiveStoreAsync("S001");
        await InsertContainerDetailAsync("D-INVALID-RETAIL", "C001", "P-INVALID-RETAIL", "普通商品", 1.2m, 0m);
        await InsertDomesticProductAsync("P-INVALID-RETAIL", "HB-INVALID-RETAIL", "无零售价商品", "No Retail Price", 0);

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-invalid-retail",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-INVALID-RETAIL" },
            }
        );

        Assert.Equal(0, result.CreatedCount);
        Assert.Contains(result.Skipped, item =>
            item.DetailHguid == "D-INVALID-RETAIL"
            && item.ReasonCode == "INVALID_OEM_PRICE"
            && item.Message == "零售价必须大于 0"
        );
    }

    [Fact]
    public async Task ExecuteAsync_SkipsDuplicateProductCodeAndSetWithoutRelation()
    {
        await _db.Insertable(new Product
        {
            UUID = "LP001",
            ProductCode = "P-EXISTS",
            ItemNumber = "HB-EXISTS",
            ProductName = "已存在",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await InsertContainerDetailAsync("D-EXISTS", "C001", "P-EXISTS", "普通商品", 1.2m, 3.4m);
        await InsertDomesticProductAsync("P-EXISTS", "HB-EXISTS", "已存在", "Exists", 0);
        await InsertContainerDetailAsync("D-SET", "C001", "P-SET", "套装商品", 1.2m, 3.4m);
        await InsertDomesticProductAsync("P-SET", "HB-SET", "套装", "Set Product", 1);

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-3",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-EXISTS", "D-SET" },
            }
        );

        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(2, result.SkippedCount);
        Assert.Contains(result.Skipped, item => item.DetailHguid == "D-EXISTS" && item.ReasonCode == "DUPLICATE_PRODUCT_CODE");
        Assert.Contains(result.Skipped, item => item.DetailHguid == "D-SET" && item.ReasonCode == "MISSING_SET_RELATION");
    }

    [Fact]
    public async Task ExecuteAsync_SkipsRowsWithDuplicateLocalItemNumber()
    {
        await _db.Insertable(new Product
        {
            UUID = "LP-DUP-ITEM",
            ProductCode = "P-OTHER",
            ItemNumber = "HB-DUP",
            ProductName = "同货号商品",
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await InsertContainerDetailAsync("D-DUP-ITEM", "C001", "P-DUP-ITEM", "普通商品", 1.2m, 3.4m);
        await InsertDomesticProductAsync("P-DUP-ITEM", "HB-DUP", "重复货号", "Duplicate Item", 0);

        var service = CreateService();

        var result = await service.ExecuteAsync(
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-dup-item",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D-DUP-ITEM" },
            }
        );

        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Contains(result.Skipped, item => item.DetailHguid == "D-DUP-ITEM" && item.ReasonCode == "DUPLICATE_ITEM_NUMBER");
        Assert.Equal(0, await _db.Queryable<WarehouseProduct>().Where(p => p.ProductCode == "P-DUP-ITEM").CountAsync());
    }

    [Fact]
    public async Task JobService_ReusesSameOperationContainerAndDetails()
    {
        var executor = new BlockingContainerProductCreationExecutor();
        var services = new ServiceCollection();
        services.AddSingleton<IContainerProductCreationExecutorService>(executor);
        var provider = services.BuildServiceProvider();
        var jobService = new ContainerProductCreationJobService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ContainerProductCreationJobService>.Instance
        );
        var request = new ContainerProductCreationJobRequestDto
        {
            OperationId = "op-reuse",
            ContainerGuid = "C001",
            DetailHguids = new List<string> { "D002", "D001" },
        };

        var first = await jobService.StartJobAsync("user-1", request);
        var second = await jobService.StartJobAsync(
            "user-1",
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-reuse",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D001", "D002" },
            }
        );
        executor.Release();
        await Task.Delay(50);

        Assert.Equal(first.JobId, second.JobId);
        Assert.True(second.IsDuplicateRequest);
    }

    [Fact]
    public async Task JobService_ReusesSameContainerAndDetailsWhenOperationIdChanges()
    {
        var executor = new BlockingContainerProductCreationExecutor();
        var services = new ServiceCollection();
        services.AddSingleton<IContainerProductCreationExecutorService>(executor);
        var provider = services.BuildServiceProvider();
        var jobService = new ContainerProductCreationJobService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ContainerProductCreationJobService>.Instance
        );

        var first = await jobService.StartJobAsync(
            "user-1",
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-reuse-a",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D002", "D001" },
            }
        );
        var second = await jobService.StartJobAsync(
            "user-1",
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-reuse-b",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D001", "D002" },
            }
        );
        executor.Release();
        await Task.Delay(50);

        Assert.Equal(first.JobId, second.JobId);
        Assert.True(second.IsDuplicateRequest);
    }

    [Fact]
    public async Task JobService_BlocksCrossUserJobResultAccess()
    {
        var executor = new BlockingContainerProductCreationExecutor();
        var services = new ServiceCollection();
        services.AddSingleton<IContainerProductCreationExecutorService>(executor);
        var provider = services.BuildServiceProvider();
        var jobService = new ContainerProductCreationJobService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ContainerProductCreationJobService>.Instance
        );

        var job = await jobService.StartJobAsync(
            "user-1",
            new ContainerProductCreationJobRequestDto
            {
                OperationId = "op-user-1",
                ContainerGuid = "C001",
                DetailHguids = new List<string> { "D001" },
            }
        );

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => jobService.GetJobAsync("user-2", job.JobId)
        );
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => jobService.StartJobAsync(
                "user-2",
                new ContainerProductCreationJobRequestDto
                {
                    OperationId = "op-user-2",
                    ContainerGuid = "C001",
                    DetailHguids = new List<string> { "D001" },
                }
            )
        );

        executor.Release();
        await Task.Delay(50);
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
        if (File.Exists(_dbPath))
        {
            SqliteTempFileCleanup.DeleteIfExists(_dbPath);
        }
    }

    private async Task InsertActiveStoreAsync(string storeCode)
    {
        await _db.Insertable(new Store
        {
            StoreCode = storeCode,
            StoreName = storeCode,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task InsertContainerDetailAsync(
        string detailCode,
        string containerCode,
        string productCode,
        string productType,
        decimal importPrice,
        decimal oemPrice
    )
    {
        await _db.Insertable(new ContainerDetail
        {
            DetailCode = detailCode,
            ContainerCode = containerCode,
            ProductCode = productCode,
            ProductType = productType,
            DomesticPrice = 10m,
            ImportPrice = importPrice,
            OEMPrice = oemPrice,
            UnitVolume = 0.12m,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task InsertDomesticProductAsync(
        string productCode,
        string itemNumber,
        string? productName,
        string? englishName,
        int productType
    )
    {
        await _db.Insertable(new DomesticProduct
        {
            ProductCode = productCode,
            HBProductNo = itemNumber,
            ProductName = productName,
            EnglishProductName = englishName,
            Barcode = $"BAR-{itemNumber}",
            ProductType = productType,
            ProductImage = $"/{itemNumber}.jpg",
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private async Task InsertDomesticSetProductAsync(
        string productCode,
        string setProductCode,
        string setProductNo
    )
    {
        await _db.Insertable(new DomesticSetProduct
        {
            SetProductCode = setProductCode,
            ProductCode = productCode,
            ProductNo = productCode,
            SetProductNo = setProductNo,
            SetBarcode = $"BAR-{setProductNo}",
            ImportPrice = 2.2m,
            OEMPrice = 5.5m,
            IsDeleted = false,
        }).ExecuteCommandAsync();
    }

    private ContainerProductCreationExecutorService CreateService()
    {
        var configuration = new ConfigurationBuilder().Build();
        var context = CreateSqlSugarContext(_db);
        var itemBarcodeService = new ItemBarcodeService(
            context,
            NullLogger<ItemBarcodeService>.Instance,
            configuration
        );
        var warehouseService = new ProductWarehouseReactService(
            context,
            CreateHqSqlSugarContext(),
            NullLogger<ProductWarehouseReactService>.Instance,
            configuration,
            itemBarcodeService,
            Mock.Of<IMapper>(),
            Mock.Of<IDataSyncFullService>()
        );

        return new ContainerProductCreationExecutorService(
            context,
            warehouseService,
            NullLogger<ContainerProductCreationExecutorService>.Instance
        );
    }

    private ContainerReactService CreateContainerReactService()
    {
        return new ContainerReactService(
            CreateSqlSugarContext(_db),
            CreateHqSqlSugarContext(),
            CreateHBSalesSqlSugarContext(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IMapper>(),
            NullLogger<ContainerReactService>.Instance,
            Mock.Of<IContainerHqSyncService>(),
            CreateTranslationServiceMock()
        );
    }

    private static ITranslationService CreateTranslationServiceMock()
    {
        var translationService = new Mock<ITranslationService>();
        translationService
            .Setup(service => service.ContainsChinese(It.IsAny<string>()))
            .Returns((string value) => value.Any(ch => ch >= '\u4e00' && ch <= '\u9fff'));
        translationService
            .Setup(service => service.BatchTranslateToEnglishAsync(It.IsAny<List<string>>()))
            .ReturnsAsync((List<string> values) => values.ToDictionary(value => value, value => value));
        return translationService.Object;
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(SqlSugarContext)
        );
        var dbField = typeof(SqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, db);
        return context;
    }

    private static HqSqlSugarContext CreateHqSqlSugarContext()
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(HqSqlSugarContext)
        );
        var dbField = typeof(HqSqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, new Mock<ISqlSugarClient>().Object);
        return context;
    }

    private static HBSalesSqlSugarContext CreateHBSalesSqlSugarContext()
    {
        var context = (HBSalesSqlSugarContext)RuntimeHelpers.GetUninitializedObject(
            typeof(HBSalesSqlSugarContext)
        );
        var dbField = typeof(HBSalesSqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, new SqlSugarScope(new ConnectionConfig
        {
            ConnectionString = "Data Source=:memory:",
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        }));
        return context;
    }

    private sealed class BlockingContainerProductCreationExecutor
        : IContainerProductCreationExecutorService
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ContainerProductCreationResultDto> ExecuteAsync(
            ContainerProductCreationJobRequestDto request,
            CancellationToken cancellationToken = default
        )
        {
            await _release.Task.WaitAsync(cancellationToken);
            return new ContainerProductCreationResultDto();
        }

        public void Release() => _release.TrySetResult();
    }
}
