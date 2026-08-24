import {
  RETAIL_PRICE_CHANGES_PDF_PAGE_SIZE,
  buildRetailPriceChangesPdfFileName,
  collectRetailPriceChangesForPdf,
  mapRetailPriceChangesPdfRows,
} from './pdfExport'
import type { RetailPriceChangeItem, RetailPriceChangesPagedResult } from './logic'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) throw new Error(`${message}: expected ${String(expected)}, received ${String(actual)}`)
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) throw new Error(`${message}: expected ${expectedJson}, received ${actualJson}`)
}

function createItems(start: number, count: number): RetailPriceChangeItem[] {
  return Array.from({ length: count }, (_, index) => ({
    productCode: `P-${start + index}`,
    itemNumber: `HB-${start + index}`,
    barcode: `400638133${String(start + index).padStart(3, '0')}`,
    latestRetailPrice: 2.5,
    lastPriceChangedAtUtc: '2026-08-13T06:58:06Z',
  }))
}

const pageCalls: Array<[number, number]> = []
const pages = new Map<number, RetailPriceChangesPagedResult>([
  [1, { items: createItems(1, 100), total: 201, pageNumber: 1, pageSize: 100 }],
  [2, { items: createItems(101, 100), total: 201, pageNumber: 2, pageSize: 100 }],
  [3, { items: createItems(201, 1), total: 201, pageNumber: 3, pageSize: 100 }],
])

const allItems = await collectRetailPriceChangesForPdf(async (pageNumber, pageSize) => {
  pageCalls.push([pageNumber, pageSize])
  const page = pages.get(pageNumber)
  if (!page) throw new Error(`unexpected page ${pageNumber}`)
  return page
})

assertEqual(RETAIL_PRICE_CHANGES_PDF_PAGE_SIZE, 100, 'PDF 导出必须使用后端允许的最大分页大小')
assertDeepEqual(pageCalls, [[1, 100], [2, 100], [3, 100]], 'PDF 导出必须读取全部匹配分页而不是只导出当前页')
assertEqual(allItems.length, 201, 'PDF 导出必须返回全部匹配记录')
assertEqual(allItems[200]?.productCode, 'P-201', 'PDF 导出必须保持后端稳定排序')

const pdfRows = mapRetailPriceChangesPdfRows([
  {
    productCode: 'P-001',
    productImage: 'https://cdn.example.test/P-001.jpg',
    itemNumber: 'HB001',
    barcode: '4006381333931',
    latestRetailPrice: 12.5,
    lastPriceChangedAtUtc: '2026-08-13T06:58:06Z',
  },
  {
    productCode: 'P-EMPTY',
    latestRetailPrice: null,
  },
], (value) => value ? `Brisbane:${value}` : '--')

assertDeepEqual(pdfRows[0], {
  productImage: 'https://cdn.example.test/P-001.jpg',
  itemNumber: 'HB001',
  barcode: '4006381333931',
  barcodeImage: '4006381333931',
  latestRetailPrice: 12.5,
  lastPriceChangedAt: 'Brisbane:2026-08-13T06:58:06Z',
}, 'PDF 行必须包含图片、货号、条码图、价格和 Brisbane 时间')
assertEqual(pdfRows[1]?.itemNumber, '--', 'PDF 缺失货号必须显示占位符')
assertEqual(pdfRows[1]?.barcode, '', 'PDF 缺失条码不得生成伪条码图')
assertEqual(pdfRows[1]?.latestRetailPrice, undefined, 'PDF 空价格必须保持为空')
assertEqual(pdfRows[1]?.lastPriceChangedAt, '--', 'PDF 缺失时间必须显示占位符')

assertEqual(
  buildRetailPriceChangesPdfFileName('仓库/零售价变化', { startDate: '2026-08-01', endDate: '2026-08-31' }),
  '仓库-零售价变化_2026-08-01_2026-08-31',
  'PDF 文件名必须包含筛选日期并移除路径字符',
)

console.log('retailPriceChanges.pdfExport.test: ok')
