## 原因定位
- 当前使用 `POST /api/react/v1/products`（ReactProductController.Create），其入参 `CreateProductDto` 在 `BlazorApp.Shared/DTOs/ProductDto.cs` 中标注了 `[Required] ProductCategoryGUID`，导致前端未填或置空时 `ModelState.IsValid=false` 返回 400。
- 实体 `BlazorApp.Shared/Models/HBweb/Product.cs` 的 `ProductCategoryGUID` 是可空的（`string?`），说明业务允许空分类。

## 后端调整
- 放宽 DTO 校验：
  - 在 `CreateProductDto` 上删除 `ProductCategoryGUID` 的 `[Required]`，改为 `string? ProductCategoryGUID`。
  - 保留对 `ProductCode` 与 `ProductName` 的必填（必要时在 Controller 显式校验以给出更清晰的错误消息）。
- 无需修改 Service：`ProductReactService.CreateAsync` 已允许 `WarehouseCategoryGUID` 赋值为 `null`。

## 前端配合
- 容器页仍沿用“原商品编码”创建，`productCategoryGUID` 不传或传空字符串即可。
- 改进错误提示：捕获 Axios 错误时展示服务端返回 `response.data.message`，避免“error overlay 没有堆栈”导致定位困难。

## 变更文件
- `BlazorApp.Shared/DTOs/ProductDto.cs`：移除 `CreateProductDto.ProductCategoryGUID` 的 Required，类型改为可空。
- （可选增强）`BlazorApp.Api/Controllers/React/ReactProductController.cs`：在 Create 中对 `ProductCode` 与 `ProductName` 做显式校验并返回具体文案。
- 前端：`ReactUmi/my-app/src/pages/ContainerDetail/index.tsx` 的错误处理逻辑增加显示 `err.response?.data?.message`。

## 验证
- 重新执行“添加新商品”，后端不再因分类为空返回 400；创建成功后仓库/分店/多码链路保持原编码。
- 若仍有校验错误，前端弹出后端的具体 `message` 便于下一步修正。