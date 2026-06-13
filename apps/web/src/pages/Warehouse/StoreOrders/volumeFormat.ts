export function formatStoreOrderVolume(value?: number | null) {
  if (value === undefined || value === null) {
    return '--'
  }

  // 体积展示统一保留两位小数，避免列表、详情和导出显示精度不一致。
  return value.toFixed(2)
}
