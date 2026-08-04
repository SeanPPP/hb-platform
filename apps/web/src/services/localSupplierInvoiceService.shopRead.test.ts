import {
  getInvoiceDetailsGrid,
  getInvoiceFilterOptions,
  getShopLocalSupplierInvoice,
  getShopLocalSupplierInvoiceDetailsGrid,
  getShopLocalSupplierInvoiceFilterOptions,
  getShopLocalSupplierInvoiceGrid,
} from './localSupplierInvoiceService'
import type { ShopLocalSupplierInvoiceGridRequest } from '../types/localSupplierInvoice'
import { RequestError } from '../utils/request'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${message}。Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

async function assertRequestError(
  promise: Promise<unknown>,
  expectedMessage: string,
  message: string,
) {
  try {
    await promise
  } catch (error) {
    assertEqual(error instanceof RequestError, true, `${message}：应抛出 RequestError`)
    assertEqual((error as RequestError).status, 200, `${message}：HTTP 200 业务失败状态应保留`)
    assertEqual((error as RequestError).message, expectedMessage, `${message}：应保留后端错误消息`)
    return
  }

  throw new Error(`${message}：请求不应成功`)
}

const originalFetch = globalThis.fetch
const detailRequests: Array<{ url: string; method: string; body: Record<string, unknown> }> = []
const gridRequests: Array<{ url: string; method: string; body: Record<string, unknown> }> = []
let filterOptionsRequestUrl = ''
let headerRequestUrl = ''

function jsonResponse(payload: unknown) {
  return new Response(JSON.stringify(payload), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  const url = String(input)

  if (url.startsWith('https://api.ipify.org')) {
    return jsonResponse({ ip: '8.8.8.88' })
  }

  if (url.endsWith('/api/react/v1/local-supplier-invoices/shop/grid')) {
    const body = JSON.parse(String(init?.body || '{}')) as Record<string, unknown>
    gridRequests.push({ url, method: String(init?.method || 'GET'), body })
    const filterModel = body.filterModel as Record<string, { filter?: unknown }> | undefined
    if (filterModel?.ProductKeyword?.filter === 'FAIL_GRID') {
      return jsonResponse({ success: false, message: '商城进货单列表读取失败' })
    }

    return jsonResponse({
      success: true,
      data: {
        Items: [
          {
            InvoiceGUID: 'invoice-001',
            StoreCode: 'BANK',
            StoreName: 'Bankstown',
            SupplierCode: 'SUP-01',
            SupplierName: 'Supplier One',
            InvoiceNo: 'INV-001',
            OrderDate: '2026-08-01',
            InboundDate: '2026-08-02',
            TotalAmount: 35.5,
            ReceivedTotalAmount: 20,
            FlowStatus: 2,
            InboundStatus: 1,
            Remarks: 'Shop read',
            CreatedAt: '不应进入商城 DTO',
          },
          { InvoiceNo: 'BROKEN' },
        ],
        Total: 1,
      },
    })
  }

  if (url.includes('/api/react/v1/local-supplier-invoices/shop/filter-options')) {
    filterOptionsRequestUrl = url
    if (new URL(url, 'https://example.test').searchParams.get('storeCode') === 'FAIL_FILTER') {
      return jsonResponse({ success: false, message: '筛选选项读取失败' })
    }

    return jsonResponse({
      isSuccess: true,
      Data: {
        Suppliers: [
          { Value: 'SUP-01', Label: 'Supplier One' },
          { value: 'SUP-02', label: 'Supplier Two' },
          { Value: '', Label: 'Broken' },
        ],
      },
    })
  }

  if (url.includes('/details/grid')) {
    detailRequests.push({
      url,
      method: String(init?.method || 'GET'),
      body: JSON.parse(String(init?.body || '{}')) as Record<string, unknown>,
    })
    if (url.includes('/shop/bad-details/details/grid')) {
      return jsonResponse({ isSuccess: false, message: '进货单明细读取失败' })
    }

    return jsonResponse({
      isSuccess: true,
      Data: {
        Items: [
          {
            DetailGUID: 'detail-001',
            StoreProductCode: 'STORE-001',
            ProductCode: 'P001',
            ItemNumber: 'HB001',
            Barcode: '9350001',
            ProductName: 'Apple',
            ProductImage: 'https://example.test/apple.jpg',
            Specification: '1kg',
            Unit: 'ctn',
            Quantity: 2,
            LastPurchasePrice: 3,
            PurchasePrice: 3.5,
            RetailPrice: 5,
            Amount: 7,
            NewAutoRetailPrice: 5.5,
            InvoiceGUID: '不应进入商城 DTO',
          },
          { ItemNumber: 'BROKEN' },
        ],
        TotalCount: 1,
      },
    })
  }

  if (url.includes('/api/react/v1/local-supplier-invoices/shop/')) {
    headerRequestUrl = url
    if (url.endsWith('/shop/bad-header')) {
      return jsonResponse({ isSuccess: false, message: '进货单头读取失败' })
    }

    return jsonResponse({
      isSuccess: true,
      Data: {
        InvoiceGUID: 'header/guid ?#',
        StoreCode: 'BANK',
        StoreName: 'Bankstown',
        SupplierCode: 'SUP-01',
        SupplierName: 'Supplier One',
        InvoiceNo: 'INV-001',
        OrderDate: '2026-08-01',
        InboundDate: '2026-08-02',
        TotalAmount: 35.5,
        ReceivedTotalAmount: 20,
        FlowStatus: 2,
        InboundStatus: 1,
        Remarks: 'Shop read',
        AppGUID: '不应进入商城 DTO',
      },
    })
  }

  return jsonResponse({ success: true, data: null })
}) as typeof fetch

