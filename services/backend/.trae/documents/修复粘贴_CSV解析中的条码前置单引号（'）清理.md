## 问题
- Excel 为保留条码前导零，常在单元格前加 `'`。当前解析未清理该字符，导致条码包含 `'`。

## 修改点（前端创建页）
- 文件：`ReactUmi/my-app/src/pages/PosAdmin/LocalSupplierInvoiceCreate/index.tsx`
- 在粘贴与CSV导入的字段处理处对条码做清理：
  - 定义 `sanitizeBarcode(raw)`：
    - 去除开头连续的单引号：`raw.replace(/^'+/, '')`
    - 去除包裹的双引号：`raw.replace(/^"+|"+$/g, '')`
    - 再 `trim()`，保留前导零与数字字符串。
  - 在 `parsePasted` 解析条码时应用 `sanitizeBarcode`。
  - 在 `onImportCsv` 的条码赋值时应用 `sanitizeBarcode`。
- 在判断旧格式“名称/条码”时使用清理后的值判断：
  - `const cleaned = sanitizeBarcode(nameOrBarcode)` 后将 `isLikelyBarcode(cleaned)` 作为判断依据。

## 验证
- 用示例行：
  - `MED-061\t1Pce Waterproof Tape 4.5m\t'9357405021583\t0\t1.31`
  - 解析结果条码应为 `9357405021583`（无 `'`）。
- 不影响其他逻辑：数量与价格校验规则保持不变，排序/高亮/统计正常。

## 影响范围
- 仅前端解析逻辑调整，后端无需改动。