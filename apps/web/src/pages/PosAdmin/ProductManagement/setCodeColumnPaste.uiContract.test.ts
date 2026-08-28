import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'

const pageSource = readFileSync('src/pages/PosAdmin/ProductManagement/index.tsx', 'utf8')

const readFormSource = (formMarker: string) => {
  const start = pageSource.indexOf(formMarker)
  assert.notEqual(start, -1, `应找到表单：${formMarker}`)
  const end = pageSource.indexOf('</Form>', start)
  assert.notEqual(end, -1, `表单应正常闭合：${formMarker}`)
  return pageSource.slice(start, end)
}

const createFormSource = readFormSource('<Form form={createForm}')
const editFormSource = readFormSource('<Form form={editForm}')

const requireSource = (snippet: string, message: string) => {
  assert.equal(pageSource.includes(snippet), true, message)
}

requireSource('const [editSetCodePasteTarget, setEditSetCodePasteTarget]', '页面应维护当前选中粘贴列')
requireSource('aria-pressed={selected}', '可聚焦列头应暴露 aria-pressed 状态')
requireSource("handleEditSetCodePaste(event, 1, 'setBarcode', rowId)", '套装条码单元格应从当前行粘贴')
requireSource("handleEditSetCodePaste(event, 1, 'setRetailPrice', rowId)", '套装零售价单元格应从当前行粘贴')
requireSource("handleEditSetCodePaste(event, 2, 'setBarcode', rowId)", '多条码单元格应从当前行粘贴')
requireSource("if (productType === 2 && field !== 'setBarcode') return", '多条码商品应只开放条码列粘贴')
requireSource("editForm.getFieldValue('purchasePrice')", '新增行和套装派生应读取表单当前采购价')
requireSource("editForm.getFieldValue('retailPrice')", '新增行和套装派生应读取表单当前零售价')
requireSource('loadCompleteSetCodeDraftRows({', '编辑弹窗应按权威总数加载完整条码快照后再做重复校验')
requireSource('getRowId: (item) => item.id ?? item.setCodeId', '完整条码快照应使用稳定行标识检查重复和漏行')
requireSource('editSaveInFlightRef.current', '保存应使用同步 ref 防止重复提交')
requireSource('confirmLoading={editSaving}', '编辑弹窗保存期间应展示确认 loading')
requireSource('<Form form={editForm} disabled={editSaving}', '保存期间应禁用商品主表单，避免快照后的输入丢失')
assert.equal(
  editFormSource.includes('disabled={editSaving || categoryLoadFailed}'),
  true,
  '编辑分类选择器不得以显式 false 绕过保存期间的 Form 禁用状态',
)
assert.equal(createFormSource.includes('editSaving'), false, '创建商品表单不得错误依赖编辑保存状态')
requireSource('if (editSaveInFlightRef.current) return', '条码草稿修改入口应同步检查保存锁')
requireSource('disabled={editSaving}', '保存期间应显式禁用条码表格输入和删除操作')
requireSource('deriveSetCodePurchasePrice({ retailPrice, mainPurchasePrice, mainRetailPrice })', '套装采购价应复用可测试的确定性派生逻辑')
requireSource('!editSetCodesReady', '保存和编辑入口应等待条码明细完整加载')
requireSource('setEditSetCodesReady(true)', '只有完整分页加载成功后才能标记条码草稿就绪')
requireSource("result.error === 'too_many_rows'", '超过安全行数上限时页面应显示明确提示')
requireSource('scroll={{ x: 700 }}', '窄视口下条码表格应允许横向溢出')

assert.equal(/(?:window|document)\.addEventListener\(['"]paste['"]/.test(pageSource), false, '页面不得注册全局 paste 监听')
