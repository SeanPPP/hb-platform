## 原因分析

* 实体与模型集中在 `d:\Development\cline\blazor\BlazorApp.Shared`，且已有 `HqEntities/DIC_供应商信息表.cs` 可供同步来源。

* 现无 LocalSupplier 实体，需要新增 Shared 模型与 DTO，再在 `BlazorApp.Api` 的 Controllers/Services/React 目录实现接口与同步逻辑，统一走 `/api/react/v1/local-suppliers` 与 React 前端对接。

## 新增与改动位置

* BlazorApp.Shared

  * 新增实体：`Models/HBweb/LocalSupplier.cs`（SqlSugar 映射）

  * 新增 DTO：`DTOs/LocalSupplierDtos.cs`（列表项、分页响应、创建/更新请求、同步结果）

* BlazorApp.Api

  * 控制器：`Controllers/React/LocalSuppliersController.cs`（REST：列表/active/同步/创建/更新/删除/状态切换/唯一性校验）

  * 服务层：`Services/React/LocalSupplierService.cs`（分页、CRUD、状态、唯一性）

  * 同步服务：`Services/React/LocalSupplierSyncService.cs`（读取 `HqEntities/DIC_供应商信息表` 批量 Upsert）

  * 仓储（可选）：`Repositories/React/LocalSupplierRepository.cs`（封装 SqlSugar 访问主库与 DIC 源库）

## 数据模型

* 表 `LocalSuppliers`

  * `Guid Id`（PK, default UUIDV7）

  * `string LocalSupplierCode`（唯一，必填）

  * `string Name`（必填）

  * `int Status`（1启用/0禁用）

  * `string? ContactPerson`、`string? Phone`、`string? Email`、`string? Remark`

  * `DateTime CreatedAt`（UTC, default GETUTCDATE()）、`DateTime? UpdatedAt`

* 索引：唯一索引 `LocalSupplierCode`；普通索引 `Status`

## 路由设计（Controllers/React/LocalSuppliersController）

* `GET /api/react/v1/local-suppliers`：分页/筛选（`pageIndex/pageSize/keyword/status`），返回 `PagedResultDto<LocalSupplierDto>`

* `GET /api/react/v1/local-suppliers/active`：启用列表 `List<LocalSupplierDto>`

* `POST /api/react/v1/local-suppliers/sync`：从 DIC 表同步，返回 `{ createdCount, updatedCount, deactivatedCount, skippedCount, errors[] }`

* `POST /api/react/v1/local-suppliers`：创建（校验唯一与必填）

* `PUT /api/react/v1/local-suppliers/{code}`：更新（除 `code` 外字段）

* `DELETE /api/react/v1/local-suppliers/{code}`：删除

* `PATCH /api/react/v1/local-suppliers/{code}/status/{status}`：状态切换（0/1）

* `GET /api/react/v1/local-suppliers/check-code/{code}`：唯一性校验

## 同步实现

* 来源：`BlazorApp.Shared/Models/HqEntities/DIC_供应商信息表.cs`

* 字段映射：

  * `LocalSupplierCode ← SupplierCode`

  * `Name ← SupplierName`

  * `Status ← IsActive ? 1 : 0`

  * `ContactPerson/Phone/Email/Remark ← DIC 对应字段`

* Upsert 策略：按 `LocalSupplierCode` 插入或更新；DIC 停用→本地设为 `Status=0`；DIC 缺失不删除。

* 批处理：每批 500 条事务提交；失败记录入 `errors[]`；返回统计。

## 业务规则

* 前端编辑弹窗显示逻辑：`LocalSupplierCode !== '200'` 显示“本地供应商”；`LocalSupplierCode === '200'` 显示“国内供应商”。后端需允许保存 `LocalSupplierCode` 与 `SupplierCode`（基础 Product 侧）。

## 与前端联通

* React 前端已通过 Umi 代理 `'/api' -> Blazor 后端`，直接调用上述路由；`src/services/localSupplier.ts` 已包含 `getLocalSuppliers / getActiveLocalSuppliers / syncLocalSuppliers`。

* 供应商管理页面 `src/pages/PosAdmin/SupplierManagement` 的“同步”按钮调用 `POST /sync` 并刷新列表。

## 验证

* 启动后端与前端；在“供应商管理”点击“同步”，返回统计并刷新列表。

* 测试分页、状态筛选、创建/更新/删除与唯一性校验；万级数据分批 Upsert 性能稳定。

## 注意事项

* 统一使用 SqlSugarContext；如需第二数据源（DIC），在服务/仓储中增加连接。

* 审计字段使用 UTC；校验邮箱格式与字段长度；返回统一 `ApiResponse` 风格。

* 权限沿用现有认证与 `isAdmin` 控制。

## 下一步

* 我将按上述结构新增 Shared 实体/DTO，Api 控制器与服务层，并实现 `/sync`、分页与基础 CRUD，随后与前端联调验证页面与弹窗。

