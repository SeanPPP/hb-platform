import {
  confirmInvoiceImport,
  previewInvoiceImport,
  updateLastPurchasePrices,
} from './localSupplierInvoiceService'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertFormData(value: unknown, message: string): asserts value is FormData {
  if (!(value instanceof FormData)) {
    throw new Error(message)
  }
}

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

const originalFetch = globalThis.fetch
let previewRequestUrl = ''
let previewRequestMethod = ''
let previewRequestBody: unknown
let previewRequestHeaders: HeadersInit | undefined
let previewRequestCount = 0
let confirmRequestUrl = ''
let confirmRequestMethod = ''
let confirmRequestBody: Record<string, unknown> | null = null
let updateLastPurchaseRequestUrl = ''
let updateLastPurchaseRequestMethod = ''
let updateLastPurchaseRequestBody: Record<string, unknown> | null = null
let refreshRequestCount = 0
let previewResponses: Response[] = []

const testFile = new Blob(['demo'], {
  type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
}) as Blob & { name: string }
testFile.name = 'invoice.xlsx'

function createPreviewSuccessResponse() {
  return new Response(JSON.stringify({
    success: true,
    data: {
      sourceColumns: [{ key: 'col_1', header: '货号', sampleValue: 'HB001' }],
      recommendedMapping: {
        itemNumberColumnKey: 'col_1',
        barcodeColumnKey: 'col_2',
        productNameColumnKey: 'col_3',
        quantityColumnKey: 'col_4',
        priceColumnKey: 'col_5',
      },
      header: {
        storeCode: 'AU01',
        supplierCode: 'SUP01',
        invoiceNo: 'INV-001',
      },
      lines: [{
        rowNumber: 1,
        rawValues: {
          col_1: 'HB001',
          col_2: '935001',
          col_3: '苹果',
          col_4: '2',
          col_5: '3.50',
        },
      }],
      warnings: [],
      errors: [],
    },
  }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

function getHeaderValue(headers: HeadersInit | undefined, name: string) {
  if (!headers) {
    return undefined
  }
  if (headers instanceof Headers) {
    return headers.get(name) ?? undefined
  }
  if (Array.isArray(headers)) {
    const match = headers.find(([key]) => key.toLowerCase() === name.toLowerCase())
    return match?.[1]
  }
  return Object.entries(headers).find(([key]) => key.toLowerCase() === name.toLowerCase())?.[1]
}

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  const url = String(input)

  if (url.startsWith('https://api.ipify.org')) {
    return new Response(JSON.stringify({ ip: '8.8.8.88' }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }

  if (url.endsWith('/api/Auth/session/refresh')) {
    refreshRequestCount += 1
    return new Response(JSON.stringify({ success: true, data: {} }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }

  if (url.endsWith('/api/react/v1/local-supplier-invoices/import/preview')) {
    previewRequestCount += 1
    previewRequestUrl = url
    previewRequestMethod = String(init?.method || '')
    previewRequestBody = init?.body as FormData
    previewRequestHeaders = init?.headers

    return previewResponses.shift() ?? createPreviewSuccessResponse()
  }

  if (url.endsWith('/api/react/v1/local-supplier-invoices/import/confirm')) {
    confirmRequestUrl = url
    confirmRequestMethod = String(init?.method || '')
    confirmRequestBody = JSON.parse(String(init?.body || '{}')) as Record<string, unknown>

    return new Response(JSON.stringify({
      success: true,
      data: {
        invoiceGuid: 'invoice-guid-001',
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }

  if (url.endsWith('/api/react/v1/local-supplier-invoices/invoice-guid-001/details/update-last-purchase-prices')) {
    updateLastPurchaseRequestUrl = url
    updateLastPurchaseRequestMethod = String(init?.method || '')
    updateLastPurchaseRequestBody = JSON.parse(String(init?.body || '{}')) as Record<string, unknown>

    return new Response(JSON.stringify({
      success: true,
      data: {
        total: 2,
        updated: 1,
        skipped: 1,
        errors: ['明细 detail-2 跳过：未找到有效上次进货价'],
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }

  return new Response(JSON.stringify({ success: true, data: null }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  previewResponses = [createPreviewSuccessResponse()]
  const previewResult = await previewInvoiceImport(testFile as unknown as File)

  assertFormData(previewRequestBody, '预览导入应以 FormData 上传文件')
  assertEqual(previewRequestMethod, 'POST', '预览导入应使用 POST')
  assert(
    previewRequestUrl.endsWith('/api/react/v1/local-supplier-invoices/import/preview'),
    '预览导入应命中 import/preview 接口',
  )
  assertEqual(
    (previewRequestBody.get('file') as Blob | null)?.size,
    testFile.size,
    'FormData 中应包含上传文件',
  )
  assertEqual(
    getHeaderValue(previewRequestHeaders, 'Content-Type'),
    undefined,
    '预览 FormData 上传不应手动设置 Content-Type',
  )
  assertEqual(previewResult.header.invoiceNo, 'INV-001', '预览结果应返回头信息')

  const confirmResult = await confirmInvoiceImport({
    sourceColumns: [{ key: 'col_1', header: '货号', sampleValue: 'HB001' }],
    header: {
      storeCode: 'AU01',
      supplierCode: 'SUP01',
      invoiceNo: 'INV-001',
    },
    mapping: {
      itemNumberColumnKey: 'col_1',
      barcodeColumnKey: 'col_2',
      productNameColumnKey: 'col_3',
      quantityColumnKey: 'col_4',
      priceColumnKey: 'col_5',
    },
    lines: [{
      rowNumber: 1,
      rawValues: {
        col_1: 'HB001',
        col_2: '935001',
        col_3: '苹果',
        col_4: '2',
        col_5: '3.50',
      },
    }],
  })

  assertEqual(confirmRequestMethod, 'POST', '确认导入应使用 POST')
  assert(
    confirmRequestUrl.endsWith('/api/react/v1/local-supplier-invoices/import/confirm'),
    '确认导入应命中 import/confirm 接口',
  )
  assertDeepEqual(
    confirmRequestBody,
    {
      sourceColumns: [{ key: 'col_1', header: '货号', sampleValue: 'HB001' }],
      header: {
        storeCode: 'AU01',
        supplierCode: 'SUP01',
        invoiceNo: 'INV-001',
      },
      mapping: {
        itemNumberColumnKey: 'col_1',
        barcodeColumnKey: 'col_2',
        productNameColumnKey: 'col_3',
        quantityColumnKey: 'col_4',
        priceColumnKey: 'col_5',
      },
      lines: [{
        rowNumber: 1,
        rawValues: {
          col_1: 'HB001',
          col_2: '935001',
          col_3: '苹果',
          col_4: '2',
          col_5: '3.50',
        },
      }],
    },
    '确认导入应提交用户确认后的 sourceColumns、header、mapping 和 lines',
  )
  assertEqual(confirmResult.invoiceGuid, 'invoice-guid-001', '确认导入应返回新建进货单 GUID')

  previewRequestCount = 0
  refreshRequestCount = 0
  previewRequestBody = undefined
  previewRequestHeaders = undefined
  previewResponses = [
    new Response(JSON.stringify({ success: false, message: '登录已过期' }), {
      status: 401,
      headers: { 'Content-Type': 'application/json' },
    }),
    createPreviewSuccessResponse(),
  ]

  const retryPreviewResult = await previewInvoiceImport(testFile as unknown as File)

  assertEqual(refreshRequestCount, 1, '预览上传遇到 401 时应复用统一 request refresh')
  assertEqual(previewRequestCount, 2, 'refresh 成功后应重试预览上传')
  assertFormData(previewRequestBody, '重试预览导入仍应以 FormData 上传文件')
  assertEqual(
    getHeaderValue(previewRequestHeaders, 'Content-Type'),
    undefined,
    '重试预览 FormData 上传仍不应手动设置 Content-Type',
  )
  assertEqual(retryPreviewResult.header.invoiceNo, 'INV-001', 'refresh 后应返回重试的预览结果')

  const selectedUpdateResult = await updateLastPurchasePrices('invoice-guid-001', {
    detailGuids: ['detail-1'],
  })
  assertEqual(updateLastPurchaseRequestMethod, 'POST', '更新上次进货价应使用 POST')
  assert(
    updateLastPurchaseRequestUrl.endsWith('/api/react/v1/local-supplier-invoices/invoice-guid-001/details/update-last-purchase-prices'),
    '更新上次进货价应命中明细快照刷新接口',
  )
  assertDeepEqual(
    updateLastPurchaseRequestBody,
    { detailGuids: ['detail-1'] },
    '选中行更新上次进货价应提交 detailGuids',
  )
  assertEqual(selectedUpdateResult.updated, 1, '更新上次进货价应返回更新数量')

  await updateLastPurchasePrices('invoice-guid-001', {})
  assertDeepEqual(
    updateLastPurchaseRequestBody,
    {},
    '未选中行更新上次进货价应提交空对象表示整单刷新',
  )

  previewResponses = [
    new Response(JSON.stringify({ success: false, message: '导入模板错误' }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }),
  ]
  try {
    await previewInvoiceImport(testFile as unknown as File)
    throw new Error('预览导入返回 success=false 时应抛出业务错误')
  } catch (error) {
    assert(error instanceof Error, '预览导入业务失败应抛出 Error')
    assertEqual(error.message, '导入模板错误', '预览导入应保留统一业务错误信息')
  }
} finally {
  globalThis.fetch = originalFetch
}

console.log('localSupplierInvoiceService.import.test: ok')
