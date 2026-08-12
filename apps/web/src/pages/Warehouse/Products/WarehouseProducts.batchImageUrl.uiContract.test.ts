import { readFileSync } from 'node:fs'
import path from 'node:path'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

function extractSection(source: string, startText: string, endText: string) {
  const startIndex = source.indexOf(startText)
  assert(startIndex >= 0, `未找到代码片段：${startText}`)

  const endIndex = source.indexOf(endText, startIndex)
  assert(endIndex >= 0, `未找到结束片段：${endText}`)

  return source.slice(startIndex, endIndex)
}

const pageSource = readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/Products/index.tsx'), 'utf8')
const serviceSource = readFileSync(path.resolve(process.cwd(), 'src/services/warehouseProductService.ts'), 'utf8')
const en = JSON.parse(readFileSync(path.resolve(process.cwd(), 'src/i18n/locales/en.json'), 'utf8'))
const zh = JSON.parse(readFileSync(path.resolve(process.cwd(), 'src/i18n/locales/zh.json'), 'utf8'))

const DEFAULT_BASE_URL = 'https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/YW200/'

// 1. 默认基础地址与示例文件常量
assert(
  pageSource.includes(`const WAREHOUSE_PRODUCT_IMAGE_DEFAULT_BASE_URL = '${DEFAULT_BASE_URL}'`) &&
    pageSource.includes("const WAREHOUSE_PRODUCT_IMAGE_EXAMPLE_FILE = 'MC164-3.jpg'"),
  '批量图片弹窗应声明默认基础地址常量与示例文件名常量',
)

// 2. 每次打开重置：生成默认关闭、基础地址回默认值、HQ 同步默认关闭
const openBatchEditSection = extractSection(
  pageSource,
  'const openBatchEdit = () => {',
  'const handleCategoryMutationCommitted',
)
assert(
  openBatchEditSection.includes('generateImageUrls: false') &&
    openBatchEditSection.includes('imageBaseUrl: WAREHOUSE_PRODUCT_IMAGE_DEFAULT_BASE_URL') &&
    openBatchEditSection.includes('syncImageToHq: false'),
  '每次打开批量修改都应重置图片生成为关闭、基础地址为默认值、HQ 同步为关闭',
)

// 3. 表单接线：生成开关默认关闭，开启后显示基础地址输入、示例与覆盖警告
assert(
  pageSource.includes("const batchGenerateImageUrls = Form.useWatch('generateImageUrls', batchEditForm)"),
  '批量修改弹窗应使用 Form.useWatch 监听图片生成开关',
)
const batchEditModalSection = extractSection(
  pageSource,
  "t('warehouse.batchEditTitle'",
  '</Form>',
)
assert(
  batchEditModalSection.includes('name="generateImageUrls"') &&
    batchEditModalSection.includes('valuePropName="checked"') &&
    batchEditModalSection.includes('batchImageGenerate'),
  '批量修改弹窗应提供默认关闭的按货号生成图片地址开关',
)
assert(
  batchEditModalSection.includes('name="imageBaseUrl"') &&
    batchEditModalSection.includes('batchImageBaseUrl') &&
    batchEditModalSection.includes('batchImageExample') &&
    batchEditModalSection.includes('batchImageOverwriteWarning') &&
    batchEditModalSection.includes('WAREHOUSE_PRODUCT_IMAGE_EXAMPLE_FILE'),
  '生成图片地址开启后应显示可编辑基础地址、示例文件与覆盖警告',
)
assert(
  batchEditModalSection.includes('access.isAdmin || access.isWarehouseManager') &&
    batchEditModalSection.includes('name="syncImageToHq"') &&
    batchEditModalSection.includes('batchImageSyncHq') &&
    batchEditModalSection.includes('disabled={!batchGenerateImageUrls}'),
  '同步 HQ 数据库开关只对 Admin/WarehouseManager 显示，且仅在图片生成开启时可选择',
)

