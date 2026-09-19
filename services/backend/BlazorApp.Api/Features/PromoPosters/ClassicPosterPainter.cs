using iTextSharp.text;
using iTextSharp.text.pdf;

namespace BlazorApp.Api.Features.PromoPosters;

/// <summary>
/// 经典风格（方案 A）：直角卡片 + 类型色细框、顶部横幅大写窄体词、英文品名、大号价格（$ 与角分上标、角分下划线）、
/// WAS / SAVE 信息行、logo 页脚。数值与设计稿生成脚本 gen.py 的 SIZES 与各函数一一对应（单位：设计像素）。
/// </summary>
internal static class ClassicPosterPainter
{
    private static readonly BaseColor Red = PosterCanvas.Hex("#D40F16");
    private static readonly BaseColor Green = PosterCanvas.Hex("#0A7A45");
    private static readonly BaseColor Yellow = PosterCanvas.Hex("#FFD000");
    private static readonly BaseColor Ink = PosterCanvas.Hex("#17181A");
    private static readonly BaseColor Grey = PosterCanvas.Hex("#475467");
    private static readonly BaseColor Line = PosterCanvas.Hex("#D0D5DD");
    private static readonly BaseColor White = BaseColor.White;

    private static BaseFont Black => PromoPosterAssets.ArchivoBlackCondensed;
    private static BaseFont Bold => PromoPosterAssets.ArchivoBold;
    private static BaseFont SemiBold => PromoPosterAssets.ArchivoSemiBold;

    /// <summary>各尺寸版式参数：m 安全白边、b 边框、pad 内边距、bpy/bpx 横幅内边距、K 标签基准字号、n 品名、P 价格上限、wz 信息行、f 页脚字号、sh 斜纹高度、lg logo 高度。</summary>
    internal sealed record Tokens(float W, float H, float M, float B, float Pad, float Bpy, float Bpx, float K, float N, float P, float Wz, float F, float Fpy, float Gap, float Sh, float Lg, bool Short);

    internal static Tokens For(PromoPosterSize size) => size switch
    {
        PromoPosterSize.A4 => new(794, 1123, 23, 4, 36, 22, 28, 150, 60, 430, 34, 17, 12, 18, 18, 50, false),
        PromoPosterSize.A5 => new(559, 794, 19, 3, 24, 15, 20, 104, 40, 290, 25, 13, 9, 12, 13, 36, false),
        PromoPosterSize.A6 => new(397, 559, 19, 3, 16, 11, 14, 72, 27, 200, 18, 12, 7, 8, 10, 28, false),
        _ => new(280, 397, 15, 2, 12, 8, 10, 50, 18, 138, 14, 12, 6, 5, 7, 22, true),
    };

    private static float R(float v) => MathF.Round(v, MidpointRounding.AwayFromZero);

