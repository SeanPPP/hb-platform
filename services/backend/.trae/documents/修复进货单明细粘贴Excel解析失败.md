## 目标
- 当粘贴解析失败时，提供清晰、可操作的友好提示，包含失败原因摘要与正确格式示例，避免仅显示“未解析到有效数据”。

## 代码位置
- 解析与提示入口：`ReactUmi/my-app/src/pages/PosAdmin/LocalSupplierInvoiceDetail/index.tsx`。
- 失败提示当前触发：`LocalSupplierInvoiceDetail/index.tsx:968`（`messageApi.warning('未解析到有效数据')`）。
- 解析函数：`parsePasted(raw)` 于 `LocalSupplierInvoiceDetail/index.tsx:117-166`。

## 方案
1. 扩展 `parsePasted` 返回结构
- 从 `ParsedItem[]` 改为 `{ items: ParsedItem[], errors: ParseError[] }`，其中 `ParseError` 包含：`lineNo`、`reason`（如“列数不足/分隔符不识别/数量不合法/进货价不合法/空行/含表头”等）、`rawLine`。
- 在原有校验点收集错误：
  - 列数 `<6`（`index.tsx:122`）→ `reason: '列数不足'`
  - 数量与进货价校验失败（`index.tsx:129-132`）→ `reason: '数值不合法'`
  - 行为空（`index.tsx:118` 过滤后）→ `reason: '空行'`
  - 检测到常见表头关键字 → `reason: '表头行'`（自动跳过但记录）

2. 优化弹窗确认逻辑（`onOk`，`index.tsx:964-981`）
- 使用新结构：`const { items, errors } = parsePasted(pasteText)`。
- 若 `items.length === 0`：
  - 汇总错误统计并展示：`messageApi.warning('未解析到有效数据：列数不足X行，数值不合法Y行，请使用Tab/逗号分隔并确保6列（货号/名称/条码/数量/进货价/零售价）')`。
  - 追加详情弹窗（`Modal.info`）展示前 3 条错误示例行与正确格式示例，支持复制。
- 若 `items.length > 0` 且存在 `errors.length > 0`：
  - 用 `messageApi.warning` 提示“部分行已忽略”，列出忽略统计与查看详情入口。

3. 在粘贴弹窗加入“格式说明与示例”
- 在文本域下方新增说明块：支持分隔符（Tab/逗号/分号/管道/两个及以上空格）、期望 6 列顺序与示例行（含 Tab 与 CSV 两版），便于用户对照修正。

## 文案示例
- 快速摘要：`未解析到有效数据：列数不足 5 行，数值不合法 3 行。请使用 Tab 或逗号分隔，按 6 列顺序：货号、名称、条码、数量、进货价、零售价。`
- 详情弹窗：展示错误行片段与正确示例：`10001	苹果	6901234567890	10	12.5	18.0`。

## 保留与约束
- 保留原有硬性规则：数量>0，进货价>0；零售价可空。
- 不更改成功分支的合并与计算逻辑（`Amount=qty*purchase`）。

## 验证
- 用多组失败样例验证消息可读性：
  - 单空格分隔/列数不足/含表头/含货币符号与千分位/数量或进货价非数字。
- 用部分成功样例验证“部分忽略”的友好提示是否准确。