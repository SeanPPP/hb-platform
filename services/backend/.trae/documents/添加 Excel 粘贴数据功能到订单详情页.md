## 实现计划：添加 Excel 粘贴数据功能

### 前端修改 - OrderDetails/index.tsx

1. **添加状态管理**
   - `pasteModalOpen`: 控制 Excel 粘贴弹窗显示
   - `pasteData`: 存储粘贴的原始文本
   - `parsedItems`: 存储解析后的商品数据
   - `columnMapping`: 列映射配置（货号列、数量列、价格列）
   - `previewData`: 预览数据（包含查询到的产品信息）

2. **在工具栏添加按钮**
   - 在 `toolBarRender` 中添加"从 Excel 粘贴"按钮
   - 图标使用 `FileTextOutlined` 或 `CopyOutlined`

3. **创建 Excel 粘贴 Modal**
   - 文本域：粘贴 Excel 数据
   - 列映射选择器：
     - 货号列（必选）
     - 数量列（可选，默认值 1）
     - 价格列（可选）
   - "解析数据"按钮
   - 数据预览表格（显示解析结果）
   - "确认添加"按钮

4. **实现数据解析逻辑**
   - 解析 Excel 粘贴的文本（按行和制表符 `\t` 分割）
   - 根据列映射提取货号、数量、价格
   - 调用 `getStoreOrderProducts` API 批量查询产品
   - 验证货号是否存在
   - 显示解析结果预览（包括产品图片、名称、价格等）

5. **添加到订单**
   - 使用现有的 `batchAddOrderLine` API
   - 格式：`{ productCode: string, quantity: number, importPrice?: number }[]`
   - 刷新订单详情

### 后端
- 无需修改，使用现有的 `batchAddOrderLine` API

### 数据格式示例
用户可以从 Excel 复制如下格式数据：
```
货号	数量	价格
ABC001	10	5.99
ABC002	20	3.50
ABC003	5	8.00
```