    public static void Paint(PosterCanvas cv, PromoPosterSpec spec)
    {
        var t = For(spec.Size);
        var accent = spec.Kind switch
        {
            PromoPosterKind.New => Green,
            PromoPosterKind.Clearance => Ink,
            _ => Red,
        };

        // 卡片内区（边框以内）
        var x0 = t.M + t.B;
        var y0 = t.M + t.B;
        var cw = t.W - 2 * t.M - 2 * t.B;
        var ch = t.H - 2 * t.M - 2 * t.B;
        var top = y0;
        var bottom = y0 + ch;

        if (spec.Kind == PromoPosterKind.Clearance)
        {
            Stripes(cv, t, x0, top, cw);
            Stripes(cv, t, x0, bottom - t.Sh, cw);
            top += t.Sh;
            bottom -= t.Sh;
        }

        float bodyPadTop;
        switch (spec.Kind)
        {
            case PromoPosterKind.Special:
                top = Band(cv, t, "SPECIAL", x0, top, cw, Red, White);
                break;
            case PromoPosterKind.MultiBuy:
                top = Band(cv, t, "MULTI-BUY", x0, top, cw, Red, White);
                break;
            case PromoPosterKind.Clearance:
                top = Band(cv, t, "CLEARANCE", x0, top, cw, Yellow, Ink);
                break;
            case PromoPosterKind.New:
                top = NewHead(cv, t, x0, top);
                break;
        }
        bodyPadTop = t.Pad;

        // 页脚贴底，价格区贴页脚上方，品名贴正文顶部（中间留白即设计稿里的 flex 弹性空白）
        var footerTop = Footer(cv, t, spec, x0, cw, bottom);
        var contentLeft = x0 + t.Pad;
        var contentWidth = cw - 2 * t.Pad;
        var contentTop = top + bodyPadTop;
        var contentBottom = footerTop - R(t.Pad * 0.8f);

        var nameBottom = Name(cv, t, spec.Title, contentLeft, contentTop, contentWidth);
        if (spec.Kind == PromoPosterKind.MultiBuy && spec.MixAndMatch)
        {
            MixTag(cv, t, $"Mix & match any {spec.Quantity}", contentLeft, nameBottom + t.Gap);
        }

        switch (spec.Kind)
        {
            case PromoPosterKind.Special:
            {
                var info = new List<InfoItem>();
                if (spec.SpecialSaving is { } save)
                {
                    info.Add(InfoItem.Was(PromoPosterText.Money(spec.WasPrice!.Value)));
                    info.Add(InfoItem.Pill($"SAVE {PromoPosterText.Money(save)}", Ink, White));
                }
                var infoTop = InfoRow(cv, t, info, contentLeft, contentWidth, contentBottom);
                var priceBottom = info.Count > 0 ? infoTop - MathF.Max(6, R(t.Gap * 0.8f)) : contentBottom;
                var p = FitPrice(spec.Price, t.P, contentWidth, 0f);
                Price(cv, spec.Price, p, contentLeft, priceBottom, Red);
                break;
            }
            case PromoPosterKind.Clearance:
            {
                var info = new List<InfoItem>();
                if (spec.ClearancePercentOff is { } pct)
                {
                    info.Add(InfoItem.Was(PromoPosterText.Money(spec.WasPrice!.Value)));
                    info.Add(InfoItem.Pill($"{pct}% OFF", Ink, Yellow));
                }
                var infoTop = InfoRow(cv, t, info, contentLeft, contentWidth, contentBottom);
                var priceBottom = info.Count > 0 ? infoTop - MathF.Max(6, R(t.Gap * 0.8f)) : contentBottom;
                // 左侧竖排 NOW 标签约占 0.19 个价格字号的宽度
                var p = FitPrice(spec.Price, R(t.P * 0.88f), contentWidth, 0.19f);
                var labelSize = MathF.Max(12, R(p * 0.15f));
                var labelGap = R(p * 0.04f);
                NowLabel(cv, labelSize, contentLeft, priceBottom - 0.8f * p, 0.8f * p);
                Price(cv, spec.Price, p, contentLeft + labelSize + labelGap, priceBottom, Ink);
                break;
            }
            case PromoPosterKind.New:
            {
                var p = FitPrice(spec.Price, R(t.P * 1.2f), contentWidth, 0f);
                Price(cv, spec.Price, p, contentLeft, contentBottom, Ink);
                break;
            }
            case PromoPosterKind.MultiBuy:
            {
                var info = new List<InfoItem>();
                if (spec.UnitPrice is { } unit && unit > 0)
                {
                    info.Add(InfoItem.Plain($"{PromoPosterText.Money(unit)} EACH"));
                    if (spec.MultiBuySaving is { } save) info.Add(InfoItem.Pill($"SAVE {PromoPosterText.Money(save)}", Ink, White));
                }
                var infoTop = InfoRow(cv, t, info, contentLeft, contentWidth, contentBottom);
                var dealBottom = info.Count > 0 ? infoTop - MathF.Max(6, R(t.Gap * 0.8f)) : contentBottom;
                Deal(cv, spec.Quantity, spec.Price, R(t.P * 0.78f), contentLeft, contentWidth, dealBottom, Red);
                break;
            }
        }

        // 类型色细框最后画，压在横幅等色块之上；CSS 边框在盒子内侧，描边线居中需内缩半个线宽
        cv.StrokeRect(t.M + t.B / 2, t.M + t.B / 2, t.W - 2 * t.M - t.B, t.H - 2 * t.M - t.B, t.B, accent);
    }

