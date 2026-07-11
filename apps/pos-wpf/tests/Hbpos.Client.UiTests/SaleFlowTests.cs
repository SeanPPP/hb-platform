using System.Globalization;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using Microsoft.Data.Sqlite;

namespace Hbpos.Client.UiTests;

public sealed class WpfAppFixtureMessageTests
{
    [Fact]
    public void Missing_control_timeout_reports_step_and_automation_id()
    {
        using var fixture = new WpfAppFixture();

        var error = Assert.ThrowsAny<Exception>(() => fixture.WaitForAutomationId(
            "MissingControl",
            TimeSpan.FromMilliseconds(1),
            step: "收银员登录"));

        Assert.Contains("收银员登录", error.Message);
        Assert.Contains("MissingControl", error.Message);
    }
}

public sealed class SaleFlowWaitTests
{
    [Fact]
    public void Waits_until_delayed_ui_element_is_available()
    {
        var expected = new object();
        var attempts = 0;

        var result = SaleFlowTests.WaitForUiElement(
            () => ++attempts < 3 ? null : expected,
            "等待购物车数量",
            "CartLineQuantity",
            TimeSpan.FromSeconds(1));

        Assert.Same(expected, result);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public void Delayed_ui_element_timeout_reports_step_and_automation_id()
    {
        var error = Assert.ThrowsAny<Exception>(() => SaleFlowTests.WaitForUiElement<object>(
            () => null,
            "等待购物车加号按钮",
            "CartLineIncreaseButton",
            TimeSpan.FromMilliseconds(1)));

        Assert.Contains("等待购物车加号按钮", error.Message);
        Assert.Contains("CartLineIncreaseButton", error.Message);
    }
}

public sealed class LiveE2eConfigurationTests
{
    [Theory]
    [InlineData("1002")]
    [InlineData("10420")]
    [InlineData("")]
    public void Rejects_store_outside_exact_allowlist(string storeCode)
    {
        var values = ValidValues();
        values["HBPOS_E2E_STORE_CODE"] = storeCode;

        var error = Assert.Throws<InvalidOperationException>(() =>
            LiveE2eConfiguration.FromEnvironment(name => values.GetValueOrDefault(name)));

        Assert.Contains("HBPOS_E2E_STORE_CODE", error.Message);
        AssertSecretsRedacted(error, values);
    }

    [Fact]
    public void Accepts_only_complete_1042_configuration()
    {
        var values = ValidValues();

        var result = LiveE2eConfiguration.FromEnvironment(name => values.GetValueOrDefault(name));

        Assert.Equal("1042", result.StoreCode);
        Assert.Equal(new Uri("http://localhost:5159/"), result.PosApiBaseUrl);
    }

    [Fact]
    public void Accepts_complete_1005_configuration()
    {
        var values = ValidValues();
        values["HBPOS_E2E_STORE_CODE"] = "1005";

        var result = LiveE2eConfiguration.FromEnvironment(name => values.GetValueOrDefault(name));

        Assert.Equal("1005", result.StoreCode);
    }

    [Theory]
    [InlineData("HBPOS_E2E_ENABLED", "false")]
    [InlineData("HBPOS_E2E_ENABLED", "")]
    [InlineData("HBPOS_E2E_ADMIN_AUDIT_SCOPE_CONFIRMED", "false")]
    [InlineData("HBPOS_E2E_ADMIN_AUDIT_SCOPE_CONFIRMED", "")]
    public void Rejects_each_confirmation_switch_unless_explicitly_true(string name, string value)
    {
        var values = ValidValues();
        values[name] = value;

        var error = Assert.Throws<InvalidOperationException>(() =>
            LiveE2eConfiguration.FromEnvironment(key => values.GetValueOrDefault(key)));

        Assert.Contains(name, error.Message);
        AssertSecretsRedacted(error, values);
    }

    [Theory]
    [InlineData("HBPOS_E2E_STORE_CODE")]
    [InlineData("HBPOS_E2E_CASHIER_BARCODE")]
    [InlineData("HBPOS_E2E_PRODUCT_BARCODE")]
    [InlineData("HBPOS_API_BASE_URL")]
    [InlineData("HBPOS_E2E_BACKEND_BASE_URL")]
    [InlineData("HBPOS_E2E_BACKEND_BEARER_TOKEN")]
    public void Rejects_each_missing_required_value_without_exposing_secrets(string name)
    {
        var values = ValidValues();
        values.Remove(name);

        var error = Assert.Throws<InvalidOperationException>(() =>
            LiveE2eConfiguration.FromEnvironment(key => values.GetValueOrDefault(key)));

        Assert.Contains(name, error.Message);
        AssertSecretsRedacted(error, values);
    }

