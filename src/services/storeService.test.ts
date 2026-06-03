import { getActiveStores } from './storeService'
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
} finally {
  globalThis.fetch = originalFetch
}
