using iTextSharp.text;
using iTextSharp.text.pdf;

namespace BlazorApp.Api.Features.PromoPosters;

/// <summary>
/// 省彩墨风格：白纸、黑色正文和价格，只保留顶部促销标签及一条短色线。
/// 所有坐标使用设计稿像素，方便移动端预览与 PDF 保持同一版式。
/// </summary>
internal static class LowInkPosterPainter
{
    private static readonly BaseColor Ink = new GrayColor(0f);
    private static readonly BaseColor Muted = Ink;
    private static readonly BaseColor Red = PosterCanvas.Hex("#C6222A");
    private static readonly BaseColor Green = PosterCanvas.Hex("#176447");

    private static BaseFont LabelFont => PromoPosterAssets.ArchivoBold;
    private static BaseFont NameFont => PromoPosterAssets.ArchivoBold;
    private static BaseFont PriceFont => PromoPosterAssets.ArchivoBlackCondensed;
    private static BaseFont FooterFont => PromoPosterAssets.ArchivoSemiBold;

    internal sealed record Tokens(float W, float H, float M, float Lg, float Label, float N, float P, float Info, float F, float Gap, bool Short);

    internal static Tokens For(PromoPosterSize size) => size switch
    {
        // 促销标题需要在小尺寸海报上保持醒目；沿用设计稿比例放大约 2 倍，
        // 并让短线跟随标题的实际 token 间距，避免压到下面的品名。
        PromoPosterSize.A4 => new(794, 1123, 44, 40, 76, 52, 340, 28, 17, 18, false),
        PromoPosterSize.A5 => new(559, 794, 32, 30, 54, 37, 240, 20, 13, 13, false),
        PromoPosterSize.A6 => new(397, 559, 24, 23, 38, 26, 168, 15, 11, 9, false),
        _ => new(280, 397, 19, 18, 27, 19, 116, 12, 10, 7, true),
    };

    private static BaseColor Accent(PromoPosterKind kind) => kind == PromoPosterKind.New ? Green : Red;

    private static string Label(PromoPosterKind kind) => kind switch
    {
        PromoPosterKind.Special => "SPECIAL",
        PromoPosterKind.MultiBuy => "MULTI-BUY",
        PromoPosterKind.New => "NEW ARRIVAL",
        _ => "CLEARANCE",
    };

    public static void Paint(PosterCanvas cv, PromoPosterSpec spec)
    {
        var t = For(spec.Size);
        var accent = Accent(spec.Kind);
        var left = t.M;
        var right = t.W - t.M;
        var width = right - left;

        // 顶部只印促销字和短色线，避免整页色块消耗彩墨。
        var label = Label(spec.Kind);
        var labelSize = MathF.Min(t.Label, MathF.Floor(width * .96f / PosterCanvas.TextWidth(LabelFont, 1, label, .04f)));
        var labelBaseline = PosterCanvas.Baseline(LabelFont, labelSize, 1.1f, t.M);
        cv.Text(LabelFont, labelSize, label, left, labelBaseline, accent, .04f);
        var lineY = t.M + 1.3f * t.Label + t.Gap;
        cv.Line(left, lineY, left + width * .18f, lineY, MathF.Max(1.5f, t.F * .12f), accent);

        var nameTop = t.H * .18f;
        var nameWidth = width;
        var nameLines = PosterCanvas.Wrap(spec.Title, s => PosterCanvas.TextWidth(NameFont, t.N, s), nameWidth, 3, balanced: true);
        for (var i = 0; i < nameLines.Count; i++)
            cv.Text(NameFont, t.N, nameLines[i], left, PosterCanvas.Baseline(NameFont, t.N, 1.15f, nameTop + i * 1.15f * t.N), Ink);

        if (spec.Kind == PromoPosterKind.MultiBuy)
        {
            var qtyTop = t.H * .39f;
            var qty = $"{spec.Quantity} FOR";
            var qtySize = FitText(LabelFont, qty, t.Info * 1.5f, width);
            cv.Text(LabelFont, qtySize, qty, left, PosterCanvas.Baseline(LabelFont, qtySize, 1.1f, qtyTop), Ink, .04f);
        }

        var info = InfoText(spec);
        var infoTop = t.H * .75f;
        for (var i = 0; i < info.Length; i++)
        {
            var infoSize = FitText(FooterFont, info[i], t.Info, width);
            cv.Text(FooterFont, infoSize, info[i], left, PosterCanvas.Baseline(FooterFont, infoSize, 1.25f, infoTop + i * 1.25f * t.Info), Muted);
        }

        // 固定价格区域的顶部及高度；金额位数增多时只缩小字号，不挤占促销条件。
        var p = FitPrice(spec.Price, MathF.Min(t.P, t.H * .23f), width);
        Price(cv, spec.Price, p, left, t.H * .46f);

        var condition = spec.Kind == PromoPosterKind.MultiBuy
            ? spec.MixAndMatch ? $"MIX & MATCH ANY {spec.Quantity}" : $"FOR {spec.Quantity} ITEMS"
            : "EACH";
        var conditionSize = FitText(FooterFont, condition, t.Info, width);
        cv.Text(FooterFont, conditionSize, condition, left, PosterCanvas.Baseline(FooterFont, conditionSize, 1.1f, t.H * .70f), Ink);

        Footer(cv, t, spec, left, right);
    }