    [Theory]
    [InlineData("HBPOS_API_BASE_URL", "not-a-uri")]
    [InlineData("HBPOS_API_BASE_URL", "/relative")]
    [InlineData("HBPOS_API_BASE_URL", "ftp://localhost/")]
    [InlineData("HBPOS_E2E_BACKEND_BASE_URL", "not-a-uri")]
    [InlineData("HBPOS_E2E_BACKEND_BASE_URL", "/relative")]
    [InlineData("HBPOS_E2E_BACKEND_BASE_URL", "ftp://backend.example.test/")]
    public void Rejects_invalid_or_relative_uri_without_exposing_secrets(string name, string value)
    {
        var values = ValidValues();
        values[name] = value;

        var error = Assert.Throws<InvalidOperationException>(() =>
            LiveE2eConfiguration.FromEnvironment(key => values.GetValueOrDefault(key)));

        Assert.Contains(name, error.Message);
        AssertSecretsRedacted(error, values);
    }

    [Fact]
    public void Reads_each_environment_value_once()
    {
        var values = ValidValues();
        var readCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        _ = LiveE2eConfiguration.FromEnvironment(name =>
        {
            readCounts[name] = readCounts.GetValueOrDefault(name) + 1;
            return values.GetValueOrDefault(name);
        });

        foreach (var name in values.Keys)
        {
            Assert.Equal(1, readCounts.GetValueOrDefault(name));
        }
    }

    [Theory]
    [InlineData("Store: Stafford (1042)", "1042")]
    [InlineData("Store 1005", "1005")]
    public void Extracts_only_an_exact_allowed_store_from_ui(string display, string expected)
    {
        Assert.Equal(expected, SaleFlowTests.ExtractAllowedStore(display));
    }

    [Theory]
    [InlineData("Store 1002")]
    [InlineData("Store 10420")]
    [InlineData("1042 / 1005")]
    public void Rejects_ambiguous_or_outside_store_from_ui(string display)
    {
        Assert.Throws<InvalidOperationException>(() => SaleFlowTests.ExtractAllowedStore(display));
    }

    private static void AssertSecretsRedacted(
        Exception error,
        IReadOnlyDictionary<string, string> values)
    {
        foreach (var name in new[]
                 {
                     "HBPOS_E2E_CASHIER_BARCODE",
                     "HBPOS_E2E_PRODUCT_BARCODE",
                     "HBPOS_E2E_BACKEND_BEARER_TOKEN",
                 })
        {
            if (values.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value))
                Assert.DoesNotContain(value, error.Message);
        }
    }

    private static Dictionary<string, string> ValidValues() => new(StringComparer.Ordinal)
    {
        ["HBPOS_E2E_ENABLED"] = "true",
        ["HBPOS_E2E_ADMIN_AUDIT_SCOPE_CONFIRMED"] = "true",
        ["HBPOS_E2E_STORE_CODE"] = "1042",
        ["HBPOS_E2E_CASHIER_BARCODE"] = "test-cashier-secret",
        ["HBPOS_E2E_PRODUCT_BARCODE"] = "test-product-secret",
        ["HBPOS_API_BASE_URL"] = "http://localhost:5159/",
        ["HBPOS_E2E_BACKEND_BASE_URL"] = "https://backend.example.test/",
        ["HBPOS_E2E_BACKEND_BEARER_TOKEN"] = "test-bearer-secret",
        ["HBPOS_OPERATION_AUDIT_UPLOAD_ENABLED"] = "true",
    };
}

