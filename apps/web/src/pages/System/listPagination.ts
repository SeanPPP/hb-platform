export const DEFAULT_SYSTEM_LIST_PAGE_SIZE = 50

export type SystemListTableAction = 'paginate' | 'sort' | 'filter'

export interface SystemListTablePagination {
  current?: number
  pageSize?: number
}

export interface SystemListPagination {
  page: number
  pageSize: number
}

export {
  createLatestRequestGuard,
  runLatestGuardedRequest,
} from '../../utils/latestRequestGuard'
export type {
  LatestGuardedRequestHandlers,
  LatestRequestGuard,
} from '../../utils/latestRequestGuard'

/**
 * 主列表翻页保留当前页；排序和筛选改变数据集时回到第一页。
 */
export function resolveSystemListPagination(
  action: SystemListTableAction,
  pagination: SystemListTablePagination,
  currentPageSize: number,
): SystemListPagination {
  return {
    page: action === 'paginate' ? pagination.current ?? 1 : 1,
    pageSize: pagination.pageSize ?? currentPageSize,
  }
}
