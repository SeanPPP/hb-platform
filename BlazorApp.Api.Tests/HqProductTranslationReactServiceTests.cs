using System.Reflection;
using System.Runtime.CompilerServices;
using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Services;
using BlazorApp.Api.Services.React;
using BlazorApp.Shared.Models.HqEntities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
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
    public async Task BatchTranslateToEnglishAsync_Mock返回原中文时_不应缓存为有效英文翻译()
    {
        var translationService = new TranslationService(
            NullLogger<TranslationService>.Instance,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Translation:Provider"] = "mock",
                })
                .Build(),
            new HttpClient()
        );

        var result = await translationService.BatchTranslateToEnglishAsync(
            new List<string> { "250g塑形泥红棕色" }
        );

        Assert.Equal("250g塑形泥红棕色", result["250g塑形泥红棕色"]);
        Assert.Null(await translationService.GetCachedTranslationAsync("250g塑形泥红棕色"));
    }

    [Fact]
    public async Task TranslateToEnglishAsync_Mock返回原中文时_不应缓存为有效英文翻译()
    {
        var translationService = new TranslationService(
            NullLogger<TranslationService>.Instance,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Translation:Provider"] = "mock",
                })
                .Build(),
            new HttpClient()
        );

        var result = await translationService.TranslateToEnglishAsync("250g塑形泥红棕色");

        Assert.Equal("250g塑形泥红棕色", result);
        Assert.Null(await translationService.GetCachedTranslationAsync("250g塑形泥红棕色"));
    }

    [Fact]
    public async Task TranslateToEnglishAsync_DeepSeek返回ChatCompletions_应解析并缓存()
    {
        var httpClient = CreateDeepSeekHttpClient("Strawberry Toy");
        var translationService = CreateDeepSeekTranslationService(httpClient);

        var result = await translationService.TranslateToEnglishAsync("草莓玩具");

        Assert.Equal("Strawberry Toy", result);
        Assert.Equal(
            "Strawberry Toy",
            await translationService.GetCachedTranslationAsync("草莓玩具")
        );
    }

    [Fact]
    public async Task BatchTranslateToEnglishAsync_DeepSeek编号结果_应映射原文并缓存()
    {
        var httpClient = CreateDeepSeekHttpClient("1. Strawberry Toy\n2. Blue Cup");
        var translationService = CreateDeepSeekTranslationService(httpClient);

        var result = await translationService.BatchTranslateToEnglishAsync(
            new List<string> { "草莓玩具", "蓝色杯子" }
        );

        Assert.Equal("Strawberry Toy", result["草莓玩具"]);
        Assert.Equal("Blue Cup", result["蓝色杯子"]);
        Assert.Equal(
            "Strawberry Toy",
            await translationService.GetCachedTranslationAsync("草莓玩具")
        );
        Assert.Equal("Blue Cup", await translationService.GetCachedTranslationAsync("蓝色杯子"));
    }

    [Fact]
    public async Task BatchTranslateToEnglishAsync_DeepSeek返回中文结果_不应缓存为有效英文翻译()
    {
        var httpClient = CreateDeepSeekHttpClient("1. 草莓玩具");
        var translationService = CreateDeepSeekTranslationService(httpClient);

        var result = await translationService.BatchTranslateToEnglishAsync(
            new List<string> { "草莓玩具" }
        );

        Assert.Equal("草莓玩具", result["草莓玩具"]);
        Assert.Null(await translationService.GetCachedTranslationAsync("草莓玩具"));
    }

    [Fact]
    public async Task BatchTranslateToEnglishAsync_DeepSeek缺少ApiKey_应明确失败()
    {
        var translationService = new TranslationService(
            NullLogger<TranslationService>.Instance,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Translation:Provider"] = "deepseek",
                })
                .Build(),
            new HttpClient(new ThrowingHttpMessageHandler())
        );

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            translationService.BatchTranslateToEnglishAsync(new List<string> { "苹果" })
        );

        Assert.Contains("DeepSeek", ex.Message);
        Assert.Contains("ApiKey", ex.Message);
    }

    [Fact]
    public async Task BatchTranslateToEnglishAsync_旧缓存为原中文时_应忽略并重新请求DeepSeek()
    {
        var httpClient = CreateDeepSeekHttpClient("1. 250g Shaping Clay Reddish Brown");
        var translationService = CreateDeepSeekTranslationService(httpClient);

        await translationService.CacheTranslationAsync("250g塑形泥红棕色", "250g塑形泥红棕色");

        var result = await translationService.BatchTranslateToEnglishAsync(
            new List<string> { "250g塑形泥红棕色" }
        );

        Assert.Equal("250g Shaping Clay Reddish Brown", result["250g塑形泥红棕色"]);
        Assert.Equal(
            "250g Shaping Clay Reddish Brown",
            await translationService.GetCachedTranslationAsync("250g塑形泥红棕色")
        );
    }

    [Fact]
    public async Task BatchTranslateToEnglishAsync_DeepSeek调用失败时_不应降级到模拟翻译()
    {
        var translationService = CreateDeepSeekTranslationService(
            new HttpClient(new ThrowingHttpMessageHandler())
        );

        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            translationService.BatchTranslateToEnglishAsync(new List<string> { "苹果" })
        );

        Assert.Contains("DeepSeek", ex.Message);
        Assert.Null(await translationService.GetCachedTranslationAsync("苹果"));
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

    private static TranslationService CreateDeepSeekTranslationService(HttpClient httpClient)
    {
        return new TranslationService(
            NullLogger<TranslationService>.Instance,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Translation:Provider"] = "deepseek",
                    ["Translation:DeepSeek:ApiKey"] = "test-api-key",
                    ["Translation:DeepSeek:Endpoint"] =
                        "https://api.deepseek.com/chat/completions",
                    ["Translation:DeepSeek:Model"] = "deepseek-v4-flash",
                })
                .Build(),
            httpClient
        );
    }

    private static HttpClient CreateDeepSeekHttpClient(string content)
    {
        return new HttpClient(new DeepSeekHttpMessageHandler(content));
    }

    private sealed class DeepSeekHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _content;

        public DeepSeekHttpMessageHandler(string content)
        {
            _content = content;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                "https://api.deepseek.com/chat/completions",
                request.RequestUri?.ToString()
            );
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-api-key", request.Headers.Authorization?.Parameter);

            var requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Contains("deepseek-v4-flash", requestJson);

            var responseJson = System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    choices = new[]
                    {
                        new { message = new { content = _content } },
                    },
                }
            );

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    responseJson,
                    System.Text.Encoding.UTF8,
                    "application/json"
                ),
            };
        }
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            throw new InvalidOperationException("不应在缺少 DeepSeek ApiKey 时发起 HTTP 请求。");
        }
    }

    private static HqSqlSugarContext CreateHqSqlSugarContext(SqlSugarClient db)
    {
        var context = (HqSqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(HqSqlSugarContext));
        var dbField = typeof(HqSqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic);
        dbField!.SetValue(context, db);
        return context;
    }
}
