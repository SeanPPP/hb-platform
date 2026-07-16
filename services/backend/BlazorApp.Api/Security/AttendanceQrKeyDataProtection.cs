using Microsoft.AspNetCore.DataProtection;

namespace BlazorApp.Api.Security;

public static class AttendanceQrKeyDataProtection
{
    public static string ApplicationName =>
        BlazorApp.Shared.Security.AttendanceQrKeyDataProtection.ApplicationName;
    public static string Purpose =>
        BlazorApp.Shared.Security.AttendanceQrKeyDataProtection.Purpose;

    public static IDataProtectionProvider CreateProvider(string keysPath)
    {
        if (string.IsNullOrWhiteSpace(keysPath) || !Path.IsPathRooted(keysPath))
        {
            throw new ArgumentException("考勤二维码 Data Protection 路径必须是绝对路径", nameof(keysPath));
        }

        Directory.CreateDirectory(keysPath);
        return DataProtectionProvider.Create(
            new DirectoryInfo(keysPath),
            builder => builder.SetApplicationName(ApplicationName));
    }

    public static AttendanceQrKeyProtector CreateProtector(IDataProtectionProvider provider) =>
        new(provider.CreateProtector(Purpose));
}

public sealed class AttendanceQrKeyProtector(IDataProtector protector)
{
    public string Protect(byte[] key) => Convert.ToBase64String(protector.Protect(key));

    public byte[] Unprotect(string protectedKey) =>
        protector.Unprotect(Convert.FromBase64String(protectedKey));
}
