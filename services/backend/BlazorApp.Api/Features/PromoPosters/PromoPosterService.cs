using BlazorApp.Api.Data;
using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.Models;
using SqlSugar;

namespace BlazorApp.Api.Features.PromoPosters;

public interface IPromoPosterService
{
    /// <summary>海报编辑页默认值；商品不存在返回 null。门店范围由调用方（控制器）先校验。</summary>
    Task<PromoPosterDefaultsDto?> GetDefaultsAsync(string storeCode, string productCode);

    /// <summary>校验请求并生成 PDF；校验失败抛 <see cref="PromoPosterValidationException"/>。</summary>
    PromoPosterPdfResult BuildPdf(PromoPosterPdfRequest request, DateTime now);
}

public sealed record PromoPosterPdfResult(byte[] Content, string FileName, int PageCount, int PosterCount);

public sealed class PromoPosterService : IPromoPosterService
{
    private readonly ISqlSugarClient _db;
    private readonly IPromotionReactService _promotions;

    public PromoPosterService(SqlSugarContext context, IPromotionReactService promotions)
    {
        _db = context.Db;
        _promotions = promotions;
    }

    public async Task<PromoPosterDefaultsDto?> GetDefaultsAsync(string storeCode, string productCode)
    {
        var product = await _db.Queryable<Product>()
            .Where(p => p.ProductCode == productCode && !p.IsDeleted)
            .Select(p => new { p.ProductCode, p.ItemNumber, p.ProductName, p.EnglishName, p.RetailPrice })
            .FirstAsync();
        if (product == null) return null;

        // 与扫码查询页口径一致：门店零售价 / 折扣率取 StoreRetailPrice，清仓价取 StoreClearancePrice（均按门店 + 未删除）
        var storePrice = await _db.Queryable<StoreRetailPrice>()
            .Where(s => s.ProductCode == productCode && s.StoreCode == storeCode && !s.IsDeleted)
            .Select(s => new { s.StoreRetailPriceValue, s.DiscountRate })
            .FirstAsync();
        var clearance = await _db.Queryable<StoreClearancePrice>()
            .Where(c => c.ProductCode == productCode && c.StoreCode == storeCode && !c.IsDeleted)
            .Select(c => new { c.ClearancePrice })
            .FirstAsync();

        var offers = new List<PromoPosterMultiBuyOfferDto>();
        var promotionResult = await _promotions.GetValidByProductAndStoreAsync(productCode, storeCode);
        if (promotionResult.Success && promotionResult.Data != null)
        {
            offers = promotionResult.Data
                .Where(p => p.ApplyQuantity >= 2 && p.FixedPrice > 0)
                .Select(p => new PromoPosterMultiBuyOfferDto
                {
                    PromotionId = p.Id.ToString(),
                    Name = p.Name,
                    ApplyQuantity = p.ApplyQuantity,
                    FixedPrice = p.FixedPrice,
                    EffectiveStart = p.EffectiveStart,
                    EffectiveEnd = p.EffectiveEnd,
                    ProductsCount = p.ProductsCount,
                })
                .ToList();
        }

        // 门店没有单独定价时回退到商品零售价（海报必须有原价可印）
        var retail = storePrice?.StoreRetailPriceValue ?? product.RetailPrice;
        var rate = NormalizeDiscountRate(storePrice?.DiscountRate);
        var discounted = retail.HasValue && rate > 0 ? RoundPrice(retail.Value * (1m - rate)) : (decimal?)null;
        var clearancePrice = clearance?.ClearancePrice is > 0 ? clearance.ClearancePrice : null;

        return new PromoPosterDefaultsDto
        {
            ProductCode = product.ProductCode ?? productCode,
            ItemNumber = product.ItemNumber,
            ProductName = product.ProductName,
            EnglishName = product.EnglishName,
            PosterTitle = SuggestTitle(product.EnglishName, product.ProductName),
            RetailPrice = retail,
            DiscountRate = rate,
            DiscountedPrice = discounted,
            ClearancePrice = clearancePrice,
            MultiBuyOffers = offers,
            CanSpecial = discounted.HasValue && retail.HasValue && discounted.Value < retail.Value,
            CanMultiBuy = offers.Count > 0,
            CanClearance = clearancePrice.HasValue,
        };
    }

    public PromoPosterPdfResult BuildPdf(PromoPosterPdfRequest request, DateTime now)
    {
        var specs = PromoPosterRequestParser.Parse(request, PromoPosterAssets.CanPrintTitleChar);
        var bytes = PromoPosterPdfRenderer.Render(specs, request.Impose);
        var pages = PromoPosterPdfRenderer.CountPages(specs, request.Impose);
        return new PromoPosterPdfResult(bytes, $"HB-Posters-{now:yyyyMMdd-HHmm}.pdf", pages, specs.Count);
    }

    /// <summary>
    /// 建议的海报英文名：优先 EnglishName；本地供应商商品的 ProductName 本来就是英文，也可直接用；
    /// 两者都含无法打印的字符（如中文）时返回空，让店员手填。
    /// </summary>
    internal static string SuggestTitle(string? englishName, string? productName)
    {
        foreach (var candidate in new[] { englishName, productName })
        {
            var title = PromoPosterRequestParser.NormalizeTitle(candidate);
            if (title.Length == 0 || title.Length > PromoPosterRequestParser.MaxTitleLength) continue;
            if (title.All(ch => PromoPosterAssets.CanPrintTitleChar(PromoPosterStyle.Classic, ch)
                && PromoPosterAssets.CanPrintTitleChar(PromoPosterStyle.Modern, ch)))
            {
                return title;
            }
        }
        return string.Empty;
    }

    /// <summary>DiscountRate 是减免比例（0.2 = 减 20%）；历史数据里偶有 1–100 的百分数，与移动端 normalizeDiscountRateValue 同样换算。</summary>
    internal static decimal NormalizeDiscountRate(decimal? value)
    {
        if (!value.HasValue || value.Value <= 0) return 0m;
        var rate = value.Value > 1m ? value.Value / 100m : value.Value;
        return rate >= 1m ? 0m : rate;
    }

    private static decimal RoundPrice(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
