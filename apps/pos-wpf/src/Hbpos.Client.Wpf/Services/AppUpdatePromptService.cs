using System.Windows;
using System.Windows.Markup;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Client.Wpf.Views.Windows;
using Hbpos.Contracts.AppUpdates;

namespace Hbpos.Client.Wpf.Services;

public interface IAppUpdatePromptService
{
    Task<bool> ConfirmOptionalDownloadAndInstallAsync(
        AppUpdateCheckResponse update,
        CancellationToken cancellationToken = default);
}

public sealed class WpfAppUpdatePromptService : IAppUpdatePromptService
{
    private readonly ILocalizationService? _localization;
    private readonly IAppUpdatePromptDialogPresenter _dialogPresenter;

    public WpfAppUpdatePromptService(ILocalizationService? localization = null)
        : this(localization, new WpfAppUpdatePromptDialogPresenter())
    {
    }

    internal WpfAppUpdatePromptService(
        ILocalizationService? localization,
        IAppUpdatePromptDialogPresenter dialogPresenter)
    {
        _localization = localization;
        _dialogPresenter = dialogPresenter;
    }

    public Task<bool> ConfirmOptionalDownloadAndInstallAsync(
        AppUpdateCheckResponse update,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var owner = Application.Current?.MainWindow;
        var culture = _localization?.CurrentCulture ?? LocalizationResourceProvider.Instance.CurrentCulture;
        var language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
        var viewModel = new AppUpdatePromptViewModel(update);
        var result = _dialogPresenter.Show(viewModel, owner, language);

        return Task.FromResult(result == true);
    }
}

internal interface IAppUpdatePromptDialogPresenter
{
    bool? Show(AppUpdatePromptViewModel viewModel, Window? owner, XmlLanguage language);
}

internal sealed class WpfAppUpdatePromptDialogPresenter : IAppUpdatePromptDialogPresenter
{
    private const double FallbackWidth = 1200;
    private const double FallbackHeight = 760;

    public bool? Show(AppUpdatePromptViewModel viewModel, Window? owner, XmlLanguage language)
    {
        var dialog = new AppUpdatePromptWindow(viewModel)
        {
            Language = language,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner
        };

        if (owner is not null)
        {
            dialog.Owner = owner;
            // 中文注释：弹窗窗口与主窗口同尺寸，确保遮罩覆盖完整收银界面。
            dialog.Width = ResolveOverlayLength(owner.ActualWidth, FallbackWidth);
            dialog.Height = ResolveOverlayLength(owner.ActualHeight, FallbackHeight);
        }

        return dialog.ShowDialog();
    }

    private static double ResolveOverlayLength(double actualLength, double fallbackLength)
    {
        return double.IsFinite(actualLength) && actualLength > 0
            ? actualLength
            : fallbackLength;
    }
}
