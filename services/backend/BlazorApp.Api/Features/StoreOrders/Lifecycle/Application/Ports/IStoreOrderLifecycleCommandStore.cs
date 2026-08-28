namespace BlazorApp.Api.Features.StoreOrders.Lifecycle.Application.Ports;

internal readonly record struct StoreOrderLifecycleTransactionResult(
    bool IsSuccess,
    Exception? ErrorException
);

internal interface IStoreOrderLifecycleCommandStore
{
    Task<int> CompareExchangeStatusAsync(
        string orderGuid,
        int? expectedStatus,
        int targetStatus,
        DateTime updatedAt,
        string updatedBy
    );

    Task<int> CompareExchangeStatusGroupAsync(
        IReadOnlyList<string> orderGuids,
        int? expectedStatus,
        int targetStatus,
        DateTime updatedAt,
        string updatedBy
    );

    Task<StoreOrderLifecycleTransactionResult> ExecuteInTransactionAsync(
        Func<Task> command
    );
}
