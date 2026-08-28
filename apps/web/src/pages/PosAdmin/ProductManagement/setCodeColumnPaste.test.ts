import assert from 'node:assert/strict'
import {
  applySetCodeColumnPaste,
  deriveSetCodePurchasePrice,
  mergeSetCodeRetailPriceEdit,
  parseSetCodeClipboardColumn,
  parseSetCodeRetailPrice,
  validateSetCodeDrafts,
  type SetCodeDraftRow,
} from './setCodeColumnPaste'

assert.deepEqual(
  parseSetCodeClipboardColumn('00123\r\n\r\nABC\r\n'),
  { ok: true, values: ['00123', '', 'ABC'] },
  '应规范化 Excel 换行、保留中间空格并移除末尾空行',
)
assert.deepEqual(
  parseSetCodeClipboardColumn('A\tB\nC\tD'),
  { ok: false, reason: 'multiple_columns' },
  '检测到 Tab 时应拒绝多列数据',
)

const baseRows: SetCodeDraftRow[] = [
  { id: 'row-1', setBarcode: 'OLD-1', setRetailPrice: 1 },
  { id: 'row-2', setBarcode: 'OLD-2', setRetailPrice: 2 },
]
const barcodePaste = applySetCodeColumnPaste({
  rows: baseRows,
  edits: {},
  startRowId: 'row-2',
  field: 'setBarcode',
  clipboardText: ' 000123 \r\n\r\nABC\r\n',
  createRow: (index) => ({ _rowId: `new-${index}`, isActive: true }),
})

assert.equal(barcodePaste.appliedCount, 2)
assert.equal(barcodePaste.skippedBlankCount, 1)
assert.equal(barcodePaste.addedCount, 2)
assert.equal(barcodePaste.invalidCount, 0)
assert.equal(barcodePaste.rows.length, 4)
assert.equal(barcodePaste.edits['row-2']?.setBarcode, '000123', '条码应保留前导零')
assert.equal(barcodePaste.edits['new-2'], undefined, '中间空格不得覆盖或创建编辑值')
assert.equal(barcodePaste.edits['new-3']?.setBarcode, 'ABC', '空格后数据应保持 Excel 行位')
assert.equal(barcodePaste.rows[2].setRetailPrice, undefined, '套装新增行应保持空价格默认值')

const multiColumnPaste = applySetCodeColumnPaste({
  rows: baseRows,
  edits: { 'row-1': { setBarcode: 'EDITED' } },
  field: 'setBarcode',
  clipboardText: 'A\tB',
  createRow: (index) => ({ _rowId: `rejected-${index}` }),
})
assert.equal(multiColumnPaste.error, 'multiple_columns')
assert.equal(multiColumnPaste.rows, baseRows, '多列拒绝应原样返回现有行引用')
assert.deepEqual(multiColumnPaste.edits, { 'row-1': { setBarcode: 'EDITED' } }, '多列拒绝不得覆盖现有编辑值')

let limitedCreateCalls = 0
const withinRowLimitPaste = applySetCodeColumnPaste({
  rows: [],
  edits: {},
  field: 'setBarcode',
  clipboardText: 'LIMIT-1\nLIMIT-2',
  maxRows: 2,
  createRow: (index) => {
    limitedCreateCalls += 1
    return { _rowId: `limit-${index}` }
  },
})
assert.equal(withinRowLimitPaste.error, undefined, '粘贴后恰好达到行数上限应允许写入')
assert.equal(withinRowLimitPaste.rows.length, 2)
assert.equal(limitedCreateCalls, 2)

limitedCreateCalls = 0
const aboveRowLimitPaste = applySetCodeColumnPaste({
  rows: [],
  edits: {},
  field: 'setBarcode',
  clipboardText: 'LIMIT-1\nLIMIT-2\nLIMIT-3',
  maxRows: 2,
  createRow: (index) => {
    limitedCreateCalls += 1
    return { _rowId: `rejected-limit-${index}` }
  },
})
assert.equal(aboveRowLimitPaste.error, 'too_many_rows', '超过行数上限时应整次拒绝')
assert.equal(aboveRowLimitPaste.rows.length, 0, '超过上限不得扩展草稿行')
assert.equal(limitedCreateCalls, 0, '超过上限必须在创建行前失败关闭')

