# Mobile OTA 多 runtime 运行手册

本文用于管理移动端 `mobile` 应用在同一环境和平台上同时投放多个 runtime。当前策略仍有一个主目标（primary），并可通过管理 API 配置附加目标（additional targets）。每个平台的目标发布必须属于同一 `environment`、`platform`、`appKey=mobile` 和与环境一致的 `clientChannel` lane，runtime 映射按发布事实精确匹配。

## 变更前检查

先确认要投放的每个 `AppOtaRelease` 已完成发布和登记，记录每个目标的：

- `Id`（下面示例使用占位 GUID）
- `RuntimeVersion`
- `Platform`
- `Environment`
- `ClientChannel`
- `UpdateId` 和 `UpdateGroupId`

主目标和附加目标必须来自同一平台和环境，并且 runtime 不得重复。发布事实是不可变记录；策略 API 只引用已登记的发布。

数据库变更按以下顺序执行。命令应在 API 项目实际部署目录、使用目标环境配置运行：

```bash
dotnet run -- --schema=migrate
dotnet run -- --schema=check
```

确认 `20260921.001-mobile-ota-runtime-targets` 已登记，并确认 `dbo.MobileOtaPolicy.AdditionalTargetsJson` 为可空 `nvarchar(max)`。使用候选 API 镜像执行迁移和检查；`--schema=check` 通过后替换 API 容器，确认健康，再使用管理 API 保存策略。生产 compose 使用 `docker compose --profile schema run --rm --no-deps hb-api-migrate` 迁移，随后使用同一服务加 `--schema=check` 参数检查。只重建 `hb-api`，不要重建 POS 或旧版服务。迁移只追加列，不更新旧策略行。

## 管理 API

读取当前策略：

```http
GET /api/app-update-policies/mobile-ota/production/android
```

替换策略：

```http
PUT /api/app-update-policies/mobile-ota/production/android
Content-Type: application/json

{
  "expectedPolicyVersion": 29,
  "enabled": true,
  "required": false,
  "targetReleaseId": "00000000-0000-0000-0000-000000000001",
  "releaseMessage": "移动端 OTA 更新",
  "additionalTargetReleaseIds": [
    "00000000-0000-0000-0000-000000000002"
  ]
}
```

`expectedPolicyVersion` 是 CAS 门禁，必须使用读取到的当前版本；冲突时重新读取策略，核对并发修改后再决定新的目标集合，不能直接套用旧请求重试。`required` 和 `releaseMessage` 是整个策略的全局字段，应用于当前请求保存的主目标和附加目标。`enabled=false` 会停用整个 lane，并清除主目标和所有附加目标；此时不应依赖目标 ID 字段。

`additionalTargetReleaseIds` 有三种语义：

| 请求字段 | 结果 |
| --- | --- |
| 省略或为 `null` | 保留现有附加目标 |
| `[]` | 清空所有附加目标 |
| 非空数组 | 用数组替换附加目标，并校验每个发布的身份和 runtime |

旧管理页面暂时不会提交附加字段，因此省略字段时可以继续保存并保留现有附加目标。附加目标目前通过上述管理 API 操作。修改 `targetReleaseId` 时，如果新主目标与现有附加目标产生 runtime 冲突，API 会拒绝该请求；应在同一次 PUT 中明确提交新的主目标和无冲突的附加目标集合，避免中间态丢失投放范围。

每次成功变更都会写入带完整目标集合的审计快照。审计读取接口为：

```http
GET /api/app-update-policies/mobile-ota/production/android/revisions
```

快照应保留主目标、全部附加目标、runtime、策略版本、`required`、说明和操作者信息。不要只依据当前策略行重建历史投放范围。

## 客户端匹配规则

客户端请求必须携带平台、channel、runtime，以及当前 update ID 和 group ID。服务端先按当前客户端 runtime 做精确匹配，再返回对应的发布事实；不能把一个 runtime 的发布当作另一个 runtime 的更新。主目标和附加目标的选择顺序由 runtime 精确映射决定，主目标只作为策略的 primary 记录，不覆盖客户端 runtime 匹配。

启用多 runtime 后，分别验证 Android 和 iOS 的真实请求：

```text
platform=Android, clientChannel=production, runtimeVersion=1.0.5
platform=Android, clientChannel=production, runtimeVersion=1.0.6
platform=iOS,     clientChannel=production, runtimeVersion=1.0.5
platform=iOS,     clientChannel=production, runtimeVersion=1.0.6
```

每个 runtime 都应只返回自己的 `releaseChannel`、`updateId` 和 `updateGroupId`；未配置的 runtime 应返回无更新。验证时保留 API 响应和策略版本，避免只凭管理页面显示判断客户端已接收 OTA。

## 回滚

回滚时保留 `AdditionalTargetsJson` 可空列和历史快照，不删除列、不覆盖旧发布事实。若需要先恢复旧客户端，策略可以暂时设置为：

- primary 指向 `1.0.5`；
- additional target 指向 `1.0.6`；
- `enabled`、`required` 和说明按业务要求设置。

新后台会同时匹配上述两个 runtime。若回滚到不支持附加目标的旧 API 镜像，它只读取 primary，因此 `1.0.5` 仍有更新目标，`1.0.6` 暂停新的投放；设备已经安装的 OTA 不会因此被卸载。恢复新版后台后，重新核验全部目标。若要停止 `1.0.6`，使用 `additionalTargetReleaseIds: []` 明确清空附加目标，再用当前 `expectedPolicyVersion` 提交。回滚后仍需按 runtime 逐个平台验证，并检查新的审计快照。

示例中的 GUID、runtime 和 channel 仅用于说明格式，执行前必须替换为目标环境中已登记的真实发布事实；文档不包含任何凭据。
