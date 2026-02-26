## 目标
- 在“分店零售价”网格数据中新增 `IsSpecialProduct` 列（来源于 `Product.IsSpecialProduct`），支持显示/排序/筛选，并提供批量编辑接口以切换该值。

## 后端改动
### 1) DTO 扩展
- 文件：`BlazorApp.Shared/DTOs/StoreRetailPriceDtos.cs`
  - 在 `StoreRetailPriceListDto` 新增字段：`public bool? IsSpecialProduct { get; set; }`

### 2) 列表查询投影
- 文件：`BlazorApp.Api/Services/React/StoreRetailPriceReactService.cs`
  - `.Select((p, prod, sup, st) => new StoreRetailPriceListDto { ... })` 中加入 `IsSpecialProduct = prod.IsSpecialProduct`
  - 排序映射：在 `switch (s.ColId)` 中新增 `case "isSpecialProduct"` → 按 `prod.IsSpecialProduct` 排序
  - 过滤：在 `type == "set"` 分支新增 `case "isSpecialProduct"`，与 `isActive/isAutoPricing` 类似，解析 `true/false` 集合并过滤

### 3) 批量编辑接口
- 文件：`BlazorApp.Api/Interfaces/React/IStoreRetailPriceReactService.cs`
  - 新增方法签名：`Task<ApiResponse<bool>> BatchUpdateSpecialFlagAsync(List<string> productCodes, bool isSpecial, string updatedBy)`
- 文件：`BlazorApp.Api/Services/React/StoreRetailPriceReactService.cs`
  - 实现：开启事务，`Updateable<Product>().SetColumns(x => x.IsSpecialProduct == isSpecial).Where(x => productCodes.Contains(x.ProductCode)).ExecuteCommandAsync()`，提交事务、错误回滚与日志
- 文件：`BlazorApp.Api/Controllers/React/ReactProductSetCodesController.cs` 或新增专属控制器
  - 提供路由：`[HttpPut("batch-special")]`，接收 DTO（产品编码列表+目标值），调用服务方法，返回统一响应

## 前端改动（ReactUmi）
- 文件：`ReactUmi/my-app/src/types/storeRetailPrice.ts`
  - 在类型中加 `isSpecialProduct?: boolean`
- 文件：`ReactUmi/my-app/src/pages/PosAdmin/StoreRetailPrices/index.tsx`
  - 列定义中新增 `IsSpecialProduct` 列（勾选框），支持排序与过滤（set 过滤 true/false）
  - 批量勾选后触发新服务方法（`storeRetailPriceService.batchUpdateSpecialFlag`）更新后刷新网格
- 文件：`ReactUmi/my-app/src/services/storeRetailPriceService.ts`
  - 新增 API 方法，调用后端 `batch-special` 路由

## 验证
- 构建后端与前端，打开页面，查看 `IsSpecialProduct` 列已显示
- 测试排序、过滤、分页一致性
- 测试批量编辑接口：选择若干行→批量设为特殊产品/取消→刷新结果验证

## 注意
- 若仅需显示不编辑，可省略批量接口与前端编辑逻辑。
- 若编辑范围应限定在当前分店，需根据 `StoreCode` 限制更新集合；此处按产品维度更新（与字段归属一致）。

确认后我将按上述步骤实施修改并进行编译与功能验证。