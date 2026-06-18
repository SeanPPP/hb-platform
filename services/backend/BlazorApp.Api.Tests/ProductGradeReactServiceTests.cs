using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Mappings.Profiles.React;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ProductGradeReactServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnection _sqliteConnection;
    private readonly SqlSugarClient _db;

    public ProductGradeReactServiceTests()
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
            typeof(ProductGrade),
            typeof(DomesticProduct),
            typeof(ChinaSupplier),
            typeof(WarehouseProduct),
            typeof(Product)
        );
    }

    [Fact]
    public async Task GetProductGradesByProductCodesAsync_EmptyCodes_ReturnsEmptyList()
    {
        var service = CreateService();

        var result = await service.GetProductGradesByProductCodesAsync(new List<string>());

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task GetProductGradesByProductCodesAsync_ReturnsFullExportFields()
    {
        const string productCode = "product-1";
        const string supplierCode = "SUP001";

        await _db.Insertable(new ChinaSupplier
        {
            Guid = "supplier-guid-1",
            SupplierCode = supplierCode,
            SupplierName = "义乌供应商",
        }).ExecuteCommandAsync();

        await _db.Insertable(new DomesticProduct
        {
            ProductCode = productCode,
            SupplierCode = supplierCode,
            HBProductNo = "HB100-001",
            Barcode = "930000000001",
            ProductName = "测试商品",
            ProductImage = "https://img.example.com/HB100-001.jpg",
            DomesticPrice = 12.34m,
        }).ExecuteCommandAsync();

        await _db.Insertable(new WarehouseProduct
        {
            ProductCode = productCode,
            ImportPrice = 2.45m,
            OEMPrice = 4.56m,
            IsActive = true,
        }).ExecuteCommandAsync();

        await _db.Insertable(new Product
        {
            UUID = productCode,
            ProductCode = productCode,
            ItemNumber = "HB100-001",
            Barcode = "930000000001",
            ProductName = "测试商品",
            RetailPrice = 6.78m,
        }).ExecuteCommandAsync();

        await _db.Insertable(new ProductGrade
        {
            Id = "grade-1",
            ProductCode = productCode,
            Grade = "A",
            CreatedBy = "tester",
        }).ExecuteCommandAsync();

        var service = CreateService();

        var result = await service.GetProductGradesByProductCodesAsync(
            new List<string> { productCode, productCode, " " }
        );

        Assert.True(result.Success);
        var item = Assert.Single(result.Data!);
        Assert.Equal(productCode, item.ProductCode);
        Assert.Equal("A", item.Grade);
        Assert.Equal(supplierCode, item.SupplierCode);
        Assert.Equal("义乌供应商", item.SupplierName);
        Assert.Equal("HB100-001", item.HbProductNo);
        Assert.Equal("930000000001", item.Barcode);
        Assert.Equal("测试商品", item.ProductName);
        Assert.Equal("https://img.example.com/HB100-001.jpg", item.ProductImage);
        Assert.Equal(12.34m, item.DomesticPrice);
        Assert.Equal(2.45m, item.ImportPrice);
        Assert.Equal(4.56m, item.OemPrice);
        Assert.Equal(6.78m, item.RetailPrice);
        Assert.Equal("tester", item.CreatedBy);
    }

    [Fact]
    public async Task GetProductGradesAsync_FiltersByColumnFieldsBeforePaging_ReturnsMatchingTotal()
    {
        await SeedProductGradeAsync(
            productCode: "product-a",
            supplierCode: "SUP001",
            supplierName: "一号供应商",
            hbProductNo: "HB-A-001",
            productName: "红色笔",
            grade: "A",
            domesticPrice: 10m,
            importPrice: 2m,
            oemPrice: 4m,
            createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        );
        await SeedProductGradeAsync(
            productCode: "product-b",
            supplierCode: "SUP002",
            supplierName: "二号供应商",
            hbProductNo: "HB-B-002",
            productName: "蓝色笔",
            grade: "B",
            domesticPrice: 20m,
            importPrice: 5m,
            oemPrice: 8m,
            createdAt: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        );

        var service = CreateService();

        var result = await service.GetProductGradesAsync(new ProductGradeListQueryDto
        {
            Page = 1,
            PageSize = 10,
            Grade = "B",
            SupplierCode = "SUP002",
            HbProductNo = "B-002",
            DomesticPriceMin = 15m,
            DomesticPriceMax = 25m,
        });

        Assert.True(result.Success);
        var data = result.Data;
        Assert.NotNull(data);
        Assert.Equal(1, data!.Total);
        Assert.NotNull(data.Items);
        var item = Assert.Single(data.Items!);
        Assert.Equal("product-b", item.ProductCode);
        Assert.Equal("HB-B-002", item.HbProductNo);
    }

    [Fact]
    public async Task GetProductGradesAsync_FiltersByImportAndOemPriceRangeBeforePaging()
    {
        await SeedProductGradeAsync(
            productCode: "product-low",
            supplierCode: "SUP001",
            supplierName: "一号供应商",
            hbProductNo: "HB-L-001",
            productName: "低价商品",
            grade: "A",
            domesticPrice: 5m,
            importPrice: 1m,
            oemPrice: 2m,
            createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        );
        await SeedProductGradeAsync(
            productCode: "product-mid",
            supplierCode: "SUP001",
            supplierName: "一号供应商",
            hbProductNo: "HB-M-001",
            productName: "中价商品",
            grade: "A",
            domesticPrice: 9m,
            importPrice: 3m,
            oemPrice: 6m,
            createdAt: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        );
        await SeedProductGradeAsync(
            productCode: "product-high",
            supplierCode: "SUP001",
            supplierName: "一号供应商",
            hbProductNo: "HB-H-001",
            productName: "高价商品",
            grade: "A",
            domesticPrice: 15m,
            importPrice: 8m,
            oemPrice: 12m,
            createdAt: new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)
        );

        var service = CreateService();

        var result = await service.GetProductGradesAsync(new ProductGradeListQueryDto
        {
            Page = 1,
            PageSize = 10,
            ImportPriceMin = 2m,
            ImportPriceMax = 5m,
            OemPriceMin = 5m,
            OemPriceMax = 7m,
        });

        Assert.True(result.Success);
        var data = result.Data;
        Assert.NotNull(data);
        Assert.Equal(1, data!.Total);
        Assert.NotNull(data.Items);
        Assert.Equal("product-mid", Assert.Single(data.Items!).ProductCode);
    }

    [Fact]
    public async Task GetProductGradesAsync_SortsByImportPriceBeforePaging()
    {
        await SeedProductGradeAsync(
            productCode: "product-expensive",
            supplierCode: "SUP001",
            supplierName: "一号供应商",
            hbProductNo: "HB-002",
            productName: "高进口价",
            grade: "A",
            domesticPrice: 20m,
            importPrice: 9m,
            oemPrice: 12m,
            createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        );
        await SeedProductGradeAsync(
            productCode: "product-cheap",
            supplierCode: "SUP002",
            supplierName: "二号供应商",
            hbProductNo: "HB-001",
            productName: "低进口价",
            grade: "A",
            domesticPrice: 10m,
            importPrice: 1m,
            oemPrice: 3m,
            createdAt: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        );

        var service = CreateService();

        var result = await service.GetProductGradesAsync(new ProductGradeListQueryDto
        {
            Page = 1,
            PageSize = 10,
            SortField = "importPrice",
            SortDirection = "asc",
        });

        Assert.True(result.Success);
        var data = result.Data;
        Assert.NotNull(data);
        Assert.NotNull(data!.Items);
        Assert.Equal(new[] { "product-cheap", "product-expensive" }, data.Items!.Select(item => item.ProductCode));
    }

    [Fact]
    public async Task GetProductGradesAsync_AppliesFilterAndSortBeforeSkipTake()
    {
        await SeedProductGradeAsync(
            productCode: "product-c",
            supplierCode: "SUP001",
            supplierName: "一号供应商",
            hbProductNo: "HB-C",
            productName: "C 商品",
            grade: "A",
            domesticPrice: 30m,
            importPrice: 3m,
            oemPrice: 6m,
            createdAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        );
        await SeedProductGradeAsync(
            productCode: "product-a",
            supplierCode: "SUP001",
            supplierName: "一号供应商",
            hbProductNo: "HB-A",
            productName: "A 商品",
            grade: "A",
            domesticPrice: 10m,
            importPrice: 1m,
            oemPrice: 2m,
            createdAt: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        );
        await SeedProductGradeAsync(
            productCode: "product-b",
            supplierCode: "SUP001",
            supplierName: "一号供应商",
            hbProductNo: "HB-B",
            productName: "B 商品",
            grade: "A",
            domesticPrice: 20m,
            importPrice: 2m,
            oemPrice: 4m,
            createdAt: new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc)
        );
        await SeedProductGradeAsync(
            productCode: "product-d",
            supplierCode: "SUP001",
            supplierName: "一号供应商",
            hbProductNo: "HB-D",
            productName: "D 商品",
            grade: "B",
            domesticPrice: 40m,
            importPrice: 4m,
            oemPrice: 8m,
            createdAt: new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc)
        );

        var service = CreateService();

        var result = await service.GetProductGradesAsync(new ProductGradeListQueryDto
        {
            Page = 2,
            PageSize = 1,
            Grade = "A",
            SortField = "domesticPrice",
            SortDirection = "asc",
        });

        Assert.True(result.Success);
        var data = result.Data;
        Assert.NotNull(data);
        Assert.Equal(3, data!.Total);
        Assert.NotNull(data.Items);
        var item = Assert.Single(data.Items!);
        Assert.Equal("product-b", item.ProductCode);
        Assert.Equal(2, data.Page);
        Assert.Equal(1, data.PageSize);
    }

    private ProductGradeReactService CreateService()
    {
        return new ProductGradeReactService(
            CreateSqlSugarContext(_db),
            CreateHqSqlSugarContext(),
            CreateMapper(),
            NullLogger<ProductGradeReactService>.Instance
        );
    }

    private async Task SeedProductGradeAsync(
        string productCode,
        string supplierCode,
        string supplierName,
        string hbProductNo,
        string productName,
        string grade,
        decimal domesticPrice,
        decimal importPrice,
        decimal oemPrice,
        DateTime createdAt,
        bool isDeleted = false
    )
    {
        var supplierExists = await _db.Queryable<ChinaSupplier>()
            .AnyAsync(s => s.SupplierCode == supplierCode);
        if (!supplierExists)
        {
            await _db.Insertable(new ChinaSupplier
            {
                Guid = $"{supplierCode}-guid",
                SupplierCode = supplierCode,
                SupplierName = supplierName,
            }).ExecuteCommandAsync();
        }

        await _db.Insertable(new DomesticProduct
        {
            ProductCode = productCode,
            SupplierCode = supplierCode,
            HBProductNo = hbProductNo,
            ProductName = productName,
            ProductImage = $"https://img.example.com/{hbProductNo}.jpg",
            DomesticPrice = domesticPrice,
        }).ExecuteCommandAsync();

        await _db.Insertable(new WarehouseProduct
        {
            ProductCode = productCode,
            ImportPrice = importPrice,
            OEMPrice = oemPrice,
            IsActive = true,
        }).ExecuteCommandAsync();

        await _db.Insertable(new Product
        {
            UUID = productCode,
            ProductCode = productCode,
            ItemNumber = hbProductNo,
            ProductName = productName,
            RetailPrice = oemPrice,
        }).ExecuteCommandAsync();

        await _db.Insertable(new ProductGrade
        {
            Id = $"{productCode}-grade",
            ProductCode = productCode,
            Grade = grade,
            CreatedAt = createdAt,
            IsDeleted = isDeleted,
            CreatedBy = "tester",
        }).ExecuteCommandAsync();
    }

    private static IMapper CreateMapper()
    {
        return new MapperConfiguration(
            cfg => cfg.AddProfile<ReactProductGradeProfile>(),
            NullLoggerFactory.Instance
        ).CreateMapper();
    }

    private static SqlSugarContext CreateSqlSugarContext(ISqlSugarClient db)
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        var dbField = typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, db);
        return context;
    }

    private static HqSqlSugarContext CreateHqSqlSugarContext()
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HqSqlSugarContext));
        var dbField = typeof(HqSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, new Mock<ISqlSugarClient>().Object);
        return context;
    }

    public void Dispose()
    {
        _db.Dispose();
        _sqliteConnection.Dispose();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
