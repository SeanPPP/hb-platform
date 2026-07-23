import type { ProductImportItem, Statistics, DuplicateGroup, MergeDuplicateProductsResult } from './types'
import type { AssignContainerItem } from '../../../services/containerService'

interface AssignProductsFailedItem {
  productCode?: string
  error?: string
}

interface AssignProductsResponseLike {
  success: boolean
  message?: string
  data?: {
    created?: number
    updated?: number
    failed?: AssignProductsFailedItem[]
  }
}

export interface InvalidAssignContainerItem {
  hbProductNo?: string
  productCode?: string
  fields: string[]
  reasons: string[]
}

export interface AssignProductsResultSummary {
  status: 'success' | 'partial' | 'failed' | 'apiError'
  success: boolean
  message?: string
  created: number
  updated: number
  succeeded: number
  failedCount: number
  failed: Array<{
    hbProductNo?: string
    productCode?: string
    reason: string
  }>
}

export interface HbwebProductNameUpdate {
  SupplierCode: string
  ItemNumber: string
  ProductName: string
}

export interface BuildHbwebProductNameUpdatesResult {
  products: HbwebProductNameUpdate[]
  missingItemNumbers: string[]
  missingProductNames: string[]
  conflictItemNumbers: string[]
}

interface ProductNameSyncResultLike {
  success: boolean
  updatedCount: number
  unchangedCount: number
  missingItemNumbers: string[]
  errors: string[]
}

interface HbwebProductNameSyncResponseLike {
  success: boolean
  message?: string
  errorCode?: string
  data?: {
    updatedCount: number
    unchangedCount: number
    missingItemNumbers: string[]
    errors: string[]
    hqSyncResult?: ProductNameSyncResultLike
  }
}

export interface ProductNameSyncSummary {
  status: 'success' | 'hqPartialFailure' | 'failure'
  hbweb: { updatedCount: number; unchangedCount: number; missingCount: number; skippedCount: number }
  hq?: { success: boolean; updatedCount: number; unchangedCount: number; missingCount: number }
  hqWarningCount: number
}

export interface ProductNameSyncNotificationDecision {
  level: 'success' | 'warning'
  includesHq: boolean
  partial: boolean
  hbweb: { updatedCount: number; unchangedCount: number; missingCount: number; warningCount: number }
  hq?: { updatedCount: number; unchangedCount: number; missingCount: number; warningCount: number }
}

export function getHbwebProductNameSyncConfirmationKeys(syncToHq: boolean): { confirmKey: string; scopeKey: string } {
  return syncToHq
    ? {
        confirmKey: 'productImport.updateHbwebProductNamesWithHqConfirm',
        scopeKey: 'productImport.updateHbwebProductNamesWithHqScope',
      }
    : {
        confirmKey: 'productImport.updateHbwebProductNamesConfirm',
        scopeKey: 'productImport.updateHbwebProductNamesScope',
      }
}

export function summarizeHbwebProductNameSyncResponse(response: HbwebProductNameSyncResponseLike): ProductNameSyncSummary {
  const data = response.data
  const hqResult = data?.hqSyncResult
  const isHqPartialFailure = !response.success
    && response.errorCode === 'HQ_PRODUCT_NAME_SYNC_FAILED'
    && data !== undefined

  return {
    status: isHqPartialFailure ? 'hqPartialFailure' : response.success ? 'success' : 'failure',
    hbweb: {
      updatedCount: data?.updatedCount ?? 0,
      unchangedCount: data?.unchangedCount ?? 0,
      missingCount: data?.missingItemNumbers?.length ?? 0,
      skippedCount: data?.errors?.filter(Boolean).length ?? 0,
    },
    hq: hqResult
      ? {
          success: hqResult.success,
          updatedCount: hqResult.updatedCount ?? 0,
          unchangedCount: hqResult.unchangedCount ?? 0,
          missingCount: hqResult.missingItemNumbers?.length ?? 0,
        }
      : undefined,
    hqWarningCount: hqResult?.errors?.filter(Boolean).length ?? 0,
  }
}

