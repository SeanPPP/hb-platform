using Hbpos.Api.Services;

namespace Hbpos.Api.Tests;

internal sealed class TestNoOpOperationAuditSchemaInitializer : IOperationAuditSchemaInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
