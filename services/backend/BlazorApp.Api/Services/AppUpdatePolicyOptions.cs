namespace BlazorApp.Api.Services;

public sealed class AppUpdatePolicyOptions
{
    public string MobileIosBundleIdentifier { get; set; } = "com.hbweb.expo";

    public string PosIpadBundleIdentifier { get; set; } = "com.hbweb.posipad";

    public string PosHandheldBundleIdentifier { get; set; } = "com.hbweb.poshandheld";

    public bool AllowLegacyManageTokenForAppUpdateDecisions { get; set; }
}
