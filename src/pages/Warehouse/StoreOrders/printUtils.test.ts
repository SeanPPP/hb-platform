import {
  PDF_IMAGE_FORMAT,
  PDF_IMAGE_MIME_TYPE,
  PDF_IMAGE_QUALITY,
  buildPdfSlicePlan,
  getPdfSliceImageData,
  paintPdfSlice,
} from './printUtils'

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

console.log('printUtils.test: ok')
