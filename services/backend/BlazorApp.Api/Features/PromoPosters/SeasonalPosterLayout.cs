using iTextSharp.text;
using iTextSharp.text.pdf;

namespace BlazorApp.Api.Features.PromoPosters;

/// <summary>两套节日主题共享业务版式；配色和边角矢量装饰独立于 Logo 开关。</summary>
internal static class SeasonalPosterLayout
{
    internal sealed record Tokens(float W, float H, float M, float Pad, float Header, float Name, float Price,
        float Info, float Footer, float Gap, float Logo, float FooterPad, bool Short);

    internal static Tokens For(PromoPosterSize size) => size switch
    {
        PromoPosterSize.A4 => new(794, 1123, 23, 36, 146, 60, 430, 34, 17, 18, 50, 12, false),
        PromoPosterSize.A5 => new(559, 794, 19, 24, 100, 40, 290, 25, 13, 12, 36, 9, false),
        PromoPosterSize.A6 => new(397, 559, 19, 16, 70, 27, 200, 18, 12, 8, 28, 7, false),
        _ => new(280, 397, 15, 12, 50, 18, 138, 14, 12, 5, 22, 6, true),
    };

    internal static void Paint(PosterCanvas cv, PromoPosterSpec spec, bool halloween)
    {
        var t = For(spec.Size);
        var ink = PosterCanvas.Hex("#181818");
        var paper = PosterCanvas.Hex(halloween ? "#FFF9EE" : "#FFFDF7");
        var accent = PosterCanvas.Hex(halloween ? "#F57616" : "#C6222A");
        var green = PosterCanvas.Hex("#176447");
        var gold = PosterCanvas.Hex("#D7A928");
        var header = halloween ? (spec.Kind == PromoPosterKind.Clearance ? ink : accent)
            : spec.Kind == PromoPosterKind.New ? green : accent;
        var headerText = halloween ? (spec.Kind == PromoPosterKind.Clearance ? accent : ink) : BaseColor.White;
        var priceColor = halloween ? ink : header;
        var left = t.M + t.Pad;
        var width = t.W - 2 * left;
        cv.FillRect(0, 0, t.W, t.H, paper);
        cv.StrokeRect(t.M / 2, t.M / 2, t.W - t.M, t.H - t.M, MathF.Max(2, t.M / 7), accent);
        var headerTop = t.M + 10;
        cv.FillRect(t.M, headerTop, t.W - 2 * t.M, t.Header, header);
        var label = spec.Kind switch
        {
            PromoPosterKind.Special => "SPECIAL",
            PromoPosterKind.MultiBuy => "MULTI-BUY",
            PromoPosterKind.New => "NEW ARRIVAL",
            _ => "CLEARANCE",
        };
        var headingSize = Fit(PromoPosterAssets.ArchivoBlackCondensed, label, t.Header * .88f, width);
        var headingW = PosterCanvas.TextWidth(PromoPosterAssets.ArchivoBlackCondensed, headingSize, label);
        cv.Text(PromoPosterAssets.ArchivoBlackCondensed, headingSize, label, (t.W - headingW) / 2,
            PosterCanvas.Baseline(PromoPosterAssets.ArchivoBlackCondensed, headingSize, 1,
                headerTop + (t.Header - headingSize) / 2), headerText);

        // 固定预留两行页脚和 Logo 区域。隐藏 Logo 不参与任何宽高计算。
        var lineH = 1.25f * t.Footer + 2;
        var footerContentH = MathF.Max(t.Logo, 2 * lineH);
        var footerTop = t.H - t.M - footerContentH - 2 * t.FooterPad;
        if (halloween) HalloweenWebBackground(cv, t, footerTop);
        else ChristmasSnowflakeBackground(cv, t, footerTop);
        cv.Line(left, footerTop, left + width, footerTop, 1, halloween ? PosterCanvas.Hex("#E9D8BF") : gold);
        cv.Logo(left, footerTop + t.FooterPad + (footerContentH - t.Logo) / 2, t.Logo);
        var footerWidth = width - 3.5f * t.Logo - t.Gap;
        var footerLines = ClassicPosterPainter.FooterLines(spec, t.Short);
        var footerTextTop = footerTop + t.FooterPad + (footerContentH - footerLines.Count * lineH) / 2;
        for (var i = 0; i < footerLines.Count; i++)
        {
            var fs = Fit(PromoPosterAssets.ArchivoSemiBold, footerLines[i], t.Footer, footerWidth);
            cv.TextRight(PromoPosterAssets.ArchivoSemiBold, fs, footerLines[i], left + width,
                PosterCanvas.Baseline(PromoPosterAssets.ArchivoSemiBold, fs, 1.25f, footerTextTop + i * lineH), ink);
        }

        // 节日图形使用独立窄条，避免与主标题、品名和页脚共享绘图区。
        var seasonalStripH = SeasonalStripHeight(t);
        var titleTop = headerTop + t.Header + seasonalStripH + t.Pad;
        var titleLines = PosterCanvas.Wrap(spec.Title, s => PosterCanvas.TextWidth(PromoPosterAssets.ArchivoBold, t.Name, s), width, 2, true);
        for (var i = 0; i < titleLines.Count; i++)
            cv.Text(PromoPosterAssets.ArchivoBold, t.Name, titleLines[i], left,
                PosterCanvas.Baseline(PromoPosterAssets.ArchivoBold, t.Name, 1.1f, titleTop + i * 1.1f * t.Name), ink);
        var contentTop = titleTop + 2 * 1.1f * t.Name + t.Gap;
        if (spec.Kind == PromoPosterKind.MultiBuy && spec.MixAndMatch)
        {
            cv.Text(PromoPosterAssets.ArchivoSemiBold, t.Info, $"Mix & match any {spec.Quantity}", left,
                PosterCanvas.Baseline(PromoPosterAssets.ArchivoSemiBold, t.Info, 1.2f, contentTop), ink);
            contentTop += 1.2f * t.Info + t.Gap;
        }

        var info = Info(spec);
        var infoTop = footerTop - t.Pad - 1.25f * t.Info;
        var infoSize = Fit(PromoPosterAssets.ArchivoSemiBold, info, t.Info, width);
        cv.Text(PromoPosterAssets.ArchivoSemiBold, infoSize, info, left,
            PosterCanvas.Baseline(PromoPosterAssets.ArchivoSemiBold, infoSize, 1.25f, infoTop), ink);
        var priceBottom = infoTop - t.Gap;
        // 同时限制宽度和高度，长品名、两位件数、大金额不会侵入说明或页脚。
        var priceCap = MathF.Min(t.Price, MathF.Max(12, (priceBottom - contentTop) / .95f));
        if (spec.Kind == PromoPosterKind.MultiBuy)
            ClassicPosterPainter.Deal(cv, spec.Quantity, spec.Price, priceCap, left, width, priceBottom, priceColor);
        else
            ClassicPosterPainter.Price(cv, spec.Price, ClassicPosterPainter.FitPrice(spec.Price, priceCap, width, 0), left, priceBottom, priceColor);

        if (halloween) HalloweenDecoration(cv, t, accent, ink, paper);
        else ChristmasDecoration(cv, t, accent, green, gold);
    }

