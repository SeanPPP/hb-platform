## 目标
- 从 `DIC_供应商信息表` 同步数据到 `LocalSupplier` 表（React 目录内后端）。
- 新增“LocalSupplier 管理页面”，在“收银后台管理/供应商管理页面”中提供“同步”按钮，完成一键数据同步并刷新列表。

## 数据同步设计
- 同步端点：`POST /api/react/v1/local-suppliers/sync`
  - 入参（可选）：`since`（Date，用于增量）、`overwrite`（bool，默认 true：按字段变更更新）
  - 返回：`{ success, createdCount, updatedCount, deactivatedCount, skippedCount, errors[] }`
- 数据源连接：SqlSugar 新增第二数据源（POS/主库）读取 `DIC_供应商信息表`
- 字段映射：
  - `LocalSupplierCode` ← `DIC.SupplierCode`
  - `name` ← `DIC.SupplierName`
  - `status` ← `DIC.IsActive ? 1 : 0`
  - `contactPerson` ← `DIC.ContactPerson`
  - `phone` ← `DIC.Phone`
  - `email` ← `DIC.Email`
  - `remark` ← `DIC.Remark`
- 合并/去重策略（Upsert）：
  - 以 `LocalSupplierCode` 为主键进行插入或更新
  - 名称/联系方式/状态发生变化则更新；未变化则跳过
  - DIC 标记停用且本地存在 → 将 `status=0`（deactivatedCount）
  - DIC 无记录但本地存在：不删除，仅保留现状（避免误删）
- 事务与并发：批量 upsert 分批提交（如 500 条/批），失败项记录到 `errors`
- 审计：`createdAt/updatedAt` 自动写入；保留操作日志（可选）

## 后端模块结构（React 目录内）
- `server/modules/localSupplier/sync.ts`（读取 DIC 表、做映射与 upsert）
- `server/modules/localSupplier/controller.ts`（新增 `/sync` 路由）
- `server/db/sqlsugar.ts`（多数据源配置：本库 + DIC 源库）

## 管理页面
- 路由：`/pos-admin/supplier-management`（收银后台管理/供应商管理页面）
- 菜单与国际化：
  - `menu.posAdmin`: 收银后台管理
  - `menu.posAdmin.supplierManagement`: 供应商管理
- 页面功能：
  - 列表：`LocalSupplierCode`、`名称`、`状态`、`联系人`、`电话`、`Email`、`备注`、`更新时间`
  - 搜索：关键字（code/name）、状态筛选
  - 操作：编辑、启用/停用、删除（可选）
  - 同步按钮：顶部工具栏“同步”→ 调用 `POST /api/react/v1/local-suppliers/sync` → 显示结果统计 → 刷新表格

## 前端服务层
- 扩展 `src/services/localSupplier.ts`：
  - `syncLocalSuppliers(params?: { since?: string; overwrite?: boolean })`
  - `toggleLocalSupplierStatus(code: string, status: number)`
  - `create/update/delete`（可选，若允许手工维护）

## 校验与安全
- 同步端需 `isAdmin` 权限；校验 `since` 格式、`overwrite` 布尔类型
- Email 格式校验、长度限制；防止超长输入
- 同步操作写操作日志，便于回溯（可选）

## 测试与验证
- 端到端联调：模拟 DIC 表新增/更新/停用，验证本地 upsert/deactivate
- 大数据量测试：万级记录分批 upsert 的稳定性与性能
- 页面验证：点击“同步”后统计信息正确显示，列表刷新展示映射后的数据

## 迁移
- 新建 `LocalSuppliers` 表与唯一索引，初始化必要的连接配置
- 首次同步：不带 `since` 的全量迁移；后续使用增量 `since` 提升性能

## 下一步
- 实现后端 `/sync` 逻辑与多数据源连接
- 创建管理页面与同步按钮、列表/筛选/操作
- 完成联调与上线准备