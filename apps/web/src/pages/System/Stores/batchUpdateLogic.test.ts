import {
  buildBatchUpdateStoresRequest,
  shouldClearStoreSelection,
} from './batchUpdateLogic'

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)
  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

function assertThrows(action: () => unknown, expectedCode: string, label: string) {
  try {
    action()
  } catch (error) {
    if (
      error instanceof Error
      && 'code' in error
      && String(error.code) === expectedCode
    ) {
      return
    }
    throw error
  }

  throw new Error(`${label}. Expected error code: ${expectedCode}`)
}

const request = buildBatchUpdateStoresRequest(
  ['store-1', 'store-2'],
  {
    applyTimeZoneId: true,
    timeZoneId: '  Australia/Sydney  ',
    applyAbn: true,
    abn: '   ',
    applyBrandName: true,
    brandName: '  Hot Bargain  ',
    applyIsActive: true,
    isActive: false,
    applyReturnPolicy: true,
    returnPolicy: '\n  ',
  },
)

assertDeepEqual(
  request,
  {
    storeGuids: ['store-1', 'store-2'],
    fields: ['timeZoneId', 'abn', 'brandName', 'isActive', 'returnPolicy'],
    timeZoneId: 'Australia/Sydney',
    abn: null,
    brandName: 'Hot Bargain',
    isActive: false,
    returnPolicy: null,
  },
  '批量请求应裁剪文本、把空白转成 null，并保留显式 false',
)

assertDeepEqual(
  buildBatchUpdateStoresRequest(
    ['store-3'],
    {
      applyAbn: true,
      abn: '12 345 678 901',
      timeZoneId: 'Australia/Perth',
      brandName: 'Ignored Brand',
      isActive: false,
      returnPolicy: 'Ignored policy',
    },
  ),
  {
    storeGuids: ['store-3'],
    fields: ['abn'],
    abn: '12 345 678 901',
  },
  '未勾选字段不应进入请求体',
)

assertThrows(
  () => buildBatchUpdateStoresRequest(['store-1'], {}),
  'NO_FIELDS_SELECTED',
  '未选择字段时应阻止提交',
)
assertThrows(
  () => buildBatchUpdateStoresRequest(['store-1'], { applyTimeZoneId: true, timeZoneId: '  ' }),
  'TIME_ZONE_REQUIRED',
  '勾选时区后空白值应阻止提交',
)
assertThrows(
  () => buildBatchUpdateStoresRequest([], { applyAbn: true, abn: '' }),
  'INVALID_TARGETS',
  '没有目标分店时应阻止提交',
)
assertDeepEqual(
  buildBatchUpdateStoresRequest(
    ['store-A', 'store-a'],
    { applyAbn: true, abn: '12 345 678 901' },
  ),
  {
    storeGuids: ['store-A', 'store-a'],
    fields: ['abn'],
    abn: '12 345 678 901',
  },
  '不透明分店标识仅在完全相同时才应视为重复',
)
assertThrows(
  () => buildBatchUpdateStoresRequest(
    ['store-1', 'store-1'],
    { applyAbn: true, abn: '' },
  ),
  'INVALID_TARGETS',
  '完全相同的分店标识仍应阻止提交',
)
assertThrows(
  () => buildBatchUpdateStoresRequest(['store-1'], { applyIsActive: true }),
  'IS_ACTIVE_REQUIRED',
  '勾选收银状态后必须保留明确布尔值',
)

assertDeepEqual(
  {
    query: shouldClearStoreSelection('query'),
    filter: shouldClearStoreSelection('filter'),
    sort: shouldClearStoreSelection('sort'),
    paginate: shouldClearStoreSelection('paginate'),
    refresh: shouldClearStoreSelection('refresh'),
  },
  {
    query: true,
    filter: true,
    sort: false,
    paginate: false,
    refresh: false,
  },
  '仅搜索和筛选范围变化时应清空跨页选择',
)

console.log('batchUpdateLogic.test: ok')