const validGridRequest: ShopLocalSupplierInvoiceGridRequest = {
  startRow: 20,
  endRow: 40,
  pageSize: 20,
  filterModel: {},
  sortModel: [{ colId: 'OrderDate', sort: 'desc' }],
}

try {
  assertEqual(
    getInvoiceFilterOptions,
    getShopLocalSupplierInvoiceFilterOptions,
    '筛选选项短名称应复用商城只读实现',
  )
  assertEqual(
    getInvoiceDetailsGrid,
    getShopLocalSupplierInvoiceDetailsGrid,
    '分页明细短名称应复用商城只读实现',
  )

  const grid = await getShopLocalSupplierInvoiceGrid(validGridRequest)
  assertEqual(gridRequests[0]?.method, 'POST', '商城列表接口应使用 POST')
  assertEqual(
    gridRequests[0]?.url.endsWith('/api/react/v1/local-supplier-invoices/shop/grid'),
    true,
    '商城列表必须调用独立 shop/grid 端点',
  )
  assertDeepEqual(gridRequests[0]?.body, validGridRequest, '商城列表请求体应原样发送 grid 契约')
  assertDeepEqual(
    grid,
    {
      items: [{
        invoiceGUID: 'invoice-001',
        storeCode: 'BANK',
        storeName: 'Bankstown',
        supplierCode: 'SUP-01',
        supplierName: 'Supplier One',
        invoiceNo: 'INV-001',
        orderDate: '2026-08-01',
        inboundDate: '2026-08-02',
        totalAmount: 35.5,
        receivedTotalAmount: 20,
        flowStatus: 2,
        inboundStatus: 1,
        remarks: 'Shop read',
      }],
      total: 1,
    },
    '商城列表应兼容 Pascal case 并只保留最小只读 DTO',
  )

  const header = await getShopLocalSupplierInvoice('header/guid ?#')
  assertEqual(
    headerRequestUrl.includes('/shop/header%2Fguid%20%3F%23'),
    true,
    '商城单头 invoiceGuid 应作为单一路径段安全编码',
  )
  assertDeepEqual(
    header,
    {
      invoiceGUID: 'header/guid ?#',
      storeCode: 'BANK',
      storeName: 'Bankstown',
      supplierCode: 'SUP-01',
      supplierName: 'Supplier One',
      invoiceNo: 'INV-001',
      orderDate: '2026-08-01',
      inboundDate: '2026-08-02',
      totalAmount: 35.5,
      receivedTotalAmount: 20,
      flowStatus: 2,
      inboundStatus: 1,
      remarks: 'Shop read',
    },
    '商城单头应归一化为最小 DTO',
  )

  const filterOptions = await getShopLocalSupplierInvoiceFilterOptions(' STORE / 01?x=1 ')
  const parsedFilterOptionsUrl = new URL(filterOptionsRequestUrl, 'https://example.test')
  assertEqual(
    parsedFilterOptionsUrl.pathname,
    '/api/react/v1/local-supplier-invoices/shop/filter-options',
    '筛选选项必须调用独立 shop/filter-options 端点',
  )
  assertEqual(
    parsedFilterOptionsUrl.searchParams.get('storeCode'),
    'STORE / 01?x=1',
    '筛选选项接口应安全编码并去除分店编码首尾空白',
  )
  assertDeepEqual(
    filterOptions,
    {
      suppliers: [
        { value: 'SUP-01', label: 'Supplier One' },
        { value: 'SUP-02', label: 'Supplier Two' },
      ],
    },
    '筛选选项响应应兼容 Pascal/camel case 并过滤无效项',
  )

  const details = await getShopLocalSupplierInvoiceDetailsGrid(
    'invoice/guid ?#',
    { page: 2, pageSize: 100 },
  )
  assertEqual(detailRequests[0]?.method, 'POST', '分页明细接口应使用 POST')
  assertEqual(
    detailRequests[0]?.url.includes('/shop/invoice%2Fguid%20%3F%23/details/grid'),
    true,
    '分页明细必须调用独立 shop 端点并安全编码 invoiceGuid',
  )
  assertDeepEqual(
    detailRequests[0]?.body,
    { startRow: 100, endRow: 200, pageSize: 100 },
    '分页明细接口应发送正确的行区间',
  )
  assertDeepEqual(
    details,
    {
      items: [{
        detailGUID: 'detail-001',
        storeProductCode: 'STORE-001',
        productCode: 'P001',
        itemNumber: 'HB001',
        barcode: '9350001',
        productName: 'Apple',
        specification: '1kg',
        unit: 'ctn',
        quantity: 2,
        lastPurchasePrice: 3,
        purchasePrice: 3.5,
        retailPrice: 5,
        amount: 7,
        productImage: 'https://example.test/apple.jpg',
        newAutoRetailPrice: 5.5,
      }],
      total: 1,
    },
    '分页明细应归一化为商城最小 DTO',
  )

  await getShopLocalSupplierInvoiceDetailsGrid('plain-guid', { page: -1, pageSize: 999 })
  assertDeepEqual(
    detailRequests[1]?.body,
    { startRow: 0, endRow: 50, pageSize: 50 },
    '非法详情分页应回退到第一页、每页 50 条',
  )

  await assertRequestError(
    getShopLocalSupplierInvoiceGrid({
      ...validGridRequest,
      filterModel: {
        ProductKeyword: { filterType: 'text', type: 'contains', filter: 'FAIL_GRID' },
      },
    }),
    '商城进货单列表读取失败',
    '列表 HTTP 200 业务失败不能归一化为空列表',
  )
  await assertRequestError(
    getShopLocalSupplierInvoiceDetailsGrid('bad-details', { page: 1, pageSize: 50 }),
    '进货单明细读取失败',
    '明细 HTTP 200 业务失败不能归一化为空列表',
  )
  await assertRequestError(
    getShopLocalSupplierInvoiceFilterOptions('FAIL_FILTER'),
    '筛选选项读取失败',
    '筛选选项 HTTP 200 业务失败不能归一化为空选项',
  )
  await assertRequestError(
    getShopLocalSupplierInvoice('bad-header'),
    '进货单头读取失败',
    '单头 HTTP 200 业务失败不能归一化为空对象',
  )
} finally {
  globalThis.fetch = originalFetch
}
