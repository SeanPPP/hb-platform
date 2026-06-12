## 问题与原因
- 接口实现错误：`PricingStrategyReactService` 未实现接口成员（GetByIdAsync/UpdateAsync/DeleteAsync），因为接口签名使用 `int id` 而实现使用 `string id`（UUID）导致类型不匹配。
  - 接口位置：`d:/Development/cline/blazor/BlazorApp.Api/Interfaces/React/IPricingStrategyReactService.cs:8-17`
  - 实现位置：`d:/Development/cline/blazor/BlazorApp.Api/Services/React/PricingStrategyReactService.cs:150, 309`
- 控制器路由不匹配：`ReactPricingStrategiesController` 使用 `{id:int}` 路由约束，前端与后端实体均使用字符串UUID，导致无法命中路由。
  - 控制器位置：`d:/Development/cline/blazor/BlazorApp.Api/Controllers/React/ReactPricingStrategiesController.cs:30-60`
- DTO 警告：`PricingStrategyReactDtos.cs` 的 `Id` 为非可空字符串，但未初始化，编译器发出“退出构造函数时不可为 null 的属性必须包含非 null 值”的警告。
  - DTO 文件：`d:/Development/cline/blazor/BlazorApp.Shared/DTOs/PricingStrategyReactDtos.cs`

## 解决方案
- 更新接口签名为字符串ID
  - 将 `GetByIdAsync(int id)` → `GetByIdAsync(string id)`
  - 将 `UpdateAsync(int id, UpdatePricingStrategyDto dto)` → `UpdateAsync(string id, UpdatePricingStrategyDto dto)`
  - 将 `DeleteAsync(int id)` → `DeleteAsync(string id)`
- 更新控制器路由与参数类型
  - `HttpGet`/`HttpPut`/`HttpDelete` 的路由从 `"{id:int}"` 改为 `"{id}"`，方法参数从 `int id` 改为 `string id`，其余保持不变。
- 修复DTO非空警告
  - 为 `PricingStrategyListDto.Id` 与 `PricingStrategyDetailDto.Id` 添加默认初始化：`= string.Empty;`，或使用 `required` 修饰符（二选一，推荐默认值以保持简单）。

## 验证步骤
- 重新构建后端并启动服务。
- 访问 `GET /api/react/v1/pricing-strategies/{uuid}`（例如你提供的 `efbc38c7-61f5-87ab-8237-019b1083caf8`），应返回包含规则与目标。
- 访问 `POST /api/react/v1/pricing-strategies/grid`，检查列表中 `StoreCodes/SupplierCodes` 标签与 `DetailsCount/TargetsCount` 为非零且符合数据库记录。
- 前端已统一为字符串ID，无需额外改动，页面登录后验证编辑弹窗与列表展示。

## 影响面与风险
- 接口签名与控制器路由的统一将彻底消除类型不匹配错误，不影响返回结构。
- 变更为字符串ID后，与现有数据库UUID主键保持一致，避免后续类型转换问题。
- DTO初始化仅消除编译警告，无行为改动。