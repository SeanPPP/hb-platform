import {
  defaultPasteFieldOrder,
  normalizePastedRetailPrice,
  parsePasteText,
} from './pasteDetails'

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

async function runTest(name: string, execute: () => void | Promise<void>): Promise<string | null> {
  try {
    await execute()
    console.log(`ok - ${name}`)
    return null
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error)
    console.error(`not ok - ${name}`)
    console.error(reason)
    return `${name}: ${reason}`
  }
}

async function main() {
  const failures: string[] = []

  const retailNormalizeFailure = await runTest('零售价规范化应按 3 元起规则归到 .50 或 .99', () => {
    assertEqual(normalizePastedRetailPrice(5), 4.99, '整数 5 应改为 4.99')
    assertEqual(normalizePastedRetailPrice(4.1), 4.5, '4.1 应改为 4.50')
    assertEqual(normalizePastedRetailPrice(4.6), 4.99, '4.6 应改为 4.99')
    assertEqual(normalizePastedRetailPrice(4.5), 4.5, '4.5 应保持 4.50')
    assertEqual(normalizePastedRetailPrice(3), 2.99, '整数 3 应改为 2.99')
    assertEqual(normalizePastedRetailPrice(1), 1, '整数 1 不应改变')
    assertEqual(normalizePastedRetailPrice(2), 2, '整数 2 不应改变')
    assertEqual(normalizePastedRetailPrice(2.9), 2.9, '小于 3 的价格不应改变')
  })
  if (retailNormalizeFailure) failures.push(retailNormalizeFailure)

  const parseRetailFailure = await runTest('粘贴解析默认只规范化零售价列', () => {
    const parsed = parsePasteText(
      [
        'SKU-1\t111\t商品1\t1\t5\t5\t5',
        'SKU-2\t222\t商品2\t1\t4.1\t5\t4.1',
        'SKU-3\t333\t商品3\t1\t4.6\t5\t4.6',
        'SKU-4\t444\t商品4\t1\t1\t5\t1',
        'SKU-5\t555\t商品5\t1\t2\t5\t2',
      ].join('\n'),
      defaultPasteFieldOrder,
      { normalizeRetailPrice: true },
    )

    assertDeepEqual(
      parsed.map((item) => ({
        purchasePrice: item.purchasePrice,
        newAutoRetailPrice: item.newAutoRetailPrice,
        retailPrice: item.retailPrice,
      })),
      [
        { purchasePrice: 5, newAutoRetailPrice: 5, retailPrice: 4.99 },
        { purchasePrice: 4.1, newAutoRetailPrice: 5, retailPrice: 4.5 },
        { purchasePrice: 4.6, newAutoRetailPrice: 5, retailPrice: 4.99 },
        { purchasePrice: 1, newAutoRetailPrice: 5, retailPrice: 1 },
        { purchasePrice: 2, newAutoRetailPrice: 5, retailPrice: 2 },
      ],
      '只应规范化 retailPrice，不应改变 purchasePrice 和 newAutoRetailPrice',
    )
  })
  if (parseRetailFailure) failures.push(parseRetailFailure)

  const disabledNormalizeFailure = await runTest('关闭零售价规范化时应保留原始零售价数字', () => {
    const parsed = parsePasteText('SKU-1\t111\t商品1\t1\t5\t5\t5', defaultPasteFieldOrder, {
      normalizeRetailPrice: false,
    })

    assertEqual(parsed[0]?.retailPrice, 5, '关闭规范化后 retailPrice 应保持 5')
    assertEqual(parsed[0]?.newAutoRetailPrice, 5, '关闭规范化后 newAutoRetailPrice 应保持 5')
  })
  if (disabledNormalizeFailure) failures.push(disabledNormalizeFailure)

  const currencyFormatFailure = await runTest('货币格式零售价应先解析再规范化', () => {
    const parsed = parsePasteText('SKU-1\t111\t商品1\t1\tA$5.00\tAUD 5.00\t$5.00', defaultPasteFieldOrder, {
      normalizeRetailPrice: true,
    })

    assertEqual(parsed[0]?.purchasePrice, 5, '进货价货币格式应解析为 5')
    assertEqual(parsed[0]?.newAutoRetailPrice, 5, '新自动零售价货币格式不应规范化')
    assertEqual(parsed[0]?.retailPrice, 4.99, '零售价货币格式应解析后规范化为 4.99')
  })
  if (currencyFormatFailure) failures.push(currencyFormatFailure)

  const barcodeLabelFailure = await runTest('粘贴条码列应去掉随单元格带入的条码标签', () => {
    const [suffixLabelRow] = parsePasteText('SKU-1\t9357405070864 条码\t商品1\t1\t5\t5\t5', defaultPasteFieldOrder)
    const [prefixLabelRow] = parsePasteText('SKU-2\t条码：9357405070864\t商品2\t1\t5\t5\t5', defaultPasteFieldOrder)
    const [excelTextRow] = parsePasteText("SKU-3\t'9357405070864\t商品3\t1\t5\t5\t5", defaultPasteFieldOrder)

    assertEqual(suffixLabelRow?.barcode, '9357405070864', '条码尾部标签应被去掉')
    assertEqual(prefixLabelRow?.barcode, '9357405070864', '条码前置标签应被去掉')
    assertEqual(excelTextRow?.barcode, '9357405070864', 'Excel 文本条码前导单引号应被去掉')
  })
  if (barcodeLabelFailure) failures.push(barcodeLabelFailure)

  if (failures.length) {
    throw new Error(failures.join('\n'))
  }

  console.log('pasteDetails.test: ok')
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error))
  process.exit(1)
})
