import { readFileSync } from 'node:fs'
import path from 'node:path'
import type { CurrentUser } from '../../../types/auth'
import { buildAccess } from '../../../utils/access'

function createCurrentUser(overrides: Partial<CurrentUser> = {}): CurrentUser {
  return {
    userGUID: 'test-user-guid',
    username: 'tester',
    email: 'tester@example.com',
    permissions: [],
    roleNames: [],
    storeNames: [],
    ...overrides,
  }
}

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

async function runTest(name: string, execute: () => void | Promise<void>): Promise<string | null> {
  try {
    await execute()
    console.log(`ok - ${name}`)
    return null
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error)
    console.error(`not ok - ${name}`)
    console.error(reason)
    return `${name}: ${reason}`
  }
}

const pageFile = path.resolve(process.cwd(), 'src/pages/PosAdmin/ProductManagement/index.tsx')
const typeFile = path.resolve(process.cwd(), 'src/types/posProduct.ts')
const serviceFile = path.resolve(process.cwd(), 'src/services/posProductService.ts')
const pageSource = readFileSync(pageFile, 'utf8')
const typeSource = readFileSync(typeFile, 'utf8')
const serviceSource = readFileSync(serviceFile, 'utf8')

async function main() {
  const failures: string[] = []

  const adminAccessFailure = await runTest('Admin 权限判断成立', () => {
    const access = buildAccess(createCurrentUser({ roleNames: ['Admin'] }))
    assertEqual(access.isAdmin, true, 'Admin 应被识别为管理员')
  })
  if (adminAccessFailure) failures.push(adminAccessFailure)

  const warehouseAccessFailure = await runTest('WarehouseStaff 权限判断成立', () => {
    const access = buildAccess(createCurrentUser({ roleNames: ['WarehouseStaff'] }))
    assertEqual(access.isAdmin, false, 'WarehouseStaff 不应被识别为管理员')
    assertEqual(access.isWarehouseStaff, true, 'WarehouseStaff 应被识别为仓库员工')
  })
  if (warehouseAccessFailure) failures.push(warehouseAccessFailure)

  const adminButtonGuardFailure = await runTest('页面应使用 Admin 权限控制 HQ 同步按钮', () => {
    assert(
      pageSource.includes("t('posAdmin.products.fullSyncFromHQ', '全量同步')") &&
        pageSource.includes("t('posAdmin.products.incrementalSyncFromHQ', '增量同步')"),
      '页面源码中应存在“全量同步”和“增量同步”两个按钮',
    )
    assert(
      pageSource.includes('useAuthStore') && pageSource.includes('isAdmin'),
      '页面应显式读取 auth store，并基于 Admin 权限决定是否渲染 HQ 同步按钮',
    )
    assert(
      pageSource.includes('ensureCanSyncProductsFromHq') &&
        pageSource.includes('if (!ensureCanSyncProductsFromHq()) return'),
      'HQ 同步打开弹窗和确认提交时都应有 Admin 权限守卫',
    )
  })
  if (adminButtonGuardFailure) failures.push(adminButtonGuardFailure)

  const writePermissionGuardFailure = await runTest('页面写操作应使用 POS 商品管理权限控制', () => {
    assert(
      pageSource.includes('canManagePosProducts'),
      '页面应读取 canManagePosProducts 控制商品编辑、分类管理、批量和同步到分店等写操作',
    )
    assert(
      pageSource.includes('ensureCanManagePosProducts') &&
        pageSource.includes("t('posAdmin.products.noManagePermission'"),
      '写操作处理函数应有统一权限保护，避免只读用户绕过按钮直接触发',
    )
  })
  if (writePermissionGuardFailure) failures.push(writePermissionGuardFailure)

  const categoryParentValueFailure = await runTest('商品分类父级 Cascader 应只提交叶子 GUID', () => {
    assert(
      pageSource.includes('resolveCascaderLeafValue') &&
        pageSource.includes('const parentGuid = resolveCascaderLeafValue(values.parentGuid)'),
      '分类父级保存前应把 Cascader 路径转换为最后一级 GUID',
    )
    assert(
      pageSource.includes('parentGuid: getCategoryValueFromGuid(node.parentGuid, categoryTree)'),
      '编辑分类时应使用完整路径回填父分类 Cascader',
    )
    assert(
      pageSource.includes('categoryParentDisabledGuids.has(parentGuid)') &&
        pageSource.includes('invalidParentCategory'),
      '保存分类时应再次校验父级不能是自身或子分类，避免绕过 UI 禁用',
    )
  })
  if (categoryParentValueFailure) failures.push(categoryParentValueFailure)

  const syncFieldsRequestFailure = await runTest('同步到分店应把用户勾选字段发送给后端', () => {
    assert(
      typeSource.includes('SyncProductsToStoresField') &&
        typeSource.includes('fields: SyncProductsToStoresField[]'),
      'SyncProductsToStoresRequest 应声明同步字段列表',
    )
    assert(
      pageSource.includes('buildSyncProductsToStoresFields(values)') &&
        pageSource.includes('fields: syncFields') &&
        pageSource.includes('selectSyncFields'),
      '同步到分店应根据复选框构造 fields，并校验至少选择一个字段',
    )
  })
  if (syncFieldsRequestFailure) failures.push(syncFieldsRequestFailure)

  const storeRecordsFailure = await runTest('商品管理应显示分店记录数量并点击查看分店记录明细', () => {
    assert(
      typeSource.includes('storeRecordCount?: number') &&
        typeSource.includes('ProductStoreRecordDto'),
      '前端商品类型应包含分店记录数量和分店记录明细 DTO',
    )
    assert(
      serviceSource.includes('getProductStoreRecords') &&
        serviceSource.includes('/store-records'),
      '商品服务应提供按商品编码读取分店记录明细的接口',
    )
    assert(
      pageSource.includes('storeRecordCount') &&
        pageSource.includes('openStoreRecords') &&
        pageSource.includes('storeRecordsRequestSeqRef') &&
        pageSource.includes('requestSeq === storeRecordsRequestSeqRef.current') &&
        pageSource.includes('storeRecordsVisible') &&
        pageSource.includes('storeRecordsLoading') &&
        pageSource.includes('canManageStoreProducts') &&
        pageSource.includes('count > 0 && canManageStoreProducts') &&
        pageSource.includes('getProductStoreRecords(record.productCode)'),
      '商品管理页面应新增分店记录数量列、点击处理、加载状态、请求竞态保护，并仅允许有分店商品权限时点击',
    )
    assert(
      pageSource.includes("t('posAdmin.products.storeRecords', '分店记录')") &&
        pageSource.includes("t('posAdmin.products.noStoreRecords', '暂无分店记录')") &&
        pageSource.includes("t('posAdmin.products.loadStoreRecordsFailed', '加载分店记录失败')"),
      '分店记录列和弹窗应有中文兜底文案',
    )
  })
  if (storeRecordsFailure) failures.push(storeRecordsFailure)

  const pushToHqFailure = await runTest('选中商品发送到 HQ 应复用选择、权限和防重复提交保护', () => {
    assert(
      typeSource.includes('PushProductsToHqRequest') &&
        typeSource.includes('productCodes: string[]') &&
        typeSource.includes('PushProductsToHqResult'),
      '类型层应声明发送到 HQ 的请求和结果契约',
    )
    assert(
      serviceSource.includes('pushProductsToHq') &&
        serviceSource.includes("`${API_BASE}/push-to-hq`") &&
        serviceSource.includes('normalizePushProductsToHqResult'),
      '服务层应提供固定接口调用，并归一后端统计字段',
    )
    assert(
      pageSource.includes('handlePushToHq') &&
        pageSource.includes('selectedRowKeys.map(String)') &&
        pageSource.includes('pushProductsToHq({') &&
        pageSource.includes('showPushToHqResult(result)'),
      '页面应把当前选中商品编码发送到 HQ，并展示成功和错误明细',
    )
    assert(
      pageSource.includes('extractPushToHqErrorResult(error)') &&
        pageSource.includes('payload.details') &&
        pageSource.includes('Modal.error({') &&
        pageSource.includes('errorResult.errors.join'),
      '发送到 HQ 失败时应从后端 data/details 中提取错误明细并弹窗展示',
    )
    assert(
      pageSource.includes('pushToHqAffectedRows') &&
        pageSource.includes('affectedRowCount') &&
        pageSource.includes('商品成功 {{success}}'),
      '发送到 HQ 结果应区分商品成功数和 HQ 影响记录数，避免统计语义混淆',
    )
    assert(
      pageSource.includes('if (!ensureCanManagePosProducts()) return') &&
        pageSource.includes('canManagePosProducts') &&
        pageSource.includes("t('posAdmin.products.pushToHq', '发送到HQ')"),
      '发送到 HQ 按钮和处理函数应复用 POS 商品管理权限',
    )
    assert(
      pageSource.includes('const pushToHqLoadingRef = useRef(false)') &&
        pageSource.includes('if (pushToHqLoadingRef.current) return') &&
        pageSource.includes('pushToHqLoadingRef.current = true') &&
        pageSource.includes('pushToHqLoadingRef.current = false') &&
        pageSource.includes('disabled={!selectedRowKeys.length || pushToHqLoading}'),
      '发送到 HQ 应使用 ref 锁和 loading 状态防止连续点击重复提交',
    )
  })
  if (pushToHqFailure) failures.push(pushToHqFailure)

  const jobEndpointFailure = await runTest('全量和增量应创建后台 job 而不是直接等待长同步请求', () => {
    assert(
      serviceSource.includes("`${SYNC_API_BASE}/products/jobs`") &&
        serviceSource.includes("`${SYNC_API_BASE}/products-incremental/jobs`") &&
        (serviceSource.includes("`${SYNC_API_BASE}/products/jobs/${encodeURIComponent(jobId)}`") ||
          serviceSource.includes("`${SYNC_API_BASE}/products/jobs/${jobId}`")),
      '服务层应提供商品 HQ 同步 job 创建和查询接口',
    )
    assert(
        pageSource.includes('createProductFullHqSyncJob({ operationId })') &&
        pageSource.includes('createProductIncrementalHqSyncJob({') &&
        pageSource.includes("const startDate = values.startDate ? values.startDate.format('YYYY-MM-DD')"),
      '页面应按同步模式分别创建全量/增量 job，增量需要传 YYYY-MM-DD 起始日期',
    )
    assert(
      !pageSource.includes('await syncProductsFromHqFull()') &&
        !pageSource.includes('await syncProductsFromHqIncremental({'),
      '页面不应继续直接等待长同步接口完成',
    )
  })
  if (jobEndpointFailure) failures.push(jobEndpointFailure)

  const syncResultMappingFailure = await runTest('HqProductSyncResult 与页面文案应切到新字段', () => {
    assert(
      typeSource.includes('productsAdded?: number') &&
        typeSource.includes('productsUpdated?: number') &&
        typeSource.includes('productsDeleted?: number'),
      'HqProductSyncResult 类型应声明 productsAdded/productsUpdated/productsDeleted',
    )
    assert(
      pageSource.includes('productsAdded') &&
        pageSource.includes('productsUpdated') &&
        pageSource.includes('productsDeleted'),
      '页面同步成功提示应读取 productsAdded/productsUpdated/productsDeleted',
    )
  })
  if (syncResultMappingFailure) failures.push(syncResultMappingFailure)

  const duplicateClickGuardFailure = await runTest('HQ 同步确认应防止连续点击重复提交', () => {
    assert(
      pageSource.includes('const [hqSyncSubmitting, setHqSyncSubmitting] = useState(false)') &&
        pageSource.includes('const hqSyncSubmittingRef = useRef(false)'),
      '页面应维护 hqSyncSubmitting 状态和 ref 锁',
    )
    assert(
      pageSource.includes('if (hqSyncSubmittingRef.current) return') &&
        pageSource.includes('hqSyncSubmittingRef.current = true') &&
        pageSource.includes('hqSyncSubmittingRef.current = false'),
      '同步处理函数应在连续点击时直接返回，并在结束后释放锁',
    )
    assert(
      pageSource.includes('confirmLoading={hqSyncSubmitting}') &&
        pageSource.includes('disabled={hqSyncSubmitting}'),
      '同步按钮和弹窗确认应绑定 submitting 状态',
    )
  })
  if (duplicateClickGuardFailure) failures.push(duplicateClickGuardFailure)

  const backgroundJobFailure = await runTest('HQ 同步应提交后台 job 后立即关闭弹窗并提示后台执行', () => {
    assert(
      pageSource.includes('setHqSyncVisible(false)') &&
        pageSource.includes('hqSyncJobSubmitted') &&
        pageSource.includes('startHqSyncJobPolling(activeJob)'),
      '创建 job 成功后应关闭弹窗、提示后台执行，并启动轮询',
    )
  })
  if (backgroundJobFailure) failures.push(backgroundJobFailure)

  const activeJobFailure = await runTest('HQ 同步 active job 应写入 localStorage 并在刷新后恢复轮询', () => {
    assert(
      pageSource.includes('PRODUCT_HQ_SYNC_ACTIVE_JOB_STORAGE_KEY') &&
        pageSource.includes('localStorage.setItem(PRODUCT_HQ_SYNC_ACTIVE_JOB_STORAGE_KEY') &&
        pageSource.includes('localStorage.removeItem(PRODUCT_HQ_SYNC_ACTIVE_JOB_STORAGE_KEY'),
      '页面应使用固定 key 保存和清理 active job',
    )
    assert(
      pageSource.includes('restoreActiveHqSyncJob()') &&
        pageSource.includes('readActiveProductHqSyncJob()') &&
        pageSource.includes('startHqSyncJobPolling(restoredJob)'),
      '页面刷新后应读取 active job 并恢复轮询',
    )
    assert(
      pageSource.includes('}, [stopHqSyncJobPolling])') &&
        pageSource.includes('}, [restoreActiveHqSyncJob])'),
      '卸载清理和恢复轮询应拆成独立 effect，避免分页/筛选变化误停轮询',
    )
  })
  if (activeJobFailure) failures.push(activeJobFailure)

  const hqSyncArchitectureFailure = await runTest('商品 HQ 同步页面应使用共享轮询器和统一 operationId', () => {
    assert(
      serviceSource.includes('export function buildProductHqSyncOperationId') &&
        pageSource.includes('buildProductHqSyncOperationId'),
      'operationId 应由服务层统一导出，页面不应维护另一套生成规则',
    )
    assert(
      serviceSource.includes('createProductHqSyncJobPoller') &&
        pageSource.includes('createProductHqSyncJobPoller'),
      '页面和服务兼容 wrapper 应共用商品 HQ 同步轮询器',
    )
    assert(
      !pageSource.includes("type HqSyncJobStatus = 'Queued'") &&
        !pageSource.includes('type ProductHqSyncJobResult = HqProductSyncResult &'),
      '页面不应镜像服务层已有 HQ 同步 job 类型',
    )
    assert(
      !pageSource.includes('setTimeout(poll, PRODUCT_HQ_SYNC_POLL_INTERVAL_MS)') &&
        !pageSource.includes('async function poll()'),
      '页面不应保留原始 setTimeout 轮询状态机',
    )
  })
  if (hqSyncArchitectureFailure) failures.push(hqSyncArchitectureFailure)

  const existingJobFailure = await runTest('已有 active job 时 HQ 同步按钮只展示状态不新建任务', () => {
    assert(
      pageSource.includes('const storedActiveJob = activeHqSyncJob ?? readActiveProductHqSyncJob()') &&
        pageSource.includes('showActiveHqSyncJobStatus(storedActiveJob)'),
      '打开 HQ 同步弹窗前应先判断 active job 并展示状态',
    )
    assert(
      pageSource.includes('hqSyncInProgress'),
      '已有任务时按钮应展示同步中状态',
    )
  })
  if (existingJobFailure) failures.push(existingJobFailure)

  const terminalResultFailure = await runTest('HQ 同步 job 完成、失败、Succeeded 加错误明细应展示友好结果', () => {
    assert(
      pageSource.includes("result.status === 'Failed'") &&
        pageSource.includes('hqSyncJobPartialSucceeded') &&
        pageSource.includes('hqSyncJobSucceeded') &&
        pageSource.includes('hqSyncJobTimeout'),
      '轮询终态应区分失败、完成、错误明细部分成功和超时',
    )
    assert(
      pageSource.includes('HqProductSyncPollingTimeoutError') &&
        pageSource.includes('showPollingTimeout()'),
      '轮询超时应由共享 poller 抛出专门错误，并走统一超时提示',
    )
    assert(
      pageSource.includes('incrementalStartDateRequired') &&
        pageSource.includes('allowClear={false}'),
      '增量同步起始日期应必填，避免提交无范围的增量 job',
    )
  })
  if (terminalResultFailure) failures.push(terminalResultFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\n- ${failures.join('\n- ')}`)
  }

  console.log('ProductManagement.hqSync.logic.test: ok')
}

await main()
