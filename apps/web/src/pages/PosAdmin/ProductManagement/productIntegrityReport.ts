import type {
  ProductIntegrityCheckResultDto,
  ProductIntegrityFixResultDto,
  TableIntegrityReport,
} from '../../../types/productIntegrity'

export interface ProductIntegrityIssueRow {
  key: string
  scope: string
  tableName: string
  issueType: '孤立记录' | '缺失记录'
  count: number
  sampleProductCodes: string[]
}

export interface ProductIntegritySummary {
  storeCount: number
  totalChecked: number
  issueCount: number
  durationSeconds: number
  issueRows: ProductIntegrityIssueRow[]
}

export interface ProductIntegrityFixSummary {
  deletedCount: number
  addedCount: number
  errorCount: number
  errors: string[]
}

function appendIssueRows(
  rows: ProductIntegrityIssueRow[],
  scope: string,
  tableReport: TableIntegrityReport | undefined | null,
) {
  if (!tableReport) return

  // 后端返回的是按分店、按表聚合的报告；这里统一转换成页面表格行，避免再读取旧版 issues 字段。
  if (tableReport.orphanedCount > 0) {
    rows.push({
      key: `${scope}-${tableReport.tableName}-orphaned`,
      scope,
      tableName: tableReport.tableName,
      issueType: '孤立记录',
      count: tableReport.orphanedCount,
      sampleProductCodes: tableReport.orphanedProductCodes ?? [],
    })
  }

  if (tableReport.missingCount > 0) {
    rows.push({
      key: `${scope}-${tableReport.tableName}-missing`,
      scope,
      tableName: tableReport.tableName,
      issueType: '缺失记录',
      count: tableReport.missingCount,
      sampleProductCodes: tableReport.missingProductCodes ?? [],
    })
  }
}

function getIssueCount(tableReport: TableIntegrityReport | undefined | null) {
  if (!tableReport) return 0
  return (tableReport.orphanedCount ?? 0) + (tableReport.missingCount ?? 0)
}

function getStoreScope(storeCode: string, storeName?: string) {
  return storeName && storeName !== storeCode ? `${storeName} (${storeCode})` : storeCode
}

export function buildProductIntegritySummary(
  result: ProductIntegrityCheckResultDto | null | undefined,
): ProductIntegritySummary {
  if (!result) {
    return {
      storeCount: 0,
      totalChecked: 0,
      issueCount: 0,
      durationSeconds: 0,
      issueRows: [],
    }
  }

  const issueRows: ProductIntegrityIssueRow[] = []
  appendIssueRows(issueRows, '总部', result.productSetCodeReport)

  const productSetCodeTotal = result.productSetCodeReport?.totalChecked ?? 0
  const storeReports = result.storeReports ?? []
  const storeTotal = storeReports.reduce(
    (sum, store) => sum + (store.tableReports ?? []).reduce(
      (tableSum, table) => tableSum + (table.totalChecked ?? 0),
      0,
    ),
    0,
  )
  const productSetCodeIssues = getIssueCount(result.productSetCodeReport)

  let storeIssueCount = 0
  storeReports.forEach((store) => {
    const scope = getStoreScope(store.storeCode, store.storeName)
    const tableReports = store.tableReports ?? []
    tableReports.forEach((tableReport) => {
      storeIssueCount += getIssueCount(tableReport)
      appendIssueRows(issueRows, scope, tableReport)
    })
  })

  return {
    storeCount: storeReports.length,
    totalChecked: productSetCodeTotal + storeTotal,
    issueCount: productSetCodeIssues + storeIssueCount,
    durationSeconds: result.durationSeconds ?? 0,
    issueRows,
  }
}

export function buildProductIntegrityFixSummary(
  result: ProductIntegrityFixResultDto,
): ProductIntegrityFixSummary {
  const reports = result.reports ?? []
  return reports.reduce<ProductIntegrityFixSummary>(
    (summary, report) => ({
      deletedCount: summary.deletedCount + (report.deletedCount ?? 0),
      addedCount: summary.addedCount + (report.addedCount ?? 0),
      errorCount: summary.errorCount + (report.errorCount ?? 0),
      errors: [...summary.errors, ...(report.errors ?? [])],
    }),
    {
      deletedCount: 0,
      addedCount: 0,
      errorCount: 0,
      errors: [],
    },
  )
}
