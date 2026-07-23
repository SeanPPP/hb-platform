import { readFileSync } from 'node:fs'
import path from 'node:path'
import { syncLocalSuppliersToHq } from './localSupplierService'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${message}。Expected: ${expectedJson}, received: ${actualJson}`)
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
      createdCount: 1,
      updatedCount: 1,
      deactivatedCount: 0,
      skippedCount: 0,
      errors: [],
    },
  }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  const result = await syncLocalSuppliersToHq(['AUS-001', 'AUS-002'])

  assert(
    capturedUrl === '/api/react/v1/local-suppliers/sync-to-hq',
    '澳洲供应商应请求独立的 HQ 同步端点',
  )
  assert(capturedInit?.method === 'POST', '澳洲供应商 HQ 同步应使用 POST')
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    { supplierCodes: ['AUS-001', 'AUS-002'] },
    'HQ 同步请求只应提交所选供应商代码',
  )
  assertDeepEqual(
    result,
    {
      createdCount: 1,
      updatedCount: 1,
      deactivatedCount: 0,
      skippedCount: 0,
      errors: [],
    },
    '应返回 HQ 同步统计',
  )
} finally {
  globalThis.fetch = originalFetch
}

const pageFile = path.resolve(
  process.cwd(),
  'src/pages/PosAdmin/SupplierManagement/index.tsx',
)
const pageSource = readFileSync(pageFile, 'utf8')
assert(
  pageSource.includes('rowKey="localSupplierCode"')
    && pageSource.includes('preserveSelectedRowKeys: true'),
  '供应商表格应以供应商代码保留跨页选择',
)
assert(
  pageSource.includes('disabled={!selectedRowKeys.length || syncingFromHq}'),
  '未选择供应商或正在从 HQ 同步时应禁用写入 HQ',
)
assert(
  pageSource.includes('const result = await syncLocalSuppliersToHq(selectedRowKeys.map(String))'),
  '页面应只把所选供应商代码同步到 HQ',
)
assert(
  pageSource.includes('Modal.confirm({')
    && pageSource.includes("'posAdmin.suppliers.syncToHqConfirm'"),
  '写入 HQ 前应显示影响范围确认',
)

console.log('localSupplierService.test: ok')
