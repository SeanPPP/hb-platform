using Hbpos.RemoteMaintenance.Setup;

namespace Hbpos.RemoteStatus.Tests;

public sealed class RemoteMaintenancePreflightTests
{
    [Theory]
    [InlineData("configure")]
    [InlineData("fail-closed")]
    public async Task 恢复和清理也不能从开发目录提权启动助手(string stage)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hbpos-preflight-" + Guid.NewGuid().ToString("N"));
        var helper = Path.Combine(directory, "Hbpos.RemoteMaintenance.Setup.exe");
        var journal = Path.Combine(directory, "remote-maintenance.journal");

        var error = await Assert.ThrowsAsync<RemoteMaintenanceSetupException>(() =>
            new WindowsRemoteMaintenanceUacHelperLauncher().RunAsync(helper, journal, Guid.NewGuid(), stage));

        Assert.Equal(RemoteMaintenanceSetupError.InstallationLocationInvalid, error.Error);
    }

    [Fact]
    public void 开发目录在下载和提权前给出安装目录错误()
    {
        var path = Path.Combine(Path.GetTempPath(), "hbpos-preflight", "Hbpos.RemoteMaintenance.Setup.exe");

        var error = Assert.Throws<RemoteMaintenanceSetupException>(() =>
            new WindowsRemoteMaintenanceUacHelperLauncher().ValidateInstallation(path));

        Assert.Equal(RemoteMaintenanceSetupError.InstallationLocationInvalid, error.Error);
    }

    [Fact]
    public void 相对路径不能作为安装助手来源()
    {
        var error = Assert.Throws<RemoteMaintenanceSetupException>(() =>
            new WindowsRemoteMaintenanceUacHelperLauncher().ValidateInstallation("Hbpos.RemoteMaintenance.Setup.exe"));

        Assert.Equal(RemoteMaintenanceSetupError.InstallationLocationInvalid, error.Error);
    }

    [Fact]
    public void 相似目录前缀不能冒充ProgramFiles()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "-other",
            "Hbpos.RemoteMaintenance.Setup.exe");

        var error = Assert.Throws<RemoteMaintenanceSetupException>(() =>
            new WindowsRemoteMaintenanceUacHelperLauncher().ValidateInstallation(path));

        Assert.Equal(RemoteMaintenanceSetupError.InstallationLocationInvalid, error.Error);
    }

    [Fact]
    public void 受保护目录缺件给出完整安装包指引错误码()
    {
        // 只读检查一个不存在的精确路径，不在 Program Files 写文件或触发 UAC。
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "HBPOS-preflight-" + Guid.NewGuid().ToString("N"), "Hbpos.RemoteMaintenance.Setup.exe");

        var error = Assert.Throws<RemoteMaintenanceSetupException>(() =>
            new WindowsRemoteMaintenanceUacHelperLauncher().ValidateInstallation(path));

        Assert.Equal(RemoteMaintenanceSetupError.ComponentsMissing, error.Error);
    }
}
