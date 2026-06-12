# 修复 `WarehouseCategoryDto` 字段大小写不一致问题

已确认前端代码中存在对 `WarehouseCategoryDto` 的 PascalCase 引用，但后端返回的数据（受 `Program.cs` 配置影响）是 camelCase。

## 1. 问题确认
*   `src/services/warehouseCategory.ts`: 定义为 PascalCase (`CategoryName`, `CategoryGUID` 等)。
*   `src/pages/StoreOrder/components/CategorySidebar.tsx`: 使用 PascalCase 访问属性。
*   后端: 默认使用 `CamelCase` 命名策略。

## 2. 解决方案
将前端代码统一调整为 camelCase，以匹配后端返回的 JSON 数据。

### 涉及文件及修改内容

1.  **`src/services/warehouseCategory.ts`**:
    *   修改 `WarehouseCategoryDto` 接口定义，将属性首字母改为小写。
    *   修改 `CreateWarehouseCategoryDto` 接口定义（及其子接口），属性改为小写。
    *   修改 `WarehouseCategoryFilterDto` 接口定义，属性改为小写。
    *   注意：需要检查 `PagedResult` 中的 `Items`, `Total`, `Page`, `PageSize` 是否也受 camelCase 策略影响（通常是的）。

2.  **`src/pages/StoreOrder/components/CategorySidebar.tsx`**:
    *   修改属性访问，使用新的 camelCase 属性名（如 `cat.categoryName`, `cat.categoryGUID`）。

3.  **其他可能受影响的文件**:
    *   虽然搜索结果没有直接显示其他文件使用了 `WarehouseCategoryDto`，但在修改接口定义后，TypeScript 编译器（或编辑器检查）会报错，如果有其他引用点，会在编译时发现。如果有，我会一并修复。

## 3. 执行步骤

1.  修改 `src/services/warehouseCategory.ts` 中的接口定义。
2.  修改 `src/pages/StoreOrder/components/CategorySidebar.tsx` 中的代码逻辑。
3.  验证修复效果。

注意：后端请求参数（Filter DTOs）在 ASP.NET Core 中通常不区分大小写（Model Binding 会自动处理），所以修改前端 DTO 为 camelCase 发送请求通常也是安全的。但为了保险起见，如果后端 DTO 是 PascalCase，前端发送 camelCase JSON，ASP.NET Core 也能正确解析。

**特别注意**:
`PagedResult<T>` 的定义中，`Items` 等字段在后端如果是 PascalCase，序列化后也是 camelCase。前端接口也应该改为 camelCase。

```typescript
export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}
```

我将一次性更新这些接口定义和使用点。
