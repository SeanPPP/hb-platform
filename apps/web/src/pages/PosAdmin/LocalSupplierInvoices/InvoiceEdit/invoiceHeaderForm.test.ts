import dayjs from 'dayjs'
import {
  buildInvoiceHeaderFormValues,
  buildInvoiceHeaderSavePayload,
  includeCurrentInvoiceHeaderOption,
} from './invoiceHeaderForm'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message)
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

const formValues = buildInvoiceHeaderFormValues({
  invoiceGUID: 'invoice-1',
  storeCode: 'S01',
  storeName: 'Brisbane',
  supplierCode: 'SUP01',
  supplierName: 'Supplier One',
  orderDate: '2026-07-18T00:00:00Z',
  inboundDate: '2026-07-19T00:00:00Z',
  totalAmount: 12.3,
  remarks: '备注',
  createdAt: '2026-07-19T00:00:00Z',
})

assertEqual(formValues.storeCode, 'S01', '表单应使用分店编码作为值')
assertEqual(formValues.supplierCode, 'SUP01', '表单应使用供应商编码作为值')
assertEqual(formValues.totalAmount, '12.30', '总金额应保持两位小数显示')

const payload = buildInvoiceHeaderSavePayload({
  ...formValues,
  storeCode: ' S02 ',
  supplierCode: ' SUP02 ',
  orderDate: dayjs('2026-07-20'),
  inboundDate: dayjs('2026-07-21'),
  remarks: ' 新备注 ',
})

assertEqual(payload.storeCode, 'S02', '保存 payload 应提交分店编码并去除空格')
assertEqual(payload.supplierCode, 'SUP02', '保存 payload 应提交供应商编码并去除空格')
assertEqual(payload.orderDate, '2026-07-20', '保存 payload 应格式化订单日期')
assertEqual(payload.inboundDate, '2026-07-21', '保存 payload 应格式化入库日期')
assertEqual(payload.remarks, '新备注', '保存 payload 应去除备注首尾空格')

const fallbackOptions = includeCurrentInvoiceHeaderOption([], 'S01', 'Brisbane', true)
assertEqual(fallbackOptions[0]?.value, 'S01', '选项加载失败时仍应回显当前分店')
assertEqual(fallbackOptions[0]?.label, 'S01 - Brisbane', '当前分店兜底项应显示编码和名称')
assert(fallbackOptions[0]?.disabled, '越出管理范围的当前分店兜底项应禁止选择')

const existingOptions = [{ value: 'SUP01', label: 'SUP01 - Supplier One' }]
assert(
  includeCurrentInvoiceHeaderOption(existingOptions, 'SUP01', 'Supplier One') === existingOptions,
  '当前供应商已在有效选项内时不应重复插入',
)

console.log('invoiceHeaderForm.test: ok')
