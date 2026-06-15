using System.IO;
using System.Security.Cryptography;
using System.Security;
using System.Text;
using Microsoft.Win32;

namespace Hbpos.Client.Wpf.Services;

public interface IDeviceFingerprintService
{
    string GetHardwareId();
}

public sealed class DeviceFingerprintService : IDeviceFingerprintService
{
    public string GetHardwareId()
    {
        var machineGuid = ReadMachineGuid();
        var material = string.Join(
            "|",
            Environment.MachineName,
            Environment.UserDomainName,
            Environment.OSVersion.Platform,
            Environment.Is64BitOperatingSystem,
            machineGuid);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash);
    }

    private static string ReadMachineGuid()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid")?.ToString() ?? string.Empty;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            ConsoleLog.WriteError(
                "DeviceFingerprint",
                $"read machine guid failed error={ex.GetType().Name} message={ex.Message}",
                exception: ex);
            return string.Empty;
        }
    }
}