export function buildHbwebProductNameSyncNotificationDecision(summary: ProductNameSyncSummary): ProductNameSyncNotificationDecision {
  const partial = summary.status === 'hqPartialFailure'
  const includesHq = partial || summary.hq !== undefined
  const hq = !partial && summary.hq
    ? {
        updatedCount: summary.hq.updatedCount,
        unchangedCount: summary.hq.unchangedCount,
        missingCount: summary.hq.missingCount,
        warningCount: summary.hqWarningCount,
      }
    : undefined
  const hasWarning = partial
    || summary.hbweb.missingCount > 0
    || summary.hbweb.skippedCount > 0
    || (hq !== undefined && (hq.missingCount > 0 || hq.warningCount > 0 || summary.hq?.success === false))

  return {
    level: hasWarning ? 'warning' : 'success',
    includesHq,
    partial,
    hbweb: {
      updatedCount: summary.hbweb.updatedCount,
      unchangedCount: summary.hbweb.unchangedCount,
      missingCount: summary.hbweb.missingCount,
      warningCount: summary.hbweb.skippedCount,
    },
    hq,
  }
}

export type AssignContainerValidationItem = AssignContainerItem & {
  domesticPrice?: number
  oemPrice?: number
}

export function generateImageUrl(productCode: string): string {
  if (!productCode) return ''
  return `https://hbimgoss.hbupplier.com/productimg/${productCode}.jpg`
}

export function parseProductImportPasteText(text: string): string[][] {
  const normalizedText = text.replace(/\r\n/g, '\n').replace(/\r/g, '\n')
  const rows: string[][] = []
  let currentRow: string[] = []
  let currentCell = ''
  let inQuotedCell = false

  for (let index = 0; index < normalizedText.length; index += 1) {
    const char = normalizedText[index]

    if (inQuotedCell) {
      if (char === '"') {
        if (normalizedText[index + 1] === '"') {
          currentCell += '"'
          index += 1
        } else {
          inQuotedCell = false
        }
      } else {
        currentCell += char
      }
      continue
    }

    if (char === '"' && currentCell.length === 0) {
      inQuotedCell = true
      continue
    }

    if (char === '\t') {
      currentRow.push(currentCell)
      currentCell = ''
      continue
    }

    if (char === '\n') {
      // Excel 空单元格会表现为连续换行；这里必须保留空行，避免列粘贴后续数据错位。
      currentRow.push(currentCell)
      rows.push(currentRow)
      currentRow = []
      currentCell = ''
      continue
    }

    currentCell += char
  }

  // 剪贴板通常以换行结尾，这个结尾只是行终止符，不代表额外空行。
  if (currentCell.length > 0 || currentRow.length > 0 || (normalizedText.length > 0 && !normalizedText.endsWith('\n'))) {
    currentRow.push(currentCell)
    rows.push(currentRow)
  }

  return rows
}

export function createEmptyProduct(): ProductImportItem {
  return {
    id: `row_${Date.now()}_${Math.random().toString(36).substring(2, 9)}`,
    selected: false,
    imageUrl: '',
    customImage: false,
    imageLoadStatus: 'loading',
    newProduct: { quantity: 1, productCode: '', productName: '' },
    status: 'unchanged',
    isDuplicate: false,
    calculated: { totalProducts: 0, totalVolume: 0 },
  }
}

function isPositiveInteger(value: number | undefined): value is number {
  return value !== undefined && Number.isFinite(value) && value > 0 && Number.isSafeInteger(value)
}

