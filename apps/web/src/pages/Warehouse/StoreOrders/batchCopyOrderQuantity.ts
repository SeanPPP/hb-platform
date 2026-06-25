import type { StoreOrderDetailLine } from '../../../types/storeOrder'

export interface BatchCopyOrderQuantityPayload {
  items: Array<{
    detailGUID: string
    productCode: string
    quantity: number
  }>
  overwriteCount: number
  zeroOrderQuantityCount: number
  shouldConfirm: boolean
}

export function buildBatchCopyOrderQuantityPayload(lines: StoreOrderDetailLine[]): BatchCopyOrderQuantityPayload {
  const items = lines.map((line) => ({
    detailGUID: line.detailGUID,
    productCode: line.productCode,
    quantity: Number(line.quantity ?? 0),
  }))
  // 复制发货数属于批量覆盖动作，每次都先确认；风险数量只负责补充提示。
  const overwriteCount = lines.filter((line) => Number(line.allocQuantity ?? 0) > 0).length
  const zeroOrderQuantityCount = lines.filter((line) => Number(line.quantity ?? 0) === 0).length

  return {
    items,
    overwriteCount,
    zeroOrderQuantityCount,
    shouldConfirm: lines.length > 0,
  }
}

export function shouldSubmitBatchCopyOrderQuantity(
  payload: BatchCopyOrderQuantityPayload,
  confirmed = false,
) {
  // 批量复制每次都需要用户确认；空 payload 不允许继续提交。
  return payload.shouldConfirm ? confirmed : false
}
