import ExcelJS from 'exceljs'
import { getImageDownloadCandidates, populateProductGradesWorksheet } from './exportService'
import type { ProductGradeListItem } from '../types/productGrade'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function createSampleProduct(overrides: Partial<ProductGradeListItem> = {}): ProductGradeListItem {
  return {
    id: 'grade-1',
    productCode: 'product-1',
    grade: 'A',
    supplierCode: 'SUP001',
    supplierName: '义乌供应商',
    hbProductNo: 'HB100-001',
    productName: '测试商品',
    productImage: 'https://img.example.com/HB100-001.jpg',
    domesticPrice: 12.34,
    importPrice: 2.45,
    oemPrice: 4.56,
    barcode: '930000000001',
    createdAt: '2026-06-18T00:00:00Z',
    ...overrides,
  }
}

function withWindowOrigin(origin: string, run: () => void) {
  const originalWindow = Object.getOwnPropertyDescriptor(globalThis, 'window')
  Object.defineProperty(globalThis, 'window', {
    configurable: true,
    value: { location: { origin } },
  })
  try {
    run()
  } finally {
    if (originalWindow) {
      Object.defineProperty(globalThis, 'window', originalWindow)
    } else {
      Reflect.deleteProperty(globalThis, 'window')
    }
  }
}

{
  const workbook = new ExcelJS.Workbook()
  const worksheet = workbook.addWorksheet('商品等级')

  const result = populateProductGradesWorksheet(worksheet, [createSampleProduct()])

  assertEqual(result.productImageColIndex, 4, '默认导出应包含商品图片列')
  assertEqual(worksheet.getRow(1).getCell(5).value, '商品图片', '商品图片列应出现在条码后')
  assertEqual(worksheet.getRow(2).height, 58, '带图片导出应增加行高')
  assertEqual(worksheet.getRow(2).getCell(1).fill?.type, undefined, '表身不应设置斑马底色')
}

{
  const workbook = new ExcelJS.Workbook()
  const worksheet = workbook.addWorksheet('商品等级')

  const result = populateProductGradesWorksheet(worksheet, [createSampleProduct()], {
    includeProductImage: false,
  })

  assertEqual(result.productImageColIndex, -1, '关闭图片导出时不应创建图片列')
  assertEqual(worksheet.getRow(1).getCell(5).value, '商品名称', '关闭图片后商品名称列应前移')
  assertEqual(worksheet.getRow(2).height, 24, '不带图片导出应使用普通行高')
}

{
  const workbook = new ExcelJS.Workbook()
  const worksheet = workbook.addWorksheet('商品等级')

  populateProductGradesWorksheet(worksheet, [
    createSampleProduct(),
    createSampleProduct({
      id: 'grade-2',
      productCode: 'product-2',
      domesticPrice: undefined,
      importPrice: undefined,
      oemPrice: undefined,
    }),
  ])

  assertEqual(worksheet.getColumn('domesticPrice').numFmt, undefined, '价格格式不应写到整列元数据')
  assertEqual(worksheet.getRow(2).getCell(8).numFmt, '¥#,##0.00', '国内价应使用 RMB 格式')
  assertEqual(worksheet.getRow(2).getCell(9).numFmt, '$#,##0.00', '进口价应使用 AUD 格式')
  assertEqual(worksheet.getRow(2).getCell(10).numFmt, '$#,##0.00', '零售价应使用 AUD 格式')
  assertEqual(worksheet.getRow(3).getCell(8).value, '', '空国内价应保持空白')
  assertEqual(worksheet.getRow(3).getCell(9).value, '', '空进口价应保持空白')
  assertEqual(worksheet.getRow(3).getCell(10).value, '', '空零售价应保持空白')
}

withWindowOrigin('https://erp.example.com', () => {
  const candidates = getImageDownloadCandidates('https://img.supplier.example.com/a b.jpg?size=500')

  assertEqual(candidates.length, 2, '跨域图片应保留直连并追加代理兜底')
  assertEqual(candidates[0], 'https://img.supplier.example.com/a%20b.jpg?size=500', '跨域图片直连地址应先归一化')
  assertEqual(
    candidates[1],
    '/api/react/v1/image-proxy?url=https%3A%2F%2Fimg.supplier.example.com%2Fa%2520b.jpg%3Fsize%3D500',
    '跨域图片代理地址应编码原始 URL',
  )
})

withWindowOrigin('https://erp.example.com', () => {
  const candidates = getImageDownloadCandidates('/uploads/HB100-001.jpg')

  assertEqual(candidates.length, 1, '同源图片不应额外走代理')
  assertEqual(candidates[0], 'https://erp.example.com/uploads/HB100-001.jpg', '同源相对路径应归一化为绝对地址')
})
