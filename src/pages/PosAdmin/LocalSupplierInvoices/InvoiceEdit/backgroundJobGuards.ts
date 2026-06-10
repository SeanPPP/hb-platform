export function canApplyInvoiceJobResult(currentInvoiceGuid: string | undefined, submittedInvoiceGuid: string) {
  return Boolean(currentInvoiceGuid) && currentInvoiceGuid === submittedInvoiceGuid
}

export function canApplyCheckProductsJobResult({
  currentInvoiceGuid,
  submittedInvoiceGuid,
  status,
  hasResult,
}: {
  currentInvoiceGuid: string | undefined
  submittedInvoiceGuid: string
  status: string
  hasResult: boolean
}) {
  // 商品检测失败时即使后端带回部分 result，也不写入当前表格，避免失败任务污染行状态。
  return canApplyInvoiceJobResult(currentInvoiceGuid, submittedInvoiceGuid) && status === 'Succeeded' && hasResult
}

