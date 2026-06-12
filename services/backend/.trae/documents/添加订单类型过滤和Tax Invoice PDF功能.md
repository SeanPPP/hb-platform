## 1. 后端 API 修改

### 1.1 更新 DTO 和类型定义
- 在 `BlazorApp.Shared/DTOs/PosmSalesOrderDTO.cs` 添加 `OrderType` 字段
- 在 `BlazorApp.Shared/Models/PosmSalesOrder.cs` 添加订单类型枚举

### 1.2 创建 Tax Invoice PDF 服务
- 在 `BlazorApp.Api/Services/` 创建 `TaxInvoiceService`
- 实现生成 PDF 方法（使用 iTextSharp 或 PdfSharp）
- API 端点：`GET /api/PosmSalesOrder/tax-invoice/{orderGuid}`

### 1.3 更新控制器
- 在 `PosmSalesOrderController` 添加订单类型过滤参数
- 添加 `GenerateTaxInvoice` 端点返回 PDF

---

## 2. 前端实现

### 2.1 类型定义
- 在 `src/types/posmSalesOrder.ts` 添加 `OrderType` 枚举
- 更新 `PosmSalesOrderQueryParams` 添加 `orderType` 字段

### 2.2 服务层
- 在 `src/services/posmSalesOrder.ts` 添加：
  - `getTaxInvoicePdf(orderGuid: string)` - 获取 PDF blob

### 2.3 页面组件更新
- **过滤区域**：添加订单类型下拉选择器（普通订单、分期订单等）
- **表格列**：添加"操作"列，包含：
  - 预览小票按钮（打开 Modal 显示 PDF）
  - 导出 PDF 按钮（下载文件）
- **PDF 预览 Modal**：使用 iframe 或 react-pdf 预览 PDF
- **国际化**：在 locales 添加相关翻译

### 2.4 UI 组件
- 使用 Ant Design 的 `<Button>` 图标按钮（FilePdfOutlined、DownloadOutlined）
- PDF 预览使用 `<Modal>` + `<iframe>` 或 `@react-pdf/renderer`

---

## 3. 依赖安装
- 前端：`@react-pdf/renderer`（用于 PDF 预览，可选）
- 后端：`iTextSharp` 或 `QuestPDF`（用于生成 PDF）