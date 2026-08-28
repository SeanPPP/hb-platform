import type { ProductCodeMode } from '../../../../services/storeProductSetCodeMaintenanceService'
import type { SetCodeDraftRow } from '../../ProductManagement/setCodeColumnPaste'

export type MaintenanceSetCodeDraftRow = SetCodeDraftRow & {
  sourceSetType: ProductCodeMode
}

export type ProductSetCodeMaintenanceLoadState = {
  mode: ProductCodeMode
  rows: MaintenanceSetCodeDraftRow[]
  baselineRows: MaintenanceSetCodeDraftRow[]
  canSwitchMode: boolean
  ready: boolean
  hasIntegrityError: boolean
  repairMultiCodeCount: number
}

export function resolveProductSetCodeMaintenanceLoadState({
  productType,
  typeOneRows,
  typeTwoRows,
}: {
  productType?: number | null
  typeOneRows: SetCodeDraftRow[]
  typeTwoRows: SetCodeDraftRow[]
}): ProductSetCodeMaintenanceLoadState {
  const normalizedTypeOneRows = typeOneRows.map((row) => ({ ...row, sourceSetType: 1 as const }))
  const normalizedTypeTwoRows = typeTwoRows.map((row) => ({
    ...row,
    // 转为套装后成本由兄弟项统一分摊，不能继续展示历史多条码的主条码成本。
    setPurchasePrice: undefined,
    sourceSetType: 2 as const,
  }))
  const mode: ProductCodeMode = productType === 1 ? 1 : 2
  const totalRows = normalizedTypeOneRows.length + normalizedTypeTwoRows.length
  if (productType === 1) {
    const rows = [...normalizedTypeOneRows, ...normalizedTypeTwoRows]
    return {
      mode: 1,
      rows,
      baselineRows: rows.map((row) => ({ ...row })),
      canSwitchMode: totalRows === 0,
      ready: true,
      hasIntegrityError: false,
      repairMultiCodeCount: normalizedTypeTwoRows.length,
    }
  }

  const hasMixedTypes = normalizedTypeOneRows.length > 0 && normalizedTypeTwoRows.length > 0
  const hasMismatchedRows = productType === 2
    ? normalizedTypeOneRows.length > 0
    : totalRows > 0
  const rows = normalizedTypeTwoRows

  return {
    mode,
    rows,
    baselineRows: rows.map((row) => ({ ...row })),
    canSwitchMode: totalRows === 0,
    ready: !hasMixedTypes && !hasMismatchedRows,
    hasIntegrityError: hasMixedTypes || hasMismatchedRows,
    repairMultiCodeCount: 0,
  }
}
