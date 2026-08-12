export const SHOP_BARCODE_REQUEST_TIMEOUT_MS = 15_000

export async function withShopBarcodeRequestTimeout<T>(
  request: (signal: AbortSignal) => Promise<T>,
  timeoutMs = SHOP_BARCODE_REQUEST_TIMEOUT_MS,
): Promise<T> {
  const controller = new AbortController()
  const timeoutId = globalThis.setTimeout(() => controller.abort(), timeoutMs)

  try {
    return await request(controller.signal)
  } finally {
    globalThis.clearTimeout(timeoutId)
  }
}
