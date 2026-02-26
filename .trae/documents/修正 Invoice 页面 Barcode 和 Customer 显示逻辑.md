# 发票页面优化计划 (包含 A4 打印适配)

我将更新发票页面以包含条码、Logo、分店名称，并优化打印样式为 A4 大小及添加页脚。

## 后端修改
1.  **更新 DTO**: 在 `StoreOrderCartItemDto` 中添加 `Barcode` 属性。
2.  **更新服务**: 修改 `GetOrderDetailAsync` 映射 `Product.Barcode`。

## 前端修改
1.  **更新接口**: 更新 TS 接口包含 `barcode`。
2.  **安装依赖**: 安装 `react-barcode`。
3.  **资源**: 创建/使用 `src/assets/logo.svg`。
4.  **发票页面 (`index.tsx`)**:
    *   显示 Logo 和 `react-barcode`。
    *   显示分店名称。
    *   添加页脚组件：显示当前打印时间。
5.  **打印样式 (`index.less`)**:
    *   设置 `@page { size: A4; ... }` 确保 A4 布局。
    *   添加页脚样式，使其在打印时固定在页面底部 (利用 `position: fixed; bottom: 0` 或 `table-footer-group` 重复显示)。
    *   *注意*: "Total Pages" (总页数) 在纯 HTML 打印中通常由浏览器的打印对话框（页眉/页脚选项）自动处理（显示为 "1/5"）。我将在自定义页脚中包含日期，页码建议使用浏览器自带功能以获得最准确的总页数。

## 验证
*   检查条码和 Logo 显示。
*   使用浏览器的打印预览验证 A4 布局和页脚日期。
