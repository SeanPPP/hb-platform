using Hbpos.Client.Wpf.Localization;

namespace Hbpos.Client.Wpf.Services;

public interface IShellCultureService
{
    Task<string> RestoreAsync(
        AppStartupOptions startupOptions,
        bool schemaReady,
        CancellationToken cancellationToken = default);

    Task<string> ApplyAsync(
        string cultureName,
        bool persist,
        bool schemaReady,
        CancellationToken cancellationToken = default);

    Task<string> ToggleAsync(bool schemaReady, CancellationToken cancellationToken = default);
}

public sealed class ShellCultureService(
    ILocalizationService localization,
    ILocalAppSettingsRepository settingsRepository) : IShellCultureService
{
    private const string LanguageSettingKey = "Language";
    private readonly object _persistenceSync = new();
    private Task _persistenceTail = Task.CompletedTask;

    public async Task<string> RestoreAsync(
        AppStartupOptions startupOptions,
        bool schemaReady,
        CancellationToken cancellationToken = default)
    {
        if (startupOptions.PreviewMode)
        {
            return await ApplyAsync(
                startupOptions.InitialCulture ?? LocalizationService.DefaultCultureName,
                persist: false,
                schemaReady,
                cancellationToken);
        }

        var cultureName = startupOptions.InitialCulture;
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            cultureName = await settingsRepository.GetValueAsync(LanguageSettingKey, cancellationToken)
                ?? LocalizationService.DefaultCultureName;
        }

        return await ApplyAsync(
            cultureName,
            persist: startupOptions.InitialCulture is not null,
            schemaReady,
            cancellationToken);
    }

    public async Task<string> ApplyAsync(
        string cultureName,
        bool persist,
        bool schemaReady,
        CancellationToken cancellationToken = default)
    {
        try
        {
            localization.SetCulture(cultureName);
        }
        catch (ArgumentException)
        {
            localization.SetCulture(LocalizationService.DefaultCultureName);
        }

        var appliedCulture = localization.CurrentCulture.Name;
        if (persist && schemaReady)
        {
            await QueuePersistenceAsync(appliedCulture, cancellationToken);
        }

        return appliedCulture;
    }

    public Task<string> ToggleAsync(bool schemaReady, CancellationToken cancellationToken = default)
    {
        var nextCultureName = string.Equals(
            localization.CurrentCulture.Name,
            LocalizationService.ChineseCultureName,
            StringComparison.OrdinalIgnoreCase)
            ? LocalizationService.DefaultCultureName
            : LocalizationService.ChineseCultureName;

        return ApplyAsync(nextCultureName, persist: true, schemaReady, cancellationToken);
    }

    private Task QueuePersistenceAsync(string cultureName, CancellationToken cancellationToken)
    {
        lock (_persistenceSync)
        {
            var writeTask = PersistAfterAsync(_persistenceTail, cultureName, cancellationToken);
            _persistenceTail = IgnorePersistenceFailureAsync(writeTask);
            return writeTask;
        }
    }

    private async Task PersistAfterAsync(
        Task previousWrite,
        string cultureName,
        CancellationToken cancellationToken)
    {
        await previousWrite.ConfigureAwait(false);
        await settingsRepository.SetValueAsync(LanguageSettingKey, cultureName, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task IgnorePersistenceFailureAsync(Task writeTask)
    {
        try
        {
            await writeTask.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 后续请求仍必须写入最新语言，当前请求的调用方会单独收到失败。
        }
    }
}
