import {
  RETAIL_PRICE_CHANGES_COLUMN_KEYS,
  buildRetailPriceChangesQuery,
  createRetailPriceChangesRequestCoordinator,
  getBrisbaneMonthRange,
  getRetailPriceChangesViewState,
  normalizeRetailPriceChangesResponse,
  type RetailPriceChangesFilters,
} from './logic'
import { resolveBarcodeFormat } from '../../../utils/barcode'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) throw new Error(`${message}: expected ${String(expected)}, received ${String(actual)}`)
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) throw new Error(`${message}: expected ${expectedJson}, received ${actualJson}`)
}

const brisbaneNow = new Date('2026-08-24T01:30:00.000Z')
assertDeepEqual(
  getBrisbaneMonthRange(brisbaneNow),
  { startDate: '2026-08-01', endDate: '2026-08-31' },
  '默认日期必须按 Brisbane 当前自然月计算',
)
assertDeepEqual(
  getBrisbaneMonthRange(new Date('2026-08-31T14:00:00.000Z')),
  { startDate: '2026-09-01', endDate: '2026-09-30' },
  'UTC 月末跨日后必须进入 Brisbane 的下一个自然月',
)

const defaultFilters: RetailPriceChangesFilters = {
  ...getBrisbaneMonthRange(brisbaneNow),
  keyword: '  HB  001  ',
  onlyWithLocation: true,
}

assertDeepEqual(
  buildRetailPriceChangesQuery(defaultFilters, 1, 50),
  {
    startDate: '2026-08-01',
    endDate: '2026-08-31',
    keyword: 'HB  001',
    onlyWithLocation: true,
    pageNumber: 1,
    pageSize: 50,
  },
  '查询参数必须规范化关键字并默认保留有货位筛选',
)

assertDeepEqual(
  buildRetailPriceChangesQuery({ ...defaultFilters, keyword: '   ', onlyWithLocation: false }, 3, 100),
  {
    startDate: '2026-08-01',
    endDate: '2026-08-31',
    onlyWithLocation: false,
    pageNumber: 3,
    pageSize: 100,
  },
  '翻页必须走后端且空关键字不得发送',
)

assertDeepEqual(
  normalizeRetailPriceChangesResponse({
    success: true,
    data: {
      items: [{
        ProductCode: 'P-001',
        ItemNumber: 'HB001',
        Barcode: '931234',
        productImage: 'https://cdn.example.test/P-001.jpg',
        latestRetailPrice: '12.5',
        lastPriceChangedAtUtc: '2026-08-03T00:20:30Z',
      }],
      total: 9,
      pageNumber: 2,
      pageSize: 20,
    },
  }),
  {
    items: [{
      productCode: 'P-001',
      itemNumber: 'HB001',
      barcode: '931234',
      productImage: 'https://cdn.example.test/P-001.jpg',
      latestRetailPrice: 12.5,
      lastPriceChangedAtUtc: '2026-08-03T00:20:30Z',
    }],
    total: 9,
    pageNumber: 2,
    pageSize: 20,
  },
  '接口响应必须优先使用规范字段并兼容信封',
)

assertDeepEqual(
  normalizeRetailPriceChangesResponse({
    startDate: '2026-08-01',
    endDate: '2026-08-31',
    onlyWithLocation: true,
    items: [{
      productCode: 'P-NULL',
      productImage: null,
      itemNumber: null,
      barcode: null,
      latestRetailPrice: null,
      lastPriceChangedAtUtc: '2026-08-31T13:59:59Z',
    }],
    total: 1,
    pageNumber: 1,
    pageSize: 50,
  }),
  {
    items: [{
      productCode: 'P-NULL',
      itemNumber: undefined,
      barcode: undefined,
      productImage: undefined,
      latestRetailPrice: null,
      lastPriceChangedAtUtc: '2026-08-31T13:59:59Z',
    }],
    total: 1,
    pageNumber: 1,
    pageSize: 50,
  },
  '接口规范的直接响应和空价格必须被正确归一化',
)

assertDeepEqual(RETAIL_PRICE_CHANGES_COLUMN_KEYS, ['image', 'itemNumber', 'barcode', 'latestRetailPrice', 'lastPriceChangedAtUtc'], '表格必须严格保留五列')
assertEqual(resolveBarcodeFormat('4006381333931'), 'EAN13', '有效的 13 位条码必须优先使用 EAN13 图形')
assertEqual(resolveBarcodeFormat('4006381333932'), 'CODE128', 'EAN13 校验位无效时必须回退 CODE128')
assertEqual(resolveBarcodeFormat('HB038-003'), 'CODE128', '非 EAN13 条码必须回退 CODE128')
assertEqual(getRetailPriceChangesViewState(true, null, 0), 'loading', '加载态优先级必须最高')
assertEqual(getRetailPriceChangesViewState(false, new Error('network'), 0), 'error', '错误态必须提供重试路径')
assertEqual(getRetailPriceChangesViewState(false, null, 0), 'empty', '无数据时必须展示空态')
assertEqual(getRetailPriceChangesViewState(false, null, 1), 'table', '有数据时必须展示表格')

const coordinator = createRetailPriceChangesRequestCoordinator()
const first = coordinator.start()
const second = coordinator.start()
assertEqual(first.signal.aborted, true, '发起新请求时必须中止旧请求')
assertEqual(coordinator.isLatest(first.requestId), false, '旧响应不得覆盖最新查询结果')
assertEqual(coordinator.isLatest(second.requestId), true, '最新响应必须允许提交')
coordinator.dispose()
assertEqual(second.signal.aborted, true, '页面卸载时必须中止在途请求')

console.log('retailPriceChanges.logic.test: ok')
