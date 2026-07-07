import type {
  DomesticProductItem,
  DomesticProductSetItem,
  UpdateDomesticProductPayload,
} from '../../../types/domesticProduct'

export type PasteableSetItemField = 'productName' | 'domesticPrice' | 'oemPrice'

const PRICE_FIELDS = new Set<PasteableSetItemField>(['domesticPrice', 'oemPrice'])

function roundPrice(value: number) {
  return Math.round((value + Number.EPSILON) * 100) / 100
}

export function parseSetItemPrice(value: string) {
  const normalized = value
    .trim()
    .replace(/,/g, '')
    .replace(/[^\d.-]/g, '')

  if (!normalized || normalized === '-' || normalized === '.' || normalized === '-.') {
    return undefined
  }

  const parsed = Number(normalized)
  if (!Number.isFinite(parsed) || parsed < 0) {
    return undefined
  }

  return roundPrice(parsed)
}

function parseClipboardColumn(clipboardText: string) {
  const values = clipboardText
    .replace(/\r\n/g, '\n')
    .replace(/\r/g, '\n')
    .split('\n')
    .map((line) => line.split('\t')[0]?.trim() ?? '')

  while (values.length && values[values.length - 1] === '') {
    values.pop()
  }

  return values
}

export function createEmptySetItem(id: string): DomesticProductSetItem {
  // 自动新增的套装子项只带临时 id，货号和条码保持为空，等待用户或后端补齐。
  return { id }
}

export function applySetItemColumnPaste({
  items,
  startRowId,
  field,
  clipboardText,
  createId,
}: {
  items: DomesticProductSetItem[]
  startRowId?: string
  field: PasteableSetItemField
  clipboardText: string
  createId: (rowIndex: number) => string
}) {
  const values = parseClipboardColumn(clipboardText)
  const startIndex = startRowId ? items.findIndex((item) => item.id === startRowId) : 0

  if (startIndex < 0 || !values.length) {
    return { items, appliedCount: 0, skippedCount: 0 }
  }

  const nextItems = [...items]
  let appliedCount = 0
  let skippedCount = 0

  values.forEach((rawValue, offset) => {
    const targetIndex = startIndex + offset
    while (nextItems.length <= targetIndex) {
      nextItems.push(createEmptySetItem(createId(nextItems.length)))
    }

    if (!rawValue) {
      return
    }

    if (PRICE_FIELDS.has(field)) {
      const price = parseSetItemPrice(rawValue)
      if (price === undefined) {
        skippedCount += 1
        return
      }

      nextItems[targetIndex] = { ...nextItems[targetIndex], [field]: price }
      appliedCount += 1
      return
    }

    nextItems[targetIndex] = { ...nextItems[targetIndex], [field]: rawValue }
    appliedCount += 1
  })

  return { items: nextItems, appliedCount, skippedCount }
}

export function calculateSetItemPriceTotals(items: DomesticProductSetItem[]) {
  let hasDomesticPrice = false
  let domesticPriceTotal = 0
  let hasOemPrice = false
  let oemPriceTotal = 0

  items.forEach((item) => {
    if (item.domesticPrice !== undefined && item.domesticPrice !== null) {
      hasDomesticPrice = true
      domesticPriceTotal += item.domesticPrice
    }

    if (item.oemPrice !== undefined && item.oemPrice !== null) {
      hasOemPrice = true
      oemPriceTotal += item.oemPrice
    }
  })

  return {
    hasDomesticPrice,
    domesticPriceTotal: hasDomesticPrice ? roundPrice(domesticPriceTotal) : undefined,
    hasOemPrice,
    oemPriceTotal: hasOemPrice ? roundPrice(oemPriceTotal) : undefined,
  }
}

export function buildSetProductPriceSyncPayload(
  product: DomesticProductItem,
  totals: ReturnType<typeof calculateSetItemPriceTotals>,
): UpdateDomesticProductPayload | undefined {
  if (!totals.hasDomesticPrice && !totals.hasOemPrice) {
    return undefined
  }

  return {
    productName: product.name,
    englishProductName: product.nameEn,
    barcode: product.barcode,
    productSpecification: product.specs,
    productType: product.productType,
    // 主码价格更新仍复用现有接口；未同步的价格带回当前值，避免国内价-only/零售价-only 保存时被覆盖为空。
    domesticPrice: totals.hasDomesticPrice ? totals.domesticPriceTotal : product.domesticPrice,
    oemPrice: totals.hasOemPrice ? totals.oemPriceTotal : product.labelPrice,
    importPrice: product.importPrice,
    packingQuantity: product.packingQty,
    unitVolume: product.volume,
    middlePackQuantity: product.middlePackQty,
    packingSize: product.packingSize,
    material: product.material,
    remarks: product.remark,
    productImage: product.productImage,
    isActive: product.isActive,
  }
}
