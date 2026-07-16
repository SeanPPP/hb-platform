namespace Hbpos.Client.Tests;

public sealed class SensitiveBufferClearingContractTests
{
    [Fact]
    public void Attendance_identity_clears_transient_and_discarded_aes_keys()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "apps", "pos-wpf", "src", "Hbpos.Client.Wpf", "ViewModels", "AttendanceQrPanelViewModel.cs"));

        Assert.Contains("SigningIdentity? transientIdentity = null;", source, StringComparison.Ordinal);
        Assert.Contains("ClearIdentityKey(transientIdentity);", source, StringComparison.Ordinal);
        Assert.True(source.Split("ClearIdentityKey(identity);", StringSplitOptions.None).Length - 1 >= 2);
    }

    [Fact]
    public void Windows_dpapi_protector_clears_managed_and_unmanaged_temporary_buffers()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "apps", "pos-wpf", "src", "Hbpos.Client.Wpf", "Services", "WindowsDpapiDeviceAuthorizationProtector.cs"));

        Assert.Contains("CryptographicOperations.ZeroMemory(plaintextBytes);", source, StringComparison.Ordinal);
        Assert.Contains("CryptographicOperations.ZeroMemory(protectedBytes);", source, StringComparison.Ordinal);
        Assert.Contains("ZeroUnmanagedMemory(input.DataPointer, input.DataLength);", source, StringComparison.Ordinal);
        Assert.Contains("ZeroUnmanagedMemory(output.DataPointer, output.DataLength);", source, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var current = new DirectoryInfo(start);
            while (current is not null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "apps", "pos-wpf")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new DirectoryNotFoundException("Unable to find repository root.");
    }
}
