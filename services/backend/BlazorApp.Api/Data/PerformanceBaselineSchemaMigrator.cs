using BlazorApp.Shared.Models.HBweb;
using SqlSugar;

namespace BlazorApp.Api.Data;

public static class PerformanceBaselineSchemaMigrator
{
    internal const string ValidateSqlServerSnapshotIsolationSql =
        """
        DECLARE @DatabaseName sysname = DB_NAME();

        IF NOT EXISTS (
            SELECT 1
            FROM sys.databases
            WHERE [name] = @DatabaseName AND snapshot_isolation_state = 1
        )
        BEGIN
            THROW 51002, '性能基线需要 SQL Server ALLOW_SNAPSHOT_ISOLATION', 1;
        END;
        """;

    private static readonly Type[] TableTypes =
    [
        typeof(PerformanceMetricSample),
        typeof(PerformanceMetricBucket),
        typeof(PerformanceMetricDailyAggregate),
        typeof(PerformanceOperationalRun),
        typeof(PerformanceOperationalRunTransitionOutbox),
        typeof(PerformanceReleaseEvent),
        typeof(PerformanceBaselineCycle),
        typeof(PerformanceBaselineDefinition),
        typeof(PerformanceIngestRateWindow),
        typeof(PerformanceCollectorState),
    ];

