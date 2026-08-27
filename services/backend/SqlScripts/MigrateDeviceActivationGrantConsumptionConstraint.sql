-- 收紧 POSM_DeviceActivationGrant 消费状态约束，修复 SQL Server CHECK 接受 UNKNOWN 的问题。
-- 本脚本仅供受控手工迁移使用，不接入应用启动流程；执行前必须确认连接到目标 POSM 数据库。

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @DeviceActivationMigrationLockResult int;
    EXEC @DeviceActivationMigrationLockResult = sys.sp_getapplock
        @Resource = N'HBPOS:Schema:DeviceActivationGrant',
        @LockMode = N'Exclusive',
        @LockOwner = N'Transaction',
        @LockTimeout = 30000;
    IF @DeviceActivationMigrationLockResult < 0
        THROW 51020, 'Could not acquire device activation schema migration lock.', 1;

    IF OBJECT_ID(N'[dbo].[POSM_DeviceActivationGrant]', N'U') IS NULL
        THROW 51021, 'Device activation grant table does not exist.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.check_constraints AS checkInfo
        WHERE checkInfo.[parent_object_id] = OBJECT_ID(N'[dbo].[POSM_DeviceActivationGrant]')
          AND checkInfo.[name] = N'CK_POSM_DeviceActivationGrant_Consumption'
    )
        THROW 51021, 'Device activation consumption constraint does not exist.', 1;

    -- CASE 只把明确满足合法状态的行计为 0；FALSE 和 UNKNOWN 都进入 ELSE，避免三值逻辑漏检。
    DECLARE @DeviceActivationInvalidGrantIds TABLE
    (
        [GrantId] uniqueidentifier NOT NULL PRIMARY KEY
    );

    INSERT INTO @DeviceActivationInvalidGrantIds ([GrantId])
    SELECT grantInfo.[GrantId]
    FROM [dbo].[POSM_DeviceActivationGrant] AS grantInfo WITH (UPDLOCK, HOLDLOCK)
    WHERE CASE WHEN
        (
            (grantInfo.[ConsumedAtUtc] IS NULL
                AND grantInfo.[ConsumedHardwareId] IS NULL
                AND grantInfo.[ConsumedDeviceCode] IS NULL
                AND grantInfo.[ConsumedDeviceRegistrationId] IS NULL
                AND grantInfo.[ConsumedAuthorizationHash] IS NULL
                AND grantInfo.[ConsumedDeviceSystem] IS NULL
                AND grantInfo.[ConsumptionKind] IS NULL
                AND grantInfo.[PreviousStoreCode] IS NULL
                AND grantInfo.[PreviousDeviceCode] IS NULL)
            OR
            (grantInfo.[ConsumedAtUtc] IS NOT NULL
                AND grantInfo.[ConsumedHardwareId] IS NOT NULL
                AND grantInfo.[ConsumedDeviceCode] IS NOT NULL
                AND grantInfo.[ConsumedDeviceRegistrationId] IS NOT NULL
                AND grantInfo.[ConsumedAuthorizationHash] IS NOT NULL
                AND grantInfo.[ConsumedDeviceSystem] IS NOT NULL
                AND grantInfo.[ConsumptionKind] IS NOT NULL
                AND grantInfo.[ConsumptionKind] IN ('Initial', 'Rebind')
                AND ((grantInfo.[ConsumptionKind] = 'Initial'
                        AND grantInfo.[PreviousStoreCode] IS NULL
                        AND grantInfo.[PreviousDeviceCode] IS NULL)
                    OR (grantInfo.[ConsumptionKind] = 'Rebind'
                        AND grantInfo.[PreviousStoreCode] IS NOT NULL
                        AND grantInfo.[PreviousDeviceCode] IS NOT NULL)))
        ) THEN 0 ELSE 1 END = 1;

    DECLARE @DeviceActivationInvalidGrantCount int =
        (SELECT COUNT(1) FROM @DeviceActivationInvalidGrantIds);
    IF @DeviceActivationInvalidGrantCount > 0
    BEGIN
        -- 只输出数量和 GrantId，不读取或暴露 secret、硬件标识及授权信息。
        SELECT @DeviceActivationInvalidGrantCount AS [InvalidGrantCount];
        SELECT invalidGrant.[GrantId]
        FROM @DeviceActivationInvalidGrantIds AS invalidGrant
        ORDER BY invalidGrant.[GrantId];

        DECLARE @DeviceActivationInvalidGrantMessage nvarchar(2048) = CONCAT(
            N'Device activation consumption migration found ',
            @DeviceActivationInvalidGrantCount,
            N' invalid grant row(s).');
        THROW 51022, @DeviceActivationInvalidGrantMessage, 1;
    END;

    ALTER TABLE [dbo].[POSM_DeviceActivationGrant]
        DROP CONSTRAINT [CK_POSM_DeviceActivationGrant_Consumption];

    ALTER TABLE [dbo].[POSM_DeviceActivationGrant] WITH CHECK ADD CONSTRAINT [CK_POSM_DeviceActivationGrant_Consumption] CHECK
    (
        ([ConsumedAtUtc] IS NULL
            AND [ConsumedHardwareId] IS NULL
            AND [ConsumedDeviceCode] IS NULL
            AND [ConsumedDeviceRegistrationId] IS NULL
            AND [ConsumedAuthorizationHash] IS NULL
            AND [ConsumedDeviceSystem] IS NULL
            AND [ConsumptionKind] IS NULL
            AND [PreviousStoreCode] IS NULL
            AND [PreviousDeviceCode] IS NULL)
        OR
        ([ConsumedAtUtc] IS NOT NULL
            AND [ConsumedHardwareId] IS NOT NULL
            AND [ConsumedDeviceCode] IS NOT NULL
            AND [ConsumedDeviceRegistrationId] IS NOT NULL
            AND [ConsumedAuthorizationHash] IS NOT NULL
            AND [ConsumedDeviceSystem] IS NOT NULL
            AND [ConsumptionKind] IS NOT NULL
            AND [ConsumptionKind] IN ('Initial', 'Rebind')
            AND (([ConsumptionKind] = 'Initial'
                    AND [PreviousStoreCode] IS NULL
                    AND [PreviousDeviceCode] IS NULL)
                OR ([ConsumptionKind] = 'Rebind'
                    AND [PreviousStoreCode] IS NOT NULL
                    AND [PreviousDeviceCode] IS NOT NULL)))
    );
    ALTER TABLE [dbo].[POSM_DeviceActivationGrant] WITH CHECK
        CHECK CONSTRAINT [CK_POSM_DeviceActivationGrant_Consumption];

    -- 在保存点内复现 case 14：消费字段齐全但 ConsumptionKind=NULL，必须由目标约束以 547 拒绝。
    DECLARE @DeviceActivationConstraintProbeRejected bit = 0;
    DECLARE @DeviceActivationConstraintProbeUnexpectedError int = NULL;
    DECLARE @DeviceActivationConstraintProbeCaughtMessage nvarchar(4000) = NULL;
    DECLARE @DeviceActivationConstraintProbeGrantId uniqueidentifier = NEWID();
    DECLARE @DeviceActivationConstraintProbeAt datetime2(7) = SYSUTCDATETIME();
    DECLARE @DeviceActivationConstraintNamePattern nvarchar(512) =
        N'CK_POSM_DeviceActivationGrant_Consumption';
    SET @DeviceActivationConstraintNamePattern =
        REPLACE(@DeviceActivationConstraintNamePattern, N'[', N'[[]');
    SET @DeviceActivationConstraintNamePattern =
        REPLACE(@DeviceActivationConstraintNamePattern, N'%', N'[%]');
    SET @DeviceActivationConstraintNamePattern =
        REPLACE(@DeviceActivationConstraintNamePattern, N'_', N'[_]');

    SET XACT_ABORT OFF;
    SAVE TRANSACTION DeviceActivationConstraintProbe;
    BEGIN TRY
        INSERT INTO [dbo].[POSM_DeviceActivationGrant]
        (
            [GrantId],
            [SecretHash],
            [StoreCode],
            [DeviceSystem],
            [CreatedAtUtc],
            [CreatedBy],
            [Reason],
            [ExpiresAtUtc],
            [ConsumedAtUtc],
            [ConsumedHardwareId],
            [ConsumedDeviceCode],
            [ConsumedDeviceRegistrationId],
            [ConsumedAuthorizationHash],
            [ConsumedDeviceSystem],
            [ConsumptionKind],
            [PreviousStoreCode],
            [PreviousDeviceCode]
        )
        VALUES
        (
            @DeviceActivationConstraintProbeGrantId,
            HASHBYTES('SHA2_256', CONVERT(varchar(36), @DeviceActivationConstraintProbeGrantId)),
            '__SCHEMA_PROBE__',
            'Windows',
            @DeviceActivationConstraintProbeAt,
            N'HBPOS_SCHEMA_PROBE',
            N'Consumption constraint migration validation',
            DATEADD(minute, 10, @DeviceActivationConstraintProbeAt),
            @DeviceActivationConstraintProbeAt,
            'HW-SCHEMA-PROBE',
            'DEVICE-SCHEMA-PROBE',
            -2147480014,
            HASHBYTES('SHA2_256', CONVERT(varchar(36), NEWID())),
            'Windows',
            NULL,
            NULL,
            NULL
        );
    END TRY
    BEGIN CATCH
        SET @DeviceActivationConstraintProbeUnexpectedError = ERROR_NUMBER();
        SET @DeviceActivationConstraintProbeCaughtMessage = ERROR_MESSAGE();

        IF @DeviceActivationConstraintProbeUnexpectedError = 547
           AND PATINDEX(
                N'%[^0-9A-Za-z_]' + @DeviceActivationConstraintNamePattern + N'[^0-9A-Za-z_]%'
                , N' ' + @DeviceActivationConstraintProbeCaughtMessage + N' ') > 0
        BEGIN
            SET @DeviceActivationConstraintProbeRejected = 1;
            SET @DeviceActivationConstraintProbeUnexpectedError = NULL;
        END;
    END CATCH;

    IF XACT_STATE() <> 1
    BEGIN
        SET XACT_ABORT ON;
        THROW 51023, 'Device activation constraint migration probe left the transaction unusable.', 1;
    END;

    ROLLBACK TRANSACTION DeviceActivationConstraintProbe;
    SET XACT_ABORT ON;

    IF @DeviceActivationConstraintProbeUnexpectedError IS NOT NULL
    BEGIN
        DECLARE @DeviceActivationConstraintProbeErrorMessage nvarchar(2048) = CONCAT(
            N'Device activation constraint migration probe failed with SQL error ',
            @DeviceActivationConstraintProbeUnexpectedError,
            N'.');
        THROW 51024, @DeviceActivationConstraintProbeErrorMessage, 1;
    END;

    IF @DeviceActivationConstraintProbeRejected = 0
        THROW 51025, 'Device activation constraint migration probe accepted invalid case 14.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    SET XACT_ABORT ON;
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