    private static string Info(PromoPosterSpec spec) => spec.Kind switch
    {
        PromoPosterKind.Special when spec.SpecialSaving is { } save => $"WAS {PromoPosterText.Money(spec.WasPrice!.Value)}   SAVE {PromoPosterText.Money(save)}",
        PromoPosterKind.Clearance when spec.ClearancePercentOff is { } pct => $"WAS {PromoPosterText.Money(spec.WasPrice!.Value)}   {pct}% OFF",
        PromoPosterKind.MultiBuy when spec.UnitPrice is > 0 => spec.MultiBuySaving is { } save
            ? $"{PromoPosterText.Money(spec.UnitPrice.Value)} EACH   SAVE {PromoPosterText.Money(save)}"
            : $"{PromoPosterText.Money(spec.UnitPrice.Value)} EACH",
        _ => "EACH",
    };

    private static float Fit(BaseFont font, string text, float cap, float width) =>
        MathF.Min(cap, width * .96f / MathF.Max(.01f, PosterCanvas.TextWidth(font, 1, text)));

    private static float SeasonalStripHeight(Tokens t) => t.W switch
    {
        >= 700 => 92,
        >= 500 => 76,
        >= 390 => 60,
        _ => 42,
    };

    private static void HalloweenWebBackground(PosterCanvas cv, Tokens t, float footerTop)
    {
        // 蛛网是内容区的浅色底纹。先于品名、价格绘制，文字始终在最上层。
        var web = PosterCanvas.Hex("#E8D4B9");
        var titleTop = t.M + 10 + t.Header + SeasonalStripHeight(t) + t.Pad;
        DrawWeb(t.W - t.M - 10, titleTop + t.Name * .65f,
            MathF.Min(t.W * .58f, (footerTop - titleTop) * .68f), false);
        DrawWeb(t.M + 12, footerTop - t.Pad * .4f,
            MathF.Min(t.W * .27f, t.H * .13f), true);

        void DrawWeb(float originX, float originY, float radius, bool lowerLeft)
        {
            const int spokes = 7;
            const int rings = 5;
            for (var spoke = 0; spoke < spokes; spoke++)
            {
                var angle = spoke * MathF.PI / (2 * (spokes - 1));
                var x = originX + (lowerLeft ? 1 : -1) * radius * MathF.Cos(angle);
                var y = originY + (lowerLeft ? -1 : 1) * radius * MathF.Sin(angle);
                cv.Line(originX, originY, x, y, MathF.Max(.65f, t.W / 397f), web);
            }
            for (var ring = 1; ring <= rings; ring++)
            {
                var r = radius * ring / rings;
                for (var spoke = 0; spoke < spokes - 1; spoke++)
                {
                    var a = spoke * MathF.PI / (2 * (spokes - 1));
                    var b = (spoke + 1) * MathF.PI / (2 * (spokes - 1));
                    var x1 = originX + (lowerLeft ? 1 : -1) * r * MathF.Cos(a);
                    var y1 = originY + (lowerLeft ? -1 : 1) * r * MathF.Sin(a);
                    var x2 = originX + (lowerLeft ? 1 : -1) * r * MathF.Cos(b);
                    var y2 = originY + (lowerLeft ? -1 : 1) * r * MathF.Sin(b);
                    cv.Line(x1, y1, x2, y2, MathF.Max(.65f, t.W / 397f), web);
                }
            }
        }
    }

