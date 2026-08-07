# 挂单（Held Orders）跨设备能力分析交接文档

> 文档状态：分析结论 + 交接清单
> 适用代码基线：`c66cb6b0`（main）
> 编写目的：为"挂单是否支持跨设备（跨终端）取回"这一需求决策提供完整现状分析，并为后续实现交接关键上下文。

---

## 1. 结论摘要

**当前挂单是纯本地、单终端（store + device 双维度）能力，跨设备取回不被支持，且这是刻意设计而非缺陷。**

- 数据只落在本机 SQLite（`held_order_records` + `terminal_cart_fences`），**不进入** `local_orders` / `tender` / `outbox` 任何同步通道（`apps/pos-ipad/src/core/db/sqlite-repositories.ts:248-252`）。
- 挂单 payload 用本机 Keychain 密钥做字段级认证加密（`SensitivePayloadEncryptor`，`requireThisDeviceOnly: true`），跨设备无法直接解密（`apps/pos-ipad/src/core/security/sensitive-payload-encryptor.ts:11,20-21`）。
- 收银员会话 scope 与终端元数据强校验（`assertTrustedCashierScope`），deviceCode 不一致直接抛错（`apps/pos-ipad/src/core/runtime/production-pos-service-composition.ts:2566-2584`）。
- 中英文 UI 文案均明确"仅限本门店、本终端"（`apps/pos-ipad/src/features/held-orders/held-orders-copy.ts:18,49`）。

**因此"跨设备挂单"属于新需求，需要新增同步/服务端能力，不是修 bug。** 改动面与风险见第 4 节。

---

## 2. 挂单功能现状全景

### 2.1 功能入口与 UI

| 项目 | 位置 |
|---|---|
| 路由入口 | `apps/pos-ipad/app/held-orders.tsx`（`HeldOrdersRoute`，权限守卫 `resolveProtectedSalesRouteGate`） |
| 销售页入口 | `apps/pos-ipad/app/sales.tsx:320`（`push("/held-orders")`），按钮 `apps/pos-ipad/src/features/sales/ui/sales-screen.tsx:1248` |
| 界面 | `apps/pos-ipad/src/features/held-orders/held-orders-screen.tsx`（FlatList、行内 recall/recover/release） |
| 文案 | `apps/pos-ipad/src/features/held-orders/held-orders-copy.ts`（中英双语） |

### 2.2 分层架构

- **领域层** `apps/pos-ipad/src/features/held-orders/held-orders-domain.ts`：纯类型 + 守卫。`HeldOrderScope = { storeCode, deviceCode }`（类型定义于 `apps/pos-ipad/src/core/contracts/held-orders.ts:11`，工厂 `heldOrderScope()` 在 `apps/pos-ipad/src/features/held-orders/held-orders-domain.ts:113-118`），`isInHeldOrderScope` 严格双字段相等（L158-166），`emptySalePricingState` 保证恢复时用挂起时的促销快照而非重新定价（L127-141）。
- **编排层** `apps/pos-ipad/src/features/held-orders/held-orders-orchestrator.ts`：hold / recall / recover / release / list 五个操作；`refreshInFlight`（L52-60）/ `runMutation`（L62-73）串行化；每个动作 = 主管授权（`withAuthorization`）+ 独占购物车租约（`withCartLease`）+ repository 调用。
- **表现层** `apps/pos-ipad/src/features/held-orders/held-orders-presenter.ts`：状态机 loading/ready/unauthorized/failed，操作后自动刷新。
- **组合根** `apps/pos-ipad/src/core/runtime/production-pos-service-composition.ts`：`createHeldOrdersOrchestrator`（L1538-1557）、`createHeldActiveCartPort`（L2478-2498）、`createHeldOrderAuthorization`（L2500-2527）、`trustedSalesSession`（L2557-2564）——即 scope 来源与强校验都在这层。

### 2.3 存储模型（纯本地 SQLite）

**M9 `held_order_records`**（`apps/pos-ipad/src/core/db/migrations.ts:495-552`）：

- PK `hold_id`；`local_sequence` UNIQUE（本地自增，`allocateLocalSequence`，`apps/pos-ipad/src/core/db/sqlite-repositories.ts:1667-1682`）。
- `store_code` / `device_code` 双列，所有查询都 `WHERE store_code = ? AND device_code = ?`。
- `status` CHECK：`Pending` / `Recalling` / `Recalled`，且通过 CHECK 约束锁定各状态的字段组合（recalling_* 字段只在 Recalling/Recalled 出现）。
- `payload_ciphertext BLOB`：完整定价快照的加密载荷（版本 1，xchacha20poly1305 + AAD）。
- 汇总列（item_count / subtotal / discount / actual_amount）供列表展示，不落明文明细。

