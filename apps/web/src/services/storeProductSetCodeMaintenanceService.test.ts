import {
  createStoreProductSetCode,
  getStoreProductCodePage,
  saveStoreProductSetCodeSnapshot,
  updateStoreProductSetCode,
} from './storeProductSetCodeMaintenanceService'
import { RequestError } from '../utils/request'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

function assertEqual(actual: unknown, expected: unknown, message: string) {
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    throw new Error(`${message}. Expected: ${JSON.stringify(expected)}, received: ${JSON.stringify(actual)}`)
  }
}

async function captureRequest<T>(responseBody: unknown, execute: () => Promise<T>) {
  const originalFetch = globalThis.fetch
  let url = ''
  let method = ''
  let body: unknown
  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    url = String(input)
    method = String(init?.method)
    body = init?.body ? JSON.parse(String(init.body)) : undefined
    return new Response(JSON.stringify(responseBody), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  try {
    return { url: () => url, method: () => method, body: () => body, result: await execute() }
  } finally {
    globalThis.fetch = originalFetch
  }
}

const pageRequest = await captureRequest(
  {
    success: true,
    data: {
      items: [{ setCodeId: 'set-1', productCode: ' P/1 ', setBarcode: '000123', setPurchasePrice: 1.25, setRetailPrice: 3.5, isActive: true }],
      totalCount: 1,
      page: 1,
      pageSize: 100,
      hasMore: false,
    },
  },
  () => getStoreProductCodePage('P/1', { storeCode: '1013', type: 1, page: 1, pageSize: 100 }),
)
assertEqual(
  pageRequest.url(),
  '/api/react/v1/store-product-maintenance/P%2F1/codes?storeCode=1013&type=1&page=1&pageSize=100',
  '条码分页应编码商品号并限定分店和类型',
)
assertEqual(pageRequest.result.items[0], {
  setCodeId: 'set-1',
  productCode: ' P/1 ',
  barcode: '000123',
  purchasePrice: 1.25,
  retailPrice: 3.5,
  isActive: true,
  setType: 1,
}, '套装条码响应应归一化为弹窗统一模型')

const createRequest = await captureRequest(
  { success: true, data: { setCodeId: 'set-2' } },
  () => createStoreProductSetCode({
    productCode: 'P1',
    storeCode: '1013',
    productType: 1,
    barcode: '000456',
    retailPrice: 4.5,
    isActive: true,
  }),
)
assertEqual(createRequest.method(), 'POST', '新增套装条码应使用 POST')
assertEqual(createRequest.url(), '/api/react/v1/store-product-maintenance/set-codes', '新增套装条码应使用门店维护接口')
assertEqual(createRequest.body(), {
  productCode: 'P1',
  storeCode: '1013',
  productType: 1,
  barcode: '000456',
  retailPrice: 4.5,
  isActive: true,
}, '新增套装条码应明确传递 productType=1')

const snapshotRequest = await captureRequest(
  {
    success: true,
    data: {
      productCode: 'P1',
      storeCode: '1013',
      productType: 1,
      items: [{ setCodeId: 'set-1', productCode: 'P1', setBarcode: '000789', setRetailPrice: 6.5, setType: 1, isActive: true }],
    },
  },
  () => saveStoreProductSetCodeSnapshot({
    productCode: 'P1',
    storeCode: '1013',
    expectedProductType: 1,
    productType: 1,
    expectedItems: [{ setCodeId: 'set-1', barcode: '000456', retailPrice: 4.5, setType: 1, isActive: true }],
    items: [{ setCodeId: 'set-1', barcode: '000789', retailPrice: 6.5, setType: 1, isActive: true }],
  }),
)
assertEqual(snapshotRequest.method(), 'POST', '快照保存应使用 POST')
assertEqual(snapshotRequest.url(), '/api/react/v1/store-product-maintenance/set-codes/save-snapshot', '快照保存应使用单事务端点')
assertEqual(snapshotRequest.body(), {
  productCode: 'P1',
  storeCode: '1013',
  expectedProductType: 1,
  productType: 1,
  expectedItems: [{ setCodeId: 'set-1', barcode: '000456', retailPrice: 4.5, setType: 1, isActive: true }],
  items: [{ setCodeId: 'set-1', barcode: '000789', retailPrice: 6.5, setType: 1, isActive: true }],
}, '快照保存应一次提交期望状态与目标状态')
assertEqual(snapshotRequest.result, {
  productCode: 'P1',
  storeCode: '1013',
  productType: 1,
  items: [{
    setCodeId: 'set-1',
    productCode: 'P1',
    barcode: '000789',
    purchasePrice: null,
    retailPrice: 6.5,
    isActive: true,
    setType: 1,
  }],
}, '快照保存结果应归一化为完整服务端快照')

const typeTwoSnapshotRequest = await captureRequest(
  {
    success: true,
    data: {
      productCode: 'P2',
      storeCode: '1013',
      productType: 2,
      items: [{ setCodeId: 'set-2', productCode: 'P2', setBarcode: 'TYPE2-BARCODE', setPurchasePrice: 2.5, setRetailPrice: 5.5, setType: 2, isActive: true }],
    },
  },
  () => saveStoreProductSetCodeSnapshot({
    productCode: 'P2',
    storeCode: '1013',
    expectedProductType: 2,
    productType: 2,
    expectedItems: [],
    items: [],
  }),
)
assertEqual(typeTwoSnapshotRequest.result.items[0], {
  setCodeId: 'set-2',
  productCode: 'P2',
  barcode: 'TYPE2-BARCODE',
  purchasePrice: 2.5,
  retailPrice: 5.5,
  isActive: true,
  setType: 2,
}, '多码快照应兼容统一端点返回的 SetBarcode 和 SetPrice 字段')

try {
  await captureRequest(
    { success: false, code: 'BARCODE_EXISTS', message: '条码已存在' },
    () => updateStoreProductSetCode('set/2', {
      storeCode: '1013',
      barcode: '000456',
      retailPrice: 4.5,
      isActive: true,
    }),
  )
  throw new Error('HTTP 200 业务失败不应被当作保存成功')
} catch (error) {
  assert(error instanceof RequestError, 'HTTP 200 业务失败应转为 RequestError')
  assert(error.message.includes('BARCODE_EXISTS'), '业务错误应保留错误码')
}
