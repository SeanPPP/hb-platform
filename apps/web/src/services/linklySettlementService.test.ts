import {
  exportLinklySettlements,
  downloadLinklySettlementExport,
  getLinklySettlementDetail,
  getLinklySettlements,
  parseContentDispositionFileName,
} from './linklySettlementService'
import type { LinklySettlementFilters, LinklySettlementListQuery } from '../types/linklySettlement'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) throw new Error(`${label}: expected ${String(expected)}, got ${String(actual)}`)
}

const originalFetch = globalThis.fetch
const calls: Array<{ url: string; init?: RequestInit }> = []
let responseFactory: () => Response
const largeSettlementId = '9007199254740993'

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  calls.push({ url: String(input), init })
  return responseFactory()
}) as typeof fetch

const filters: LinklySettlementFilters = {
  businessDateFrom: '2026-08-01',
  businessDateTo: '2026-08-03',
  storeCode: '001',
  deviceCode: 'POS-1',
  connectionMode: 'CloudBackendAsync',
  environment: 'Production',
  status: 'Succeeded',
  providerSubmissionState: 'Submitted',
  keyword: 'approved',
  sortBy: 'requestedAtUtc',
  sortOrder: 'desc',
}

try {
  responseFactory = () => new Response(JSON.stringify({
    success: true,
    data: { items: [{ id: largeSettlementId }], totalCount: 1, pageNumber: 2, pageSize: 20 },
  }), { status: 200, headers: { 'content-type': 'application/json' } })

  const controller = new AbortController()
  const page = await getLinklySettlements({ ...filters, pageNumber: 2, pageSize: 20 } as LinklySettlementListQuery, controller.signal)
  const listCall = calls[0]
  assert(listCall.url.startsWith('/api/react/v1/linkly-settlements?'), '列表 URL 应使用锁定 API')
  for (const expected of [
    'businessDateFrom=2026-08-01', 'businessDateTo=2026-08-03', 'storeCode=001',
    'deviceCode=POS-1', 'connectionMode=CloudBackendAsync', 'environment=Production',
    'status=Succeeded', 'providerSubmissionState=Submitted', 'keyword=approved',
    'sortBy=requestedAtUtc', 'sortOrder=desc', 'pageNumber=2', 'pageSize=20',
  ]) assert(listCall.url.includes(expected), `列表参数缺少 ${expected}`)
  assertEqual(listCall.init?.credentials, 'include', '列表请求应携带 cookie')
  assertEqual(listCall.init?.signal, controller.signal, '列表请求应传递 AbortSignal')
  assertEqual(page.pageNumber, 2, '分页应读取 pageNumber')
  assertEqual(page.total, 1, '分页应兼容 totalCount')
  assertEqual(page.items[0]?.id, largeSettlementId, '列表 BIGINT ID 必须以十进制字符串精确往返')

  responseFactory = () => new Response(JSON.stringify({
    success: true,
    data: {
      id: largeSettlementId,
      cloudBackendSessionId: largeSettlementId,
      clientRevision: largeSettlementId,
      receipts: ['receipt 1'],
      cardTotals: [],
    },
  }), { status: 200, headers: { 'content-type': 'application/json' } })
  const detail = await getLinklySettlementDetail(largeSettlementId)
  assertEqual(calls[1].url, `/api/react/v1/linkly-settlements/${largeSettlementId}`, '详情 URL 必须保留 BIGINT 原字符串')
  assertEqual(detail.id, largeSettlementId, '详情 BIGINT ID 必须精确往返')
  assertEqual(detail.cloudBackendSessionId, largeSettlementId, 'CloudBackendSessionId 必须精确往返')
  assertEqual(detail.clientRevision, largeSettlementId, 'ClientRevision 必须精确往返')
  assertEqual(detail.receipts[0], 'receipt 1', '详情应解包 receipts')

  responseFactory = () => new Response(new Blob(['xlsx-bytes'], {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  }), {
    status: 200,
    headers: {
      'content-type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
      'content-disposition': "attachment; filename*=UTF-8''Linkly%20Settlements.xlsx",
    },
  })
  const exported = await exportLinklySettlements(filters)
  const exportCall = calls[2]
  assertEqual(exportCall.url, '/api/react/v1/linkly-settlements/export', '导出 URL')
  assertEqual(exportCall.init?.method, 'POST', '导出方法')
  assertEqual(exportCall.init?.credentials, 'include', '导出请求应携带 cookie')
  assertEqual((JSON.parse(String(exportCall.init?.body)) as LinklySettlementFilters).keyword, 'approved', '导出 body 应保留当前筛选')
  assertEqual(exported.fileName, 'Linkly Settlements.xlsx', '应解析 UTF-8 文件名')
  assertEqual(await exported.blob.text(), 'xlsx-bytes', '应返回 xlsx blob')
  assertEqual(parseContentDispositionFileName('attachment; filename="safe.xlsx"'), 'safe.xlsx', '应解析普通文件名')

  const urlApi = URL as typeof URL & {
    createObjectURL?: (blob: Blob) => string
    revokeObjectURL?: (url: string) => void
  }
  const originalCreateObjectUrl = urlApi.createObjectURL
  const originalRevokeObjectUrl = urlApi.revokeObjectURL
  const originalDocument = globalThis.document
  let clicked = false
  let revokedUrl = ''
  Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: () => 'blob:linkly-export' })
  Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: (url: string) => { revokedUrl = url } })
  Object.defineProperty(globalThis, 'document', {
    configurable: true,
    value: {
      createElement: () => ({
        href: '', download: '', style: {},
        click: () => { clicked = true },
        remove: () => {},
      }),
      body: { appendChild: () => {} },
    },
  })
  try {
    downloadLinklySettlementExport(exported)
    assertEqual(clicked, true, '导出应触发浏览器下载')
    assertEqual(revokedUrl, 'blob:linkly-export', '导出后必须释放 Blob URL')
  } finally {
    Object.defineProperty(URL, 'createObjectURL', { configurable: true, value: originalCreateObjectUrl })
    Object.defineProperty(URL, 'revokeObjectURL', { configurable: true, value: originalRevokeObjectUrl })
    Object.defineProperty(globalThis, 'document', { configurable: true, value: originalDocument })
  }

  responseFactory = () => new Response(JSON.stringify({
    success: false,
    errorCode: 'EXPORT_LIMIT_EXCEEDED',
    message: '导出结果超过 5000 行上限',
  }), { status: 400, headers: { 'content-type': 'application/problem+json' } })
  let errorMessage = ''
  try {
    await exportLinklySettlements(filters)
  } catch (error) {
    errorMessage = error instanceof Error ? error.message : String(error)
  }
  assert(errorMessage.includes('5000'), 'JSON ApiResponse 错误必须展示服务端 5000 行提示')

  console.log('linklySettlementService.test: ok')
} finally {
  globalThis.fetch = originalFetch
}
