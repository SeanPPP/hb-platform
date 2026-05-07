import JsBarcode from 'jsbarcode'

export interface BarcodeOptions {
  width?: number
  height?: number
  displayValue?: boolean
  fontSize?: number
  margin?: number
  background?: string
  lineColor?: string
}

export const defaultBarcodeOptions: BarcodeOptions = {
  width: 2,
  height: 80,
  displayValue: true,
  fontSize: 12,
  margin: 10,
  background: '#ffffff',
  lineColor: '#000000',
}

export type BarcodeFormat = 'EAN13' | 'CODE128'

export function calculateEAN13CheckDigit(firstTwelveDigits: string): number | null {
  if (!/^\d{12}$/.test(firstTwelveDigits)) {
    return null
  }

  const digits = firstTwelveDigits.split('').map(Number)
  const sum = digits.reduce((total, digit, index) => {
    const position = index + 1
    return total + digit * (position % 2 === 0 ? 3 : 1)
  }, 0)

  return (10 - (sum % 10)) % 10
}

export function isValidEAN13(barcode: string): boolean {
  if (!/^\d{13}$/.test(barcode)) {
    return false
  }

  const expectedCheckDigit = calculateEAN13CheckDigit(barcode.slice(0, 12))
  if (expectedCheckDigit === null) {
    return false
  }

  return expectedCheckDigit === Number(barcode[12])
}

export function resolveBarcodeFormat(barcode: string): BarcodeFormat {
  return isValidEAN13(barcode) ? 'EAN13' : 'CODE128'
}

export function renderBarcodeToCanvas(
  canvas: HTMLCanvasElement,
  barcode: string,
  options: BarcodeOptions = defaultBarcodeOptions,
) {
  const format = resolveBarcodeFormat(barcode)

  try {
    JsBarcode(canvas, barcode, {
      format,
      ...defaultBarcodeOptions,
      ...options,
    })
  } catch (error) {
    if (format === 'EAN13') {
      JsBarcode(canvas, barcode, {
        format: 'CODE128',
        ...defaultBarcodeOptions,
        ...options,
      })
      return
    }

    throw error
  }
}

export function generateBarcodeDataUrl(
  barcode: string,
  options: BarcodeOptions = defaultBarcodeOptions,
): string {
  if (!barcode || !barcode.trim()) {
    throw new Error('条码内容不能为空')
  }

  const canvas = document.createElement('canvas')
  renderBarcodeToCanvas(canvas, barcode, options)
  return canvas.toDataURL('image/png')
}

export async function generateBarcodeImages(
  barcodes: string[],
  options: BarcodeOptions = defaultBarcodeOptions,
): Promise<Map<string, string>> {
  const barcodeMap = new Map<string, string>()

  for (const barcode of barcodes) {
    try {
      barcodeMap.set(barcode, generateBarcodeDataUrl(barcode, options))
    } catch (error) {
      console.error(`生成条码失败: ${barcode}`, error)
      barcodeMap.set(barcode, '')
    }
  }

  return barcodeMap
}