    // ---------------------------------------------------------------- 横幅与标题

    private static float Band(PosterCanvas cv, Tokens t, string word, float x0, float top, float cw, BaseColor bg, BaseColor fg)
    {
        // 大字按横幅可用宽度撑满（留 5%），与设计稿 band() 一致
        var avail = cw - 2 * t.Bpx;
        var unit = PosterCanvas.TextWidth(Black, 1, word, 0.01f);
        var fs = MathF.Floor(avail * 0.95f / unit);
        var height = 2 * t.Bpy + 0.86f * fs;
        cv.FillRect(x0, top, cw, height, bg);
        var textWidth = PosterCanvas.TextWidth(Black, fs, word, 0.01f);
        var baseline = PosterCanvas.Baseline(Black, fs, 0.86f, top + t.Bpy);
        cv.Text(Black, fs, word, x0 + (cw - textWidth) / 2, baseline, fg, 0.01f);
        return top + height;
    }

    private static float NewHead(PosterCanvas cv, Tokens t, float x0, float top)
    {
        var headTop = top + t.Pad;
        var tf = R(t.K * 0.92f);
        var notch = R(t.K * 0.28f);
        var tagX = x0 + t.Pad;
        var tagW = t.Bpx + PosterCanvas.TextWidth(Black, tf, "NEW") + t.Bpx + notch;
        var tagH = 2 * t.Bpy + 0.86f * tf;
        // 箭头形标签：右侧尖角
        cv.FillPolygon(new[]
        {
            (tagX, headTop), (tagX + tagW - notch, headTop), (tagX + tagW, headTop + tagH / 2),
            (tagX + tagW - notch, headTop + tagH), (tagX, headTop + tagH),
        }, Green);
        cv.Text(Black, tf, "NEW", tagX + t.Bpx, PosterCanvas.Baseline(Black, tf, 0.86f, headTop + t.Bpy), White);

        var js = R(tf * 0.42f);
        var stackH = 2 * 0.92f * js;
        var stackTop = headTop + (tagH - stackH) / 2;
        var stackX = tagX + tagW + R(t.Bpx * 0.8f);
        cv.Text(Black, js, "JUST", stackX, PosterCanvas.Baseline(Black, js, 0.92f, stackTop), Green, 0.02f);
        cv.Text(Black, js, "ARRIVED", stackX, PosterCanvas.Baseline(Black, js, 0.92f, stackTop + 0.92f * js), Green, 0.02f);
        return headTop + MathF.Max(tagH, stackH);
    }

    private static float Name(PosterCanvas cv, Tokens t, string title, float left, float top, float width)
    {
        var lines = PosterCanvas.Wrap(title, s => PosterCanvas.TextWidth(Bold, t.N, s), width, 2, balanced: true);
        for (var i = 0; i < lines.Count; i++)
        {
            cv.Text(Bold, t.N, lines[i], left, PosterCanvas.Baseline(Bold, t.N, 1.1f, top + i * 1.1f * t.N), Ink);
        }
        return top + lines.Count * 1.1f * t.N;
    }

    private static void MixTag(PosterCanvas cv, Tokens t, string text, float left, float top)
    {
        var border = MathF.Max(1.5f, t.B * 0.6f);
        var padV = R(t.Wz * 0.1f);
        var padH = R(t.Wz * 0.35f);
        var w = PosterCanvas.TextWidth(Bold, t.Wz, text) + 2 * padH + 2 * border;
        var h = 1.2f * t.Wz + 2 * padV + 2 * border;
        cv.StrokeRoundRect(left + border / 2, top + border / 2, w - border, h - border, MathF.Max(2, R(t.Wz * 0.15f)), border, Red);
        cv.Text(Bold, t.Wz, text, left + border + padH, PosterCanvas.Baseline(Bold, t.Wz, 1.2f, top + border + padV), Red);
    }

