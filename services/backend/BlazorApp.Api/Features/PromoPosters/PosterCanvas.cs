using iTextSharp.text;
using iTextSharp.text.pdf;

namespace BlazorApp.Api.Features.PromoPosters;

/// <summary>
/// 海报绘图辅助：版式参数沿用设计稿的 CSS 像素（96dpi、左上角为原点、y 向下），
/// 这里统一换算成 PDF 坐标（点、左下角为原点、y 向上），画家类只需要按设计稿的数值写。
/// </summary>
internal sealed class PosterCanvas
{
    private readonly PdfContentByte _cb;
    private readonly float _originX;
    private readonly float _originTop;
    private readonly Image? _logo;

    /// <summary>1 个设计像素对应的 PDF 点数（约 0.75）。</summary>
    public float Scale { get; }

    public PosterCanvas(PdfContentByte cb, float originX, float originTop, float scale, Image? logo)
    {
        _cb = cb;
        _originX = originX;
        _originTop = originTop;
        Scale = scale;
        _logo = logo;
    }

    private float X(float px) => _originX + px * Scale;

    private float Y(float py) => _originTop - py * Scale;

    public static BaseColor Hex(string hex)
    {
        var v = Convert.ToInt32(hex.TrimStart('#'), 16);
        return new BaseColor((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
    }

    // ---------------------------------------------------------------- 形状

    public void FillRect(float x, float y, float w, float h, BaseColor color)
    {
        _cb.SetColorFill(color);
        _cb.Rectangle(X(x), Y(y + h), w * Scale, h * Scale);
        _cb.Fill();
    }

    /// <summary>描边矩形：线宽居中压在矩形边上（与 CSS border 从外沿向内不同，调用方自行内缩半个线宽）。</summary>
    public void StrokeRect(float x, float y, float w, float h, float lineWidth, BaseColor color)
    {
        _cb.SetColorStroke(color);
        _cb.SetLineWidth(lineWidth * Scale);
        _cb.Rectangle(X(x), Y(y + h), w * Scale, h * Scale);
        _cb.Stroke();
    }

    public void FillRoundRect(float x, float y, float w, float h, float radius, BaseColor color)
    {
        _cb.SetColorFill(color);
        _cb.RoundRectangle(X(x), Y(y + h), w * Scale, h * Scale, Math.Min(radius, Math.Min(w, h) / 2) * Scale);
        _cb.Fill();
    }

    public void StrokeRoundRect(float x, float y, float w, float h, float radius, float lineWidth, BaseColor color)
    {
        _cb.SetColorStroke(color);
        _cb.SetLineWidth(lineWidth * Scale);
        _cb.RoundRectangle(X(x), Y(y + h), w * Scale, h * Scale, Math.Min(radius, Math.Min(w, h) / 2) * Scale);
        _cb.Stroke();
    }

    public void FillCircle(float cx, float cy, float radius, BaseColor color)
    {
        _cb.SetColorFill(color);
        _cb.Circle(X(cx), Y(cy), radius * Scale);
        _cb.Fill();
    }

    public void FillPolygon(IReadOnlyList<(float X, float Y)> points, BaseColor color)
    {
        _cb.SetColorFill(color);
        _cb.MoveTo(X(points[0].X), Y(points[0].Y));
        for (var i = 1; i < points.Count; i++) _cb.LineTo(X(points[i].X), Y(points[i].Y));
        _cb.ClosePath();
        _cb.Fill();
    }

    public void Line(float x1, float y1, float x2, float y2, float lineWidth, BaseColor color, float dash = 0)
    {
        _cb.SaveState();
        _cb.SetColorStroke(color);
        _cb.SetLineWidth(lineWidth * Scale);
        if (dash > 0) _cb.SetLineDash(dash * Scale, dash * Scale, 0);
        _cb.MoveTo(X(x1), Y(y1));
        _cb.LineTo(X(x2), Y(y2));
        _cb.Stroke();
        _cb.RestoreState();
    }

    /// <summary>在矩形范围内裁切后执行绘制（清仓斜纹用）。</summary>
    public void Clipped(float x, float y, float w, float h, Action draw)
    {
        _cb.SaveState();
        _cb.Rectangle(X(x), Y(y + h), w * Scale, h * Scale);
        _cb.Clip();
        _cb.NewPath();
        draw();
        _cb.RestoreState();
    }

    // ---------------------------------------------------------------- 文字

    /// <summary>文字宽度（设计像素），含 CSS letter-spacing（每个字符后都加，包括最后一个）。</summary>
    public static float TextWidth(BaseFont font, float size, string text, float letterSpacingEm = 0) =>
        font.GetWidthPoint(text, size) + text.Length * letterSpacingEm * size;

    /// <summary>大写字母高度（设计像素）。</summary>
    public static float CapHeight(BaseFont font, float size) => font.GetFontDescriptor(BaseFont.CAPHEIGHT, size);

    /// <summary>
    /// 按 CSS 行盒规则算基线：行高 lineHeightEm × size 的盒子里，字体 ascent+descent 的内容区垂直居中，
    /// 基线在内容区 ascent 处。这样 PDF 与设计稿（浏览器渲染）的文字位置一致。
    /// </summary>
    public static float Baseline(BaseFont font, float size, float lineHeightEm, float lineTop)
    {
        var ascent = font.GetFontDescriptor(BaseFont.AWT_ASCENT, size);
        var descent = -font.GetFontDescriptor(BaseFont.AWT_DESCENT, size);
        var halfLeading = (lineHeightEm * size - (ascent + descent)) / 2f;
        return lineTop + halfLeading + ascent;
    }

    public void Text(BaseFont font, float size, string text, float x, float baseline, BaseColor color, float letterSpacingEm = 0)
    {
        if (string.IsNullOrEmpty(text)) return;
        _cb.BeginText();
        _cb.SetFontAndSize(font, size * Scale);
        _cb.SetColorFill(color);
        _cb.SetCharacterSpacing(letterSpacingEm * size * Scale);
        _cb.SetTextMatrix(X(x), Y(baseline));
        _cb.ShowText(text);
        _cb.SetCharacterSpacing(0);
        _cb.EndText();
    }

    /// <summary>右对齐文字：x 为文字右端（不含末尾字距）。</summary>
    public void TextRight(BaseFont font, float size, string text, float right, float baseline, BaseColor color, float letterSpacingEm = 0)
    {
        var width = TextWidth(font, size, text, letterSpacingEm) - letterSpacingEm * size;
        Text(font, size, text, right - width, baseline, color, letterSpacingEm);
    }

    /// <summary>
    /// 旋转文字：以 (cx, cy) 为旋转中心，(dx, baseline) 为未旋转时相对中心的位置（设计像素，y 向下），
    /// degreesCcw 为逆时针角度（CSS rotate(-10deg) 对应 +10）。
    /// </summary>
    public void TextRotated(BaseFont font, float size, string text, float cx, float cy, float dx, float dy, float degreesCcw, BaseColor color, float letterSpacingEm = 0)
    {
        if (string.IsNullOrEmpty(text)) return;
        var rad = degreesCcw * Math.PI / 180d;
        var cos = (float)Math.Cos(rad);
        var sin = (float)Math.Sin(rad);
        // 相对中心的偏移先换成 PDF 方向（y 向上），再旋转
        var ox = dx * Scale;
        var oy = -dy * Scale;
        var rx = ox * cos - oy * sin;
        var ry = ox * sin + oy * cos;
        _cb.BeginText();
        _cb.SetFontAndSize(font, size * Scale);
        _cb.SetColorFill(color);
        _cb.SetCharacterSpacing(letterSpacingEm * size * Scale);
        _cb.SetTextMatrix(cos, sin, -sin, cos, X(cx) + rx, Y(cy) + ry);
        _cb.ShowText(text);
        _cb.SetCharacterSpacing(0);
        _cb.EndText();
    }

    // ---------------------------------------------------------------- 图片

    /// <summary>画 logo：给定高度按原比例缩放，返回实际宽度（设计像素）；没有 logo 返回 0。</summary>
    public float Logo(float x, float y, float height)
    {
        if (_logo == null) return 0;
        var width = height * _logo.Width / _logo.Height;
        _logo.ScaleAbsolute(width * Scale, height * Scale);
        _logo.SetAbsolutePosition(X(x), Y(y + height));
        _cb.AddImage(_logo);
        return width;
    }

    public float LogoWidth(float height) => _logo == null ? 0 : height * _logo.Width / _logo.Height;

    // ---------------------------------------------------------------- 排版工具

    /// <summary>
    /// 品名折行：最多 maxLines 行。balanced=true 时两行尽量等宽（对应设计稿 text-wrap: balance），
    /// 放不下时最后一行截断加省略号。
    /// </summary>
    public static List<string> Wrap(string text, Func<string, float> measure, float maxWidth, int maxLines, bool balanced)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return new List<string>();
        if (measure(text) <= maxWidth) return new List<string> { text };

        if (balanced && maxLines >= 2)
        {
            // 在所有两行拆法里，选两行都放得下且较宽一行最窄的那种
            string? best1 = null, best2 = null;
            var bestMax = float.MaxValue;
            for (var i = 1; i < words.Length; i++)
            {
                var l1 = string.Join(' ', words[..i]);
                var l2 = string.Join(' ', words[i..]);
                var w1 = measure(l1);
                var w2 = measure(l2);
                if (w1 <= maxWidth && w2 <= maxWidth && Math.Max(w1, w2) < bestMax)
                {
                    bestMax = Math.Max(w1, w2);
                    best1 = l1;
                    best2 = l2;
                }
            }
            if (best1 != null && best2 != null) return new List<string> { best1, best2 };
        }

        // 贪心填充：前 maxLines-1 行放得下就换行；最后一行把剩余单词全放进去，超宽再加省略号
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            var onLastLine = lines.Count == maxLines - 1;
            if (current.Length == 0 || onLastLine || measure(candidate) <= maxWidth)
            {
                current = candidate;
                continue;
            }
            lines.Add(Ellipsize(current, measure, maxWidth));
            current = word;
        }
        if (current.Length > 0) lines.Add(Ellipsize(current, measure, maxWidth));
        return lines;
    }

    private static string Ellipsize(string text, Func<string, float> measure, float maxWidth)
    {
        if (measure(text) <= maxWidth) return text;
        const string ellipsis = "...";
        var t = text;
        while (t.Length > 0 && measure(t.TrimEnd() + ellipsis) > maxWidth) t = t[..^1];
        return t.TrimEnd() + ellipsis;
    }
}