    private static string[] InfoText(PromoPosterSpec spec) => spec.Kind switch
    {
        PromoPosterKind.Special or PromoPosterKind.Clearance when spec.SpecialSaving is { } save =>
            new[] { $"WAS {PromoPosterText.Money(spec.WasPrice!.Value)}", $"SAVE {PromoPosterText.Money(save)}" },
        PromoPosterKind.MultiBuy when spec.UnitPrice is > 0 => spec.MultiBuySaving is { } save
            ? new[] { $"{PromoPosterText.Money(spec.UnitPrice.Value)} EACH", $"SAVE {PromoPosterText.Money(save)} WHEN YOU BUY {spec.Quantity}" }
            : new[] { $"{PromoPosterText.Money(spec.UnitPrice.Value)} EACH" },
        _ => Array.Empty<string>(),
    };

    private static float FitText(BaseFont font, string text, float cap, float width) =>
        MathF.Min(cap, width / MathF.Max(1, PosterCanvas.TextWidth(font, 1, text)));

    private static float FitPrice(decimal value, float cap, float availableWidth)
    {
        var (dollars, cents) = PromoPosterText.SplitPrice(value);
        var unit = PosterCanvas.TextWidth(PriceFont, .36f, "$") + .02f + PosterCanvas.TextWidth(PriceFont, 1, dollars, -.02f) + .035f + PosterCanvas.TextWidth(PriceFont, .42f, cents);
        return MathF.Min(cap, MathF.Floor(availableWidth * .96f / unit));
    }

    private static void Price(PosterCanvas cv, decimal value, float size, float left, float top)
    {
        var (dollars, cents) = PromoPosterText.SplitPrice(value);
        var baseline = PosterCanvas.Baseline(PriceFont, size, 1f, top);
        var cap = PosterCanvas.CapHeight(PriceFont, 1);
        var small = .36f * size;
        var centsSize = .42f * size;
        var x = left;
        cv.Text(PriceFont, small, "$", x, baseline - cap * (size - small), Ink);
        x += PosterCanvas.TextWidth(PriceFont, small, "$") + .02f * size;
        cv.Text(PriceFont, size, dollars, x, baseline, Ink, -.02f);
        x += PosterCanvas.TextWidth(PriceFont, size, dollars, -.02f) + .035f * size;
        cv.Text(PriceFont, centsSize, cents, x, baseline - cap * (size - centsSize), Ink);
    }

    private static void Footer(PosterCanvas cv, Tokens t, PromoPosterSpec spec, float left, float right)
    {
        var logoW = cv.LogoWidth(t.Lg);
        if (logoW > 0) cv.Logo(left, t.H - t.M - t.Lg, t.Lg);
        var dividerY = t.H * .835f;
        cv.Line(left, dividerY, right, dividerY, MathF.Max(1f, t.F * .06f), Ink);
        var top = t.H * .85f;
        var topLine = spec.Kind switch
        {
            PromoPosterKind.Clearance => "While stocks last",
            PromoPosterKind.New when spec.InStoreSince is { } since => $"In store since {(t.Short ? PromoPosterText.Day(since) : PromoPosterText.DayYear(since))}",
            PromoPosterKind.Special or PromoPosterKind.MultiBuy => PromoPosterText.Validity(spec.ValidFrom, spec.ValidTo, t.Short),
            _ => null,
        };
        if (!string.IsNullOrWhiteSpace(topLine))
        {
            var fontSize = FitText(FooterFont, topLine, t.F, right - left);
            cv.Text(FooterFont, fontSize, topLine, left, PosterCanvas.Baseline(FooterFont, fontSize, 1.2f, top), Muted);
        }
        if (!string.IsNullOrWhiteSpace(spec.ItemNumber))
        {
            var item = $"Item {spec.ItemNumber}";
            var fontSize = FitText(FooterFont, item, t.F, right - left - logoW - (logoW > 0 ? t.Gap : 0));
            cv.TextRight(FooterFont, fontSize, item, right, PosterCanvas.Baseline(FooterFont, fontSize, 1.2f, t.H - t.M - fontSize * 1.2f), Muted);
        }
    }
}
