export function formatOrderHistoryQuantity(value: number | null | undefined): string {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    return '—'
  }

  return value.toFixed(2).replace(/\.?0+$/, '')
}
