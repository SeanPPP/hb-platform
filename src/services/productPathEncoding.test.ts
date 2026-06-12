import { fixProductImage } from './domesticProductImportService'
import {
  deletePrefixCode,
  getProductsByPrefix,
  updatePrefixCode,
} from './productPrefixCodeService'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const originalFetch = globalThis.fetch
const calls: Array<{ url: string; method?: string }> = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  calls.push({
    url: String(input),
    method: init?.method,
  })
  return new Response(JSON.stringify({ success: true, data: true }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  await fixProductImage('AB/12?#', 'https://example.test/image.png')
  await updatePrefixCode('PX/12?#', {
    supplierCode: 'S01',
    prefixName: 'Prefix',
  })
  await deletePrefixCode('PX/12?#')
  await getProductsByPrefix('PX/12?#', { page: 2, pageSize: 30 })

  assertEqual(
    calls[0]?.url,
    '/api/react/v1/domestic-products/AB%2F12%3F%23/image',
    '商品图片修复接口应编码商品编码 path segment',
  )
  assertEqual(
    calls[1]?.url,
    '/api/react/v1/product-prefix-codes/PX%2F12%3F%23',
    '前缀更新接口应编码前缀编码 path segment',
  )
  assertEqual(
    calls[2]?.url,
    '/api/react/v1/product-prefix-codes/PX%2F12%3F%23',
    '前缀删除接口应编码前缀编码 path segment',
  )
  assertEqual(
    calls[3]?.url,
    '/api/react/v1/product-prefix-codes/PX%2F12%3F%23/products?page=2&pageSize=30',
    '前缀关联商品接口应编码前缀编码 path segment 并保留查询参数',
  )
} finally {
  globalThis.fetch = originalFetch
}

console.log('productPathEncoding.test: ok')
