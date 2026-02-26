## 修改计划

### 1. 修改后端 DTO (`BlazorApp.Shared\DTOs\StoreOrderDtos.cs`)
- 为 `ProductQuantityDto` 类添加 `ImportPrice` 可选属性

### 2. 修改后端服务 (`BlazorApp.Api\Services\React\StoreOrderReactService.cs`)
- 在 `BatchAddOrderLineAsync` 方法的 `AddOrUpdateDetailAsync` 调用中，传递 `item.ImportPrice` 而不是 `null`

### 3. 修改前端服务类型 (`ReactUmi\my-app\src\services\storeOrder.ts`)
- 更新 `batchAddOrderLine` 函数的类型定义，在 items 中添加 `importPrice?: number`