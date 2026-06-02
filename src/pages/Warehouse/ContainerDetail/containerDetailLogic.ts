import type { ContainerDetail, ContainerMain, UpdateContainerDetailRequest } from '../../../types/container'
import type { PushProductsToHqResult } from '../../../types/posProduct'

export type ContainerDetailTagFilter = 'all' | 'new' | 'existing' | 'noOemPrice' | 'abnormalImport'

export type ContainerDetailTagStats = Record<ContainerDetailTagFilter, number>

export function getContainerDetailProductName(row: ContainerDetail) {
  return row.商品名称 ?? row.商品信息?.商品名称
}

export function getContainerDetailEnglishName(row: ContainerDetail) {
  return row.英文名称 ?? row.商品信息?.英文名称
}

export function withContainerDetailEnglishName(row: ContainerDetail, englishName?: string): ContainerDetail {
  return {
    ...row,
    英文名称: englishName,
    商品信息: row.商品信息 ? { ...row.商品信息, 英文名称: englishName } : row.商品信息,
  }
}

export function mergeContainerDetailPatch(row: ContainerDetail, patch: Partial<ContainerDetail>): ContainerDetail {
  const next = { ...row, ...patch }

  if ('英文名称' in patch) {
    return withContainerDetailEnglishName(next, patch.英文名称)
  }

  return next
}

export function matchesContainerDetailTagFilter(row: ContainerDetail, filter: ContainerDetailTagFilter) {
  if (filter === 'new') return Boolean(row.是否新商品)
  if (filter === 'existing') return !row.是否新商品
  if (filter === 'noOemPrice') return Boolean(row.是否新商品) && (!row.贴牌价格 || row.贴牌价格 <= 0)
  if (filter === 'abnormalImport') return !row.进口价格 || row.进口价格 <= 0
  return true
}

export function buildContainerDetailTagStats(rows: ContainerDetail[]): ContainerDetailTagStats {
  const stats: ContainerDetailTagStats = {
    all: rows.length,
    new: 0,
    existing: 0,
    noOemPrice: 0,
    abnormalImport: 0,
  }

  rows.forEach((row) => {
    // 统计栏和标签过滤共用同一判断，避免数量与点击后的列表不一致。
    if (matchesContainerDetailTagFilter(row, 'new')) stats.new += 1
    if (matchesContainerDetailTagFilter(row, 'existing')) stats.existing += 1
    if (matchesContainerDetailTagFilter(row, 'noOemPrice')) stats.noOemPrice += 1
    if (matchesContainerDetailTagFilter(row, 'abnormalImport')) stats.abnormalImport += 1
  })

  return stats
}

export function buildContainerDetailTranslationUpdates(
  rows: ContainerDetail[],
  translations: Record<string, string>,
): UpdateContainerDetailRequest[] {
  const updates: UpdateContainerDetailRequest[] = []

  rows.forEach((row) => {
    const name = getContainerDetailProductName(row)
    const englishName = name ? translations[name] : undefined

    if (row.hguid && englishName) {
      updates.push({ hguid: row.hguid, 英文名称: englishName })
    }
  })

  return updates
}

export function applyContainerDetailEnglishNameUpdates(
  rows: ContainerDetail[],
  updates: Pick<UpdateContainerDetailRequest, 'hguid' | '英文名称'>[],
): ContainerDetail[] {
  const updateMap = new Map(updates.map((item) => [item.hguid, item.英文名称]))

  return rows.map((row) => (
    updateMap.has(row.hguid)
      ? withContainerDetailEnglishName(row, updateMap.get(row.hguid))
      : row
  ))
}

function roundToDigits(value: number, digits: number) {
  const base = 10 ** digits
  return Math.round((value + Number.EPSILON) * base) / base
}

function isPlainRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

export function normalizeContainerDetailPushToHqPayload(raw: unknown, fallbackMessage?: string): PushProductsToHqResult | null {
  if (!isPlainRecord(raw)) return null

  const errors = Array.isArray(raw.errors)
    ? raw.errors.map(String)
    : []
  const successCount = Number(raw.successCount ?? raw.productsAdded ?? 0) +
    Number(raw.successCount === undefined ? raw.productsUpdated ?? 0 : 0)
  const failedCount = Number(raw.failedCount ?? raw.errorCount ?? errors.length)
  const affectedRowCount =
    Number(raw.affectedRowCount ?? 0) ||
    Number(raw.productsAdded ?? 0) +
      Number(raw.productsUpdated ?? 0) +
      Number(raw.storeRetailPricesCreated ?? 0) +
      Number(raw.storeRetailPricesUpdated ?? 0) +
      Number(raw.productSetCodesCreated ?? raw.productSetCodesAdded ?? 0) +
      Number(raw.productSetCodesUpdated ?? 0) +
      Number(raw.storeMultiCodesCreated ?? 0) +
      Number(raw.storeMultiCodesUpdated ?? 0)

  return {
    ...(raw as Partial<PushProductsToHqResult>),
    successCount,
    failedCount,
    totalCount: Number(raw.totalCount ?? successCount + failedCount),
    affectedRowCount,
    errors,
    message: typeof raw.message === 'string' ? raw.message : fallbackMessage,
  }
}

