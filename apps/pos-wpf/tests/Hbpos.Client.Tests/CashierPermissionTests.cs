using System.Net;
using System.Text;
using System.Text.Json;
using BlazorApp.Shared.DTOs;
using Hbpos.Client.Wpf.Models;
using Hbpos.Client.Wpf.Services;
using Hbpos.Client.Wpf.ViewModels;
using Hbpos.Contracts.Cashiers;
using Hbpos.Contracts.Catalog;
using Hbpos.Contracts.Orders;
using BlazorApp.Shared.Constants;

namespace Hbpos.Client.Tests;

public sealed class CashierPermissionTests
{
    [Fact]
    public void Cashier_context_denies_permission_until_cashier_logs_in()
    {
        var context = new CashierSessionContext();

        Assert.False(context.HasPermission(Permissions.PosTerminal.Sales.AddItem));
        Assert.False(context.RequirePermission(Permissions.PosTerminal.Sales.ChangePrice, out var message));
        Assert.Equal("当前收银员没有改价权限", message);
    }

    [Fact]
    public void Cashier_context_compare_and_swap_cannot_replace_or_clear_a_newer_session()
    {
        var original = CreateSession() with { AuthorizationToken = "ticket-a" };
        var newer = original with { AuthorizationToken = "ticket-b" };
        var context = new CashierSessionContext();
        context.SetCurrent(original);
        context.SetCurrent(newer);

        Assert.False(context.TrySetCurrent(original, original with { AuthorizationToken = "ticket-a-renewed" }));
        Assert.False(context.TryClear(original));
        Assert.Same(newer, context.CurrentSession);
    }

    [Fact]
    public void Emergency_override_grants_all_pos_terminal_permissions_without_cache()
    {
        var context = new CashierSessionContext();

        context.SetCurrent(CashierSessionContext.CreateEmergencyOverride(
            "S001", "POS-01", Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1), "token"));

