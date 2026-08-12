using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class WarehouseStorePriceSyncServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public WarehouseStorePriceSyncServiceTests()
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
            typeof(Store),
            typeof(Product),
            typeof(WarehouseProduct),
            typeof(StoreRetailPrice)
        );
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task 执行同步_商品范围不符合互斥规则时拒绝且零写入(
        bool applyToAllProducts,
        bool includeProductCode
    )
    {
        await SeedStoreAsync("S01", "一店");
        var service = CreateService(Mock.Of<IProductHqSyncService>());

        var response = await service.ExecuteAsync(
            new WarehouseStorePriceSyncRequestDto
            {
                ApplyToAllProducts = applyToAllProducts,
                ProductCodes = includeProductCode ? ["P01"] : [],
                TargetStoreCodes = ["S01"],
            },
            "admin"
        );

        Assert.False(response.Success);
        Assert.Equal("INVALID_PRODUCT_SCOPE", response.ErrorCode);
        Assert.Equal(0, await _db.Queryable<StoreRetailPrice>().CountAsync());
    }

    [Fact]
    public async Task 全量同步_包含下架排除删除且缺价跳过并接受零价格()
    {
        await SeedStoreAsync("S01", "一店");
        await SeedProductAsync("P01", true, false, 0m, 0m);
        await SeedProductAsync("P02", false, false, 2m, 3m);
        await SeedProductAsync("P03", true, false, null, 5m);
        await SeedProductAsync("P04", true, true, 9m, 9m);
        var hqService = new Mock<IProductHqSyncService>(MockBehavior.Strict);
        var service = CreateService(hqService.Object);

        var response = await service.ExecuteAsync(
            new WarehouseStorePriceSyncRequestDto
            {
                ApplyToAllProducts = true,
                TargetStoreCodes = [" s01 ", "S01"],
                SyncToHq = false,
            },
            "price-admin"
        );

        Assert.True(response.Success, response.Message);
        Assert.NotNull(response.Data);
        Assert.Equal(3, response.Data!.RequestedProductCount);
        Assert.Equal(2, response.Data.EligibleProductCount);
        Assert.Equal(1, response.Data.SkippedProductCount);
        Assert.Equal(1, response.Data.TargetStoreCount);
        Assert.Equal(2, response.Data.LocalCreatedCount);
        Assert.Contains(
            response.Data.Errors,
            error => error.ProductCode == "P03" && error.Code == "MISSING_PRICE"
        );
        var missingPrice = response.Data.Errors.Single(error => error.ProductCode == "P03");
        Assert.Contains("ImportPrice", missingPrice.Message);
        Assert.DoesNotContain("OEMPrice", missingPrice.Message);

        var rows = await _db.Queryable<StoreRetailPrice>().OrderBy(row => row.ProductCode).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(0m, rows[0].PurchasePrice);
        Assert.Equal(0m, rows[0].StoreRetailPriceValue);
        Assert.False(rows[1].IsActive);
        Assert.DoesNotContain(rows, row => row.ProductCode == "P04");
        hqService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task 获取全量商品数_与执行范围一致且包含无Product主档的下架商品()
    {
        await _db.Insertable(new[]
        {
            new WarehouseProduct
            {
                ProductCode = "ORPHAN-INACTIVE",
                ImportPrice = 1m,
                OEMPrice = 2m,
                IsActive = false,
                IsDeleted = false,
            },
            new WarehouseProduct
            {
                ProductCode = "DELETED",
                ImportPrice = 1m,
                OEMPrice = 2m,
                IsActive = true,
                IsDeleted = true,
            },
        }).ExecuteCommandAsync();
        var service = CreateService(Mock.Of<IProductHqSyncService>());

        var count = await service.GetAllProductCountAsync();

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task 指定同步_未找到商品计为失败但不重复计入缺价跳过()
    {
        await SeedStoreAsync("S01", "一店");
        await SeedProductAsync("P01", true, false, 1m, 2m);
        var service = CreateService(Mock.Of<IProductHqSyncService>());

        var response = await service.ExecuteAsync(
            new WarehouseStorePriceSyncRequestDto
            {
                ProductCodes = ["P01", "P404"],
                TargetStoreCodes = ["S01"],
            },
            "admin"
        );

        Assert.True(response.Success, response.Message);
        Assert.Equal(2, response.Data?.RequestedProductCount);
        Assert.Equal(1, response.Data?.EligibleProductCount);
        Assert.Equal(0, response.Data?.SkippedProductCount);
        Assert.Contains(
            response.Data!.Errors,
            error => error.ProductCode == "P404" && error.Code == "PRODUCT_NOT_FOUND"
        );
    }

    [Fact]
    public async Task 本地同步_既有供应商不同时仍更新同一行且只改固定字段与审计()
    {
        await SeedStoreAsync("S01", "一店");
        await SeedStoreAsync("S02", "二店");
        await SeedProductAsync("P01", false, false, 1.25m, 2.5m, "SUP01", true);
        var originalCreatedAt = DateTime.UtcNow.AddDays(-10);
        await _db.Insertable(new StoreRetailPrice
        {
            UUID = "existing-price",
            StoreCode = "S01",
            ProductCode = "P01",
            StoreProductCode = "KEEP-CODE",
            SupplierCode = "LEGACY-SUP",
            PurchasePrice = 99m,
            StoreRetailPriceValue = 88m,
            DiscountRate = 0.5m,
            IsAutoPricing = true,
            IsSpecialProduct = false,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = originalCreatedAt,
            CreatedBy = "creator",
            UpdatedAt = originalCreatedAt,
            UpdatedBy = "old-admin",
        }).ExecuteCommandAsync();
        ConfigureBackgroundAuditAop(_db);
        var service = CreateService(Mock.Of<IProductHqSyncService>());

        var response = await service.ExecuteAsync(
            new WarehouseStorePriceSyncRequestDto
            {
                ProductCodes = [" p01 "],
                TargetStoreCodes = ["s01", " S02 "],
            },
            " price-admin "
        );

        Assert.True(response.Success, response.Message);
        Assert.Equal(1, response.Data?.LocalUpdatedCount);
        Assert.Equal(1, response.Data?.LocalCreatedCount);

        var existing = await _db.Queryable<StoreRetailPrice>()
            .SingleAsync(row => row.UUID == "existing-price");
        Assert.Equal(1.25m, existing.PurchasePrice);
        Assert.Equal(2.5m, existing.StoreRetailPriceValue);
        Assert.Equal(0m, existing.DiscountRate);
        Assert.False(existing.IsAutoPricing);
        Assert.Equal("price-admin", existing.UpdatedBy);
        Assert.Equal("KEEP-CODE", existing.StoreProductCode);
        Assert.Equal("LEGACY-SUP", existing.SupplierCode);
        Assert.False(existing.IsSpecialProduct);
        Assert.True(existing.IsActive);
        Assert.Equal("creator", existing.CreatedBy);
        Assert.Equal(originalCreatedAt, existing.CreatedAt);
        Assert.Equal(1, await _db.Queryable<StoreRetailPrice>()
            .Where(row => row.StoreCode == "S01" && row.ProductCode == "P01")
            .CountAsync());

        var created = await _db.Queryable<StoreRetailPrice>()
            .SingleAsync(row => row.StoreCode == "S02" && row.ProductCode == "P01");
        Assert.Equal("S02P01", created.StoreProductCode);
        Assert.Equal("SUP01", created.SupplierCode);
        Assert.True(created.IsSpecialProduct);
        Assert.False(created.IsActive);
        Assert.Equal("price-admin", created.CreatedBy);
        Assert.Equal("price-admin", created.UpdatedBy);
        Assert.False(SqlSugarAuditScope.ShouldPreserveExplicitAuditFields);
    }

    [Fact]
    public async Task 本地同步_新增失败时回滚同一事务内已执行的更新且不运行HQ()
    {
        await SeedStoreAsync("S01", "一店");
        await SeedProductAsync("P01", true, false, 1m, 2m);
        await SeedProductAsync("P02", true, false, 3m, 4m);
        await _db.Insertable(new StoreRetailPrice
        {
            UUID = "existing-price-for-rollback",
            StoreCode = "S01",
            ProductCode = "P01",
            PurchasePrice = 99m,
            StoreRetailPriceValue = 88m,
            DiscountRate = 0.5m,
            IsAutoPricing = true,
            IsActive = true,
            IsDeleted = false,
        }).ExecuteCommandAsync();
        await _db.Ado.ExecuteCommandAsync(
            """
            CREATE TRIGGER fail_store_price_insert
            BEFORE INSERT ON StoreRetailPrice
            WHEN NEW.ProductCode = 'P02'
            BEGIN
                SELECT RAISE(ABORT, 'forced insert failure');
            END;
            """
        );
        var hqService = new Mock<IProductHqSyncService>(MockBehavior.Strict);
        var service = CreateService(hqService.Object);

        var response = await service.ExecuteAsync(
            new WarehouseStorePriceSyncRequestDto
            {
                ProductCodes = ["P01", "P02"],
                TargetStoreCodes = ["S01"],
            },
            "admin"
        );

        Assert.False(response.Success);
        Assert.Equal("LOCAL_WRITE_FAILED", response.ErrorCode);
        var failureResult = Assert.IsType<WarehouseStorePriceSyncResultDto>(response.Details);
        var localError = Assert.Single(failureResult.Errors);
        Assert.Equal("本地分店价格写入失败", localError.Message);
        Assert.DoesNotContain("forced insert failure", localError.Message);
        var existing = await _db.Queryable<StoreRetailPrice>()
            .SingleAsync(row => row.UUID == "existing-price-for-rollback");
        Assert.Equal(99m, existing.PurchasePrice);
        Assert.Equal(88m, existing.StoreRetailPriceValue);
        Assert.Equal(0.5m, existing.DiscountRate);
        Assert.True(existing.IsAutoPricing);
        Assert.Equal(0, await _db.Queryable<StoreRetailPrice>()
            .Where(row => row.ProductCode == "P02")
            .CountAsync());
        hqService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task 执行同步_全部商品缺价时失败且不写本地或HQ()
    {
        await SeedStoreAsync("S01", "一店");
        await SeedProductAsync("P01", true, false, null, 2m);
        await SeedProductAsync("P02", true, false, 1m, null);
        var hqService = new Mock<IProductHqSyncService>(MockBehavior.Strict);
        var service = CreateService(hqService.Object);

        var response = await service.ExecuteAsync(
            new WarehouseStorePriceSyncRequestDto
            {
                ProductCodes = ["P01", "P02"],
                TargetStoreCodes = ["S01"],
                SyncToHq = true,
            },
            "admin"
        );

        Assert.False(response.Success);
        Assert.Equal("NO_ELIGIBLE_PRODUCTS", response.ErrorCode);
        var result = Assert.IsType<WarehouseStorePriceSyncResultDto>(response.Details);
        Assert.Equal(0, result.EligibleProductCount);
        Assert.Equal(2, result.SkippedProductCount);
        Assert.False(result.LocalCommitted);
        Assert.Equal(0, await _db.Queryable<StoreRetailPrice>().CountAsync());
        hqService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task 执行同步_HQ关闭时绝不触达HQ服务()
    {
        await SeedStoreAsync("S01", "一店");
        await SeedProductAsync("P01", true, false, 1m, 2m);
        var hqService = new Mock<IProductHqSyncService>(MockBehavior.Strict);
        var service = CreateService(hqService.Object);

        var response = await service.ExecuteAsync(
            new WarehouseStorePriceSyncRequestDto
            {
                ProductCodes = ["P01"],
                TargetStoreCodes = ["S01"],
                SyncToHq = false,
            },
            "admin"
        );

        Assert.True(response.Success, response.Message);
        Assert.True(response.Data?.LocalCommitted);
        Assert.Null(response.Data?.HqSucceeded);
        hqService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task 执行同步_HQ分店预校验失败时本地零写入()
    {
        await SeedStoreAsync("S01", "一店");
        await SeedProductAsync("P01", true, false, 1m, 2m);
        var hqService = new Mock<IProductHqSyncService>();
        hqService
            .Setup(service => service.ValidateWarehouseStorePriceTargetsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(ApiResponse<WarehouseStorePriceHqValidationResultDto>.Error(
                "HQ缺少分店 S01",
                "HQ_TARGET_STORE_NOT_FOUND",
                new WarehouseStorePriceHqValidationResultDto
                {
                    Errors =
                    [
                        new WarehouseStorePriceSyncErrorDto
                        {
                            Stage = "HqValidation",
                            StoreCode = "S01",
                            Code = "HQ_TARGET_STORE_NOT_FOUND",
                            Message = "HQ缺少分店 S01",
                        },
                    ],
                }
            ));
        var service = CreateService(hqService.Object);

        var response = await service.ExecuteAsync(
            new WarehouseStorePriceSyncRequestDto
            {
                ProductCodes = ["P01"],
                TargetStoreCodes = ["S01"],
                SyncToHq = true,
            },
            "admin"
        );

        Assert.False(response.Success);
        Assert.Equal("HQ_TARGET_STORE_NOT_FOUND", response.ErrorCode);
        var result = Assert.IsType<WarehouseStorePriceSyncResultDto>(response.Details);
        Assert.False(result.LocalCommitted);
        Assert.Equal(0, await _db.Queryable<StoreRetailPrice>().CountAsync());
        hqService.Verify(service => service.SyncWarehouseStorePricesAsync(
            It.IsAny<WarehouseStorePriceHqSyncRequestDto>(),
            It.IsAny<CancellationToken>()
        ), Times.Never);
    }

    [Fact]
    public async Task 执行同步_HQ运行失败时保留本地并返回部分成功依据()
    {
        await SeedStoreAsync("S01", "一店");
        await SeedProductAsync("P01", true, false, 1m, 2m);
        var hqService = new Mock<IProductHqSyncService>();
        hqService
            .Setup(service => service.ValidateWarehouseStorePriceTargetsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(ApiResponse<WarehouseStorePriceHqValidationResultDto>.OK(
                new WarehouseStorePriceHqValidationResultDto
                {
                    CanonicalTargetStoreCodes = ["S01"],
                }
            ));
        hqService
            .Setup(service => service.SyncWarehouseStorePricesAsync(
                It.IsAny<WarehouseStorePriceHqSyncRequestDto>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(ApiResponse<WarehouseStorePriceHqSyncResultDto>.Error(
                "HQ写入失败",
                "WAREHOUSE_STORE_PRICE_HQ_SYNC_FAILED",
                new WarehouseStorePriceHqSyncResultDto
                {
                    Errors =
                    [
                        new WarehouseStorePriceSyncErrorDto
                        {
                            Stage = "HqWrite",
                            Code = "WAREHOUSE_STORE_PRICE_HQ_SYNC_FAILED",
                            Message = "HQ写入失败",
                        },
                    ],
                }
            ));
        var service = CreateService(hqService.Object);

        var response = await service.ExecuteAsync(
            new WarehouseStorePriceSyncRequestDto
            {
                ProductCodes = ["P01"],
                TargetStoreCodes = ["S01"],
                SyncToHq = true,
            },
            "admin"
        );

        Assert.False(response.Success);
        var result = Assert.IsType<WarehouseStorePriceSyncResultDto>(response.Details);
        Assert.True(result.LocalCommitted);
        Assert.False(result.HqSucceeded);
        Assert.Equal(1, result.LocalCreatedCount);
        Assert.Equal(1, await _db.Queryable<StoreRetailPrice>().CountAsync());
        Assert.Contains(result.Errors, error => error.Stage == "HqWrite");
    }

    [Fact]
    public async Task 执行同步_本地提交后HQ抛取消异常仍保留部分成功依据()
    {
        await SeedStoreAsync("S01", "一店");
        await SeedProductAsync("P01", true, false, 1m, 2m);
        var hqService = new Mock<IProductHqSyncService>();
        hqService
            .Setup(service => service.ValidateWarehouseStorePriceTargetsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(ApiResponse<WarehouseStorePriceHqValidationResultDto>.OK(
                new WarehouseStorePriceHqValidationResultDto
                {
                    CanonicalTargetStoreCodes = ["S01"],
                }
            ));
        hqService
            .Setup(service => service.SyncWarehouseStorePricesAsync(
                It.IsAny<WarehouseStorePriceHqSyncRequestDto>(),
                It.IsAny<CancellationToken>()
            ))
            .ThrowsAsync(new OperationCanceledException("HQ阶段取消"));
        var service = CreateService(hqService.Object);

        var response = await service.ExecuteAsync(
            new WarehouseStorePriceSyncRequestDto
            {
                ProductCodes = ["P01"],
                TargetStoreCodes = ["S01"],
                SyncToHq = true,
            },
            "admin"
        );

        Assert.False(response.Success);
        Assert.Equal("HQ_WRITE_CANCELLED", response.ErrorCode);
        var result = Assert.IsType<WarehouseStorePriceSyncResultDto>(response.Details);
        Assert.True(result.LocalCommitted);
        Assert.False(result.HqSucceeded);
        Assert.Equal(1, result.LocalCreatedCount);
        Assert.Equal(1, await _db.Queryable<StoreRetailPrice>().CountAsync());
        Assert.Contains(
            result.Errors,
            error => error.Stage == "HqWrite" && error.Code == "HQ_WRITE_CANCELLED"
        );
    }

    [Fact]
    public async Task 执行同步_本地提交后HQ抛运行异常仍保留部分成功依据()
    {
        await SeedStoreAsync("S01", "一店");
        await SeedProductAsync("P01", true, false, 1m, 2m);
        var hqService = new Mock<IProductHqSyncService>();
        hqService
            .Setup(service => service.ValidateWarehouseStorePriceTargetsAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<CancellationToken>()
            ))
            .ReturnsAsync(ApiResponse<WarehouseStorePriceHqValidationResultDto>.OK(
                new WarehouseStorePriceHqValidationResultDto
                {
                    CanonicalTargetStoreCodes = ["S01"],
                }
            ));
        hqService
            .Setup(service => service.SyncWarehouseStorePricesAsync(
                It.IsAny<WarehouseStorePriceHqSyncRequestDto>(),
                It.IsAny<CancellationToken>()
            ))
            .ThrowsAsync(new InvalidOperationException("HQ连接中断"));
        var service = CreateService(hqService.Object);

        var response = await service.ExecuteAsync(
            new WarehouseStorePriceSyncRequestDto
            {
                ProductCodes = ["P01"],
                TargetStoreCodes = ["S01"],
                SyncToHq = true,
            },
            "admin"
        );

        Assert.False(response.Success);
        Assert.Equal("HQ_WRITE_EXCEPTION", response.ErrorCode);
        var result = Assert.IsType<WarehouseStorePriceSyncResultDto>(response.Details);
        Assert.True(result.LocalCommitted);
        Assert.False(result.HqSucceeded);
        Assert.Equal(1, result.LocalCreatedCount);
        Assert.Equal(1, await _db.Queryable<StoreRetailPrice>().CountAsync());
        Assert.Contains(
            result.Errors,
            error => error.Stage == "HqWrite" && error.Code == "HQ_WRITE_EXCEPTION"
        );
    }

    [Fact]
    public async Task 获取目标分店_只返回本地启用未删除分店并按编码去重()
    {
        await SeedStoreAsync(" s01 ", "一店");
        await SeedStoreAsync("S01", "重复一店");
        await SeedStoreAsync("S02", "停用店", isActive: false);
        await SeedStoreAsync("S03", "删除店", isDeleted: true);
        var hqService = new Mock<IProductHqSyncService>(MockBehavior.Strict);
        var service = CreateService(hqService.Object);

        var stores = await service.GetTargetStoresAsync();

        var store = Assert.Single(stores);
        Assert.Equal("S01", store.StoreCode, ignoreCase: true);
        hqService.VerifyNoOtherCalls();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    private WarehouseStorePriceSyncService CreateService(IProductHqSyncService hqService)
    {
        return new WarehouseStorePriceSyncService(
            CreateSqlSugarContext(_db),
            hqService,
            NullLogger<WarehouseStorePriceSyncService>.Instance
        );
    }

    private async Task SeedStoreAsync(
        string storeCode,
        string storeName,
        bool isActive = true,
        bool isDeleted = false
    )
    {
        await _db.Insertable(new Store
        {
            StoreGUID = Guid.NewGuid().ToString("N"),
            StoreCode = storeCode,
            StoreName = storeName,
            IsActive = isActive,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
    }

    private async Task SeedProductAsync(
        string productCode,
        bool isActive,
        bool isDeleted,
        decimal? importPrice,
        decimal? oemPrice,
        string supplierCode = "200",
        bool isSpecialProduct = false
    )
    {
        await _db.Insertable(new Product
        {
            UUID = $"product-{productCode}",
            ProductCode = productCode,
            ProductName = productCode,
            LocalSupplierCode = supplierCode,
            IsActive = true,
            IsSpecialProduct = isSpecialProduct,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
        await _db.Insertable(new WarehouseProduct
        {
            ProductCode = productCode,
            ImportPrice = importPrice,
            OEMPrice = oemPrice,
            IsActive = isActive,
            IsDeleted = isDeleted,
        }).ExecuteCommandAsync();
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        var field = typeof(SqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        field!.SetValue(context, db);
        return context;
    }

    private static void ConfigureBackgroundAuditAop(SqlSugarClient db)
    {
        db.Aop.DataExecuting = (oldValue, entityInfo) =>
        {
            if (entityInfo.EntityValue is not BaseEntity)
            {
                return;
            }

            if (
                SqlSugarAuditScope.ShouldPreserveExplicitAuditFields
                && entityInfo.PropertyName is "CreatedAt" or "UpdatedAt" or "CreatedBy" or "UpdatedBy"
            )
            {
                return;
            }

            if (
                entityInfo.OperationType == DataFilterType.UpdateByObject
                && entityInfo.PropertyName == nameof(BaseEntity.UpdatedBy)
            )
            {
                // 后台 scope 没有 HttpContext 时，生产 AOP 会取得 System。
                entityInfo.SetValue("System");
            }
            if (
                entityInfo.OperationType == DataFilterType.InsertByObject
                && entityInfo.PropertyName is nameof(BaseEntity.CreatedBy) or nameof(BaseEntity.UpdatedBy)
                && string.IsNullOrEmpty((string?)oldValue)
            )
            {
                entityInfo.SetValue("System");
            }
        };
    }
}
