import { getGridData } from './multiCodeSetService'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

const originalFetch = globalThis.fetch
let capturedUrl = ''
let capturedInit: RequestInit | undefined

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  capturedUrl = String(input)
  capturedInit = init

  return new Response(JSON.stringify({
    success: true,
    data: {
      total: 3,
      items: [
        {
          setCodeId: 'set-current-1',
          productCode: 'P-A',
          setBarcode: '111',
          setPurchasePrice: 1.2,
          setRetailPrice: 2.99,
          isActive: true,
        },
        {
          setCodeId: 'set-other-1',
          productCode: 'P-B',
          setBarcode: '222',
          setPurchasePrice: 1.5,
          setRetailPrice: 3.99,
          isActive: true,
        },
      ],
    },
  }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  const result = await getGridData({ productCode: 'P-A', pageIndex: 2, pageSize: 20 })

  assertEqual(
    capturedUrl,
    '/api/react/v1/product-set-codes/grid',
    '多码 grid 应请求 React 多码接口',
  )
  assertEqual(capturedInit?.method, 'POST', '多码 grid 应使用 POST')
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    {
      productCode: 'P-A',
      startRow: 20,
      endRow: 40,
      pageSize: 20,
      filterModel: {
        productCode: {
          filterType: 'text',
          type: 'equals',
          filter: 'P-A',
        },
      },
    },
    '多码 grid 请求必须按当前商品编码传递分页和兼容筛选',
  )
  assertDeepEqual(
    result,
    {
      total: 1,
      items: [
        {
          setCodeId: 'set-current-1',
          productCode: 'P-A',
          setBarcode: '111',
          setPurchasePrice: 1.2,
          setRetailPrice: 2.99,
          isActive: true,
          id: 'set-current-1',
        },
      ],
    },
    '多码 grid 响应必须归一化 setCodeId 并过滤其他商品数据',
  )
} finally {
  globalThis.fetch = originalFetch
}

console.log('multiCodeSetService.test: ok')
