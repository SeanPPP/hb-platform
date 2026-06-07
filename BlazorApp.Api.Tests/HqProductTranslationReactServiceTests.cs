using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

public sealed class HqProductTranslationReactServiceTests : IDisposable
{
    private readonly string _hqDbPath;
    private readonly SqliteConnection _hqConnection;
    private readonly SqlSugarClient _hqDb;

    public HqProductTranslationReactServiceTests()
    {
        _hqDbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        _hqConnection = new SqliteConnection($"Data Source={_hqDbPath}");
        _hqConnection.Open();
        _hqDb = new SqlSugarClient(
            new ConnectionConfig
            {
                ConnectionString = _hqConnection.ConnectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = false,
                InitKeyType = InitKeyType.Attribute,
            }
        );
        _hqDb.CodeFirst.InitTables(typeof(CPT_DIC_商品信息字典表));
    }

    [Fact]
    public async Task TranslateNamesAllAsync_译文仍含中文_应跳过且不写入英文名称()
    {
        await SeedHqProductAsync("P-MIXED", "草莓玩具");
        var service = CreateService(new Dictionary<string, string> { ["草莓玩具"] = "Strawberry 玩具" });

        var result = await service.TranslateNamesAllAsync(overwriteExisting: true);

        var product = await _hqDb.Queryable<CPT_DIC_商品信息字典表>()
            .SingleAsync(x => x.商品编码 == "P-MIXED");
        Assert.Equal(1, result.TotalCandidates);
        Assert.Equal(0, result.TotalTranslated);
        Assert.Equal(1, result.TotalSkipped);
        Assert.Null(product.英文名称);
    }

    [Fact]
    public async Task TranslateNamesAllAsync_纯英文译文_应写入英文名称()
    {
        await SeedHqProductAsync("P-EN", "草莓玩具");
        var service = CreateService(new Dictionary<string, string> { ["草莓玩具"] = "Strawberry Toy" });

        var result = await service.TranslateNamesAllAsync(overwriteExisting: true);

        var product = await _hqDb.Queryable<CPT_DIC_商品信息字典表>()
            .SingleAsync(x => x.商品编码 == "P-EN");
        Assert.Equal(1, result.TotalCandidates);
        Assert.Equal(1, result.TotalTranslated);
        Assert.Equal(0, result.TotalSkipped);
        Assert.Equal("Strawberry Toy", product.英文名称);
    }

    [Fact]
    public async Task TranslateNamesAllAsync_现有英文名称仍含中文_应优先翻译英文名称本身()
    {
        await SeedHqProductAsync("P-ZH-EN", "商品中文名不应优先", englishName: "草莓玩具");
        var service = CreateService(
            new Dictionary<string, string>
            {
                ["草莓玩具"] = "Strawberry Toy",
                ["商品中文名不应优先"] = "Wrong Source",
            }
        );

        var result = await service.TranslateNamesAllAsync(overwriteExisting: false);

        var product = await _hqDb.Queryable<CPT_DIC_商品信息字典表>()
            .SingleAsync(x => x.商品编码 == "P-ZH-EN");
        Assert.Equal(1, result.TotalCandidates);
        Assert.Equal(1, result.TotalTranslated);
        Assert.Equal(0, result.TotalSkipped);
        Assert.Equal("Strawberry Toy", product.英文名称);
    }

    public void Dispose()
    {
        _hqDb.Dispose();
        _hqConnection.Dispose();
        SqliteTempFileCleanup.DeleteIfExists(_hqDbPath);
    }

    private async Task SeedHqProductAsync(
        string productCode,
        string chineseName,
        string? englishName = null
    )
    {
        await _hqDb.Insertable(
            new CPT_DIC_商品信息字典表
            {
                商品编码 = productCode,
                中文名称 = chineseName,
                英文名称 = englishName,
            }
        ).ExecuteCommandAsync();
    }

    private HqProductTranslationReactService CreateService(Dictionary<string, string> translations)
    {
        var translationService = new Mock<ITranslationService>();
        translationService
            .Setup(x => x.ContainsChinese(It.IsAny<string>()))
            .Returns<string>(value => value.Any(c => c >= '\u4e00' && c <= '\u9fff'));
        translationService
            .Setup(x => x.BatchTranslateToEnglishAsync(It.IsAny<List<string>>()))
            .ReturnsAsync(translations);

        return new HqProductTranslationReactService(
            CreateHqSqlSugarContext(_hqDb),
            translationService.Object,
            NullLogger<HqProductTranslationReactService>.Instance
        );
    }

    private static HqSqlSugarContext CreateHqSqlSugarContext(SqlSugarClient db)
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HqSqlSugarContext));
        var dbField = typeof(HqSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, db);
        return context;
    }
}
