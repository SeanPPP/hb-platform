using iTextSharp.text;
using iTextSharp.text.pdf;

namespace BlazorApp.Api.Features.PromoPosters;

/// <summary>
/// 现代风格（方案 B）：圆角色块卡片 + 白色信息面板、小写几何字标题（Outfit）、高窄价格数字（Big Shoulders）、
/// 品名（Bricolage Grotesque）、标题行右侧斜贴圆形贴纸（只压面板顶部留白，不遮文字）。
/// 数值与设计稿生成脚本 posters_b.py 的 SZB / THEME 一一对应（单位：设计像素）。
/// </summary>
internal static class ModernPosterPainter
{
    private static readonly BaseColor Ink = PosterCanvas.Hex("#17181A");
    private static readonly BaseColor Muted = PosterCanvas.Hex("#475467");
    private static readonly BaseColor White = BaseColor.White;

    private static BaseFont Word => PromoPosterAssets.OutfitBold;
    private static BaseFont Num => PromoPosterAssets.BigShouldersBold;
    private static BaseFont NameFont => PromoPosterAssets.BricolageBold;

    /// <summary>配色：Bg 色块、On 色块上文字、Price 面板内价格、Sticker 贴纸底色与文字、Accent 多件价「for」。</summary>
    private sealed record Theme(BaseColor Bg, BaseColor On, BaseColor Price, string Word, BaseColor StickerBg, BaseColor StickerFg, BaseColor Accent);

    private static Theme ThemeOf(PromoPosterKind kind) => kind switch
    {
        PromoPosterKind.Special => new(PosterCanvas.Hex("#E4252C"), White, PosterCanvas.Hex("#E4252C"), "special", Ink, White, Ink),
        PromoPosterKind.MultiBuy => new(PosterCanvas.Hex("#FF6B00"), Ink, Ink, "multi-buy", Ink, White, PosterCanvas.Hex("#E25A00")),
        PromoPosterKind.New => new(PosterCanvas.Hex("#2248F0"), White, PosterCanvas.Hex("#2248F0"), "new in", Ink, White, Ink),
        _ => new(PosterCanvas.Hex("#FFD500"), Ink, Ink, "clearance", Ink, PosterCanvas.Hex("#FFD500"), Ink),
    };

    /// <summary>各尺寸参数：R 卡片圆角、Fp 色块内边距、Wcap 标题字号上限、Pad/Pr 面板内边距与圆角、S 贴纸直径、Info 信息字号。</summary>
    internal sealed record Tokens(float W, float H, float M, float R, float Fp, float Wcap, float Pad, float Pr, float N, float P, float S, float F, float Lg, float Gap, float Info, bool Short);

    internal static Tokens For(PromoPosterSize size) => size switch
    {
        PromoPosterSize.A4 => new(794, 1123, 23, 32, 24, 190, 32, 24, 50, 380, 176, 16, 40, 20, 30, false),
        PromoPosterSize.A5 => new(559, 794, 19, 24, 17, 134, 22, 18, 35, 265, 124, 13, 28, 14, 21, false),
        PromoPosterSize.A6 => new(397, 559, 19, 18, 12, 95, 15, 14, 24, 185, 88, 12, 22, 10, 15, false),
        _ => new(280, 397, 15, 12, 9, 66, 11, 10, 17, 128, 62, 12, 18, 7, 12, true),
    };

    private static float Rn(float v) => MathF.Round(v, MidpointRounding.AwayFromZero);

