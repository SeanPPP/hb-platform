import { NativeModules, PermissionsAndroid, Platform } from "react-native";
import type { PrinterDevice, PrinterStatus, ProductLabelPrintPayload } from "@/modules/printer/types";

type NativePrinterModule = {
  getStatus(): Promise<PrinterStatus>;
  scanPrinters(durationMs?: number): Promise<PrinterDevice[]>;
  connect(address: string): Promise<boolean>;
  disconnect(): Promise<boolean>;
  print(command: string, encoding?: string): Promise<boolean>;
  printProductLabel(payload: ProductLabelPrintPayload): Promise<boolean>;
  printDiscountLabel(payload: ProductLabelPrintPayload): Promise<boolean>;
  printClearanceLabel(payload: ProductLabelPrintPayload): Promise<boolean>;
  printBigDiscountLabel(payload: ProductLabelPrintPayload, printType?: string | null): Promise<boolean>;
};

const nativeModule = NativeModules.HbPrinterModule as NativePrinterModule | undefined;

function getModule() {
  if (Platform.OS !== "android") {
    throw new Error("Bluetooth label printing is only supported on Android right now.");
  }

  if (!nativeModule) {
    throw new Error("The Android printer module is not available.");
  }

  return nativeModule;
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

export async function printNativeProductLabel(payload: ProductLabelPrintPayload) {
  await ensureBluetoothPermissions();
  return getModule().printProductLabel(payload);
}

export async function printNativeDiscountLabel(payload: ProductLabelPrintPayload) {
  await ensureBluetoothPermissions();
  return getModule().printDiscountLabel(payload);
}

export async function printNativeClearanceLabel(payload: ProductLabelPrintPayload) {
  await ensureBluetoothPermissions();
  return getModule().printClearanceLabel(payload);
}

export async function printNativeBigDiscountLabel(
  payload: ProductLabelPrintPayload,
  printType?: string | null
) {
  await ensureBluetoothPermissions();
  return getModule().printBigDiscountLabel(payload, printType ?? null);
}
