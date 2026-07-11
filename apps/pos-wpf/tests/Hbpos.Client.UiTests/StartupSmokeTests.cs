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
    public void Live_child_removes_all_test_only_environment_from_parent_and_caller()
    {
        var inheritedKeys = new[]
        {
            "HBPOS_E2E_ENABLED",
            "HBPOS_E2E_BACKEND_BEARER_TOKEN",
            "HBPOS_E2E_PARENT_ONLY",
        };
        var originalValues = inheritedKeys.ToDictionary(key => key, Environment.GetEnvironmentVariable);

        try
        {
            foreach (var key in inheritedKeys) Environment.SetEnvironmentVariable(key, "parent-test-secret");
            var callerEnvironment = new Dictionary<string, string?>
            {
                ["HBPOS_E2E_CASHIER_BARCODE"] = "caller-cashier-secret",
                ["HBPOS_E2E_PRODUCT_BARCODE"] = "caller-product-secret",
                ["HBPOS_E2E_BACKEND_BEARER_TOKEN"] = "caller-token-secret",
                ["hbpos_e2e_caller_only"] = "caller-only-secret",
                ["HBPOS_API_BASE_URL"] = "http://localhost:5159/",
                ["HBPOS_OPERATION_AUDIT_UPLOAD_ENABLED"] = "true",
            };

            var startInfo = _app.CreateStartInfo(
                "Hbpos.Client.Wpf.exe",
                "--culture=en-AU",
                callerEnvironment);

            Assert.DoesNotContain(
                startInfo.Environment.Keys,
                key => key.StartsWith("HBPOS_E2E_", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("http://localhost:5159/", startInfo.Environment["HBPOS_API_BASE_URL"]);
            Assert.Equal("true", startInfo.Environment["HBPOS_OPERATION_AUDIT_UPLOAD_ENABLED"]);
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
