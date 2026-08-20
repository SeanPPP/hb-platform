import {
  getProductSalesAnalysisOptions,
  queryProductSalesBranches,
  queryProductSalesBranchDaily,
  queryProductSalesCandidates,
  queryProductSalesDaily,
  queryProductSalesSummary,
} from './productSalesAnalysisService'
import type {
  ProductSalesAnalysisScope,
  ProductSalesAnalysisSelection,
} from '../types/productSalesAnalysis'

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}。Expected: ${String(expected)}, received: ${String(actual)}`)
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  assertEqual(JSON.stringify(actual), JSON.stringify(expected), message)
}

async function assertRejects(execute: () => Promise<unknown>, expectedMessage: string, message: string) {
  try {
    await execute()
  } catch (error) {
    assertEqual(error instanceof Error ? error.message : String(error), expectedMessage, message)
    return
  }
  throw new Error(message)
}

const originalFetch = globalThis.fetch
const captured: { url: string; init: RequestInit | undefined }[] = []
let mockStatus: string | undefined = 'Fresh'
let mockStatisticMessage: string | undefined

function jsonResponse(payload: unknown) {
  return new Response(JSON.stringify(payload), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })
}

try {
  globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    const pathname = new URL(url, 'http://localhost').pathname
    captured.push({ url, init })

    if (pathname.includes('/product-sales-analysis/options')) {
      return jsonResponse({
        success: true,
        data: {
          ...(mockStatus !== undefined ? { StatisticStatus: mockStatus } : {}),
          ...(mockStatisticMessage !== undefined ? { StatisticMessage: mockStatisticMessage } : {}),
          StatisticUpdatedAt: '2026-08-18T01:00:00Z',
          CacheVersion: 'v1',
          Data: {
            AustralianSuppliers: [{ Code: 'A1', Name: 'Aussie One' }],
            // 故意缺失国内供应商数组，服务层必须容忍而不是报错。
          },
        },
      })
    }

    if (pathname.endsWith('/candidates')) {
      return jsonResponse({
        success: true,
        data: {
          ...(mockStatus !== undefined ? { statisticStatus: mockStatus } : {}),
          data: {
            Items: [
              {
                ProductCode: 'P1',
                ItemNumber: 'HB-1',
                Barcode: '9340000000012',
                ProductName: '红罐饮料',
                EnglishName: 'Red Can',
                ImageUrl: '/p1.jpg',
                AustralianSuppliers: [{ Code: 'A1', Name: 'Aussie One' }],
              },
            ],
            Total: 1,
            PageNumber: 2,
            PageSize: 20,
          },
        },
      })
    }

    if (pathname.endsWith('/summary')) {
      return jsonResponse({
        success: true,
        data: {
          ...(mockStatus !== undefined ? { statisticStatus: mockStatus } : {}),
          data: {
            items: [
              {
                productCode: 'P1',
                productName: '红罐饮料',
                englishName: 'Red Can',
                Metrics: {
                  Quantity: 12,
                  SalesAmount: 36,
                  AverageUnitPrice: 3,
                },
              },
            ],
            total: 1,
            pageNumber: 1,
            pageSize: 20,
          },
        },
      })
    }

    if (pathname.endsWith('/product-daily') || pathname.endsWith('/branch-daily')) {
      return jsonResponse({
        success: true,
        data: {
          ...(mockStatus !== undefined ? { statisticStatus: mockStatus } : {}),
          data: [
            {
              Date: '2026-08-17T00:00:00',
              Metrics: {
                Quantity: -2,
                SalesAmount: -10,
                AverageUnitPrice: null,
              },
            },
            {
              date: '2026-08-18T14:30:00Z',
              metrics: {
                quantity: 4,
                salesAmount: 18,
                averageUnitPrice: 4.5,
              },
            },
            {
              Date: '2026-08-19',
              Metrics: {
                Quantity: 6,
                SalesAmount: 24,
                AverageUnitPrice: 4,
              },
            },
          ],
        },
      })
    }

    if (pathname.endsWith('/branches')) {
      return jsonResponse({
        success: true,
        data: {
          ...(mockStatus !== undefined ? { statisticStatus: mockStatus } : {}),
          data: [
            {
              BranchCode: 'S1',
              BranchName: 'Sunnybank',
              metrics: { quantity: 5, salesAmount: 20, averageUnitPrice: 4 },
            },
          ],
        },
      })
    }

    return jsonResponse({ success: false, message: '统计未就绪' })
  }) as typeof fetch

  const controller = new AbortController()
  const options = await getProductSalesAnalysisOptions({
    startDate: '2026-07-20',
    endDate: '2026-08-18',
    australianSupplierCodes: ['A1'],
    chinaSupplierCodes: [],
  }, controller.signal)

  assertEqual(new URL(captured[0].url, 'http://localhost').pathname, '/api/react/v1/dashboard/product-sales-analysis/options', 'options 应调用正确路径')
  assertEqual(captured[0].init?.method, 'GET', 'options 应使用 GET')
  assertEqual(captured[0].init?.signal, controller.signal, 'options 应透传 AbortSignal')
  assertEqual(options.statisticStatus, 'Fresh', '领域响应元数据应解包 PascalCase')
  assertEqual(options.statisticUpdatedAt, '2026-08-18T01:00:00Z', '统计更新时间应保留')
  assertDeepEqual(options.data.australianSuppliers, [{ code: 'A1', name: 'Aussie One' }], '澳洲供应商应归一化')
  assertDeepEqual(options.data.chinaSuppliers, [], '缺失供应商数组应回退为空数组')

  const candidates = await queryProductSalesCandidates(
    { startDate: '2026-07-20', endDate: '2026-08-18', australianSupplierCodes: [], chinaSupplierCodes: [] },
    { mode: 'allFiltered', includedProductCodes: [], excludedProductCodes: ['P9'] },
    { pageNumber: 2, pageSize: 20, sortBy: 'salesAmount', sortDirection: 'desc' },
  )
  assertEqual(captured[1].url, '/api/react/v1/dashboard/product-sales-analysis/candidates', 'candidates 应调用正确路径')
  assertEqual(captured[1].init?.method, 'POST', 'candidates 应使用 POST')
  assertDeepEqual(JSON.parse(String(captured[1].init?.body)), {
    filter: {
      startDate: '2026-07-20',
      endDate: '2026-08-18',
      australianSupplierCodes: [],
      chinaSupplierCodes: [],
    },
    selection: { mode: 'allFiltered', includedProductCodes: [], excludedProductCodes: ['P9'] },
    pageNumber: 2,
    pageSize: 20,
    sortBy: 'salesAmount',
    sortDirection: 'desc',
  }, 'candidates 应发送 filter、selection 和分页')
  assertDeepEqual(candidates.data.items[0], {
    productCode: 'P1',
    itemNumber: 'HB-1',
    barcode: '9340000000012',
    productName: '红罐饮料',
    englishName: 'Red Can',
    imageUrl: '/p1.jpg',
    australianSuppliers: [{ code: 'A1', name: 'Aussie One' }],
    chinaSuppliers: [],
    chinaSupplierUnmapped: undefined,
  }, 'candidates 商品应归一化 Pascal/camel 并容忍缺失数组')
  assertEqual(candidates.data.pageNumber, 2, '分页数据应保留 pageNumber')

  const summary = await queryProductSalesSummary(
    { startDate: '2026-07-20', endDate: '2026-08-18', australianSupplierCodes: [], chinaSupplierCodes: [] },
    { mode: 'included', includedProductCodes: ['P1'], excludedProductCodes: [] },
    { mode: 'selectedProducts' },
    { pageNumber: 1, pageSize: 20 },
  )
  assertDeepEqual(JSON.parse(String(captured[2].init?.body)).scope, { mode: 'selectedProducts' }, 'summary 应发送 selectedProducts scope')
  assertEqual(summary.data.items[0].metrics.quantity, 12, 'summary 指标应归一化')
  assertEqual(summary.data.items[0].metrics.averageUnitPrice, 3, 'summary 均价应保留数字')

  await queryProductSalesDaily(
    { startDate: '2026-07-20', endDate: '2026-08-18', australianSupplierCodes: [], chinaSupplierCodes: [] },
    { mode: 'allFiltered', includedProductCodes: [], excludedProductCodes: [] },
    { mode: 'currentProduct', productCode: 'P1' },
  )
  assertEqual(captured[3].url, '/api/react/v1/dashboard/product-sales-analysis/product-daily', 'product-daily 应调用正确路径')
  assertDeepEqual(JSON.parse(String(captured[3].init?.body)).scope, { mode: 'currentProduct', productCode: 'P1' }, 'product-daily 应发送 currentProduct scope')

  await queryProductSalesBranches(
    { startDate: '2026-07-20', endDate: '2026-08-18', australianSupplierCodes: [], chinaSupplierCodes: [] },
    { mode: 'allFiltered', includedProductCodes: [], excludedProductCodes: [] },
    { mode: 'selectedProducts' },
  )
  assertEqual(captured[4].url, '/api/react/v1/dashboard/product-sales-analysis/branches', 'branches 应调用正确路径')
  assertEqual(captured[4].init?.method, 'POST', 'branches 应使用 POST')
  assertEqual(JSON.parse(String(captured[4].init?.body)).scope.mode, 'selectedProducts', 'branches 应发送 scope')

  await queryProductSalesBranchDaily(
    { startDate: '2026-07-20', endDate: '2026-08-18', australianSupplierCodes: [], chinaSupplierCodes: [] },
    { mode: 'allFiltered', includedProductCodes: [], excludedProductCodes: [] },
    { mode: 'currentProduct', productCode: 'P1' },
    'S1',
  )
  assertEqual(captured[5].url, '/api/react/v1/dashboard/product-sales-analysis/branch-daily', 'branch-daily 应调用正确路径')
  assertDeepEqual(JSON.parse(String(captured[5].init?.body)), {
    filter: {
      startDate: '2026-07-20',
      endDate: '2026-08-18',
      australianSupplierCodes: [],
      chinaSupplierCodes: [],
    },
    selection: { mode: 'allFiltered', includedProductCodes: [], excludedProductCodes: [] },
    scope: { mode: 'currentProduct', productCode: 'P1' },
    branchCode: 'S1',
  }, 'branch-daily 应发送 filter、selection、scope 和 branchCode')

  const daily = await queryProductSalesDaily(
    { startDate: '2026-07-20', endDate: '2026-08-18', australianSupplierCodes: [], chinaSupplierCodes: [] },
    { mode: 'allFiltered', includedProductCodes: [], excludedProductCodes: [] },
    { mode: 'currentProduct', productCode: 'P1' },
  )
  assertEqual(daily.statisticStatus, 'Fresh', '每日响应统计状态应保留')
  assertDeepEqual(daily.data, [
    { date: '2026-08-17', quantity: -2, salesAmount: -10, averageUnitPrice: null },
    { date: '2026-08-18', quantity: 4, salesAmount: 18, averageUnitPrice: 4.5 },
    { date: '2026-08-19', quantity: 6, salesAmount: 24, averageUnitPrice: 4 },
  ], '每日数据应归一化 Pascal/camel、date-only/ISO 日期，并保留 null 均价')

  mockStatus = 'Stale'
  const staleDaily = await queryProductSalesDaily(
    { startDate: '2026-07-20', endDate: '2026-08-18', australianSupplierCodes: [], chinaSupplierCodes: [] },
    { mode: 'allFiltered', includedProductCodes: [], excludedProductCodes: [] },
    { mode: 'currentProduct', productCode: 'P1' },
  )
  assertDeepEqual(staleDaily.data, [], '非 Fresh 响应不得向页面暴露部分指标')

  const preserveNonFreshData = { allowNonFreshData: true }
  const preservedSummary = await queryProductSalesSummary(
    { startDate: '2026-07-20', endDate: '2026-08-18', australianSupplierCodes: [], chinaSupplierCodes: [] },
    { mode: 'included', includedProductCodes: ['P1'], excludedProductCodes: [] },
    { mode: 'currentProduct', productCode: 'P1' },
    { pageNumber: 1, pageSize: 20 },
    undefined,
    preserveNonFreshData,
  )
  assertEqual(preservedSummary.data.items[0]?.metrics.quantity, 12, '允许非 Fresh 数据时 summary 应保留当前指标')

  const preservedDaily = await queryProductSalesDaily(
    { startDate: '2026-07-20', endDate: '2026-08-18', australianSupplierCodes: [], chinaSupplierCodes: [] },
    { mode: 'allFiltered', includedProductCodes: [], excludedProductCodes: [] },
    { mode: 'currentProduct', productCode: 'P1' },
    undefined,
    preserveNonFreshData,
  )
  assertEqual(preservedDaily.data.length, 3, '允许非 Fresh 数据时 product-daily 应保留当前指标')

  const preservedBranches = await queryProductSalesBranches(
    { startDate: '2026-07-20', endDate: '2026-08-18', australianSupplierCodes: [], chinaSupplierCodes: [] },
    { mode: 'allFiltered', includedProductCodes: [], excludedProductCodes: [] },
    { mode: 'currentProduct', productCode: 'P1' },
    undefined,
    preserveNonFreshData,
  )
  assertEqual(preservedBranches.data[0]?.branchCode, 'S1', '允许非 Fresh 数据时 branches 应保留当前指标')

  const preservedBranchDaily = await queryProductSalesBranchDaily(
    { startDate: '2026-07-20', endDate: '2026-08-18', australianSupplierCodes: [], chinaSupplierCodes: [] },
    { mode: 'allFiltered', includedProductCodes: [], excludedProductCodes: [] },
    { mode: 'currentProduct', productCode: 'P1' },
    'S1',
    undefined,
    preserveNonFreshData,
  )
  assertEqual(preservedBranchDaily.data.length, 3, '允许非 Fresh 数据时 branch-daily 应保留当前指标')
  for (const request of captured.slice(-4)) {
    assertEqual(
      new URL(request.url, 'http://localhost').searchParams.get('allowNonFreshData'),
      'true',
      '允许非 Fresh 数据时应发送 allowNonFreshData=true',
    )
  }

  const freshFilter = {
    startDate: '2026-07-20',
    endDate: '2026-08-18',
    australianSupplierCodes: [],
    chinaSupplierCodes: [],
  }
  const allFilteredSelection: ProductSalesAnalysisSelection = {
    mode: 'allFiltered',
    includedProductCodes: [],
    excludedProductCodes: [],
  }
  const selectedProductsScope: ProductSalesAnalysisScope = { mode: 'selectedProducts' }
  const currentProductScope: ProductSalesAnalysisScope = {
    mode: 'currentProduct',
    productCode: 'P1',
  }

  const endpointCases = [
    {
      name: 'options',
      call: () => getProductSalesAnalysisOptions(freshFilter),
      emptyData: { australianSuppliers: [], chinaSuppliers: [] },
    },
    {
      name: 'candidates',
      call: () => queryProductSalesCandidates(
        freshFilter,
        allFilteredSelection,
        { pageNumber: 1, pageSize: 20 },
      ),
      emptyData: { items: [], total: 0, pageNumber: 1, pageSize: 20 },
    },
    {
      name: 'summary',
      call: () => queryProductSalesSummary(
        freshFilter,
        allFilteredSelection,
        selectedProductsScope,
        { pageNumber: 1, pageSize: 20 },
      ),
      emptyData: { items: [], total: 0, pageNumber: 1, pageSize: 20 },
    },
    {
      name: 'product-daily',
      call: () => queryProductSalesDaily(freshFilter, allFilteredSelection, currentProductScope),
      emptyData: [],
    },
    {
      name: 'branches',
      call: () => queryProductSalesBranches(freshFilter, allFilteredSelection, selectedProductsScope),
      emptyData: [],
    },
    {
      name: 'branch-daily',
      call: () => queryProductSalesBranchDaily(
        freshFilter,
        allFilteredSelection,
        currentProductScope,
        'S1',
      ),
      emptyData: [],
    },
  ]

  for (const status of [undefined, '   ', 'Unknown', 'Pending', 'Stale', 'Failed']) {
    mockStatus = status
    for (const testCase of endpointCases) {
      const statusLabel = status === undefined ? '缺失' : JSON.stringify(status)
      const envelope = await testCase.call()
      assertDeepEqual(
        envelope.data,
        testCase.emptyData,
        `${testCase.name} 在统计状态${statusLabel}时必须清空为 emptyData`,
      )
      assertEqual(
        envelope.statisticStatus,
        'Pending',
        `${testCase.name} 在统计状态${statusLabel}时对外 statisticStatus 必须归一化为 Pending`,
      )
    }
  }

  mockStatus = undefined
  const missingMessageOptions = await getProductSalesAnalysisOptions(freshFilter)
  assertEqual(
    missingMessageOptions.statisticMessage,
    undefined,
    '缺失统计说明时不应新增 statisticMessage',
  )

  mockStatus = 'Stale'
  mockStatisticMessage = '统计暂不可用'
  const staleOptions = await getProductSalesAnalysisOptions(freshFilter)
  assertDeepEqual(
    staleOptions.data,
    { australianSuppliers: [], chinaSuppliers: [] },
    'Stale options 必须清空为 emptyData',
  )
  assertEqual(staleOptions.statisticStatus, 'Pending', 'Stale options 状态必须归一化为 Pending')
  assertEqual(staleOptions.statisticMessage, '统计暂不可用', '清空数据时必须保留统计说明')
  assertEqual(staleOptions.statisticUpdatedAt, '2026-08-18T01:00:00Z', '清空数据时必须保留更新时间')
  assertEqual(staleOptions.cacheVersion, 'v1', '清空数据时必须保留缓存水位')

  for (const status of ['Fresh', 'fresh']) {
    mockStatus = status
    for (const testCase of endpointCases) {
      const envelope = await testCase.call()
      assertEqual(
        envelope.statisticStatus?.toLowerCase(),
        'fresh',
        `${testCase.name} 在状态 ${status} 时对外 statisticStatus 应保留`,
      )
      const isEmpty = JSON.stringify(envelope.data) === JSON.stringify(testCase.emptyData)
      assertEqual(
        isEmpty,
        false,
        `${testCase.name} 在状态 ${status} 时必须保留数据`,
      )
    }
  }

  globalThis.fetch = (async () => jsonResponse({ success: false, message: '统计未就绪' })) as typeof fetch
  await assertRejects(
    () => queryProductSalesBranches(
      { startDate: '2026-07-20', endDate: '2026-08-18', australianSupplierCodes: [], chinaSupplierCodes: [] },
      { mode: 'allFiltered', includedProductCodes: [], excludedProductCodes: [] },
      { mode: 'selectedProducts' },
    ),
    '统计未就绪',
    '业务失败即使 HTTP 200 也必须抛出',
  )

  console.log('productSalesAnalysisService.test: ok')
} finally {
  globalThis.fetch = originalFetch
}
