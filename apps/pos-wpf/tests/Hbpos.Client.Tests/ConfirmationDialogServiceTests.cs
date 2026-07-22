using Hbpos.Client.Wpf.Localization;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class ConfirmationDialogServiceTests
{
    [Theory]
    [InlineData("exit", "Exit", "Are you sure you want to exit the POS application?", "Exit", true)]
    [InlineData("reset", "Reset test sales data", "Delete all test sales data? This action cannot be undone.", "Reset", true)]
    [InlineData("full-first-payment", "Confirm installment order", "The first installment payment covers the full order amount. Continue creating an installment order?", "Confirm", false)]
    [InlineData("pickup", "Confirm pickup", "This installment order is fully paid. Confirm customer pickup now?", "Confirm", false)]
    public async Task Confirmation_scenarios_publish_expected_english_content(
        string scenario,
        string expectedTitle,
        string expectedMessage,
        string expectedConfirmButton,
        bool expectedDestructive)
    {
        var service = new WpfConfirmationDialogService(new LocalizationService());
        var presenter = Assert.IsAssignableFrom<IConfirmationDialogPresenter>(service);

        var resultTask = OpenAsync(service, scenario);

        Assert.True(presenter.IsOpen);
        Assert.Equal(expectedTitle, presenter.TitleText);
        Assert.Equal(expectedMessage, presenter.MessageText);
        Assert.Equal(expectedConfirmButton, presenter.ConfirmButtonText);
        Assert.Equal("Cancel", presenter.CancelButtonText);
        Assert.Equal(expectedDestructive, presenter.IsDestructive);

        presenter.CancelCommand.Execute(null);
        Assert.False(await resultTask);
    }

    [Fact]
    public async Task Confirm_and_cancel_complete_once_with_expected_result()
    {
        var service = new WpfConfirmationDialogService(new LocalizationService());
        var presenter = (IConfirmationDialogPresenter)service;

        var confirmedTask = service.ConfirmExitApplicationAsync();
        presenter.ConfirmCommand.Execute(null);
        presenter.ConfirmCommand.Execute(null);

        Assert.True(await confirmedTask);
        Assert.False(presenter.IsOpen);

        var cancelledTask = service.ConfirmResetTestSalesDataAsync();
        presenter.CancelCommand.Execute(null);
        presenter.CancelCommand.Execute(null);

        Assert.False(await cancelledTask);
        Assert.False(presenter.IsOpen);
    }

    [Fact]
    public async Task Concurrent_confirmation_fails_closed_without_replacing_open_content()
    {
        var service = new WpfConfirmationDialogService(new LocalizationService());
        var presenter = (IConfirmationDialogPresenter)service;
        var firstTask = service.ConfirmExitApplicationAsync();

        var secondResult = await service.ConfirmResetTestSalesDataAsync();

        Assert.False(secondResult);
        Assert.Equal("Exit", presenter.TitleText);
        Assert.Equal("Are you sure you want to exit the POS application?", presenter.MessageText);
        presenter.CancelCommand.Execute(null);
        Assert.False(await firstTask);
    }

    [Fact]
    public async Task Open_confirmation_refreshes_when_culture_changes()
    {
        var localization = new LocalizationService();
        var service = new WpfConfirmationDialogService(localization);
        var presenter = (IConfirmationDialogPresenter)service;
        var resultTask = service.ConfirmExitApplicationAsync();

        localization.SetCulture("zh-CN");

        Assert.Equal("退出软件", presenter.TitleText);
        Assert.Equal("确定要退出收银软件吗？", presenter.MessageText);
        Assert.Equal("退出软件", presenter.ConfirmButtonText);
        Assert.Equal("取消", presenter.CancelButtonText);
        presenter.CancelCommand.Execute(null);
        Assert.False(await resultTask);
    }

    [Fact]
    public async Task Date_range_reupload_confirmation_formats_count_batches_and_localizes_open_dialog()
    {
        var localization = new LocalizationService();
        var service = new WpfConfirmationDialogService(localization);
        var presenter = (IConfirmationDialogPresenter)service;

        var resultTask = service.ConfirmOrderDateRangeReuploadAsync(
            501,
            2,
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 2));

        Assert.Equal("Confirm date-range reupload", presenter.TitleText);
        Assert.Contains("501", presenter.MessageText);
        Assert.Contains("2 batch", presenter.MessageText);

        localization.SetCulture("zh-CN");

        Assert.Equal("确认重传日期范围", presenter.TitleText);
        Assert.Contains("501", presenter.MessageText);
        Assert.Contains("2 批", presenter.MessageText);
        presenter.CancelCommand.Execute(null);
        Assert.False(await resultTask);
    }

    private static Task<bool> OpenAsync(IConfirmationDialogService service, string scenario) =>
        scenario switch
        {
            "exit" => service.ConfirmExitApplicationAsync(),
            "reset" => service.ConfirmResetTestSalesDataAsync(),
            "full-first-payment" => service.ConfirmInstallmentFullFirstPaymentAsync(),
            "pickup" => service.ConfirmInstallmentPickupAfterPaidOffAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };
}