    public static void Paint(PosterCanvas cv, PromoPosterSpec spec)
    {
        var t = For(spec.Size);
        var th = ThemeOf(spec.Kind);

        var x0 = t.M;
        var y0 = t.M;
        var cw = t.W - 2 * t.M;
        var ch = t.H - 2 * t.M;
        cv.FillRoundRect(x0, y0, cw, ch, t.R, th.Bg);

        var fx = x0 + t.Fp;
        var fw = cw - 2 * t.Fp;
        var footerTop = Footer(cv, t, th, spec, fx, fw, y0 + ch - t.Fp);

        // 贴纸内容；没有可展示的优惠（例如未填原价）时不画贴纸，标题可占满整行
        var sticker = StickerLines(spec);
        var drop = Rn(t.Gap + t.Pad * 0.85f);

        // 标题行：标题词左下对齐，贴纸右侧、向下伸进面板顶部留白
        var rowTop = y0 + t.Fp + Rn(t.Fp * 0.15f);
        var unit = PosterCanvas.TextWidth(Word, 1, th.Word, -0.035f);
        var ws = MathF.Min(t.Wcap, MathF.Floor((fw - (sticker != null ? t.S * 1.08f : 0)) / unit * 0.97f));
        var rowH = MathF.Max(0.84f * ws, sticker != null ? t.S - drop : 0);
        var rowBottom = rowTop + rowH;
        cv.Text(Word, ws, th.Word, fx + Rn(ws * 0.02f), PosterCanvas.Baseline(Word, ws, 0.84f, rowBottom - 0.84f * ws), th.On, -0.035f);

        // 白色信息面板
        var panelTop = rowBottom + t.Gap;
        var panelBottom = footerTop - t.Gap;
        cv.FillRoundRect(fx, panelTop, fw, panelBottom - panelTop, t.Pr, White);

        var left = fx + t.Pad;
        var width = fw - 2 * t.Pad;
        var contentTop = panelTop + t.Pad;
        var contentBottom = panelBottom - t.Pad;

        var nameBottom = Name(cv, t, spec.Title, left, contentTop, width);
        if (spec.Kind == PromoPosterKind.MultiBuy && spec.MixAndMatch)
        {
            MixPill(cv, t, th, $"mix & match any {spec.Quantity}", left, nameBottom + t.Gap);
        }

        var info = InfoText(spec);
        var infoTop = contentBottom;
        if (info != null)
        {
            infoTop = contentBottom - 1.2f * t.Info;
            Info(cv, t, info.Value.Text, info.Value.StrikeFrom, left, infoTop);
        }
        var priceBottom = info != null ? infoTop - t.Gap : contentBottom;

        if (spec.Kind == PromoPosterKind.MultiBuy)
        {
            Deal(cv, spec.Quantity, spec.Price, Rn(t.P * 0.92f), left, width, priceBottom, th.Price, th.Accent);
        }
        else
        {
            var p = FitPrice(spec.Price, t.P, width);
            Price(cv, spec.Price, p, left, priceBottom, th.Price);
        }

        // 贴纸最后画，压在面板上
        if (sticker != null)
        {
            var cx = fx + fw - t.S / 2;
            var cy = rowBottom + drop - t.S / 2;
            Sticker(cv, t, th, sticker.Value, cx, cy);
        }
    }

    // ---------------------------------------------------------------- 各块

    private static float Name(PosterCanvas cv, Tokens t, string title, float left, float top, float width)
    {
        var lines = PosterCanvas.Wrap(title, s => PosterCanvas.TextWidth(NameFont, t.N, s, -0.01f), width, 2, balanced: false);
        for (var i = 0; i < lines.Count; i++)
        {
            cv.Text(NameFont, t.N, lines[i], left, PosterCanvas.Baseline(NameFont, t.N, 1.08f, top + i * 1.08f * t.N), Ink, -0.01f);
        }
        return top + lines.Count * 1.08f * t.N;
    }

    private static void MixPill(PosterCanvas cv, Tokens t, Theme th, string text, float left, float top)
    {
        var padV = Rn(t.Info * 0.18f);
        var padH = Rn(t.Info * 0.45f);
        var h = 1.2f * t.Info + 2 * padV;
        var w = PosterCanvas.TextWidth(Word, t.Info, text) + 2 * padH;
        cv.FillRoundRect(left, top, w, h, h / 2, th.Bg);
        cv.Text(Word, t.Info, text, left + padH, PosterCanvas.Baseline(Word, t.Info, 1.2f, top + padV), Ink);
    }

    /// <summary>价格下方的一行说明：特价 / 清仓「was $12.99」（价格划线），多件价「$3.99 each」，新品「just arrived」。</summary>
    private static (string Text, int StrikeFrom)? InfoText(PromoPosterSpec spec) => spec.Kind switch
    {
        PromoPosterKind.Special when spec.SpecialSaving.HasValue => ("was " + PromoPosterText.Money(spec.WasPrice!.Value), 4),
        PromoPosterKind.Clearance when spec.ClearancePercentOff.HasValue => ("was " + PromoPosterText.Money(spec.WasPrice!.Value), 4),
        PromoPosterKind.MultiBuy when spec.UnitPrice is > 0 => (PromoPosterText.Money(spec.UnitPrice!.Value) + " each", -1),
        PromoPosterKind.New => ("just arrived", -1),
        _ => null,
    };

