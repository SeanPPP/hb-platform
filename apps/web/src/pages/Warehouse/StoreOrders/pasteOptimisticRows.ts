import type { StoreOrderDetailLine, StoreOrderPasteReplaceJobResult, StoreOrderPasteTargetField } from '../../../types/storeOrder'
import { getPasteSubmitQuantity, type StoreOrderPastePreviewItem, type StoreOrderPasteQuantityMode } from './pastePreview'

export interface StoreOrderPasteOptimisticPending {
  jobId: string
  orderGUID: string
}

interface BuildPasteOptimisticRowsOptions {
  currentItems: StoreOrderDetailLine[]
  previewItems: StoreOrderPastePreviewItem[]
  targetField: StoreOrderPasteTargetField
  quantityMode?: StoreOrderPasteQuantityMode
}

function toNumber(value: number | undefined | null) {
  return Number(value ?? 0)
}

function buildOptimisticDetailGUID(item: StoreOrderPastePreviewItem) {
  return `optimistic-paste-${item.rowIndex}-${item.product?.productCode ?? item.itemNumber}`
}

function recalculateLine(line: StoreOrderDetailLine): StoreOrderDetailLine {
  const quantity = toNumber(line.quantity)
  const allocQuantity = toNumber(line.allocQuantity)
  const price = toNumber(line.price)
  const importPrice = toNumber(line.importPrice)
  const volume = line.volume

  return {
    ...line,
    quantity,
    allocQuantity,
    price,
    importPrice,
    amount: price * allocQuantity,
    importAmount: importPrice * allocQuantity,
    orderVolume: volume === undefined || volume === null ? line.orderVolume : volume * quantity,
    allocVolume: volume === undefined || volume === null ? line.allocVolume : volume * allocQuantity,
  }
}

function createLineFromPreview(item: StoreOrderPastePreviewItem, targetField: StoreOrderPasteTargetField): StoreOrderDetailLine {
  const product = item.product
  const existingQuantity = toNumber(item.existingQuantity)
  const existingAllocQuantity = toNumber(item.existingAllocQuantity)

  return recalculateLine({
    detailGUID: buildOptimisticDetailGUID(item),
    productCode: product?.productCode ?? item.itemNumber,
    itemNumber: product?.itemNumber ?? item.itemNumber,
    barcode: product?.barcode,
    productName: product?.productName,
    productImage: product?.productImage,
    quantity: targetField === 'quantity' ? existingQuantity : 0,
    allocQuantity: targetField === 'allocQuantity' ? existingAllocQuantity : 0,
    price: toNumber(product?.oemPrice),
    amount: 0,
    importPrice: toNumber(item.price ?? product?.importPrice),
    importAmount: 0,
    minOrderQuantity: toNumber(product?.minOrderQuantity) || 1,
    isActive: true,
  })
}

function applyPasteQuantity(
  line: StoreOrderDetailLine,
  item: StoreOrderPastePreviewItem,
  targetField: StoreOrderPasteTargetField,
  quantityMode: StoreOrderPasteQuantityMode,
) {
  const writeQuantity = getPasteSubmitQuantity(item, quantityMode)
  const currentQuantity = targetField === 'quantity' ? toNumber(line.quantity) : toNumber(line.allocQuantity)
  const nextQuantity = item.action === 'append' ? currentQuantity + writeQuantity : writeQuantity
  const nextLine: StoreOrderDetailLine = {
    ...line,
    importPrice: item.price ?? line.importPrice,
    [targetField]: nextQuantity,
  }

  return recalculateLine(nextLine)
}

export function buildPasteOptimisticRows({
  currentItems,
  previewItems,
  targetField,
  quantityMode = 'direct',
}: BuildPasteOptimisticRowsOptions): StoreOrderDetailLine[] {
  const lineMap = new Map<string, StoreOrderDetailLine>()
  const rowOrder: string[] = []

  currentItems.forEach((line) => {
    if (!line.productCode) {
      return
    }
    const productCodeKey = line.productCode.toLocaleLowerCase()
    lineMap.set(productCodeKey, line)
    rowOrder.push(productCodeKey)
  })

  previewItems.forEach((item) => {
    const productCode = item.product?.productCode
    if (!item.valid || item.action === 'skip' || !productCode) {
      return
    }

    const productCodeKey = productCode.toLocaleLowerCase()
    const baseLine = lineMap.get(productCodeKey) ?? createLineFromPreview(item, targetField)
    const nextLine = applyPasteQuantity(baseLine, item, targetField, quantityMode)
    lineMap.set(productCodeKey, nextLine)

    // 当前页不存在但本次 Excel 有效的商品也要临时显示，给用户即时反馈。
    if (!rowOrder.includes(productCodeKey)) {
      rowOrder.push(productCodeKey)
    }
  })

  return rowOrder
    .map((productCodeKey) => lineMap.get(productCodeKey))
    .filter((line): line is StoreOrderDetailLine => Boolean(line))
}

export function applyPasteOptimisticRowsToDetail<TDetail extends { items: StoreOrderDetailLine[] }>(
  detail: TDetail,
  optimisticRows: StoreOrderDetailLine[],
): TDetail {
  // 临时预览只替换表格行，整单合计和远程分页总数继续保持服务器真实值。
  return {
    ...detail,
    items: optimisticRows,
  }
}

export function resolvePasteOptimisticPendingAfterJob(
  pending: StoreOrderPasteOptimisticPending | null,
  result: Pick<StoreOrderPasteReplaceJobResult, 'jobId' | 'status'>,
) {
  if (!pending || pending.jobId !== result.jobId) {
    return pending
  }

  return result.status === 'Succeeded' || result.status === 'Failed' ? null : pending
}