const multiCodePaste = applySetCodeColumnPaste({
  rows: [],
  edits: {},
  field: 'setBarcode',
  clipboardText: 'M-1\nM-2',
  createRow: (index) => ({
    _rowId: `multi-${index}`,
    setPurchasePrice: 28,
    setRetailPrice: 50,
    isActive: true,
  }),
})
assert.deepEqual(
  multiCodePaste.rows.map((row) => [row.setPurchasePrice, row.setRetailPrice]),
  [[28, 50], [28, 50]],
  '多条码自动新增行应保留主条码价格默认值',
)

const duplicateRows: SetCodeDraftRow[] = [
  { id: 'dup-1', setBarcode: 'AbC', setRetailPrice: 1 },
  { id: 'dup-2', setBarcode: 'DEF', setRetailPrice: 2 },
]
const duplicatePaste = applySetCodeColumnPaste({
  rows: duplicateRows,
  edits: {},
  startRowId: 'dup-2',
  field: 'setBarcode',
  clipboardText: ' abc ',
  createRow: (index) => ({ _rowId: `dup-new-${index}` }),
})
assert.equal(duplicatePaste.error, 'duplicate_barcode')
assert.equal(duplicatePaste.duplicateBarcode, 'AbC')
assert.deepEqual(duplicatePaste.duplicateRowNumbers, [1, 2])
assert.deepEqual(duplicatePaste.rows, duplicateRows, '重复条码时整次粘贴不得改变行数据')
assert.deepEqual(duplicatePaste.edits, {}, '重复条码时整次粘贴不得改变编辑覆盖值')

assert.equal(parseSetCodeRetailPrice('$1,234.567'), 1234.57)
assert.equal(parseSetCodeRetailPrice('￥ 0'), 0)
assert.equal(parseSetCodeRetailPrice('1 234.5 €'), 1234.5)
assert.equal(parseSetCodeRetailPrice('10.075'), 10.08, '十进制金额应按第三位小数四舍五入')
assert.equal(parseSetCodeRetailPrice('4.015'), 4.02, '二进制浮点误差不得少算一分钱')
assert.equal(parseSetCodeRetailPrice('1.005'), 1.01, '常见半分边界应稳定进位')
assert.equal(parseSetCodeRetailPrice('1,2'), undefined, '错误的千分位分组应视为非法价格')
assert.equal(parseSetCodeRetailPrice('AUD 8'), undefined, '非货币符号文本不得被静默剥离')
assert.equal(parseSetCodeRetailPrice('-2'), undefined)

assert.deepEqual(
  mergeSetCodeRetailPriceEdit({ setPurchasePrice: 7 }, 0, () => undefined),
  { setPurchasePrice: 7, setRetailPrice: 0 },
  '手工零售价修改无法派生采购价时应保留已有采购价覆盖',
)
assert.deepEqual(
  mergeSetCodeRetailPriceEdit({ setPurchasePrice: 7 }, 0, () => 0),
  { setPurchasePrice: 0, setRetailPrice: 0 },
  '派生采购价 0 时应明确覆盖旧采购价',
)

assert.equal(
  deriveSetCodePurchasePrice({ retailPrice: 20.15, mainPurchasePrice: 1, mainRetailPrice: 2 }),
  10.08,
  '采购价比例结果为半分钱时应按十进制 half-up 进位',
)
assert.equal(
  deriveSetCodePurchasePrice({ retailPrice: 8.03, mainPurchasePrice: 1, mainRetailPrice: 2 }),
  4.02,
  '采购价比例不得因二进制浮点误差少算一分钱',
)
assert.equal(
  deriveSetCodePurchasePrice({ retailPrice: 0, mainPurchasePrice: 1, mainRetailPrice: 2 }),
  0,
  '合法零售价 0 应派生采购价 0',
)
assert.equal(
  deriveSetCodePurchasePrice({ retailPrice: 8, mainPurchasePrice: 1, mainRetailPrice: 0 }),
  undefined,
  '主商品零售价无效时不得伪造采购价',
)