    private static void Stripes(PosterCanvas cv, Tokens t, float x, float y, float w)
    {
        // 对应 repeating-linear-gradient(-45deg, 黑 0 sw, 黄 sw 2sw)：条纹为「/」方向
        var sw = MathF.Max(5, R(t.Sh * 0.75f));
        var h = t.Sh;
        cv.FillRect(x, y, w, h, Yellow);
        cv.Clipped(x, y, w, h, () =>
        {
            var along = sw * MathF.Sqrt(2);
            var period = 2 * along;
            for (var sx = x - h - period; sx < x + w + period; sx += period)
            {
                cv.FillPolygon(new[] { (sx, y + h), (sx + along, y + h), (sx + along + h, y), (sx + h, y) }, Ink);
            }
        });
    }

    // ---------------------------------------------------------------- 页脚

    private static float Footer(PosterCanvas cv, Tokens t, PromoPosterSpec spec, float x0, float cw, float bottom)
    {
        var lines = FooterLines(spec, t.Short);
        var textH = t.Short || lines.Count <= 1 ? 1.3f * t.F : 2 * 1.3f * t.F + 2;
        var contentH = MathF.Max(t.Lg, textH);
        var height = 1 + 2 * t.Fpy + contentH;
        var footerTop = bottom - height;

        cv.Line(x0, footerTop + 0.5f, x0 + cw, footerTop + 0.5f, 1, Line, dash: 3);
        var innerTop = footerTop + 1 + t.Fpy;
        cv.Logo(x0 + t.Pad, innerTop + (contentH - t.Lg) / 2, t.Lg);

        var right = x0 + cw - t.Pad;
        var blockTop = innerTop + (contentH - textH) / 2;
        if (t.Short || lines.Count <= 1)
        {
            var text = string.Join(" · ", lines);
            cv.TextRight(SemiBold, t.F, text, right, PosterCanvas.Baseline(SemiBold, t.F, 1.3f, blockTop), Grey);
        }
        else
        {
            for (var i = 0; i < lines.Count; i++)
            {
                cv.TextRight(SemiBold, t.F, lines[i], right, PosterCanvas.Baseline(SemiBold, t.F, 1.3f, blockTop + i * (1.3f * t.F + 2)), Grey);
            }
        }
        return footerTop;
    }

    /// <summary>页脚右侧文字：特价 / 多件价为「有效期 + 货号」，新品为「货号 + 上架日期」，清仓为「售完即止 + 货号」。</summary>
    internal static List<string> FooterLines(PromoPosterSpec spec, bool shortText)
    {
        var item = string.IsNullOrWhiteSpace(spec.ItemNumber) ? null : (shortText ? $"#{spec.ItemNumber}" : $"Item {spec.ItemNumber}");
        var lines = new List<string?>();
        switch (spec.Kind)
        {
            case PromoPosterKind.Special:
            case PromoPosterKind.MultiBuy:
                lines.Add(PromoPosterText.Validity(spec.ValidFrom, spec.ValidTo, shortText));
                lines.Add(item);
                break;
            case PromoPosterKind.New:
                lines.Add(item);
                if (spec.InStoreSince is { } since)
                {
                    lines.Add(shortText ? $"Since {PromoPosterText.Day(since)}" : $"In store since {PromoPosterText.DayYear(since)}");
                }
                break;
            case PromoPosterKind.Clearance:
                lines.Add("While stocks last");
                lines.Add(item);
                break;
        }
        return lines.Where(l => !string.IsNullOrEmpty(l)).Select(l => l!).ToList();
    }

    // ---------------------------------------------------------------- 价格

    /// <summary>价格字号：不超过上限，且整行宽度不超过可用宽度的 97%（三位数价格会自动缩小）。</summary>
    internal static float FitPrice(decimal value, float cap, float availableWidth, float leadEm)
    {
        var unit = PriceWidth(value, 1) + leadEm;
        return MathF.Min(cap, MathF.Floor(availableWidth * 0.97f / unit));
    }

