using System.Windows;
using Hbpos.Client.Wpf.ViewModels;

namespace Hbpos.Client.Wpf.Views.Windows;

public partial class AppUpdatePromptWindow : Window
{
    internal AppUpdatePromptWindow(AppUpdatePromptViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void AppUpdatePromptWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 中文注释：沿用原 MessageBox 的安全默认项，回车和 Esc 均不会直接启动安装。
        InstallLaterButton.Focus();
    }

    private void InstallLaterButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void RestartAndInstallButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