export function detectDuplicates(products: ProductImportItem[]): DuplicateGroup[] {
  const codeMap = new Map<string, ProductImportItem[]>()
  products.forEach((p) => {
    const code = p.newProduct.productCode?.trim()
    if (!code) return
    const existing = codeMap.get(code) || []
    existing.push(p)
    codeMap.set(code, existing)
  })
  const groups: DuplicateGroup[] = []
  codeMap.forEach((items, productCode) => {
    if (items.length > 1) {
      const invalidFields = new Set<DuplicateGroup['invalidFields'][number]>()
      let casePackQuantity = 0
      let volume = 0

      items.forEach((item) => {
        const { quantity, casePackQuantity: itemCasePackQuantity, volume: itemVolume } = item.newProduct
        if (!isPositiveInteger(quantity)) invalidFields.add('quantity')
        if (!isPositiveInteger(itemCasePackQuantity)) invalidFields.add('casePackQuantity')
        if (!isPositiveNumber(itemVolume)) invalidFields.add('volume')

        if (isPositiveInteger(quantity) && isPositiveInteger(itemCasePackQuantity)) {
          casePackQuantity += quantity * itemCasePackQuantity
        }
        if (isPositiveInteger(quantity) && isPositiveNumber(itemVolume)) {
          volume += quantity * itemVolume
        }
      })

      const roundedVolume = Math.round((volume + Number.EPSILON) * 1000) / 1000
      if (!Number.isSafeInteger(casePackQuantity)) invalidFields.add('casePackQuantity')
      // 最终业务值保留三位；舍入后为 0 同样不能进入检测、更新或货柜请求。
      if (!isPositiveNumber(roundedVolume)) invalidFields.add('volume')

      groups.push({
        productCode,
        count: items.length,
        rows: items.map((_, i) => i),
        items,
        merged: {
          quantity: 1,
          casePackQuantity,
          volume: roundedVolume,
        },
        invalidFields: Array.from(invalidFields),
        isMergeable: invalidFields.size === 0,
      })
    }
  })
  return groups
}

export function mergeDuplicateProducts(products: ProductImportItem[]): MergeDuplicateProductsResult {
  const duplicateGroups = detectDuplicates(products)
  const invalidGroups = duplicateGroups.filter((group) => !group.isMergeable)
  if (invalidGroups.length > 0) {
    return { products, invalidGroups, mergedGroupCount: 0 }
  }

  const groupsByCode = new Map(duplicateGroups.map((group) => [group.productCode, group]))
  const mergedCodes = new Set<string>()
  const result: ProductImportItem[] = []

  products.forEach((p) => {
    const code = p.newProduct.productCode?.trim()
    const group = code ? groupsByCode.get(code) : undefined
    if (!group) {
      result.push(p)
      return
    }

    if (mergedCodes.has(code!)) return
    mergedCodes.add(code!)

    // 合并只生成新的业务对象，避免修改粘贴进来的原始行或第一行的嵌套字段。
    result.push(updateCalculatedFields({
      ...p,
      newProduct: {
        ...p.newProduct,
        quantity: group.merged.quantity,
        casePackQuantity: group.merged.casePackQuantity,
        volume: group.merged.volume,
      },
      matchedProduct: p.matchedProduct ? { ...p.matchedProduct } : undefined,
      status: 'unchanged',
      isDuplicate: false,
      duplicateGroup: undefined,
      mergedFrom: group.count,
      diffFields: [],
      errors: undefined,
      calculated: { ...p.calculated },
    }))
  })

  return { products: result, invalidGroups: [], mergedGroupCount: duplicateGroups.length }
}

export function calculateStatistics(products: ProductImportItem[], selectedIds: string[]): Statistics {
  const selectedProducts = products.filter((p) => selectedIds.includes(p.id))
  return {
    total: products.length,
    duplicateCount: products.filter((p) => p.isDuplicate).length,
    newCount: products.filter((p) => p.status === 'new').length,
    updateCount: products.filter((p) => p.status === 'updated').length,
    unchangedCount: products.filter((p) => p.status === 'unchanged').length,
    errorCount: products.filter((p) => p.status === 'error').length,
    dbDuplicateCount: products.filter((p) => p.status === 'dbDuplicate').length,
    selectedCount: selectedProducts.length,
    totalQuantity: selectedProducts.reduce((sum, p) => sum + (p.newProduct.quantity || 0), 0),
    totalProducts: selectedProducts.reduce((sum, p) => sum + (p.newProduct.quantity || 0), 0),
    totalVolume: selectedProducts.reduce((sum, p) => sum + ((p.newProduct.volume || 0) * (p.newProduct.quantity || 0)), 0),
  }
}

