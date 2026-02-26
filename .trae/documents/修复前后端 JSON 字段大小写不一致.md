# 修复前后端数据字段大小写不一致问题

经过检查，发现前后端在 JSON 序列化方面存在不一致：

## 1. 问题分析

**后端配置 (`Program.cs`)**:
```csharp
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
```
后端明确配置了 `CamelCase` 命名策略。这意味着 `WarehouseCategoryDto` 中的 `CategoryName` 属性会被序列化为 `categoryName`。

**前端代码 (`CategorySidebar.tsx`)**:
```typescript
    return categories.map((cat) => ({
      title: cat.CategoryName, // 这里期望 PascalCase
      key: cat.CategoryGUID,   // 这里期望 PascalCase
      children: cat.Children ? transformToDataNode(cat.Children) : [],
    }));
```
前端代码直接使用 PascalCase 访问属性。

**结果**:
前端收到的 JSON 数据是 camelCase (例如 `categoryName`)，但代码尝试访问 PascalCase 属性 (例如 `CategoryName`)，导致取值为 `undefined`。因此分类树虽然有节点（根据数组长度），但没有标题和 Key，显示为空白。

## 2. 解决方案

**最佳实践**是前端适应后端的 camelCase 标准。这符合 JavaScript/TypeScript 的惯例。

### 需要修改的文件
1.  `src/services/warehouseCategory.ts`: 更新 TypeScript 接口定义，使其与后端返回的 JSON (camelCase) 匹配。
2.  `src/pages/StoreOrder/components/CategorySidebar.tsx`: 更新属性访问方式，使用 camelCase。

### 详细步骤

1.  **修改 `src/services/warehouseCategory.ts`**:
    将 `WarehouseCategoryDto` 接口中的属性名改为 camelCase。
    ```typescript
    export interface WarehouseCategoryDto {
      categoryGUID: string;
      parentGUID?: string;
      categoryName: string;
      chineseName?: string;
      isActive: boolean;
      sortOrder?: number;
      remarks?: string;
      children?: WarehouseCategoryDto[];
    }
    ```

2.  **修改 `src/pages/StoreOrder/components/CategorySidebar.tsx`**:
    更新 `transformToDataNode` 函数中的属性访问。
    ```typescript
      title: cat.categoryName,
      key: cat.categoryGUID,
      children: cat.children ? transformToDataNode(cat.children) : [],
    ```

## 3. 验证计划
*   **代码修改**：执行上述两个文件的修改。
*   **验证**：刷新页面，查看分类树是否正常显示内容。

**注意**：如果有其他前端组件也使用了 `WarehouseCategoryDto`，也需要一并检查和修改。我会先搜索一下该接口的使用情况。
