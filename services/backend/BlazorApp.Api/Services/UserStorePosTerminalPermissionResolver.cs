namespace BlazorApp.Api.Services;

public static class UserStorePosTerminalPermissionResolver
{
    public static IReadOnlyCollection<string> ResolveEffectivePermissionCodes(
        IEnumerable<string>? inheritedPermissionCodes,
        IReadOnlyDictionary<string, bool>? overrides,
        bool isAdministrator
    )
    {
        var effective = new HashSet<string>(
            inheritedPermissionCodes ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase
        );

        if (isAdministrator)
        {
            return effective;
        }

        // 门店级显式 allow/deny 最后应用，因此优先级高于角色和用户直接权限。
        foreach (var entry in overrides ?? new Dictionary<string, bool>())
        {
            if (entry.Value)
            {
                effective.Add(entry.Key);
            }
            else
            {
                effective.Remove(entry.Key);
            }
        }

        return effective;
    }
}
