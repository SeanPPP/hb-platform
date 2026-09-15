using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

internal sealed class MutablePaymentMethodSettingsService(PaymentMethodSettings? initial = null) : IPaymentMethodSettingsService
{
    public PaymentMethodSettings Current { get; private set; } = initial ?? new();
    public event EventHandler? Changed;
    public Func<CancellationToken, Task<PaymentMethodSettings>>? LoadHandler { get; set; }

    public async Task<PaymentMethodSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (LoadHandler is not null) Current = await LoadHandler(cancellationToken);
        return Current;
    }

    public Task SaveAsync(PaymentMethodSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Current = settings;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}
