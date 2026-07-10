export const SUPPLIER_PAGE_SIZE = 20;
export const PRODUCT_PAGE_SIZE = 20;

export function getPageRows<T>(rows: T[], page: number, pageSize: number) {
  return rows.slice((page - 1) * pageSize, page * pageSize);
}
