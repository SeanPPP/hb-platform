namespace BlazorApp.Api.Services;

public sealed class AppUpdatePolicyOptions
{
    public string MobileIosBundleIdentifier { get; set; } = "com.hbweb.expo";

    public string PosIpadBundleIdentifier { get; set; } = "com.hbweb.posipad";
}
