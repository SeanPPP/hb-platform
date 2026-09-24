import assert from 'node:assert/strict'
import ExcelJS from 'exceljs'
import { getImageDownloadCandidates } from '../../../services/exportService'
import { buildSalesDetailWorkbook, selectCurrentPageExportRows } from './export'
import type { SalesDetailQuery, SalesDetailRow } from './reportService'

const row = (index: number): SalesDetailRow => ({
  code: `P${index}`, itemNumber: `00${index}`, name: `商品 ${index}`,
  productImage: index === 1 ? 'https://example.com/one.jpg' : index === 2 ? 'https://example.com/missing.jpg' : '',
  revenue: 50, compareRevenue: 25, quantity: 5, compareQuantity: 4,
  averageUnitPrice: 10, compareAverageUnitPrice: 6.25,
  grossProfit: null, compareGrossProfit: null, grossMarginRate: null, compareGrossMarginRate: null,
  orderCount: null, compareOrderCount: null, averageTransaction: null, compareAverageTransaction: null,
  share: null, compareShare: null, chinaShare: null, compareChinaShare: null,
})
const query: SalesDetailQuery = {
  kind: 'australia', startDate: '2026-09-01', endDate: '2026-09-23', compareMode: 'ByWeek',
  selectedSupplierCode: 'S1', search: '商品', pageIndex: 2, pageSize: 20,
}
const controller = new AbortController()
const currentPage = { rows: [row(21), row(22)], total: 13271 }
assert.deepEqual(selectCurrentPageExportRows(currentPage, controller.signal).map(item => item.code), ['P21', 'P22'],
  '默认仅导出当前页，不能因总数大而读取全部结果')
assert.throws(() => selectCurrentPageExportRows({ rows: Array.from({ length: 501 }, (_, index) => row(index)), total: 501 }, controller.signal),
  /最多 500/, '不得绕过分页上限生成无限大的带图工作簿')
const salesProxy = getImageDownloadCandidates('https://hotbargain-yw-2023-1300114625.cos.ap-shanghai.myqcloud.com/a.jpg',
  '/api/react/v1/image-proxy/sales-detail')
assert.match(salesProxy[0], /^\/api\/react\/v1\/image-proxy\/sales-detail\?url=/)
const cancelled = new AbortController()
cancelled.abort()
assert.throws(() => selectCurrentPageExportRows(currentPage, cancelled.signal), { name: 'AbortError' })

const png = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO/aAooAAAAASUVORK5CYII='
const loaded: string[] = []
const { workbook, failedImages } = await buildSalesDetailWorkbook([row(1), row(2), row(3)], {
  compare: true, english: false, startDate: query.startDate, endDate: query.endDate,
  signal: controller.signal, loadImage: async url => { loaded.push(url); return url.endsWith('one.jpg') ? png : null },
})
assert.equal(failedImages, 1)
assert.deepEqual(loaded, ['https://example.com/one.jpg', 'https://example.com/missing.jpg'])
const roundTrip = new ExcelJS.Workbook()
await roundTrip.xlsx.load(await workbook.xlsx.writeBuffer() as ArrayBuffer)
const sheet = roundTrip.worksheets[0]!
const column = (header: string) => {
  const index = Array.from({ length: sheet.columnCount }, (_, offset) => sheet.getRow(3).getCell(offset + 1).value).indexOf(header)
  assert.ok(index >= 0, `缺少列：${header}`)
  return index + 1
}
assert.equal(sheet.getCell('A1').value, '销售商品明细')
assert.equal(sheet.getCell('A3').value, '货号')
assert.equal(sheet.getRow(4).getCell(column('货号')).value, '001', '货号前导零必须保留')
assert.equal(sheet.getRow(4).getCell(column('营业额')).value, 50)
assert.equal(sheet.getRow(4).getCell(column('营业额增长率')).value, 1)
assert.equal(sheet.getRow(4).getCell(column('毛利额')).value, null, '成本未知不得写成零')
assert.equal(sheet.getRow(5).getCell(column('商品图片')).value, '图片读取失败')
assert.equal(sheet.getImages().length, 1, 'Excel 文件须真实嵌入图片')

const maxRows = Array.from({ length: 500 }, (_, index) => ({ ...row(index + 1),
  productImage: `https://example.com/${index + 1}.png` }))
const maxWorkbook = await buildSalesDetailWorkbook(maxRows, {
  compare: false, english: false, startDate: query.startDate, endDate: query.endDate,
  signal: controller.signal, loadImage: async () => png,
})
const maxRoundTrip = new ExcelJS.Workbook()
await maxRoundTrip.xlsx.load(await maxWorkbook.workbook.xlsx.writeBuffer() as ArrayBuffer)
assert.equal(maxRoundTrip.worksheets[0]!.getImages().length, 500, '上限 500 件时仍应完整写入图片')

const plain = await buildSalesDetailWorkbook([row(3)], {
  compare: false, english: true, startDate: query.startDate, endDate: query.endDate,
  signal: controller.signal, loadImage: async () => null,
})
const plainSheet = plain.workbook.worksheets[0]!
assert.equal(Array.from({ length: plainSheet.columnCount }, (_, index) => plainSheet.getRow(3).getCell(index + 1).value).includes('Previous revenue'), false)
console.log('销售商品明细导出：当前页范围、Excel 数值与 500 张嵌入图片通过')
