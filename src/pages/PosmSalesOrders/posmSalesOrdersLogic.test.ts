import { OrderType } from '../../types/posmSalesOrder'
import { buildPosmSalesOrderListQuery } from './posmSalesOrdersLogic'

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

const currentState = {
  startDate: '2026-06-10',
  endDate: '2026-06-11',
  branchCode: 'S01',
  orderType: OrderType.Paid,
  keyword: 'invoice',
  page: 3,
  pageSize: 50,
}

assertDeepEqual(
  buildPosmSalesOrderListQuery(currentState, { page: 1 }),
  {
    startDate: '2026-06-10',
    endDate: '2026-06-11',
    branchCode: 'S01',
    orderType: OrderType.Paid,
    keyword: 'invoice',
    pageNumber: 1,
    pageSize: 50,
  },
  '搜索时应使用显式 page=1，而不是旧的第 3 页状态',
)

assertDeepEqual(
  buildPosmSalesOrderListQuery(currentState, {
    startDate: '2026-06-12',
    endDate: '2026-06-12',
    branchCode: '',
    orderType: OrderType.All,
    keyword: '',
    page: 1,
  }),
  {
    startDate: '2026-06-12',
    endDate: '2026-06-12',
    branchCode: undefined,
    orderType: OrderType.All,
    keyword: undefined,
    pageNumber: 1,
    pageSize: 50,
  },
  '重置时应使用同一次计算出的默认筛选条件立即请求',
)

console.log('posmSalesOrdersLogic.test: ok')
