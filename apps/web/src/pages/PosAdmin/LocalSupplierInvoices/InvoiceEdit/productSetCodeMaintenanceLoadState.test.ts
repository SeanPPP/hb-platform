import assert from 'node:assert/strict'
import { resolveProductSetCodeMaintenanceLoadState } from './productSetCodeMaintenanceLoadState'

const setRow = {
  id: 'set-1',
  setBarcode: 'SET-BARCODE',
  setRetailPrice: 12,
  isActive: true,
}
const historicalMultiCodeRow = {
  id: 'multi-1',
  setBarcode: 'MULTI-BARCODE',
  setPurchasePrice: 5,
  setRetailPrice: 15,
  isActive: true,
}

const repairState = resolveProductSetCodeMaintenanceLoadState({
  productType: 1,
  typeOneRows: [setRow],
  typeTwoRows: [historicalMultiCodeRow],
})
assert.equal(repairState.mode, 1, '主档为套装时应保持套装模式')
assert.equal(repairState.ready, true, '主档为套装时应允许把历史多条码子项转换为套装')
assert.equal(repairState.hasIntegrityError, false, '可确定转换方向时不应继续显示阻断错误')
assert.equal(repairState.repairMultiCodeCount, 1, '应明确提示待转换的历史多条码数量')
assert.deepEqual(
  repairState.rows.map((row) => [row.id, row.sourceSetType, row.setPurchasePrice, row.setRetailPrice]),
  [
    ['set-1', 1, undefined, 12],
    ['multi-1', 2, undefined, 15],
  ],
  '套装草稿应合并两类历史行、保留原类型基线，并清除旧多条码进货价展示',
)

const unsafeReverseMismatch = resolveProductSetCodeMaintenanceLoadState({
  productType: 2,
  typeOneRows: [setRow],
  typeTwoRows: [],
})
assert.equal(unsafeReverseMismatch.ready, false, '主档为多条码但存在套装行时仍应阻断，避免丢失独立套装价格语义')
assert.equal(unsafeReverseMismatch.hasIntegrityError, true)
