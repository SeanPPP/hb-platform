namespace Hbpos.Client.UiTests;

[Collection(WpfUiCollection.Name)]
public sealed class StartupSmokeTests
{
    private readonly WpfAppFixture _app;

    public StartupSmokeTests(WpfAppFixture app) => _app = app;

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
