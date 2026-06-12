# 现代化订货页面开发计划 (单购物车逻辑 + 独立接口)

本计划旨在开发一个全新的订货页面，采用 **左侧分类 + 右侧商品网格** 布局，并实现 **单购物车 (FlowStatus=0)** 逻辑。所有后端逻辑将封装在全新的控制器和服务中，不影响现有系统。

## 1. 后端开发 (Backend) - 全新独立模块

### 1.1 新增 DTOs
*   **文件**: `BlazorApp.Shared/DTOs/StoreOrderDtos.cs`
*   **内容**:
    *   `StoreOrderProductDto`: 商品信息 + 仓库价格/库存/起订量。
    *   `StoreOrderFilterDto`: 查询参数 (仅含 `ItemNumber` 搜索, `CategoryGUID`, 分页)。
    *   `StoreOrderCartDto`: 购物车信息 (包含 `OrderGUID`, `TotalAmount` 等)。
    *   `AddToCartRequestDto`: 添加购物车请求。

### 1.2 新增服务层 (Service)
*   **接口**: `BlazorApp.Api/Interfaces/React/IStoreOrderReactService.cs`
*   **实现**: `BlazorApp.Api/Services/React/StoreOrderReactService.cs`
*   **核心方法**:
    *   `GetPagedListAsync`:
        *   联表查询 `Product` 和 `WarehouseProduct`。
        *   **搜索**: 仅支持 `ItemNumber` 精确/模糊匹配。
        *   **筛选**: 支持左侧分类树筛选。
    *   `GetActiveCartAsync(string storeCode)`:
        *   查询该分店下 `FlowStatus = 0` (购物车) 的订单。
        *   若存在，返回订单及明细；若不存在，返回空。
    *   `AddToCartAsync`:
        *   检查该分店是否有 `FlowStatus = 0` 的订单。
        *   **无**: 创建新订单 (`WareHouseOrder`, FlowStatus=0)。
        *   **有**: 使用现有订单。
        *   在 `WareHouseOrderDetails` 中添加或更新商品数量。
    *   `SubmitOrderAsync`:
        *   将订单 `FlowStatus` 从 `0` 更新为 `1` (审核中)，从而生成正式订单并清空购物车状态。

### 1.3 新增控制器 (Controller)
*   **文件**: `BlazorApp.Api/Controllers/React/ReactStoreOrderController.cs`
*   **路由**: `/api/react/v1/store-order`
*   **API 端点**:
    *   `POST /products`: 获取商品列表。
    *   `GET /cart/{storeCode}`: 获取当前购物车。
    *   `POST /cart/add`: 添加到购物车。
    *   `POST /cart/submit`: 提交订单。

## 2. 前端开发 (Frontend)

### 2.1 页面结构
*   **布局**: 经典的电商布局 (左侧 Tree, 右侧 Grid)。
*   **搜索**: 顶部搜索框仅用于输入 `Item Number`。

### 2.2 服务层
*   **文件**: `src/services/storeOrder.ts`
*   **功能**: 封装上述所有新 API。

### 2.3 页面组件 (`src/pages/StoreOrder/`)
*   `index.tsx`: 页面入口，状态管理。
*   `components/CategorySidebar.tsx`: 分类树。
*   `components/ProductGrid.tsx`: 商品列表，包含搜索栏和分页。
*   `components/ProductCard.tsx`: 单个商品展示，含 "Add" 按钮。
*   `components/CartDrawer.tsx`: 右侧购物车抽屉，显示当前 `FlowStatus=0` 的订单明细。

## 3. 实施步骤

1.  **后端**: 创建 DTOs 和 Service 接口/实现。
2.  **后端**: 创建 Controller 并注册服务。
3.  **前端**: 配置路由和 Service。
4.  **前端**: 开发 UI 组件并对接接口。
