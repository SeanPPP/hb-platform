import type { PrinterDevice } from "@/modules/printer/types";

export function orderPrinterDevices(devices: PrinterDevice[]) {
  return devices
    .map((device, index) => ({ device, index }))
    .sort(
      (left, right) =>
        Number(right.device.bonded) - Number(left.device.bonded) || left.index - right.index
    )
    .map(({ device }) => device);
}
