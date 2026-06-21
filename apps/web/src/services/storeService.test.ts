import { createStore, getActiveStores, getNextStoreCode, getStores } from './storeService'
import type { StoreDto } from '../types/store'

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
      ['sortField', 'brandName'],
      ['sortOrder', 'desc'],
    ],
    '分店列表查询应该透传品牌、状态和排序参数',
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
} finally {
  globalThis.fetch = originalFetch
}
