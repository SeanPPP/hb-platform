using System.Windows;

namespace Hbpos.Client.Wpf.Services;

public interface IWindowOwnerProvider
{
    Window? CurrentOwner { get; }
}

public sealed class WpfWindowOwnerProvider : IWindowOwnerProvider
{
    public Window? CurrentOwner => Application.Current?.MainWindow;
}
