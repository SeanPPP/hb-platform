using Hbpos.Api.Data;

namespace Hbpos.Api.Services;

public interface IOrderSyncSchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public interface IOrderSyncSchemaSqlExecutor
{
    Task ExecuteAsync(string sql, CancellationToken cancellationToken = default);
}

public sealed class SqlSugarOrderSyncSchemaInitializer(
    IOrderSyncSchemaSqlExecutor sqlExecutor) : IOrderSyncSchemaInitializer
{
    // Square/Linkly 退款引用会同时保存退款号与原交易号，长度可能超过旧库的 100 字符限制。
    internal const string EnsurePaymentReferenceLengthSql = """
        SET LOCK_TIMEOUT 15000;

        BEGIN TRY
            DECLARE @ReferenceType sysname;
            DECLARE @ReferenceMaxLength smallint;

            SELECT
                @ReferenceType = types.[name],
                @ReferenceMaxLength = columns.[max_length]
            FROM sys.columns AS columns
            INNER JOIN sys.types AS types
                ON columns.[user_type_id] = types.[user_type_id]
            WHERE columns.[object_id] = OBJECT_ID(N'[dbo].[payment_detail]', N'U')
              AND columns.[name] = N'Reference';

            -- 缺表、缺列或无法读取列元数据时停止启动，避免把未兼容的库误判为可同步。
            IF @ReferenceType IS NULL OR @ReferenceMaxLength IS NULL
            BEGIN
                THROW 51000, 'dbo.payment_detail.Reference does not exist.', 1;
            END;

            -- 仅支持保持原类型并扩宽 varchar/nvarchar；其他类型需要人工迁移。
            IF @ReferenceType NOT IN (N'varchar', N'nvarchar')
            BEGIN
                THROW 51001, 'dbo.payment_detail.Reference must be varchar or nvarchar.', 1;
            END;

            -- sys.columns.max_length 使用字节数；-1 表示 MAX，已有 MAX 或更宽列绝不收窄。
            IF @ReferenceMaxLength <> -1
            BEGIN
                IF @ReferenceType = N'varchar' AND @ReferenceMaxLength < 2000
                BEGIN
                    ALTER TABLE [dbo].[payment_detail]
                        ALTER COLUMN [Reference] VARCHAR(2000) NULL;
                END;
                ELSE IF @ReferenceType = N'nvarchar' AND @ReferenceMaxLength < 4000
                BEGIN
                    ALTER TABLE [dbo].[payment_detail]
                        ALTER COLUMN [Reference] NVARCHAR(2000) NULL;
                END;
            END;

            SET LOCK_TIMEOUT -1;
        END TRY
        BEGIN CATCH
            SET LOCK_TIMEOUT -1;
            THROW;
        END CATCH;

        -- 退货订单详情按 OrderGuid 查询；缺少索引时会扫描百万级支付和银行流水表。
        BEGIN TRY
            IF OBJECT_ID(N'[dbo].[payment_detail]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.payment_detail', N'OrderGuid') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[dbo].[payment_detail]', N'U')
                     AND [name] = N'IX_payment_detail_OrderGuid')
            BEGIN
                CREATE NONCLUSTERED INDEX [IX_payment_detail_OrderGuid]
                    ON [dbo].[payment_detail] ([OrderGuid]);
            END;
        END TRY
        BEGIN CATCH
            -- 多实例同时启动时，只忽略另一实例已成功创建同名索引的竞争。
            IF ERROR_NUMBER() <> 1913
               OR NOT EXISTS (
                   SELECT 1 FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[dbo].[payment_detail]', N'U')
                     AND [name] = N'IX_payment_detail_OrderGuid')
                THROW;
        END CATCH;

        BEGIN TRY
            IF OBJECT_ID(N'[dbo].[BankTransaction]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.BankTransaction', N'OrderGuid') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[dbo].[BankTransaction]', N'U')
                     AND [name] = N'IX_BankTransaction_OrderGuid')
            BEGIN
                CREATE NONCLUSTERED INDEX [IX_BankTransaction_OrderGuid]
                    ON [dbo].[BankTransaction] ([OrderGuid]);
            END;
        END TRY
        BEGIN CATCH
            IF ERROR_NUMBER() <> 1913
               OR NOT EXISTS (
                   SELECT 1 FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[dbo].[BankTransaction]', N'U')
                     AND [name] = N'IX_BankTransaction_OrderGuid')
                THROW;
        END CATCH;

        BEGIN TRY
            IF OBJECT_ID(N'[dbo].[sales_return_record]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.sales_return_record', N'OriginalOrderGuid') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[dbo].[sales_return_record]', N'U')
                     AND [name] = N'IX_sales_return_record_OriginalOrderGuid')
            BEGIN
                CREATE NONCLUSTERED INDEX [IX_sales_return_record_OriginalOrderGuid]
                    ON [dbo].[sales_return_record] ([OriginalOrderGuid]);
            END;
        END TRY
        BEGIN CATCH
            IF ERROR_NUMBER() <> 1913
               OR NOT EXISTS (
                   SELECT 1 FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[dbo].[sales_return_record]', N'U')
                     AND [name] = N'IX_sales_return_record_OriginalOrderGuid')
                THROW;
        END CATCH;

        BEGIN TRY
            IF OBJECT_ID(N'[dbo].[sales_return_record]', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.sales_return_record', N'ReturnOrderGuid') IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[dbo].[sales_return_record]', N'U')
                     AND [name] = N'IX_sales_return_record_ReturnOrderGuid')
            BEGIN
                CREATE NONCLUSTERED INDEX [IX_sales_return_record_ReturnOrderGuid]
                    ON [dbo].[sales_return_record] ([ReturnOrderGuid]);
            END;
        END TRY
        BEGIN CATCH
            IF ERROR_NUMBER() <> 1913
               OR NOT EXISTS (
                   SELECT 1 FROM sys.indexes
                   WHERE [object_id] = OBJECT_ID(N'[dbo].[sales_return_record]', N'U')
                     AND [name] = N'IX_sales_return_record_ReturnOrderGuid')
                THROW;
        END CATCH;
        """;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return sqlExecutor.ExecuteAsync(EnsurePaymentReferenceLengthSql, cancellationToken);
    }
}

public sealed class SqlSugarOrderSyncSchemaSqlExecutor(
    HbposSqlSugarContext dbContext) : IOrderSyncSchemaSqlExecutor
{
    public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        return dbContext.PosmDb.Ado.ExecuteCommandAsync(sql);
    }
}
