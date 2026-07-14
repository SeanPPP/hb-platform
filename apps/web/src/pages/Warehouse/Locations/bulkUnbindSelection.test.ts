import type { LocationItem, LocationProductUnbindFailure } from '../../../types/location'
import {
  buildSelectedLocationProductBindings,
  coordinateBatchUnbindLocationProducts,
  getSelectableFailedLocationKeys,
  hasUnbindableProducts,
  removeSucceededLocationProductBindings,
} from './bulkUnbindSelection'

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    throw new Error(`${message}\n实际: ${JSON.stringify(actual)}\n预期: ${JSON.stringify(expected)}`)
  }
}

const locations: LocationItem[] = [
  {
    locationGuid: 'location-a',
    products: [
      { productCode: 'product-1' },
      { productCode: ' product-2 ' },
      { productCode: 'product-1' },
      { productCode: '   ' },
      {},
    ],
  },
  {
    locationGuid: 'location-b',
    products: [{ productCode: '' }, {}],
  },
  {
    locationGuid: 'location-c',
    products: [{ productCode: 'product-3' }],
  },
]

const unbindableCases = [
  { name: '多商品货位可解绑', location: locations[0], expected: true },
  { name: '缺少有效 productCode 的货位不可解绑', location: locations[1], expected: false },
]

for (const testCase of unbindableCases) {
  assertDeepEqual(hasUnbindableProducts(testCase.location), testCase.expected, testCase.name)
}

assertDeepEqual(
  buildSelectedLocationProductBindings(locations, ['location-a']),
  [
    { locationGuid: 'location-a', productCode: 'product-1' },
    { locationGuid: 'location-a', productCode: 'product-2' },
  ],
  '所选货位应展开多商品、过滤空代码、trim 并按货位和商品代码去重',
)

const failures: LocationProductUnbindFailure[] = [
  { locationGuid: 'location-a', productCode: 'product-1', message: '失败' },
  { locationGuid: 'location-a', productCode: 'product-2', message: '失败' },
  { locationGuid: 'location-b', productCode: 'missing', message: '失败' },
  { locationGuid: 'location-missing', productCode: 'missing', message: '失败' },
]
const refreshFailedData: LocationItem[] | undefined = undefined

const failedSelectionCases = [
  {
    name: '部分失败只恢复仍有有效商品的失败货位',
    source: locations,
    expected: ['location-a'],
  },
  {
    name: '刷新失败回退旧数据时仍保留失败货位',
    source: refreshFailedData ?? locations,
    expected: ['location-a'],
  },
  {
    name: '刷新后货位已空时不恢复选择',
    source: locations.map((location) =>
      location.locationGuid === 'location-a' ? { ...location, products: [] } : location,
    ),
    expected: [],
  },
]

for (const testCase of failedSelectionCases) {
  assertDeepEqual(
    getSelectableFailedLocationKeys(testCase.source, failures),
    testCase.expected,
    testCase.name,
  )
}

const retrySource: LocationItem[] = [
  {
    locationGuid: 'location-a',
    products: [
      { productCode: 'product-1', productName: '成功商品' },
      { productCode: ' product-2 ', productName: '失败商品' },
      { productName: '无代码商品' },
    ],
  },
  {
    locationGuid: 'location-b',
    products: [{ productCode: 'product-1', productName: '其他货位商品' }],
  },
]
const patchedRetrySource = removeSucceededLocationProductBindings(retrySource, [
  { locationGuid: 'location-a', productCode: 'product-1' },
])
const retryFailures: LocationProductUnbindFailure[] = [
  { locationGuid: 'location-a', productCode: 'product-2', message: '失败' },
]
const retryKeys = getSelectableFailedLocationKeys(patchedRetrySource, retryFailures)

assertDeepEqual(
  buildSelectedLocationProductBindings(patchedRetrySource, retryKeys),
  [{ locationGuid: 'location-a', productCode: 'product-2' }],
  '刷新失败后重试只能再次发送失败关联',
)
assertDeepEqual(
  patchedRetrySource,
  [
    {
      locationGuid: 'location-a',
      products: [
        { productCode: ' product-2 ', productName: '失败商品' },
        { productName: '无代码商品' },
      ],
    },
    retrySource[1],
  ],
  '本地补丁只能精确移除成功关联，其他货位和无代码商品必须保留',
)
assertDeepEqual(retrySource[0].products.length, 3, '本地补丁不能修改原始列表')

