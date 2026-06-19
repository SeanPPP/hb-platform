import {
  confirmInvoiceImport,
  previewInvoiceImport,
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
let confirmRequestUrl = ''
let confirmRequestMethod = ''
let confirmRequestBody: Record<string, unknown> | null = null

const testFile = new Blob(['demo'], {
  type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
}) as Blob & { name: string }
testFile.name = 'invoice.xlsx'

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  const url = String(input)

  if (url.endsWith('/api/react/v1/local-supplier-invoices/import/preview')) {
    previewRequestUrl = url
    previewRequestMethod = String(init?.method || '')
    previewRequestBody = init?.body as FormData

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

  return new Response(JSON.stringify({ success: true, data: null }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
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
} finally {
  globalThis.fetch = originalFetch
}

console.log('localSupplierInvoiceService.import.test: ok')
