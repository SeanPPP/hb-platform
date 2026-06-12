## 问题原因
- 后端 `POST /api/react/v1/store-retail-prices/grid` 控制器返回的数据结构为 `data = { items, total }`（小写键），见 `BlazorApp.Api/Controllers/React/ReactStoreRetailPricesController.cs:22-47`。
- 前端页面解析使用大写键：`res.data?.data?.Items` 与 `Total`，见 `ReactUmi/my-app/src/pages/PosAdmin/StoreRetailPrices/index.tsx:159-166`。
- 结果：即使服务层查询返回了数据（`StoreRetailPriceReactService.cs:426` 使用 `GridResponseDto.OK(items, total)`），因为控制器包装时改成小写键，前端读取不到，列表为空、总数为 0。

## 方案 A（推荐，后端统一）
- 修改 `ReactStoreRetailPricesController.Grid` 的返回结构为大写键：`data = { Items = result.Items, Total = result.Total }`，或直接返回 `GridResponseDto<StoreRetailPriceListDto>`，保持与共享 DTO 一致（`BlazorApp.Shared/DTOs/GridRequestDto.cs:97-146` 中 `Items/Total`）。
- 不需要改前端；保持现有解析逻辑与类型 `GridResponse<T>`（大写键）一致，见 `ReactUmi/my-app/src/types/storeRetailPrice.ts:23-28`。
- 验证：加载页面后表格数据与分页总数正确显示；排序与筛选维持正常（`StoreRetailPriceReactService.cs:184-250` 排序，`107-180` 过滤）。

## 方案 B（前端快速修复）
- 将页面解析改为小写键：`const items = res.data?.data?.items || []; const totalVal = res.data?.data?.total || 0;`，见 `index.tsx:164-166`。
- 如需严格类型，调整 `GridResponse<T>` 为小写键或在该页使用局部类型绕过。
- 验证：页面可正常显示数据与总数。

## 选择建议
- 选择方案 A，以保持与共享 DTO 命名一致，避免其它页面或未来代码重复出现同类问题；影响面小、改动集中于单个控制器方法。

## 验证步骤
- 启动前端页面并触发数据加载，确认表格渲染正常、总数正确。
- 执行一次排序与筛选（例如按 `StoreName` 升序与关键字过滤），确认服务端返回与前端显示一致。

## 回退与兼容
- 若暂时担心其它消费者依赖小写键，可在控制器过渡期双写：`data = { Items = ..., Total = ..., items = ..., total = ... }`，待前端全面统一后移除。