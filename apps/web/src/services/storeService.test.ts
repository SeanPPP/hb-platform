import {
  batchUpdateStores,
  createStore,
  getActiveStores,
  getNextStoreCode,
  getStores,
  syncStoreToHq,
} from './storeService'
import type { BatchUpdateStoresRequest, StoreDto } from '../types/store'

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

function buildStore(storeCode: string, storeName: string): StoreDto {
  return {
    storeGUID: `${storeCode}-guid`,
    storeCode,
    storeName,
    isActive: true,
    createdAt: '2026-06-01T00:00:00Z',
    updatedAt: '2026-06-01T00:00:00Z',
  }
}

const originalFetch = globalThis.fetch

globalThis.fetch = (async () => new Response(JSON.stringify({
  success: true,
  data: [
    buildStore('1001', 'Robinson'),
    buildStore('1009', 'Lakehaven'),
    buildStore('1005', 'Charlestown Square'),
  ],
}), {
  status: 200,
  headers: { 'Content-Type': 'application/json' },
})) as typeof fetch

try {
  const stores = await getActiveStores()

  assertDeepEqual(
    stores,
    [
      { label: 'Charlestown Square', value: '1005' },
      { label: 'Lakehaven', value: '1009' },
      { label: 'Robinson', value: '1001' },
    ],
    '分店选项应该按照名称升序排列',
  )

  let requestedUrl = ''
  globalThis.fetch = (async (input) => {
    requestedUrl = String(input)
    return new Response(JSON.stringify({
      success: true,
      data: {
        items: [],
        total: 0,
        page: 2,
        pageSize: 50,
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  await getStores({
    page: 2,
    pageSize: 50,
    brandName: 'Hot Bargain',
    isActive: true,
    timeZoneId: 'Australia/Sydney',
    sortField: 'brandName',
    sortOrder: 'desc',
  })

  const requestUrl = new URL(requestedUrl, 'http://localhost')
  assertDeepEqual(
    Array.from(requestUrl.searchParams.entries()),
    [
      ['page', '2'],
      ['pageSize', '50'],
      ['brandName', 'Hot Bargain'],
      ['isActive', 'true'],
      ['timeZoneId', 'Australia/Sydney'],
      ['sortField', 'brandName'],
      ['sortOrder', 'desc'],
    ],
    '分店列表查询应该透传品牌、状态、时区和排序参数',
  )

  requestedUrl = ''
  await getStores({ timeZoneId: '__unset__' })
  assertDeepEqual(
    Array.from(new URL(requestedUrl, 'http://localhost').searchParams.entries()),
    [['timeZoneId', '__unset__']],
    '分店列表查询应该透传未设置时区筛选标识',
  )

  let nextCodeUrl = ''
  globalThis.fetch = (async (input) => {
    nextCodeUrl = String(input)
    return new Response(JSON.stringify({
      success: true,
      data: '1043',
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  const nextCode = await getNextStoreCode()
  assertDeepEqual(
    {
      path: new URL(nextCodeUrl, 'http://localhost').pathname,
      nextCode,
    },
    {
      path: '/api/stores/next-code',
      nextCode: '1043',
    },
    '获取下一个分店编码应调用 next-code 接口并返回编码字符串',
  )

  let capturedCreateUrl = ''
  let capturedCreateInit: RequestInit | undefined
  globalThis.fetch = (async (input, init) => {
    capturedCreateUrl = String(input)
    capturedCreateInit = init
    return new Response(JSON.stringify({
      success: true,
      data: {
        ...buildStore('1999', 'New Store'),
        isActive: false,
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  const createdStore = await createStore({
    storeName: 'New Store',
    storeCode: '1999',
    brandName: 'Hot Bargain',
    isActive: false,
  })

  assertDeepEqual(
    {
      path: new URL(capturedCreateUrl, 'http://localhost').pathname,
      method: capturedCreateInit?.method,
      body: JSON.parse(String(capturedCreateInit?.body)),
      isActive: createdStore.isActive,
    },
    {
      path: '/api/stores',
      method: 'POST',
      body: {
        storeName: 'New Store',
        storeCode: '1999',
        brandName: 'Hot Bargain',
        isActive: false,
      },
      isActive: false,
    },
    '创建分店接口应使用 POST 并原样提交未启用收银系统状态',
  )

  let capturedSyncUrl = ''
  let capturedSyncInit: RequestInit | undefined
  globalThis.fetch = (async (input, init) => {
    capturedSyncUrl = String(input)
    capturedSyncInit = init
    return new Response(JSON.stringify({
      success: true,
      data: true,
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  const syncResult = await syncStoreToHq('store-guid-1')
  assertDeepEqual(
    {
      path: new URL(capturedSyncUrl, 'http://localhost').pathname,
      method: capturedSyncInit?.method,
      syncResult,
    },
    {
      path: '/api/stores/guid/store-guid-1/sync-hq',
      method: 'POST',
      syncResult: true,
    },
    '同步HQ分店应调用当前分店的 sync-hq POST 接口',
  )

  globalThis.fetch = (async () => new Response(JSON.stringify({
    success: false,
    message: '同步HQ分店失败',
    errorCode: 'SYNC_STORE_TO_HQ_ERROR',
  }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })) as typeof fetch

  let failed = false
  try {
    await syncStoreToHq('store-guid-2')
  } catch (error) {
    failed = error instanceof Error && error.message === '同步HQ分店失败'
  }
  assertDeepEqual(failed, true, '同步HQ分店业务失败时应抛出后端错误消息')

  let capturedBatchUrl = ''
  let capturedBatchInit: RequestInit | undefined
  globalThis.fetch = (async (input, init) => {
    capturedBatchUrl = String(input)
    capturedBatchInit = init
    return new Response(JSON.stringify({
      success: true,
      data: {
        requestedCount: 2,
        updatedCount: 2,
        updatedStoreGuids: ['store-1', 'store-2'],
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  const batchPayload: BatchUpdateStoresRequest = {
    storeGuids: ['store-1', 'store-2'],
    fields: ['abn', 'isActive'],
    abn: null,
    isActive: false,
  }
  const batchResult = await batchUpdateStores(batchPayload)
  assertDeepEqual(
    {
      path: new URL(capturedBatchUrl, 'http://localhost').pathname,
      method: capturedBatchInit?.method,
      body: JSON.parse(String(capturedBatchInit?.body)),
      result: batchResult,
    },
    {
      path: '/api/stores/batch',
      method: 'PATCH',
      body: batchPayload,
      result: {
        requestedCount: 2,
        updatedCount: 2,
        updatedStoreGuids: ['store-1', 'store-2'],
      },
    },
    '批量修改分店应使用 PATCH 路径并原样保留 null 与 false',
  )

  globalThis.fetch = (async () => new Response(JSON.stringify({
    success: false,
    message: '部分分店不存在或已删除',
    errorCode: 'STORE_BATCH_TARGET_INVALID',
    data: {
      requestedCount: 2,
      updatedCount: 0,
      updatedStoreGuids: [],
    },
  }), {
    status: 409,
    headers: { 'Content-Type': 'application/json' },
  })) as typeof fetch

  let batchFailure = ''
  try {
    await batchUpdateStores(batchPayload)
  } catch (error) {
    batchFailure = error instanceof Error ? error.message : ''
  }
  assertDeepEqual(
    batchFailure,
    '部分分店不存在或已删除',
    '批量修改业务错误应保留后端错误消息',
  )
} finally {
  globalThis.fetch = originalFetch
}
