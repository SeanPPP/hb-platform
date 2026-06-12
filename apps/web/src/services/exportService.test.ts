import ExcelJS from 'exceljs'
import { populateContainerDetailsWorksheet, type ContainerDetailExportColumn } from './exportService'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const workbook = new ExcelJS.Workbook()
const worksheet = workbook.addWorksheet('货柜明细')

const columns: ContainerDetailExportColumn[] = [
  { header: '序号', key: 'index', width: 8, valueType: 'integer' },
  { header: '件数', key: 'containerPieces', width: 12, valueType: 'integer' },
  { header: '单件体积', key: 'unitVolume', width: 12, valueType: 'volume' },
  { header: '国内价格', key: 'domesticPrice', width: 12, valueType: 'money' },
]

const result = populateContainerDetailsWorksheet(
  worksheet,
  [
    {
      index: 1,
      containerPieces: 12,
      unitVolume: 0.123456,
      domesticPrice: 4.5,
    },
  ],
  {
    columns,
    summary: {
      title: '货柜主表信息',
      rows: [
        [
          { label: '货柜编号', value: 'CSNU6137731' },
          { label: '运费', value: 12000, valueType: 'money' },
        ],
        [
          { label: '总体积', value: 69.053, valueType: 'volume' },
        ],
      ],
    },
  },
)

assertEqual(result.headerRowNumber, 5, '主表信息和空行之后应写入明细表头')
assertEqual(worksheet.getCell('A1').value, '货柜主表信息', 'summary 标题应写在首行')
assertEqual(worksheet.getCell('A2').value, '货柜编号', 'summary 应写在明细表头上方')
assertEqual(worksheet.getCell('D2').numFmt, '$#,##0.00', 'summary 金额应保留 2 位小数')
assertEqual(worksheet.getCell('B3').numFmt, '#,##0.0000', 'summary 体积应保留 4 位小数')
assertEqual(worksheet.getRow(result.headerRowNumber).getCell(1).value, '序号', '明细表头应在返回的表头行')
assertEqual(worksheet.getRow(result.dataStartRowNumber).getCell(1).value, 1, '明细数据应写在表头下一行')
assertEqual(worksheet.getColumn('index').numFmt, undefined, '列级格式不应污染整列元数据')
assertEqual(worksheet.getRow(result.dataStartRowNumber).getCell(1).numFmt, '#,##0', '数量应使用整数格式')
assertEqual(worksheet.getRow(result.dataStartRowNumber).getCell(3).numFmt, '#,##0.0000', '体积应使用 4 位小数格式')
assertEqual(worksheet.getRow(result.dataStartRowNumber).getCell(4).numFmt, '¥#,##0.00', '国内价格应使用人民币 2 位小数格式')
assertEqual((worksheet.views[0] as { ySplit?: number } | undefined)?.ySplit, result.headerRowNumber, '冻结行应落在明细表头行')