    private static void ChristmasSnowflakeBackground(PosterCanvas cv, Tokens t, float footerTop)
    {
        // 雪花只使用淡色线条，放在内容区底层；不影响 Logo 开关或价格布局。
        var snow = PosterCanvas.Hex("#C9E0D1");
        var titleTop = t.M + 10 + t.Header + SeasonalStripHeight(t) + t.Pad;
        var availableH = footerTop - titleTop;
        DrawSnowflake(t.W * .78f, titleTop + availableH * .20f, t.W * .075f);
        DrawSnowflake(t.W * .83f, titleTop + availableH * .67f, t.W * .11f);
        DrawSnowflake(t.W * .22f, titleTop + availableH * .56f, t.W * .052f);

        void DrawSnowflake(float cx, float cy, float r)
        {
            for (var arm = 0; arm < 6; arm++)
            {
                var angle = arm * MathF.PI / 3;
                var dx = MathF.Cos(angle);
                var dy = MathF.Sin(angle);
                var px = -dy;
                var py = dx;
                cv.Line(cx, cy, cx + r*dx, cy + r*dy, MathF.Max(.8f, t.W/397f), snow);
                var bx = cx + r*.68f*dx;
                var by = cy + r*.68f*dy;
                cv.Line(bx, by, bx - r*.2f*dx + r*.15f*px, by - r*.2f*dy + r*.15f*py, MathF.Max(.8f, t.W/397f), snow);
                cv.Line(bx, by, bx - r*.2f*dx - r*.15f*px, by - r*.2f*dy - r*.15f*py, MathF.Max(.8f, t.W/397f), snow);
            }
        }
    }

