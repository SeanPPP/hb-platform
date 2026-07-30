# iPad 目录索引发布与回滚门禁

本文只描述目录索引本轮单 API 副本发布。多副本协调、共享快照目录和应用自动修改数据库选项均不在本次范围内。

## 发布前数据库门禁

先在目标 SQL Server 数据库以只读账号执行：

```sql
SELECT
    name,
    snapshot_isolation_state_desc,
    is_read_committed_snapshot_on
FROM sys.databases
WHERE database_id = DB_ID();
```

`snapshot_isolation_state_desc` 必须为 `ON`，否则停止发布。由 DBA 在维护窗口评估并启用 `ALLOW_SNAPSHOT_ISOLATION`；应用和部署脚本不得执行 `ALTER DATABASE`。

## 发布顺序

1. 保留当前可运行的 API 镜像、配置和目录快照目录，确认可直接回滚。
2. 仅发布一个兼容旧客户端的 API 副本，检查 health、目录冷构建、full、delta、noChange 和旧版无租约分页。
3. 先做单店单机 canary 24 小时，再扩大到 10% 门店 48 小时，最后全量。
4. 新 iPad M25 必须分两阶段发布：先发布仍停留在 M24、但 schema 校验可接受 M24/M25 的过渡版本；确认覆盖后再发布执行 M25 的目录版本。M25 后的 app 回滚目标只能是该过渡版本。更早的严格 M24 版本不能打开新增列后的数据库，不能作为恢复手段。

## 指标门禁

- 首次安装或换店全量端到端 p95 不超过 300 秒。
- 同店 delta p95 不超过 60 秒；noChange p95 不超过 5 秒。
- 后端冷构建 p95 不超过 240 秒。
- 目录维护期间，订单和支付数据库操作额外阻塞 p95 不超过 500ms，最大不超过 1 秒。
- 使用当前实际商品数与 344,665 条中的较大值；设备使用最旧的已批准生产 iPad，网络使用试点门店观测到的 p10 条件。
- API 进程峰值内存不得超过容器限制的 70%；出现 `CATALOG_CAPACITY_BUSY` 时，已有租约分页必须继续成功。

任一门禁未达标均停止扩量，并保留当前 canary 范围和证据。

## 回滚

1. 优先设置 `HBPOS_CATALOG_DELTA_ENABLED=false` 并重建 API 容器，使 sync-plan 统一返回带租约的 full。
2. 后端需要回滚时恢复上一可运行镜像；新 iPad 在 sync-plan 不存在时回退旧首包固定版本全量流程。
3. M25 是加法迁移；不要删除新增列或表。事务失败、取消、断电时旧 active 必须继续可用，由启动恢复清理遗留 staging/retired 数据。
4. 回滚后重新验证 health、旧版无租约 full 分页、订单与支付，并保留新旧目录快照文件用于故障分析。
