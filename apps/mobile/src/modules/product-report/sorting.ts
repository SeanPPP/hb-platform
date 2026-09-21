/**
 * 商品报告明细排序。字段取值与后端 enhanced-sales-product-details 的 sortField 一致；
 * 前端排序的全量表与后端分页排序遵循同一规则：本期值 → 同期值 → 代码。
 */
export type ReportSortField = "amount" | "quantity" | "unitPrice";
export type ReportSortOrder = "asc" | "desc";

export interface ReportSort {
  field: ReportSortField;
  order: ReportSortOrder;
}

/** 默认金额降序，与后端历史顺序一致。 */
export const DEFAULT_REPORT_SORT: ReportSort = { field: "amount", order: "desc" };

export function isDefaultReportSort(sort: ReportSort) {
  return sort.field === DEFAULT_REPORT_SORT.field && sort.order === DEFAULT_REPORT_SORT.order;
}

/** 点表头：换到新列时默认降序；再点同一列在降序和升序之间切换。 */
export function toggleReportSort(current: ReportSort, field: ReportSortField): ReportSort {
  if (current.field !== field) return { field, order: "desc" };
  return { field, order: current.order === "desc" ? "asc" : "desc" };
}

export function getReportSortKey(sort: ReportSort) {
  return `${sort.field}:${sort.order}`;
}

/**
 * 每个排序字段读取 [本期值, 同期值]。null 表示无法计算（例如数量为 0 时的均价），
 * 无论升序还是降序都排在最后，避免空值在升序时顶到最前面。
 */
export type ReportSortValues<T> = Record<
  ReportSortField,
  (row: T) => readonly [number | null, number | null]
>;

function compareNullableValues(left: number | null, right: number | null, direction: 1 | -1) {
  if (left === null || right === null) {
    if (left === right) return 0;
    return left === null ? 1 : -1;
  }
  if (left === right) return 0;
  return (left < right ? -1 : 1) * direction;
}

/** 返回排序后的新数组，不修改入参；代码按序数比较兜底，保证同值行顺序稳定。 */
export function sortReportRows<T>(
  rows: readonly T[],
  sort: ReportSort,
  values: ReportSortValues<T>,
  tieBreakKey: (row: T) => string,
): T[] {
  const direction = sort.order === "asc" ? 1 : -1;
  const read = values[sort.field];
  return rows
    .map((row) => ({ row, value: read(row), key: tieBreakKey(row) }))
    .sort((left, right) =>
      compareNullableValues(left.value[0], right.value[0], direction)
      || compareNullableValues(left.value[1], right.value[1], direction)
      || (left.key < right.key ? -1 : left.key > right.key ? 1 : 0))
    .map((entry) => entry.row);
}
