import type { HidScannerRouter } from "./hid-scanner";

export type ExpoCameraBarcodeResult = Readonly<{
  data?: string | null;
}>;

/** 将 expo-camera 的回调结果送入同一上下文路由；紧急 QR 的验签由 security 域负责。 */
export function createExpoCameraResultAdapter(scanner: HidScannerRouter) {
  return {
    onBarcodeScanned(result: ExpoCameraBarcodeResult): boolean {
      return scanner.acceptCameraText(result.data ?? "");
    },
  };
}
