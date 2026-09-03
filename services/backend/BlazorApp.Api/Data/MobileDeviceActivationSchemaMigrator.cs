using BlazorApp.Shared.Models.POSM;

namespace BlazorApp.Api.Data;

public sealed class MobileDeviceActivationSchemaMigrator(POSMSqlSugarContext context)
{
    public static IReadOnlyList<string> SqlScriptsForTests { get; } =
        [MobileDeviceActivationSchema.EnsureSql];

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await context.Db.Ado.ExecuteCommandAsync(MobileDeviceActivationSchema.EnsureSql);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
