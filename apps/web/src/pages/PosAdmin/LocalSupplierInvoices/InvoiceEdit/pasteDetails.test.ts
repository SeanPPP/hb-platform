import {
  analyzePasteMultilineCells,
  defaultPasteFieldOrder,
  getPasteTextMaxColumnCount,
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

  const barcodeMultiCodeFailure = await runTest('粘贴条码列遇到逗号多条码时应拆出主条码和副条码', () => {
    const [row] = parsePasteText(
      '88841\t191554890459,191554890480,191554890497,191554890473,191554888418\tWomen Travel Perfume Assorted 35mL\t48\t1.6546',
      defaultPasteFieldOrder,
    )

    assertEqual(row?.barcode, '191554890459', '逗号分隔的多条码第一条应作为主条码')
    assertDeepEqual(
      row?.additionalBarcodes,
      ['191554890480', '191554890497', '191554890473', '191554888418'],
      '其余条码应作为副条码保留',
    )
  })
  if (barcodeMultiCodeFailure) failures.push(barcodeMultiCodeFailure)

  const barcodeMultiSeparatorFailure = await runTest('粘贴条码列应支持中文逗号分号和顿号分隔副条码', () => {
    const [row] = parsePasteText(
      '88842\t191554882676，191554882690;191554882669、191554888425；191554882676\tMen Travel Perfume Assorted 35mL\t48\t1.6546',
      defaultPasteFieldOrder,
    )

    assertEqual(row?.barcode, '191554882676', '第一条仍应作为主条码')
    assertDeepEqual(
      row?.additionalBarcodes,
      ['191554882690', '191554882669', '191554888425'],
      '中文标点分隔的副条码应去重后保留顺序',
    )
  })
  if (barcodeMultiSeparatorFailure) failures.push(barcodeMultiSeparatorFailure)

  const headerRowFailure = await runTest('粘贴供应商表格时应自动跳过表头行', () => {
    const parsed = parsePasteText(
      [
        'Item No.\tBarcode\tDescription\tInvoice Qty\tPrice (ex GST)',
        '15085-1xLV5085\t840417950853\tWomen Perfumen New Crystal Absolute\t6\t$2.5000',
      ].join('\n'),
      defaultPasteFieldOrder,
    )

    assertEqual(parsed.length, 1, '表头行不应作为商品明细提交')
    assertEqual(parsed[0]?.itemNumber, '15085-1xLV5085', '第一条数据行货号应保留')
    assertEqual(parsed[0]?.quantity, 6, 'Invoice Qty 应映射到数量')
    assertEqual(parsed[0]?.purchasePrice, 2.5, 'Price (ex GST) 应映射到本次进货价')
  })
  if (headerRowFailure) failures.push(headerRowFailure)

  const itemNumberQuoteFailure = await runTest('粘贴货号列应去掉 Excel 文本格式前导单引号', () => {
    const [excelTextRow] = parsePasteText("'027000040\t8719987314001\t商品1\t1\t5\t5\t5", defaultPasteFieldOrder)
    const [multiQuoteRow] = parsePasteText("''SKU-1\t8719987314002\t商品2\t1\t5\t5\t5", defaultPasteFieldOrder)
    const [middleQuoteRow] = parsePasteText("SKU-'KEEP\t8719987314003\t商品3\t1\t5\t5\t5", defaultPasteFieldOrder)

    assertEqual(excelTextRow?.itemNumber, '027000040', '货号前导单引号应被去掉')
    assertEqual(multiQuoteRow?.itemNumber, 'SKU-1', '货号多个前导单引号应全部去掉')
    assertEqual(middleQuoteRow?.itemNumber, "SKU-'KEEP", '货号中间单引号应保留')
  })
  if (itemNumberQuoteFailure) failures.push(itemNumberQuoteFailure)

  const multilineMergeFailure = await runTest('默认合并单元格内换行并只生成一条明细', () => {
    const parsed = parsePasteText('SKU-1\t111\t"Gloves Powder\nx Thickn\ness 0.10mm)"\t1\t5\t5\t5', defaultPasteFieldOrder)
    const analysis = analyzePasteMultilineCells('SKU-1\t111\t"Gloves Powder\nx Thickn\ness 0.10mm)"\t1\t5\t5\t5', defaultPasteFieldOrder)

    assertEqual(parsed.length, 1, '单元格内换行不应被拆成多条明细')
    assertEqual(parsed[0]?.productName, 'Gloves Powder x Thickn ess 0.10mm)', '商品名单元格内换行应合并为空格')
    assertEqual(analysis.hasMultilineCells, true, '应检测到单元格内换行')
    assertEqual(analysis.unsafeRecordCount, 1, '只有商品名多行时不满足安全拆分条件')
  })
  if (multilineMergeFailure) failures.push(multilineMergeFailure)

  const multilineSmartSplitFailure = await runTest('智能拆分应在所有业务列多行数一致时拆成多条明细', () => {
    const parsed = parsePasteText(
      '"SKU-1\nSKU-2\nSKU-3"\t"111\n222\n333"\t"商品1\n商品2\n商品3"\t"1\n2\n3"\t"5\n6\n7"\t"5.5\n6.5\n7.5"\t"8\n9\n10"',
      defaultPasteFieldOrder,
      { multilineCellMode: 'smartSplit' },
    )

    assertEqual(parsed.length, 3, '所有业务列都有 3 行时应拆成 3 条')
    assertDeepEqual(
      parsed.map((item) => ({
        itemNumber: item.itemNumber,
        barcode: item.barcode,
        productName: item.productName,
        quantity: item.quantity,
        purchasePrice: item.purchasePrice,
        newAutoRetailPrice: item.newAutoRetailPrice,
        retailPrice: item.retailPrice,
      })),
      [
        { itemNumber: 'SKU-1', barcode: '111', productName: '商品1', quantity: 1, purchasePrice: 5, newAutoRetailPrice: 5.5, retailPrice: 8 },
        { itemNumber: 'SKU-2', barcode: '222', productName: '商品2', quantity: 2, purchasePrice: 6, newAutoRetailPrice: 6.5, retailPrice: 9 },
        { itemNumber: 'SKU-3', barcode: '333', productName: '商品3', quantity: 3, purchasePrice: 7, newAutoRetailPrice: 7.5, retailPrice: 10 },
      ],
      '智能拆分应按同一行号组装字段',
    )
  })
  if (multilineSmartSplitFailure) failures.push(multilineSmartSplitFailure)

  const multilinePartialFailure = await runTest('智能拆分遇到部分列多行时应自动合并', () => {
    const parsed = parsePasteText('SKU-1\t111\t"商品1\n商品1补充"\t1\t5\t5\t5', defaultPasteFieldOrder, {
      multilineCellMode: 'smartSplit',
    })

    assertEqual(parsed.length, 1, '只有部分列多行时不应错位拆分')
    assertEqual(parsed[0]?.productName, '商品1 商品1补充', '自动合并时应把内部换行压成空格')
  })
  if (multilinePartialFailure) failures.push(multilinePartialFailure)

  const multilineMismatchFailure = await runTest('智能拆分遇到多行数量不一致时应自动合并', () => {
    const parsed = parsePasteText(
      '"SKU-1\nSKU-2"\t"111\n222\n333"\t"商品1\n商品2"\t"1\n2"\t"5\n6"\t"5\n6"\t"8\n9"',
      defaultPasteFieldOrder,
      { multilineCellMode: 'smartSplit' },
    )

    assertEqual(parsed.length, 1, '多行数量不一致时不应拆分')
    assertEqual(parsed[0]?.itemNumber, 'SKU-1 SKU-2', '自动合并时应保留货号内容')
    assertEqual(parsed[0]?.productName, '商品1 商品2', '自动合并时应保留商品名内容')
  })
  if (multilineMismatchFailure) failures.push(multilineMismatchFailure)

  const multilineSkipFailure = await runTest('智能拆分判断应忽略跳过列', () => {
    const parsed = parsePasteText(
      '"SKU-1\nSKU-2"\t"备注1\n备注2\n备注3"\t"111\n222"\t"商品1\n商品2"\t"1\n2"',
      ['itemNumber', 'skip', 'barcode', 'productName', 'quantity'],
      { multilineCellMode: 'smartSplit' },
    )

    assertEqual(parsed.length, 2, '跳过列多行数不同不应阻止业务列拆分')
    assertEqual(parsed[1]?.itemNumber, 'SKU-2', '第二行货号应来自第二个单元格行')
    assertEqual(parsed[1]?.barcode, '222', '第二行条码应来自第二个单元格行')
    assertEqual(parsed[1]?.productName, '商品2', '第二行商品名应来自第二个单元格行')
  })
  if (multilineSkipFailure) failures.push(multilineSkipFailure)

  const multilineOrdinaryRowsFailure = await runTest('普通多条 Excel 行不受多行单元格解析影响', () => {
    const parsed = parsePasteText(['SKU-1\t111\t商品1\t1\t5\t5\t5', 'SKU-2\t222\t商品2\t2\t6\t6\t6'].join('\n'), defaultPasteFieldOrder, {
      multilineCellMode: 'smartSplit',
    })

    assertEqual(parsed.length, 2, '普通两行仍应解析为两条明细')
    assertEqual(getPasteTextMaxColumnCount('SKU-1\t111\t"商品1\n补充"\t1\t5\t5\t5'), 7, '列数计算应忽略单元格内换行')
  })
  if (multilineOrdinaryRowsFailure) failures.push(multilineOrdinaryRowsFailure)

  if (failures.length) {
    throw new Error(failures.join('\n'))
  }

  console.log('pasteDetails.test: ok')
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error))
  process.exit(1)
})
