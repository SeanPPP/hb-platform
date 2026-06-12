## 实施内容

* Shared：新增 `Models/HBweb/LocalSupplier.cs`（继承 `BaseEntity`，主键 `Guid`，唯一 `LocalSupplierCode`，字段含 `Name/Status/ContactPerson/Phone/Email/Remark`），新增 `DTOs/LocalSupplierDtos.cs`（列表项、创建/更新请求、同步结果、分页响应）。

* Api 控制器：`Controllers/React/LocalSuppliersController.cs` 路由前缀 `/api/react/v1/local-suppliers`，提供：

  * `GET /` 分页/筛选

  * `GET /active` 启用列表

  * `POST /sync` 从 `DIC_供应商信息表` 同步（统计：新增/更新/停用/跳过/错误）

  * `POST /` 创建

  * `PUT /{code}` 更新

  * `DELETE /{code}` 删除

  * `PATCH /{code}/status/{status}` 状态切换

  * `GET /check-code/{code}` 唯一性校验

* Api 服务层：`Services/React/LocalSupplierService.cs`（分页、CRUD、状态、校验），`Services/React/LocalSupplierSyncService.cs`（同步实现，使用 `SqlSugarContext` 写入本库，用 `HBSalesSqlSugarContext` 读取 `DIC_供应商信息表`）。

## 同步映射与策略

* 字段映射：`LocalSupplierCode ← DIC.H供应商编码`，`Name ← DIC.H供应商名称`，`Status ← 1/0`，其余联系人/电话/邮箱/备注按 DIC 字段映射。

* Upsert：以 `LocalSupplierCode` 为键，分批（500）事务提交；DIC 停用→本地 `Status=0`；DIC 缺失不删除。

## 返回格式与权限

* 控制器返回 `{ success, data, message }` 结构，风格与现有 React 控制器一致。

* 标注 `[Authorize]` 与必要角色（如 Admin/ WarehouseManager）。

## 前端联调

* 已有页面 `PosAdmin/SupplierManagement` 与服务 `src/services/localSupplier.ts`（列表与同步），保持调用 `/api/react/v1/local-suppliers/*`。

* 编辑弹窗逻辑沿用：`LocalSupplierCode !== '200'` 显示本地供应商，`== '200'` 显示国内供应商。

## 验证

* 启动后端与前端，点击“同步”返回统计并刷新列表。

* 验证分页/筛选/状态切换/创建更新删除/唯一性校验；数据与 UI 一致。

## 后续

* 若需要：添加批量导入/导出、本地供应商编辑弹窗、操作日志。

