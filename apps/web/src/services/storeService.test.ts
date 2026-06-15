import { getActiveStores, getStores } from './storeService'
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
} finally {
  globalThis.fetch = originalFetch
}
