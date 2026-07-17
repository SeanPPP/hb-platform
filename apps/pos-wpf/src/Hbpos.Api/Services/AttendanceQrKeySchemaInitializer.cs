using Hbpos.Api.Data;

namespace Hbpos.Api.Services;

public interface IAttendanceQrKeySchemaInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

public interface IAttendanceQrKeySchemaSqlExecutor
{
    Task ExecuteAsync(string sql, CancellationToken cancellationToken = default);
}

public sealed class SqlSugarAttendanceQrKeySchemaInitializer(
    IAttendanceQrKeySchemaSqlExecutor sqlExecutor) : IAttendanceQrKeySchemaInitializer
{
    // 关键逻辑：先保留每个设备和硬件最新的活动密钥，再创建过滤唯一索引，兼容已有重复数据。
    internal const string EnsureAttendanceQrKeySchemaSql = """
        SET XACT_ABORT ON;

        BEGIN TRY
            BEGIN TRANSACTION;

            DECLARE @AttendanceQrSchemaLockResult int;
            EXEC @AttendanceQrSchemaLockResult = sys.sp_getapplock
                @Resource = N'AttendancePosQrKey_Schema_Initialization',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 30000;

            IF @AttendanceQrSchemaLockResult < 0
            BEGIN
                THROW 51010, N'无法获取考勤二维码结构初始化锁。', 1;
            END;

        IF OBJECT_ID(N'[dbo].[AttendancePosQrKey]', N'U') IS NULL
        BEGIN
            CREATE TABLE [dbo].[AttendancePosQrKey] (
                [Kid] nvarchar(64) NOT NULL CONSTRAINT [PK_AttendancePosQrKey] PRIMARY KEY,
                [Algorithm] nvarchar(20) NOT NULL,
                [ProtectedKey] nvarchar(max) NOT NULL,
                [StoreCode] nvarchar(50) NOT NULL,
                [DeviceCode] nvarchar(50) NOT NULL,
                [HardwareId] nvarchar(100) NOT NULL,
                [Status] nvarchar(20) NOT NULL,
                [RegisteredAtUtc] datetime2 NOT NULL,
                [RevokedAtUtc] datetime2 NULL
            );
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [name] = N'UX_AttendancePosQrKey_ActiveDevice'
              AND [object_id] = OBJECT_ID(N'[dbo].[AttendancePosQrKey]')
        )
        BEGIN
            ;WITH [ActiveDeviceRows] AS (
                SELECT
                    [Kid],
                    ROW_NUMBER() OVER (
                        PARTITION BY [DeviceCode]
                        ORDER BY [RegisteredAtUtc] DESC, [Kid] DESC
                    ) AS [RowNumber]
                FROM [dbo].[AttendancePosQrKey] WITH (TABLOCKX, HOLDLOCK)
                WHERE [Status] = N'Active'
            )
            UPDATE [dbo].[AttendancePosQrKey]
            SET [Status] = N'Revoked', [RevokedAtUtc] = SYSUTCDATETIME()
            WHERE [Kid] IN (
                SELECT [Kid] FROM [ActiveDeviceRows] WHERE [RowNumber] > 1
            );

            CREATE UNIQUE INDEX [UX_AttendancePosQrKey_ActiveDevice]
            ON [dbo].[AttendancePosQrKey]([DeviceCode])
            WHERE [Status] = N'Active';
        END;

        IF NOT EXISTS (
            SELECT 1 FROM sys.indexes
            WHERE [name] = N'UX_AttendancePosQrKey_ActiveHardware'
              AND [object_id] = OBJECT_ID(N'[dbo].[AttendancePosQrKey]')
        )
        BEGIN
            ;WITH [ActiveHardwareRows] AS (
                SELECT
                    [Kid],
                    ROW_NUMBER() OVER (
                        PARTITION BY [HardwareId]
                        ORDER BY [RegisteredAtUtc] DESC, [Kid] DESC
                    ) AS [RowNumber]
                FROM [dbo].[AttendancePosQrKey] WITH (TABLOCKX, HOLDLOCK)
                WHERE [Status] = N'Active'
            )
            UPDATE [dbo].[AttendancePosQrKey]
            SET [Status] = N'Revoked', [RevokedAtUtc] = SYSUTCDATETIME()
            WHERE [Kid] IN (
                SELECT [Kid] FROM [ActiveHardwareRows] WHERE [RowNumber] > 1
            );

            CREATE UNIQUE INDEX [UX_AttendancePosQrKey_ActiveHardware]
            ON [dbo].[AttendancePosQrKey]([HardwareId])
            WHERE [Status] = N'Active';
        END;

            COMMIT TRANSACTION;
        END TRY
        BEGIN CATCH
            IF XACT_STATE() <> 0
            BEGIN
                ROLLBACK TRANSACTION;
            END;

            THROW;
        END CATCH;
        """;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return sqlExecutor.ExecuteAsync(EnsureAttendanceQrKeySchemaSql, cancellationToken);
    }
}

public sealed class SqlSugarAttendanceQrKeySchemaSqlExecutor(
    HbposSqlSugarContext dbContext) : IAttendanceQrKeySchemaSqlExecutor
{
    public Task ExecuteAsync(string sql, CancellationToken cancellationToken = default)
    {
        return dbContext.MainDb.Ado.ExecuteCommandAsync(sql);
    }
}
