import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import {
  getProductStoreDailyStatisticStates,
  getProductStoreDailyStatisticSummary,
  recalculateDailyStatisticsAlignment,
  recalculateProductStoreDailyRange,
  recalculateRecentProductStoreDaily,
} from './salesStatisticsManagementService'

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) {
    throw new Error(message)
  }
}

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`)
  }
}

const originalFetch = globalThis.fetch
const calls: Array<{ url: string; init?: RequestInit }> = []

globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
  const url = String(input)
  calls.push({ url, init })

  if (url.includes('/states')) {
    return new Response(
      JSON.stringify({
        success: true,
        data: [
          {
            StatisticType: 'ProductStoreDaily',
            Date: '2026-06-08',
            Status: 'Running',
            LastSourceUploadTime: '2026-06-08T10:01:00',
            SourceTimeZone: 'POSM',
            LastAggregatedAtUtc: '2026-06-08T00:02:00Z',
            LastCheckedAtUtc: '2026-06-08T00:03:00Z',
            ErrorMessage: '',
            JobId: 'job-running',
            StartedAtUtc: '2026-06-08T00:04:00Z',
          },
        ],
      }),
      { status: 200, headers: { 'content-type': 'application/json' } },
    )
  }

  if (url.includes('/2026-06-09/summary')) {
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          date: '2026-06-09',
          status: 'Pending',
          recordCount: 0,
          totalQuantity: 0,
          totalAmount: 0,
          grossProfit: null,
          reconciliationStatus: 'Pending',
        },
      }),
      { status: 200, headers: { 'content-type': 'application/json' } },
    )
  }

  if (url.includes('/2026-06-10/summary')) {
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          Date: '2026-06-10',
          Status: 'Fresh',
          RecordCount: 8,
          TotalQuantity: 21,
          TotalAmount: 123.45,
          GrossProfit: 9.87,
          ReconciliationStatus: 'Passed',
          SalesReconciliationStatus: 'Failed',
          ProductTotalAmount: 120.45,
          StoreTotalAmount: 119.45,
          AmountDifference: 1,
          ProductTotalQuantity: 22,
          StoreTotalQuantity: 20,
          QuantityDifference: 2,
          UnmatchedSupplierAmount: 15.5,
          UnmatchedSupplierQuantity: 3,
          UnmatchedSupplierProductCount: 2,
        },
      }),
      { status: 200, headers: { 'content-type': 'application/json' } },
    )
  }

  if (url.includes('/summary')) {
    return new Response(
      JSON.stringify({
        success: true,
        data: {
          date: '2026-06-08',
          status: 'Fresh',
          recordCount: 12,
          totalQuantity: 34,
          totalAmount: 56.7,
          grossProfit: 8.9,
          reconciliationStatus: 'Passed',
          salesReconciliationStatus: 'Passed',
          productTotalAmount: 50.7,
          storeTotalAmount: 49.7,
          amountDifference: 1,
          productTotalQuantity: 34,
          storeTotalQuantity: 33,
          quantityDifference: 1,
          unmatchedSupplierAmount: 6.5,
          unmatchedSupplierQuantity: 2,
          unmatchedSupplierProductCount: 1,
        },
      }),
      { status: 200, headers: { 'content-type': 'application/json' } },
    )
  }

  if (url.includes('/alignment/recalculate')) {
    return new Response(JSON.stringify({
      success: true,
      message: '已提交 2 天异常统计后台补算，可在任务日志查看执行结果',
      jobId: 'alignment-job-1',
      processedDates: [],
      skippedDates: [],
      failedDates: [],
    }), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    })
  }

  return new Response(JSON.stringify({
    success: true,
    message: '已提交 1 天商品统计重算，跳过 1 天执行中的任务',
    jobId: 'job-submit',
    status: 'Queued',
    submittedDates: ['2026-06-01'],
    skippedDates: ['2026-06-02'],
  }), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  })
}) as typeof fetch

try {
  const states = await getProductStoreDailyStatisticStates({
    statisticType: 'ProductStoreDaily',
    startDate: '2026-06-01',
    endDate: '2026-06-08',
    status: 'Fresh',
  })
  const stateUrl = new URL(calls[0].url, 'http://localhost')

  assertEqual(stateUrl.pathname, '/api/StatisticsJobTrigger/product-store-daily/states', '统计状态接口路径应正确')
  assertEqual(stateUrl.searchParams.get('statisticType'), 'ProductStoreDaily', '应传递统计类型')
  assertEqual(stateUrl.searchParams.get('startDate'), '2026-06-01', '应传递开始日期')
  assertEqual(stateUrl.searchParams.get('endDate'), '2026-06-08', '应传递结束日期')
  assertEqual(stateUrl.searchParams.get('status'), 'Fresh', '应传递状态筛选')
  assertEqual(states[0]?.date, '2026-06-08', '应兼容 PascalCase 日期字段')
  assertEqual(states[0]?.status, 'Running', '应兼容 PascalCase 状态字段')
  assertEqual(states[0]?.lastSourceUploadTime, '2026-06-08T10:01:00', '应兼容 POSM 水位字段')
  assertEqual(states[0]?.jobId, 'job-running', '应兼容任务 ID 字段')
  assertEqual(states[0]?.startedAtUtc, '2026-06-08T00:04:00Z', '应兼容任务开始时间字段')

  const summary = await getProductStoreDailyStatisticSummary('2026-06-08')
  assertEqual(summary.recordCount, 12, '应解包商品统计记录数')
  assertEqual(summary.totalQuantity, 34, '应解包销量合计')
  assertEqual(summary.totalAmount, 56.7, '应解包销售额合计')
  assertEqual(summary.grossProfit, 8.9, '应解包毛利合计')
  assertEqual(summary.reconciliationStatus, 'Passed', '应解包对账状态')
  assertEqual(summary.salesReconciliationStatus, 'Passed', '应兼容 camelCase 销售对账状态字段')
  assertEqual(summary.productTotalAmount, 50.7, '应兼容 camelCase 商品销售额字段')
  assertEqual(summary.storeTotalAmount, 49.7, '应兼容 camelCase 分店销售额字段')
  assertEqual(summary.amountDifference, 1, '应兼容 camelCase 销售额差异字段')
  assertEqual(summary.productTotalQuantity, 34, '应兼容 camelCase 商品销量字段')
  assertEqual(summary.storeTotalQuantity, 33, '应兼容 camelCase 分店销量字段')
  assertEqual(summary.quantityDifference, 1, '应兼容 camelCase 销量差异字段')
  assertEqual(summary.unmatchedSupplierAmount, 6.5, '应兼容 camelCase 未匹配供应商销售额字段')
  assertEqual(summary.unmatchedSupplierQuantity, 2, '应兼容 camelCase 未匹配供应商销量字段')
  assertEqual(summary.unmatchedSupplierProductCount, 1, '应兼容 camelCase 未匹配供应商商品数字段')

  const pendingSummary = await getProductStoreDailyStatisticSummary('2026-06-09')
  assertEqual(pendingSummary.grossProfit, null, '毛利为空时应保留 null 而不是归一成 0')

  const pascalSummary = await getProductStoreDailyStatisticSummary('2026-06-10')
  assertEqual(pascalSummary.salesReconciliationStatus, 'Failed', '应兼容 PascalCase 销售对账状态字段')
  assertEqual(pascalSummary.productTotalAmount, 120.45, '应兼容 PascalCase 商品销售额字段')
  assertEqual(pascalSummary.storeTotalAmount, 119.45, '应兼容 PascalCase 分店销售额字段')
  assertEqual(pascalSummary.amountDifference, 1, '应兼容 PascalCase 销售额差异字段')
  assertEqual(pascalSummary.productTotalQuantity, 22, '应兼容 PascalCase 商品销量字段')
  assertEqual(pascalSummary.storeTotalQuantity, 20, '应兼容 PascalCase 分店销量字段')
  assertEqual(pascalSummary.quantityDifference, 2, '应兼容 PascalCase 销量差异字段')
  assertEqual(pascalSummary.unmatchedSupplierAmount, 15.5, '应兼容 PascalCase 未匹配供应商销售额字段')
  assertEqual(pascalSummary.unmatchedSupplierQuantity, 3, '应兼容 PascalCase 未匹配供应商销量字段')
  assertEqual(pascalSummary.unmatchedSupplierProductCount, 2, '应兼容 PascalCase 未匹配供应商商品数字段')

  const recentResult = await recalculateRecentProductStoreDaily(7)
  const recentCall = calls[calls.length - 1]
  assert(recentCall, '应记录最近 7 天重算请求')
  assertEqual(new URL(recentCall.url, 'http://localhost').pathname, '/api/StatisticsJobTrigger/recent-product-store-daily', '最近 7 天重算接口路径应正确')
  assertEqual(recentCall.init?.method, 'POST', '最近 7 天重算应使用 POST')
  assertEqual((JSON.parse(String(recentCall.init?.body)) as { days: number }).days, 7, '最近 7 天重算应传递 days')
  assertEqual(recentResult.status, 'Queued', '最近 7 天重算应解包提交状态')
  assertEqual(recentResult.submittedDates?.[0], '2026-06-01', '最近 7 天重算应解包已提交日期')
  assertEqual(recentResult.skippedDates?.[0], '2026-06-02', '最近 7 天重算应解包跳过日期')

  await recalculateProductStoreDailyRange('2026-06-01', '2026-06-08', 3)
  const rangeCall = calls[calls.length - 1]
  assert(rangeCall, '应记录日期范围重算请求')
  assertEqual(new URL(rangeCall.url, 'http://localhost').pathname, '/api/StatisticsJobTrigger/batch-product-store-daily', '日期范围重算接口路径应正确')
  assertEqual(rangeCall.init?.method, 'POST', '日期范围重算应使用 POST')
  const rangeBody = JSON.parse(String(rangeCall.init?.body)) as { startDate: string; endDate: string; maxConcurrency: number }
  assertEqual(rangeBody.startDate, '2026-06-01', '日期范围重算应传递开始日期')
  assertEqual(rangeBody.endDate, '2026-06-08', '日期范围重算应传递结束日期')
  assertEqual(rangeBody.maxConcurrency, 3, '日期范围重算应传递最大并发')

  const alignmentResult = await recalculateDailyStatisticsAlignment({
    dates: ['2026-06-01', '2026-06-02'],
    maxConcurrency: 3,
  })
  const alignmentCall = calls[calls.length - 1]
  assert(alignmentCall, '应记录数据对齐后台补算请求')
  assertEqual(new URL(alignmentCall.url, 'http://localhost').pathname, '/api/StatisticsJobTrigger/alignment/recalculate', '数据对齐后台补算接口路径应正确')
  assertEqual(alignmentCall.init?.method, 'POST', '数据对齐后台补算应使用 POST')
  const alignmentBody = JSON.parse(String(alignmentCall.init?.body)) as { dates: string[]; maxConcurrency: number }
  assertEqual(alignmentBody.dates[0], '2026-06-01', '数据对齐后台补算应传递第一个日期')
  assertEqual(alignmentBody.dates[1], '2026-06-02', '数据对齐后台补算应传递第二个日期')
  assertEqual(alignmentBody.maxConcurrency, 3, '数据对齐后台补算应传递最大并发')
  assertEqual(alignmentResult.success, true, '数据对齐后台补算应解包提交成功状态')
  assertEqual(alignmentResult.jobId, 'alignment-job-1', '数据对齐后台补算应解包后台任务 ID')
  assertEqual(alignmentResult.message, '已提交 2 天异常统计后台补算，可在任务日志查看执行结果', '数据对齐后台补算应解包后台提交提示')
  assertEqual(alignmentResult.processedDates.length, 0, '后台提交响应不应伪造已完成日期')
  assertEqual(alignmentResult.failedDates.length, 0, '后台提交响应不应伪造失败日期')

  const scheduledStatisticsSource = readFileSync(
    join(process.cwd(), 'src/pages/System/ScheduledStatistics/index.tsx'),
    'utf8',
  )
  assert(scheduledStatisticsSource.includes('后台补算异常日期'), '页面按钮应改为后台补算语义')
  assert(scheduledStatisticsSource.includes('后台补算任务已提交'), '提交成功提示应改为后台提交语义')
  assert(!scheduledStatisticsSource.includes('补算任务已完成'), '页面不应再提示同步补算已完成')
  assert(!scheduledStatisticsSource.includes('补算完成但存在失败日期'), '页面失败提示不应再使用同步完成语义')

  console.log('salesStatisticsManagementService.test: ok')
} finally {
  globalThis.fetch = originalFetch
}
