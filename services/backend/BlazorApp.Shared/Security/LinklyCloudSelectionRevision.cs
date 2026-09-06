namespace BlazorApp.Shared.Security;

public static class LinklyCloudSelectionRevision
{
    // Revision 会通过 Web 的 JavaScript number 传输，因此限制在 52 bit 以内，保留精确整数表示。
    // 排除历史固定初值 1；清除后重建必须取得新值，避免旧付款或清除请求在 ABA 情况下重新生效。
    public const long MaxJsSafeRevision = (1L << 52) - 1;

    public static long CreateInitial()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        while (true)
        {
            System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
            var revision = (long)(BitConverter.ToUInt64(bytes) & (ulong)MaxJsSafeRevision);
            if (revision > 1)
            {
                return revision;
            }
        }
    }
}
