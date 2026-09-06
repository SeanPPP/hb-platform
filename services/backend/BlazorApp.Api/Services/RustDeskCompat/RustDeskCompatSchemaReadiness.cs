using BlazorApp.Api.Data;
using BlazorApp.Api.Data.SchemaMigrations;
using SqlSugar;

namespace BlazorApp.Api.Services.RustDeskCompat;

/// <summary>兼容接口只读检查 schema；正常 HTTP 启动绝不建表。</summary>
public sealed class RustDeskCompatSchemaReadiness(SqlSugarContext context)
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Db.CurrentConnectionConfig.DbType == DbType.Sqlite)
            return context.Db.DbMaintenance.IsAnyTable("HBweb_RustDeskClientSession", false)
                && context.Db.DbMaintenance.IsAnyTable("HBweb_RustDeskManagedDevice", false);
        try
        {
            await context.Db.Ado.ExecuteCommandAsync(RustDeskClientSchemaMigrator.VerifySql, Array.Empty<SugarParameter>());
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }
}
