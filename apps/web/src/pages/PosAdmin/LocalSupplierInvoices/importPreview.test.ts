import { readFileSync } from 'node:fs'
import path from 'node:path'
import {
  buildImportPreviewLines,
  hasDuplicateImportMappings,
  hasRequiredImportMappings,
  isLegacyExcelFileName,
} from './importPreview'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
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

const modalFile = path.resolve(process.cwd(), 'src/pages/PosAdmin/LocalSupplierInvoices/ImportInvoiceModal.tsx')
const modalSource = readFileSync(modalFile, 'utf8')

async function main() {
  const failures: string[] = []

  const uploadAcceptFailure = await runTest('导入弹窗应限制文件选择为 xlsx、xlsm 和 pdf', () => {
    assert(
      modalSource.includes('accept=".xlsx,.xlsm,.pdf"'),
      '文件选择控件应显式限制为 .xlsx,.xlsm,.pdf',
    )
  })
  if (uploadAcceptFailure) failures.push(uploadAcceptFailure)

  const legacyExcelFailure = await runTest('.xls 应被识别为不支持的旧版 Excel', () => {
    assertEqual(isLegacyExcelFileName('invoice.xls'), true, '.xls 文件应被拒绝')
    assertEqual(isLegacyExcelFileName('invoice.xlsx'), false, '.xlsx 文件不应被拒绝')
    assertEqual(isLegacyExcelFileName('invoice.xlsm'), false, '.xlsm 文件不应被拒绝')
    assertEqual(isLegacyExcelFileName('invoice.pdf'), false, '.pdf 文件不应被拒绝')
  })
  if (legacyExcelFailure) failures.push(legacyExcelFailure)

  const mappingRequiredFailure = await runTest('五项字段映射必须全部提供且不能重复', () => {
    assertEqual(
      hasRequiredImportMappings({
        itemNumberColumnKey: 'col_1',
        barcodeColumnKey: 'col_2',
        productNameColumnKey: 'col_3',
        quantityColumnKey: 'col_4',
      }),
      false,
      '缺少价格列时不应允许继续',
    )
    assertEqual(
      hasRequiredImportMappings({
        itemNumberColumnKey: 'col_1',
        barcodeColumnKey: 'col_2',
        productNameColumnKey: 'col_3',
        quantityColumnKey: 'col_4',
        priceColumnKey: 'col_5',
      }),
      true,
      '五项字段映射齐全时应允许继续',
    )
    assertEqual(
      hasDuplicateImportMappings({
        itemNumberColumnKey: 'col_1',
        barcodeColumnKey: 'col_2',
        productNameColumnKey: 'col_3',
        quantityColumnKey: 'col_4',
        priceColumnKey: 'col_4',
      }),
      true,
      '同一列不能同时映射到数量和价格',
    )
  })
  if (mappingRequiredFailure) failures.push(mappingRequiredFailure)

  const previewRemapFailure = await runTest('修改列映射后应根据 rawValues 重新生成预览明细', () => {
    const lines = [
      {
        rowNumber: 7,
        rawValues: {
          col_1: 'HB001',
          col_2: '935001',
          col_3: '苹果',
          col_4: '2',
          col_5: '3.50',
        },
      },
    ]

    const defaultPreview = buildImportPreviewLines(lines, {
      itemNumberColumnKey: 'col_1',
      barcodeColumnKey: 'col_2',
      productNameColumnKey: 'col_3',
      quantityColumnKey: 'col_4',
      priceColumnKey: 'col_5',
    })
    const remappedPreview = buildImportPreviewLines(lines, {
      itemNumberColumnKey: 'col_3',
      barcodeColumnKey: 'col_2',
      productNameColumnKey: 'col_1',
      quantityColumnKey: 'col_5',
      priceColumnKey: 'col_4',
    })

    assertDeepEqual(
      defaultPreview[0],
      {
        key: '7-HB001|935001|苹果|2|3.50',
        rowNumber: 7,
        itemNumber: 'HB001',
        barcode: '935001',
        productName: '苹果',
        quantity: 2,
        price: 3.5,
        amount: 7,
        rawValues: {
          col_1: 'HB001',
          col_2: '935001',
          col_3: '苹果',
          col_4: '2',
          col_5: '3.50',
        },
      },
      '默认映射应按原始列顺序生成预览',
    )
    assertDeepEqual(
      remappedPreview[0],
      {
        key: '7-HB001|935001|苹果|2|3.50',
        rowNumber: 7,
        itemNumber: '苹果',
        barcode: '935001',
        productName: 'HB001',
        quantity: 3.5,
        price: 2,
        amount: 7,
        rawValues: {
          col_1: 'HB001',
          col_2: '935001',
          col_3: '苹果',
          col_4: '2',
          col_5: '3.50',
        },
      },
      '修改映射后预览行应基于 rawValues 重新计算字段和值',
    )
  })
  if (previewRemapFailure) failures.push(previewRemapFailure)

  if (failures.length > 0) {
    throw new Error(`共有 ${failures.length} 个测试失败\\n- ${failures.join('\\n- ')}`)
  }

  console.log('importPreview.test: ok')
}

await main()
