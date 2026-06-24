using System.Windows;

namespace Hbpos.Client.Wpf;

public partial class StartupSplashWindow : Window
{
    public StartupSplashWindow(StartupProgressState progressState)
    {
        InitializeComponent();
        DataContext = progressState;
        CenterOnPrimaryScreen();
    }

    private void CenterOnPrimaryScreen()
    {
        // 启动页必须固定在主屏工作区居中，避免 CenterScreen 跟随鼠标跑到副屏。
        var position = StartupSplashWindowPlacement.CenterInWorkArea(SystemParameters.WorkArea, Width, Height);
        Left = position.X;
        Top = position.Y;
    }
}

internal static class StartupSplashWindowPlacement
{
    public static Point CenterInWorkArea(Rect workArea, double windowWidth, double windowHeight)
    {
        var safeWidth = NormalizeLength(windowWidth);
        var safeHeight = NormalizeLength(windowHeight);

        // 当窗口大于主屏工作区时贴住工作区左上角，避免被放到屏幕外。
        var leftOffset = Math.Max(0, (workArea.Width - safeWidth) / 2);
        var topOffset = Math.Max(0, (workArea.Height - safeHeight) / 2);

        return new Point(workArea.Left + leftOffset, workArea.Top + topOffset);
    }

    private static double NormalizeLength(double value)
    {
        return double.IsNaN(value) || double.IsInfinity(value) || value < 0
            ? 0
            : value;
    }
}
