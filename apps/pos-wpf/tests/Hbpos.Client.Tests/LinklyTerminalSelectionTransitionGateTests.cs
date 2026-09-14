using Hbpos.Client.Wpf.Services;

namespace Hbpos.Client.Tests;

public sealed class LinklyTerminalSelectionTransitionGateTests
{
    [Fact]
    public async Task Assignment_is_rejected_while_financial_operation_is_running()
    {
        using var gate = new LinklyTerminalSelectionTransitionGate();
        await using var financialLease = await gate.EnterFinancialOperationAsync();

        var assignmentLease = await gate.TryEnterAssignmentAsync();

        Assert.Null(assignmentLease);
    }

    [Fact]
    public async Task Financial_operation_waits_when_it_begins_after_assignment_check()
    {
        using var gate = new LinklyTerminalSelectionTransitionGate();
        var assignmentLease = await gate.TryEnterAssignmentAsync();
        Assert.NotNull(assignmentLease);

        var financialLeaseTask = gate.EnterFinancialOperationAsync().AsTask();
        Assert.False(financialLeaseTask.IsCompleted);

        await assignmentLease!.DisposeAsync();
        await using var financialLease = await financialLeaseTask;
    }
}
