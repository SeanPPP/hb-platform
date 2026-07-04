using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Wpf.ViewModels;

internal sealed record PaymentNavigationActions(
    Action? BackToPos,
    Action? ShowInstallmentCenter,
    Func<Task<bool>>? RecoverPreviousCardTransactionAsync,
    Func<InstallmentOrderSummary, Task>? InstallmentOrderCreatedAsync,
    Func<bool>? ConfirmInstallmentFullFirstPayment)
{
    public bool CanRecoverPreviousCardTransaction => RecoverPreviousCardTransactionAsync is not null;

    public static PaymentNavigationActions FromLegacyCallbacks(
        Action? onBackToPos,
        Action? onShowInstallmentCenter,
        Func<Task<bool>>? recoverPreviousCardTransactionAsync,
        Func<InstallmentOrderSummary, Task>? onInstallmentOrderCreatedAsync = null,
        Func<bool>? confirmInstallmentFullFirstPayment = null)
    {
        // 中文注释：先把 Payment 页依赖的外部导航/恢复回调收口，避免 ViewModel 继续直接持有零散委托。
        return new PaymentNavigationActions(
            onBackToPos,
            onShowInstallmentCenter,
            recoverPreviousCardTransactionAsync,
            onInstallmentOrderCreatedAsync,
            confirmInstallmentFullFirstPayment);
    }
}
