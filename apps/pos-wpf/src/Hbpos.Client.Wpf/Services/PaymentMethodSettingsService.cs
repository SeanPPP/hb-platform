using System.Text.Json;

namespace Hbpos.Client.Wpf.Services;

public sealed record PaymentMethodSettings(bool UseManualCard = false, bool VoucherEnabled = false);

public interface IPaymentMethodSettingsService
{
    PaymentMethodSettings Current { get; }
    event EventHandler? Changed;
    Task<PaymentMethodSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(PaymentMethodSettings settings, CancellationToken cancellationToken = default);
}

public sealed class PaymentMethodSettingsService(ILocalAppSettingsRepository settingsRepository)
    : IPaymentMethodSettingsService
{
    internal const string SettingsKey = "PaymentMethods:Configuration";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PaymentMethodSettings Current { get; private set; } = new();
    public event EventHandler? Changed;

    public async Task<PaymentMethodSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        bool changed;
        PaymentMethodSettings loaded;
        try
        {
            var value = await settingsRepository.GetValueAsync(SettingsKey, cancellationToken);
            try
            {
                loaded = string.IsNullOrWhiteSpace(value)
                    ? new()
                    : JsonSerializer.Deserialize<PaymentMethodSettings>(value) ?? new();
            }
            catch (JsonException)
            {
                // 缺失或损坏的开关不自动启用新的收款方式。
                loaded = new();
            }
            changed = Current != loaded;
            Current = loaded;
        }
        finally
        {
            _gate.Release();
        }

        if (changed) Changed?.Invoke(this, EventArgs.Empty);
        return loaded;
    }

    public async Task SaveAsync(PaymentMethodSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken);
        bool changed;
        try
        {
            // 两个开关作为同一配置原子保存，成功后才改变当前支付入口。
            await settingsRepository.SetValueAsync(SettingsKey, JsonSerializer.Serialize(settings), cancellationToken);
            changed = Current != settings;
            Current = settings;
        }
        finally
        {
            _gate.Release();
        }

        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }
}
