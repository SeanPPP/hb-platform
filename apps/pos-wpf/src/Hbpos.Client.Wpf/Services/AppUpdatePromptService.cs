using System.Windows;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Contracts.AppUpdates;

namespace Hbpos.Client.Wpf.Services;

public interface IAppUpdatePromptService
{
    Task<bool> ConfirmOptionalDownloadAndInstallAsync(
        AppUpdateCheckResponse update,
        CancellationToken cancellationToken = default);
}

public sealed class WpfAppUpdatePromptService(ILocalizationService? localization = null) : IAppUpdatePromptService
{
    public Task<bool> ConfirmOptionalDownloadAndInstallAsync(
        AppUpdateCheckResponse update,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = Application.Current?.MainWindow;
        var result = MessageBox.Show(
            owner,
            Format("appUpdate.optional.prompt.message", update.TargetVersion),
            T("appUpdate.prompt.title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.No);

        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    private string T(string key)
    {
        return localization?.T(key) ?? LocalizationResourceProvider.Instance[key];
    }

    private string Format(string key, params object[] args)
    {
        var template = T(key);
        return string.Format(localization?.CurrentCulture ?? LocalizationResourceProvider.Instance.CurrentCulture, template, args);
    }
}
