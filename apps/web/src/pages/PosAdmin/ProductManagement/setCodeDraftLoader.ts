export type SetCodeDraftPage<T> = {
  items: T[]
  total: number
}

export const MAX_SET_CODE_DRAFT_ROWS = 20_000

export async function loadCompleteSetCodeDraftRows<T>({
  fetchPage,
  getRowId,
  initialPageSize = 200,
  maxItems = MAX_SET_CODE_DRAFT_ROWS,
}: {
  fetchPage: (pageIndex: number, pageSize: number) => Promise<SetCodeDraftPage<T>>
  getRowId: (row: T) => string | undefined
  initialPageSize?: number
  maxItems?: number
}): Promise<T[]> {
  const firstPage = await fetchPage(1, initialPageSize)
  const expectedTotal = normalizeTotal(firstPage.total)
  if (expectedTotal > maxItems) {
    throw new Error('商品条码数量超过安全上限')
  }
  if (firstPage.items.length > expectedTotal) {
    throw new Error('商品条码明细总数不一致')
  }

  // 首屏拿到权威总数后，从第 1 行一次性回读完整快照，避免 UpdatedAt 同值时 offset 分页重叠或漏行。
  const snapshot = firstPage.items.length === expectedTotal
    ? firstPage
    : await fetchPage(1, expectedTotal)
  const snapshotTotal = normalizeTotal(snapshot.total)
  if (snapshotTotal !== expectedTotal || snapshot.items.length !== snapshotTotal) {
    throw new Error('商品条码明细总数不一致')
  }

  const rowIds = new Set<string>()
  for (const row of snapshot.items) {
    const rowId = getRowId(row)
    if (!rowId) {
      throw new Error('商品条码明细缺少稳定行标识')
    }
    if (rowIds.has(rowId)) {
      throw new Error('商品条码明细存在重复行')
    }
    rowIds.add(rowId)
  }

  return snapshot.items
}

function normalizeTotal(total: number) {
  if (!Number.isSafeInteger(total) || total < 0) {
    throw new Error('商品条码明细总数无效')
  }
  return total
}
