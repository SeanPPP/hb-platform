namespace BlazorApp.Shared.Options;

public sealed class MobileDeviceActivationOptions
{
    public const string SectionName = "MobileDeviceActivation";

    /// <summary>
    /// 启用后，Mobile 旧注册入口不再签发新设备凭据，必须改走开通码流程。
    /// 默认关闭，便于先发布兼容版本再分阶段启用服务端闸门。
    /// </summary>
    public bool EnforceForNewRegistrations { get; set; }
}
