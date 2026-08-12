import { readFileSync } from 'node:fs'
import path from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

const pageSource = readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/Products/index.tsx'), 'utf8')
const modalSource = readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/Products/WarehouseProductStorePriceSyncModal.tsx'), 'utf8')
const en = JSON.parse(readFileSync(path.resolve(process.cwd(), 'src/i18n/locales/en.json'), 'utf8'))
const zh = JSON.parse(readFileSync(path.resolve(process.cwd(), 'src/i18n/locales/zh.json'), 'utf8'))

assert(
  pageSource.includes('const canManageWarehouseStorePriceSync = access.isAdmin || access.isWarehouseManager') &&
    pageSource.includes('canManageWarehouseStorePriceSync ?') &&
    pageSource.includes('<WarehouseProductStorePriceSyncModal'),
  '更新分店价格按钮和独立弹窗必须只对 Admin/WarehouseManager 接线显示',
)
assert(
  pageSource.includes('selectedProductCodes={selectedRowKeys.map(String)}') &&
    pageSource.includes('setSelectedRowKeys([])') &&
    pageSource.includes('refreshCurrentList({ page: 1 })'),
  '页面接线必须把选中 ProductCode 传入，并在完全成功后清空选择和刷新',
)
assert(
  modalSource.includes('Modal.confirm({') &&
    modalSource.includes('applyToAllProducts') &&
    modalSource.includes('未勾选将处理全部仓库商品'),
  '全量模式提交必须有二次 Modal.confirm 和明确的包含下架商品提示',
)
assert(
  modalSource.includes('error.productCode') &&
    modalSource.includes('error.storeCode') &&
    modalSource.includes('error.message'),
  '结果错误必须至少展示商品编码、分店编码和错误消息',
)
assert(
  modalSource.includes('summary.failedCount'),
  '结果摘要必须展示排除 MISSING_PRICE 后的失败数量',
)
assert(
  !modalSource.includes("status: 'Failed'"),
  '轮询超时或网络失败时不得把仍在运行的服务端任务伪装成 Failed',
)
assert(
  modalSource.includes('pollingActive') &&
    modalSource.includes('createWarehouseStorePriceSyncJobPoller') &&
    modalSource.includes("t('common.retry'"),
  '轮询状态必须独立于服务端 job.status，并提供不重复创建任务的重试轮询入口',
)
assert(
  modalSource.includes("'warehouse.storePriceSync.hqWriteExpansionWarning'") &&
    modalSource.includes('syncToHq ?'),
  '勾选 HQ 时必须提示缺少主商品会向全部 HQ 分店扩展建档',
)
for (const locale of [en, zh]) {
  assert(locale.common?.retry, 'common.retry 必须提供中英文翻译')
  assert(locale.warehouse?.retailPrice, 'warehouse.retailPrice 必须提供中英文翻译')
  assert(locale.warehouse?.discountRate, 'warehouse.discountRate 必须提供中英文翻译')
  assert(locale.warehouse?.autoPricing, 'warehouse.autoPricing 必须提供中英文翻译')
  assert(locale.warehouse?.storePriceSync?.failedItems, 'warehouse.storePriceSync.failedItems 必须提供中英文翻译')
  assert(locale.warehouse?.storePriceSync?.hqWriteExpansionWarning, 'warehouse.storePriceSync.hqWriteExpansionWarning 必须提供中英文翻译')
  assert(locale.warehouse?.storePriceSync?.fullScopeSummary?.toLowerCase().includes(locale === zh ? '本地最大写入量' : 'local maximum writes'), '最大写入量必须明确限定为本地写入')
}

console.log('WarehouseProductStorePriceSync.uiContract.test: ok')