**M10 `terminal_cart_fences`**（`apps/pos-ipad/src/core/db/migrations.ts:559-655`）：

- PK `(store_code, device_code)`，每终端最多一个栅栏；`kind` CHECK `HoldClear` / `RecallActive`。
- `hold_id` UNIQUE 且引用 `held_order_records`（`ON DELETE RESTRICT`）——**hold_id 只能绑定一个 scope，避免同一挂单被两个终端同时恢复**（注释见 `apps/pos-ipad/src/core/db/migrations.ts:555-557`，DDL 在 L564-565）。
- 触发器校验栅栏状态与挂单状态一致（L599-643），并迁移历史 Recalling 记录为栅栏（L647-654）。

**Repository**：`SqliteHeldOrderRecordRepository`（`apps/pos-ipad/src/core/db/sqlite-repositories.ts:253-585`）实现 `HeldOrderRecordRepositoryPort`（`apps/pos-ipad/src/core/contracts/held-orders.ts:56-90`）。每个状态动作都在同一 `BEGIN IMMEDIATE` 事务内更新挂单 + 审计 + 栅栏，进程被杀后仍能判断应清车、恢复或释放。

### 2.4 数据流

- **创建 hold**：销售页 → `holdCurrentCart` → `orchestrator.hold` → `holdOnce` → `withAuthorization(HOLD_ORDER_PERMISSION)`（主管扫码）→ `withCartLease` → `holdAuthorized`：校验购物车（sale 模式、非空、无活动栅栏）→ `repository.hold`（事务：INSERT 挂单 + ORDER_HOLD 审计 + HoldClear fence）→ 清车 → `confirmHoldCartCleared` 删 fence。
- **取回 recall**：`orchestrator.recall` → `recallOnce` → 双权限（RECALL_LIST + RECALL_RESTORE）→ `recallAuthorized`：校验当前车为空 → `repository.claimRecall`（事务 Pending→Recalling + RecallActive fence）→ `restoreClaim`：购物车租约阻塞等待恢复 → 用加密 payload 还原定价状态。
- **崩溃恢复 recover**：binding 匹配直接成功，否则 `loadRecallClaim` → `restoreClaim`。
- **释放 release**：清车 → `releaseRecallAfterCartCleared`（删 fence + Recalling→Pending）。
- **列表 list**：`listPending(scope, 200)` + `listRecoverable` 合并去重，按 localSequence 倒序。
- **现金结账收尾**：`completeRecalledHoldInCashTransaction`（`apps/pos-ipad/src/core/db/pos-database.ts:1482-1526`）把 Recalling→Recalled 并删 fence；结账前经 `readTerminalCartFenceForCheckout` 检查栅栏（`apps/pos-ipad/src/core/db/pos-database.ts:1367` 起）。

### 2.5 兼容层

legacy M3 `held_orders` 表 + `SqliteHeldOrderRepository`（`apps/pos-ipad/src/core/db/sqlite-repositories.ts:212-217`）仍存在，但**只供兼容导出，生产收银流程必须走 `heldOrderRecords`**（`apps/pos-ipad/src/core/db/sqlite-repositories.ts:64`、`apps/pos-ipad/src/core/contracts/held-orders.ts:52-55`：legacy 缺完整定价状态和设备范围，禁止静默有损恢复）。

---

## 3. 跨设备能力现状：不支持的原因（逐条证据）

| # | 阻碍点 | 证据 |
|---|---|---|
| 1 | **无同步通道**：挂单明确排除在 `local_orders` / `tender` / `outbox` 之外，本地工作流是刻意设计 | `apps/pos-ipad/src/core/db/sqlite-repositories.ts:248-252` |
| 2 | **scope 双字段硬绑定**：所有 SQL 与守卫都要求 store + device 完全相等 | `apps/pos-ipad/src/features/held-orders/held-orders-domain.ts:158-166`；`apps/pos-ipad/src/core/db/sqlite-repositories.ts:335,374,432,454,501,533,568` |
| 3 | **载荷加密绑死本机**：Keychain 密钥 `requireThisDeviceOnly: true`，他机无法解密 | `apps/pos-ipad/src/core/security/sensitive-payload-encryptor.ts:11,20-21` |
| 4 | **收银员会话强校验**：session.deviceCode ≠ 终端元数据 deviceCode 即抛错 | `apps/pos-ipad/src/core/runtime/production-pos-service-composition.ts:2566-2584` |
| 5 | **local_sequence 本地自增**：跨设备合并必然冲突（UNIQUE） | `apps/pos-ipad/src/core/db/sqlite-repositories.ts:1667-1682`；`apps/pos-ipad/src/core/db/migrations.ts:498` |
| 6 | **fence 语义防双终端**：hold_id 全局 UNIQUE + PK(store, device)，栅栏触发器等约束假定单终端权威 | `apps/pos-ipad/src/core/db/migrations.ts:554-557,559-593` |
| 7 | **UI 文案承诺单终端**：改了行为必须同步改文案 | `apps/pos-ipad/src/features/held-orders/held-orders-copy.ts:3,18,49,64` |
| 8 | 全仓无跨设备相关 TODO/FIXME；`cross-device`（含 camelCase 变体 `crossDevice*`）命中仅在 installments（分期）与 openapi/schema 能力开关（`crossDevice*Enabled`，属 installments 能力），与挂单零关联 | 全仓 grep |