export function updateCalculatedFields(product: ProductImportItem): ProductImportItem {
  return {
    ...product,
    calculated: {
      totalProducts: product.newProduct.quantity || 0,
      totalVolume: (product.newProduct.volume || 0) * (product.newProduct.quantity || 0),
    },
  }
}

export function containsChineseText(value: string): boolean {
  return /[\u3400-\u9fff]/.test(value)
}

export function applyProductImportNameTranslations(
  products: ProductImportItem[],
  translations: Record<string, string>,
  selectedIds: string[] = [],
): { products: ProductImportItem[]; appliedCount: number; skippedCount: number } {
  const selectedSet = new Set(selectedIds)
  const hasSelection = selectedSet.size > 0
  let appliedCount = 0
  let skippedCount = 0

  const nextProducts = products.map((product) => {
    if (hasSelection && !selectedSet.has(product.id)) return product

    const originalName = product.newProduct.productName.trim()
    const translatedName = translations[originalName]?.trim()
    if (!originalName || !containsChineseText(originalName)) return product

    // 只把有效英文翻译写入英文名称；保存/检测链路仍沿用页面现有数据结构。
    if (!translatedName || translatedName === originalName || containsChineseText(translatedName)) {
      skippedCount += 1
      return product
    }

    appliedCount += 1
    return {
      ...product,
      newProduct: {
        ...product.newProduct,
        englishName: translatedName,
      },
    }
  })

  return { products: nextProducts, appliedCount, skippedCount }
}

export function validateProduct(product: ProductImportItem, mode: string): { [field: string]: string } {
  const errors: { [field: string]: string } = {}
  if (!product.newProduct.productCode?.trim()) errors.productCode = '货号不能为空'
  if (mode === 'import' && !product.newProduct.productName?.trim()) errors.productName = '商品名称不能为空'
  return errors
}

function firstDefinedNumber(...values: Array<number | undefined>) {
  return values.find((value) => value !== undefined)
}

function firstPositiveNumber(...values: Array<number | undefined>) {
  return values.find((value) => value !== undefined && value > 0)
}

function isPositiveNumber(value: number | undefined): value is number {
  return value !== undefined && Number.isFinite(value) && value > 0
}

function isMissingText(value: string | undefined) {
  return !value?.trim()
}

export function buildAssignContainerItems(products: ProductImportItem[], notes?: string): AssignContainerValidationItem[] {
  return products.map((product) => ({
    hbProductNo: product.newProduct.productCode,
    productCode: product.matchedProduct?.productCode,
    quantity: product.newProduct.quantity,
    packingQuantity: firstPositiveNumber(product.newProduct.casePackQuantity, product.matchedProduct?.packingQuantity),
    unitVolume: firstPositiveNumber(product.newProduct.volume, product.matchedProduct?.unitVolume),
    domesticPrice: firstDefinedNumber(product.newProduct.domesticPrice, product.matchedProduct?.domesticPrice),
    oemPrice: firstDefinedNumber(product.newProduct.oemPrice, product.matchedProduct?.oemPrice),
    notes,
  }))
}

export function stripAssignContainerItemsForRequest(items: AssignContainerValidationItem[]): AssignContainerItem[] {
  return items.map(({ hbProductNo, productCode, quantity, packingQuantity, unitVolume, domesticPrice, oemPrice, notes }) => ({
    hbProductNo,
    productCode,
    quantity,
    packingQuantity,
    unitVolume,
    domesticPrice,
    oemPrice,
    notes,
  }))
}