    private static void ChristmasDecoration(PosterCanvas cv, Tokens t, BaseColor red, BaseColor green, BaseColor gold)
    {
        // 三个主图案占满专属装饰带，四周留白，实际裁切不会截断图形。
        var stripH = SeasonalStripHeight(t);
        // 以装饰带高度缩放，最高的树星与南瓜叶也留在底色内。
        var s = (stripH - 8f) / 42f;
        var stripTop = t.M + 10 + t.Header + 3;
        var top = stripTop + stripH / 2;
        var snow = PosterCanvas.Hex("#FFFDF7");
        cv.FillRoundRect(t.M + 4, stripTop, t.W - 2 * t.M - 8, stripH - 6, 5, PosterCanvas.Hex("#F0F7EF"));
        cv.Line(t.M + 10, stripTop + stripH - 8, t.W - t.M - 10, stripTop + stripH - 8, 1.4f, gold);

        var santaX = t.W * .19f;
        cv.FillCircle(santaX, top + 4*s, 9*s, snow); // 白胡子
        cv.FillCircle(santaX, top - 1*s, 7*s, PosterCanvas.Hex("#FFE2C2"));
        cv.FillPolygon(new[] { (santaX-11*s,top-7*s), (santaX+7*s,top-7*s), (santaX+2*s,top-17*s) }, red);
        cv.FillRoundRect(santaX-11*s, top-8*s, 20*s, 3*s, 1.5f*s, snow);
        cv.FillCircle(santaX+3*s, top-17*s, 2.5f*s, snow);
        cv.FillCircle(santaX-3*s, top-1*s, .8f*s, PosterCanvas.Hex("#181818"));
        cv.FillCircle(santaX+3*s, top-1*s, .8f*s, PosterCanvas.Hex("#181818"));
        cv.FillCircle(santaX, top+4*s, 1.3f*s, red);

        var trainX = t.W * .50f;
        var trainY = top + 4*s;
        cv.FillRoundRect(trainX-23*s, trainY-8*s, 27*s, 11*s, 2*s, red);
        cv.FillRect(trainX+4*s, trainY-13*s, 14*s, 16*s, green); // 驾驶室
        cv.FillRect(trainX+7*s, trainY-10*s, 7*s, 6*s, snow);
        cv.FillRect(trainX-15*s, trainY-14*s, 3*s, 6*s, green); // 烟囱
        cv.FillCircle(trainX-14*s, trainY-19*s, 2.5f*s, snow);
        cv.FillCircle(trainX-19*s, trainY+5*s, 3.7f*s, gold);
        cv.FillCircle(trainX-2*s, trainY+5*s, 3.7f*s, gold);
        cv.FillCircle(trainX+13*s, trainY+5*s, 3.7f*s, gold);
        cv.Line(trainX-24*s, trainY+9*s, trainX+21*s, trainY+9*s, 1.4f*s, gold);

        var treeX = t.W * .81f;
        cv.FillPolygon(new[] { (treeX,top-18*s), (treeX-11*s,top+4*s), (treeX+11*s,top+4*s) }, green);
        cv.FillPolygon(new[] { (treeX,top-10*s), (treeX-14*s,top+10*s), (treeX+14*s,top+10*s) }, green);
        cv.FillRect(treeX-2*s, top+10*s, 4*s, 4*s, red);
        cv.FillCircle(treeX, top-18*s, 2.8f*s, gold);
        cv.FillCircle(treeX-5*s, top+3*s, 1.6f*s, red);
        cv.FillCircle(treeX+6*s, top+5*s, 1.6f*s, gold);
    }

