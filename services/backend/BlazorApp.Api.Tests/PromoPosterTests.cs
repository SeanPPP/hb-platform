using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using AutoMapper;
using BlazorApp.Api.Controllers.React;
using BlazorApp.Api.Data;
using BlazorApp.Api.Features.PromoPosters;
using BlazorApp.Api.Interfaces;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;
using BlazorApp.Shared.Models;
using iTextSharp.text.pdf;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SqlSugar;
using Xunit;

namespace BlazorApp.Api.Tests;

/// <summary>促销海报：请求校验、PDF 渲染与拼版、默认值查询、接口鉴权。</summary>
public sealed class PromoPosterTests : IDisposable
{
    private const string StoreCode = "S1";
    private const string ProductCode = "P1";
    private const float MmToPt = 72f / 25.4f;

    private readonly string _dbPath;
    private readonly SqliteConnection _connection;
    private readonly SqlSugarClient _db;

    public PromoPosterTests()
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
        _db.CodeFirst.InitTables(typeof(Product), typeof(StoreRetailPrice), typeof(StoreClearancePrice), typeof(Store), typeof(UserStore));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        SqliteConnection.ClearAllPools();
        SqliteTempFileCleanup.DeleteIfExists(_dbPath);
    }

    // ---------------------------------------------------------------- 渲染

    [Fact]
    public void Render_四类三风格四尺寸不拼版时每张一页且为实际纸张尺寸()
    {
        var specs = (from kind in Enum.GetValues<PromoPosterKind>()
                     from style in Enum.GetValues<PromoPosterStyle>()
                     from size in Enum.GetValues<PromoPosterSize>()
                     select Spec(kind, style, size)).ToList();

        var pdf = PromoPosterPdfRenderer.Render(specs, impose: false);

        using var reader = new PdfReader(pdf);
        Assert.Equal(specs.Count, reader.NumberOfPages);
        Assert.Equal(specs.Count, PromoPosterPdfRenderer.CountPages(specs, impose: false));
        for (var i = 0; i < specs.Count; i++)
        {
            var (w, h) = PaperMm(specs[i].Size);
            AssertPageSize(reader, i + 1, w, h);
        }
    }

    [Fact]
    public void Render_拼版时按尺寸分组铺满A4且页数与预计一致()
    {
        var specs = new List<PromoPosterSpec>();
        specs.AddRange(Enumerable.Range(0, 5).Select(_ => Spec(PromoPosterKind.Special, PromoPosterStyle.Classic, PromoPosterSize.A6)));
        specs.Add(Spec(PromoPosterKind.New, PromoPosterStyle.Modern, PromoPosterSize.A4));
        specs.AddRange(Enumerable.Range(0, 9).Select(_ => Spec(PromoPosterKind.Clearance, PromoPosterStyle.Modern, PromoPosterSize.A7)));
        specs.Add(Spec(PromoPosterKind.MultiBuy, PromoPosterStyle.Classic, PromoPosterSize.A5));

        var pdf = PromoPosterPdfRenderer.Render(specs, impose: true);

        using var reader = new PdfReader(pdf);
        // A6：5 张 → 2 页（每页 4）；A4：1 页；A7：9 张 → 2 页（每页 8）；A5：1 张 → 1 页（每页 2）
        Assert.Equal(6, reader.NumberOfPages);
        Assert.Equal(6, PromoPosterPdfRenderer.CountPages(specs, impose: true));
        AssertPageSize(reader, 1, 210, 297); // A6 拼版：A4 纵向
        AssertPageSize(reader, 2, 210, 297);
        AssertPageSize(reader, 3, 210, 297); // A4 单张
        AssertPageSize(reader, 4, 297, 210); // A7 拼版：A4 横向
        AssertPageSize(reader, 5, 297, 210);
        AssertPageSize(reader, 6, 297, 210); // A5 拼版：A4 横向
    }

    [Fact]
    public void Render_海报文字可检索且嵌入了字体()
    {
        var specs = new List<PromoPosterSpec>
        {
            Spec(PromoPosterKind.Special, PromoPosterStyle.Classic, PromoPosterSize.A4),
            Spec(PromoPosterKind.MultiBuy, PromoPosterStyle.Modern, PromoPosterSize.A4) with { Quantity = 3, Price = 10m, UnitPrice = 3.99m, MixAndMatch = true },
            Spec(PromoPosterKind.Clearance, PromoPosterStyle.Classic, PromoPosterSize.A6) with { Price = 5m, WasPrice = 14.99m },
        };

        var pdf = PromoPosterPdfRenderer.Render(specs, impose: false);

        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        var page1 = document.GetPage(1).Text;
        Assert.Contains("SPECIAL", page1);
        Assert.Contains("Stainless Steel", page1);
        Assert.Contains("SAVE $3.90", page1);
        Assert.Contains("Item K1048", page1);
        var page2 = document.GetPage(2).Text;
        Assert.Contains("multi-buy", page2);
        Assert.Contains("$3.99 each", page2);
        Assert.Contains("SAVE", page2);
        Assert.Contains("$1.97", page2); // 3 × 3.99 − 10，按本商品计算
        Assert.Contains("mix & match any 3", page2);
        var page3 = document.GetPage(3).Text;
        Assert.Contains("CLEARANCE", page3);
        Assert.Contains("67% OFF", page3);
        Assert.Contains("While stocks last", page3);

        using var reader = new PdfReader(pdf);
        var fontNames = new HashSet<string>();
        for (var i = 1; i <= reader.NumberOfPages; i++)
        {
            var fonts = reader.GetPageN(i).GetAsDict(PdfName.Resources)?.GetAsDict(PdfName.Font);
            if (fonts == null) continue;
            foreach (var key in fonts.Keys)
            {
                var font = fonts.GetAsDict((PdfName)key);
                fontNames.Add(font?.GetAsName(PdfName.Basefont)?.ToString() ?? string.Empty);
            }
        }
        Assert.Contains(fontNames, n => n.Contains("ArchivoExtraCondensed-Black"));
        Assert.Contains(fontNames, n => n.Contains("BigShoulders"));
    }

    [Theory]
    [InlineData(PromoPosterStyle.Classic, PromoPosterSize.A4, false)]
    [InlineData(PromoPosterStyle.Classic, PromoPosterSize.A5, true)]
    [InlineData(PromoPosterStyle.Classic, PromoPosterSize.A6, true)]
    [InlineData(PromoPosterStyle.Classic, PromoPosterSize.A7, true)]
    [InlineData(PromoPosterStyle.Modern, PromoPosterSize.A4, false)]
    [InlineData(PromoPosterStyle.Modern, PromoPosterSize.A5, true)]
    [InlineData(PromoPosterStyle.Modern, PromoPosterSize.A6, true)]
    [InlineData(PromoPosterStyle.Modern, PromoPosterSize.A7, true)]
    [InlineData(PromoPosterStyle.LowInk, PromoPosterSize.A4, false)]
    [InlineData(PromoPosterStyle.LowInk, PromoPosterSize.A5, true)]
    [InlineData(PromoPosterStyle.LowInk, PromoPosterSize.A6, true)]
    [InlineData(PromoPosterStyle.LowInk, PromoPosterSize.A7, true)]
    public void Render_Logo默认显示且关闭时不加载图片(PromoPosterStyle style, PromoPosterSize size, bool impose)
    {
        var spec = Spec(PromoPosterKind.Special, style, size);

        using var defaultReader = new PdfReader(PromoPosterPdfRenderer.Render(new[] { spec }, impose));
        using var hiddenReader = new PdfReader(PromoPosterPdfRenderer.Render(new[] { spec }, impose, showLogo: false));

        var defaultXObjects = defaultReader.GetPageN(1).GetAsDict(new PdfName("Resources"))?.GetAsDict(new PdfName("XObject"));
        var hiddenXObjects = hiddenReader.GetPageN(1).GetAsDict(new PdfName("Resources"))?.GetAsDict(new PdfName("XObject"));
        Assert.NotNull(PromoPosterAssets.LogoBytes);
        Assert.NotNull(defaultXObjects);
        Assert.Empty(hiddenXObjects?.Keys ?? Array.Empty<PdfName>());
    }

    [Fact]
    public void PdfRequest_旧JSON未传ShowLogo时默认开启且服务转发关闭值()
    {
        var legacyRequest = System.Text.Json.JsonSerializer.Deserialize<PromoPosterPdfRequest>("{}");
        Assert.NotNull(legacyRequest);
        Assert.True(legacyRequest.ShowLogo);
        var service = new PromoPosterService(Context(), Mock.Of<IPromotionReactService>());
        var item = Item() with { Size = "A4", WasPrice = null };

        using var hiddenReader = new PdfReader(service.BuildPdf(new PromoPosterPdfRequest
        {
            StoreCode = StoreCode,
            ShowLogo = false,
            Impose = false,
            Posters = new() { item },
        }, new DateTime(2026, 9, 21)).Content);
        var hiddenXObjects = hiddenReader.GetPageN(1).GetAsDict(new PdfName("Resources"))?.GetAsDict(new PdfName("XObject"));
        Assert.Empty(hiddenXObjects?.Keys ?? Array.Empty<PdfName>());
    }

    [Theory]
    [InlineData(PromoPosterStyle.Classic)]
    [InlineData(PromoPosterStyle.Modern)]
    [InlineData(PromoPosterStyle.LowInk)]
    public void Render_无原价的特价不显示WAS或SAVE(PromoPosterStyle style)
    {
        var spec = Spec(PromoPosterKind.Special, style, PromoPosterSize.A4) with { WasPrice = null };
        using var document = UglyToad.PdfPig.PdfDocument.Open(PromoPosterPdfRenderer.Render(new[] { spec }, impose: false));
        var text = document.GetPage(1).Text;
        Assert.DoesNotContain("WAS", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SAVE", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_lowInk风格并保留默认业务字段()
    {
        var spec = Parse(Item() with { Style = "low-ink", Size = "A4" });
        Assert.Equal(PromoPosterStyle.LowInk, spec.Style);
        Assert.Equal(PromoPosterKind.Special, spec.Kind);
        Assert.Equal(9.09m, spec.Price);
        Assert.Equal(12.99m, spec.WasPrice);
    }

    [Fact]
    public void Render_LowInk业务文字包含EACH和WAS_SAVE且只在混搭时显示Mix()
    {
        var mixed = Spec(PromoPosterKind.MultiBuy, PromoPosterStyle.LowInk, PromoPosterSize.A4);
        var single = Spec(PromoPosterKind.Special, PromoPosterStyle.LowInk, PromoPosterSize.A4);
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(PromoPosterPdfRenderer.Render(new[] { mixed, single, mixed with { MixAndMatch = false } }, false));
        var mixedText = pdf.GetPage(1).Text;
        var singleText = pdf.GetPage(2).Text;
        Assert.Contains("EACH", mixedText);
        Assert.Contains("SAVE", mixedText);
        Assert.Contains("MIX & MATCH", mixedText);
        Assert.Contains("WAS", singleText);
        Assert.Contains("SAVE", singleText);
        Assert.Contains("EACH", singleText);
        Assert.DoesNotContain("MIX & MATCH", singleText);
        var fixedBundleText = pdf.GetPage(3).Text;
        Assert.Contains("FOR 3 ITEMS", fixedBundleText);
        Assert.DoesNotContain("MIX & MATCH", fixedBundleText);
    }

    [Fact]
    public void Render_LowInk大金额长货号A7文字不越界且单价单位在优惠区上方()
    {
        var spec = Spec(PromoPosterKind.Special, PromoPosterStyle.LowInk, PromoPosterSize.A7) with
        {
            Price = 99998.99m,
            WasPrice = 99999.99m,
            ItemNumber = "LONG-ITEM-1234567890",
            Title = "Extra Large Heavy Duty Storage Box"
        };
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(PromoPosterPdfRenderer.Render(new[] { spec }, false));
        var page = pdf.GetPage(1);
        Assert.Contains("WAS $99999.99", page.Text);
        Assert.Contains("SAVE $1.00", page.Text);
        // EACH 必须在单独的条件行，不能成为优惠区第三行而落到分割线上。
        var each = Assert.Single(page.GetWords(), word => word.Text == "EACH");
        Assert.True(each.BoundingBox.Bottom > page.Height * .25);
        Assert.All(page.Letters, letter =>
        {
            Assert.InRange(letter.BoundingBox.Left, 0, page.Width);
            Assert.InRange(letter.BoundingBox.Right, 0, page.Width);
            Assert.InRange(letter.BoundingBox.Bottom, 0, page.Height);
            Assert.InRange(letter.BoundingBox.Top, 0, page.Height);
        });
    }

    [Fact]
    public void Render_超长品名与三位数价格不会抛异常()
    {
        var longTitle = string.Join(' ', Enumerable.Repeat("Extra Large Heavy Duty Storage Box", 2));
        var specs = Enum.GetValues<PromoPosterStyle>()
            .SelectMany(style => Enum.GetValues<PromoPosterSize>().Select(size =>
                Spec(PromoPosterKind.Special, style, size) with { Title = longTitle[..Math.Min(longTitle.Length, 80)], Price = 129.99m, WasPrice = 199.99m }))
            .ToList();

        var pdf = PromoPosterPdfRenderer.Render(specs, impose: true);

        Assert.True(pdf.Length > 1000);
        Assert.Equal(PromoPosterPdfRenderer.CountPages(specs, true), new PdfReader(pdf).NumberOfPages);
    }

    // ---------------------------------------------------------------- 校验

    [Fact]
    public void Parse_中文品名被拒绝并指出字符()
    {
        var ex = Assert.Throws<PromoPosterValidationException>(() =>
            Parse(Item() with { Title = "保温瓶 Flask" }));
        Assert.Contains("第 1 张", ex.Message);
        Assert.Contains("保", ex.Message);
    }

    [Theory]
    [InlineData("price")]
    [InlineData("kind")]
    [InlineData("size")]
    [InlineData("style")]
    [InlineData("title")]
    public void Parse_缺少或无效字段时给出中文提示(string field)
    {
        var item = field switch
        {
            "price" => Item() with { Price = null },
            "kind" => Item() with { Kind = "bogus" },
            "size" => Item() with { Size = "A3" },
            "style" => Item() with { Style = "retro" },
            _ => Item() with { Title = "   " },
        };
        var ex = Assert.Throws<PromoPosterValidationException>(() => Parse(item));
        Assert.StartsWith("第 1 张", ex.Message);
    }

    [Fact]
    public void Parse_多件价件数不足或海报过多被拒绝()
    {
        Assert.Throws<PromoPosterValidationException>(() => Parse(Item() with { Kind = "multibuy", Quantity = 1 }));
        var tooMany = new PromoPosterPdfRequest { StoreCode = StoreCode, Posters = Enumerable.Range(0, 201).Select(_ => Item()).ToList() };
        Assert.Throws<PromoPosterValidationException>(() => PromoPosterRequestParser.Parse(tooMany, PromoPosterAssets.CanPrintTitleChar));
        Assert.Throws<PromoPosterValidationException>(() => PromoPosterRequestParser.Parse(new PromoPosterPdfRequest(), PromoPosterAssets.CanPrintTitleChar));
    }

    [Fact]
    public void Parse_按类型保留字段并计算优惠()
    {
        var special = Parse(Item());
        Assert.Equal(3.90m, special.SpecialSaving);

        var clearance = Parse(Item() with { Kind = "clearance", Price = 5m, WasPrice = 14.99m, ValidTo = new DateTime(2026, 10, 2) });
        Assert.Equal(67, clearance.ClearancePercentOff);
        Assert.Null(clearance.ValidTo); // 清仓不印有效期

        var multi = Parse(Item() with { Kind = "multibuy", Price = 10m, Quantity = 3, UnitPrice = 3.99m, WasPrice = 99m, MixAndMatch = true });
        Assert.Equal(1.97m, multi.MultiBuySaving);
        Assert.Null(multi.WasPrice);
        Assert.True(multi.MixAndMatch);

        var noSaving = Parse(Item() with { WasPrice = null });
        Assert.Null(noSaving.SpecialSaving); // 未提供原价时不显示 SAVE
    }

    [Fact]
    public void Parse_Special原价可省略但填写时必须高于现价()
    {
        Assert.Throws<PromoPosterValidationException>(() => Parse(Item() with { WasPrice = 9.09m }));
        Assert.Throws<PromoPosterValidationException>(() => Parse(Item() with { WasPrice = 9m }));
        Assert.Null(Parse(Item() with { WasPrice = null }).WasPrice);
    }

    [Fact]
    public void Parse_Clearance必须有高于现价的原价()
    {
        Assert.Throws<PromoPosterValidationException>(() => Parse(Item() with { Kind = "clearance", Price = 5m, WasPrice = null }));
        Assert.Throws<PromoPosterValidationException>(() => Parse(Item() with { Kind = "clearance", Price = 5m, WasPrice = 5m }));
        Assert.Throws<PromoPosterValidationException>(() => Parse(Item() with { Kind = "clearance", Price = 5m, WasPrice = 4.99m }));
        Assert.Equal(14.99m, Parse(Item() with { Kind = "clearance", Price = 5m, WasPrice = 14.99m }).WasPrice);
    }

    [Fact]
    public void FooterLines_按类型与尺寸生成页脚文字()
    {
        var spec = Spec(PromoPosterKind.Special, PromoPosterStyle.Classic, PromoPosterSize.A4);
        Assert.Equal(new[] { "Valid 19 Sep – 2 Oct 2026", "Item K1048" }, ClassicPosterPainter.FooterLines(spec, false));
        Assert.Equal(new[] { "Until 2 Oct", "#K1048" }, ClassicPosterPainter.FooterLines(spec, true));

        var fresh = Spec(PromoPosterKind.New, PromoPosterStyle.Modern, PromoPosterSize.A4) with { InStoreSince = new DateTime(2026, 9, 19) };
        Assert.Equal(new[] { "Item K5031", "In store since 19 Sep 2026" }, ClassicPosterPainter.FooterLines(fresh, false));

        var noDate = spec with { ValidFrom = null, ValidTo = null, ItemNumber = null };
        Assert.Empty(ClassicPosterPainter.FooterLines(noDate, false));
    }

    [Fact]
    public void SuggestTitle_优先英文名_中文名不可打印时留空()
    {
        Assert.Equal("Vacuum Flask 500ml", PromoPosterService.SuggestTitle("  Vacuum   Flask 500ml ", "不锈钢保温瓶"));
        Assert.Equal("Garden Kneeling Pad", PromoPosterService.SuggestTitle(null, "Garden Kneeling Pad"));
        Assert.Equal(string.Empty, PromoPosterService.SuggestTitle(null, "不锈钢保温瓶"));
    }

    [Theory]
    [InlineData(null, 0.0)]
    [InlineData(0.3, 0.3)]
    [InlineData(30.0, 0.3)]
    [InlineData(150.0, 0.0)]
    public void NormalizeDiscountRate_兼容百分数(double? input, double expected)
    {
        Assert.Equal((decimal)expected, PromoPosterService.NormalizeDiscountRate(input.HasValue ? (decimal)input.Value : null));
    }

    // ---------------------------------------------------------------- 默认值

    [Fact]
    public async Task GetDefaults_取门店价折扣清仓价与多件促销()
    {
        SeedProduct(englishName: "Stainless Steel Vacuum Flask 500ml", retail: 11.99m);
        _db.Insertable(new StoreRetailPrice { StoreCode = StoreCode, ProductCode = ProductCode, StoreRetailPriceValue = 12.99m, DiscountRate = 0.3m }).ExecuteCommand();
        _db.Insertable(new StoreClearancePrice { StoreCode = StoreCode, ProductCode = ProductCode, ClearancePrice = 5m }).ExecuteCommand();
        var promotions = new Mock<IPromotionReactService>();
        promotions.Setup(p => p.GetValidByProductAndStoreAsync(ProductCode, StoreCode, null))
            .ReturnsAsync(ApiResponse<List<PromotionListDto>>.OK(new List<PromotionListDto>
            {
                new() { Id = "promo-1", Name = "Spring 3 for 10", ApplyQuantity = 3, FixedPrice = 10m, ProductsCount = 6,
                    EffectiveStart = new DateTime(2026, 9, 19), EffectiveEnd = new DateTime(2026, 10, 2, 23, 59, 59) },
                new() { Id = "promo-2", Name = "单件立减", ApplyQuantity = 1, FixedPrice = 9m },
            }));

        var defaults = await new PromoPosterService(Context(), promotions.Object).GetDefaultsAsync(StoreCode, ProductCode);

        Assert.NotNull(defaults);
        Assert.Equal("Stainless Steel Vacuum Flask 500ml", defaults!.PosterTitle);
        Assert.Equal(12.99m, defaults.RetailPrice);
        Assert.Equal(9.09m, defaults.DiscountedPrice);
        Assert.Equal(5m, defaults.ClearancePrice);
        var offer = Assert.Single(defaults.MultiBuyOffers); // 件数 < 2 的促销不算多件价
        Assert.Equal(3, offer.ApplyQuantity);
        Assert.True(defaults.CanSpecial && defaults.CanMultiBuy && defaults.CanClearance);
    }

    [Fact]
    public async Task GetDefaults_门店未定价时回退商品零售价且无折扣仍可做特价()
    {
        SeedProduct(englishName: null, retail: 6.49m, productName: "不锈钢保温瓶");
        var promotions = new Mock<IPromotionReactService>();
        promotions.Setup(p => p.GetValidByProductAndStoreAsync(ProductCode, StoreCode, null))
            .ReturnsAsync(ApiResponse<List<PromotionListDto>>.OK(new List<PromotionListDto>()));

        var defaults = await new PromoPosterService(Context(), promotions.Object).GetDefaultsAsync(StoreCode, ProductCode);

        Assert.Equal(6.49m, defaults!.RetailPrice);
        Assert.Null(defaults.DiscountedPrice);
        Assert.Equal(string.Empty, defaults.PosterTitle); // 只有中文名，需要店员手填英文名
        Assert.True(defaults.CanSpecial);
        Assert.False(defaults.CanMultiBuy || defaults.CanClearance);
        Assert.Null(await new PromoPosterService(Context(), promotions.Object).GetDefaultsAsync(StoreCode, "missing"));
    }

    // ---------------------------------------------------------------- 接口

    [Fact]
    public async Task Controller_未注册服务时返回503()
    {
        var controller = Controller(service: null, Principal("Manager"));
        var result = await controller.GetPromoPosterDefaults(StoreCode, ProductCode);
        Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Controller_缺少分店代码返回400_越权分店返回Forbid()
    {
        var service = new Mock<IPromoPosterService>(MockBehavior.Strict);
        Assert.IsType<BadRequestObjectResult>(await Controller(service.Object, Principal("Manager")).GetPromoPosterDefaults(" ", ProductCode));

        // 普通店员只绑定了 S1，请求 S2 被拒
        var storeGuid = Guid.NewGuid().ToString();
        _db.Insertable(new Store { StoreGUID = storeGuid, StoreCode = StoreCode, StoreName = "Sunnybank" }).ExecuteCommand();
        _db.Insertable(new UserStore { UserGUID = "user-1", StoreGUID = storeGuid }).ExecuteCommand();
        var staff = Principal("Staff", userGuid: "user-1");
        Assert.IsType<ForbidResult>(await Controller(service.Object, staff).CreatePromoPosterPdf(new PromoPosterPdfRequest { StoreCode = "S2", Posters = new() { Item() } }));
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Controller_生成PDF返回文件_校验失败返回400()
    {
        var service = new PromoPosterService(Context(), Mock.Of<IPromotionReactService>());
        var controller = Controller(service, Principal("Manager"));

        var ok = await controller.CreatePromoPosterPdf(new PromoPosterPdfRequest { StoreCode = StoreCode, Posters = new() { Item(), Item() with { Size = "A7" } } });
        var file = Assert.IsType<FileContentResult>(ok);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.StartsWith("HB-Posters-", file.FileDownloadName);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(file.FileContents, 0, 4));
        Assert.Equal("2", controller.Response.Headers["X-Poster-Page-Count"].ToString());

        var bad = await Controller(service, Principal("Manager")).CreatePromoPosterPdf(new PromoPosterPdfRequest { StoreCode = StoreCode, Posters = new() { Item() with { Title = "中文" } } });
        var badRequest = Assert.IsType<BadRequestObjectResult>(bad);
        Assert.Contains("无法打印", Assert.IsType<ApiResponse<object>>(badRequest.Value).Message);
    }

    /// <summary>设置环境变量 PROMO_POSTER_SAMPLE_DIR 时输出样张，便于人工对照设计稿（CI 不设置则跳过）。</summary>
    [Fact]
    public void Samples_按需输出样张()
    {
        var dir = Environment.GetEnvironmentVariable("PROMO_POSTER_SAMPLE_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;
        Directory.CreateDirectory(dir);
        foreach (var style in Enum.GetValues<PromoPosterStyle>())
        {
            var specs = (from kind in Enum.GetValues<PromoPosterKind>() from size in Enum.GetValues<PromoPosterSize>() select Spec(kind, style, size)).ToList();
            File.WriteAllBytes(Path.Combine(dir, $"posters-{style}.pdf"), PromoPosterPdfRenderer.Render(specs, impose: false));
            var sheet = Enum.GetValues<PromoPosterKind>().Select(kind => Spec(kind, style, PromoPosterSize.A6)).ToList();
            File.WriteAllBytes(Path.Combine(dir, $"sheet-A6-{style}.pdf"), PromoPosterPdfRenderer.Render(sheet, impose: true));
        }
    }

    // ---------------------------------------------------------------- 工具

    /// <summary>与设计稿示例一致的样例数据。</summary>
    private static PromoPosterSpec Spec(PromoPosterKind kind, PromoPosterStyle style, PromoPosterSize size) => kind switch
    {
        PromoPosterKind.Special => new(kind, style, size, "Stainless Steel Vacuum Flask 500ml", "K1048", 9.09m, 12.99m, 0, null, false,
            new DateTime(2026, 9, 19), new DateTime(2026, 10, 2), null),
        PromoPosterKind.MultiBuy => new(kind, style, size, "Scented Candle Jar 200g", "H4127", 10m, null, 3, 3.99m, true,
            new DateTime(2026, 9, 19), new DateTime(2026, 10, 2), null),
        PromoPosterKind.New => new(kind, style, size, "Ceramic Noodle Bowl with Lid 18cm", "K5031", 6.49m, null, 0, null, false,
            null, null, new DateTime(2026, 9, 19)),
        _ => new(kind, style, size, "Kids Waterproof Rain Boots", "W2093", 5m, 14.99m, 0, null, false, null, null, null),
    };

    private static PromoPosterItemRequest Item() => new()
    {
        Kind = "special",
        Style = "classic",
        Size = "A6",
        ProductCode = ProductCode,
        ItemNumber = "K1048",
        Title = "Stainless Steel Vacuum Flask 500ml",
        Price = 9.09m,
        WasPrice = 12.99m,
        ValidFrom = new DateTime(2026, 9, 19),
        ValidTo = new DateTime(2026, 10, 2),
    };

    private static PromoPosterSpec Parse(PromoPosterItemRequest item) =>
        PromoPosterRequestParser.Parse(new PromoPosterPdfRequest { StoreCode = StoreCode, Posters = new() { item } }, PromoPosterAssets.CanPrintTitleChar)[0];

    private static (float W, float H) PaperMm(PromoPosterSize size) => size switch
    {
        PromoPosterSize.A4 => (210, 297),
        PromoPosterSize.A5 => (148, 210),
        PromoPosterSize.A6 => (105, 148),
        _ => (74, 105),
    };

    private static void AssertPageSize(PdfReader reader, int page, float wMm, float hMm)
    {
        var rect = reader.GetPageSize(page);
        Assert.InRange(rect.Width, wMm * MmToPt - 0.5f, wMm * MmToPt + 0.5f);
        Assert.InRange(rect.Height, hMm * MmToPt - 0.5f, hMm * MmToPt + 0.5f);
    }

    private void SeedProduct(string? englishName, decimal? retail, string productName = "不锈钢真空保温瓶 500ml") =>
        _db.Insertable(new Product { ProductCode = ProductCode, ProductName = productName, EnglishName = englishName, ItemNumber = "K1048", RetailPrice = retail })
            .ExecuteCommand();

    private SqlSugarContext Context()
    {
        var context = (SqlSugarContext)RuntimeHelpers.GetUninitializedObject(typeof(SqlSugarContext));
        typeof(SqlSugarContext).GetField("_db", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(context, _db);
        return context;
    }

    private static ClaimsPrincipal Principal(string role, string? userGuid = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "tester"), new(ClaimTypes.Role, role) };
        if (userGuid != null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userGuid));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private ReactStoreProductMaintenanceController Controller(IPromoPosterService? service, ClaimsPrincipal user) =>
        new(
            Mock.Of<IStoreProductMaintenanceReactService>(), Mock.Of<IDeviceRegistrationService>(), Mock.Of<IMapper>(),
            Context(), NullLogger<ReactStoreProductMaintenanceController>.Instance, Mock.Of<IAuthorizationService>(),
            promoPosterService: service
        )
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = user } },
        };
}
