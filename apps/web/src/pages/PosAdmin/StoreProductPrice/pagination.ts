type PaginationTranslator = (
  key: string,
  fallback: string,
  values: { count: number; pages: number },
) => string

export function getPaginationTotalPages(total: number, pageSize: number) {
  const safePageSize = pageSize > 0 ? pageSize : 1
  return total > 0 ? Math.ceil(total / safePageSize) : 0
}

export function formatPaginationTotalText(
  total: number,
  pageSize: number,
  translate: PaginationTranslator,
) {
  return translate('posAdmin.productPrice.paginationTotal', '共 {{count}} 条 / {{pages}} 页', {
    count: total,
    pages: getPaginationTotalPages(total, pageSize),
  })
}
