import {
  PDF_IMAGE_FORMAT,
  PDF_IMAGE_MIME_TYPE,
  PDF_IMAGE_QUALITY,
  buildDocumentFileName,
  buildPdfSlicePlan,
  collectElementBreakOffsets,
  formatDocumentFileDate,
  formatPrintDate,
  getPdfSliceImageData,
  paintPdfSlice,
} from './printUtils'
import fs from 'node:fs'
import path from 'node:path'

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualText = JSON.stringify(actual)
  const expectedText = JSON.stringify(expected)
  if (actualText !== expectedText) {
    throw new Error(`${label}。Expected: ${expectedText}, received: ${actualText}`)
  }
}

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function runTest(name: string, execute: () => void) {
  execute()
  console.log(`ok - ${name}`)
}

// 这里锁定 PDF 切片规则，避免浮点高度和空白切片重新引入 wrong PNG signature。
runTest('切片高度应向下取整为整数像素，并完整覆盖剩余高度', () => {
  const slices = buildPdfSlicePlan(250, 104.7)
  assertDeepEqual(slices, [
    { offsetY: 0, height: 104 },
    { offsetY: 104, height: 104 },
    { offsetY: 208, height: 42 },
  ], '浮点页高应切成整数像素切片')
})

runTest('页高小于 1 像素时也不应生成空切片', () => {
  const slices = buildPdfSlicePlan(3, 0.4)
  assertDeepEqual(slices, [
    { offsetY: 0, height: 1 },
    { offsetY: 1, height: 1 },
    { offsetY: 2, height: 1 },
  ], '极小页高时应回退到 1 像素切片')
  assertEqual(slices.reduce((sum, item) => sum + item.height, 0), 3, '所有切片高度之和应等于原图高度')
})

runTest('PDF 切片应优先贴合行边界，避免把表格行切成两半', () => {
  const slices = buildPdfSlicePlan(260, 100, [30, 90, 150, 210, 260])
  assertDeepEqual(slices, [
    { offsetY: 0, height: 90 },
    { offsetY: 90, height: 60 },
    { offsetY: 150, height: 60 },
    { offsetY: 210, height: 50 },
  ], '切片结束位置应优先选择当前页内容范围内的最后一个行边界')
})

runTest('没有可用行边界时 PDF 切片应回退到标准页高', () => {
  const slices = buildPdfSlicePlan(260, 100, [140, 260])
  assertDeepEqual(slices, [
    { offsetY: 0, height: 100 },
    { offsetY: 100, height: 40 },
    { offsetY: 140, height: 100 },
    { offsetY: 240, height: 20 },
  ], '行边界不在当前页范围内时应保持前进，避免死循环')
})

runTest('PDF 避免切断偏移应从明细行、页脚和滚动高度收集', () => {
  const createElement = (top: number) => ({
    getBoundingClientRect: () => ({ top }),
  })
  const root = {
    scrollHeight: 260,
    getBoundingClientRect: () => ({ top: 10 }),
    querySelectorAll: (selector: string) => selector === 'tbody tr' ? [createElement(20), createElement(80), createElement(140)] : [],
    querySelector: (selector: string) => selector === '.footer' ? createElement(220) : null,
  } as unknown as HTMLElement

  assertDeepEqual(
    collectElementBreakOffsets(root, 'tbody tr', '.footer'),
    [10, 70, 130, 210, 260],
    '应返回相对根元素的行顶部、页脚顶部和根滚动高度',
  )
})

runTest('PDF 图片输出应锁定 JPEG 格式，避免回退到 PNG', () => {
  const calls: Array<[string, number]> = []
  const fakeCanvas = {
    toDataURL: (mimeType: string, quality: number) => {
      calls.push([mimeType, quality])
      return 'data:image/jpeg;base64,abc'
    },
  } as HTMLCanvasElement

  const imageData = getPdfSliceImageData(fakeCanvas)
  assertEqual(PDF_IMAGE_FORMAT, 'JPEG', '写入 jsPDF 的图片格式应为 JPEG')
  assertEqual(PDF_IMAGE_MIME_TYPE, 'image/jpeg', 'canvas 输出 MIME 应为 image/jpeg')
  assertEqual(PDF_IMAGE_QUALITY, 0.95, 'JPEG 质量应保持 0.95')
  assertEqual(imageData, 'data:image/jpeg;base64,abc', '应返回 canvas 输出的 JPEG data URL')
  assertDeepEqual(calls, [['image/jpeg', 0.95]], 'toDataURL 应按 JPEG 参数调用')
})