    private static void HalloweenDecoration(PosterCanvas cv, Tokens t, BaseColor orange, BaseColor ink, BaseColor paper)
    {
        var stripTop = t.M + 10 + t.Header + 3;
        var stripH = SeasonalStripHeight(t);
        var s = (stripH - 8f) / 42f;
        cv.FillRoundRect(t.M + 4, stripTop, t.W - 2 * t.M - 8, stripH - 6, 5, PosterCanvas.Hex("#FFE8CA"));
        cv.Line(t.M + 10, stripTop + stripH - 8, t.W - t.M - 10, stripTop + stripH - 8, 1.5f, orange);
        // 图案中心均在内框里，南瓜两侧和蛛网不会再被裁掉。
        var cx = t.W * .16f;
        var cy = stripTop + stripH/2;
        cv.FillCircle(cx-7*s, cy, 10*s, orange);
        cv.FillCircle(cx+7*s, cy, 10*s, orange);
        cv.FillCircle(cx, cy, 12*s, orange);
        cv.Line(cx, cy-10*s, cx+3*s, cy-17*s, 3*s, ink);
        cv.FillPolygon(new[] { (cx+2*s,cy-15*s),(cx+9*s,cy-15*s),(cx+5*s,cy-19*s) }, orange);
        cv.FillPolygon(new[] { (cx-8*s,cy-2*s),(cx-2*s,cy-2*s),(cx-5*s,cy-7*s) },ink);
        cv.FillPolygon(new[] { (cx+2*s,cy-2*s),(cx+8*s,cy-2*s),(cx+5*s,cy-7*s) },ink);
        cv.FillPolygon(new[] { (cx-7*s,cy+5*s),(cx,cy+3*s),(cx+7*s,cy+5*s),(cx,cy+8*s) },ink);
        // 顶部蝙蝠完整置于标题栏右角，中心区域留给主标题。
        var bx = t.W * .84f;
        var by = cy;
        cv.FillPolygon(new[] { (bx-22*s,by-3*s),(bx-14*s,by+8*s),(bx-8*s,by+3*s),(bx-3*s,by+7*s),(bx,by+4*s),(bx+3*s,by+7*s),(bx+8*s,by+3*s),(bx+14*s,by+8*s),(bx+22*s,by-3*s),(bx+9*s,by),(bx+3*s,by-5*s),(bx,by-2*s),(bx-3*s,by-5*s),(bx-9*s,by) },ink);
        // 蝙蝠下方的骷髅徽章放在右侧内框，眼窝和下颌让轮廓在小尺寸也可辨识。
        var skullX = t.W * .61f;
        var skullY = stripTop + stripH/2;
        cv.FillCircle(skullX, skullY, 10*s, ink);
        cv.FillRect(skullX-7*s, skullY+4*s, 14*s, 8*s, ink);
        cv.FillCircle(skullX-4*s, skullY-2*s, 2.5f*s, paper);
        cv.FillCircle(skullX+4*s, skullY-2*s, 2.5f*s, paper);
        cv.FillPolygon(new[] { (skullX-2*s,skullY+4*s),(skullX+2*s,skullY+4*s),(skullX,skullY+1*s) }, paper);
        for (var tooth = -4; tooth <= 4; tooth += 4)
            cv.Line(skullX + tooth*s, skullY+5*s, skullX + tooth*s, skullY+10*s, 1.5f*s, paper);
        // 节日条保留蜘蛛主体，蛛网则转移到内容区作为背景。
        var rx=t.W * .36f; var ry=cy;
        cv.FillCircle(rx, ry, 3*s, ink);
        for (var i = 0; i < 4; i++)
        {
            var dy = (i - 1.5f) * 3.8f * s;
            cv.Line(rx - 2*s, ry + dy*.35f, rx - (8+i)*s, ry + dy, 1*s, ink);
            cv.Line(rx + 2*s, ry + dy*.35f, rx + (8+i)*s, ry + dy, 1*s, ink);
        }
    }

}
