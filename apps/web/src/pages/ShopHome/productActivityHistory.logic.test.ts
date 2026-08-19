import type { StoreOrderProductActivityFilter } from '../../types/storeOrder'
import {
  createProductActivityHistoryRequestCoordinator,
  getProductActivityHistoryRequestIdentity,
  runProductActivityHistoryRequest,
} from './productActivityHistoryRequestCoordinator'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${label}。Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

function createDeferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })

  return { promise, resolve, reject }
}

interface ActivitySnapshot {
  id: string
  items: Array<{ recordType: string }>
}

function getIdentity(input: {
  open: boolean
  storeCode: string | null
  productCode: string | null
  page: number
  recordType: StoreOrderProductActivityFilter
  retryVersion: number
}) {
  return getProductActivityHistoryRequestIdentity(input)
}

async function main() {
  const coordinator = createProductActivityHistoryRequestCoordinator()
  const committed: ActivitySnapshot[] = []
  const errors: string[] = []
  let fetchCount = 0

  const request = (
    identity: string | null,
    deferred: ReturnType<typeof createDeferred<ActivitySnapshot>>,
  ) =>
    runProductActivityHistoryRequest({
      coordinator,
      identity,
      request: () => {
        fetchCount += 1
        return deferred.promise
      },
      onSuccess: (result) => committed.push(result),
      onError: () => errors.push(identity ?? 'closed'),
    })

  // open=false 必须连 request 函数都不进入，避免隐藏弹窗产生后台流量。
  const closedIdentity = getIdentity({
    open: false,
    storeCode: 'S1',
    productCode: 'P1',
    page: 1,
    recordType: 'all',
    retryVersion: 0,
  })
  coordinator.activate(closedIdentity)
  const closedDeferred = createDeferred<ActivitySnapshot>()
  await request(closedIdentity, closedDeferred)
  assertEqual(fetchCount, 0, '关闭弹窗不请求')

  const itemA = getIdentity({ open: true, storeCode: 'S1', productCode: 'P1', page: 1, recordType: 'all', retryVersion: 0 })
  const itemB = getIdentity({ open: true, storeCode: 'S1', productCode: 'P2', page: 1, recordType: 'all', retryVersion: 0 })
  const itemADeferred = createDeferred<ActivitySnapshot>()
  const itemBDeferred = createDeferred<ActivitySnapshot>()
  coordinator.activate(itemA)
  const itemARequest = request(itemA, itemADeferred)
  coordinator.activate(itemB)
  const itemBRequest = request(itemB, itemBDeferred)
  itemBDeferred.resolve({ id: 'item-b', items: [{ recordType: 'order' }] })
  await itemBRequest
  itemADeferred.resolve({ id: 'item-a-stale', items: [{ recordType: 'order' }] })
  await itemARequest
  assertDeepEqual(committed, [{ id: 'item-b', items: [{ recordType: 'order' }] }], '换商品后旧响应不得提交')

  const pageOne = getIdentity({ open: true, storeCode: 'S1', productCode: 'P2', page: 1, recordType: 'all', retryVersion: 0 })
  const pageTwo = getIdentity({ open: true, storeCode: 'S1', productCode: 'P2', page: 2, recordType: 'all', retryVersion: 0 })
  const pageOneDeferred = createDeferred<ActivitySnapshot>()
  const pageTwoDeferred = createDeferred<ActivitySnapshot>()
  coordinator.activate(pageOne)
  const pageOneRequest = request(pageOne, pageOneDeferred)
  coordinator.activate(pageTwo)
  const pageTwoRequest = request(pageTwo, pageTwoDeferred)
  pageTwoDeferred.resolve({ id: 'page-2', items: [] })
  await pageTwoRequest
  pageOneDeferred.resolve({ id: 'page-1-stale', items: [{ recordType: 'order' }] })
  await pageOneRequest
  assertDeepEqual(
    committed,
    [
      { id: 'item-b', items: [{ recordType: 'order' }] },
      { id: 'page-2', items: [] },
    ],
    '翻页后旧页响应不得提交，空 items 必须完整传递',
  )

  const filterAll = getIdentity({ open: true, storeCode: 'S1', productCode: 'P2', page: 1, recordType: 'all', retryVersion: 0 })
  const filterSales = getIdentity({ open: true, storeCode: 'S1', productCode: 'P2', page: 1, recordType: 'sales', retryVersion: 0 })
  const filterAllDeferred = createDeferred<ActivitySnapshot>()
  const filterSalesDeferred = createDeferred<ActivitySnapshot>()
  coordinator.activate(filterAll)
  const filterAllRequest = request(filterAll, filterAllDeferred)
  coordinator.activate(filterSales)
  const filterSalesRequest = request(filterSales, filterSalesDeferred)
  filterSalesDeferred.resolve({ id: 'filter-sales', items: [] })
  await filterSalesRequest
  filterAllDeferred.resolve({ id: 'filter-all-stale', items: [{ recordType: 'order' }] })
  await filterAllRequest
  assertDeepEqual(
    committed,
    [
      { id: 'item-b', items: [{ recordType: 'order' }] },
      { id: 'page-2', items: [] },
      { id: 'filter-sales', items: [] },
    ],
    '切换筛选后旧筛选响应不得提交',
  )

  const retryOne = getIdentity({ open: true, storeCode: 'S1', productCode: 'P2', page: 1, recordType: 'sales', retryVersion: 1 })
  const retryTwo = getIdentity({ open: true, storeCode: 'S1', productCode: 'P2', page: 1, recordType: 'sales', retryVersion: 2 })
  const retryOneDeferred = createDeferred<ActivitySnapshot>()
  const retryTwoDeferred = createDeferred<ActivitySnapshot>()
  coordinator.activate(retryOne)
  const retryOneRequest = request(retryOne, retryOneDeferred)
  coordinator.activate(retryTwo)
  const retryTwoRequest = request(retryTwo, retryTwoDeferred)
  retryOneDeferred.reject(new Error('stale retry failure'))
  await retryOneRequest
  retryTwoDeferred.resolve({ id: 'retry-2', items: [] })
  await retryTwoRequest
  assertDeepEqual(errors, [], '重试后的旧错误不得污染新状态')
  assertEqual(committed[committed.length - 1]?.id, 'retry-2', '最新重试结果提交')

  const closing = getIdentity({ open: true, storeCode: 'S1', productCode: 'P2', page: 1, recordType: 'sales', retryVersion: 3 })
  const closingDeferred = createDeferred<ActivitySnapshot>()
  coordinator.activate(closing)
  const closingRequest = request(closing, closingDeferred)
  coordinator.activate(null)
  closingDeferred.resolve({ id: 'closed-stale', items: [] })
  await closingRequest
  assertEqual(committed.some((value) => value.id === 'closed-stale'), false, '关闭后旧响应不得提交')

  const storeOne = getIdentity({ open: true, storeCode: 'S1', productCode: 'P2', page: 1, recordType: 'sales', retryVersion: 0 })
  const storeTwo = getIdentity({ open: true, storeCode: 'S2', productCode: 'P2', page: 1, recordType: 'sales', retryVersion: 0 })
  const storeOneDeferred = createDeferred<ActivitySnapshot>()
  const storeTwoDeferred = createDeferred<ActivitySnapshot>()
  coordinator.activate(storeOne)
  const storeOneRequest = request(storeOne, storeOneDeferred)
  coordinator.activate(storeTwo)
  const storeTwoRequest = request(storeTwo, storeTwoDeferred)
  storeOneDeferred.reject(new Error('stale store failure'))
  await storeOneRequest
  storeTwoDeferred.resolve({ id: 'store-2', items: [] })
  await storeTwoRequest
  assertDeepEqual(errors, [], '切店后的旧错误不得污染新状态')
  assertEqual(committed[committed.length - 1]?.id, 'store-2', '切店后只提交新门店响应')

  // salesSubtotal 小计行由后端返回，前端不得本地过滤；它必须原样提交，且同样受身份守卫保护。
  const subtotalIdentity = getIdentity({ open: true, storeCode: 'S3', productCode: 'P4', page: 1, recordType: 'sales', retryVersion: 0 })
  const subtotalDeferred = createDeferred<ActivitySnapshot>()
  coordinator.activate(subtotalIdentity)
  const subtotalRequest = request(subtotalIdentity, subtotalDeferred)
  subtotalDeferred.resolve({ id: 'subtotal-pass', items: [{ recordType: 'salesSubtotal' }] })
  await subtotalRequest
  assertEqual(committed[committed.length - 1]?.id, 'subtotal-pass', 'salesSubtotal 小计行必须原样提交，不得被前端本地过滤')

  assertEqual(fetchCount, 12, '仅有效打开态请求')

  // AbortController 取消旧请求时，AbortError 不得污染新状态；身份守卫继续丢弃过期结果。
  const beforeAbortCommitted = committed.length
  const beforeAbortErrors = errors.length
  const abortIdentity = getIdentity({ open: true, storeCode: 'S1', productCode: 'P3', page: 1, recordType: 'all', retryVersion: 0 })
  const abortController = new AbortController()
  const abortDeferred = createDeferred<ActivitySnapshot>()
  coordinator.activate(abortIdentity)
  const abortRun = runProductActivityHistoryRequest({
    coordinator,
    identity: abortIdentity,
    signal: abortController.signal,
    request: () => abortDeferred.promise,
    onSuccess: (result) => committed.push(result),
    onError: () => errors.push('aborted'),
  })
  abortController.abort()
  abortDeferred.reject(Object.assign(new Error('aborted'), { name: 'AbortError' }))
  await abortRun
  assertEqual(abortController.signal.aborted, true, 'AbortController 必须已取消')
  assertEqual(committed.length, beforeAbortCommitted, 'AbortError 不得提交成功结果')
  assertEqual(errors.length, beforeAbortErrors, 'AbortError 不得进入错误状态')

  console.log('productActivityHistory.logic.test: ok')
}

await main()