runTest('文件名日期应固定格式化为 yyyy-MM-dd', () => {
  assertEqual(formatDocumentFileDate('2026-06-04T12:30:00'), '2026-06-04', 'ISO 日期应直接提取年月日')
  assertEqual(formatDocumentFileDate('2026/6/4'), '2026-06-04', '斜杠日期也应补零')
})

runTest('date-only 发票日期显示不应受 UTC 负时区影响', () => {
  const originalTimezone = process.env.TZ
  process.env.TZ = 'America/Los_Angeles'
  try {
    assertEqual(formatPrintDate('2026-06-05', false, 'en-US'), '6/5/2026', 'date-only 字符串应按本地日期组件显示')
  } finally {
    process.env.TZ = originalTimezone
  }
})

runTest('文档文件名只有传入日期时才追加日期后缀', () => {
  const fallbackTexts = { unknownStore: 'UnknownStore', unknownOrder: 'UnknownOrder' }
  assertEqual(
    buildDocumentFileName('INVOICE', 'Bankstown', '2026-1049', 'xlsx', fallbackTexts, '2026-06-17'),
    'INVOICE_Bankstown_2026-1049_2026-06-17.xlsx',
    '发票文件名应追加 invoice 日期',
  )
  assertEqual(
    buildDocumentFileName('PICKING', 'Bankstown', '2026-1049', 'xlsx', fallbackTexts),
    'PICKING_Bankstown_2026-1049.xlsx',
    '未传日期时应保持原文件名格式',
  )
})

runTest('PDF 切片绘制应先铺白底再绘制原图切片', () => {
  const calls: unknown[] = []
  const context = {
    set fillStyle(value: string) {
      calls.push(['fillStyle', value])
    },
    fillRect: (...args: number[]) => calls.push(['fillRect', ...args]),
    drawImage: (...args: unknown[]) => calls.push(['drawImage', ...args]),
  } as unknown as CanvasRenderingContext2D
  const sourceCanvas = { width: 120, height: 300 } as HTMLCanvasElement

  paintPdfSlice(context, sourceCanvas, 120, { offsetY: 40, height: 80 })

  assertDeepEqual(
    calls,
    [
      ['fillStyle', '#ffffff'],
      ['fillRect', 0, 0, 120, 80],
      ['drawImage', sourceCanvas, 0, 40, 120, 80, 0, 0, 120, 80],
    ],
    '绘制顺序和参数应锁住白底 JPEG 切片逻辑',
  )
})

runTest('分页 PDF 应逐页渲染 A4 页面并写入 jsPDF', () => {
  const printUtilsSource = fs.readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/printUtils.ts'), 'utf8')
  assertEqual(printUtilsSource.includes('createPdfDocumentFromPages'), true, '应提供分页 PDF 生成函数')
  assertEqual(printUtilsSource.includes("querySelectorAll<HTMLElement>(pageSelector)"), true, '分页 PDF 应按页面选择器逐页读取 DOM')
  assertEqual(printUtilsSource.includes('pdf.addPage()'), true, '分页 PDF 多页时应追加 PDF 页面')
  assertEqual(printUtilsSource.includes('pdf.addImage(imageData, PDF_IMAGE_FORMAT, 0, 0, 210, 297)'), true, '每个分页应按 A4 尺寸写入 PDF')
})

runTest('分页 PDF 打印应使用临时 Blob URL 并清理资源', () => {
  const printUtilsSource = fs.readFileSync(path.resolve(process.cwd(), 'src/pages/Warehouse/StoreOrders/printUtils.ts'), 'utf8')
  assertEqual(printUtilsSource.includes("pdf.output('blob')"), true, '打印 PDF 应输出 Blob')
  assertEqual(printUtilsSource.includes('URL.createObjectURL(blob)'), true, '打印 PDF 应创建临时 URL')
  assertEqual(printUtilsSource.includes('frame.contentWindow?.print()'), true, '打印 PDF 应触发 iframe 打印')
  assertEqual(printUtilsSource.includes('URL.revokeObjectURL(url)'), true, '打印完成后应清理临时 URL')
  assertEqual(printUtilsSource.includes('frame.remove()'), true, '打印完成后应移除临时 iframe')
})

console.log('printUtils.test: ok')
