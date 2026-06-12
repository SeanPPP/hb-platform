## 问题摘要
- TypeScript 错误：`元素隐式具有 any 类型，因为类型为 string 的表达式不能用于索引类型 ...`。
- 位置：`ReactUmi/my-app/src/pages/PosAdmin/LocalSupplierInvoiceDetail/index.tsx:154`（`const maxIdx = Math.max(...cols.map(c => indexMap[c]))`）。
- 根因：`cols`在编译期被推断为`string[]`，而`indexMap`键类型为联合字面量（列键）。使用`string`索引会触发类型错误。

## 修复目标
- 统一列键类型，确保`cols`为受限的联合类型`ColKey[]`，并与`indexMap: Record<ColKey, number>`一致，从而消除索引错误。

## 修改方案
1) 声明列键类型与索引映射
- 在文件顶部解析区域新增：`type ColKey = 'itemNumber'|'productName'|'barcode'|'quantity'|'purchasePrice'|'retailPrice'`
- 定义：`const indexMap: Record<ColKey, number> = { itemNumber:0, productName:1, barcode:2, quantity:3, purchasePrice:4, retailPrice:5 } as const`

2) 规范状态与回调类型
- `pasteSelectedCols` 状态类型改为 `ColKey[]`（已基本符合，保持一致）。
- Checkbox.Group `onChange` 显式类型：`(v: ColKey[]) => setPasteSelectedCols(v)`，避免 `as any` 造成联合类型丢失。

3) 修复 `parsePastedDetailed` 的参数与内部变量类型
- 函数签名：`parsePastedDetailed(raw: string, selectedCols?: Readonly<ColKey[]>)`
- 默认列：`const defaultCols: ColKey[] = ['itemNumber','productName','barcode','quantity','purchasePrice']`
- 归一化：`const cols: ColKey[] = (selectedCols && selectedCols.length ? selectedCols : defaultCols)`
- 计算最大索引：`const maxIdx = Math.max(...(cols as ColKey[]).map(k => indexMap[k]))`
- 其余 `cols.includes('itemNumber')` 等保持，因字面量与 `ColKey`兼容。

4) 代码位置调整
- 以上改动集中于：
  - 解析函数定义区：`index.tsx:118–190`附近
  - 弹窗列选择控件：`index.tsx:1104–1115`
  - `pasteSelectedCols` 状态定义：`index.tsx:42`

## 验证
- 本地编译通过，不再出现 TS 索引类型错误。
- 粘贴导入与列选择功能正常；`maxIdx`正确反映选择的最高列索引。

## 风险与兼容
- 改动仅限类型层面与极少量签名，不影响运行时逻辑。
- 保持现有“友好提示”“图片优先检测源”等功能不变。

若确认，我将按上述方案实施具体修改并验证。