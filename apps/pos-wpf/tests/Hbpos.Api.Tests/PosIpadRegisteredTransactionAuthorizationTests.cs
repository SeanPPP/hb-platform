using System.Reflection;
using Hbpos.Api.Auth;
using Hbpos.Api.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace Hbpos.Api.Tests;

public sealed class PosIpadRegisteredTransactionAuthorizationTests
{
    [Fact]
    public void Registered_ipad_transaction_actions_keep_cashier_authorization_policies()
    {
        var protectedActions = new (Type Controller, string Action, string Policy)[]
        {
            (typeof(InstallmentsController), nameof(InstallmentsController.Create), CashierAuthorizationPolicies.InstallmentCreate),
            (typeof(InstallmentsController), nameof(InstallmentsController.AppendPayment), CashierAuthorizationPolicies.InstallmentPayment),
            (typeof(InstallmentsController), nameof(InstallmentsController.ConfirmPickup), CashierAuthorizationPolicies.InstallmentPickup),
            (typeof(InstallmentsController), nameof(InstallmentsController.Cancel), CashierAuthorizationPolicies.InstallmentCancel),
            (typeof(InstallmentsController), nameof(InstallmentsController.Void), CashierAuthorizationPolicies.InstallmentCancel),
            (typeof(LinklyController), nameof(LinklyController.StartCloudBackendTransaction), CashierAuthorizationPolicies.TakeCard),
            (typeof(OrdersController), nameof(OrdersController.CreateReturns), CashierAuthorizationPolicies.Returns),
            (typeof(SquareController), nameof(SquareController.CreateCheckout), CashierAuthorizationPolicies.TakeCard),
            (typeof(SquareController), nameof(SquareController.CreateRefund), CashierAuthorizationPolicies.Returns),
            (typeof(VouchersController), nameof(VouchersController.Lock), CashierAuthorizationPolicies.Voucher),
            (typeof(VouchersController), nameof(VouchersController.IssueRefund), CashierAuthorizationPolicies.VoucherRefund),
            (typeof(VouchersController), nameof(VouchersController.Issue), CashierAuthorizationPolicies.VoucherRefund)
        };

        foreach (var (controller, action, policy) in protectedActions)
        {
            var method = controller.GetMethod(action)!;
            var authorize = method.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Single();

            Assert.Equal(policy, authorize.Policy);
            Assert.DoesNotContain(
                method.GetCustomAttributes(inherit: true),
                attribute => attribute.GetType().Name == "PosIpadNewTransactionAttribute");
        }
    }
}