    public static async Task EnsureAsync(ISqlSugarClient db, ILogger logger)
    {
        if (db.CurrentConnectionConfig.DbType == DbType.SqlServer)
        {
            await EnsureSqlServerAsync(db, logger);
            return;
        }

        db.CodeFirst.InitTables(TableTypes);
        if (db.CurrentConnectionConfig.DbType == DbType.Sqlite)
        {
            await db.Ado.ExecuteCommandAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS UX_PerformanceMetricSample_Project_Event "
                    + "ON PerformanceMetricSample(ProjectCode, EventId);"
            );
            await db.Ado.ExecuteCommandAsync(
                "CREATE INDEX IF NOT EXISTS IX_PerformanceMetricSample_ObservedAtUtc "
                    + "ON PerformanceMetricSample(ObservedAtUtc);"
            );
            await db.Ado.ExecuteCommandAsync(
                "CREATE INDEX IF NOT EXISTS IX_PerformanceMetricSample_ExactWebBundle "
                    + "ON PerformanceMetricSample(Environment, SourceType, MetricName, ObservedAtUtc);"
            );
            await db.Ado.ExecuteCommandAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS UX_PerformanceMetricBucket_Key "
                    + "ON PerformanceMetricBucket(MetricName, ProjectCode, Environment, SourceType, InstanceId, DimensionsHash, WindowStartUtc, BucketSizeMinutes);"
            );
            await db.Ado.ExecuteCommandAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS UX_PerformanceOperationalRun_External "
                    + "ON PerformanceOperationalRun(Category, Source, ExternalRunId);"
            );
            await db.Ado.ExecuteCommandAsync(
                "CREATE INDEX IF NOT EXISTS IX_PerformanceOperationalRunOutbox_Due "
                    + "ON PerformanceOperationalRunTransitionOutbox(DeadLetteredAtUtc, NextAttemptAtUtc);"
            );
            await db.Ado.ExecuteCommandAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS UX_PerformanceBaselineCycle_Environment "
                    + "ON PerformanceBaselineCycle(Environment);"
            );
            await db.Ado.ExecuteCommandAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS UX_PerformanceBaselineDefinition_Key "
                    + "ON PerformanceBaselineDefinition(CycleId, MetricName, Selector);"
            );
            await db.Ado.ExecuteCommandAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS UX_PerformanceMetricDailyAggregate_Key "
                    + "ON PerformanceMetricDailyAggregate(MetricName, ProjectCode, Environment, SourceType, DimensionsHash, DayUtc);"
            );
            await db.Ado.ExecuteCommandAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS UX_PerformanceIngestRateWindow_Key "
                    + "ON PerformanceIngestRateWindow(ProjectCode, ClientKeyHash, WindowStartUtc);"
            );
            await db.Ado.ExecuteCommandAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS UX_PerformanceCollectorState_Key "
                    + "ON PerformanceCollectorState(CollectorKey);"
            );
        }

        logger.LogInformation("性能与质量基线数据库结构检查完成");
    }

    private static async Task EnsureSqlServerAsync(ISqlSugarClient db, ILogger logger)
    {
        // 数据库级选项只能由 DBA 在维护窗口启用；应用启动只读校验，绝不执行 ALTER DATABASE。
        await db.Ado.ExecuteCommandAsync(ValidateSqlServerSnapshotIsolationSql);
        db.Ado.BeginTran();
        try
        {
            const string acquireLockSql =
                """
                DECLARE @Result int;
                EXEC @Result = sys.sp_getapplock
                    @Resource = N'PerformanceBaseline_Schema_Initialization',
                    @LockMode = N'Exclusive',
                    @LockOwner = N'Transaction',
                    @LockTimeout = 60000;
                IF @Result < 0
                    THROW 51000, '无法获取性能基线结构初始化锁', 1;
                """;
            await db.Ado.ExecuteCommandAsync(acquireLockSql);
            db.CodeFirst.InitTables(TableTypes);

            const string indexSql =
                """
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PerformanceMetricSample_Project_Event' AND object_id = OBJECT_ID(N'[dbo].[PerformanceMetricSample]'))
                    CREATE UNIQUE INDEX [UX_PerformanceMetricSample_Project_Event]
                    ON [dbo].[PerformanceMetricSample]([ProjectCode], [EventId]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PerformanceMetricSample_ObservedAtUtc' AND object_id = OBJECT_ID(N'[dbo].[PerformanceMetricSample]'))
                    CREATE INDEX [IX_PerformanceMetricSample_ObservedAtUtc]
                    ON [dbo].[PerformanceMetricSample]([ObservedAtUtc]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PerformanceMetricSample_ExactWebBundle' AND object_id = OBJECT_ID(N'[dbo].[PerformanceMetricSample]'))
                    CREATE INDEX [IX_PerformanceMetricSample_ExactWebBundle]
                    ON [dbo].[PerformanceMetricSample]([Environment], [SourceType], [MetricName], [ObservedAtUtc]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PerformanceMetricBucket_Key' AND object_id = OBJECT_ID(N'[dbo].[PerformanceMetricBucket]'))
                    CREATE UNIQUE INDEX [UX_PerformanceMetricBucket_Key]
                    ON [dbo].[PerformanceMetricBucket]([MetricName], [ProjectCode], [Environment], [SourceType], [InstanceId], [DimensionsHash], [WindowStartUtc], [BucketSizeMinutes]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PerformanceMetricBucket_Query' AND object_id = OBJECT_ID(N'[dbo].[PerformanceMetricBucket]'))
                    CREATE INDEX [IX_PerformanceMetricBucket_Query]
                    ON [dbo].[PerformanceMetricBucket]([Environment], [MetricName], [WindowStartUtc])
                    INCLUDE ([SampleCount], [SumValue], [MaximumValue], [LastObservedAtUtc]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PerformanceMetricDailyAggregate_Key' AND object_id = OBJECT_ID(N'[dbo].[PerformanceMetricDailyAggregate]'))
                    CREATE UNIQUE INDEX [UX_PerformanceMetricDailyAggregate_Key]
                    ON [dbo].[PerformanceMetricDailyAggregate]([MetricName], [ProjectCode], [Environment], [SourceType], [DimensionsHash], [DayUtc]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PerformanceMetricDailyAggregate_Query' AND object_id = OBJECT_ID(N'[dbo].[PerformanceMetricDailyAggregate]'))
                    CREATE INDEX [IX_PerformanceMetricDailyAggregate_Query]
                    ON [dbo].[PerformanceMetricDailyAggregate]([Environment], [MetricName], [DayUtc])
                    INCLUDE ([SampleCount], [SumValue], [MaximumValue], [LastObservedAtUtc]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PerformanceOperationalRun_Query' AND object_id = OBJECT_ID(N'[dbo].[PerformanceOperationalRun]'))
                    CREATE INDEX [IX_PerformanceOperationalRun_Query]
                    ON [dbo].[PerformanceOperationalRun]([Environment], [Category], [QueuedAtUtc]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PerformanceOperationalRun_External' AND object_id = OBJECT_ID(N'[dbo].[PerformanceOperationalRun]'))
                    CREATE UNIQUE INDEX [UX_PerformanceOperationalRun_External]
                    ON [dbo].[PerformanceOperationalRun]([Category], [Source], [ExternalRunId])
                    WHERE [ExternalRunId] IS NOT NULL;

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PerformanceOperationalRunOutbox_Due' AND object_id = OBJECT_ID(N'[dbo].[PerformanceOperationalRunTransitionOutbox]'))
                    CREATE INDEX [IX_PerformanceOperationalRunOutbox_Due]
                    ON [dbo].[PerformanceOperationalRunTransitionOutbox]([DeadLetteredAtUtc], [NextAttemptAtUtc]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PerformanceReleaseEvent_Query' AND object_id = OBJECT_ID(N'[dbo].[PerformanceReleaseEvent]'))
                    CREATE INDEX [IX_PerformanceReleaseEvent_Query]
                    ON [dbo].[PerformanceReleaseEvent]([Environment], [Action], [Status], [CompletedAtUtc]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PerformanceBaselineCycle_Environment' AND object_id = OBJECT_ID(N'[dbo].[PerformanceBaselineCycle]'))
                    CREATE UNIQUE INDEX [UX_PerformanceBaselineCycle_Environment]
                    ON [dbo].[PerformanceBaselineCycle]([Environment]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PerformanceBaselineDefinition_Key' AND object_id = OBJECT_ID(N'[dbo].[PerformanceBaselineDefinition]'))
                    CREATE UNIQUE INDEX [UX_PerformanceBaselineDefinition_Key]
                    ON [dbo].[PerformanceBaselineDefinition]([CycleId], [MetricName], [Selector]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PerformanceIngestRateWindow_Key' AND object_id = OBJECT_ID(N'[dbo].[PerformanceIngestRateWindow]'))
                    CREATE UNIQUE INDEX [UX_PerformanceIngestRateWindow_Key]
                    ON [dbo].[PerformanceIngestRateWindow]([ProjectCode], [ClientKeyHash], [WindowStartUtc]);

                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_PerformanceCollectorState_Key' AND object_id = OBJECT_ID(N'[dbo].[PerformanceCollectorState]'))
                    CREATE UNIQUE INDEX [UX_PerformanceCollectorState_Key]
                    ON [dbo].[PerformanceCollectorState]([CollectorKey]);
                """;
            await db.Ado.ExecuteCommandAsync(indexSql);
            db.Ado.CommitTran();
            logger.LogInformation("性能与质量基线数据库结构检查完成");
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
    }
}
