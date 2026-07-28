const TIMEZONE_SUFFIX_PATTERN = /(Z|[+-]\d{2}:?\d{2})$/i

function normalizeAuditTimestamp(value: string) {
  const trimmed = value.trim()
  if (TIMEZONE_SUFFIX_PATTERN.test(trimmed)) {
    return trimmed
  }

  // 兼容旧接口：无时区后缀的审计时间实际为 UTC，新接口自带的时区信息则保持原语义。
  return `${trimmed.replace(' ', 'T')}Z`
}

export function formatLocalSupplierInvoiceAuditTime(value?: string | null) {
  if (!value?.trim()) {
    return '--'
  }

  const date = new Date(normalizeAuditTimestamp(value))
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('zh-CN', { hour12: false })
}
