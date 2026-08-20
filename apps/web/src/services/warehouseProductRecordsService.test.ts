import {
  queryWarehouseProductAllocations,
  queryWarehouseProductContainers,
  queryWarehouseProductRecordSummary,
} from './warehouseProductRecordsService'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  assertEqual(JSON.stringify(actual), JSON.stringify(expected), message)
}

async function assertRejects(execute: () => Promise<unknown>, message: string) {
  try {
    await execute()
  } catch (error) {
    assertEqual(error instanceof Error ? error.message : String(error), message, '失败响应应透传后端消息')
    return
  }
  throw new Error('失败响应应拒绝 Promise')
}

const originalFetch = globalThis.fetch
let capturedUrl = ''
let capturedInit: RequestInit | undefined

try {
  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedInit = init
    return new Response(JSON.stringify({
      success: true,
      data: { productCode: 'P/1', itemNumber: 'I-1', barcode: 'B-1', productName: '商品', englishName: 'Product', imageUrl: null, isActive: true },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  await queryWarehouseProductRecordSummary('P/1')
  assertEqual(capturedUrl, '/api/react/v1/warehouse-product-records/P%2F1/summary', '商品摘要应编码商品编码并调用正确接口')
  assertEqual(capturedInit?.method, 'GET', '商品摘要应使用 GET')

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedInit = init
    return new Response(JSON.stringify({
      success: true,
      data: {
        totalCount: 1,
        pageNumber: 1,
        pageSize: 20,
        summary: { containerCount: 2, loadingPieces: 100, loadingQuantity: 80, totalAmount: 999.5 },
        items: [{
          detailCode: 'D-NULL',
          containerCode: 'C-NULL',
          loadingDate: '2026-01-01T00:00:00',
          status: null,
          loadingPieces: null,
          loadingQuantity: null,
          domesticPrice: null,
          importPrice: null,
          totalAmount: null,
        }],
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  const containerReport = await queryWarehouseProductContainers('P/1', {
    containerKeyword: 'C-1',
    arrivalStartDate: '2026-01-01',
    arrivalEndDate: '2026-01-31',
    statuses: [0, 1, 4],
    pageNumber: 2,
    pageSize: 50,
    sortBy: 'loadingDate',
    sortDirection: 'desc',
  })
  assertEqual(capturedUrl, '/api/react/v1/warehouse-product-records/P%2F1/containers/query', '货柜查询应编码商品编码并调用正确接口')
  assertEqual(capturedInit?.method, 'POST', '货柜查询应使用 POST')
  assertDeepEqual(JSON.parse(String(capturedInit?.body)), {
    containerKeyword: 'C-1',
    arrivalStartDate: '2026-01-01',
    arrivalEndDate: '2026-01-31',
    statuses: [0, 1, 4],
    pageNumber: 2,
    pageSize: 50,
    sortBy: 'loadingDate',
    sortDirection: 'desc',
  }, '货柜查询应原样映射关键字、到港日、状态、分页和排序请求')
  assertEqual(containerReport.items[0]?.status, null, '未知货柜状态不得归一化为草稿')
  assertEqual(containerReport.items[0]?.loadingQuantity, null, '缺失装柜数量不得归一化为 0')
  assertEqual(containerReport.items[0]?.loadingDate, '2026-01-01', '货柜日期应归一化为日期值')

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedInit = init
    return new Response(JSON.stringify({
      success: true,
      data: {
        summary: { allocationQuantity: 10, allocationAmount: 200, orderCount: 3 },
        branches: [{
          storeCode: '',
          storeName: '未匹配分店（无编码）',
          isActive: false,
          allocationQuantity: 10,
          allocationAmount: 200,
          orderCount: 3,
          firstAllocationDate: '2026-06-01T00:00:00',
          lastAllocationDate: '2026-06-30T00:00:00',
        }],
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  const allocationReport = await queryWarehouseProductAllocations('P/1', { startDate: '2026-06-01', endDate: '2026-06-30' })
  assertEqual(capturedUrl, '/api/react/v1/warehouse-product-records/P%2F1/allocations/query', '配货查询应编码商品编码并调用正确接口')
  assertEqual(capturedInit?.method, 'POST', '配货查询应使用 POST')
  assertDeepEqual(JSON.parse(String(capturedInit?.body)), {
    startDate: '2026-06-01',
    endDate: '2026-06-30',
  }, '配货查询应发送日期范围')
  assertEqual(allocationReport.branches.length, 1, '无分店编码的配货分组不得在前端丢失')
  assertEqual(allocationReport.branches[0]?.firstAllocationDate, '2026-06-01', '配货日期应归一化为日期值')

  globalThis.fetch = (async () => new Response(JSON.stringify({ success: false, message: '无权限查看' }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })) as typeof fetch
  await assertRejects(() => queryWarehouseProductRecordSummary('P/1'), '无权限查看')
} finally {
  globalThis.fetch = originalFetch
}

console.log('warehouseProductRecordsService.test: ok')
