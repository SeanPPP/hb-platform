import fs from 'node:fs'
import path from 'node:path'

function assert(condition: unknown, label: string) {
  if (!condition) {
    throw new Error(label)
  }
}

const pageSource = fs.readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/index.tsx'), 'utf8')
const serviceSource = fs.readFileSync(path.resolve(process.cwd(), 'src/services/storeOrderService.ts'), 'utf8')
const compactCssSource = fs.readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/compact.css'), 'utf8')

assert(
  pageSource.includes("t('storeOrders.fixStoreGuid'") &&
    pageSource.includes('openUnmatchedStoreModal'),
  '分店订货列表工具栏应提供修复分店 GUID 入口',
)

assert(
  pageSource.includes('getUnmatchedStoreOrderGroups()') &&
    pageSource.includes('unmatchedStoreGroups') &&
    pageSource.includes('rowKey="sourceStoreCode"'),
  '修复弹窗应按旧分店 GUID 聚合显示，不展示订单明细列表',
)

assert(
  pageSource.includes('batchMapStoreOrderStoreCode({ mappings })') &&
    pageSource.includes('sourceStoreCode: group.sourceStoreCode') &&
    pageSource.includes('targetStoreCode: unmatchedStoreTargets[group.sourceStoreCode]'),
  '修复保存应提交旧 GUID 到目标本地分店编码的映射',
)

assert(
  pageSource.includes('loadAllUnmatchedTargetStores') &&
    pageSource.includes('UNMATCHED_TARGET_STORE_PAGE_SIZE = 500') &&
    pageSource.includes("sortField: 'storeName'") &&
    pageSource.includes("sortOrder: 'ascend'") &&
    !pageSource.includes('isActive: true,\n          sortField: \'storeName\''),
  '目标本地分店应分页加载所有分店并按名称排序，不应只取启用分店',
)

assert(
  pageSource.includes('stores.length >= result.total') &&
    pageSource.includes('result.items.length < UNMATCHED_TARGET_STORE_PAGE_SIZE') &&
    pageSource.includes('page += 1'),
  '目标本地分店应循环加载所有分页，避免只显示第一页分店',
)

assert(
  pageSource.includes('buildUnmatchedTargetStoreLabel') &&
    pageSource.includes('store.brandName?.trim()') &&
    pageSource.includes('store.address?.trim()') &&
    pageSource.includes('`${storeName}（停用）`') &&
    pageSource.includes("labelParts.join('｜')") &&
    pageSource.includes('title: buildUnmatchedTargetStoreLabel(item)'),
  '目标本地分店选项应展示分店编码、名称、品牌、地址，并省略空字段分隔符',
)

assert(
  pageSource.includes('width="min(1280px, calc(100vw - 48px))"') &&
    !pageSource.includes('width={980}'),
  '修复分店 GUID 弹窗应使用更宽的响应式宽度',
)

assert(
  pageSource.includes('scroll={{ x: 1138, y: 420 }}') &&
    pageSource.includes('width: 520') &&
    pageSource.includes('popupMatchSelectWidth={640}'),
  '目标本地分店列和下拉应有明确宽度，避免品牌地址被过早截断',
)

assert(
  pageSource.includes("classNames={{ popup: { root: 'store-order-unmatched-target-popup' } }}") &&
    compactCssSource.includes('.store-order-unmatched-target-popup .ant-select-item-option-content') &&
    compactCssSource.includes('white-space: nowrap') &&
    compactCssSource.includes('text-overflow: ellipsis'),
  '目标本地分店下拉应使用局部样式控制长文本展示',
)

assert(
  pageSource.includes('展示品牌和地址用于人工确认 GUID 对应分店') &&
    pageSource.includes('保存时仍只提交目标 StoreCode'),
  '目标本地分店选项应保留中文注释说明展示信息和保存值的边界',
)

assert(
  pageSource.includes('await Promise.all([loadData(), loadBranches(), loadUnmatchedStoreGroups()])'),
  '修复成功后应刷新主列表、分店筛选和未匹配聚合',
)

assert(
  serviceSource.includes("`${API_BASE}/unmatched-store-groups`") &&
    serviceSource.includes("`${API_BASE}/batch-map-store-code`"),
  '前端服务层应封装未匹配分店聚合和批量映射接口',
)

console.log('storeOrderUnmatchedStore.logic.test: ok')
