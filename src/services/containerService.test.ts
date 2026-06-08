import {
  batchUpdateDetails,
  getComingSoonContainerProducts,
  getComingSoonContainerSummaries,
  syncContainersFromHq,
  translateHqProductNamesByContainerNumber,
  updateContainer,
} from './containerService'
import type { UpdateContainerDetailRequest, UpdateContainerRequest } from '../types/container'

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualJson = JSON.stringify(actual)
  const expectedJson = JSON.stringify(expected)

  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`)
  }
}

async function assertRejects(execute: () => Promise<unknown>, expectedMessage: string, label: string) {
  try {
    await execute()
  } catch (error) {
    const actualMessage = error instanceof Error ? error.message : String(error)
    assertEqual(actualMessage, expectedMessage, label)
    return
  }

  throw new Error(`${label}. Expected promise to reject`)
}

const originalFetch = globalThis.fetch
let capturedUrl = ''
let capturedInit: RequestInit | undefined
const capturedUrls: string[] = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  capturedUrl = String(input)
  capturedInit = init

  return new Response(JSON.stringify({
    success: true,
    data: {
      TotalCandidates: 1,
      TotalTranslated: 1,
      TotalSkipped: 0,
      TotalFailed: 0,
      Samples: { 自动脱毛梳: 'Pet Grooming Comb' },
    },
  }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}) as typeof fetch

try {
  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedInit = init

    return new Response(JSON.stringify({ success: true }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  const updatePayload: UpdateContainerRequest = {
    实际到货日期: '2026-06-16',
    汇率: 4.5,
    运费: 1280,
    备注: '状态切换测试',
    状态: 1,
  }
  await updateContainer('OOCU5568972', updatePayload)

  assertEqual(
    capturedUrl,
    '/api/react/v1/containers/OOCU5568972',
    'updateContainer should keep the React container update URL unchanged',
  )
  assertEqual(capturedInit?.method, 'PUT', 'updateContainer should use PUT')
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    updatePayload,
    'updateContainer should send container status with the header update payload',
  )

  const detailUpdates: UpdateContainerDetailRequest[] = [
    { hguid: 'D-CLEAR-EN', ClearEnglishName: true },
  ]
  await batchUpdateDetails(detailUpdates)

  assertEqual(
    capturedUrl,
    '/api/react/v1/containers/batch-update-details',
    'batchUpdateDetails should keep the React detail update URL unchanged',
  )
  assertEqual(capturedInit?.method, 'POST', 'batchUpdateDetails should use POST')
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    [{ HGUID: 'D-CLEAR-EN', ClearEnglishName: true }],
    'batchUpdateDetails should send the explicit English-name clear marker',
  )

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedInit = init

    return new Response(JSON.stringify({
      success: true,
      data: {
        TotalCandidates: 1,
        TotalTranslated: 1,
        TotalSkipped: 0,
        TotalFailed: 0,
        Samples: { 自动脱毛梳: 'Pet Grooming Comb' },
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  await translateHqProductNamesByContainerNumber('CSNU6601647')

  assertEqual(
    capturedUrl,
    '/api/react/v1/hq-products/translate-names/by-container-number',
    'HQ container translation should call the body-based endpoint',
  )
  assertEqual(capturedInit?.method, 'POST', 'HQ container translation should use POST')
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    { ContainerNumbers: ['CSNU6601647'], OverwriteExisting: false },
    'HQ container translation should send container numbers in the request body',
  )

  globalThis.fetch = (async () => new Response(JSON.stringify({
    success: true,
    data: {
      totalCandidates: 9,
      totalTranslated: 7,
      totalSkipped: 1,
      totalFailed: 1,
      samples: { 大草莓: 'Large Strawberry' },
    },
  }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })) as typeof fetch

  const camelCaseTranslation = await translateHqProductNamesByContainerNumber('CSGU7149907')
  assertDeepEqual(
    camelCaseTranslation,
    {
      TotalCandidates: 9,
      TotalTranslated: 7,
      TotalSkipped: 1,
      TotalFailed: 1,
      Samples: { 大草莓: 'Large Strawberry' },
    },
    'HQ container translation should normalize camelCase backend statistics',
  )

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedInit = init

    return new Response(JSON.stringify({
      success: false,
      message: 'HQ 同步失败：业务错误',
      data: {
        isSuccess: false,
        message: '不应返回成功结果',
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  await assertRejects(
    () => syncContainersFromHq('2026-05-01'),
    'HQ 同步失败：业务错误',
    'syncContainersFromHq should throw backend message when success is false',
  )
  assertEqual(capturedUrl, '/api/react/v1/containers/sync-from-hq', 'syncContainersFromHq should keep the sync URL unchanged')
  assertEqual(capturedInit?.method, 'POST', 'syncContainersFromHq should use POST')
  assertDeepEqual(
    JSON.parse(String(capturedInit?.body)),
    { startDate: '2026-05-01' },
    'syncContainersFromHq should keep the request body unchanged',
  )

  globalThis.fetch = (async () => new Response(JSON.stringify({
    success: false,
    message: 'HQ 同步失败：HTTP 错误',
  }), {
    status: 500,
    headers: { 'Content-Type': 'application/json' },
  })) as typeof fetch

  await assertRejects(
    () => syncContainersFromHq(),
    'HQ 同步失败：HTTP 错误',
    'syncContainersFromHq should throw backend message when HTTP status is not 2xx',
  )

  capturedUrls.length = 0
  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedInit = init
    capturedUrls.push(capturedUrl)

    return new Response(JSON.stringify({
      success: true,
      data: {
        items: [
          { id: 1, hguid: 'ARRIVED-GUID', 货柜编号: 'ARRIVED-1', 实际到货日期: '2026-06-01' },
          { id: 2, hguid: 'UPCOMING-GUID', 货柜编号: 'UPCOMING-1', 预计到岸日期: '2026-06-16' },
        ],
        total: 2,
        page: 1,
        pageSize: 100,
      },
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  const summaries = await getComingSoonContainerSummaries()

  assertEqual(summaries.length, 2, 'Coming Soon 摘要应返回货柜头列表')
  assertEqual(
    capturedUrls.filter((url) => url.includes('/products')).length,
    0,
    'Coming Soon 摘要首屏不应提前请求货柜商品明细',
  )
  assertEqual(capturedInit?.method, 'POST', 'Coming Soon 摘要仍应使用列表 POST 接口')

  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    capturedUrl = String(input)
    capturedInit = init

    return new Response(JSON.stringify({
      success: true,
      data: [
        {
          id: 1,
          hguid: 'DETAIL-1',
          商品编码: 'P-3',
          商品信息: { 货号: 'HB10', 条形码: '9300000000100', 商品名称: 'Item 10', 零售价格: 1.99 },
          装柜数量: 10,
          是否新商品: false,
        },
        {
          id: 2,
          hguid: 'DETAIL-2',
          商品编码: 'P-1',
          商品信息: { 货号: 'HB2', 商品名称: 'Item 2' },
          装柜数量: 20,
          是否新商品: true,
        },
        {
          id: 3,
          hguid: 'DETAIL-3',
          商品编码: 'P-2',
          商品信息: { 商品名称: 'No Item Number' },
          装柜数量: 30,
          是否新商品: false,
        },
      ],
    }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    })
  }) as typeof fetch

  const products = await getComingSoonContainerProducts('CONTAINER-GUID')

  assertEqual(
    capturedUrl,
    '/api/react/v1/containers/CONTAINER-GUID/products',
    'Coming Soon 单货柜商品应复用现有明细接口',
  )
  assertEqual(capturedInit?.method, 'GET', 'Coming Soon 单货柜商品应使用 GET')
  assertDeepEqual(
    products.map((item) => item.itemNumber ?? ''),
    ['HB2', 'HB10', ''],
    'Coming Soon 单货柜商品应按货号自然排序，空货号排最后',
  )
  assertEqual(
    products.find((item) => item.itemNumber === 'HB10')?.barcode,
    '9300000000100',
    'Coming Soon 单货柜商品应映射商品条形码用于生成条码图',
  )
  assertEqual(
    products.find((item) => item.itemNumber === 'HB10')?.retailPrice,
    1.99,
    'Coming Soon 单货柜商品应映射商品建议零售价',
  )
} finally {
  globalThis.fetch = originalFetch
}
