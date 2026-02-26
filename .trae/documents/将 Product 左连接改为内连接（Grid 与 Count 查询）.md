## 意图
- 在 `StoreRetailPriceReactService.cs` 的网格与计数查询中，将 `Product` 的左连接改为内连接，保证仅返回有匹配商品的数据行。

## 范围
- 文件：`BlazorApp.Api/Services/React/StoreRetailPriceReactService.cs`
  - 网格查询构建处：`Line 37`
  - 计数查询构建处：`Line 250`

## 修改
- 把 `.LeftJoin<Product>((p, prod) => p.ProductCode == prod.ProductCode)` 改为 `.InnerJoin<Product>((p, prod) => p.ProductCode == prod.ProductCode)`（两个位置）。
- 其它连接（`HBLocalSupplier`、`Store`）仍保持左连接不变。

## 影响
- 无 `Product` 记录的价格行将被过滤，不再出现在结果与计数中。
- 与 `prod.*` 的访问将更安全（不再为空）。

## 验证
- 构建项目，确认无编译错误。
- 基本检查：筛选、排序、分页仍正常，网格与计数一致。

确认后我将按上述两处替换并执行编译验证。