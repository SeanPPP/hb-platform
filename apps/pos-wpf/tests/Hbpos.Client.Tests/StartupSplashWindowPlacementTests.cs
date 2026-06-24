using System.Windows;
using Hbpos.Client.Wpf;

namespace Hbpos.Client.Tests;

public sealed class StartupSplashWindowPlacementTests
{
    [Fact]
    public void CenterInWorkArea_uses_primary_work_area_origin()
    {
        var workArea = new Rect(100, 40, 1920, 1040);

        var position = StartupSplashWindowPlacement.CenterInWorkArea(workArea, 460, 380);

        Assert.Equal(830, position.X);
        Assert.Equal(370, position.Y);
    }

    [Fact]
    public void CenterInWorkArea_does_not_place_outside_when_window_is_larger_than_work_area()
    {
        var workArea = new Rect(100, 40, 300, 200);

        var position = StartupSplashWindowPlacement.CenterInWorkArea(workArea, 460, 380);

        Assert.Equal(100, position.X);
        Assert.Equal(40, position.Y);
    }
}
