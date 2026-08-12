export type ShopBarcodeSubmitSource = 'camera' | 'hid' | 'manual'

interface ShopBarcodeSubmitGateInput {
  barcode: string
  busy: boolean
  cameraActive: boolean
  pickerOpen: boolean
  source: ShopBarcodeSubmitSource
}

export function shouldIgnoreShopBarcodeSubmit({
  barcode,
  busy,
  cameraActive,
  pickerOpen,
  source,
}: ShopBarcodeSubmitGateInput) {
  return (
    !barcode.trim()
    || busy
    || pickerOpen
    || (source === 'camera' ? !cameraActive : cameraActive)
  )
}
