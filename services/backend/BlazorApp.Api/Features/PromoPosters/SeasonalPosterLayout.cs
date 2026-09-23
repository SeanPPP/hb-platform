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

        var titleTop = headerTop + t.Header + t.Pad;
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

    private static void ChristmasDecoration(PosterCanvas cv, Tokens t, BaseColor red, BaseColor green, BaseColor gold)
    {
        // 冬青沿纸边展开，正文安全区从 M + Pad 开始。
        void Holly(float cx, float cy, float scale)
        {
            cv.FillPolygon(new[] { (cx - 20*scale,cy), (cx-13*scale,cy-5*scale), (cx-13*scale,cy-10*scale), (cx-6*scale,cy-6*scale), (cx,cy), (cx-7*scale,cy+5*scale), (cx-14*scale,cy+4*scale) }, green);
            cv.FillPolygon(new[] { (cx,cy), (cx+7*scale,cy-13*scale), (cx+11*scale,cy-10*scale), (cx+18*scale,cy-12*scale), (cx+14*scale,cy-5*scale), (cx+16*scale,cy), (cx+7*scale,cy+3*scale) }, green);
            cv.FillCircle(cx-3*scale,cy,3.5f*scale,red); cv.FillCircle(cx+3*scale,cy+2*scale,3.5f*scale,red); cv.FillCircle(cx,cy-4*scale,3.5f*scale,red);
        }
        var s = t.Short ? .55f : t.M / 22;
        Holly(t.M + 6, t.M / 2 + 3, s);
        Holly(t.W - t.M - 8, t.H - t.M / 2, s);
        cv.Line(t.M + 30*s, t.M/2, t.W-t.M-6, t.M/2, 1, gold);
        if (!t.Short)
        {
            Holly(t.M/2+3, t.H*.68f, .65f*s);
            cv.FillCircle(t.W-t.M/2, t.H*.35f, 3*s, gold);
        }
    }

    private static void HalloweenDecoration(PosterCanvas cv, Tokens t, BaseColor orange, BaseColor ink, BaseColor paper)
    {
        var s = t.Short ? .52f : t.M / 23;
        // 南瓜在左边框；叶柄和面孔均为矢量，不遮挡货号或 Logo。
        var cx=t.M/2+2; var cy=t.H*.72f;
        cv.FillCircle(cx-5*s,cy,8*s,orange); cv.FillCircle(cx+5*s,cy,8*s,orange); cv.FillCircle(cx,cy,9*s,orange);
        cv.Line(cx,cy-8*s,cx+2*s,cy-14*s,3*s,ink);
        cv.FillPolygon(new[] { (cx-6*s,cy-2*s),(cx-2*s,cy-2*s),(cx-4*s,cy-5*s) },ink);
        cv.FillPolygon(new[] { (cx+2*s,cy-2*s),(cx+6*s,cy-2*s),(cx+4*s,cy-5*s) },ink);
        cv.Line(cx-4*s,cy+4*s,cx+4*s,cy+4*s,2*s,ink);
        // 顶部蝙蝠，A7 保留较小轮廓；蛛网只在较大纸张的右上角出现。
        var bx=t.W*.72f; var by=t.M/2+3;
        cv.FillPolygon(new[] { (bx-20*s,by-3*s),(bx-12*s,by+7*s),(bx-8*s,by+2*s),(bx-3*s,by+6*s),(bx,by+3*s),(bx+3*s,by+6*s),(bx+8*s,by+2*s),(bx+12*s,by+7*s),(bx+20*s,by-3*s),(bx+8*s,by),(bx+3*s,by-4*s),(bx,by-1*s),(bx-3*s,by-4*s),(bx-8*s,by) },ink);
        if (t.Short) return;
        var rx=t.W-t.M/2; var ry=t.M/2; var r=t.M*.8f;
        cv.FillRect(rx-r-2, ry-2, r+4, r+4, paper);
        for (var i=0;i<3;i++)
        {
            var a=i*MathF.PI/4;
            cv.Line(rx,ry,rx-r*MathF.Cos(a),ry+r*MathF.Sin(a),.7f,ink);
        }
        for (var ring=1;ring<=3;ring++)
        {
            var rr=r*ring/3;
            cv.Line(rx-rr,ry,rx-rr*.707f,ry+rr*.707f,.6f,ink);
            cv.Line(rx-rr*.707f,ry+rr*.707f,rx,ry+rr,.6f,ink);
        }
    }
}