    private static float PriceWidth(decimal value, float p)
    {
        var (dollars, cents) = PromoPosterText.SplitPrice(value);
        return PosterCanvas.TextWidth(Black, 0.36f * p, "$") + 0.02f * p
            + PosterCanvas.TextWidth(Black, p, dollars, -0.02f)
            + 0.035f * p + PosterCanvas.TextWidth(Black, 0.42f * p, cents);
    }

    /// <summary>大价格：$ 与角分缩小并与整数顶端对齐，角分下划线。bottom 为价格行盒（行高 0.8）底边。</summary>
    private static void Price(PosterCanvas cv, decimal value, float p, float left, float bottom, BaseColor color)
    {
        var (dollars, cents) = PromoPosterText.SplitPrice(value);
        var baseline = PosterCanvas.Baseline(Black, p, 0.8f, bottom - 0.8f * p);
        var cap = PosterCanvas.CapHeight(Black, 1);
        var s = 0.36f * p;
        var cs = 0.42f * p;

        var x = left;
        cv.Text(Black, s, "$", x, baseline - cap * (p - s), color);
        x += PosterCanvas.TextWidth(Black, s, "$") + 0.02f * p;
        cv.Text(Black, p, dollars, x, baseline, color, -0.02f);
        x += PosterCanvas.TextWidth(Black, p, dollars, -0.02f) + 0.035f * p;
        var centsBaseline = baseline - cap * (p - cs);
        cv.Text(Black, cs, cents, x, centsBaseline, color);
        var thickness = MathF.Max(2, R(p * 0.028f));
        cv.FillRect(x, centsBaseline + R(p * 0.05f), PosterCanvas.TextWidth(Black, cs, cents), thickness, color);
    }

    /// <summary>多件价「3 FOR $10」：件数与金额同为大字，FOR 小字垂直居中；整数金额不显示角分。</summary>
    private static void Deal(PosterCanvas cv, int quantity, decimal value, float cap, float left, float availableWidth, float bottom, BaseColor color)
    {
        var (dollars, cents) = PromoPosterText.SplitPrice(value);
        var qty = quantity.ToString();
        var showCents = cents != "00";
        float Width(float p) =>
            PosterCanvas.TextWidth(Black, p, qty) + 0.12f * p + PosterCanvas.TextWidth(Black, 0.2f * p, "FOR", 0.04f)
            + PosterCanvas.TextWidth(Black, 0.36f * p, "$") + 0.02f * p + PosterCanvas.TextWidth(Black, p, dollars, -0.02f)
            + (showCents ? 0.035f * p + PosterCanvas.TextWidth(Black, 0.42f * p, cents) : 0);
        var p = MathF.Min(cap, MathF.Floor(availableWidth * 0.97f / Width(1)));

        var baseline = PosterCanvas.Baseline(Black, p, 0.8f, bottom - 0.8f * p);
        var capH = PosterCanvas.CapHeight(Black, 1);
        var fz = 0.2f * p;
        var s = 0.36f * p;
        var cs = 0.42f * p;
        var x = left;
        cv.Text(Black, p, qty, x, baseline, color);
        x += PosterCanvas.TextWidth(Black, p, qty) + 0.06f * p;
        cv.Text(Black, fz, "FOR", x, baseline - capH * (p - fz) / 2, color, 0.04f);
        x += PosterCanvas.TextWidth(Black, fz, "FOR", 0.04f) + 0.06f * p;
        cv.Text(Black, s, "$", x, baseline - capH * (p - s), color);
        x += PosterCanvas.TextWidth(Black, s, "$") + 0.02f * p;
        cv.Text(Black, p, dollars, x, baseline, color, -0.02f);
        x += PosterCanvas.TextWidth(Black, p, dollars, -0.02f);
        if (showCents)
        {
            x += 0.035f * p;
            var centsBaseline = baseline - capH * (p - cs);
            cv.Text(Black, cs, cents, x, centsBaseline, color);
            cv.FillRect(x, centsBaseline + R(p * 0.05f), PosterCanvas.TextWidth(Black, cs, cents), MathF.Max(2, R(p * 0.028f)), color);
        }
    }

