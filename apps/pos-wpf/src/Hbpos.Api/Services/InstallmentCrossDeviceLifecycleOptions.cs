namespace Hbpos.Api.Services;

public sealed class InstallmentCrossDeviceLifecycleOptions
{
    public bool CancelRefundEnabled { get; set; }

    public bool VoidEnabled { get; set; }

    public bool PickupEnabled { get; set; }
}
