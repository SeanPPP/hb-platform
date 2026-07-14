import { getOperationAuditDetail, getOperationAudits } from './operationAuditService'

function assertEqual(actual: unknown, expected: unknown, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const originalFetch = globalThis.fetch
const calls: string[] = []

globalThis.fetch = (async (input: RequestInfo | URL) => {
  const url = String(input)
  calls.push(url)
  const data = url.includes('/event-1')
    ? { eventId: 'event-1', operationType: 'SALE_COMPLETE', items: [] }
    : { items: [{ eventId: 'event-1' }], totalCount: 1, pageIndex: 2, pageSize: 20 }

  return new Response(JSON.stringify({ success: true, data }), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  })
}) as typeof fetch

async function run() {
  const page = await getOperationAudits({
    fromUtc: '2026-07-01T00:00:00.000Z',
    toUtc: '2026-07-08T00:00:00.000Z',
    storeCode: 'S01',
    pageNumber: 2,
    pageSize: 20,
    sortBy: 'amountDelta',
    sortOrder: 'asc',
  })

  assertEqual(
    calls[0],
    '/api/react/pos-operation-audits?fromUtc=2026-07-01T00%3A00%3A00.000Z&toUtc=2026-07-08T00%3A00%3A00.000Z&storeCode=S01&pageNumber=2&pageSize=20&sortBy=amountDelta&sortOrder=asc',
    '列表接口应使用只读 GET 查询参数',
  )
  assertEqual(page.total, 1, '列表接口应兼容 totalCount')
  assertEqual(page.pageNumber, 2, '列表接口应兼容 pageIndex')

  const detail = await getOperationAuditDetail('event-1')
  assertEqual(calls[1], '/api/react/pos-operation-audits/event-1', '详情接口应按 EventId 查询')
  assertEqual(detail.eventId, 'event-1', '详情接口应解包标准响应')
}

run()
  .then(() => {
    globalThis.fetch = originalFetch
    console.log('operationAuditService.test: ok')
  })
  .catch((error) => {
    globalThis.fetch = originalFetch
    console.error(error)
    process.exitCode = 1
  })
