import type { PrinterDevice } from "@/modules/printer/types";

export interface PrinterTransportFilters {
  showClassic: boolean;
  showBle: boolean;
}

export const DEFAULT_PRINTER_TRANSPORT_FILTERS: Readonly<PrinterTransportFilters> = {
  showClassic: true,
  showBle: false,
};

export function getPrinterTransport(device: PrinterDevice) {
  switch (device.transport) {
    case "classic":
    case "ble":
    case "dual":
      return device.transport;
    default:
      // 旧原生包没有类型字段，不能根据名称或地址猜测传输能力。
      return "unknown";
  }
}

export function isUnsupportedPrinterTransport(device: PrinterDevice, platform: string) {
  return platform === "android" && getPrinterTransport(device) === "ble";
}

export function getPrinterDeviceIcon(device: PrinterDevice): "printer" | "bluetooth" {
  // 影像主类别也包含相机/扫描仪，只有 Printer 能力位才显示打印机；仍不代表协议兼容性。
  return typeof device.deviceClass === "number"
    && (device.deviceClass & 0x1f00) === 0x0600
    && (device.deviceClass & 0x0080) !== 0
    ? "printer"
    : "bluetooth";
}

export function filterPrinterDevices(
  devices: PrinterDevice[],
  options: PrinterTransportFilters & { xpOnly?: boolean; platform: string }
) {
  return orderPrinterDevices(devices.filter((device) => {
    if (options.xpOnly && !device.name?.trim().toUpperCase().startsWith("XP")) return false;
    if (options.platform !== "android") return true;
    const transport = getPrinterTransport(device);
    if (transport === "ble") return options.showBle;
    if (transport === "dual") return options.showClassic || options.showBle;
    return options.showClassic;
  }));
}

export function orderPrinterDevices(devices: PrinterDevice[]) {
  return devices
    .map((device, index) => ({ device, index }))
    .sort(
      (left, right) =>
        Number(right.device.bonded) - Number(left.device.bonded) || left.index - right.index
    )
    .map(({ device }) => device);
}
