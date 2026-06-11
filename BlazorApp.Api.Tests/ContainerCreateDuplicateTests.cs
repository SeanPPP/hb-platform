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
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class ContainerCreateDuplicateTests : IDisposable
{
    private readonly string _localDbPath;
    private readonly string _hbSalesDbPath;
    private readonly SqliteConnection _localConnection;
    private readonly SqliteConnection _hbSalesConnection;
    private readonly SqlSugarClient _localDb;
    private readonly SqlSugarScope _hbSalesDb;

    public ContainerCreateDuplicateTests()
    {
        _localDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hbSalesDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _localConnection = new SqliteConnection($"Data Source={_localDbPath}");
        _hbSalesConnection = new SqliteConnection($"Data Source={_hbSalesDbPath}");
        _localConnection.Open();
        _hbSalesConnection.Open();
        _localDb = new SqlSugarClient(CreateConnectionConfig(_localConnection.ConnectionString));
        _hbSalesDb = new SqlSugarScope(CreateConnectionConfig(_hbSalesConnection.ConnectionString));
        _localDb.CodeFirst.InitTables(typeof(Container));
    }

    [Fact]
    public async Task ReactCreateContainerAsync_同编号不同装柜日期_应允许创建()
    {
        await SeedContainerAsync("C-EXISTING", "CSNU6209359", new DateTime(2026, 5, 29));
        var service = CreateReactService();

        var containerGuid = await service.CreateContainerAsync(
            new CreateContainerDto
            {
                货柜编号 = "CSNU6209359",
                装柜日期 = new DateTime(2026, 5, 30),
            }
        );

        var created = await _localDb.Queryable<Container>().SingleAsync(x => x.ContainerCode == containerGuid);
        Assert.Equal("CSNU6209359", created.ContainerNumber);
        Assert.Equal(new DateTime(2026, 5, 30), created.LoadingDate);
    }

    [Fact]
    public async Task ReactCreateContainerAsync_同编号同装柜日期_应拒绝创建()
    {
        await SeedContainerAsync("C-EXISTING", "CSNU6209359", new DateTime(2026, 5, 29, 8, 30, 0));
        var service = CreateReactService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateContainerAsync(
                new CreateContainerDto
                {
                    货柜编号 = "CSNU6209359",
                    装柜日期 = new DateTime(2026, 5, 29, 15, 45, 0),
                }
            )
        );

        Assert.Equal("货柜编号 CSNU6209359 在装柜日期 2026-05-29 已存在", ex.Message);
    }

    [Fact]
    public async Task ReactCreateContainerAsync_编号前后空格_应按Trim后判重并入库()
    {
        await SeedContainerAsync("C-EXISTING", "CSNU6209359", new DateTime(2026, 5, 29));
        var service = CreateReactService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateContainerAsync(
                new CreateContainerDto
                {
                    货柜编号 = "  CSNU6209359  ",
                    装柜日期 = new DateTime(2026, 5, 29),
                }
            )
        );

        Assert.Equal("货柜编号 CSNU6209359 在装柜日期 2026-05-29 已存在", ex.Message);
    }

    [Fact]
    public async Task LegacyCreateContainerAsync_同编号不同装柜日期_应允许创建()
    {
        await SeedContainerAsync("C-EXISTING", "CSNU6209359", new DateTime(2026, 5, 29));
        var service = CreateLegacyService();

        var containerGuid = await service.CreateContainerAsync(
            new CreateContainerDto
            {
                货柜编号 = "CSNU6209359",
                装柜日期 = new DateTime(2026, 5, 30),
            }
        );

        var created = await _localDb.Queryable<Container>().SingleAsync(x => x.ContainerCode == containerGuid);
        Assert.Equal("CSNU6209359", created.ContainerNumber);
        Assert.Equal(new DateTime(2026, 5, 30), created.LoadingDate);
    }

    [Fact]
    public async Task LegacyCreateContainerAsync_同编号同装柜日期_应拒绝创建()
    {
        await SeedContainerAsync("C-EXISTING", "CSNU6209359", new DateTime(2026, 5, 29));
        var service = CreateLegacyService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateContainerAsync(
                new CreateContainerDto
                {
                    货柜编号 = "CSNU6209359",
                    装柜日期 = new DateTime(2026, 5, 29),
                }
            )
        );

        Assert.Equal("货柜编号 CSNU6209359 在装柜日期 2026-05-29 已存在", ex.Message);
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

    private async Task SeedContainerAsync(string containerCode, string containerNumber, DateTime loadingDate)
    {
        await _localDb.Insertable(
            new Container
            {
                ContainerCode = containerCode,
                ContainerNumber = containerNumber,
                LoadingDate = loadingDate,
                Status = 0,
                IsDeleted = false,
            }
        ).ExecuteCommandAsync();
    }

    private ContainerReactService CreateReactService()
    {
        return new ContainerReactService(
            CreateSqlSugarContext(_localDb),
            CreateHqSqlSugarContext(),
            CreateHBSalesSqlSugarContext(_hbSalesDb),
            new ConfigurationBuilder().Build(),
            Mock.Of<IMapper>(),
            NullLogger<ContainerReactService>.Instance,
            Mock.Of<IContainerHqSyncService>(),
            CreateTranslationServiceMock()
        );
    }

    private ContainerService CreateLegacyService()
    {
        return new ContainerService(
            CreateSqlSugarContext(_localDb),
            Mock.Of<IMapper>(),
            NullLogger<ContainerService>.Instance,
            CreateTranslationServiceMock()
        );
    }

    private static ITranslationService CreateTranslationServiceMock()
    {
        var translationService = new Mock<ITranslationService>();
        translationService
            .Setup(x => x.ContainsChinese(It.IsAny<string>()))
            .Returns<string>(value => value.Any(c => c >= '\u4e00' && c <= '\u9fff'));
        translationService
            .Setup(x => x.BatchTranslateToEnglishAsync(It.IsAny<List<string>>()))
            .ReturnsAsync((List<string> texts) => texts.ToDictionary(text => text, text => text));
        return translationService.Object;
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
        var dbField = typeof(HBSalesSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, db);
        return context;
    }
}
