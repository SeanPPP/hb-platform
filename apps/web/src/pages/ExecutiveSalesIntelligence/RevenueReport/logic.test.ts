import assert from 'node:assert/strict'
import {
  aggregateHourlyRows,
  buildRevenueOverviewSearch,
  buildSalesDetailPath,
  getHierarchyBranchCode,
  getHierarchyDateRange,
  getRevenueSummary,
  getRevenueTrend,
  makeRevenueQueryScope,
  parseRevenueOverviewSearch,
  sortRevenueBranchesByRevenue,
} from './logic'
import type { RevenueBranch, RevenueHourly, RevenueWeeklyNode } from './types'

const branches: RevenueBranch[] = [
  { rank: 1, branchCode: ' B-02 ', branchName: '北区', revenue: 200, revenueLY: 100, orderCount: 8, orderCountLY: 5, aov: 25, aovLY: 20 },
  { rank: 2, branchCode: 'A01', branchName: '南区', revenue: 100, revenueLY: 0, orderCount: 2, orderCountLY: 0, aov: 50, aovLY: 0 },
]

assert.deepEqual(getRevenueSummary(branches, null, true), {
  revenue: 300,
  revenueLY: 100,
  orders: 10,
  ordersLY: 5,
  aov: 30,
  aovLY: 20,
  decliningBranches: 0,
  newBranches: 1,
})
assert.equal(getRevenueSummary(branches, 'b-02', true).revenue, 200, '分店代码匹配应忽略空格与大小写')
assert.equal(getRevenueSummary(branches, null, false).revenueLY, null, '关闭对比后不得伪造同期零值')
const unsortedBranches: RevenueBranch[] = [
  { ...branches[0]!, rank: 3, branchCode: 'C03', revenue: 50 },
  { ...branches[1]!, rank: 2, branchCode: 'B02', revenue: 100 },
  { ...branches[1]!, rank: 1, branchCode: 'A01', revenue: 100 },
]
assert.deepEqual(
  sortRevenueBranchesByRevenue(unsortedBranches).map(branch => [branch.branchCode, branch.rank]),
  [['A01', 1], ['B02', 2], ['C03', 3]],
  '分店必须按本期营业额降序，同额沿用原排名并重新生成连续名次',
)
assert.deepEqual(unsortedBranches.map(branch => branch.rank), [3, 2, 1], '排序不得修改接口原始数据')
assert.deepEqual(getRevenueTrend(10, 0), { text: 'new', tone: 'neutral' })
assert.deepEqual(getRevenueTrend(0, 0), { text: '0.0%', tone: 'neutral' })
assert.deepEqual(getRevenueTrend(9, null), { text: '—', tone: 'neutral' })

const hours: RevenueHourly[] = [
  { hour: '10:00', branchCode: 'A01', branchName: '南区', revenue: 40, revenueLY: 20, percentage: 100, isPeak: true },
  { hour: '09:00', branchCode: 'A01', branchName: '南区', revenue: 10, revenueLY: 20, percentage: 25, isPeak: false },
  { hour: '10:00', branchCode: 'B02', branchName: '北区', revenue: 60, revenueLY: 30, percentage: 100, isPeak: true },
]
assert.deepEqual(aggregateHourlyRows(hours, true), [
  { hour: '09:00', revenue: 10, revenueLY: 20, percentage: 10, isPeak: false },
  { hour: '10:00', revenue: 100, revenueLY: 50, percentage: 100, isPeak: true },
])
assert.equal(aggregateHourlyRows(hours, false)[0]?.revenueLY, null, '关闭对比后时段同期必须为空')

