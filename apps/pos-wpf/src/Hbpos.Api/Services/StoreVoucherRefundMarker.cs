using System.Text;

namespace Hbpos.Api.Services;

internal static class StoreVoucherRefundMarker
{
    internal const string Prefix = "RefundKey[";
    internal const string Separator = " | ";

    internal static string Create(string idempotencyKey)
    {
        var encodedKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(idempotencyKey.Trim()));
        return $"{Prefix}{encodedKey}]";
    }

    internal static bool HasCanonicalPrefix(string? remark, string marker) =>
        !string.IsNullOrWhiteSpace(remark) &&
        remark.StartsWith(marker + Separator, StringComparison.Ordinal);
}
