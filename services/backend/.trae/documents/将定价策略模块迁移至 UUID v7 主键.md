# 修改定价策略模块使用 UUID v7 主键

**原因分析**
用户要求将 `PricingStrategy` 及其相关模型的主键从自增 `int` 修改为 `string` 类型的 UUID v7，以符合系统其他模块的规范，并支持分布式生成 ID。

**修改方案**

1.  **修改数据模型 (`BlazorApp.Shared/Models/HBweb`)**
    *   `PricingStrategy.cs`: 将 `Id` 类型改为 `string`，移除 `IsIdentity = true`。
    *   `PricingStrategyDetail.cs`: 将 `Id` 和 `StrategyId` 类型改为 `string`，移除 `IsIdentity = true`。
    *   `PricingStrategyTarget.cs`: 将 `Id` 和 `StrategyId` 类型改为 `string`，移除 `IsIdentity = true`。

2.  **修改数据传输对象 (`BlazorApp.Shared/DTOs/PricingStrategyReactDtos.cs`)**
    *   `PricingStrategyListDto`: `Id` 改为 `string`。
    *   `PricingStrategyDetailDto`: `Id` 改为 `string`。
    *   `PricingStrategyRuleDto`: `Id` 改为 `string?`。
    *   `PricingStrategyTargetDto`: `Id` 改为 `string?`。

3.  **修改接口 (`BlazorApp.Api/Interfaces/React/IPricingStrategyReactService.cs`)**
    *   `GetAsync(int id)` -> `GetAsync(string id)`
    *   `DeleteAsync(int id)` -> `DeleteAsync(string id)`
    *   `UpdateAsync(int id, ...)` -> `UpdateAsync(string id, ...)`

4.  **修改服务实现 (`BlazorApp.Api/Services/React/PricingStrategyReactService.cs`)**
    *   在 `CreateAsync` 中，使用 `UuidHelper.GenerateUuid7()` 生成主键。
    *   在插入明细和目标时，同样生成 UUID。
    *   更新所有方法的参数类型为 `string`。
    *   修改查询逻辑以匹配新的主键类型。

5.  **修改自动定价服务 (`BlazorApp.Api/Services/Pricing/AutoPricingService.cs`)**
    *   修改 `FindStrategyAsync` 等方法中涉及 `StrategyId` 的逻辑，适配 `string` 类型。

6.  **修改控制器 (`BlazorApp.Api/Controllers/React/ReactPricingStrategiesController.cs`)**
    *   更新所有 Action 的参数类型从 `int` 为 `string`。

**依赖工具**
使用 `BlazorApp.Shared.Helper.UuidHelper.GenerateUuid7()` 生成 ID。

**注意**
由于这是一次破坏性修改（修改主键类型），现有的定价策略数据（如果存在）将不再兼容。考虑到是开发环境，将直接删除并重新创建表。