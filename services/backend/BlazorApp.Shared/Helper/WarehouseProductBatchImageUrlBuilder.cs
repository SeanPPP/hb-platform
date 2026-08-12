namespace BlazorApp.Shared.Helper;

/// <summary>
/// 仓库批量图片地址的后端权威构造规则。
/// </summary>
public static class WarehouseProductBatchImageUrlBuilder
{
    public const int MaximumImageUrlLength = 200;

    public static bool TryNormalizeBaseUrl(
        string? imageBaseUrl,
        out string normalizedBaseUrl,
        out string errorMessage
    )
    {
        normalizedBaseUrl = string.Empty;
        errorMessage = string.Empty;
        var candidate = imageBaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            errorMessage = "图片基础地址不能为空";
            return false;
        }

        if (
            !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        )
        {
            errorMessage = "图片基础地址必须是有效的 HTTP(S) 绝对地址";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            errorMessage = "图片基础地址不能包含查询参数或片段";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            errorMessage = "图片基础地址不能包含登录信息";
            return false;
        }

        normalizedBaseUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/";
        return true;
    }

    public static bool TryBuild(
        string normalizedBaseUrl,
        string? itemNumber,
        out string imageUrl,
        out string errorMessage
    )
    {
        imageUrl = string.Empty;
        errorMessage = string.Empty;
        var normalizedItemNumber = itemNumber?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedItemNumber))
        {
            errorMessage = "商品货号为空，无法生成图片地址";
            return false;
        }

        imageUrl = normalizedBaseUrl + Uri.EscapeDataString(normalizedItemNumber) + ".jpg";
        if (imageUrl.Length > MaximumImageUrlLength)
        {
            errorMessage = $"生成后的图片地址超过 {MaximumImageUrlLength} 个字符";
            imageUrl = string.Empty;
            return false;
        }

        return true;
    }
}