async function testBatchUnbindCoordinator() {
  const selectedBindings = buildSelectedLocationProductBindings(retrySource, ['location-a'])

  {
    let unbindCalls = 0
    let refreshCalls = 0
    let receivedBindings: unknown
    const outcome = await coordinateBatchUnbindLocationProducts({
      bindings: selectedBindings,
      locations: retrySource,
      unbind: async (bindings) => {
        unbindCalls += 1
        receivedBindings = bindings
        return { succeeded: [...bindings], failed: [] }
      },
      refresh: async () => {
        refreshCalls += 1
        return []
      },
    })

    assertDeepEqual(unbindCalls, 1, '全成功应调用一次 unbind')
    assertDeepEqual(refreshCalls, 1, '有成功项时应调用一次 refresh')
    assertDeepEqual(receivedBindings, selectedBindings, 'unbind 应收到全部所选关联')
    assertDeepEqual(outcome.nextSelectedRowKeys, [], '全成功应清空选择')
    assertDeepEqual(outcome.shouldApplyPatchedData, false, '刷新成功不应写回本地补丁')
  }

  {
    let unbindCalls = 0
    let refreshCalls = 0
    const outcome = await coordinateBatchUnbindLocationProducts({
      bindings: [selectedBindings[0]],
      locations: retrySource,
      unbind: async (bindings) => {
        unbindCalls += 1
        return {
          succeeded: [],
          failed: bindings.map((binding) => ({ ...binding, message: '失败' })),
        }
      },
      refresh: async () => {
        refreshCalls += 1
        return retrySource
      },
    })

    assertDeepEqual(unbindCalls, 1, '全失败应调用一次 unbind')
    assertDeepEqual(refreshCalls, 0, '零成功项不能调用 refresh')
    assertDeepEqual(outcome.nextSelectedRowKeys, ['location-a'], '全失败应保留有效失败货位')
    assertDeepEqual(outcome.shouldApplyPatchedData, false, '全失败不需要写回本地补丁')
  }

  {
    let unbindCalls = 0
    let refreshCalls = 0
    let receivedBindings: unknown
    const refreshedLocations: LocationItem[] = [
      { locationGuid: 'location-a', products: [{ productCode: 'product-2' }] },
    ]
    const outcome = await coordinateBatchUnbindLocationProducts({
      bindings: selectedBindings,
      locations: retrySource,
      unbind: async (bindings) => {
        unbindCalls += 1
        receivedBindings = bindings
        return {
          succeeded: [selectedBindings[0]],
          failed: [{ ...selectedBindings[1], message: '失败' }],
        }
      },
      refresh: async () => {
        refreshCalls += 1
        return refreshedLocations
      },
    })

    assertDeepEqual(unbindCalls, 1, '部分成功且刷新成功应调用一次 unbind')
    assertDeepEqual(receivedBindings, selectedBindings, '部分成功应传入完整关联列表')
    assertDeepEqual(refreshCalls, 1, '部分成功应调用一次 refresh')
    assertDeepEqual(outcome.nextSelectedRowKeys, ['location-a'], '刷新成功应按服务端数据恢复失败货位')
    assertDeepEqual(outcome.shouldApplyPatchedData, false, '刷新成功不应覆盖服务端数据')
  }

  {
    let unbindCalls = 0
    let refreshCalls = 0
    let receivedBindings: unknown
    const outcome = await coordinateBatchUnbindLocationProducts({
      bindings: selectedBindings,
      locations: retrySource,
      unbind: async (bindings) => {
        unbindCalls += 1
        receivedBindings = bindings
        return {
          succeeded: [selectedBindings[0]],
          failed: [{ ...selectedBindings[1], message: '失败' }],
        }
      },
      refresh: async () => {
        refreshCalls += 1
        return undefined
      },
    })

    assertDeepEqual(unbindCalls, 1, '部分成功且刷新失败应调用一次 unbind')
    assertDeepEqual(receivedBindings, selectedBindings, '刷新失败前应传入完整关联列表')
    assertDeepEqual(refreshCalls, 1, '刷新失败分支也只能调用一次 refresh')
    assertDeepEqual(outcome.shouldApplyPatchedData, true, '刷新失败应标记写回本地补丁')
    assertDeepEqual(outcome.nextSelectedRowKeys, ['location-a'], '刷新失败应从补丁数据恢复失败货位')
    assertDeepEqual(
      buildSelectedLocationProductBindings(outcome.patchedData ?? [], outcome.nextSelectedRowKeys),
      [{ locationGuid: 'location-a', productCode: 'product-2' }],
      '刷新失败后的下一次重试只能包含失败项',
    )
  }
}

await testBatchUnbindCoordinator()

console.log('bulkUnbindSelection.test: ok')
