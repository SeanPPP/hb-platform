import type { StoreOrderProductActivityHistoryItem } from '../../types/storeOrder'

export interface ProductActivityTableRow extends StoreOrderProductActivityHistoryItem {
  tableKey: string
  children?: ProductActivityTableRow[]
  isSalesContinuation?: boolean
}

function getSalesPeriodKey(item: StoreOrderProductActivityHistoryItem): string {
  if (item.periodStartDate && item.periodEndDate) {
    return `${item.periodStartDate}::${item.periodEndDate}`
  }

  // 兼容前后端滚动发布：旧接口未返回区间日期时，仍把当前页每日销量收在一个分组内。
  return 'current-page'
}

// Interval total 保持顶层显示；逐日 Sales 作为其子行，交由 Table 默认折叠。
export function buildProductActivityTableRows(
  items: StoreOrderProductActivityHistoryItem[],
): ProductActivityTableRow[] {
  const rows: ProductActivityTableRow[] = []
  const salesParents = new Map<string, ProductActivityTableRow>()

  items.forEach((item, index) => {
    if (item.recordType === 'order') {
      rows.push({
        ...item,
        tableKey: `order-${item.orderGUID || index}`,
      })
      return
    }

    const periodKey = getSalesPeriodKey(item)
    const tableKey = `sales-period-${periodKey}`

    if (item.recordType === 'salesSubtotal') {
      const existing = salesParents.get(periodKey)
      if (existing) {
        Object.assign(existing, item, {
          tableKey,
          isSalesContinuation: false,
        })
        return
      }

      const parent: ProductActivityTableRow = {
        ...item,
        tableKey,
        children: [],
      }
      salesParents.set(periodKey, parent)
      rows.push(parent)
      return
    }

    let parent = salesParents.get(periodKey)
    if (!parent) {
      // 服务端分页可能把小计留在上一页；用非小计占位行保证本页每日销量仍可折叠查看。
      parent = {
        recordType: 'salesSubtotal',
        recordDate: item.periodEndDate ?? item.recordDate,
        periodStartDate: item.periodStartDate,
        periodEndDate: item.periodEndDate,
        tableKey,
        children: [],
        isSalesContinuation: true,
      }
      salesParents.set(periodKey, parent)
      rows.push(parent)
    }

    parent.children?.push({
      ...item,
      tableKey: `sales-${periodKey}-${item.recordDate || index}-${index}`,
    })
  })

  return rows
}
