using System.Reflection;
using System.Runtime.CompilerServices;
using AutoMapper;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Api.Mappings.Profiles;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ContainerReactServiceLocalSupplierCodeTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hbSalesDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hbSalesConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarScope _hbSalesDb;
    private readonly IMapper _mapper;

    public ContainerReactServiceLocalSupplierCodeTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hbSalesDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _hbSalesConnection = new SqliteConnection($"Data Source={_hbSalesDbPath}");
        _localConnection.Open();
        _hbSalesConnection.Open();
        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _hbSalesDb = new SqlSugarScope(CreateConnectionConfig(_hbSalesConnection.ConnectionString));

        _localDb.CodeFirst.InitTables(
            typeof(Container),
            typeof(ContainerDetail),
            typeof(DomesticProduct),
            typeof(Product),
            typeof(WarehouseProduct)
        );

        _mapper = new MapperConfiguration(
            cfg => cfg.AddProfile<ContainerMappingProfile>(),
            NullLoggerFactory.Instance
        ).CreateMapper();
    }

    [Fact]
    public async Task GetContainerProductsAsync_应返回本地供应商编码到明细和商品信息()
    {
        await SeedContainerGraphAsync("C-SUP-LIST", "D-SUP-LIST", "P-SUP-LIST", "SUP01");
        var service = CreateService();

        var result = await service.GetContainerProductsAsync("C-SUP-LIST");

        var detail = Assert.Single(result);
        Assert.Equal("SUP01", ReadLocalSupplierCode(detail));
        Assert.NotNull(detail.商品信息);
        Assert.Equal("SUP01", ReadLocalSupplierCode(detail.商品信息!));
    }

    [Fact]
    public async Task GetContainerDetailAsync_应通过映射返回本地供应商编码到明细和商品信息()
    {
        await SeedContainerGraphAsync("C-SUP-DETAIL", "D-SUP-DETAIL", "P-SUP-DETAIL", "SUP01");
        var service = CreateService();

        var result = await service.GetContainerDetailAsync("C-SUP-DETAIL");

        var container = Assert.IsType<ContainerMainDto>(result);
        var detail = Assert.Single(container.Details);
        Assert.Equal("SUP01", ReadLocalSupplierCode(detail));
        Assert.NotNull(detail.商品信息);
        Assert.Equal("SUP01", ReadLocalSupplierCode(detail.商品信息!));
    }

    public void Dispose()
    {
        _localDb.Dispose();
        _hbSalesDb.Dispose();
        _localConnection.Dispose();
        _hbSalesConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_localDbPath);
        SqliteTempFileCleanup.DeleteIfExists(_hbSalesDbPath);
    }

    private ContainerReactService CreateService()
    {
        return new ContainerReactService(
            CreateSqlSugarContext(_localDb),
            CreateHqSqlSugarContext(),
            CreateHBSalesSqlSugarContext(_hbSalesDb),
            new ConfigurationBuilder().Build(),
            _mapper,
            NullLogger<ContainerReactService>.Instance,
            Mock.Of<IContainerHqSyncService>(),
            Mock.Of<ITranslationService>()
        );
    }

    private async Task SeedContainerGraphAsync(
        string containerCode,
        string detailCode,
        string productCode,
        string localSupplierCode
    )
    {
        await _localDb.Insertable(
            new Container
            {
                ContainerCode = containerCode,
                ContainerNumber = $"NO-{containerCode}",
                Status = 1,
            }
        ).ExecuteCommandAsync();

        await _localDb.Insertable(
            new ContainerDetail
            {
                DetailCode = detailCode,
                ContainerCode = containerCode,
                ProductCode = productCode,
                ImportPrice = 3.21m,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        await _localDb.Insertable(
            new DomesticProduct
            {
                ProductCode = productCode,
                HBProductNo = "HB-001",
                ProductName = "测试商品",
                EnglishProductName = "Test Product",
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();

        await _localDb.Insertable(
            new Product
            {
                ProductCode = productCode,
                ProductName = "本地测试商品",
                LocalSupplierCode = localSupplierCode,
                IsActive = true,
            }
        ).ExecuteCommandAsync();
    }

    private static string? ReadLocalSupplierCode(object dto)
    {
        // 先用反射卡住 DTO 契约，确保测试能在新增属性前先失败。
        var property = dto.GetType().GetProperty("LocalSupplierCode", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(property);
        return property!.GetValue(dto) as string;
    }

    private static ConnectionConfig CreateConnectionConfig(string connectionString) =>
        new()
        {
            ConnectionString = connectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = false,
            InitKeyType = InitKeyType.Attribute,
        };

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

    private static HBSalesSqlSugarContext CreateHBSalesSqlSugarContext(SqlSugarScope db)
    {
        var context = (HBSalesSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HBSalesSqlSugarContext));
        var dbField = typeof(HBSalesSqlSugarContext).GetField(
            "_db",
            BindingFlags.Instance | BindingFlags.NonPublic
        );
        dbField!.SetValue(context, db);
        return context;
    }
}
