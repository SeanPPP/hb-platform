## 原因分析
- 前端页面 `ReactUmi/my-app/src/pages/PosAdmin/ProductManagement/index.tsx:225-339` 的表格列当前未包含“更新人/更新时间”。
- 前端 `ProductDto` 类型 `ReactUmi/my-app/src/services/product.ts:23-39` 不包含 `updatedAt/updatedBy` 字段，无法展示。
- 后端服务虽然在更新时写入了 `UpdatedAt`（`BlazorApp.Api/Services/React/ProductReactService.cs:290-291`、`371-373`），但返回给前端的 `ProductDto` 未映射 `UpdatedAt/UpdatedBy`（`ProductReactService.cs:120-137`、`175-192`）。
- 后端 DTO `BlazorApp.Shared/DTOs/ProductDto.cs:8-96` 目前未声明 `UpdatedAt/UpdatedBy` 字段。

## 实施方案
- 后端 DTO 扩展：在 `BlazorApp.Shared/DTOs/ProductDto.cs` 增加 `public DateTime? UpdatedAt { get; set; }`、`public string? UpdatedBy { get; set; }`。
- 后端服务映射：在 `ProductReactService.GetPagedListAsync` 的 `.Select(new ProductDto { ... })` 中加入 `UpdatedAt = p.UpdatedAt`、`UpdatedBy = p.UpdatedBy`；在 `GetByIdAsync` 返回的 `dto` 同样加入这两个字段。
- 更新人写入策略：在 `UpdateAsync` 与 `BatchUpdateAsync` 时为 `product.UpdatedBy` 赋值当前用户名（`HttpContext.User.Identity.Name`）。建议通过 `IHttpContextAccessor` 注入服务并统一取名；或在控制器获取后传入服务方法参数。
- 前端类型补齐：在 `ReactUmi/my-app/src/services/product.ts` 的 `ProductDto` 中加入 `updatedAt?: string; updatedBy?: string;`。
- 前端列展示：在 `index.tsx` 的 `columns` 增加两列：
  - `更新时间`：`dataIndex: 'updatedAt'`，`sorter: true`，`render: (text) => dayjs(text).format('YYYY-MM-DD HH:mm:ss')`。
  - `更新人`：`dataIndex: 'updatedBy'`，可作为普通文本列。
- 排序契约：前端对 `updatedAt` 列启用排序，服务端已有默认按 `UpdatedAt` 排序（`ProductReactService.cs:103-111`），当 `sortBy=updatedAt` 时走同一逻辑。

## 具体改动位置
- DTO：`BlazorApp.Shared/DTOs/ProductDto.cs` 新增属性；保持序列化为 ISO 字符串。
- 服务层：
  - 列表映射 `ProductReactService.cs:120-137` 增加 `UpdatedAt/UpdatedBy`。
  - 详情映射 `ProductReactService.cs:175-192` 增加 `UpdatedAt/UpdatedBy`。
  - 更新写入 `ProductReactService.cs:290-291` 补充 `product.UpdatedBy = currentUser`；批量更新 `ProductReactService.cs:371-373` 同样补充。
- 前端：
  - `ReactUmi/my-app/src/services/product.ts:23-39` 扩展 `ProductDto`。
  - `ReactUmi/my-app/src/pages/PosAdmin/ProductManagement/index.tsx:225-339` 在“状态”与“操作”之间新增两列。

## 时间与时区
- 后端返回时间将以 ISO 8601 序列化；前端用 `dayjs(text).format('YYYY-MM-DD HH:mm:ss')`，默认按浏览器本地时区显示，满足“本地时区”要求。
- 若后端将 `DateTime` 存为本地时间或 UTC（含 `Z`），`dayjs` 都会在显示时转换为本地时区。

## 验证要点
- 更新任意商品后，表格的“更新时间/更新人”应即时刷新显示。
- 排序：点击“更新时间”列头触发服务端排序，最新更新在前。
- 时区：跨时区客户端应显示其本地时间；格式统一为 `YYYY-MM-DD HH:mm:ss`。