---

## 4. 若支持跨设备：改动面与方案选项

> 以下为**分析**，非已批准方案。任何落地前需要产品确认跨设备场景的真实形态（同店多终端？跨店？云端查看？）。

### 4.1 必须先回答的三个产品问题

1. 跨设备的作用域：**同门店内多终端**（store 相同、device 不同）还是跨门店？
2. 数据方向：仅"他机可查看"（只读），还是"他机可取回"（恢复购物车）？
3. 离线容忍度：目标终端离线时，是允许取回还是必须在线？

### 4.2 改动面清单（按影响排序）

| 改动面 | 位置 | 影响 |
|---|---|---|
| **同步通道/服务端接口** | 新增（现无任何通道） | 最大。挂单载荷含完整定价快照（促销、明细），需要新 API + 上送/拉取/删除协议；或复用 outbox 体系（需评估审计与幂等） |
| **scope 模型** | `apps/pos-ipad/src/features/held-orders/held-orders-domain.ts`、`apps/pos-ipad/src/core/contracts/held-orders.ts`、全部 SQL | device 维度弱化或引入"目标终端"概念；`isInHeldOrderScope`、fence PK 全部受影响 |
| **payload 加密** | `apps/pos-ipad/src/core/security/sensitive-payload-encryptor.ts` | 跨设备解密需要：服务端代为托管密钥（**信任模型变更：服务端将持有可解密全部门店挂单明文的密钥，单点失陷=全量泄露，需 KMS/信封加密/密钥分片与审计合规设计**）或改用服务端加密再下发（改 AAD/版本，风险等级低于托管密钥） |
| **并发控制** | `terminal_cart_fences` + orchestrator | 双终端同时取回同一挂单的竞争需要服务端原子认领（现靠本机事务 + hold_id UNIQUE） |
| **local_sequence** | `allocateLocalSequence` | 跨设备唯一序号需服务端分配或改为 (store, device) 复合序号 + 展示层调整 |
| **授权模型** | `apps/pos-ipad/src/core/runtime/production-pos-service-composition.ts:2557-2584` | `assertTrustedCashierScope` 需放行"他机取回"场景，权限码与审计需扩展 |
| **UI/文案** | `apps/pos-ipad/src/features/held-orders/held-orders-copy.ts`、`apps/pos-ipad/src/features/held-orders/held-orders-screen.tsx` | "仅限本终端"文案、空态提示、跨设备标识展示 |
| **测试** | `apps/pos-ipad/src/features/held-orders/held-orders-orchestrator.test.ts`、`apps/pos-ipad/src/core/db/sqlite-held-order-records.integration.test.ts` | 全部 scope 断言需按新模型重写 |

### 4.3 方案选项（粗略对比）

| 方案 | 复杂度 | 风险 | 适用场景 |
|---|---|---|---|
| A. 维持单设备，跨设备仅提供"只读导出/打印" | 低（legacy 已有导出基础） | 低-中（**导出解密后的定价快照明文将产生跨终端/打印明文泄露面，需最小化导出内容并脱敏**） | 需求仅为"其他终端能看到这台机器挂了单" |
| B. 服务端云挂单：hold 时上送、recall 时拉取，服务端原子认领 | 高 | 中-高（**密钥托管=信任模型变更：服务端可解密全部挂单明文（含顾客数据），需 KMS/信封加密/密钥分片 + 数据驻留审计；另需离线语义与审计**） | 多终端真需要互相取回，且网络可靠 |
| C. 同门店局域网直连同步（无服务端） | 中 | 高（**无认证的设备发现协议、明文/弱加密传输、MITM/伪终端风险；需 TLS + 设备认证**） | 单门店少量终端且不允许云数据 |

