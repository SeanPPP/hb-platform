## 问题
- 后端：`Product` 没有 `HBProductNo` 字段，实际为 `ItemNumber`。导致多处编译错误。
- 后端：在列过滤 `switch (col)` 中 `case "productName"` 后缺少 `break`，造成“case贯穿”错误。
- 前端：`Card` 的 `variant` 仅支持 `borderless | outlined`，传入 `elevated` 类型错误。

## 修复
1. 将 `HBProductNo` 全部改为 `ItemNumber`：
   - 全局搜索、列过滤、排序、`Select` 映射（列表DTO填充）
   - 文件：`BlazorApp.Api/Services/React/StoreRetailPriceReactService.cs`
2. 在 `switch (col)` 文本过滤分支中为每个 `case` 添加 `break`，尤其是 `productName` 与 `itemNumber`。
3. 前端 `Card` 改为 `variant="outlined"`。

## 验证
- 后端项目重新构建无编译错误；接口返回包含 `ItemNumber/Barcode`。
- 前端不再出现 `variant` 类型错误，页面正常渲染。