# 分时全年回填 Runner

该工具直接引用后端正式服务，默认执行纯只读逐日预览。它不启动 WebHost，不读取或修改 `ActiveInstanceId`，写模式只推进命令指定的批次，并与后台 worker 共用数据库全局租约。

```bash
dotnet build outputs/hourly-backfill-runner-20260915/HourlyBackfillRunner.csproj
dotnet run --project outputs/hourly-backfill-runner-20260915/HourlyBackfillRunner.csproj -- --self-test
dotnet run --project outputs/hourly-backfill-runner-20260915/HourlyBackfillRunner.csproj -- preview --start 2025-09-15 --end 2025-09-15 --checkpoint /tmp/hourly-preview.json
```

配置可来自 `--config-root` 下的 appsettings，也可由容器现有的 `ConnectionStrings__DefaultConnection`、`ConnectionStrings__HBPOSMConnection`、`ConnectionStrings__HBSalesRecord` 环境变量提供。工具不会输出连接串。完整受控步骤见正式 runbook。
