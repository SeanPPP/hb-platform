namespace Hbpos.Client.UiTests;

[Collection(WpfUiCollection.Name)]
public sealed class StartupSmokeTests
{
    private readonly WpfAppFixture _app;

    public StartupSmokeTests(WpfAppFixture app) => _app = app;

    [Theory]
    [InlineData("--preview")]
    [InlineData("--screen=pos")]
    [InlineData("\"--preview\"")]
    [InlineData("--culture=en-AU\t--screen=pos")]
    public void Preview_arguments_force_safe_environment_after_parent_and_caller_overrides(string arguments)
    {
        var keys = new[]
        {
            "HBPOS_API_BASE_URL",
            "HBPOS_LOG_CENTER_ENABLED",
            "HBPOS_OPERATION_AUDIT_UPLOAD_ENABLED",
        };
        var originalValues = keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);

        try
        {
            foreach (var key in keys) Environment.SetEnvironmentVariable(key, "parent-override");
            var callerEnvironment = keys.ToDictionary(key => key, _ => (string?)"caller-override");

            var startInfo = _app.CreateStartInfo("Hbpos.Client.Wpf.exe", arguments, callerEnvironment);

            Assert.Equal("http://127.0.0.1:0/", startInfo.Environment["HBPOS_API_BASE_URL"]);
            Assert.Equal("false", startInfo.Environment["HBPOS_LOG_CENTER_ENABLED"]);
            Assert.Equal("false", startInfo.Environment["HBPOS_OPERATION_AUDIT_UPLOAD_ENABLED"]);
        }
        finally
        {
            foreach (var pair in originalValues) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        }
    }

    [Fact]
    public void Preview_mode_shows_pos_screen_and_exits_cleanly()
    {
        try
        {
            var window = _app.Launch("--preview --screen=pos --culture=en-AU");
            Assert.Equal("PosMainWindow", window.AutomationId);
            var pos = _app.WaitForAutomationId("PosTerminalScreen", TimeSpan.FromSeconds(30));
            Assert.False(pos.IsOffscreen);
            Assert.True(_app.CloseOwnedProcess());
        }
        catch
        {
            _app.CaptureFailure(nameof(Preview_mode_shows_pos_screen_and_exits_cleanly));
            throw;
        }
    }
}
