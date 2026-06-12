import type { PosmSalesOrderQueryParams } from '../../types/posmSalesOrder'
import { OrderType } from '../../types/posmSalesOrder'

export interface PosmSalesOrderListQueryState {
  startDate: string
  endDate: string
  branchCode: string
  orderType: OrderType
  keyword: string
  page: number
  pageSize: number
}

export function buildPosmSalesOrderListQuery(
  state: PosmSalesOrderListQueryState,
  overrides: Partial<PosmSalesOrderListQueryState> = {},
): PosmSalesOrderQueryParams {
  const nextState = { ...state, ...overrides }

  return {
    startDate: nextState.startDate,
    endDate: nextState.endDate,
    branchCode: nextState.branchCode || undefined,
    orderType: nextState.orderType,
    keyword: nextState.keyword || undefined,
    pageNumber: nextState.page,
    pageSize: nextState.pageSize,
  }
}
