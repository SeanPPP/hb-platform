using Hbpos.Api.Services;

namespace Hbpos.Api.Tests;

internal sealed class TestNoOpAttendanceQrKeySchemaInitializer : IAttendanceQrKeySchemaInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
