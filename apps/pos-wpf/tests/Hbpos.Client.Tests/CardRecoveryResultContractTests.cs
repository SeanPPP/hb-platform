using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class CardRecoveryResultContractTests
{
    [Fact]
    public void Recovery_queue_item_carries_square_payment_status_without_shifting_existing_optional_fields()
    {
        var attemptGuid = Guid.NewGuid();
        var operationGuid = Guid.NewGuid();
        var item = new CardRecoveryQueueItem(
            CardProcessorKind.Square,
            attemptGuid,
            "Refund",
            12.34m,
            "S001",
            "POS-01",
            "C001",
            "Sandbox",
            "PaymentVerified",
            DateTimeOffset.Parse("2026-08-23T00:00:00Z"),
            DateTimeOffset.Parse("2026-08-23T00:01:00Z"),
            PaymentId: "payment-123",
            OperationGuid: operationGuid,
            PaymentStatus: "COMPLETED");

        Assert.Equal("payment-123", item.PaymentId);
        Assert.Equal(operationGuid, item.OperationGuid);
        Assert.Equal("COMPLETED", item.PaymentStatus);
    }

    [Fact]
    public void Supervisor_resolution_results_distinguish_persisted_decision_from_completed_recovery()
    {
        var payment = new CardPaymentSupervisorResolutionResult(
            false,
            "pending",
            LockRetained: true,
            ResolutionPersisted: true);
        var refund = new CardRefundSupervisorResolutionResult(
            false,
            "pending",
            LockRetained: true,
            ResolutionPersisted: true);
        var unified = new CardRecoveryResolutionResult(
            false,
            "pending",
            LockRetained: true,
            ResolutionPersisted: true);

        Assert.False(payment.Succeeded);
        Assert.True(payment.ResolutionPersisted);
        Assert.False(payment.ResolutionApplied);
        Assert.False(refund.Succeeded);
        Assert.True(refund.ResolutionPersisted);
        Assert.False(refund.ResolutionApplied);
        Assert.False(unified.Succeeded);
        Assert.True(unified.ResolutionPersisted);
        Assert.False(unified.ResolutionApplied);
    }
}
