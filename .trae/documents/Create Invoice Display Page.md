# Invoice 页面开发计划

我将为分店订货单创建一个 Invoice 展示页面，包含导出和邮件发送功能。

## 1. 创建 Invoice 组件
- **文件**: `src/pages/StoreOrder/Invoice/index.tsx`
- **样式**: `src/pages/StoreOrder/Invoice/index.less`
- **路由**: 在 `OrderDetails` 页面添加跳转按钮，路径为 `/store-order/invoice/:id`。
- **功能**:
  - 调用 `getOrderDetail` 获取订单详情。
  - 调用 `getStores` (通过 `storeCode` 搜索) 获取分店详情（地址、电话等）。
  - **界面布局**: 严格按照发票格式渲染：
    - **页头**: Hot Bargain Logo、仓库地址、ABN、联系方式、Invoice No、Date、客户信息（分店名称、地址、电话）。
    - **商品列表**: 表格展示 Item No、Barcode (图片)、Name、Cost、Qty、Subtotal。
    - **页脚**: Sub-Total、GST、Freight、Total、付款信息（Bank Info）。
  - **地址编辑**: 如果分店信息中缺失地址，提供简单的编辑功能以便在打印前补充。

## 2. 实现导出功能
- **Excel 导出**:
  - 使用 `xlsx` 库（如果项目中未安装，将使用 CSV 或建议安装）。
  - 导出包含发票主要数据的表格。
- **PDF 导出**:
  - 通过 **浏览器打印** (`window.print()`) 实现。
  - 编写 `@media print` CSS 样式，隐藏页面无关元素（导航栏、按钮等），确保 A4 纸张布局完美。

## 3. 实现邮件发送功能
- **UI**: 添加“发送邮件”按钮。
- **交互**:
  - 弹出模态框确认接收邮箱（默认预填分店邮箱）。
  - 由于后端未配置 SMTP，将采用 **`mailto:`** 方式，点击后自动唤起本地邮件客户端，并自动填充主题（Invoice #单号）和正文。
  - *说明*: 带附件的自动发送需要后端支持，当前方案为最可行的前端实现。

## 4. 入口集成
- 在 `OrderDetails` (订单详情页) 的工具栏中添加 "View Invoice" 按钮，点击跳转到新页面。

## 验证
- 验证页面数据加载正确。
- 验证打印预览样式符合发票要求。
- 验证 Excel 导出功能。
- 验证邮件按钮能否唤起邮件客户端。
