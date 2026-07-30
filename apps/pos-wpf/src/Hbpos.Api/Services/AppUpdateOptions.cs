namespace Hbpos.Api.Services;

public sealed class AppUpdateOptions
{
    public const string CenterApiKeyHeaderName = "X-HBPOS-App-Update-Key";

    public string? CenterBaseUrl { get; set; }

    public string Channel { get; set; } = "production";

    public string? CheckApiKey { get; set; }

    public string? CenterApiKey { get; set; }

    /// <summary>
    /// 中央更新决策接口使用的 hbsvc_ 服务令牌；生产环境应通过 secret/env 注入。
    /// </summary>
    public string? ServiceApiToken { get; set; }

    /// <summary>
    /// iPad 原生更新是否改由中央策略决定。默认关闭，以免首次部署中央尚未播种策略时提前解除旧强制升级。
    /// </summary>
    public bool CentralPolicyEnabled { get; set; }
}
