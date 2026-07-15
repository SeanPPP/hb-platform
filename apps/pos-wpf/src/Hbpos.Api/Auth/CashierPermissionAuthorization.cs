using System.Security.Claims;
using BlazorApp.Shared.Constants;
using BlazorApp.Shared.Security;
using Hbpos.Api.Services;
using Hbpos.Contracts.Cashiers;
using Hbpos.Contracts.Devices;
using Microsoft.AspNetCore.Authorization;

namespace Hbpos.Api.Auth;

public static class CashierAuthorizationPolicies
{
    public const string OrderSync = "Cashier.OrderSync";
    public const string History = "Cashier.History";
    public const string Returns = "Cashier.Returns";
    public const string Voucher = "Cashier.Voucher";
    public const string VoucherRefund = "Cashier.VoucherRefund";
    public const string InstallmentView = "Cashier.InstallmentView";
    public const string InstallmentCreate = "Cashier.InstallmentCreate";
    public const string InstallmentPayment = "Cashier.InstallmentPayment";
    public const string InstallmentPickup = "Cashier.InstallmentPickup";
    public const string InstallmentCancel = "Cashier.InstallmentCancel";
    public const string TakeCard = "Cashier.TakeCard";
    public const string PaymentSettings = "Cashier.PaymentSettings";
    public const string SpecialProductsView = "Cashier.SpecialProductsView";
    public const string SpecialProductsManage = "Cashier.SpecialProductsManage";
    public const string DeviceRegistration = "Cashier.DeviceRegistration";

    public static void AddPolicies(AuthorizationOptions options)
    {
        Add(options, OrderSync, Permissions.PosTerminal.Payment.Confirm, Permissions.PosTerminal.System.Sync);
        Add(options, History, Permissions.PosTerminal.History.View);
        Add(options, Returns, Permissions.PosTerminal.Returns.Confirm);
        Add(options, Voucher, Permissions.PosTerminal.Payment.TakeVoucher);
        Add(options, VoucherRefund,
            Permissions.PosTerminal.Returns.Confirm,
            Permissions.PosTerminal.Installments.Cancel);
        Add(options, InstallmentView, Permissions.PosTerminal.Installments.View);
        Add(options, InstallmentCreate, Permissions.PosTerminal.Installments.Create);
        Add(options, InstallmentPayment, Permissions.PosTerminal.Installments.AddRepayment);
        Add(options, InstallmentPickup, Permissions.PosTerminal.Installments.ConfirmPickup);
        Add(options, InstallmentCancel, Permissions.PosTerminal.Installments.Cancel);
        AddAll(options, TakeCard,
            Permissions.PosTerminal.Payment.TakeCard,
            Permissions.PosTerminal.Payment.Confirm);
        Add(options, PaymentSettings, Permissions.PosTerminal.Settings.PaymentTerminal);
        Add(options, SpecialProductsView, Permissions.PosTerminal.SpecialProducts.View);
        Add(options, SpecialProductsManage, Permissions.PosTerminal.SpecialProducts.Manage);
        Add(options, DeviceRegistration, Permissions.PosTerminal.Settings.DeviceRegistration);
    }

    private static void Add(AuthorizationOptions options, string name, params string[] permissions)
    {
        options.AddPolicy(name, policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.AddRequirements(new CashierPermissionRequirement(permissions));
        });
    }

    private static void AddAll(AuthorizationOptions options, string name, params string[] permissions)
    {
        options.AddPolicy(name, policy =>
        {
            policy.RequireAuthenticatedUser();
            // 关键逻辑：每个权限作为独立 requirement，确保刷卡入口同时具备收卡与确认权限。
            foreach (var permission in permissions)
            {
                policy.AddRequirements(new CashierPermissionRequirement([permission]));
            }
        });
    }
}

public sealed record CashierPermissionRequirement(string[] PermissionCodes) : IAuthorizationRequirement;

public static class CashierAuthorizationContext
{
    public const string CashierIdItemKey = "Hbpos.AuthorizedCashierId";
}

public sealed class CashierPermissionAuthorizationHandler(
    IHttpContextAccessor httpContextAccessor,
    ICashierAuthorizationTicketService ticketService,
    IConfiguration? configuration = null,
    ILogger<CashierPermissionAuthorizationHandler>? logger = null) : AuthorizationHandler<CashierPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CashierPermissionRequirement requirement)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return;
        }

        var deviceStoreCode = context.User.FindFirstValue(DeviceAuthConstants.StoreCodeClaim);
        var deviceCode = context.User.FindFirstValue(DeviceAuthConstants.DeviceCodeClaim);
        var token = httpContext.Request.Headers[CashierAuthorizationConstants.HeaderName].ToString();
        var ticket = ticketService.Validate(token);
        if (ticket is not null &&
            string.Equals(ticket.StoreCode, deviceStoreCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ticket.DeviceCode, deviceCode, StringComparison.OrdinalIgnoreCase))
        {
            // 只有真正校验收银员票据时才解析数据库服务，普通设备认证端点不会提前连接数据库。
            var cashierService = httpContext!.RequestServices.GetRequiredService<ICashierService>();
            if (await cashierService.HasAnyPermissionAsync(
                    ticket.UserGuid,
                    ticket.StoreCode,
                    requirement.PermissionCodes,
                    httpContext.RequestAborted))
            {
                // 关键逻辑：敏感业务字段必须使用已验票身份，不能继续信任客户端快照中的 CashierId。
                httpContext.Items[CashierAuthorizationContext.CashierIdItemKey] = ticket.CashierId;
                context.Succeed(requirement);
                return;
            }
        }

        if (EmergencyLoginTokenCodec.HasSupportedPrefix(token))
        {
            // 仅紧急二维码才解析摘要数据库服务，缺失票据不会让普通设备请求提前连接数据库。
            var emergencyGrantService = httpContext.RequestServices
                .GetRequiredService<IEmergencyGrantAuthorizationService>();
            var emergency = await emergencyGrantService.ValidateAsync(
                token,
                deviceStoreCode ?? string.Empty,
                httpContext.RequestAborted);
            if (emergency is not null)
            {
                httpContext.Items[CashierAuthorizationContext.CashierIdItemKey] =
                    $"EMERGENCY:{emergency.GrantId:N}";
                context.Succeed(requirement);
                return;
            }
        }

        if (string.Equals(
                configuration?["CashierAuthorization:Mode"],
                "Audit",
                StringComparison.OrdinalIgnoreCase))
        {
            // Audit 阶段只记录缺失授权，不阻断已通过设备认证的旧客户端；Enforce 是默认安全模式。
            logger?.LogWarning(
                "Cashier authorization audit bypass store={StoreCode} device={DeviceCode} permissions={Permissions}",
                deviceStoreCode,
                deviceCode,
                string.Join(",", requirement.PermissionCodes));
            context.Succeed(requirement);
        }
    }
}
