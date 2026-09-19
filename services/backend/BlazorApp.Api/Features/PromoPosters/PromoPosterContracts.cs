using System.Globalization;

namespace BlazorApp.Api.Features.PromoPosters;

/// <summary>海报类型：特价 / 多件价 / 新品 / 清仓。</summary>
public enum PromoPosterKind
{
    Special,
    MultiBuy,
    New,
    Clearance,
}

/// <summary>海报风格：经典（方案 A）/ 现代（方案 B）。</summary>
public enum PromoPosterStyle
{
    Classic,
    Modern,
}

/// <summary>纸张尺寸。</summary>
public enum PromoPosterSize
{
    A4,
    A5,
    A6,
    A7,
}

/// <summary>海报编辑页默认值（GET promo-posters/defaults）。</summary>
public sealed class PromoPosterDefaultsDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string? ItemNumber { get; set; }
    public string? ProductName { get; set; }
    public string? EnglishName { get; set; }

    /// <summary>建议印在海报上的英文名；为空表示没有可打印的英文名，需要店员手填。</summary>
    public string PosterTitle { get; set; } = string.Empty;

    public decimal? RetailPrice { get; set; }

    /// <summary>减免比例（0.3 表示减 30%）。</summary>
    public decimal DiscountRate { get; set; }

    public decimal? DiscountedPrice { get; set; }
    public decimal? ClearancePrice { get; set; }
    public List<PromoPosterMultiBuyOfferDto> MultiBuyOffers { get; set; } = new();
    public bool CanSpecial { get; set; }
    public bool CanMultiBuy { get; set; }
    public bool CanClearance { get; set; }
}

public sealed class PromoPosterMultiBuyOfferDto
{
    public string PromotionId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public int ApplyQuantity { get; set; }
    public decimal FixedPrice { get; set; }
    public DateTime EffectiveStart { get; set; }
    public DateTime EffectiveEnd { get; set; }
    public int ProductsCount { get; set; }
}

/// <summary>生成 PDF 请求（POST promo-posters/pdf）。</summary>
public sealed class PromoPosterPdfRequest
{
    public string? StoreCode { get; set; }

    /// <summary>小尺寸（A5/A6/A7）是否拼到 A4 纸并画裁切线。</summary>
    public bool Impose { get; set; } = true;

    public List<PromoPosterItemRequest>? Posters { get; set; }
}

/// <summary>单张海报请求；用 record 便于测试按样例派生（属性仍可读写，模型绑定不受影响）。</summary>
public sealed record PromoPosterItemRequest
{
    public string? Kind { get; set; }
    public string? Style { get; set; }
    public string? Size { get; set; }
    public string? ProductCode { get; set; }
    public string? ItemNumber { get; set; }
    public string? Title { get; set; }

    /// <summary>special: 现价；clearance: 清仓价；new: 售价；multibuy: 组合价。</summary>
    public decimal? Price { get; set; }

    /// <summary>special / clearance 的原价（划线价）。</summary>
    public decimal? WasPrice { get; set; }

    public int? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public bool MixAndMatch { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public DateTime? InStoreSince { get; set; }
}

/// <summary>校验后的单张海报规格，渲染器只认这个结构。</summary>
public sealed record PromoPosterSpec(
    PromoPosterKind Kind,
    PromoPosterStyle Style,
    PromoPosterSize Size,
    string Title,
    string? ItemNumber,
    decimal Price,
    decimal? WasPrice,
    int Quantity,
    decimal? UnitPrice,
    bool MixAndMatch,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    DateTime? InStoreSince
)
{
    /// <summary>特价省多少钱；原价不高于现价时不显示。</summary>
    public decimal? SpecialSaving => WasPrice.HasValue && WasPrice.Value > Price ? WasPrice.Value - Price : null;

    /// <summary>清仓折扣百分比（四舍五入到整数）。</summary>
    public int? ClearancePercentOff =>
        WasPrice.HasValue && WasPrice.Value > Price && WasPrice.Value > 0
            ? (int)Math.Round((WasPrice.Value - Price) / WasPrice.Value * 100m, MidpointRounding.AwayFromZero)
            : null;

    /// <summary>多件价按本商品计算的节省：单价 × 件数 − 组合价。</summary>
    public decimal? MultiBuySaving
    {
        get
        {
            if (!UnitPrice.HasValue || Quantity <= 0) return null;
            var saving = UnitPrice.Value * Quantity - Price;
            return saving > 0 ? saving : null;
        }
    }
}

/// <summary>请求校验失败，消息直接展示给店员。</summary>
public sealed class PromoPosterValidationException(string message) : Exception(message);

/// <summary>把 App 传来的字符串参数解析成强类型规格，并做业务校验。</summary>
public static class PromoPosterRequestParser
{
    public const int MaxPosters = 200;
    public const int MaxTitleLength = 80;

    public static IReadOnlyList<PromoPosterSpec> Parse(PromoPosterPdfRequest request, Func<PromoPosterStyle, char, bool> canPrintChar)
    {
        var items = request.Posters ?? new List<PromoPosterItemRequest>();
        if (items.Count == 0) throw new PromoPosterValidationException("请至少选择一张海报");
        if (items.Count > MaxPosters) throw new PromoPosterValidationException($"一次最多生成 {MaxPosters} 张海报");

        var specs = new List<PromoPosterSpec>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            specs.Add(ParseItem(items[i], i + 1, canPrintChar));
        }
        return specs;
    }

