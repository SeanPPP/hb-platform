using System.Windows;
using System.Windows.Markup;
using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Contracts.AppUpdates;

namespace Hbpos.Client.Tests;

public sealed class AppUpdatePromptServiceTests
{
    [Fact]
    public async Task ConfirmOptionalDownloadAndInstallAsync_passes_localized_update_content_to_custom_dialog()
    {
        var localization = new LocalizationService();
        localization.SetCulture("zh-CN");
        var presenter = new CapturingDialogPresenter(true);
        var service = new WpfAppUpdatePromptService(localization, presenter);
        var update = new AppUpdateCheckResponse
        {
            CurrentVersion = " 1.4.2 ",
            TargetVersion = "1.5.0",
            ReleaseNotes = "- 扫码稳定性改进\r\n* 查询速度优化\n\u2022 安全修复"
        };

        var accepted = await service.ConfirmOptionalDownloadAndInstallAsync(update);

        Assert.True(accepted);
        Assert.Equal(1, presenter.ShowCount);
        Assert.NotNull(presenter.ViewModel);
        Assert.Equal("1.4.2", presenter.ViewModel.CurrentVersion);
        Assert.Equal("1.5.0", presenter.ViewModel.TargetVersion);
        Assert.Equal(["扫码稳定性改进", "查询速度优化", "安全修复"], presenter.ViewModel.ReleaseNotes);
        Assert.True(presenter.ViewModel.HasReleaseNotes);
        Assert.Equal("zh-CN", presenter.Language?.IetfLanguageTag, ignoreCase: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task ConfirmOptionalDownloadAndInstallAsync_treats_decline_or_window_close_as_not_confirmed(bool? dialogResult)
    {
        var presenter = new CapturingDialogPresenter(dialogResult);
        var service = new WpfAppUpdatePromptService(null, presenter);

        var accepted = await service.ConfirmOptionalDownloadAndInstallAsync(new AppUpdateCheckResponse
        {
            CurrentVersion = "1.0.0",
            TargetVersion = "1.1.0"
        });

        Assert.False(accepted);
        Assert.Equal(1, presenter.ShowCount);
    }

    [Fact]
    public async Task ConfirmOptionalDownloadAndInstallAsync_honors_pre_cancelled_token_before_showing_dialog()
    {
        var presenter = new CapturingDialogPresenter(true);
        var service = new WpfAppUpdatePromptService(null, presenter);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ConfirmOptionalDownloadAndInstallAsync(
                new AppUpdateCheckResponse(),
                cancellation.Token));

        Assert.Equal(0, presenter.ShowCount);
    }

    [Fact]
    public void Prompt_view_model_exposes_empty_release_notes_state_and_safe_version_fallbacks()
    {
        var viewModel = new AppUpdatePromptViewModel(new AppUpdateCheckResponse
        {
            CurrentVersion = " ",
            TargetVersion = string.Empty,
            ReleaseNotes = "\r\n  \n"
        });

        Assert.Equal("-", viewModel.CurrentVersion);
        Assert.Equal("-", viewModel.TargetVersion);
        Assert.False(viewModel.HasReleaseNotes);
        Assert.Empty(viewModel.ReleaseNotes);
    }

    private sealed class CapturingDialogPresenter(bool? result) : IAppUpdatePromptDialogPresenter
    {
        public int ShowCount { get; private set; }

        public AppUpdatePromptViewModel? ViewModel { get; private set; }

        public Window? Owner { get; private set; }

        public XmlLanguage? Language { get; private set; }

        public bool? Show(AppUpdatePromptViewModel viewModel, Window? owner, XmlLanguage language)
        {
            ShowCount++;
            ViewModel = viewModel;
            Owner = owner;
            Language = language;
            return result;
        }
    }
}
