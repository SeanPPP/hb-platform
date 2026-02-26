## 页面目标
- 在 `ReactUmi/my-app` 中新增“满减/固定组合价”促销管理页面（含列表、创建/编辑、启停、删除、评估）。
- 路由遵循 Umi 约定式：`src/pages/PosAdmin/Promotions/index.tsx` → `/posadmin/promotions`。

## 数据模型与后端接口对接
- 类型：复用/引入 `src/types/promotion.ts`（PromotionListDto/DetailDto/Create/Update/Evaluate 等）。
- 服务文件：新增 `src/services/promotionService.ts`
  - `GET/POST /api/react/v1/promotions/grid` 列表分页
  - `GET /api/react/v1/promotions/{id}` 详情
  - `POST /api/react/v1/promotions` 创建
  - `PUT /api/react/v1/promotions/{id}` 更新
  - `DELETE /api/react/v1/promotions/{id}` 删除
  - `POST /api/react/v1/promotions/{id}/enable?enable=bool` 启停（含排他冲突校验）
  - `POST /api/react/v1/promotions/evaluate` 评估购物车（后续用于结算页）
- 分店/商品选择：
  - 分店：复用 `storeRetailPriceService.getActiveStores()` 提供下拉选项
  - 商品：提供两种方式（可同时支持）：
    - 直接输入 `productCode`（Form.List 行输入 + 校验）
    - 搜索选择：调用 `product.getProducts({ search })` 弹窗选择商品，写入 `productCode` 与 `unitWeight`

## 页面结构与交互
- 列表页 `src/pages/PosAdmin/Promotions/index.tsx`
  - 顶部筛选：
    - `storeCode` 下拉（allowClear、showSearch）
    - `keyword` 输入（按名称模糊）
    - 查询/重置按钮
  - 表格列：
    - 名称、启用 `Switch`（切换调用 enable 接口）
    - 排他 `Tag`（是/否）、优先级 `priority`
    - 生效开始/结束时间
    - 门槛件数 `applyQuantity`、固定总价 `fixedPrice`
    - 商品数 `productsCount`、分店数 `storesCount`
    - 操作：编辑、删除（`Popconfirm`）
  - 分页/排序：与 `PricingStrategies` 页面一致（`GridRequestDto`、`SortModelDto`）
- 编辑弹窗（Modal + Form，宽 900）
  - 基本字段：`name`、`description`、`isEnabled`、`isExclusive`、`priority`
  - 时间窗口：`effectiveStart`、`effectiveEnd`（`DatePicker` 范围或两个 `DatePicker`）
  - 规则字段：`applyQuantity`（≥2）、`fixedPrice`（decimal）、`maxApplicationsPerOrder`（可空）
  - 作用范围：分店多选（`Select mode=multiple`，源自 `getActiveStores`）
  - 商品集合：`Form.List` 每行含 `productCode`（输入/选择）与 `unitWeight`（默认 1）
  - 校验与保存：
    - 创建：`POST /promotions`
    - 更新：`PUT /promotions/{id}`（编辑态）
    - 成功后关闭弹窗并刷新列表
- 删除：`DELETE /promotions/{id}` 成功后刷新列表
- 启停：表格内 `Switch` → `POST /promotions/{id}/enable?enable=true|false`，失败时回滚开关状态并 `message.error`

## 评估联调（可选）
- 在页面底部提供“促销评估”卡片：
  - 表单：`storeCode`、购物车明细 `items[{productCode, qty, unitPrice}]`
  - 调用 `POST /promotions/evaluate`，展示 `appliedPromotions`、`totalDiscount` 与逐项 `adjustedUnitPrice`

## 代码位置与文件清单
- `src/services/promotionService.ts`：封装促销 API 调用（复用 `@/utils/request` 请求封装）
- `src/pages/PosAdmin/Promotions/index.tsx`：主页面
- `src/pages/PosAdmin/Promotions/ProductPicker.tsx`（可选）：商品搜索选择组件（调用 `product.getProducts`）

## 设计与样式
- 使用 Ant Design 组件（Form/Table/Modal/Select/Switch/DatePicker/Tag/Space 等），风格与 `PricingStrategies` 保持一致。
- 表单校验：必填项（name、时间窗口、applyQuantity≥2、fixedPrice>0、至少一个门店与一个商品）。

## 验证与自测
- 打包编译无错误
- 列表分页/排序/筛选正常
- 新建/编辑/删除/启停流程正常，排他冲突由后端返回 `exclusive_conflict` 时展示错误提示
- 评估功能返回 `adjustedItems` 与 `totalDiscount` 正确展示

## 交付内容
- 完整前端页面、服务封装与（可选）商品选择组件
- 接入现有登录鉴权（基于 `@/utils/request` Token 拦截器）
- 文档化使用方式（路由、入口菜单说明）