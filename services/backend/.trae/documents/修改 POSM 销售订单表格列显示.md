1. **添加行号列**：在 columns 数组最前面添加行号列配置
2. **修改订单号列**：

   * 将 `substring(0, 12)` 改为 `slice(-6)` 显示最后6位

   * 添加 `onRow` 点击事件或单独的点击处理函数

   * 添加 Modal 组件用于显示二维码

   * 引入 `Modal` 组件和 `QRCode` 组件（需要安装 `qrcode.react`）
3. **修改分店名称列**：

   * 将显示逻辑改为只显示 `branchName`，去掉 `branchCode` 备选

涉及的文件：

* `ReactUmi/my-app/src/pages/PosmSalesOrders/index.tsx`

* 可能需要安装依赖：`npm install qrcode.react`

