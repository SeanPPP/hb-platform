using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BlazorApp.Api.Services.Performance;

public sealed record SqlPerformanceFingerprint(string Hash, string Template)
{
    private static readonly Regex BlockComment = new(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex LineComment = new(@"--[^\r\n]*", RegexOptions.Compiled);
    private static readonly Regex TaggedDollarQuotedLiteral = new(
        @"\$([A-Za-z_][A-Za-z0-9_]*)\$.*?\$\1\$",
        RegexOptions.Singleline | RegexOptions.Compiled
    );
    private static readonly Regex DollarQuotedLiteral = new(
        @"\$\$.*?\$\$",
        RegexOptions.Singleline | RegexOptions.Compiled
    );
    private static readonly Regex StringLiteral = new(@"N?'(?:''|[^'])*'", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HexLiteral = new(@"\b0x[0-9a-f]+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NumberLiteral = new(@"(?<![\w@])[-+]?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?", RegexOptions.Compiled);
    private static readonly Regex Parameter = new(@"@[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    public static SqlPerformanceFingerprint Create(string? sql)
    {
        var normalized = NormalizeFull(sql);
        var template = normalized.Length <= 500 ? normalized : normalized[..500];
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return new SqlPerformanceFingerprint(hash, template);
    }

    internal static string Normalize(string? sql)
    {
        var normalized = NormalizeFull(sql);
        return normalized.Length <= 500 ? normalized : normalized[..500];
    }

    private static string NormalizeFull(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return "<empty>";
        }

        // 先替换字符串，避免值内的 -- 或 /* */ 被误判为注释并留下未闭合的敏感字面量。
        var normalized = TaggedDollarQuotedLiteral.Replace(sql, "?");
        normalized = DollarQuotedLiteral.Replace(normalized, "?");
        normalized = StringLiteral.Replace(normalized, "?");
        normalized = BlockComment.Replace(normalized, " ");
        normalized = LineComment.Replace(normalized, " ");
        normalized = HexLiteral.Replace(normalized, "?");
        normalized = NumberLiteral.Replace(normalized, "?");
        normalized = Parameter.Replace(normalized, "?");
        return Whitespace.Replace(normalized, " ").Trim().ToUpperInvariant();
    }
}
