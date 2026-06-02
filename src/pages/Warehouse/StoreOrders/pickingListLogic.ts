import type { StoreOrderDetail, StoreOrderDetailLine } from '../../../types/storeOrder'

export interface PickingListExcelTexts {
  sheetName: string
  orderNoLabel: string
  storeLabel: string
  orderDateLabel: string
  printTimeLabel: string
  remarksLabel: string
  totalSKULabel: string
  totalOrderQtyLabel: string
  totalShipQtyLabel: string
  totalOrderVolumeLabel: string
  detailHeaders: {
    index: string
    itemNumber: string
    location: string
    productName: string
    importPrice: string
    rrp: string
    innerPackCount: string
    orderQuantity: string
    allocQuantity: string
  }
}

export interface PickingListExcelMeta {
  orderNoText: string
  storeText: string
  orderDateText: string
  printTimeText: string
  totalOrderVolumeText: string
}

export interface PickingListExcelData {
  sheetName: string
  overviewRows: Array<[string, string]>
  detailHeader: string[]
  detailRows: Array<Array<string | number>>
  remarksRow?: [string, string]
  totalRows: Array<[string, string | number]>
}

function formatExcelCurrency(value?: number) {
  return Number(Number(value ?? 0).toFixed(2))
}

// 统一管理配货单的派生展示逻辑，避免组件里散落业务格式化判断。
export function formatInnerPackCount(orderQuantity: number, minOrderQuantity?: number) {
  if (!Number.isFinite(orderQuantity)) {
    return ''
  }

  if (typeof minOrderQuantity !== 'number' || !Number.isFinite(minOrderQuantity) || minOrderQuantity <= 0) {
    return ''
  }

  const innerPackCount = orderQuantity / minOrderQuantity
  if (!Number.isFinite(innerPackCount)) {
    return ''
  }

  return Number.isInteger(innerPackCount) ? String(innerPackCount) : innerPackCount.toFixed(1)
}

export function buildPickingListExcelData(
  order: StoreOrderDetail,
  items: StoreOrderDetailLine[],
  texts: PickingListExcelTexts,
  meta: PickingListExcelMeta = {
    orderNoText: order.orderNo || order.orderGUID || '',
    storeText: order.storeCode || '',
    orderDateText: order.orderDate || '',
    printTimeText: '',
    totalOrderVolumeText:
      typeof order.totalOrderVolume === 'number'
        ? order.totalOrderVolume.toFixed(4)
        : typeof order.totalVolume === 'number'
          ? order.totalVolume.toFixed(4)
          : '--',
  },
): PickingListExcelData {
  return {
    sheetName: texts.sheetName,
    overviewRows: [
      [texts.orderNoLabel, meta.orderNoText],
      [texts.storeLabel, meta.storeText],
      [texts.orderDateLabel, meta.orderDateText],
      [texts.printTimeLabel, meta.printTimeText],
    ],
    detailHeader: [
      texts.detailHeaders.index,
      texts.detailHeaders.itemNumber,
      texts.detailHeaders.location,
      texts.detailHeaders.productName,
      texts.detailHeaders.importPrice,
      texts.detailHeaders.rrp,
      texts.detailHeaders.innerPackCount,
      texts.detailHeaders.orderQuantity,
      texts.detailHeaders.allocQuantity,
    ],
    detailRows: items.map((item, index) => [
      index + 1,
      item.itemNumber || '',
      item.locationCode || '',
      item.productName || '',
      formatExcelCurrency(item.importPrice),
      item.rrp === undefined || item.rrp === null ? '' : formatExcelCurrency(item.rrp),
      formatInnerPackCount(item.quantity, item.minOrderQuantity),
      Number(item.quantity || 0),
      Number(item.allocQuantity ?? 0),
    ]),
    remarksRow: order.remarks ? [texts.remarksLabel, order.remarks] : undefined,
    totalRows: [
      [texts.totalSKULabel, order.totalSKU ?? items.length],
      [texts.totalOrderQtyLabel, order.totalQuantity],
      [texts.totalShipQtyLabel, order.totalAllocQuantity ?? 0],
      [texts.totalOrderVolumeLabel, meta.totalOrderVolumeText],
    ],
  }
}