        Assert.True(context.HasPermission(Permissions.PosTerminal.Sales.ChangePrice));
        Assert.True(context.HasPermission(Permissions.PosTerminal.Payment.Confirm));
        Assert.True(context.CurrentSession!.IsEmergencyOverride);
        Assert.Contains(Permissions.PosTerminal.CashDrawer.Open, context.CurrentSession.PermissionCodes);
    }

    [Fact]
    public void Emergency_override_is_cleared_when_permission_is_checked_after_expiry()
    {
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-14T03:00:00Z"));
        var context = new CashierSessionContext(time);
        context.SetCurrent(CashierSessionContext.CreateEmergencyOverride(
            "S001", "POS-01", Guid.NewGuid(), time.UtcNow.AddMinutes(1), "token"));

        time.UtcNow = time.UtcNow.AddMinutes(2);

        Assert.False(context.HasPermission(Permissions.PosTerminal.Payment.Confirm));
        Assert.Null(context.CurrentSession);
    }

    [Fact]
    public void Legacy_line_discount_snapshot_allows_all_new_line_discounts_but_not_order_discounts()
    {
        var context = new CashierSessionContext();
        context.SetCurrent(CreateSession(permissionCodes: [Permissions.PosTerminal.Sales.LineDiscount]));

        Assert.True(context.HasPermission(Permissions.PosTerminal.Sales.LineManualDiscount));
        Assert.True(context.HasPermission(Permissions.PosTerminal.Sales.LineQuickDiscount10Percent));
        Assert.True(context.HasPermission(Permissions.PosTerminal.Sales.LineQuickDiscount20Percent));
        Assert.True(context.HasPermission(Permissions.PosTerminal.Sales.LineQuickDiscount30Percent));
        Assert.True(context.HasPermission(Permissions.PosTerminal.Sales.LineQuickDiscount40Percent));
        Assert.True(context.HasPermission(Permissions.PosTerminal.Sales.LineQuickDiscount50Percent));
        Assert.False(context.HasPermission(Permissions.PosTerminal.Sales.OrderManualDiscount));
    }

    [Fact]
    public void Partial_new_line_discount_snapshot_does_not_expand_to_other_percentages()
    {
        var context = new CashierSessionContext();
        context.SetCurrent(CreateSession(permissionCodes: [Permissions.PosTerminal.Sales.LineQuickDiscount10Percent]));

        Assert.True(context.HasPermission(Permissions.PosTerminal.Sales.LineQuickDiscount10Percent));
        Assert.False(context.HasPermission(Permissions.PosTerminal.Sales.LineManualDiscount));
        Assert.False(context.HasPermission(Permissions.PosTerminal.Sales.LineQuickDiscount20Percent));
    }

    [Fact]
    public async Task Cashier_login_uses_offline_cache_only_when_online_api_is_unreachable_and_store_allowed()
    {
        var settings = new InMemoryAppSettingsRepository();
        var onlineSession = CreateSession(
            permissionCodes: [Permissions.PosTerminal.Sales.AddItem],
            allowedStoreCodes: ["S001"]);
        var service = new CashierLoginService(
            new SequenceCashierLoginApiClient(
                CashierLoginAttempt.OnlineAccepted(onlineSession),
                CashierLoginAttempt.ApiUnavailable()),
            settings,
            new PassthroughProtector());

        var first = await service.LoginAsync("S001", "POS-01", "BAR-1");
        var second = await service.LoginAsync("S001", "POS-01", "BAR-1");

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.False(first.Session!.IsOfflineCached);
        Assert.True(second.Session!.IsOfflineCached);
        Assert.Equal("C001", second.Session.CashierId);
        Assert.DoesNotContain(settings.Keys, key => key.Contains("BAR-1", StringComparison.Ordinal));
        Assert.All(settings.Values, value => Assert.StartsWith("protected:", value, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cashier_login_known_offline_reads_cache_without_calling_api()
    {
        var settings = new InMemoryAppSettingsRepository();
        var protector = new PassthroughProtector();
        var onlineSession = CreateSession(allowedStoreCodes: ["S001"]);
        var seedService = new CashierLoginService(
            new SequenceCashierLoginApiClient(CashierLoginAttempt.OnlineAccepted(onlineSession)),
            settings,
            protector);
        Assert.True((await seedService.LoginAsync("S001", "POS-01", "BAR-1")).Succeeded);

        var api = new NeverCompletingCashierLoginApiClient();
        var service = new CashierLoginService(api, settings, protector);
        var result = await service.LoginAsync(
            "S001",
            "POS-01",
            "BAR-1",
            attemptOnline: false).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(result.Succeeded);
        Assert.True(result.Session!.IsOfflineCached);
        Assert.Equal(0, api.CallCount);
    }

    [Fact]
    public async Task Cashier_login_known_offline_cache_miss_fails_without_calling_api()
    {
        var api = new NeverCompletingCashierLoginApiClient();
        var service = new CashierLoginService(
            api,
            new InMemoryAppSettingsRepository(),
            new PassthroughProtector());

        var result = await service.LoginAsync(
            "S001",
            "POS-01",
            "BAR-MISSING",
            attemptOnline: false).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(result.Succeeded);
        Assert.Equal("CASHIER_LOGIN_API_UNAVAILABLE", result.ErrorCode);
        Assert.Equal(0, api.CallCount);
    }

    [Fact]
    public async Task Cashier_login_keeps_cache_after_online_rejection_but_does_not_fallback_for_that_attempt()
    {
        var settings = new InMemoryAppSettingsRepository();
        var service = new CashierLoginService(
            new SequenceCashierLoginApiClient(
                CashierLoginAttempt.OnlineAccepted(CreateSession()),
                CashierLoginAttempt.OnlineRejected("条码无效"),
                CashierLoginAttempt.ApiUnavailable()),
            settings,
            new PassthroughProtector());

        await service.LoginAsync("S001", "POS-01", "BAR-1");
        var rejected = await service.LoginAsync("S001", "POS-01", "BAR-1");
        var unavailable = await service.LoginAsync("S001", "POS-01", "BAR-1");

        Assert.False(rejected.Succeeded);
        Assert.Null(rejected.Session);
        Assert.Equal("条码无效", rejected.Message);
        Assert.True(unavailable.Succeeded);
        Assert.True(unavailable.Session!.IsOfflineCached);
        Assert.Equal("C001", unavailable.Session.CashierId);
    }

    [Fact]
    public async Task Cashier_login_ignores_legacy_plaintext_cache_until_next_online_success_removes_it()
    {
        var settings = new InMemoryAppSettingsRepository();
        const string legacyKey = "cashier-session:S001:POS-01:BAR-1";
        await settings.SetValueAsync(
            legacyKey,
            JsonSerializer.Serialize(CreateSession(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var service = new CashierLoginService(
            new SequenceCashierLoginApiClient(
                CashierLoginAttempt.ApiUnavailable(),
                CashierLoginAttempt.OnlineAccepted(CreateSession())),
            settings,
            new PassthroughProtector());

        var offline = await service.LoginAsync("S001", "POS-01", "BAR-1");
        var online = await service.LoginAsync("S001", "POS-01", "BAR-1");

        Assert.False(offline.Succeeded);
        Assert.True(online.Succeeded);
        Assert.DoesNotContain(legacyKey, settings.Keys);
        Assert.Single(settings.Keys);
        Assert.DoesNotContain(settings.Keys, key => key.Contains("BAR-1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Expired_online_authorization_ticket_does_not_expire_offline_cashier_cache()
    {
        var expiredOnlineSession = CreateSession() with
        {
            AuthorizationToken = "expired-ticket",
            AuthorizationExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
        };
        var service = new CashierLoginService(
            new SequenceCashierLoginApiClient(
                CashierLoginAttempt.OnlineAccepted(expiredOnlineSession),
                CashierLoginAttempt.ApiUnavailable()),
            new InMemoryAppSettingsRepository(),
            new PassthroughProtector());

        Assert.True((await service.LoginAsync("S001", "POS-01", "BAR-1")).Succeeded);
        var offline = await service.LoginAsync("S001", "POS-01", "BAR-1");

        Assert.True(offline.Succeeded);
        Assert.True(offline.Session!.IsOfflineCached);
        Assert.Equal("expired-ticket", offline.Session.AuthorizationToken);
    }

    [Fact]
    public async Task Cashier_session_refresh_updates_encrypted_cache_before_replacing_current_session()
    {
        var current = CreateSession(permissionCodes: [Permissions.PosTerminal.Sales.AddItem]);
        var refreshed = current with
        {
            PermissionCodes = [Permissions.PosTerminal.Sales.LineQuickDiscount20Percent],
            AuthorizationToken = "renewed-ticket"
        };
        var context = new CashierSessionContext();
        context.SetCurrent(current);
        var cache = new RecordingCashierSessionCacheUpdater(() => context.CurrentSession);
        var service = new CashierSessionRefreshService(
            new SequenceCashierSessionRefreshApiClient(CashierSessionRefreshAttempt.Refreshed(refreshed)),
            context,
            cache);

        await service.RefreshOnceAsync();

        Assert.Same(refreshed, context.CurrentSession);
        Assert.Same(refreshed, cache.UpdatedSession);
        Assert.Same(current, cache.SessionObservedDuringUpdate);
        Assert.False(cache.Removed);
    }

    [Fact]
    public async Task Cashier_session_refresh_persists_new_permissions_to_encrypted_offline_cache()
    {
        var settings = new InMemoryAppSettingsRepository();
        var protector = new PassthroughProtector();
        var initial = CreateSession(permissionCodes: [Permissions.PosTerminal.Sales.AddItem]);
        var refreshed = initial with
        {
            PermissionCodes = [Permissions.PosTerminal.Sales.LineQuickDiscount20Percent],
            AuthorizationToken = "renewed-ticket"
        };
        var loginService = new CashierLoginService(
            new SequenceCashierLoginApiClient(CashierLoginAttempt.OnlineAccepted(initial)),
            settings,
            protector);
        var login = await loginService.LoginAsync("S001", "POS-01", "BAR-REFRESH");
        var context = new CashierSessionContext();
        context.SetCurrent(login.Session!);
        var refreshService = new CashierSessionRefreshService(
            new SequenceCashierSessionRefreshApiClient(CashierSessionRefreshAttempt.Refreshed(refreshed)),
            context,
            loginService);

        await refreshService.RefreshOnceAsync();

        var offlineLoginService = new CashierLoginService(
            new SequenceCashierLoginApiClient(CashierLoginAttempt.ApiUnavailable()),
            settings,
            protector);
        var offline = await offlineLoginService.LoginAsync("S001", "POS-01", "BAR-REFRESH");
        Assert.True(offline.Succeeded);
        Assert.True(offline.Session!.IsOfflineCached);
        Assert.Contains(Permissions.PosTerminal.Sales.LineQuickDiscount20Percent, offline.Session.PermissionCodes);
        Assert.DoesNotContain(Permissions.PosTerminal.Sales.AddItem, offline.Session.PermissionCodes);
        Assert.All(settings.Values, value => Assert.StartsWith("protected:", value, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cashier_session_refresh_keeps_last_snapshot_when_api_is_unavailable()
    {
        var current = CreateSession();
        var context = new CashierSessionContext();
        context.SetCurrent(current);
        var cache = new RecordingCashierSessionCacheUpdater(() => context.CurrentSession);
        var service = new CashierSessionRefreshService(
            new SequenceCashierSessionRefreshApiClient(CashierSessionRefreshAttempt.ApiUnavailable()),
            context,
            cache);

        await service.RefreshOnceAsync();

        Assert.Same(current, context.CurrentSession);
        Assert.Null(cache.UpdatedSession);
        Assert.False(cache.Removed);
    }

    [Fact]
    public async Task Cashier_session_refresh_clears_session_and_cache_after_online_rejection()
    {
        var current = CreateSession();
        var context = new CashierSessionContext();
        context.SetCurrent(current);
        var cache = new RecordingCashierSessionCacheUpdater(() => context.CurrentSession);
        var service = new CashierSessionRefreshService(
            new SequenceCashierSessionRefreshApiClient(CashierSessionRefreshAttempt.OnlineRejected()),
            context,
            cache);

        await service.RefreshOnceAsync();

        Assert.Null(context.CurrentSession);
        Assert.True(cache.Removed);
        Assert.Same(current, cache.RemovedSession);
    }

    [Fact]
    public async Task Cashier_session_refresh_skips_emergency_override()
    {
        var context = new CashierSessionContext();
        context.SetCurrent(CashierSessionContext.CreateEmergencyOverride(
            "S001", "POS-01", Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1), "token"));
        var api = new SequenceCashierSessionRefreshApiClient(CashierSessionRefreshAttempt.OnlineRejected());
        var cache = new RecordingCashierSessionCacheUpdater(() => context.CurrentSession);
        var service = new CashierSessionRefreshService(api, context, cache);

        await service.RefreshOnceAsync();

        Assert.NotNull(context.CurrentSession);
        Assert.Equal(0, api.CallCount);
        Assert.False(cache.Removed);
    }

    [Fact]
    public async Task Cashier_session_refresh_success_does_not_replace_a_newer_login_session()
    {
        var original = CreateSession() with { AuthorizationToken = "ticket-a" };
        var newer = CreateSession() with
        {
            UserGuid = "user-b",
            CashierId = "cashier-b",
            AuthorizationToken = "ticket-b"
        };
        var refreshedOriginal = original with
        {
            PermissionCodes = [Permissions.PosTerminal.Sales.LineQuickDiscount20Percent],
            AuthorizationToken = "ticket-a-renewed"
        };
        var context = new CashierSessionContext();
        context.SetCurrent(original);
        var api = new DeferredCashierSessionRefreshApiClient();
        var cache = new RecordingCashierSessionCacheUpdater(() => context.CurrentSession);
        var service = new CashierSessionRefreshService(api, context, cache);

        var refreshTask = service.RefreshOnceAsync();
        await api.Started.Task;
        context.SetCurrent(newer);
        api.Complete(CashierSessionRefreshAttempt.Refreshed(refreshedOriginal));
        await refreshTask;

        Assert.Same(newer, context.CurrentSession);
        Assert.Null(cache.UpdatedSession);
        Assert.False(cache.Removed);
    }

    [Fact]
    public async Task Cashier_session_refresh_cas_failure_restores_newer_same_identity_cache()
    {
        var original = CreateSession() with
        {
            PermissionCodes = [Permissions.PosTerminal.Sales.AddItem],
            AuthorizationToken = "ticket-a"
        };
        var newer = original with
        {
            PermissionCodes = [Permissions.PosTerminal.Sales.LineQuickDiscount30Percent],
            AuthorizationToken = "ticket-b"
        };
        var refreshedOriginal = original with
        {
            PermissionCodes = [Permissions.PosTerminal.Sales.LineQuickDiscount20Percent],
            AuthorizationToken = "ticket-a-renewed"
        };
        var context = new CashierSessionContext();
        context.SetCurrent(original);
        var updateCount = 0;
        var cache = new RecordingCashierSessionCacheUpdater(
            () => context.CurrentSession,
            _ =>
            {
                if (updateCount++ == 0)
                {
                    // 模拟缓存写入期间同一收银员重新登录并取得新票据。
                    context.SetCurrent(newer);
                }
            });
        var service = new CashierSessionRefreshService(
            new SequenceCashierSessionRefreshApiClient(
                CashierSessionRefreshAttempt.Refreshed(refreshedOriginal)),
            context,
            cache);

        await service.RefreshOnceAsync();

        Assert.Same(newer, context.CurrentSession);
        Assert.Equal([refreshedOriginal, newer], cache.UpdatedSessions);
        Assert.False(cache.Removed);
    }

    [Fact]
    public async Task Cashier_session_refresh_rejection_does_not_clear_a_newer_login_session()
    {
        var original = CreateSession() with { AuthorizationToken = "ticket-a" };
        var newer = CreateSession() with
        {
            UserGuid = "user-b",
            CashierId = "cashier-b",
            AuthorizationToken = "ticket-b"
        };
        var context = new CashierSessionContext();
        context.SetCurrent(original);
        var api = new DeferredCashierSessionRefreshApiClient();
        var cache = new RecordingCashierSessionCacheUpdater(() => context.CurrentSession);
        var service = new CashierSessionRefreshService(api, context, cache);

        var refreshTask = service.RefreshOnceAsync();
        await api.Started.Task;
        context.SetCurrent(newer);
        api.Complete(CashierSessionRefreshAttempt.OnlineRejected());
        await refreshTask;

        Assert.Same(newer, context.CurrentSession);
        Assert.True(cache.Removed);
        Assert.Same(original, cache.RemovedSession);
    }

    [Fact]
    public async Task Cashier_session_refresh_rejection_deletes_rejected_cache_but_keeps_different_identity_login()
    {
        var settings = new InMemoryAppSettingsRepository();
        var protector = new PassthroughProtector();
        var original = CreateSession() with
        {
            UserGuid = "user-a",
            CashierId = "cashier-a",
            AuthorizationToken = "ticket-a",
            AuthorizationExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        var newer = CreateSession() with
        {
            UserGuid = "user-b",
            CashierId = "cashier-b",
            AuthorizationToken = "ticket-b",
            AuthorizationExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(2)
        };
        var loginService = new CashierLoginService(
            new SequenceCashierLoginApiClient(
                CashierLoginAttempt.OnlineAccepted(original),
                CashierLoginAttempt.OnlineAccepted(newer)),
            settings,
            protector);
        var firstLogin = await loginService.LoginAsync("S001", "POS-01", "BAR-A");
        var context = new CashierSessionContext();
        context.SetCurrent(firstLogin.Session!);
        var refreshApi = new DeferredCashierSessionRefreshApiClient();
        var refreshService = new CashierSessionRefreshService(refreshApi, context, loginService);

        var refreshTask = refreshService.RefreshOnceAsync();
        await refreshApi.Started.Task;
        var secondLogin = await loginService.LoginAsync("S001", "POS-01", "BAR-B");
        context.SetCurrent(secondLogin.Session!);
        refreshApi.Complete(CashierSessionRefreshAttempt.OnlineRejected());
        await refreshTask;

        var offlineA = await new CashierLoginService(
            new SequenceCashierLoginApiClient(CashierLoginAttempt.ApiUnavailable()),
            settings,
            protector).LoginAsync("S001", "POS-01", "BAR-A");
        var offlineB = await new CashierLoginService(
            new SequenceCashierLoginApiClient(CashierLoginAttempt.ApiUnavailable()),
            settings,
            protector).LoginAsync("S001", "POS-01", "BAR-B");
        Assert.Same(secondLogin.Session, context.CurrentSession);
        Assert.False(offlineA.Succeeded);
        Assert.True(offlineB.Succeeded);
        Assert.Equal("ticket-b", offlineB.Session!.AuthorizationToken);
    }

    [Fact]
    public async Task Cashier_session_refresh_rejection_does_not_delete_same_identity_new_ticket_cache()
    {
        var settings = new InMemoryAppSettingsRepository();
        var protector = new PassthroughProtector();
        var original = CreateSession() with
        {
            AuthorizationToken = "ticket-a",
            AuthorizationExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        var newer = original with
        {
            PermissionCodes = [Permissions.PosTerminal.Sales.LineQuickDiscount30Percent],
            AuthorizationToken = "ticket-b",
            AuthorizationExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(2)
        };
        var loginService = new CashierLoginService(
            new SequenceCashierLoginApiClient(
                CashierLoginAttempt.OnlineAccepted(original),
                CashierLoginAttempt.OnlineAccepted(newer)),
            settings,
            protector);
        var firstLogin = await loginService.LoginAsync("S001", "POS-01", "BAR-SAME");
        var context = new CashierSessionContext();
        context.SetCurrent(firstLogin.Session!);
        var refreshApi = new DeferredCashierSessionRefreshApiClient();
        var refreshService = new CashierSessionRefreshService(refreshApi, context, loginService);

        var refreshTask = refreshService.RefreshOnceAsync();
        await refreshApi.Started.Task;
        var secondLogin = await loginService.LoginAsync("S001", "POS-01", "BAR-SAME");
        context.SetCurrent(secondLogin.Session!);
        refreshApi.Complete(CashierSessionRefreshAttempt.OnlineRejected());
        await refreshTask;

        var offline = await new CashierLoginService(
            new SequenceCashierLoginApiClient(CashierLoginAttempt.ApiUnavailable()),
            settings,
            protector).LoginAsync("S001", "POS-01", "BAR-SAME");
        Assert.Same(secondLogin.Session, context.CurrentSession);
        Assert.True(offline.Succeeded);
        Assert.Equal("ticket-b", offline.Session!.AuthorizationToken);
        Assert.Contains(Permissions.PosTerminal.Sales.LineQuickDiscount30Percent, offline.Session.PermissionCodes);
    }

    [Fact]
    public void Cashier_session_refresh_host_uses_sixty_second_interval()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), CashierSessionRefreshHostedService.RefreshInterval);
    }

    [Fact]
    public void Windows_dpapi_protector_round_trips_for_current_user()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var protector = new WindowsDpapiDeviceAuthorizationProtector();
        const string plaintext = "cashier-cache-secret";

        var protectedValue = protector.Protect(plaintext);

        Assert.NotNull(protectedValue);
        Assert.NotEqual(plaintext, protectedValue);
        Assert.Equal(plaintext, protector.Unprotect(protectedValue));
    }

    [Fact]
    public async Task Cashier_login_api_client_treats_5xx_html_as_api_unavailable()
    {
        var client = new CashierLoginApiClient(new HttpClient(new StaticResponseHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("<html>bad gateway</html>", Encoding.UTF8, "text/html")
            }))
        {
            BaseAddress = new Uri("http://localhost/")
        });

        var attempt = await client.LoginAsync(new CashierBarcodeLoginRequest("S001", "BAR-1", "POS-01"));

        Assert.True(attempt.IsApiUnavailable);
        Assert.False(attempt.IsOnlineRejected);
        Assert.Equal("CASHIER_LOGIN_API_UNAVAILABLE", attempt.ErrorCode);
    }

    [Fact]
    public async Task Cashier_login_api_client_keeps_401_html_as_online_rejection()
    {
        var client = new CashierLoginApiClient(new HttpClient(new StaticResponseHandler(
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("<html>unauthorized</html>", Encoding.UTF8, "text/html")
            }))
        {
            BaseAddress = new Uri("http://localhost/")
        });

        var attempt = await client.LoginAsync(new CashierBarcodeLoginRequest("S001", "BAR-1", "POS-01"));

        Assert.True(attempt.IsOnlineRejected);
        Assert.False(attempt.IsApiUnavailable);
        Assert.Equal("CASHIER_LOGIN_FAILED", attempt.ErrorCode);
    }

    [Fact]
    public void Pos_terminal_blocks_change_price_without_permission()
    {
        var cart = new PosCartService();
        var line = cart.AddItem(CreateItem());
        var workflow = new FakePosTerminalWorkflowService();
        var viewModel = new PosTerminalViewModel(
            new LocalSellableItemIndex(),
            cart,
            Session,
            onOpenPayment: null,
            workflowService: workflow,
            cashierSessionContext: new CashierSessionContext(),
            enforcePermissionsWhenNoCashier: true);
        viewModel.SelectedCartLine = line;
        viewModel.KeypadBuffer = "1.00";

        viewModel.ModifySelectedLinePriceCommand.Execute(null);

        Assert.Equal(0, workflow.ChangePriceCalls);
        Assert.Equal("当前收银员没有改价权限", viewModel.StatusMessage);
    }

    [Theory]
    [InlineData(false, "Permissions.PosTerminal.Sales.LineManualDiscount")]
    [InlineData(true, "Permissions.PosTerminal.Sales.OrderManualDiscount")]
    public void Pos_terminal_manual_discount_only_uses_matching_line_or_order_permission(
        bool isWholeOrderOperation,
        string permissionCode)
    {
        var (viewModel, workflow, line) = CreateDiscountViewModel(permissionCode, isWholeOrderOperation);

        viewModel.KeypadBuffer = "1";
        viewModel.ApplySelectedLineDiscountAmountCommand.Execute(null);
        viewModel.KeypadBuffer = "10";
        viewModel.ApplySelectedLineDiscountPercentCommand.Execute(null);

        Assert.Equal(1, workflow.ManualDiscountAmountCalls);
        Assert.Equal(1, workflow.ManualDiscountPercentCalls);
        Assert.Equal(0m, line.DiscountAmount);
    }

    [Theory]
    [InlineData(false, "10", "Permissions.PosTerminal.Sales.LineQuickDiscount10Percent", "20", "当前收银员没有单行快速折扣 20% 权限")]
    [InlineData(false, "20", "Permissions.PosTerminal.Sales.LineQuickDiscount20Percent", "30", "当前收银员没有单行快速折扣 30% 权限")]
    [InlineData(false, "30", "Permissions.PosTerminal.Sales.LineQuickDiscount30Percent", "40", "当前收银员没有单行快速折扣 40% 权限")]
    [InlineData(false, "40", "Permissions.PosTerminal.Sales.LineQuickDiscount40Percent", "50", "当前收银员没有单行快速折扣 50% 权限")]
    [InlineData(false, "50", "Permissions.PosTerminal.Sales.LineQuickDiscount50Percent", "10", "当前收银员没有单行快速折扣 10% 权限")]
    [InlineData(true, "10", "Permissions.PosTerminal.Sales.OrderQuickDiscount10Percent", "20", "当前收银员没有整单快速折扣 20% 权限")]
    [InlineData(true, "20", "Permissions.PosTerminal.Sales.OrderQuickDiscount20Percent", "30", "当前收银员没有整单快速折扣 30% 权限")]
    [InlineData(true, "30", "Permissions.PosTerminal.Sales.OrderQuickDiscount30Percent", "40", "当前收银员没有整单快速折扣 40% 权限")]
    [InlineData(true, "40", "Permissions.PosTerminal.Sales.OrderQuickDiscount40Percent", "50", "当前收银员没有整单快速折扣 50% 权限")]
    [InlineData(true, "50", "Permissions.PosTerminal.Sales.OrderQuickDiscount50Percent", "10", "当前收银员没有整单快速折扣 10% 权限")]
    public void Pos_terminal_quick_discount_only_uses_its_exact_scope_and_percentage_permission(
        bool isWholeOrderOperation,
        string grantedPercent,
        string permissionCode,
        string deniedPercent,
        string deniedMessage)
    {
        var (viewModel, workflow, line) = CreateDiscountViewModel(permissionCode, isWholeOrderOperation);

        viewModel.ApplyQuickDiscountPercentCommand.Execute(grantedPercent);
        viewModel.ApplyQuickDiscountPercentCommand.Execute(deniedPercent);

        Assert.Equal([grantedPercent], workflow.QuickDiscountPercentValues);
        Assert.Equal(0m, line.DiscountAmount);
        Assert.Equal(deniedMessage, viewModel.StatusMessage);
    }

    [Fact]
    public void Pos_terminal_quick_discount_rejects_non_fixed_percentage_without_calling_workflow()
    {
        var (viewModel, workflow, line) = CreateDiscountViewModel(
            Permissions.PosTerminal.Sales.LineManualDiscount,
            isWholeOrderOperation: false);

        viewModel.ApplyQuickDiscountPercentCommand.Execute("15");

        Assert.Empty(workflow.QuickDiscountPercentValues);
        Assert.Equal(0m, line.DiscountAmount);
        Assert.Equal("快速折扣仅支持 10%、20%、30%、40%、50%", viewModel.StatusMessage);
    }

    [Fact]
    public void Pos_terminal_denied_new_discount_permissions_keep_cart_unchanged_and_record_scoped_audit()
    {
        var lineLogger = new RecordingOperationAuditLogger();
        var (lineViewModel, lineWorkflow, line) = CreateDiscountViewModel(
            Permissions.PosTerminal.Sales.AddItem,
            isWholeOrderOperation: false,
            lineLogger);

        lineViewModel.ApplyQuickDiscountPercentCommand.Execute("20");

        Assert.Empty(lineWorkflow.QuickDiscountPercentValues);
        Assert.Equal(0m, line.DiscountAmount);
        var lineAudit = Assert.Single(lineLogger.Events);
        Assert.Equal(OperationAuditTypes.CartLineDiscountChange, lineAudit.OperationType);
        Assert.Equal("PERMISSION_DENIED", lineAudit.ReasonCode);

        var orderLogger = new RecordingOperationAuditLogger();
        var (orderViewModel, orderWorkflow, _) = CreateDiscountViewModel(
            Permissions.PosTerminal.Sales.AddItem,
            isWholeOrderOperation: true,
            orderLogger);
        orderViewModel.KeypadBuffer = "10";

        orderViewModel.ApplySelectedLineDiscountPercentCommand.Execute(null);

        Assert.Equal(0, orderWorkflow.ManualDiscountPercentCalls);
        var orderAudit = Assert.Single(orderLogger.Events);
        Assert.Equal(OperationAuditTypes.CartOrderDiscountChange, orderAudit.OperationType);
        Assert.Equal("PERMISSION_DENIED", orderAudit.ReasonCode);
    }

    [Fact]
    public async Task Payment_page_blocks_cash_tender_without_permission()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem());
        var workflow = new FakeCashPaymentWorkflowService();
        var viewModel = new PaymentViewModel(
            cart,
            workflow,
            Session,
            cashierSessionContext: new CashierSessionContext(),
            enforcePermissionsWhenNoCashier: true);
        viewModel.TenderAmountText = "5";

        await viewModel.SelectCashCommand.ExecuteAsync(null);

        Assert.Equal(0, workflow.AddTenderCallCount);
        Assert.Equal("当前收银员没有收现金权限", viewModel.StatusMessage);
    }

    [Fact]
    public void Pos_terminal_blocks_payment_entry_without_permission()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem());
        var opened = false;
        var viewModel = new PosTerminalViewModel(
            new LocalSellableItemIndex(),
            cart,
            Session,
            onOpenPayment: () => opened = true,
            workflowService: new FakePosTerminalWorkflowService(),
            cashierSessionContext: new CashierSessionContext(),
            enforcePermissionsWhenNoCashier: true);

        viewModel.OpenPaymentCommand.Execute(null);

        Assert.False(opened);
        Assert.Equal("当前收银员没有进入付款页权限", viewModel.StatusMessage);
    }

    [Fact]
    public void Payment_page_blocks_installment_center_without_permission()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem());
        var opened = false;
        var viewModel = new PaymentViewModel(
            cart,
            new FakeCashPaymentWorkflowService(),
            Session,
            onShowInstallmentCenter: () => opened = true,
            cashierSessionContext: new CashierSessionContext(),
            enforcePermissionsWhenNoCashier: true);

        viewModel.ShowInstallmentCenterCommand.Execute(null);

        Assert.False(opened);
        Assert.Equal("当前收银员没有查看分期权限", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Card_tender_auto_complete_requires_confirm_permission_before_terminal_charge()
    {
        var cart = new PosCartService();
        cart.AddItem(CreateItem());
        var workflow = new FakeCashPaymentWorkflowService();
        var cashierContext = new CashierSessionContext();
        cashierContext.SetCurrent(CreateSession(permissionCodes: [Permissions.PosTerminal.Payment.TakeCard]));
        var viewModel = new PaymentViewModel(
            cart,
            workflow,
            Session,
            cashierSessionContext: cashierContext,
            enforcePermissionsWhenNoCashier: true);

        await viewModel.SelectCardCommand.ExecuteAsync(null);

        Assert.Equal(0, workflow.AddTenderCallCount);
        Assert.Equal(0, workflow.CompletePaymentCallCount);
        Assert.Empty(viewModel.PaymentTenders);
        Assert.Equal("当前收银员没有确认付款权限", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Pos_terminal_scanner_without_cashier_tries_cashier_login_fallback()
    {
        var cart = new PosCartService();
        var workflow = new FakePosTerminalWorkflowService
        {
            ScanResult = new PosTerminalWorkflowResult
            {
                StatusKey = "pos.status.noLocalMatch",
                Matches = []
            }
        };
        var cashierBarcode = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new PosTerminalViewModel(
            new LocalSellableItemIndex(),
            cart,
            Session,
            onOpenPayment: null,
            workflowService: workflow,
            tryLoginCashierFromScannerFallbackAsync: (barcode, _) =>
            {
                cashierBarcode.TrySetResult(barcode);
                return Task.FromResult(true);
            },
            cashierSessionContext: new CashierSessionContext(),
            enforcePermissionsWhenNoCashier: true);

        var processed = viewModel.ProcessScannerBarcode("1234567890123", "scanner-device", "raw");

        Assert.True(processed);
        Assert.Equal("1234567890123", await cashierBarcode.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        for (var attempt = 0; attempt < 20 && viewModel.ScanText.Length > 0; attempt++)
        {
            await Task.Delay(25);
        }

        Assert.Equal(0, workflow.ProcessScanAsyncCalls);
        Assert.Empty(viewModel.CartLines);
        Assert.Equal(string.Empty, viewModel.ScanText);
        Assert.False(viewModel.IsMatchesPopupOpen);
        Assert.Equal("收银员登录成功", viewModel.StatusMessage);
    }

    [Fact]
    public async Task Pos_terminal_scanner_without_cashier_prefers_login_fallback_over_known_product()
    {
        var cart = new PosCartService();
        var priceIndex = new LocalSellableItemIndex();
        priceIndex.ReplaceAll([CreateItem()]);
        var workflow = new FakePosTerminalWorkflowService();
        var cashierBarcode = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new PosTerminalViewModel(
            priceIndex,
            cart,
            Session,
            onOpenPayment: null,
            workflowService: workflow,
            tryLoginCashierFromScannerFallbackAsync: (barcode, _) =>
            {
                cashierBarcode.TrySetResult(barcode);
                return Task.FromResult(true);
            },
            cashierSessionContext: new CashierSessionContext(),
            enforcePermissionsWhenNoCashier: true);

        var processed = viewModel.ProcessScannerBarcode("930001", "scanner-device", "raw");

        Assert.True(processed);
        Assert.Equal("930001", await cashierBarcode.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(0, workflow.ProcessScanAsyncCalls);
        Assert.Empty(cart.Lines);
        Assert.Empty(viewModel.CartLines);
    }

    [Fact]
    public async Task Pos_terminal_routes_emergency_token_before_product_workflow()
    {
        var workflow = new FakePosTerminalWorkflowService();
        var scanned = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new PosTerminalViewModel(
            new LocalSellableItemIndex(),
            new PosCartService(),
            Session,
            onOpenPayment: null,
            workflowService: workflow,
            tryLoginCashierFromScannerFallbackAsync: (barcode, _) =>
            {
                scanned.TrySetResult(barcode);
                return Task.FromResult(true);
            },
            cashierSessionContext: new CashierSessionContext(),
            enforcePermissionsWhenNoCashier: false);

        Assert.True(viewModel.ProcessScannerBarcode("HBPOSE1-K1-AA-BB", "scanner", "raw"));
        Assert.Equal("HBPOSE1-K1-AA-BB", await scanned.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(0, workflow.ProcessScanAsyncCalls);
    }

    [Fact]
    public async Task Pos_terminal_scanner_with_cashier_without_add_permission_never_switches_cashier_on_unknown_barcode()
    {
        var cart = new PosCartService();
        var workflow = new FakePosTerminalWorkflowService
        {
            ScanResult = new PosTerminalWorkflowResult
            {
                StatusKey = "pos.status.noLocalMatch",
                Matches = []
            }
        };
        var cashierContext = new CashierSessionContext();
        cashierContext.SetCurrent(CreateSession(permissionCodes: [Permissions.PosTerminal.Payment.TakeCash]));
        var fallbackCalled = false;
        var viewModel = new PosTerminalViewModel(
            new LocalSellableItemIndex(),
            cart,
            Session,
            onOpenPayment: null,
            workflowService: workflow,
            tryLoginCashierFromScannerFallbackAsync: (_, _) =>
            {
                fallbackCalled = true;
                return Task.FromResult(true);
            },
            cashierSessionContext: cashierContext,
            enforcePermissionsWhenNoCashier: true);

        var processed = viewModel.ProcessScannerBarcode("1234567890123", "scanner-device", "raw");

        Assert.True(processed);
        for (var attempt = 0; attempt < 20 && string.IsNullOrEmpty(viewModel.StatusMessage); attempt++)
        {
            await Task.Delay(25);
        }

        Assert.False(fallbackCalled);
        Assert.Equal(0, workflow.ProcessScanAsyncCalls);
        Assert.Empty(viewModel.CartLines);
        Assert.False(cashierContext.RequirePermission(Permissions.PosTerminal.Sales.AddItem, out var deniedMessage));
        Assert.Equal(deniedMessage, viewModel.StatusMessage);
    }

    [Fact]
    public async Task Pos_terminal_scanner_with_cashier_without_add_permission_does_not_fallback_for_known_product()
    {
        var cart = new PosCartService();
        var priceIndex = new LocalSellableItemIndex();
        priceIndex.ReplaceAll([CreateItem()]);
        var workflow = new FakePosTerminalWorkflowService();
        var cashierContext = new CashierSessionContext();
        cashierContext.SetCurrent(CreateSession(permissionCodes: [Permissions.PosTerminal.Payment.TakeCash]));
        var fallbackCalled = false;
        var viewModel = new PosTerminalViewModel(
            priceIndex,
            cart,
            Session,
            onOpenPayment: null,
            workflowService: workflow,
            tryLoginCashierFromScannerFallbackAsync: (_, _) =>
            {
                fallbackCalled = true;
                return Task.FromResult(true);
            },
            cashierSessionContext: cashierContext,
            enforcePermissionsWhenNoCashier: true);

        var processed = viewModel.ProcessScannerBarcode("930001", "scanner-device", "raw");

        Assert.True(processed);
        for (var attempt = 0; attempt < 20 && string.IsNullOrEmpty(viewModel.StatusMessage); attempt++)
        {
            await Task.Delay(25);
        }

        Assert.False(fallbackCalled);
        Assert.Equal(0, workflow.ProcessScanAsyncCalls);
        Assert.Empty(viewModel.CartLines);
        Assert.False(cashierContext.RequirePermission(Permissions.PosTerminal.Sales.AddItem, out var deniedMessage));
        Assert.Equal(deniedMessage, viewModel.StatusMessage);
    }

    [Fact]
    public async Task Pos_terminal_scanner_with_authorized_cashier_catalog_miss_never_switches_cashier()
    {
        var cart = new PosCartService();
        var workflow = new FakePosTerminalWorkflowService
        {
            ScanResult = new PosTerminalWorkflowResult
            {
                StatusKey = "pos.status.noLocalMatch",
                Matches = []
            }
        };
        var cashierContext = new CashierSessionContext();
        cashierContext.SetCurrent(CreateSession(permissionCodes: [Permissions.PosTerminal.Sales.AddItem]));
        var fallbackCalled = false;
        var viewModel = new PosTerminalViewModel(
            new LocalSellableItemIndex(),
            cart,
            Session,
            onOpenPayment: null,
            workflowService: workflow,
            tryLoginCashierFromScannerFallbackAsync: (_, _) =>
            {
                fallbackCalled = true;
                return Task.FromResult(true);
            },
            cashierSessionContext: cashierContext,
            enforcePermissionsWhenNoCashier: true);

        Assert.True(viewModel.ProcessScannerBarcode("1234567890123", "scanner-device", "raw"));
        for (var attempt = 0; attempt < 40 && workflow.ProcessScanAsyncCalls == 0; attempt++)
        {
            await Task.Delay(25);
        }

        Assert.Equal(1, workflow.ProcessScanAsyncCalls);
        Assert.False(fallbackCalled);
        Assert.Empty(viewModel.CartLines);
    }

    [Fact]
    public async Task Pos_terminal_scanner_catalog_miss_tries_cashier_login_fallback()
    {
        var cart = new PosCartService();
        var workflow = new FakePosTerminalWorkflowService
        {
            ScanResult = new PosTerminalWorkflowResult
            {
                StatusKey = "pos.status.noLocalMatch",
                Matches = []
            }
        };
        var cashierBarcode = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new PosTerminalViewModel(
            new LocalSellableItemIndex(),
            cart,
            Session,
            onOpenPayment: null,
            workflowService: workflow,
            tryLoginCashierFromScannerFallbackAsync: (barcode, _) =>
            {
                cashierBarcode.TrySetResult(barcode);
                return Task.FromResult(true);
            });

        var processed = viewModel.ProcessScannerBarcode("1234567890123", "scanner-device", "raw");

        Assert.True(processed);
        Assert.Equal("1234567890123", await cashierBarcode.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        for (var attempt = 0; attempt < 20 && viewModel.ScanText.Length > 0; attempt++)
        {
            await Task.Delay(25);
        }

        Assert.Empty(viewModel.CartLines);
        Assert.Equal(string.Empty, viewModel.ScanText);
        Assert.False(viewModel.IsMatchesPopupOpen);
        Assert.Equal("收银员登录成功", viewModel.StatusMessage);
    }

    [Fact]
    public void Pos_terminal_scan_logs_redact_barcode_values()
    {
        using var logs = new ConsoleLogCapture();
        var controller = new PosTerminalScanController(new PosCartService());
        const string sensitiveBarcode = "SECRET-CASHIER-12345";
        var plan = controller.CreateScanner(sensitiveBarcode, "scanner-device", "raw", DateTimeOffset.Now);

        controller.LogStarted(plan, "S001");
        controller.LogInputApplied(plan, 7);
        controller.LogFinished(plan, "cashier-login", false, 0, 1, 2, 3);

        var scanLines = logs.Lines
            .Where(line => line.Contains($"traceId={plan.TraceId}", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(3, scanLines.Length);
        Assert.All(scanLines, line => Assert.Contains("barcodeInfo=length=20", line, StringComparison.Ordinal));
        Assert.All(scanLines, line => Assert.DoesNotContain(sensitiveBarcode, line, StringComparison.Ordinal));
        Assert.All(scanLines, line => Assert.DoesNotContain("barcode=", line, StringComparison.Ordinal));
    }

    private static PosSessionState Session => new("HB POS", "S001", "Main Store", "POS-01", "C001", "Alice", true, 0);

    private static CashierSessionDto CreateSession(
        string[]? permissionCodes = null,
        string[]? allowedStoreCodes = null) =>
        new(
            "C001",
            Guid.NewGuid().ToString("N"),
            "Alice",
            "S001",
            "POS-01",
            ["Cashier"],
            permissionCodes ?? [Permissions.PosTerminal.Sales.AddItem, Permissions.PosTerminal.Payment.TakeCash],
            allowedStoreCodes ?? ["S001"],
            IsSuperAdmin: false,
            IsOfflineCached: false,
            IsEmergencyOverride: false);

    private static (PosTerminalViewModel ViewModel, FakePosTerminalWorkflowService Workflow, CartLine Line)
        CreateDiscountViewModel(
            string permissionCode,
            bool isWholeOrderOperation,
            IOperationAuditLogger? operationAuditLogger = null)
    {
        var cart = new PosCartService();
        var line = cart.AddItem(CreateItem());
        var workflow = new FakePosTerminalWorkflowService();
        var cashierContext = new CashierSessionContext();
        cashierContext.SetCurrent(CreateSession(permissionCodes: [permissionCode]));
        var viewModel = new PosTerminalViewModel(
            new LocalSellableItemIndex(),
            cart,
            Session,
            onOpenPayment: null,
            workflowService: workflow,
            cashierSessionContext: cashierContext,
            enforcePermissionsWhenNoCashier: true,
            operationAuditLogger: operationAuditLogger)
        {
            SelectedCartLine = line,
            IsWholeOrderOperation = isWholeOrderOperation
        };

        return (viewModel, workflow, line);
    }

    private static SellableItemDto CreateItem() =>
        new(
            StoreCode: "S001",
            ProductCode: "SKU-1",
            ReferenceCode: null,
            DisplayName: "Tea",
            LookupCode: "930001",
            ItemNumber: "SKU-1",
            Barcode: "930001",
            RetailPrice: 5m,
            PriceSource: PriceSourceKind.StoreRetailPrice,
            PriceSourceLabel: PriceSourceKind.StoreRetailPrice.ToString(),
            QuantityFactor: 1m,
            UpdatedAt: DateTimeOffset.UtcNow,
            ProductImage: null);

    private sealed class InMemoryAppSettingsRepository : ILocalAppSettingsRepository
    {
        private readonly Dictionary<string, string> _values = [];

        public IEnumerable<string> Keys => _values.Keys;

        public IEnumerable<string> Values => _values.Values;

        public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.TryGetValue(key, out var value);
            return Task.FromResult<string?>(value);
        }

        public Task SetValueAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task DeleteValueAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class PassthroughProtector : IDeviceAuthorizationProtector
    {
        public string? Protect(string? value) => value is null ? null : $"protected:{value}";

        public string? Unprotect(string? protectedValue) =>
            protectedValue?.StartsWith("protected:", StringComparison.Ordinal) == true
                ? protectedValue["protected:".Length..]
                : null;
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class SequenceCashierLoginApiClient(params CashierLoginAttempt[] attempts) : ICashierLoginApiClient
    {
        private int _index;

        public Task<CashierLoginAttempt> LoginAsync(
            CashierBarcodeLoginRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(attempts[Math.Min(_index++, attempts.Length - 1)]);
        }
    }

    private sealed class NeverCompletingCashierLoginApiClient : ICashierLoginApiClient
    {
        public int CallCount { get; private set; }

        public Task<CashierLoginAttempt> LoginAsync(
            CashierBarcodeLoginRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return new TaskCompletionSource<CashierLoginAttempt>(
                TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }
    }

    private sealed class SequenceCashierSessionRefreshApiClient(params CashierSessionRefreshAttempt[] attempts)
        : ICashierSessionRefreshApiClient
    {
        private int _index;

        public int CallCount { get; private set; }

        public Task<CashierSessionRefreshAttempt> RefreshAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(attempts[Math.Min(_index++, attempts.Length - 1)]);
        }
    }

    private sealed class DeferredCashierSessionRefreshApiClient : ICashierSessionRefreshApiClient
    {
        private readonly TaskCompletionSource<CashierSessionRefreshAttempt> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<CashierSessionRefreshAttempt> RefreshAsync(CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            return _completion.Task.WaitAsync(cancellationToken);
        }

        public void Complete(CashierSessionRefreshAttempt attempt)
        {
            _completion.TrySetResult(attempt);
        }
    }

    private sealed class RecordingCashierSessionCacheUpdater(
        Func<CashierSessionDto?> currentSession,
        Action<CashierSessionDto>? afterUpdate = null)
        : ICashierSessionCacheUpdater
    {
        public CashierSessionDto? UpdatedSession => UpdatedSessions.LastOrDefault();

        public List<CashierSessionDto> UpdatedSessions { get; } = [];

        public CashierSessionDto? RemovedSession { get; private set; }

        public CashierSessionDto? SessionObservedDuringUpdate { get; private set; }

        public bool Removed { get; private set; }

        public Task UpdateCachedSessionAsync(
            CashierSessionDto session,
            CancellationToken cancellationToken = default)
        {
            SessionObservedDuringUpdate = currentSession();
            UpdatedSessions.Add(session);
            afterUpdate?.Invoke(session);
            return Task.CompletedTask;
        }

        public Task RemoveCachedSessionAsync(
            CashierSessionDto session,
            CancellationToken cancellationToken = default)
        {
            Removed = true;
            RemovedSession = session;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingOperationAuditLogger : IOperationAuditLogger
    {
        public List<OperationAuditEventDto> Events { get; } = [];

        public void Record(OperationAuditEventDto auditEvent)
        {
            Events.Add(auditEvent);
        }
    }

    private sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }

    private sealed class ConsoleLogCapture : IDisposable
    {
        private readonly List<string> _lines = [];

        public ConsoleLogCapture()
        {
            ConsoleLog.LineWritten += OnLineWritten;
        }

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_lines)
                {
                    return _lines.ToArray();
                }
            }
        }

        public void Dispose()
        {
            ConsoleLog.LineWritten -= OnLineWritten;
        }

        private void OnLineWritten(string line)
        {
            lock (_lines)
            {
                _lines.Add(line);
            }
        }
    }

    private sealed class FakePosTerminalWorkflowService : IPosTerminalWorkflowService
    {
        public event EventHandler<PosTerminalCatalogReloadedEventArgs>? CatalogReloaded
        {
            add { }
            remove { }
        }

        public int ChangePriceCalls { get; private set; }

        public int ManualDiscountAmountCalls { get; private set; }

        public int ManualDiscountPercentCalls { get; private set; }

        public List<string?> QuickDiscountPercentValues { get; } = [];

        public int ProcessScanAsyncCalls { get; private set; }

        public PosTerminalWorkflowResult ScanResult { get; init; } = new();

        public Task<PosTerminalWorkflowResult> ProcessScanAsync(PosSessionState session, string scanText, bool preferExactLookup, string source, string? traceId = null, CancellationToken cancellationToken = default)
        {
            ProcessScanAsyncCalls++;
            return Task.FromResult(ScanResult);
        }

        public PosTerminalWorkflowResult ProcessScan(PosSessionState session, string scanText, bool preferExactLookup, string source, string? traceId = null) => ScanResult;

        public PosTerminalWorkflowResult AddSelectedItem(PosSessionState session, SellableItemDto item, bool clearScanText, bool closeMatchesPopup, string operation) => new();

        public PosTerminalWorkflowResult AddOpenItem(PosSessionState session, string keypadBuffer) => new();

        public PosTerminalWorkflowResult RemoveLine(CartLine? line) => new();

        public PosTerminalWorkflowResult IncreaseLine(CartLine? line) => new();

        public PosTerminalWorkflowResult DecreaseLine(CartLine? line) => new();

        public PosTerminalWorkflowResult ModifySelectedLineQuantity(CartLine? selectedLine, string keypadBuffer) => new();

        public PosTerminalWorkflowResult ModifySelectedLinePrice(CartLine? selectedLine, string keypadBuffer)
        {
            ChangePriceCalls++;
            return new PosTerminalWorkflowResult();
        }

        public PosTerminalWorkflowResult ApplySelectedLineDiscountAmount(CartLine? selectedLine, string keypadBuffer, bool isWholeOrderOperation)
        {
            ManualDiscountAmountCalls++;
            return new();
        }

        public PosTerminalWorkflowResult ApplySelectedLineDiscountPercent(CartLine? selectedLine, string keypadBuffer, bool isWholeOrderOperation)
        {
            ManualDiscountPercentCalls++;
            return new();
        }

        public PosTerminalWorkflowResult ApplyQuickDiscountPercent(CartLine? selectedLine, string? value, bool isWholeOrderOperation)
        {
            QuickDiscountPercentValues.Add(value);
            return new();
        }

        public PosTerminalWorkflowResult ClearCart() => new();

        public PosTerminalWorkflowResult GuardPayment() => new() { PaymentAllowed = true };
    }

    private sealed class FakeCashPaymentWorkflowService : ICashPaymentWorkflowService
    {
        public int AddTenderCallCount { get; private set; }

        public int CompletePaymentCallCount { get; private set; }

        public bool TryParseTenderedAmount(string? amountTenderedText, out decimal tenderedAmount) => decimal.TryParse(amountTenderedText, out tenderedAmount);

        public decimal CalculateChange(string? amountTenderedText, decimal actualAmount) => 0m;

        public decimal CalculateTenderedAmount(IReadOnlyList<PaymentTender> tenders) => tenders.Sum(tender => tender.Amount);

        public decimal CalculateRemainingAmount(decimal actualAmount, IReadOnlyList<PaymentTender> tenders) => Math.Max(0m, actualAmount - CalculateTenderedAmount(tenders));

        public decimal CalculateChange(IReadOnlyList<PaymentTender> tenders, decimal actualAmount) => 0m;

        public Task<PaymentTenderAttemptResult> AddTenderAsync(PaymentMethodKind method, PosSessionState session, decimal actualAmount, IReadOnlyList<PaymentTender> currentTenders, string? amountText, string? referenceText = null, CancellationToken cancellationToken = default, PosCartSnapshot? cartSnapshot = null)
        {
            AddTenderCallCount++;
            return Task.FromResult(PaymentTenderAttemptResult.Success(new PaymentTender(method, 5m, referenceText), "payment.status.tenderAdded"));
        }

        public Task<CashPaymentWorkflowResult> CompleteAsync(PosCartService cart, PosSessionState session, string? amountTenderedText, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CashPaymentWorkflowResult> CompletePaymentAsync(PosCartService cart, PosSessionState session, IReadOnlyList<PaymentTender> tenders, decimal cashTenderedAmount, CancellationToken cancellationToken = default)
        {
            CompletePaymentCallCount++;
            var order = new LocalOrder(
                Guid.NewGuid(),
                session.StoreCode,
                session.DeviceCode,
                session.CashierId,
                session.CashierName,
                DateTimeOffset.UtcNow,
                cart.TotalAmount,
                cart.DiscountAmount,
                cart.ActualAmount,
                [],
                [],
                cashTenderedAmount,
                0m);
            return Task.FromResult(new CashPaymentWorkflowResult(order, cashTenderedAmount, 0m, 0, session));
        }

        public Task<CashPaymentWorkflowResult> RetryVoucherUploadAsync(Guid orderGuid, PosCartService cart, PosSessionState session, decimal tenderedAmount, decimal changeAmount, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
