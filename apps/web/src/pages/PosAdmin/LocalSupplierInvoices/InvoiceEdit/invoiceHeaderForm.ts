import dayjs, { type Dayjs } from 'dayjs'
import type {
  LocalSupplierInvoiceDetailDto,
  UpdateInvoiceRequest,
} from '../../../../types/localSupplierInvoice'

export interface InvoiceHeaderFormValues {
  invoiceNo?: string
  storeCode?: string
  supplierCode?: string
  orderDate?: Dayjs
  inboundDate?: Dayjs
  totalAmount: string
  remarks?: string
}

export interface InvoiceHeaderSelectOption {
  label: string
  value: string
  disabled?: boolean
}

function formatAmount(value?: number) {
  if (value === undefined || value === null) return '--'
  return value.toFixed(2)
}

export function buildInvoiceHeaderFormValues(data: LocalSupplierInvoiceDetailDto): InvoiceHeaderFormValues {
  return {
    invoiceNo: data.invoiceNo,
    storeCode: data.storeCode,
    supplierCode: data.supplierCode,
    orderDate: data.orderDate ? dayjs(data.orderDate) : undefined,
    inboundDate: data.inboundDate ? dayjs(data.inboundDate) : undefined,
    totalAmount: formatAmount(data.totalAmount),
    remarks: data.remarks,
  }
}

export function buildInvoiceHeaderSavePayload(values: InvoiceHeaderFormValues): UpdateInvoiceRequest {
  return {
    storeCode: values.storeCode?.trim() || undefined,
    supplierCode: values.supplierCode?.trim() || undefined,
    orderDate: values.orderDate?.format('YYYY-MM-DD'),
    inboundDate: values.inboundDate?.format('YYYY-MM-DD'),
    remarks: values.remarks?.trim() || undefined,
  }
}

export function includeCurrentInvoiceHeaderOption(
  options: InvoiceHeaderSelectOption[],
  currentCode: string | undefined,
  currentName: string | undefined,
  disabled = false,
): InvoiceHeaderSelectOption[] {
  const code = currentCode?.trim()
  if (!code || options.some((option) => option.value === code)) {
    return options
  }

  const name = currentName?.trim()
  // 当前订单可能引用已停用或本轮选项加载失败的数据，保留只用于回显的兜底项。
  return [
    {
      value: code,
      label: name ? `${code} - ${name}` : code,
      disabled,
    },
    ...options,
  ]
}