export function extractPushToHqErrorResult(error: unknown): PushProductsToHqResult | null {
  if (!isPlainRecord(error) || !('payload' in error)) return null
  const payload = error.payload
  if (!isPlainRecord(payload)) return null
  const fallbackMessage = typeof payload.message === 'string'
    ? payload.message
    : error instanceof Error
      ? error.message
      : undefined
  return (
    normalizeContainerDetailPushToHqPayload(payload.data, fallbackMessage) ??
    normalizeContainerDetailPushToHqPayload(payload.details, fallbackMessage) ??
    normalizeContainerDetailPushToHqPayload(payload, fallbackMessage)
  )
}

export function calculateContainerDetailTransportCost(row: ContainerDetail, container?: Pick<ContainerMain, '运费' | '总体积'> | null) {
  const freight = container?.运费
  const totalVolume = container?.总体积
  const containerQuantity = row.装柜数量
  const unitVolume = row.单件体积 ?? row.商品信息?.单件体积
  const detailVolume = row.合计装柜体积 ?? (
    row.装柜件数 != null && unitVolume != null
      ? row.装柜件数 * unitVolume
      : undefined
  )

  if (
    freight == null ||
    freight < 0 ||
    !totalVolume ||
    totalVolume <= 0 ||
    containerQuantity == null ||
    containerQuantity <= 0 ||
    detailVolume == null ||
    detailVolume < 0
  ) {
    return row.运输成本
  }

  return roundToDigits((freight * detailVolume) / containerQuantity / totalVolume, 2)
}

export function calculateContainerDetailImportPrice(
  row: ContainerDetail,
  container: Pick<ContainerMain, '汇率'> | null | undefined,
  floatRate: number,
  transportCost: number | undefined,
) {
  const exchangeRate = container?.汇率

  if (!exchangeRate || exchangeRate <= 0 || row.国内价格 == null) {
    return row.进口价格
  }

  return roundToDigits(((row.国内价格 / exchangeRate + (transportCost ?? 0)) * floatRate * 10) / 11, 2)
}

export function buildContainerDetailFloatRateUpdates(
  rows: ContainerDetail[],
  container: Pick<ContainerMain, '汇率' | '运费' | '总体积'> | null | undefined,
  floatRate?: number,
): UpdateContainerDetailRequest[] {
  return rows
    .filter((row) => row.hguid)
    .map((row): UpdateContainerDetailRequest | null => {
      const nextFloatRate = floatRate ?? row.调整浮率 ?? 1
      const transportCost = calculateContainerDetailTransportCost(row, container)
      const importPrice = calculateContainerDetailImportPrice(row, container, nextFloatRate, transportCost)
      const hasChange =
        row.调整浮率 !== nextFloatRate ||
        row.运输成本 !== transportCost ||
        row.进口价格 !== importPrice

      if (!hasChange) {
        return null
      }

      return {
        hguid: row.hguid,
        调整浮率: nextFloatRate,
        运输成本: transportCost,
        进口价格: importPrice,
      }
    })
    .filter((update): update is UpdateContainerDetailRequest => update !== null)
}

export interface ContainerDetailHqPushSelection {
  productCodes: string[]
  skippedNewProductCount: number
  missingProductCodeCount: number
}

export function buildContainerDetailHqPushSelection(rows: ContainerDetail[]): ContainerDetailHqPushSelection {
  const productCodes: string[] = []
  let skippedNewProductCount = 0
  let missingProductCodeCount = 0

  rows.forEach((row) => {
    // 本地没有的新商品不能写 HQ，避免误创建还未补齐资料的商品。
    if (row.是否新商品 === true) {
      skippedNewProductCount += 1
      return
    }

    const productCode = row.商品编码?.trim() || row.商品信息?.商品编码?.trim()
    if (!productCode) {
      missingProductCodeCount += 1
      return
    }

    if (!productCodes.includes(productCode)) {
      productCodes.push(productCode)
    }
  })

  return {
    productCodes,
    skippedNewProductCount,
    missingProductCodeCount,
  }
}