    private static PromoPosterSpec ParseItem(PromoPosterItemRequest item, int index, Func<PromoPosterStyle, char, bool> canPrintChar)
    {
        var kind = ParseKind(item.Kind, index);
        var style = ParseStyle(item.Style, index);
        var size = ParseSize(item.Size, index);

        var title = NormalizeTitle(item.Title);
        if (title.Length == 0) throw new PromoPosterValidationException($"第 {index} 张海报缺少英文品名");
        if (title.Length > MaxTitleLength) throw new PromoPosterValidationException($"第 {index} 张海报的英文品名超过 {MaxTitleLength} 个字符");
        // 海报字体只有拉丁字形：中文等字符会印成空白，必须在这里拦下。
        var bad = title.Where(ch => !canPrintChar(style, ch)).Distinct().ToArray();
        if (bad.Length > 0)
        {
            throw new PromoPosterValidationException($"第 {index} 张海报的英文品名含有无法打印的字符：{new string(bad)}");
        }

        var price = item.Price ?? throw new PromoPosterValidationException($"第 {index} 张海报缺少价格");
        EnsureMoney(price, index, "价格");
        if (price <= 0) throw new PromoPosterValidationException($"第 {index} 张海报的价格必须大于 0");
        if (item.WasPrice.HasValue) EnsureMoney(item.WasPrice.Value, index, "原价");
        if (item.UnitPrice.HasValue) EnsureMoney(item.UnitPrice.Value, index, "单价");

        var quantity = 0;
        if (kind == PromoPosterKind.MultiBuy)
        {
            quantity = item.Quantity ?? 0;
            if (quantity < 2 || quantity > 99) throw new PromoPosterValidationException($"第 {index} 张多件价海报的件数必须在 2 到 99 之间");
        }

        if (item.ValidFrom.HasValue && item.ValidTo.HasValue && item.ValidFrom.Value.Date > item.ValidTo.Value.Date)
        {
            throw new PromoPosterValidationException($"第 {index} 张海报的有效期开始日期晚于结束日期");
        }

        return new PromoPosterSpec(
            kind,
            style,
            size,
            title,
            string.IsNullOrWhiteSpace(item.ItemNumber) ? null : item.ItemNumber.Trim(),
            price,
            kind is PromoPosterKind.Special or PromoPosterKind.Clearance ? item.WasPrice : null,
            quantity,
            kind == PromoPosterKind.MultiBuy ? item.UnitPrice : null,
            kind == PromoPosterKind.MultiBuy && item.MixAndMatch,
            kind is PromoPosterKind.Special or PromoPosterKind.MultiBuy ? item.ValidFrom?.Date : null,
            kind is PromoPosterKind.Special or PromoPosterKind.MultiBuy ? item.ValidTo?.Date : null,
            kind == PromoPosterKind.New ? item.InStoreSince?.Date : null
        );
    }

    /// <summary>合并连续空白、去掉首尾空白；弯引号等排版字符保留（字体里有）。</summary>
    public static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        return string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static void EnsureMoney(decimal value, int index, string label)
    {
        if (value < 0 || value > 99999.99m) throw new PromoPosterValidationException($"第 {index} 张海报的{label}超出范围");
    }

    private static PromoPosterKind ParseKind(string? value, int index) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "special" => PromoPosterKind.Special,
            "multibuy" or "multi-buy" => PromoPosterKind.MultiBuy,
            "new" => PromoPosterKind.New,
            "clearance" => PromoPosterKind.Clearance,
            _ => throw new PromoPosterValidationException($"第 {index} 张海报的类型无效"),
        };

    private static PromoPosterStyle ParseStyle(string? value, int index) =>
        (value ?? "classic").Trim().ToLowerInvariant() switch
        {
            "" or "classic" => PromoPosterStyle.Classic,
            "modern" => PromoPosterStyle.Modern,
            _ => throw new PromoPosterValidationException($"第 {index} 张海报的风格无效"),
        };

    private static PromoPosterSize ParseSize(string? value, int index) =>
        (value ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "A4" => PromoPosterSize.A4,
            "A5" => PromoPosterSize.A5,
            "A6" => PromoPosterSize.A6,
            "A7" => PromoPosterSize.A7,
            _ => throw new PromoPosterValidationException($"第 {index} 张海报的尺寸无效"),
        };
}

/// <summary>海报上的文字格式（金额、日期），统一英文格式。</summary>
public static class PromoPosterText
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Money(decimal value) => "$" + value.ToString("0.00", Invariant);

    /// <summary>拆成整数与两位角分，用于大价格排版。</summary>
    public static (string Dollars, string Cents) SplitPrice(decimal value)
    {
        var text = Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", Invariant);
        var dot = text.IndexOf('.');
        return (text[..dot], text[(dot + 1)..]);
    }

    public static string Day(DateTime date) => date.ToString("d MMM", Invariant);

    public static string DayYear(DateTime date) => date.ToString("d MMM yyyy", Invariant);

    /// <summary>有效期文字：完整版「Valid 19 Sep – 2 Oct 2026」，A7 短版「Until 2 Oct」。</summary>
    public static string? Validity(DateTime? from, DateTime? to, bool shortText)
    {
        if (!to.HasValue) return from.HasValue ? (shortText ? $"From {Day(from.Value)}" : $"From {DayYear(from.Value)}") : null;
        if (shortText) return $"Until {Day(to.Value)}";
        return from.HasValue ? $"Valid {Day(from.Value)} – {DayYear(to.Value)}" : $"Valid until {DayYear(to.Value)}";
    }
}
