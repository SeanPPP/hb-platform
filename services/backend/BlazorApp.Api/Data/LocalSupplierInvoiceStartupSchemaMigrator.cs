using Microsoft.Extensions.Logging;
using SqlSugar;

namespace BlazorApp.Api.Data
{
    public static class LocalSupplierInvoiceStartupSchemaMigrator
    {
        public static async Task EnsureAsync(ISqlSugarClient db, ILogger logger)
        {
            if (db.CurrentConnectionConfig.DbType != DbType.SqlServer)
            {
                return;
            }

            const string sql =
                @"
IF OBJECT_ID('StoreLocalSupplierInvoiceDetails', 'U') IS NOT NULL
   AND COL_LENGTH('StoreLocalSupplierInvoiceDetails', 'AdditionalBarcodesJson') IS NULL
BEGIN
    ALTER TABLE [StoreLocalSupplierInvoiceDetails]
    ADD [AdditionalBarcodesJson] nvarchar(max) NULL;
END;";

            // 关键位置：多条码字段是新版本读取明细的硬依赖，启动时补齐可避免旧库部署后整页不可用。
            await db.Ado.ExecuteCommandAsync(sql);
            logger.LogInformation("分店进货单明细多条码字段检查完成");
        }
    }
}