    private static void Info(PosterCanvas cv, Tokens t, string text, int strikeFrom, float left, float top)
    {
        var baseline = PosterCanvas.Baseline(Word, t.Info, 1.2f, top);
        cv.Text(Word, t.Info, text, left, baseline, Muted);
        if (strikeFrom >= 0)
        {
            var sx = left + PosterCanvas.TextWidth(Word, t.Info, text[..strikeFrom]);
            var sw = PosterCanvas.TextWidth(Word, t.Info, text[strikeFrom..]);
            cv.FillRect(sx, baseline - 0.3f * t.Info - 1, sw, 2, Muted);
        }
    }

    private static (string Small, string Big, bool BigFirst)? StickerLines(PromoPosterSpec spec) => spec.Kind switch
    {
        PromoPosterKind.Special when spec.SpecialSaving is { } save => ("SAVE", PromoPosterText.Money(save), false),
        PromoPosterKind.MultiBuy when spec.MultiBuySaving is { } save => ("SAVE", PromoPosterText.Money(save), false),
        PromoPosterKind.Clearance when spec.ClearancePercentOff is { } pct => ("OFF", $"{pct}%", true),
        PromoPosterKind.New => ("IN STORE", "NOW", false),
        _ => null,
    };

    /// <summary>圆形贴纸：两行文字整体逆时针 10°（CSS rotate(-10deg)），块在圆内垂直居中。</summary>
    private static void Sticker(PosterCanvas cv, Tokens t, Theme th, (string Small, string Big, bool BigFirst) s, float cx, float cy)
    {
        cv.FillCircle(cx, cy, t.S / 2, th.StickerBg);
        var smallSize = Rn(t.S * 0.14f);
        var bigSize = Rn(t.S * 0.34f);
        var gap = Rn(t.S * 0.03f);
        var smallH = 1f * smallSize;
        var bigH = 0.9f * bigSize;
        var blockTop = -(smallH + gap + bigH) / 2;

        var lines = s.BigFirst
            ? new[] { (Font: Num, Size: bigSize, Lh: 0.9f, Text: s.Big, Ls: 0f), (Font: Word, Size: smallSize, Lh: 1f, Text: s.Small, Ls: 0.04f) }
            : new[] { (Font: Word, Size: smallSize, Lh: 1f, Text: s.Small, Ls: 0.04f), (Font: Num, Size: bigSize, Lh: 0.9f, Text: s.Big, Ls: 0f) };
        var y = blockTop;
        foreach (var line in lines)
        {
            var w = PosterCanvas.TextWidth(line.Font, line.Size, line.Text, line.Ls) - line.Ls * line.Size;
            var baseline = PosterCanvas.Baseline(line.Font, line.Size, line.Lh, y);
            cv.TextRotated(line.Font, line.Size, line.Text, cx, cy, -w / 2, baseline, 10, th.StickerFg, line.Ls);
            y += line.Lh * line.Size + gap;
        }
    }

    private static float Footer(PosterCanvas cv, Tokens t, Theme th, PromoPosterSpec spec, float left, float width, float bottom)
    {
        var lines = ClassicPosterPainter.FooterLines(spec, t.Short);
        var chipPadV = Rn(t.Lg * 0.12f);
        var chipPadH = Rn(t.Lg * 0.25f);
        var chipH = t.Lg + 2 * chipPadV;
        var textH = t.Short || lines.Count <= 1 ? 1.25f * t.F : 2 * 1.25f * t.F + 2;
        var height = MathF.Max(chipH, textH);
        var top = bottom - height;

        // logo 放在白色圆角小底板上（logo 原图是白底）
        var logoW = cv.LogoWidth(t.Lg);
        if (logoW > 0)
        {
            var chipTop = top + (height - chipH) / 2;
            cv.FillRoundRect(left, chipTop, logoW + 2 * chipPadH, chipH, Rn(t.Lg * 0.25f), White);
            cv.Logo(left + chipPadH, chipTop + chipPadV, t.Lg);
        }

        var right = left + width;
        var blockTop = top + (height - textH) / 2;
        if (t.Short || lines.Count <= 1)
        {
            cv.TextRight(Word, t.F, string.Join(" · ", lines), right, PosterCanvas.Baseline(Word, t.F, 1.25f, blockTop), th.On);
        }
        else
        {
            for (var i = 0; i < lines.Count; i++)
            {
                cv.TextRight(Word, t.F, lines[i], right, PosterCanvas.Baseline(Word, t.F, 1.25f, blockTop + i * (1.25f * t.F + 2)), th.On);
            }
        }
        return top;
    }

