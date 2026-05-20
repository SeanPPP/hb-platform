export function normalizeDiscountRate(v: number | null | undefined): number | null | undefined {
  if (v == null) return v
  if (v > 1) return v / 100
  return v
}

export function discountRateToDecimal(percentValue: number): number {
  return percentValue / 100
}

export function discountRateToPercent(decimalValue: number | null | undefined): number | null | undefined {
  if (decimalValue == null) return decimalValue
  const normalized = normalizeDiscountRate(decimalValue)
  return normalized! * 100
}

export function formatDiscountRate(v: number | null | undefined): string {
  if (v == null) return '-'
  const percent = discountRateToPercent(v)
  return percent!.toFixed(1) + '%'
}