### 4.4 必须保持的不变量（无论选哪个方案）

1. 挂单恢复必须使用**挂起时的促销/定价快照**，不得按恢复时刻重新定价（`emptySalePricingState` / `HeldOrderPayloadV1.pricingState`）。
2. 同一挂单**同一时刻只能被一个终端取回**（现由 fence + hold_id UNIQUE 保证，跨设备后需等价的服务端原子性）。
3. 挂单永远不能以有损方式恢复（legacy M3 禁止静默恢复的先例）。
4. 现金结账完成前 `bound_order_guid` 必须保持 NULL（`apps/pos-ipad/src/core/db/migrations.ts:568`；注意：DB CHECK 仅在 HoldClear 分支强制 NULL（L574），RecallActive 分支无约束，该不变量依赖应用层保证）。
5. 挂单载荷解密失败必须整体拒绝而非静默跳过（`apps/pos-ipad/src/core/db/sqlite-repositories.ts:573-574` 的行为契约）。

---

## 5. 交接清单（给后续实现者）

### 5.1 必读文件（按顺序）

1. `apps/pos-ipad/src/core/contracts/held-orders.ts` — Port 契约（90 行，先读这个）
2. `apps/pos-ipad/src/features/held-orders/held-orders-domain.ts` — 领域类型与守卫
3. `apps/pos-ipad/src/features/held-orders/held-orders-orchestrator.ts` — 状态机与编排（503 行）
4. `apps/pos-ipad/src/core/db/migrations.ts` M9/M10 — 表结构与约束（L491-654）
5. `apps/pos-ipad/src/core/db/sqlite-repositories.ts:253-585` — Repository 实现
6. `apps/pos-ipad/src/core/runtime/production-pos-service-composition.ts:1538-1557, 2478-2584` — 组合根与授权
7. `apps/pos-ipad/src/core/security/sensitive-payload-encryptor.ts` — 载荷加密
8. `apps/pos-ipad/src/features/held-orders/held-orders-copy.ts` — 现有"单终端"承诺文案

### 5.2 测试资产

- `apps/pos-ipad/src/features/held-orders/held-orders-orchestrator.test.ts`（777 行，状态机全覆盖）
- `apps/pos-ipad/src/core/db/sqlite-held-order-records.integration.test.ts`（存储层集成）
- `apps/pos-ipad/src/features/held-orders/held-orders-screen.rntl.test.tsx`、`apps/pos-ipad/src/tests/routes/held-orders-route.rntl.test.tsx`（UI/路由）

### 5.3 现有已知限制（设计注释中记录）

- (a) V2 与 legacy M3 并存，legacy 只能导出（`apps/pos-ipad/src/core/contracts/held-orders.ts:52-55`）。
- (b) 挂单不入 outbox/local_orders → 跨设备不可用的根因（`apps/pos-ipad/src/core/db/sqlite-repositories.ts:248-252`）。
- (c) 恢复记录密文/结构异常整体拒绝，不静默跳过（`apps/pos-ipad/src/core/db/sqlite-repositories.ts:573-574`）。
- (d) M9 升级遇 Recalling 记录时靠唯一约束 fail-closed（`apps/pos-ipad/src/core/db/migrations.ts:645-646`）。

### 5.4 建议的下一步

1. 产品确认 4.1 的三个问题 → 在 4.3 中选方案。
2. 若选方案 A（只读导出）：改动集中在展示层 + 复用 legacy 导出路径，风险最低。
3. 若选方案 B/C：先做同步通道与加密密钥的架构设计评审（涉及安全与审计），再动 scope 模型。

---

## 6. 附：验证记录

本文档所有代码引用均基于以下验证：

- `SqliteHeldOrderRecordRepository` 全 SQL 均为 `store_code = ? AND device_code = ?` 双条件过滤（抽查 `apps/pos-ipad/src/core/db/sqlite-repositories.ts:335, 374`）。
- `SensitivePayloadEncryptor` 密钥仅存本机 Keychain，`requireThisDeviceOnly: true`（`apps/pos-ipad/src/core/security/sensitive-payload-encryptor.ts:11`）。
- `assertTrustedCashierScope` 对 store/device 双字段强校验（`apps/pos-ipad/src/core/runtime/production-pos-service-composition.ts:2578-2583`）。
- 中英文文案明确单终端承诺（`apps/pos-ipad/src/features/held-orders/held-orders-copy.ts:18, 49`）。
- 全仓无挂单跨设备 TODO/FIXME。
