using System.Windows;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class CustomerDisplayWindowServiceTests
{
    [Fact]
    public void Fullscreen_layout_plan_matches_maximize_then_hide_titlebar_flow()
    {
        var plan = CustomerDisplayWindowService.GetLayoutPlan(CustomerDisplayWindowMode.Fullscreen);

        Assert.True(plan.TitleBarVisibleDuringPlacement);
        Assert.False(plan.CenterAfterPlacement);
        Assert.True(plan.UseFullDisplayBoundsForPlacement);
        Assert.Equal(WindowState.Maximized, plan.FinalWindowState);
        Assert.False(plan.TitleBarVisibleAfterStateChange);
    }

    [Fact]
    public void Normal_layout_plan_keeps_titlebar_visible_and_centered()
    {
        var plan = CustomerDisplayWindowService.GetLayoutPlan(CustomerDisplayWindowMode.Normal);

        Assert.True(plan.TitleBarVisibleDuringPlacement);
        Assert.True(plan.CenterAfterPlacement);
        Assert.False(plan.UseFullDisplayBoundsForPlacement);
        Assert.Equal(WindowState.Normal, plan.FinalWindowState);
        Assert.True(plan.TitleBarVisibleAfterStateChange);
    }
}