export function findInvalidAssignContainerItems(items: AssignContainerValidationItem[]): InvalidAssignContainerItem[] {
  return items
    .map((item) => {
      const fields: string[] = []
      const reasons: string[] = []
      if (isMissingText(item.productCode)) {
        fields.push('本地商品编码')
        reasons.push('未匹配本地商品编码')
      }
      if (!isPositiveNumber(item.quantity)) fields.push('件数')
      if (!isPositiveNumber(item.domesticPrice)) fields.push('国内价格')
      if (!isPositiveNumber(item.packingQuantity)) fields.push('装箱数')
      if (!isPositiveNumber(item.unitVolume)) fields.push('体积')
      return { hbProductNo: item.hbProductNo, productCode: item.productCode, fields, reasons }
    })
    .filter((item) => item.fields.length > 0)
}

export function buildHbwebProductNameUpdates(
  products: ProductImportItem[],
  selectedIds: string[],
  supplierCode: string,
): BuildHbwebProductNameUpdatesResult {
  const selectedSet = new Set(selectedIds)
  const normalizedSupplierCode = supplierCode.trim()
  const nameBySupplierItem = new Map<string, string>()
  const updates: HbwebProductNameUpdate[] = []
  const missingItemNumbers: string[] = []
  const missingProductNames: string[] = []
  const conflictItemNumbers = new Set<string>()

  products.forEach((product) => {
    if (!selectedSet.has(product.id)) return

    const itemNumber = product.newProduct.productCode?.trim() ?? ''
    const productName = product.newProduct.englishName?.trim() ?? ''

    if (!itemNumber) {
      missingItemNumbers.push(product.id)
      return
    }

    if (!productName) {
      missingProductNames.push(itemNumber)
      return
    }

    const supplierItemKey = `${normalizedSupplierCode}\u001f${itemNumber}`.toUpperCase()
    const existingName = nameBySupplierItem.get(supplierItemKey)
    if (existingName === undefined) {
      // 供应商代码对应 HBweb Product.LocalSupplierCode，必须与货号一起定位主表商品。
      nameBySupplierItem.set(supplierItemKey, productName)
      updates.push({ SupplierCode: normalizedSupplierCode, ItemNumber: itemNumber, ProductName: productName })
      return
    }

    if (existingName !== productName) {
      conflictItemNumbers.add(itemNumber)
    }
  })

  return {
    products: updates,
    missingItemNumbers,
    missingProductNames,
    conflictItemNumbers: Array.from(conflictItemNumbers),
  }
}

export function summarizeAssignProductsResult(
  response: AssignProductsResponseLike,
  items: AssignContainerItem[] = [],
): AssignProductsResultSummary {
  const created = response.data?.created ?? 0
  const updated = response.data?.updated ?? 0
  const failedItems = response.data?.failed ?? []
  const succeeded = created + updated
  const failedCount = failedItems.length
  const hbProductNoByProductCode = new Map<string, string>()

  // 用本次发送的 payload 补齐失败明细里的货号，便于页面直接展示。
  items.forEach((item) => {
    const productCode = item.productCode?.trim()
    const hbProductNo = item.hbProductNo?.trim()
    if (productCode && hbProductNo) {
      hbProductNoByProductCode.set(productCode, hbProductNo)
    }
  })

  const failed = failedItems.map((item) => ({
    hbProductNo: item.productCode ? hbProductNoByProductCode.get(item.productCode.trim()) : undefined,
    productCode: item.productCode,
    reason: item.error || '未知原因',
  }))

  if (!response.success) {
    return {
      status: 'apiError',
      success: false,
      message: response.message,
      created,
      updated,
      succeeded,
      failedCount,
      failed,
    }
  }

  if (failedCount > 0 && succeeded === 0) {
    return {
      status: 'failed',
      success: false,
      message: response.message,
      created,
      updated,
      succeeded,
      failedCount,
      failed,
    }
  }

  if (failedCount > 0) {
    return {
      status: 'partial',
      success: true,
      message: response.message,
      created,
      updated,
      succeeded,
      failedCount,
      failed,
    }
  }

  return {
    status: 'success',
    success: true,
    message: response.message,
    created,
    updated,
    succeeded,
    failedCount,
    failed,
  }
}
