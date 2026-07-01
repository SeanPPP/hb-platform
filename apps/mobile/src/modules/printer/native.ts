import { NativeModules, PermissionsAndroid, Platform } from "react-native";
import {
  buildBigDiscountLabelCommand,
  buildClearanceLabelCommand,
  buildDiscountLabelCommand,
  buildProductLabelCommand,
  buildWarehouseLocationLabelCommand,
  buildWarehouseProductLabelCommand,
} from "@/modules/printer/cpcl-labels";
import type {
  PrinterDevice,
  PrinterStatus,
  ProductLabelPrintPayload,
  WarehouseLocationLabelPrintPayload,
  WarehouseProductLabelPrintPayload,
} from "@/modules/printer/types";

type NativePrinterModule = {
  getStatus(): Promise<PrinterStatus>;
  scanPrinters(durationMs?: number): Promise<PrinterDevice[]>;
  connect(address: string): Promise<boolean>;
  disconnect(): Promise<boolean>;
  print(command: string, encoding?: string): Promise<boolean>;
  printProductLabel(payload: ProductLabelPrintPayload, printType?: string | null): Promise<boolean>;
  printDiscountLabel(payload: ProductLabelPrintPayload, printType?: string | null): Promise<boolean>;
  printClearanceLabel(payload: ProductLabelPrintPayload): Promise<boolean>;
  printBigDiscountLabel(payload: ProductLabelPrintPayload, printType?: string | null): Promise<boolean>;
  printWarehouseProductLabel(payload: WarehouseProductLabelPrintPayload): Promise<boolean>;
  printWarehouseLocationLabel(payload: WarehouseLocationLabelPrintPayload): Promise<boolean>;
};

const nativeModule = NativeModules.HbPrinterModule as NativePrinterModule | undefined;
const unsupportedPrinterStatus: PrinterStatus = {
  supported: false,
  enabled: false,
  connected: false,
  address: null,
};

function getModule() {
  if (Platform.OS !== "android" && Platform.OS !== "ios") {
    throw new Error("Bluetooth printing is only supported on Android and iOS right now.");
  }

  if (!nativeModule) {
    throw new Error("The Bluetooth printer module is not available.");
  }

  return nativeModule;
}

function printIosCpclLabel(command: string) {
  // iOS 原生模块当前只负责 BLE raw 写入；业务标签在 TS 层生成 CPCL，避免复刻 Android 位图渲染。
  return getModule().print(command, "GB18030");
}

async function requestAndroidBluetoothPermissions() {
  if (Platform.OS !== "android") {
    return true;
  }

  const permissions: string[] =
    Platform.Version >= 31
      ? [
          PermissionsAndroid.PERMISSIONS.BLUETOOTH_CONNECT,
          PermissionsAndroid.PERMISSIONS.BLUETOOTH_SCAN,
        ]
      : [
          "android.permission.BLUETOOTH",
          "android.permission.BLUETOOTH_ADMIN",
          PermissionsAndroid.PERMISSIONS.ACCESS_FINE_LOCATION,
        ];

  const result = (await PermissionsAndroid.requestMultiple(
    permissions as never[]
  )) as Record<string, string>;
  return permissions.every((permission) => result[permission] === PermissionsAndroid.RESULTS.GRANTED);
}

export async function ensureBluetoothPermissions() {
  const granted = await requestAndroidBluetoothPermissions();
  if (!granted) {
    throw new Error("Bluetooth permission was not granted.");
  }
}

export async function getPrinterStatus() {
  // iOS 真机通过原生模块检查 BLE 状态；无模块时仍保持“不支持”状态，避免页面崩溃。
  if ((Platform.OS !== "android" && Platform.OS !== "ios") || !nativeModule) {
    return unsupportedPrinterStatus;
  }

  return getModule().getStatus();
}

export async function scanPrinters(durationMs = 5000) {
  await ensureBluetoothPermissions();
  return getModule().scanPrinters(durationMs);
}

export async function connectPrinter(address: string) {
  await ensureBluetoothPermissions();
  return getModule().connect(address);
}

export async function disconnectPrinter() {
  return getModule().disconnect();
}

export async function printRawCommand(command: string) {
  await ensureBluetoothPermissions();
  return getModule().print(command, "GB18030");
}

export async function printNativeProductLabel(payload: ProductLabelPrintPayload, printType?: string | null) {
  await ensureBluetoothPermissions();
  if (Platform.OS === "ios") {
    return printIosCpclLabel(buildProductLabelCommand(payload, printType));
  }
  return getModule().printProductLabel(payload, printType ?? null);
}

export async function printNativeDiscountLabel(payload: ProductLabelPrintPayload, printType?: string | null) {
  await ensureBluetoothPermissions();
  if (Platform.OS === "ios") {
    return printIosCpclLabel(buildDiscountLabelCommand(payload, printType));
  }
  return getModule().printDiscountLabel(payload, printType ?? null);
}

export async function printNativeClearanceLabel(payload: ProductLabelPrintPayload) {
  await ensureBluetoothPermissions();
  if (Platform.OS === "ios") {
    return printIosCpclLabel(buildClearanceLabelCommand(payload));
  }
  return getModule().printClearanceLabel(payload);
}

export async function printNativeBigDiscountLabel(
  payload: ProductLabelPrintPayload,
  printType?: string | null
) {
  await ensureBluetoothPermissions();
  if (Platform.OS === "ios") {
    return printIosCpclLabel(buildBigDiscountLabelCommand(payload, printType));
  }
  return getModule().printBigDiscountLabel(payload, printType ?? null);
}

export async function printNativeWarehouseProductLabel(payload: WarehouseProductLabelPrintPayload) {
  await ensureBluetoothPermissions();
  if (Platform.OS === "ios") {
    return printIosCpclLabel(buildWarehouseProductLabelCommand(payload));
  }
  return getModule().printWarehouseProductLabel(payload);
}

export async function printNativeWarehouseLocationLabel(payload: WarehouseLocationLabelPrintPayload) {
  await ensureBluetoothPermissions();
  if (Platform.OS === "ios") {
    return printIosCpclLabel(buildWarehouseLocationLabelCommand(payload));
  }
  return getModule().printWarehouseLocationLabel(payload);
}