    /// <summary>清仓价左侧竖排「NOW」（自下而上读），在价格行盒内垂直居中。</summary>
    private static void NowLabel(PosterCanvas cv, float size, float left, float boxTop, float boxHeight)
    {
        var length = PosterCanvas.TextWidth(Bold, size, "NOW", 0.1f) - 0.1f * size;
        var baselineX = left + PosterCanvas.Baseline(Bold, size, 1f, 0);
        var startY = boxTop + boxHeight / 2 + length / 2;
        cv.TextRotated(Bold, size, "NOW", baselineX, startY, 0, 0, 90, Ink, 0.1f);
    }

    // ---------------------------------------------------------------- 信息行（WAS / EACH / SAVE）

    private sealed record InfoItem(string Text, bool IsPill, bool Strike, BaseColor? Bg, BaseColor? Fg)
    {
        public static InfoItem Was(string price) => new(price, false, true, null, null);
        public static InfoItem Plain(string text) => new(text, false, false, null, null);
        public static InfoItem Pill(string text, BaseColor bg, BaseColor fg) => new(text, true, false, bg, fg);
    }

    /// <summary>画在 bottom 之上，返回信息行顶边；放不下一行时第二项换到下一行。</summary>
    private static float InfoRow(PosterCanvas cv, Tokens t, List<InfoItem> items, float left, float width, float bottom)
    {
        if (items.Count == 0) return bottom;
        var padV = R(t.Wz * 0.12f);
        var padH = R(t.Wz * 0.35f);
        var rowH = 1.2f * t.Wz + 2 * padV;
        var gap = MathF.Max(6, R(t.Wz * 0.45f));

        float ItemWidth(InfoItem item) => item.IsPill
            ? PosterCanvas.TextWidth(Bold, t.Wz, item.Text) + 2 * padH
            : PosterCanvas.TextWidth(Bold, t.Wz, item.Strike ? "WAS " + item.Text : item.Text);

        var total = items.Sum(ItemWidth) + gap * (items.Count - 1);
        var rows = total <= width ? new List<List<InfoItem>> { items } : items.Select(i => new List<InfoItem> { i }).ToList();
        var blockH = rows.Count * rowH + (rows.Count - 1) * gap;
        var top = bottom - blockH;

        for (var r = 0; r < rows.Count; r++)
        {
            var rowTop = top + r * (rowH + gap);
            var x = left;
            foreach (var item in rows[r])
            {
                if (item.IsPill)
                {
                    var w = ItemWidth(item);
                    cv.FillRoundRect(x, rowTop, w, rowH, MathF.Max(2, R(t.Wz * 0.15f)), item.Bg!);
                    cv.Text(Bold, t.Wz, item.Text, x + padH, PosterCanvas.Baseline(Bold, t.Wz, 1.2f, rowTop + padV), item.Fg!);
                    x += w + gap;
                }
                else
                {
                    var baseline = PosterCanvas.Baseline(Bold, t.Wz, 1.2f, rowTop + padV);
                    if (item.Strike)
                    {
                        cv.Text(Bold, t.Wz, "WAS ", x, baseline, Ink);
                        var px = x + PosterCanvas.TextWidth(Bold, t.Wz, "WAS ");
                        var pw = PosterCanvas.TextWidth(Bold, t.Wz, item.Text);
                        cv.Text(Bold, t.Wz, item.Text, px, baseline, Ink);
                        var thick = MathF.Max(2, R(t.Wz * 0.09f));
                        cv.FillRect(px, baseline - 0.28f * t.Wz - thick / 2, pw, thick, Red);
                        x = px + pw + gap;
                    }
                    else
                    {
                        cv.Text(Bold, t.Wz, item.Text, x, baseline, Ink);
                        x += ItemWidth(item) + gap;
                    }
                }
            }
        }
        return top;
    }
}
