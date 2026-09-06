using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Interfaces.React;

public interface IProductHqSyncOutboxExecutor
{
    Task<ProductHqSyncOutboxExecutionResult> ExecuteAsync(
        ProductHqSyncOutboxWorkItemDto workItem,
        CancellationToken cancellationToken = default
    );
}
