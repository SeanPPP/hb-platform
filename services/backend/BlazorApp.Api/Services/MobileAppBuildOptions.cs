namespace BlazorApp.Api.Services;

/// <summary>
/// Mobile 原生 APK 的匿名自动更新开关。
/// 人工下载管理端不读取此开关，便于在生产异常时先止损自动安装链路。
/// </summary>
public sealed class MobileAppBuildOptions
{
    public const string SectionName = "MobileAppBuilds";

    public bool PublicAndroidUpdatesEnabled { get; set; } = true;
}