    // ---------------------------------------------------------------- 价格

    internal static float FitPrice(decimal value, float cap, float availableWidth)
    {
        var (dollars, cents) = PromoPosterText.SplitPrice(value);
        var unit = PosterCanvas.TextWidth(Num, 0.36f, "$") + 0.02f + PosterCanvas.TextWidth(Num, 1, dollars, -0.01f)
            + 0.03f + PosterCanvas.TextWidth(Num, 0.42f, cents);
        return MathF.Min(cap, MathF.Floor(availableWidth * 0.97f / unit));
    }

    private static void Price(PosterCanvas cv, decimal value, float p, float left, float bottom, BaseColor color)
    {
        var (dollars, cents) = PromoPosterText.SplitPrice(value);
        var baseline = PosterCanvas.Baseline(Num, p, 0.8f, bottom - 0.8f * p);
        var cap = PosterCanvas.CapHeight(Num, 1);
        var s = 0.36f * p;
        var cs = 0.42f * p;
        var x = left;
        cv.Text(Num, s, "$", x, baseline - cap * (p - s), color);
        x += PosterCanvas.TextWidth(Num, s, "$") + 0.02f * p;
        cv.Text(Num, p, dollars, x, baseline, color, -0.01f);
        x += PosterCanvas.TextWidth(Num, p, dollars, -0.01f) + 0.03f * p;
        cv.Text(Num, cs, cents, x, baseline - cap * (p - cs), color);
    }

    /// <summary>多件价「3 for $10」：for 用标题字体小写 + 强调色。</summary>
    private static void Deal(PosterCanvas cv, int quantity, decimal value, float capSize, float left, float availableWidth, float bottom, BaseColor color, BaseColor accent)
    {
        var (dollars, cents) = PromoPosterText.SplitPrice(value);
        var qty = quantity.ToString();
        var showCents = cents != "00";
        float Width(float p) =>
            PosterCanvas.TextWidth(Num, p, qty) + 0.14f * p + PosterCanvas.TextWidth(Word, 0.22f * p, "for")
            + PosterCanvas.TextWidth(Num, 0.36f * p, "$") + 0.02f * p + PosterCanvas.TextWidth(Num, p, dollars, -0.01f)
            + (showCents ? 0.03f * p + PosterCanvas.TextWidth(Num, 0.42f * p, cents) : 0);
        var p = MathF.Min(capSize, MathF.Floor(availableWidth * 0.97f / Width(1)));

        var baseline = PosterCanvas.Baseline(Num, p, 0.8f, bottom - 0.8f * p);
        var cap = PosterCanvas.CapHeight(Num, 1);
        var x = left;
        cv.Text(Num, p, qty, x, baseline, color);
        x += PosterCanvas.TextWidth(Num, p, qty) + 0.07f * p;
        cv.Text(Word, 0.22f * p, "for", x, baseline - 0.7f * p * 0.36f, accent);
        x += PosterCanvas.TextWidth(Word, 0.22f * p, "for") + 0.07f * p;
        var s = 0.36f * p;
        cv.Text(Num, s, "$", x, baseline - cap * (p - s), color);
        x += PosterCanvas.TextWidth(Num, s, "$") + 0.02f * p;
        cv.Text(Num, p, dollars, x, baseline, color, -0.01f);
        if (showCents)
        {
            x += PosterCanvas.TextWidth(Num, p, dollars, -0.01f) + 0.03f * p;
            var cs = 0.42f * p;
            cv.Text(Num, cs, cents, x, baseline - cap * (p - cs), color);
        }
    }
}
