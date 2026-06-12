namespace Hbpos.Client.Wpf.Services;

public sealed class DeviceAuthorizationState
{
    private readonly object _gate = new();
    private DeviceAuthorizationContext? _current;

    public DeviceAuthorizationContext? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Set(DeviceAuthorizationContext context)
    {
        lock (_gate)
        {
            _current = context;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _current = null;
        }
    }
}

public sealed record DeviceAuthorizationContext(
    string DeviceCode,
    string StoreCode,
    string HardwareId,
    string AuthorizationCode);
