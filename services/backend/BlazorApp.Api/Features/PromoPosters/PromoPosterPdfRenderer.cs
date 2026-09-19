using iTextSharp.text;
using iTextSharp.text.pdf;

namespace BlazorApp.Api.Features.PromoPosters;

/// <summary>
/// 把海报规格排成 PDF：
/// - 不拼版：每张海报单独一页，页面为该海报的实际纸张尺寸；
/// - 拼版：A4 每张一页；A5 两张拼一页（A4 横向）、A6 四张（A4 纵向）、A7 八张（A4 横向），格子之间画虚线裁切线。
/// A 系列纸张正好对半分，拼版后没有缝隙；海报自带 4–6mm 白边，裁切线落在白边上，普通打印机的不可打印区也不会切到内容。
/// </summary>
public static class PromoPosterPdfRenderer
{
    private const float MmToPt = 72f / 25.4f;
    private static readonly BaseColor CutLine = PosterCanvas.Hex("#98A2B3");

    /// <summary>纸张尺寸（毫米，纵向）。</summary>
    private static (float W, float H) PaperMm(PromoPosterSize size) => size switch
    {
        PromoPosterSize.A4 => (210, 297),
        PromoPosterSize.A5 => (148, 210),
        PromoPosterSize.A6 => (105, 148),
        _ => (74, 105),
    };

    /// <summary>拼版方式：每页几列几行、A4 是否横放。</summary>
    internal static (int Cols, int Rows, bool Landscape) Imposition(PromoPosterSize size) => size switch
    {
        PromoPosterSize.A4 => (1, 1, false),
        PromoPosterSize.A5 => (2, 1, true),
        PromoPosterSize.A6 => (2, 2, false),
        _ => (4, 2, true),
    };

    /// <summary>预计页数，供 App 展示与测试断言。</summary>
    public static int CountPages(IReadOnlyList<PromoPosterSpec> specs, bool impose)
    {
        if (!impose) return specs.Count;
        return specs.GroupBy(s => s.Size).Sum(g =>
        {
            var (cols, rows, _) = Imposition(g.Key);
            return (int)Math.Ceiling(g.Count() / (double)(cols * rows));
        });
    }

    public static byte[] Render(IReadOnlyList<PromoPosterSpec> specs, bool impose)
    {
        if (specs.Count == 0) throw new ArgumentException("没有可生成的海报", nameof(specs));

        using var stream = new MemoryStream();
        var first = FirstPageSize(specs, impose);
        using var document = new Document(first, 0, 0, 0, 0);
        var writer = PdfWriter.GetInstance(document, stream);
        document.AddTitle("Hot Bargain promo posters");
        document.AddCreator("HB Platform");
        document.Open();

        // 同一张 logo 在整份文档里只写一次（iText 按 Image 实例复用 XObject）
        var logoBytes = PromoPosterAssets.LogoBytes;
        var logo = logoBytes != null ? Image.GetInstance(logoBytes) : null;

        var firstPage = true;
        void NewPage(Rectangle size)
        {
            document.SetPageSize(size);
            // 第一页在 Open 时已按 FirstPageSize 建好；之后每次先设尺寸再换页（每页都会画内容，不会被当成空白页跳过）
            if (!firstPage) document.NewPage();
            else firstPage = false;
        }

        if (!impose)
        {
            foreach (var spec in specs)
            {
                var page = PageRect(spec.Size, landscapeA4: false);
                NewPage(page);
                PaintPoster(writer.DirectContent, spec, 0, page.Height, page.Width, page.Height, logo);
            }
        }
        else
        {
            // 按尺寸分组（保持首次出现的顺序），每组依次铺满拼版页
            foreach (var group in specs.GroupBy(s => s.Size))
            {
                var (cols, rows, landscape) = Imposition(group.Key);
                var sheet = group.Key == PromoPosterSize.A4 ? PageRect(PromoPosterSize.A4, false) : PageRect(PromoPosterSize.A4, landscape);
                var (cellWmm, cellHmm) = PaperMm(group.Key);
                var cellW = cellWmm * MmToPt;
                var cellH = cellHmm * MmToPt;
                var gridX = (sheet.Width - cols * cellW) / 2;
                var gridTop = sheet.Height - (sheet.Height - rows * cellH) / 2;
                var perPage = cols * rows;
                var items = group.ToList();
                for (var start = 0; start < items.Count; start += perPage)
                {
                    NewPage(sheet);
                    var cb = writer.DirectContent;
                    var pageItems = items.Skip(start).Take(perPage).ToList();
                    for (var i = 0; i < pageItems.Count; i++)
                    {
                        var col = i % cols;
                        var row = i / cols;
                        PaintPoster(cb, pageItems[i], gridX + col * cellW, gridTop - row * cellH, cellW, cellH, logo);
                    }
                    if (perPage > 1) CutLines(cb, sheet, cols, rows, gridX, gridTop, cellW, cellH);
                }
            }
        }

        document.Close();
        return stream.ToArray();
    }

    private static Rectangle FirstPageSize(IReadOnlyList<PromoPosterSpec> specs, bool impose)
    {
        var size = specs[0].Size;
        if (!impose || size == PromoPosterSize.A4) return PageRect(size, false);
        return PageRect(PromoPosterSize.A4, Imposition(size).Landscape);
    }

    private static Rectangle PageRect(PromoPosterSize size, bool landscapeA4)
    {
        var (w, h) = PaperMm(size);
        return landscapeA4 ? new Rectangle(h * MmToPt, w * MmToPt) : new Rectangle(w * MmToPt, h * MmToPt);
    }

    /// <summary>在 (left, top) 起、宽高为 w×h 点的格子里画一张海报；设计像素按宽高较小比例缩放并居中。</summary>
    private static void PaintPoster(PdfContentByte cb, PromoPosterSpec spec, float left, float top, float w, float h, Image? logo)
    {
        var (designW, designH) = spec.Style == PromoPosterStyle.Classic
            ? (ClassicPosterPainter.For(spec.Size).W, ClassicPosterPainter.For(spec.Size).H)
            : (ModernPosterPainter.For(spec.Size).W, ModernPosterPainter.For(spec.Size).H);
        var scale = Math.Min(w / designW, h / designH);
        var offsetX = left + (w - designW * scale) / 2;
        var offsetTop = top - (h - designH * scale) / 2;

        cb.SaveState();
        var canvas = new PosterCanvas(cb, offsetX, offsetTop, scale, logo);
        if (spec.Style == PromoPosterStyle.Classic) ClassicPosterPainter.Paint(canvas, spec);
        else ModernPosterPainter.Paint(canvas, spec);
        cb.RestoreState();
    }

    private static void CutLines(PdfContentByte cb, Rectangle sheet, int cols, int rows, float gridX, float gridTop, float cellW, float cellH)
    {
        cb.SaveState();
        cb.SetColorStroke(CutLine);
        cb.SetLineWidth(0.75f);
        cb.SetLineDash(2.25f, 2.25f, 0);
        for (var c = 1; c < cols; c++)
        {
            var x = gridX + c * cellW;
            cb.MoveTo(x, 0);
            cb.LineTo(x, sheet.Height);
        }
        for (var r = 1; r < rows; r++)
        {
            var y = gridTop - r * cellH;
            cb.MoveTo(0, y);
            cb.LineTo(sheet.Width, y);
        }
        cb.Stroke();
        cb.RestoreState();
    }
}