// 4. 图片关闭时默认地址不算变更；保存前二次确认数量/基础地址/覆盖/HQ 选择
const batchEditSaveSection = extractSection(
  pageSource,
  'const submitBatchEdit = async',
  'const handleToggleSingleActive = async',
)
assert(
  batchEditSaveSection.includes('isWarehouseProductBatchImageField') &&
    batchEditSaveSection.includes('图片关闭时默认基础地址不算变更'),
  '图片生成关闭时默认基础地址不得计入变更',
)
assert(
  batchEditSaveSection.includes('normalizeWarehouseProductImageBaseUrl') &&
    batchEditSaveSection.includes('isValidWarehouseProductImageBaseUrl') &&
    batchEditSaveSection.includes("t('warehouse.batchImageBaseUrlRequired'") &&
    batchEditSaveSection.includes("t('warehouse.batchImageBaseUrlInvalid'"),
  '保存前应规范化基础地址，并校验必填、HTTP(S) 目录格式与查询参数',
)
assert(
  batchEditSaveSection.includes('Modal.confirm({') &&
    batchEditSaveSection.includes('batchImageConfirmCount') &&
    batchEditSaveSection.includes('batchImageConfirmBaseUrl') &&
    batchEditSaveSection.includes('batchImageConfirmOverwrite') &&
    batchEditSaveSection.includes('batchImageConfirmHq'),
  '保存前应二次确认商品数量、规范化基础地址、本地覆盖与 HQ 选择',
)

// 5. 批量修改改为后台任务：持久化 job、轮询终态并按结果处理选择
assert(
  batchEditSaveSection.includes('createWarehouseProductBatchUpdateJob(items, options)') &&
    batchEditSaveSection.includes('startBatchUpdateJobPolling(activeJob)') &&
    !batchEditSaveSection.includes('await batchUpdateWarehouseProducts(items, options)'),
  '仓库商品页批量修改应提交后台 job，不得继续等待同步 batch-update 请求',
)
assert(
  pageSource.includes('readActiveWarehouseProductBatchUpdateJob') &&
    pageSource.includes('saveActiveWarehouseProductBatchUpdateJob') &&
    pageSource.includes('createWarehouseProductBatchUpdateJobPoller') &&
    pageSource.includes('getWarehouseProductBatchUpdateJob'),
  '后台批量修改应持久化活动 job，并支持页面刷新后恢复轮询',
)
assert(
  pageSource.includes("result.status === 'PartiallySucceeded'") &&
    pageSource.includes('setHqImageSyncFailDetail') &&
    pageSource.includes('submittedProductCodes'),
  '后台任务部分失败时应展示本地/HQ 明细并保留已提交商品选择',
)
const batchUpdatePollingSection = extractSection(
  pageSource,
  'const startBatchUpdateJobPolling = useCallback',
  'const showActiveBatchUpdateJobStatus = useCallback',
)
assert(
  batchUpdatePollingSection.includes('error instanceof RequestError && error.status === 404') &&
    batchUpdatePollingSection.includes('clearActiveBatchUpdateJob();') &&
    batchUpdatePollingSection.includes('saveActiveBatchUpdateJob(job);'),
  '轮询仅在明确 404 时清理活动任务；超时、网络错误和 5xx 应保留 job 供刷新恢复',
)
const batchUpdateStorageSection = extractSection(
  pageSource,
  'function saveActiveWarehouseProductBatchUpdateJob',
  'function buildSupplierOptions',
)
assert(
  batchUpdateStorageSection.includes('try {') &&
    batchUpdateStorageSection.includes('return false;') &&
    batchUpdatePollingSection.includes('batchUpdateJobStorageUnavailable'),
  'localStorage 写入失败不得中断已经创建的后台任务与当前页面轮询',
)
assert(
  pageSource.includes('clearSubmittedBatchUpdateSelection') &&
    pageSource.includes('void refreshCurrentList()'),
  '后台任务全部成功时应只清除该任务提交的选择并刷新列表',
)
assert(
  pageSource.includes("t('warehouse.batchImageHqFailTitle'") &&
    pageSource.includes('hqImageSyncFailDetail.errors') &&
    pageSource.includes('hqImageSyncFailDetail.items') &&
    pageSource.includes('hqImageSyncFailOpen'),
  'HQ 同步失败弹窗应展示错误明细与单项失败明细',
)

