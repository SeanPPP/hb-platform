namespace Hbpos.Contracts.Devices;

public static class DeviceAuthConstants
{
    public const string Scheme = "HbposDevice";
    public const string AuthorizationHeader = "Authorization";
    public const string BearerPrefix = "Bearer ";
    public const string DeviceCodeHeader = "X-HBPOS-Device-Code";
    public const string StoreCodeHeader = "X-HBPOS-Store-Code";
    public const string HardwareIdHeader = "X-HBPOS-Hardware-Id";

    public const string DeviceCodeClaim = "hbpos_device_code";
    public const string StoreCodeClaim = "hbpos_store_code";
    public const string HardwareIdClaim = "hbpos_hardware_id";
}
