import type { StoreOrderDetailLine } from "./types";

export const DEFAULT_ORDER_LIST_PAGE_SIZE = 10;

export interface StoreOrderListRequestParams {
  storeCode?: string;
  pageNumber?: number;
  pageSize?: number;
  statusList?: number[];
  keyword?: string;
}

export function buildOrderListRequest(params: StoreOrderListRequestParams = {}) {
  return {
    storeCode: params.storeCode,
    pageNumber: params.pageNumber ?? 1,
    pageSize: params.pageSize ?? DEFAULT_ORDER_LIST_PAGE_SIZE,
    statusList: params.statusList ?? [1, 2, 3],
    keyword: params.keyword,
  };
}

export function getOrderRowNumber(pageNumber: number, pageSize: number, index: number) {
  return (Math.max(1, pageNumber) - 1) * pageSize + index + 1;
}

export function filterOrderDetailLinesByItemNumber(
  items: StoreOrderDetailLine[],
  keyword: string
) {
  const normalizedKeyword = keyword.trim().toLocaleLowerCase();
  if (!normalizedKeyword) {
    return items;
  }

  return items.filter((item) => {
    const itemNumber = item.itemNumber?.trim();
    // 货号存在时只按货号筛选；货号为空才允许条码兜底。
    const searchableValue = itemNumber || item.barcode?.trim() || "";
    return searchableValue.toLocaleLowerCase().includes(normalizedKeyword);
  });
}
