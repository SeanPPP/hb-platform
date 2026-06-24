import { parseProductImportPasteText } from './utils'

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

assertDeepEqual(
  parseProductImportPasteText('玉米\r\n"表情球\n慢回弹\n10.5cm"\r\n\r\n桃子\r\n小熊\r\n\r\n光变包子\r\n'),
  [
    ['玉米'],
    ['表情球\n慢回弹\n10.5cm'],
    [''],
    ['桃子'],
    ['小熊'],
    [''],
    ['光变包子'],
  ],
  'Excel 单列粘贴应保留空单元格，并把单元格内换行保留在同一行',
)

assertDeepEqual(
  parseProductImportPasteText('HB001\t\t苹果\r\nHB002\t9527\t"大香蕉\nPVC盒"\r\n'),
  [
    ['HB001', '', '苹果'],
    ['HB002', '9527', '大香蕉\nPVC盒'],
  ],
  'Excel 多列粘贴应保留中间空列，避免字段错位',
)
