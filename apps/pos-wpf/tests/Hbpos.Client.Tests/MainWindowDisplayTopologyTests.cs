using Hbpos.Client.Wpf;

namespace Hbpos.Client.Tests;

public sealed class MainWindowDisplayTopologyTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void Open_customer_display_closes_only_when_fewer_than_two_displays_remain(
        int displayCount,
        bool expected)
    {
        Assert.Equal(
            expected,
            MainWindow.ShouldCloseCustomerDisplayAfterTopologyChange(
                isCustomerDisplayOpen: true,
                displayCount));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Closed_customer_display_stays_closed_when_topology_changes(int displayCount)
    {
        Assert.False(MainWindow.ShouldCloseCustomerDisplayAfterTopologyChange(
            isCustomerDisplayOpen: false,
            displayCount));
    }

    [Theory]
    [InlineData(0x007E, false, true)]
    [InlineData(0x007E, true, false)]
    [InlineData(0x00FF, false, false)]
    public void Display_change_check_is_queued_once(
        int messageId,
        bool isCheckPending,
        bool expected)
    {
        Assert.Equal(
            expected,
            MainWindow.ShouldQueueDisplayTopologyCheck(messageId, isCheckPending));
    }
}
