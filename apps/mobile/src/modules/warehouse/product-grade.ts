export function normalizeWarehouseProductGrade(value?: string | null) {
  return value?.trim().toUpperCase() ?? "";
}

export function toggleWarehouseProductGradeSelection(currentGrade: string, selectedGrade: string) {
  const current = normalizeWarehouseProductGrade(currentGrade);
  const selected = normalizeWarehouseProductGrade(selectedGrade);

  // 重复点击当前等级表示清空等级，保存时会按空值提交。
  return current === selected ? "" : selected;
}
