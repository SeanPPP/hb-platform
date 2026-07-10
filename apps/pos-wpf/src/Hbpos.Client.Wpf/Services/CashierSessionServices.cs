using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using BlazorApp.Shared.Constants;
using Hbpos.Client.Wpf.Models;
using Hbpos.Contracts.Cashiers;
using Hbpos.Contracts.Common;

namespace Hbpos.Client.Wpf.Services;

public interface ICashierSessionContext
{
    CashierSessionDto? CurrentSession { get; }

    void SetCurrent(CashierSessionDto session);

    void Clear();

    bool HasPermission(string permissionCode);

    bool RequirePermission(string permissionCode, out string message);
}

public sealed class CashierSessionContext : ICashierSessionContext
{
    private static readonly string[] AllPosTerminalPermissions = Permissions.GetAllPermissions()
        .Select(permission => permission.Code)
        .Where(code => code.StartsWith("Permissions.PosTerminal.", StringComparison.Ordinal))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public CashierSessionDto? CurrentSession { get; private set; }

    public void SetCurrent(CashierSessionDto session)
    {
        CurrentSession = session;
    }

    public void Clear()
    {
        CurrentSession = null;
    }

    public bool HasPermission(string permissionCode)
    {
        if (CurrentSession is null)
        {
            return false;
        }

        // 中文注释：超级密码和后台超管只在本机上下文授予 POS 端权限，不落库。
        if (CurrentSession.IsEmergencyOverride || CurrentSession.IsSuperAdmin)
        {
            return permissionCode.StartsWith("Permissions.PosTerminal.", StringComparison.Ordinal);
        }

        return Permissions.ExpandPermissionCodes(CurrentSession.PermissionCodes)
            .Contains(permissionCode, StringComparer.OrdinalIgnoreCase);
    }

    public bool RequirePermission(string permissionCode, out string message)
    {
        if (HasPermission(permissionCode))
        {
            message = string.Empty;
            return true;
        }

        message = GetDeniedMessage(permissionCode);
        return false;
    }

    public static CashierSessionDto CreateEmergencyOverride(string storeCode, string deviceCode, DateOnly businessDate)
    {
        // 小票和历史直接使用 CashierName，超级密码只保留权限标记，名称使用固定用户标识。
        return new CashierSessionDto(
            "EMERGENCY",
            "EMERGENCY",
            "EMERGENCY",
            storeCode,
            deviceCode,
            ["EmergencyOverride"],
            AllPosTerminalPermissions,
            [storeCode],
            IsSuperAdmin: true,
            IsOfflineCached: false,
            IsEmergencyOverride: true);
    }

    public static PosSessionState ApplyToSession(PosSessionState state, CashierSessionDto cashier)
    {
        return state with
        {
            StoreCode = cashier.StoreCode,
            DeviceCode = cashier.DeviceCode,
            CashierId = cashier.CashierId,
            CashierName = cashier.CashierName,
            CashierSession = cashier
        };
    }