// 6. 服务契约：请求扩展图片生成/HQ 同步字段，响应支持更新数与 HQ 明细，逐项失败不抛错
const batchUpdateSection = extractSection(
  serviceSource,
  'export async function batchUpdateWarehouseProducts(',
  'export async function getWarehouseProductsTable(',
)
assert(
  serviceSource.includes('generateImageUrls?: boolean') &&
    serviceSource.includes('imageBaseUrl?: string') &&
    serviceSource.includes('syncImageToHq?: boolean'),
  '批量更新选项应声明 generateImageUrls/imageBaseUrl/syncImageToHq',
)
assert(
  batchUpdateSection.includes('GenerateImageUrls:') &&
    batchUpdateSection.includes('ImageBaseUrl:') &&
    batchUpdateSection.includes('SyncImageToHq:'),
  '批量更新请求应发送 GenerateImageUrls/ImageBaseUrl/SyncImageToHq',
)
assert(
  serviceSource.includes('imageUpdatedCount') &&
    serviceSource.includes('hqImageSync') &&
    serviceSource.includes('normalizeWarehouseProductHqImageSync'),
  '批量更新响应应支持 imageUpdatedCount 与 hqImageSync 明细',
)
assert(
  !batchUpdateSection.includes('仓库批量更新部分失败'),
  '逐项/HQ 失败不得由 service helper 抛出，仅 HTTP/整批失败抛错',
)

// 7. 中英文文案
for (const locale of [en, zh]) {
  for (const key of [
    'batchImageGenerate',
    'batchImageBaseUrl',
    'batchImageExample',
    'batchImageOverwriteWarning',
    'batchImageSyncHq',
    'batchImageBaseUrlRequired',
    'batchImageBaseUrlInvalid',
    'batchEditConfirmTitle',
    'batchEditConfirmOk',
    'batchImageConfirmCount',
    'batchImageConfirmBaseUrl',
    'batchImageConfirmOverwrite',
    'batchImageConfirmHq',
    'batchImageHqFailTitle',
    'batchUpdateFailDetailTitle',
    'batchImageHqFailMessage',
    'batchImageHqFailNoItems',
    'batchImageUpdatedCount',
    'batchUpdateJobCreateFailed',
    'batchUpdateJobSubmitted',
    'batchUpdateJobSubmittedDescription',
    'batchUpdateJobDuplicate',
    'batchUpdateJobSucceeded',
    'batchUpdateJobPartialSucceeded',
    'batchUpdateJobFailed',
    'batchUpdateJobRetryHint',
    'batchUpdateJobTimeoutTitle',
    'batchUpdateJobTimeout',
    'batchUpdateJobQueryFailed',
    'batchUpdateJobMissing',
    'batchUpdateJobStatusTitle',
    'batchUpdateJobId',
    'batchUpdateJobStatus',
    'batchUpdateJobStartedAt',
    'batchUpdateJobProductCount',
    'batchUpdateJobStorageUnavailable',
    'batchUpdateJobStorageUnavailableDescription',
    'batchUpdateJobQueryInterrupted',
  ]) {
    assert(locale.warehouse?.[key], `warehouse.${key} 必须提供中英文翻译`)
  }
}
assert(
  zh.warehouse?.batchImageOverwriteWarning.includes('覆盖') &&
    zh.warehouse?.batchImageOverwriteWarning.includes('货号') &&
    en.warehouse?.batchImageOverwriteWarning.includes('overwritten'),
  '覆盖警告应同时说明本地覆盖与按货号拼接',
)
assert(
  zh.warehouse?.batchImageSyncHq.includes('H商品') && en.warehouse?.batchImageSyncHq.includes('H-product'),
  '同步 HQ 开关应限定仅 H 商品图片',
)

console.log('WarehouseProducts.batchImageUrl.uiContract.test: ok')
