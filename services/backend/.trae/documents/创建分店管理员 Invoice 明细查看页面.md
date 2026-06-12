## 实施计划

在 `d:\Development\cline\blazor\ReactUmi\my-app\src\pages\PosAdmin\StoreInvoiceView\` 目录下创建一个新的只读查看页面。

### 页面功能
1. **进货单基础信息卡片**
   - 分店名称
   - 供应商名称
   - 随货单号
   - 订单日期、入库日期
   - 总金额
   - 入库状态
   - 备注

2. **商品明细表格**
   - 序号
   - 货号
   - 商品名称
   - 条码
   - 规格
   - 单位
   - 数量
   - 进货价
   - 金额
   - 零售价

3. **页面特性**
   - 只读模式，无编辑功能
   - 返回按钮返回列表页
   - 使用 Ant Design 组件保持风格一致
   - 支持表格排序、分页
   - 响应式布局

### 实现细节
- 使用 `useParams` 获取 invoiceGuid
- 复用现有的 `getInvoiceByGuid` 和 `getInvoiceDetails` API
- 使用 `Card` 和 `Descriptions` 展示基础信息
- 使用 `Table` 展示商品明细
- 参考现有页面的样式和布局