    private static string GetDeniedMessage(string permissionCode)
    {
        return permissionCode switch
        {
            Permissions.PosTerminal.Sales.AddItem => "当前收银员没有添加商品权限",
            Permissions.PosTerminal.Sales.AddOpenItem => "当前收银员没有添加自定义商品权限",
            Permissions.PosTerminal.Sales.RemoveLine => "当前收银员没有删行权限",
            Permissions.PosTerminal.Sales.ChangeQuantity => "当前收银员没有改数量权限",
            Permissions.PosTerminal.Sales.ChangePrice => "当前收银员没有改价权限",
            Permissions.PosTerminal.Sales.LineDiscount => "当前收银员没有行折扣权限",
            Permissions.PosTerminal.Sales.OrderDiscount => "当前收银员没有整单折扣权限",
            Permissions.PosTerminal.Sales.ClearCart => "当前收银员没有清空购物车权限",
            Permissions.PosTerminal.Sales.HoldOrder => "当前收银员没有挂单权限",
            Permissions.PosTerminal.Sales.RecallOrder => "当前收银员没有取单权限",
            Permissions.PosTerminal.Payment.View => "当前收银员没有进入付款页权限",
            Permissions.PosTerminal.Payment.TakeCash => "当前收银员没有收现金权限",
            Permissions.PosTerminal.Payment.TakeCard => "当前收银员没有收卡权限",
            Permissions.PosTerminal.Payment.TakeVoucher => "当前收银员没有收券权限",
            Permissions.PosTerminal.Payment.RemoveTender => "当前收银员没有移除付款权限",
            Permissions.PosTerminal.Payment.Confirm => "当前收银员没有确认付款权限",
            Permissions.PosTerminal.Returns.View => "当前收银员没有进入退货页权限",
            Permissions.PosTerminal.Returns.AddReceiptLine => "当前收银员没有添加小票退货行权限",
            Permissions.PosTerminal.Returns.AddNoReceiptItem => "当前收银员没有添加无票退货商品权限",
            Permissions.PosTerminal.Returns.Confirm => "当前收银员没有确认退货权限",
            Permissions.PosTerminal.SpecialProducts.View => "当前收银员没有进入特价商品页权限",
            Permissions.PosTerminal.SpecialProducts.AddToCart => "当前收银员没有添加特价商品权限",
            Permissions.PosTerminal.SpecialProducts.Manage => "当前收银员没有维护特价商品权限",
            Permissions.PosTerminal.History.View => "当前收银员没有查看交易历史权限",
            Permissions.PosTerminal.History.Recall => "当前收银员没有调取历史交易权限",
            Permissions.PosTerminal.History.Reprint => "当前收银员没有历史小票补打权限",
            Permissions.PosTerminal.CashDrawer.Open => "当前收银员没有开钱箱权限",
            Permissions.PosTerminal.Receipt.PrintLast => "当前收银员没有补打小票权限",
            Permissions.PosTerminal.Settings.View => "当前收银员没有查看设置权限",
            Permissions.PosTerminal.Settings.PaymentTerminal => "当前收银员没有支付终端设置权限",
            Permissions.PosTerminal.Settings.ReceiptPrinter => "当前收银员没有小票打印机设置权限",
            Permissions.PosTerminal.Settings.CatalogDownload => "当前收银员没有下载商品资料权限",
            Permissions.PosTerminal.Settings.CatalogReset => "当前收银员没有重置商品资料权限",
            Permissions.PosTerminal.Settings.TestDataReset => "当前收银员没有重置测试销售数据权限",
            Permissions.PosTerminal.Settings.DeviceRegistration => "当前收银员没有重新注册设备权限",
            Permissions.PosTerminal.Settings.AppUpdate => "当前收银员没有检查应用更新权限",
            Permissions.PosTerminal.DailyClose.View => "当前收银员没有查看日结权限",
            Permissions.PosTerminal.DailyClose.Save => "当前收银员没有保存日结权限",
            Permissions.PosTerminal.DailyClose.Reprint => "当前收银员没有补打日结权限",
            Permissions.PosTerminal.Installments.View => "当前收银员没有查看分期权限",
            Permissions.PosTerminal.Installments.Create => "当前收银员没有创建分期权限",
            Permissions.PosTerminal.Installments.AddRepayment => "当前收银员没有添加分期还款权限",
            Permissions.PosTerminal.Installments.Cancel => "当前收银员没有取消分期权限",
            Permissions.PosTerminal.Installments.ConfirmPickup => "当前收银员没有确认分期取货权限",
            Permissions.PosTerminal.CustomerDisplay.Manage => "当前收银员没有管理客显权限",
            Permissions.PosTerminal.System.Sync => "当前收银员没有手动同步权限",
            _ => "当前收银员没有权限"
        };
    }
}

public sealed class EmergencyOverridePasswordService(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private int _failedAttempts;
    private DateTimeOffset? _lockedUntil;

    public bool TryCreateOverride(
        string input,
        string storeCode,
        string deviceCode,
        out CashierSessionDto? session,
        out string message)
    {
        var now = _timeProvider.GetLocalNow();
        if (_lockedUntil is not null && now < _lockedUntil.Value)
        {
            session = null;
            message = "超级密码失败次数过多，请稍后再试";
            return false;
        }

        if (string.Equals(input.Trim(), BuildPassword(now.Date), StringComparison.Ordinal))
        {
            _failedAttempts = 0;
            _lockedUntil = null;
            session = CashierSessionContext.CreateEmergencyOverride(storeCode, deviceCode, DateOnly.FromDateTime(now.Date));
            message = "已启用超级密码权限";
            return true;
        }

        _failedAttempts++;
        if (_failedAttempts >= 5)
        {
            _lockedUntil = now.AddSeconds(60);
        }

        session = null;
        message = "超级密码错误";
        return false;
    }

    private static string BuildPassword(DateTime date)
    {
        var isoDay = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
        return $"{date:yyyyMMdd}{isoDay}";
    }
}

public sealed record CashierLoginAttempt(
    bool IsOnlineRejected,
    bool IsApiUnavailable,
    CashierSessionDto? Session,
    string Message)
{
    public static CashierLoginAttempt OnlineAccepted(CashierSessionDto session) => new(false, false, session, string.Empty);

    public static CashierLoginAttempt OnlineRejected(string message) => new(true, false, null, message);

    public static CashierLoginAttempt ApiUnavailable() => new(false, true, null, "收银员登录服务不可用");
}

public sealed record CashierLoginResult(bool Succeeded, CashierSessionDto? Session, string Message)
{
    public static CashierLoginResult Success(CashierSessionDto session) => new(true, session, string.Empty);

