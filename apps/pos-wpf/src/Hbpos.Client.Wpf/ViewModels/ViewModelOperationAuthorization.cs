using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Wpf.ViewModels;

internal static class ViewModelOperationAuthorization
{
    public static async Task<ViewModelAuthorizationGrant?> AuthorizeAsync(
        IOperationAuthorizationService? service,
        Func<string, bool> fallbackPermissionCheck,
        string permissionCode,
        string screen,
        string action,
        PosSessionState session,
        CancellationToken cancellationToken = default)
    {
        if (service is null)
        {
            return fallbackPermissionCheck(permissionCode) ? new ViewModelAuthorizationGrant(null) : null;
        }

        var scope = await service.AuthorizeAsync(permissionCode, screen, action, session, cancellationToken);
        if (scope is null)
        {
            return null;
        }

        return new ViewModelAuthorizationGrant(scope);
    }
}

internal sealed class ViewModelAuthorizationGrant(OperationAuthorizationScope? scope) : IDisposable
{
    // 中文注释：必须由业务方法在 await 授权返回后激活，AsyncLocal 才会进入实际 HTTP 调用链。
    public IDisposable Activate() => scope?.Activate() ?? NoopDisposable.Instance;

    public void Dispose() => scope?.Dispose();

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