public sealed class LiveDeviceBindingTests
{
    [Fact]
    public async Task Reads_latest_binding_and_rejects_other_store_without_exposing_authorization_code()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"hbpos-device-{Guid.NewGuid():N}.db");
        const string authorizationCode = "test-authorization-secret";

        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false,
            };
            await using (var connection = new SqliteConnection(builder.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE DeviceCache (
                        StoreCode TEXT NOT NULL,
                        DeviceCode TEXT NOT NULL,
                        AuthorizationCodeProtected TEXT NULL,
                        UpdatedAt TEXT NOT NULL
                    );
                    INSERT INTO DeviceCache (StoreCode, DeviceCode, AuthorizationCodeProtected, UpdatedAt)
                    VALUES ('1042', 'DEV-1042', $authorizationCode, '2026-07-11T00:00:00Z');
                    """;
                command.Parameters.AddWithValue("$authorizationCode", authorizationCode);
                await command.ExecuteNonQueryAsync();
            }

            var binding = await LiveDeviceBinding.ReadLatestAsync(databasePath);

            Assert.Equal("1042", binding.StoreCode);
            Assert.Equal("DEV-1042", binding.DeviceCode);
            binding.EnsureMatches("1042");
            var error = Assert.Throws<InvalidOperationException>(() => binding.EnsureMatches("1005"));
            Assert.DoesNotContain(authorizationCode, error.Message);
        }
        finally
        {
            if (File.Exists(databasePath)) File.Delete(databasePath);
        }
    }
}

[Collection(WpfUiCollection.Name)]
public sealed class SaleFlowTests
{
    private readonly WpfAppFixture _app;

    public SaleFlowTests(WpfAppFixture app) => _app = app;

    [Fact]
    public async Task Store_cash_sale_reaches_payment_success()
    {
        try
        {
            // 关键门禁必须全部先于 WPF 启动，避免门店不匹配时产生任何业务操作。
            var config = LiveE2eConfiguration.FromEnvironment();
            var device = await LiveDeviceBinding.ReadLatestAsync();
            device.EnsureMatches(config.StoreCode);
            var window = _app.Launch("--culture=en-AU", new Dictionary<string, string?>
            {
                ["HBPOS_API_BASE_URL"] = config.PosApiBaseUrl.ToString(),
                ["HBPOS_OPERATION_AUDIT_UPLOAD_ENABLED"] = "true",
            });
            Assert.Equal("PosMainWindow", window.AutomationId);
            Assert.Equal(config.StoreCode, ExtractAllowedStore(_app.WaitForAutomationId(
                "CurrentStoreInfo",
                step: "校验当前门店").Name));

            var login = _app.WaitForAutomationId("CashierLoginInput", step: "等待收银员登录输入框");
            login.Focus();
            Keyboard.Type(config.CashierBarcode);
            Keyboard.Type(VirtualKeyShort.RETURN);
            WaitUntilHidden("CashierLoginOverlay");

            var scan = _app.WaitForAutomationId("ProductBarcodeInput", step: "等待商品扫码输入框");
            scan.Focus();
            Keyboard.Type(config.ProductBarcode);
            Keyboard.Type(VirtualKeyShort.RETURN);
            var row = WaitForSingleCartRow("CartItemsGrid");
            var quantity = ReadQuantity(row, "CartLineQuantity");
            // 行模板中的 AutomationId 会重复，必须在目标行子树内定位。
            WaitForUiElement(
                    () => row.FindFirstDescendant("CartLineIncreaseButton"),
                    "等待购物车加号按钮",
                    "CartLineIncreaseButton")
                .AsButton()
                .Invoke();
            WaitForQuantity(row, quantity + 1m);

            _app.WaitForAutomationId("OpenPaymentButton", step: "等待支付入口").AsButton().Invoke();
            _app.WaitForAutomationId("PaymentScreen", TimeSpan.FromSeconds(30), "等待支付页面");
            _app.WaitForAutomationId("AddCashTenderButton", step: "等待现金付款按钮").AsButton().Invoke();
            WaitForTenderAndZeroRemaining();
            var confirm = _app.WaitForAutomationId("ConfirmPaymentButton", step: "等待确认支付按钮").AsButton();
            Assert.True(confirm.IsEnabled);
            confirm.Invoke();
            _app.WaitForAutomationId("PaymentSuccessScreen", TimeSpan.FromSeconds(60), "等待支付成功页");
            Assert.NotEqual("-", _app.WaitForAutomationId(
                "CompletedTransactionId",
                step: "等待交易编号").Name);
        }
        catch
        {
            _app.CaptureFailure(nameof(Store_cash_sale_reaches_payment_success));
            throw;
        }
        finally
        {
            _app.CloseOwnedProcess();
        }
    }

    internal static string ExtractAllowedStore(string display)
    {
        var matches = Regex.Matches(
            display,
            @"(?<!\d)(?:1042|1005)(?!\d)",
            RegexOptions.CultureInvariant);
        if (matches.Count != 1)
            throw new InvalidOperationException("当前界面门店必须且只能匹配 1042 或 1005。");
        return matches[0].Value;
    }

    internal static T WaitForUiElement<T>(
        Func<T?> find,
        string step,
        string automationId,
        TimeSpan? timeout = null)
        where T : class =>
        Retry.WhileNull(
            find,
            timeout: timeout ?? TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(100),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: $"{step}超时，AutomationId={automationId}。")
        .Result!;

    private void WaitUntilHidden(string automationId)
    {
        Retry.WhileFalse(
            () =>
            {
                var element = _app.MainWindow?.FindFirstDescendant(automationId);
                return element is null || element.IsOffscreen;
            },
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(100),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: $"等待登录遮盖隐藏超时，AutomationId={automationId}。");
    }

    private AutomationElement WaitForSingleCartRow(string automationId)
    {
        var grid = _app.WaitForAutomationId(automationId, step: "等待购物车列表").AsGrid();
        return Retry.WhileNull(
            () =>
            {
                var rows = grid.Rows;
                return rows.Length == 1 ? rows[0] : null;
            },
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(100),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: $"等待唯一购物车行超时，AutomationId={automationId}。")
        .Result!;
    }

    private static decimal ReadQuantity(AutomationElement row, string automationId)
    {
        var quantity = WaitForUiElement(
            () => row.FindFirstDescendant(automationId),
            "等待购物车数量控件",
            automationId);
        return ReadNumber(quantity.Name, automationId);
    }

    private static void WaitForQuantity(AutomationElement row, decimal expected)
    {
        const string automationId = "CartLineQuantity";
        Retry.WhileFalse(
            () => ReadQuantity(row, automationId) == expected,
            timeout: TimeSpan.FromSeconds(15),
            interval: TimeSpan.FromMilliseconds(100),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: $"等待购物车数量更新超时，AutomationId={automationId}。");
    }

    private void WaitForTenderAndZeroRemaining()
    {
        const string tenderId = "AppliedTendersList";
        const string remainingId = "RemainingAmount";
        var tenders = _app.WaitForAutomationId(tenderId, step: "等待已应用付款项列表");
        var remaining = _app.WaitForAutomationId(remainingId, step: "等待剩余金额");
        Retry.WhileFalse(
            () => tenders.FindAllChildren().Length > 0 && ReadNumber(remaining.Name, remainingId) == 0m,
            timeout: TimeSpan.FromSeconds(30),
            interval: TimeSpan.FromMilliseconds(100),
            throwOnTimeout: true,
            ignoreException: true,
            timeoutMessage: $"等待现金付款项和余额归零超时，AutomationId={tenderId}/{remainingId}。");
    }

    private static decimal ReadNumber(string text, string automationId)
    {
        var normalized = Regex.Replace(text, @"[^0-9,.-]", string.Empty);
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.GetCultureInfo("en-AU"), out var value))
            return value;
        throw new InvalidOperationException($"AutomationId={automationId} 的数值格式无效。");
    }
}

internal sealed record LiveDeviceBinding(string StoreCode, string DeviceCode)
{
    public static async Task<LiveDeviceBinding> ReadLatestAsync(
        string? databasePath = null,
        CancellationToken cancellationToken = default)
    {
        databasePath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hbpos.Client", "hbpos_client.db");
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT StoreCode, DeviceCode FROM DeviceCache ORDER BY UpdatedAt DESC LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("本机没有已注册的 WPF 设备绑定。");
        return new LiveDeviceBinding(reader.GetString(0).Trim(), reader.GetString(1).Trim());
    }

    public void EnsureMatches(string targetStoreCode)
    {
        if (!string.Equals(StoreCode, targetStoreCode, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(DeviceCode))
            throw new InvalidOperationException("本机设备门店与 HBPOS_E2E_STORE_CODE 不一致，已在业务操作前中止。");
    }
}

internal sealed record LiveE2eConfiguration(
    string StoreCode,
    string CashierBarcode,
    string ProductBarcode,
    Uri PosApiBaseUrl,
    Uri BackendBaseUrl,
    string BackendBearerToken)
{
    private static readonly HashSet<string> AllowedStores = ["1042", "1005"];

    public static LiveE2eConfiguration FromEnvironment(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;
        if (!string.Equals(read("HBPOS_E2E_ENABLED"), "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("必须显式设置 HBPOS_E2E_ENABLED=true。");
        if (!string.Equals(read("HBPOS_E2E_ADMIN_AUDIT_SCOPE_CONFIRMED"), "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("必须确认 HBPOS_E2E_ADMIN_AUDIT_SCOPE_CONFIRMED=true。");
        if (string.Equals(read("HBPOS_OPERATION_AUDIT_UPLOAD_ENABLED"), "false", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("HBPOS_OPERATION_AUDIT_UPLOAD_ENABLED 不能为 false。");

        var store = Required("HBPOS_E2E_STORE_CODE", read);
        if (!AllowedStores.Contains(store))
            throw new InvalidOperationException("HBPOS_E2E_STORE_CODE 只允许 1042 或 1005。");

        return new LiveE2eConfiguration(
            store,
            Required("HBPOS_E2E_CASHIER_BARCODE", read),
            Required("HBPOS_E2E_PRODUCT_BARCODE", read),
            RequiredUri("HBPOS_API_BASE_URL", read),
            RequiredUri("HBPOS_E2E_BACKEND_BASE_URL", read),
            Required("HBPOS_E2E_BACKEND_BEARER_TOKEN", read));
    }

    private static string Required(string name, Func<string, string?> read)
    {
        var value = read(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"缺少环境变量 {name}。")
            : value.Trim();
    }

    private static Uri RequiredUri(string name, Func<string, string?> read)
    {
        var value = Required(name, read);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException($"环境变量 {name} 必须是绝对 HTTP/HTTPS URI。");
        return uri;
    }
}
