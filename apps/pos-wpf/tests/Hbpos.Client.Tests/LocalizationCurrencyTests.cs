using System.Globalization;
using Hbpos.Client.Wpf.Localization;

namespace Hbpos.Client.Tests;

[Collection(CultureSensitiveTestCollection.Name)]
public sealed class LocalizationCurrencyTests
{
    [Fact]
    public void Localization_keeps_dollar_currency_symbol_for_chinese_ui()
    {
        using var scope = new CultureScope();
        var localization = new LocalizationService();

        localization.SetCulture(LocalizationService.ChineseCultureName);

        Assert.Equal("$5.00", string.Format(CultureInfo.CurrentCulture, "{0:C2}", 5m));
        Assert.Equal("$", localization.CurrentCulture.NumberFormat.CurrencySymbol);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;
        private readonly CultureInfo? _originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        private readonly CultureInfo? _originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _originalCulture;
            CultureInfo.CurrentUICulture = _originalUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = _originalDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = _originalDefaultUiCulture;
        }
    }
}
