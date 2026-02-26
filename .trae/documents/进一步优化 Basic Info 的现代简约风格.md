我将对 Order Details 页的 Basic Info 区域进行二次风格优化，使其更现代、简约、信息层次更清晰，同时保持可编辑功能。

### 设计目标
- 去装饰化：边框和分隔线最小化，突出关键数字与信息。
- 强层次：标签弱化、数值强化、标题与副标题对比明显。
- 更好排版：响应式多列布局，移动端一列、桌面端 4 栏。
- 操作友好：可编辑控件采用无边框/紧凑模式，减少视觉噪音。

### 前端实现（仅修改 `OrderDetails/index.tsx`）
- 保留 `Card` 包裹，但提升视觉层次：标题左对齐、订单号展示为 Tag，整体使用 borderless。
- 将 `ProDescriptions` 调整为响应式列配置 `column={{ xs:1, sm:2, md:4 }}`，减少密集感。
- 关键指标展示优化：
  - 使用更醒目的 Typography 强化数值（Total Amount 采用主题主色）。
  - 日期右侧使用 `DatePicker` 的无边框样式（bordered=false），紧凑排列。
  - 统计项（Order Qty / Send Qty / SKU / Volume）保持一行四列，标签弱化、数值加粗。
- 运费区域：保留一行，`InputNumber` 使用无边框（bordered=false），对齐其他信息的风格。
- Address 与 Remarks 下移到 Basic Info 的最后，Remarks 的保存按钮使用 `type="link"` 并与输入框同一行右侧，减少按钮存在感。
- 引入轻量样式：
  - 标签颜色使用 `colorTextSecondary`，数值使用 `colorText`。
  - 通过少量 inline style 达成（避免新增全局样式），必要时新增本地 `OrderDetails.less` 以定义 `.basic-info-card` 的圆角与间距（可选）。

### 交互与可编辑
- `DatePicker` 与 `InputNumber` 保持当前数据绑定与保存逻辑，保存时与 Remarks 同时提交（已在现有逻辑中）。
- 不显示 Store Code，仅显示店铺名称。

### 验证
- 代码修改后，检查 Basic Info 在不同宽度下的布局是否为 1/2/4 列自适应。
- 确认数值与标签的对比度明显，按钮与输入控件风格统一、简约。

说明：本次仅涉及前端风格与布局优化，不变更后端接口。

涉及文件：
- `ReactUmi/my-app/src/pages/StoreOrder/OrderDetails/index.tsx`