const retailPaste = applySetCodeColumnPaste({
  rows: [{ id: 'price-1', setBarcode: 'P1', setRetailPrice: 9.9 }],
  edits: {},
  startRowId: 'price-1',
  field: 'setRetailPrice',
  clipboardText: '$1,234.567\ninvalid\n0\n-2',
  createRow: (index) => ({ _rowId: `price-${index + 1}`, isActive: true }),
  derivePurchasePrice: (retailPrice) => (
    retailPrice > 0 ? Math.round(retailPrice * 0.5 * 100) / 100 : undefined
  ),
})
assert.equal(retailPaste.appliedCount, 2)
assert.equal(retailPaste.invalidCount, 2)
assert.equal(retailPaste.addedCount, 3)
assert.deepEqual(retailPaste.edits['price-1'], {
  setRetailPrice: 1234.57,
  setPurchasePrice: 617.29,
})
assert.equal(retailPaste.edits['price-2'], undefined, '非法价格应保持原值')
assert.deepEqual(retailPaste.edits['price-3'], { setRetailPrice: 0 })
assert.equal(retailPaste.edits['price-4'], undefined, '负数价格应保持原值')

const unavailableDerivationPaste = applySetCodeColumnPaste({
  rows: [{ id: 'derive-unavailable', setBarcode: 'D1', setRetailPrice: 8 }],
  edits: { 'derive-unavailable': { setPurchasePrice: 7 } },
  startRowId: 'derive-unavailable',
  field: 'setRetailPrice',
  clipboardText: '0',
  createRow: (index) => ({ _rowId: `derive-unavailable-${index}` }),
  derivePurchasePrice: () => undefined,
})
assert.deepEqual(
  unavailableDerivationPaste.edits['derive-unavailable'],
  { setPurchasePrice: 7, setRetailPrice: 0 },
  '主商品比例无法派生时应保留已有采购价编辑',
)

assert.deepEqual(
  validateSetCodeDrafts([
    { id: 'valid-1', setBarcode: 'ONE', setRetailPrice: 1 },
    { id: 'valid-2', setBarcode: 'TWO', setRetailPrice: 2 },
  ], {
    'valid-2': { setBarcode: ' one ' },
  }),
  {
    valid: false,
    reason: 'duplicate_barcode',
    rowNumbers: [1, 2],
    barcode: 'ONE',
  },
  '保存前应检查原行与编辑覆盖值合并后的重复条码',
)
assert.deepEqual(
  validateSetCodeDrafts([
    { id: 'existing', setBarcode: 'EXISTING', setRetailPrice: 1 },
    { _rowId: 'new-row', setBarcode: '', setRetailPrice: 2 },
    { id: 'edited-row', setBarcode: 'ORIGINAL', setRetailPrice: 3 },
  ], {
    'new-row': { setBarcode: 'mixed' },
    'edited-row': { setBarcode: ' MIXED ' },
  }),
  {
    valid: false,
    reason: 'duplicate_barcode',
    rowNumbers: [2, 3],
    barcode: 'mixed',
  },
  '保存前应统一检查已有行、新增行和编辑覆盖值',
)

const largeDraft = Array.from({ length: 201 }, (_, index): SetCodeDraftRow => ({
  id: `large-${index + 1}`,
  setBarcode: `CODE-${index + 1}`,
  setRetailPrice: index + 1,
}))
assert.deepEqual(
  validateSetCodeDrafts(largeDraft, {
    'large-1': { setBarcode: ' code-201 ' },
  }),
  {
    valid: false,
    reason: 'duplicate_barcode',
    rowNumbers: [1, 201],
    barcode: 'code-201',
  },
  '完整草稿超过 200 行时仍应检测首尾重复条码',
)

assert.deepEqual(
  validateSetCodeDrafts([{ id: 'required-1', setBarcode: '', setRetailPrice: 1 }], {}),
  { valid: false, reason: 'barcode_required', rowNumbers: [1] },
)
assert.deepEqual(
  validateSetCodeDrafts([{ id: 'required-2', setBarcode: 'OK' }], {}),
  { valid: false, reason: 'retail_price_required', rowNumbers: [1] },
)
assert.deepEqual(
  validateSetCodeDrafts([{ id: 'cleared-price', setBarcode: 'OK', setRetailPrice: 9.9 }], {
    'cleared-price': { setRetailPrice: null },
  }),
  { valid: false, reason: 'retail_price_required', rowNumbers: [1] },
  '手工清空既有零售价后，保存前校验不得回退到原值',
)
assert.deepEqual(
  validateSetCodeDrafts([{ id: 'ok', setBarcode: 'OK', setRetailPrice: 0 }], {}),
  { valid: true },
  '零售价 0 仍应视为已填写',
)
