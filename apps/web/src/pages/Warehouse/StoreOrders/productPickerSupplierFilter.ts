export interface ProductPickerSupplierOption {
  label: string
  value: string
  supplierCode: string
  supplierName: string
  shopNumber?: string
}

export function formatProductPickerSupplierLabel(
  supplierName: string | undefined,
  supplierCode: string,
  shopNumber?: string,
) {
  const displayName = supplierName || supplierCode
  return shopNumber ? `${displayName} (${shopNumber})` : displayName
}

export function matchesProductPickerSupplierOption(
  input: string,
  option?: ProductPickerSupplierOption | null,
) {
  const keyword = input.trim().toLowerCase()
  if (!keyword) {
    return true
  }

  // 国内供应商下拉需同时支持按供应商编码、名称和店铺号检索。
  return [option?.supplierCode, option?.supplierName, option?.shopNumber]
    .filter((value): value is string => Boolean(value))
    .some((value) => value.toLowerCase().includes(keyword))
}