    public static CashierLoginResult Fail(string message) => new(false, null, message);
}

public interface ICashierLoginApiClient
{
    Task<CashierLoginAttempt> LoginAsync(
        CashierBarcodeLoginRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class CashierLoginApiClient(HttpClient httpClient) : ICashierLoginApiClient
{
    public async Task<CashierLoginAttempt> LoginAsync(
        CashierBarcodeLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync("api/v1/cashiers/barcode-login", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                if (IsServiceUnavailable(response.StatusCode))
                {
                    return CashierLoginAttempt.ApiUnavailable();
                }

                ApiResult<CashierSessionDto>? failed;
                try
                {
                    failed = await response.Content.ReadFromJsonAsync<ApiResult<CashierSessionDto>>(cancellationToken);
                }
                catch (JsonException)
                {
                    return CashierLoginAttempt.OnlineRejected("收银员条码无效或已停用");
                }

                return CashierLoginAttempt.OnlineRejected(failed?.Message ?? "收银员条码无效或已停用");
            }

            var result = await response.Content.ReadFromJsonAsync<ApiResult<CashierSessionDto>>(cancellationToken);
            return result?.Success == true && result.Data is not null
                ? CashierLoginAttempt.OnlineAccepted(result.Data)
                : CashierLoginAttempt.OnlineRejected(result?.Message ?? "收银员条码无效或已停用");
        }
        catch (JsonException)
        {
            // 关键逻辑：服务端 5xx/网关错误常返回 HTML 或空响应，这不是在线拒绝，应允许离线缓存兜底。
            return CashierLoginAttempt.ApiUnavailable();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return CashierLoginAttempt.ApiUnavailable();
        }
    }

    private static bool IsServiceUnavailable(HttpStatusCode statusCode)
    {
        var numericStatusCode = (int)statusCode;
        return numericStatusCode >= 500 ||
            statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;
    }
}

public interface ICashierLoginService
{
    Task<CashierLoginResult> LoginAsync(
        string storeCode,
        string deviceCode,
        string userBarcode,
        CancellationToken cancellationToken = default);
}

public sealed class CashierLoginService(
    ICashierLoginApiClient apiClient,
    ILocalAppSettingsRepository settingsRepository) : ICashierLoginService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CashierLoginResult> LoginAsync(
        string storeCode,
        string deviceCode,
        string userBarcode,
        CancellationToken cancellationToken = default)
    {
        var request = new CashierBarcodeLoginRequest(storeCode, userBarcode, deviceCode);
        var attempt = await apiClient.LoginAsync(request, cancellationToken);
        if (attempt.IsOnlineRejected)
        {
            await settingsRepository.DeleteValueAsync(BuildCacheKey(storeCode, deviceCode, userBarcode), cancellationToken);
            return CashierLoginResult.Fail(attempt.Message);
        }

        if (attempt.Session is not null)
        {
            await CacheSessionAsync(storeCode, deviceCode, userBarcode, attempt.Session, cancellationToken);
            return CashierLoginResult.Success(attempt.Session);
        }

        if (!attempt.IsApiUnavailable)
        {
            return CashierLoginResult.Fail(attempt.Message);
        }

        var cached = await ReadCachedSessionAsync(storeCode, deviceCode, userBarcode, cancellationToken);
        return cached is null
            ? CashierLoginResult.Fail(attempt.Message)
            : CashierLoginResult.Success(cached);
    }

    private async Task CacheSessionAsync(
        string storeCode,
        string deviceCode,
        string userBarcode,
        CashierSessionDto session,
        CancellationToken cancellationToken)
    {
        await settingsRepository.SetValueAsync(
            BuildCacheKey(storeCode, deviceCode, userBarcode),
            JsonSerializer.Serialize(session with { IsOfflineCached = false }, JsonOptions),
            cancellationToken);
    }

    private async Task<CashierSessionDto?> ReadCachedSessionAsync(
        string storeCode,
        string deviceCode,
        string userBarcode,
        CancellationToken cancellationToken)
    {
        var json = await settingsRepository.GetValueAsync(BuildCacheKey(storeCode, deviceCode, userBarcode), cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var session = JsonSerializer.Deserialize<CashierSessionDto>(json, JsonOptions);
        if (session?.AllowedStoreCodes.Contains(storeCode, StringComparer.OrdinalIgnoreCase) != true)
        {
            return null;
        }

        // 中文注释：离线只复用同门店、同设备、同条码缓存，并显式标记来源。
        return session with
        {
            StoreCode = storeCode,
            DeviceCode = deviceCode,
            IsOfflineCached = true,
            IsEmergencyOverride = false
        };
    }

    private static string BuildCacheKey(string storeCode, string deviceCode, string userBarcode)
    {
        return $"cashier-session:{storeCode.Trim()}:{deviceCode.Trim()}:{userBarcode.Trim()}";
    }
}
