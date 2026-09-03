using BlazorApp.Api.Interfaces.React;
using BlazorApp.Shared.DTOs;

namespace BlazorApp.Api.Services.React;

public sealed class ProductMaintenanceHqSyncOutboxExecutor : IProductHqSyncOutboxExecutor
{
    private readonly IProductMaintenanceHqProjectionWriter _writer;

    public ProductMaintenanceHqSyncOutboxExecutor(IProductMaintenanceHqProjectionWriter writer)
    {
        _writer = writer;
    }

    public Task<ProductHqSyncOutboxExecutionResult> ExecuteAsync(
        ProductHqSyncOutboxWorkItemDto workItem,
        CancellationToken cancellationToken = default
    ) => _writer.ApplyAsync(workItem, cancellationToken);
}
