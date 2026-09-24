using iTextSharp.text.pdf;

namespace BlazorApp.Api.Features.PromoPosters;

/// <summary>
/// 海报用到的字体与 logo。字体均为 OFL 开源字体（许可证与字体放在同一目录），以子集方式嵌入 PDF，
/// 保证任何打印机 / 阅读器输出一致。BaseFont 可跨文档复用，这里做进程级缓存。
/// </summary>
public static class PromoPosterAssets
{
    private const string FontFolder = "Fonts/PromoPosters";

    private static readonly Lazy<BaseFont> ArchivoBlackCondensedLazy = new(() => Load("ArchivoExtraCondensed-Black.ttf"));
    private static readonly Lazy<BaseFont> ArchivoBoldLazy = new(() => Load("Archivo-Bold.ttf"));
    private static readonly Lazy<BaseFont> ArchivoSemiBoldLazy = new(() => Load("Archivo-SemiBold.ttf"));
    private static readonly Lazy<BaseFont> OutfitBoldLazy = new(() => Load("Outfit-Bold.ttf"));
    private static readonly Lazy<BaseFont> BigShouldersBoldLazy = new(() => Load("BigShoulders-Bold.ttf"));
    private static readonly Lazy<BaseFont> BricolageBoldLazy = new(() => Load("BricolageGrotesque-Bold.ttf"));
    private static readonly Lazy<byte[]> LogoBytesLazy = new(LoadLogo);

    /// <summary>经典风格：横幅大字、价格（Archivo ExtraCondensed Black）。</summary>
    public static BaseFont ArchivoBlackCondensed => ArchivoBlackCondensedLazy.Value;

    /// <summary>经典风格：品名、WAS / SAVE。</summary>
    public static BaseFont ArchivoBold => ArchivoBoldLazy.Value;

    /// <summary>经典风格：页脚小字。</summary>
    public static BaseFont ArchivoSemiBold => ArchivoSemiBoldLazy.Value;

    /// <summary>现代风格：小写标题词、说明文字、页脚。</summary>
    public static BaseFont OutfitBold => OutfitBoldLazy.Value;

    /// <summary>现代风格：价格与贴纸数字。</summary>
    public static BaseFont BigShouldersBold => BigShouldersBoldLazy.Value;

    /// <summary>现代风格：品名。</summary>
    public static BaseFont BricolageBold => BricolageBoldLazy.Value;

    /// <summary>Hot Bargain logo（与发票同一张图）；找不到时返回 null，海报照常生成只是不带 logo。</summary>
    public static byte[]? LogoBytes => LogoBytesLazy.Value.Length > 0 ? LogoBytesLazy.Value : null;

    /// <summary>品名所用字体是否包含该字符（中文等没有字形的字符会被拒绝）。</summary>
    public static bool CanPrintTitleChar(PromoPosterStyle style, char ch)
    {
        if (ch == ' ') return true;
        if (char.IsControl(ch) || char.IsSurrogate(ch)) return false;
        var font = style switch
        {
            PromoPosterStyle.Classic or PromoPosterStyle.LowInk or PromoPosterStyle.Christmas or PromoPosterStyle.Halloween => ArchivoBold,
            _ => BricolageBold,
        };
        return font.CharExists(ch);
    }

    private static BaseFont Load(string fileName)
    {
        var path = ResolveAsset(Path.Combine(FontFolder, fileName))
            ?? throw new FileNotFoundException($"找不到海报字体文件 {fileName}");
        return BaseFont.CreateFont(path, BaseFont.IDENTITY_H, BaseFont.EMBEDDED);
    }

    private static byte[] LoadLogo()
    {
        var path = ResolveAsset("invoice-logo.png") ?? ResolveUpward(Path.Combine("BlazorApp.Shared", "Helper", "logo", "HB_logo(1).png"));
        return path == null ? Array.Empty<byte>() : File.ReadAllBytes(path);
    }

    /// <summary>先找发布目录下的 Assets，找不到（例如测试运行目录）再向上查源码目录。</summary>
    private static string? ResolveAsset(string relative)
    {
        var published = Path.Combine(AppContext.BaseDirectory, "Assets", relative);
        if (File.Exists(published)) return published;
        return ResolveUpward(Path.Combine("BlazorApp.Api", "Assets", relative));
    }

    private static string? ResolveUpward(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