const weekly: RevenueWeeklyNode = {
  key: 'w2026-36', level: 'week', hierarchy: '2026-W36', revenue: 300, revenueLY: 200,
  orders: 10, ordersLY: 8, aov: 30, aovLY: 25,
  children: [{
    key: 'w2026-36-B-02', level: 'branch', hierarchy: '北区', revenue: 200, revenueLY: 100,
    orders: 8, ordersLY: 5, aov: 25, aovLY: 20,
    children: [
      { key: 'w2026-36-B-02-20260901', level: 'date', hierarchy: '2026-09-01', revenue: 90, revenueLY: 40, orders: 3, ordersLY: 2, aov: 30, aovLY: 20 },
      { key: 'w2026-36-B-02-20260903', level: 'date', hierarchy: '2026-09-03', revenue: 110, revenueLY: 60, orders: 5, ordersLY: 3, aov: 22, aovLY: 20 },
    ],
  }],
}
assert.equal(getHierarchyBranchCode(weekly.children![0]!), 'B-02', '带连字符分店代码必须完整解析')
assert.equal(getHierarchyBranchCode(weekly.children![0]!.children![0]!), 'B-02', '日期节点必须继承完整分店代码')
assert.deepEqual(getHierarchyDateRange(weekly), { startDate: '2026-09-01', endDate: '2026-09-03' })
assert.equal(getHierarchyDateRange(weekly.children![0]!.children![0]!)?.startDate, '2026-09-01')
assert.deepEqual(
  getHierarchyDateRange(weekly, { startDate: '2026-08-31', endDate: '2026-09-06' }),
  { startDate: '2026-08-31', endDate: '2026-09-06' },
  '周点击必须使用 ISO 周与查询范围交集，不能因周一没有销售而丢掉周一',
)

assert.equal(
  buildSalesDetailPath('B & 02', {
    startDate: '2026-09-01', endDate: '2026-09-03', compare: false, compareMode: 'ByDate',
  }),
  '/executive-sales-intelligence/sales-detail-v2?branch=B+%26+02&startDate=2026-09-01&endDate=2026-09-03&compare=false&compareMode=ByDate',
  '反查链接必须安全编码并保留全部日期与同比参数',
)
assert.equal(makeRevenueQueryScope(' user-1 ', ['B-02', 'a01', 'A01']), 'user-1:a01,b-02')
assert.equal(makeRevenueQueryScope('user-1', null), 'user-1:all')
assert.deepEqual(parseRevenueOverviewSearch('?branch=B-02&startDate=2026-09-01&endDate=2026-09-03', '2026-09-06'), {
  selection: { startDate: '2026-09-01', endDate: '2026-09-03', quick: 'custom', compare: true, compareMode: 'ByWeek' },
  branchCode: 'B-02',
})
assert.deepEqual(parseRevenueOverviewSearch('?startDate=bad&endDate=2026-09-03', '2026-09-06'), {
  selection: { startDate: '2026-09-06', endDate: '2026-09-06', quick: 'today', compare: true, compareMode: 'ByWeek' },
  branchCode: null,
}, '非法 URL 日期不得进入查询')
assert.equal(
  buildRevenueOverviewSearch({ startDate: '2026-09-01', endDate: '2026-09-03', quick: 'custom', compare: true, compareMode: 'ByWeek' }, 'B & 02'),
  '?branch=B+%26+02&startDate=2026-09-01&endDate=2026-09-03',
)
assert.deepEqual(parseRevenueOverviewSearch('?startDate=2026-09-01&endDate=2026-09-03&compare=0&compareMode=ByDate'), {
  selection: { startDate: '2026-09-01', endDate: '2026-09-03', quick: 'custom', compare: false, compareMode: 'ByDate' },
  branchCode: null,
})
assert.equal(
  parseRevenueOverviewSearch('?startDate=2026-09-01&endDate=2026-09-03&compare=false').selection.compare,
  false,
  '销售明细返回的布尔同比参数必须可回放',
)
assert.equal(
  buildRevenueOverviewSearch({ startDate: '2026-09-01', endDate: '2026-09-03', quick: 'custom', compare: false, compareMode: 'ByDate' }, null),
  '?startDate=2026-09-01&endDate=2026-09-03&compare=0&compareMode=ByDate',
)
const replaySelection = { startDate: '2026-09-01', endDate: '2026-09-03', quick: 'custom', compare: false, compareMode: 'ByDate' } as const
assert.deepEqual(
  parseRevenueOverviewSearch(buildRevenueOverviewSearch(replaySelection, 'B-02')),
  { selection: replaySelection, branchCode: 'B-02' },
  'URL 写入后重新载入必须恢复日期、分店和同比状态',
)

console.log('营业额视图派生、层级联动与反查参数：通过')
