## 修改方案：按粘贴数量添加，不累加

### 需要修改 `handleParsePasteData` 函数

**当前行为**：
- 解析后对相同货号进行去重合并
- 相同货号的数量会累加
- 预览表格只显示唯一货号

**修改为**：
- 不对相同货号进行合并
- 每个粘贴行独立处理
- 预览表格显示所有行（包括相同货号）

### 修改 `handleConfirmPasteAdd` 函数

**当前行为**：
- 使用 `previewData`（去重后的数据）添加到订单

**修改为**：
- 使用 `parsedItems`（原始解析数据）添加到订单
- 完全按照粘贴的数量添加

### 修改位置
- 文件：`d:\Development\cline\blazor\ReactUmi\my-app\src\pages\StoreOrder\OrderDetails\index.tsx`
- 函数：`handleParsePasteData`（第 371-456 行）
- 函数：`handleConfirmPasteAdd`（第 458-479 行）