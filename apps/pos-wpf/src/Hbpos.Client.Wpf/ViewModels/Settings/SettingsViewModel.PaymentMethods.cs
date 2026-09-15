using BlazorApp.Shared.Constants;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Wpf.ViewModels;

public sealed partial class SettingsViewModel
{
    private readonly IPaymentMethodSettingsService? _paymentMethodSettingsService;

    [ObservableProperty]
    private bool _useManualCard;

    [ObservableProperty]
    private bool _voucherEnabled;

    public IAsyncRelayCommand SavePaymentMethodsCommand { get; }

    private bool CanSavePaymentMethods() => !IsBusy && _paymentMethodSettingsService is not null;

    private async Task LoadPaymentMethodsAsync()
    {
        if (_paymentMethodSettingsService is null) return;
        var settings = await _paymentMethodSettingsService.LoadAsync();
        UseManualCard = settings.UseManualCard;
        VoucherEnabled = settings.VoucherEnabled;
        RaiseActivePaymentProviderProperties();
    }

    private async Task SavePaymentMethodsAsync()
    {
        if (!CanSavePaymentMethods()) return;
        using var permissionGrant = await AuthorizeAsync(
            Permissions.PosTerminal.Settings.PaymentTerminal, "save-payment-methods");
        if (permissionGrant is null || !CanSavePaymentMethods()) return;
        using var authorizationActivation = permissionGrant.Activate();
        var settings = new PaymentMethodSettings(UseManualCard, VoucherEnabled);
        await RunBusyAsync(async () =>
        {
            await _paymentMethodSettingsService!.SaveAsync(settings);
            RaiseActivePaymentProviderProperties();
            SetStatus("settings.payment.methods.saved");
        }, "save payment methods");
    }
}
