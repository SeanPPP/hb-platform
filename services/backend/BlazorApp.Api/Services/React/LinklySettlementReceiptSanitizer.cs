using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlazorApp.Api.Services.React;

internal static partial class LinklySettlementReceiptSanitizer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly Regex LabelledTrackPattern = new(
        @"(?im)(\btrack\s*(?:1|2|data)?\b\s*[:=]\s*)[^\r\n]+",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex RawTrackOnePattern = new(
        @"%B\d{12,19}\^[^\r\n?]*\?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex RawTrackTwoPattern = new(
        @";\d{12,19}=[^\r\n?]*\?",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex SecurityCodePattern = new(
        @"(?i)(\b(?:cvv2?|cvc2?|cvn2?|security\s*code)\b\s*[:=]?\s*)\S+",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex AuthorizationPattern = new(
        @"(?i)(\bauthorization\b[\s\u00A0]*[:=]?[\s\u00A0]*)(?:bearer[\s\u00A0]+)?\S+",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex CredentialPattern = new(
        @"(?i)(\b(?:access[\s_\-]*token|refresh[\s_\-]*token|bearer[\s_\-]*token|payment[\s_\-]*token|card[\s_\-]*token|token|cryptogram)\b[\s\u00A0]*[:=]?[\s\u00A0]*)\S+",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex BareBearerPattern = new(
        @"(?i)(\bbearer[\s\u00A0]+)\S+",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex PanPattern = new(
        @"(?<!\d)(?:\d[ \t\u00A0.\-]*){11,18}\d(?!\d)",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    public static string[] ParseAndSanitize(string? receiptTextsJson)
    {
        var receipts = Parse(receiptTextsJson);
        if (receipts.Length == 0)
            return [];

        var result = new string[receipts.Length];
        for (var index = 0; index < receipts.Length; index++)
            result[index] = Sanitize(receipts[index]);
        return result;
    }

    public static int Count(string? receiptTextsJson) => Parse(receiptTextsJson).Length;

    private static string[] Parse(string? receiptTextsJson)
    {
        if (string.IsNullOrWhiteSpace(receiptTextsJson))
            return [];

        try
        {
            using var document = JsonDocument.Parse(receiptTextsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var values = new List<string>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                    return [];
                values.Add(element.GetString() ?? string.Empty);
            }

            return values.ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Sanitize(string receipt)
    {
        try
        {
            var sanitized = LabelledTrackPattern.Replace(receipt, "$1[REDACTED]");
            sanitized = RawTrackOnePattern.Replace(sanitized, "[REDACTED TRACK]");
            sanitized = RawTrackTwoPattern.Replace(sanitized, "[REDACTED TRACK]");
            sanitized = SecurityCodePattern.Replace(sanitized, "$1[REDACTED]");
            sanitized = AuthorizationPattern.Replace(sanitized, "$1[REDACTED]");
            sanitized = CredentialPattern.Replace(sanitized, "$1[REDACTED]");
            sanitized = BareBearerPattern.Replace(sanitized, "$1[REDACTED]");
            return PanPattern.Replace(sanitized, match =>
            {
                var digitCount = match.Value.Count(char.IsDigit);
                return digitCount is >= 12 and <= 19 ? "[REDACTED PAN]" : match.Value;
            });
        }
        catch (RegexMatchTimeoutException)
        {
            // 极端输入宁可整张小票隐藏，也不能因脱敏超时回传原文。
            return "[REDACTED RECEIPT]";
        }
    }
}
