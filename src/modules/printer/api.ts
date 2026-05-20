import type { ProductDetail } from "@/modules/product-maintenance/types";
import {
  connectPrinter,
  disconnectPrinter,
  getPrinterStatus,
  printNativeBigDiscountLabel,
  printNativeClearanceLabel,
  printNativeDiscountLabel,
  printNativeProductLabel,
  printNativeWarehouseLocationLabel,
  printNativeWarehouseProductLabel,
  printRawCommand,
  scanPrinters,
} from "@/modules/printer/native";
import { PrinterStorage } from "@/modules/printer/storage";
import { usePrinterStore } from "@/modules/printer/state";
import type {
  PrinterDevice,
  SavedPrinter,
  WarehouseLocationLabelPrintPayload,
  WarehouseProductLabelPrintPayload,
} from "@/modules/printer/types";

interface ProductLabelOverrides {
  barcode?: string | null;
  retailPrice?: number | null;
  discountRate?: number | null;
  clearanceBarcode?: string | null;
  clearancePrice?: number | null;
}

function toSavedPrinter(device: PrinterDevice | SavedPrinter): SavedPrinter {
  return {
    name: device.name ?? null,
    address: device.address,
  };
}

function buildPayload(detail: ProductDetail, overrides?: ProductLabelOverrides) {
  return {
    productName: detail.productName,
    itemNumber: detail.itemNumber,
    grade: detail.grade,
    supplierName: detail.localSupplierName,
    barcode: overrides?.barcode ?? detail.barcode,
    retailPrice: overrides?.retailPrice ?? detail.storePrice?.retailPrice ?? null,
    discountRate: overrides?.discountRate ?? detail.storePrice?.discountRate ?? null,
    clearanceBarcode: overrides?.clearanceBarcode ?? detail.clearancePrice?.clearanceBarcode ?? null,
    clearancePrice: overrides?.clearancePrice ?? detail.clearancePrice?.clearancePrice ?? null,
  };
}

function toNullableString(value: unknown): string | null {
  if (typeof value !== "string") {
    return null;
  }
  const trimmed = value.trim();
  return trimmed ? trimmed : null;
}

function toNullableNumber(value: unknown): number | null {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }
  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }
  return null;
}

function normalizeWarehouseProductLabelPayload(
  payload: WarehouseProductLabelPrintPayload
): WarehouseProductLabelPrintPayload {
  const data = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
  return {
    productCode: String(data.productCode ?? data.ProductCode ?? ""),
    productName: String(data.productName ?? data.ProductName ?? ""),
    itemNumber: toNullableString(data.itemNumber ?? data.ItemNumber),
    barcode: toNullableString(data.barcode ?? data.Barcode),
    supplierName: toNullableString(data.supplierName ?? data.SupplierName),
    middlePackageQuantity: toNullableNumber(data.middlePackageQuantity ?? data.MiddlePackageQuantity),
    purchasePrice: toNullableNumber(data.purchasePrice ?? data.PurchasePrice),
    retailPrice: toNullableNumber(data.retailPrice ?? data.RetailPrice),
    domesticPrice: toNullableNumber(data.domesticPrice ?? data.DomesticPrice),
    oemPrice: toNullableNumber(data.oEMPrice ?? data.OEMPrice ?? data.oemPrice ?? data.OemPrice),
    importPrice: toNullableNumber(data.importPrice ?? data.ImportPrice),
    locationCode: toNullableString(data.locationCode ?? data.LocationCode),
    locationBarcode: toNullableString(data.locationBarcode ?? data.LocationBarcode),
  };
}

function normalizeWarehouseLocationLabelPayload(
  payload: WarehouseLocationLabelPrintPayload
): WarehouseLocationLabelPrintPayload {
  const data = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
  return {
    locationGuid: String(data.locationGuid ?? data.LocationGuid ?? ""),
    locationCode: toNullableString(data.locationCode ?? data.LocationCode),
    locationBarcode: toNullableString(data.locationBarcode ?? data.LocationBarcode),
    productCount: toNullableNumber(data.productCount ?? data.ProductCount) ?? 0,
  };
}

async function ensureConnectedPrinter() {
  const store = usePrinterStore.getState();
  const status = await getPrinterStatus();
  if (status.connected) {
    store.setStatus("connected");
    return;
  }

  if (store.autoReconnectPaused) {
    store.setStatus("paused");
    throw new Error("Printer auto-connect is paused. Reconnect it in Settings first.");
  }

  const savedPrinter = await PrinterStorage.getPrinter();
  if (!savedPrinter?.address) {
    store.setStatus("disconnected");
    throw new Error("No label printer has been selected yet.");
  }

  store.setStatus("connecting");
  store.setAutoReconnectPaused(false);
  store.setLastError(null);
  const connected = await connectPrinter(savedPrinter.address);
  if (!connected) {
    usePrinterStore.getState().setStatus("error");
    throw new Error("Unable to connect to the saved label printer.");
  }
  usePrinterStore.getState().setSavedPrinter(savedPrinter);
  usePrinterStore.getState().setStatus("connected");
}

export async function scanPrinterDevices() {
  return scanPrinters();
}

export { getPrinterStatus };

export async function selectPrinter(device: PrinterDevice) {
  const nextPrinter = toSavedPrinter(device);
  await PrinterStorage.setPrinter(nextPrinter);
  const store = usePrinterStore.getState();
  store.setSavedPrinter(nextPrinter);
  store.setAutoReconnectPaused(false);
  store.setLastError(null);
  store.setStatus("connecting");
  const connected = await connectPrinter(device.address);
  store.setStatus(connected ? "connected" : "error");
  return connected;
}

export async function getSavedPrinter() {
  return PrinterStorage.getPrinter();
}

export async function clearSavedPrinter() {
  await disconnectPrinter();
  await PrinterStorage.clearPrinter();
  const store = usePrinterStore.getState();
  store.setSavedPrinter(null);
  store.setAutoReconnectPaused(false);
  store.setLastError(null);
  store.setStatus("idle");
}

export async function hydrateSavedPrinter() {
  const savedPrinter = await PrinterStorage.getPrinter();
  const store = usePrinterStore.getState();
  store.setSavedPrinter(savedPrinter);
  store.setHydrated(true);
  return savedPrinter;
}

export async function connectSavedPrinter(options?: { status?: "connecting" | "reconnecting" }) {
  const savedPrinter = await PrinterStorage.getPrinter();
  if (!savedPrinter?.address) {
    usePrinterStore.getState().setStatus("disconnected");
    throw new Error("No label printer has been selected yet.");
  }

  const store = usePrinterStore.getState();
  store.setSavedPrinter(savedPrinter);
  store.setAutoReconnectPaused(false);
  store.setLastError(null);
  store.setStatus(options?.status ?? "connecting");

  const connected = await connectPrinter(savedPrinter.address);
  if (!connected) {
    store.setStatus("error");
    throw new Error("Unable to connect to the saved label printer.");
  }

  store.setStatus("connected");
  return true;
}

export async function startPrinterAutoConnect() {
  resumePrinterAutoReconnect();
  return connectSavedPrinter({ status: "connecting" });
}

export async function disconnectCurrentPrinter(options?: { pauseAutoReconnect?: boolean }) {
  await disconnectPrinter();
  const pause = options?.pauseAutoReconnect ?? false;
  const store = usePrinterStore.getState();
  store.setAutoReconnectPaused(pause);
  store.setLastError(null);
  store.setStatus(pause ? "paused" : "disconnected");
  return true;
}

export function resumePrinterAutoReconnect() {
  const store = usePrinterStore.getState();
  store.setAutoReconnectPaused(false);
  if (store.savedPrinter) {
    store.setStatus("disconnected");
  }
}

export function stopPrinterAutoReconnect() {
  usePrinterStore.getState().setAutoReconnectPaused(true);
}

export async function syncPrinterStatus() {
  const nativeStatus = await getPrinterStatus();
  const store = usePrinterStore.getState();

  if (!store.savedPrinter) {
    store.setStatus(nativeStatus.connected ? "connected" : "idle");
    return nativeStatus;
  }

  if (nativeStatus.connected && nativeStatus.address === store.savedPrinter.address) {
    store.setStatus("connected");
    store.setLastError(null);
  } else if (store.autoReconnectPaused) {
    store.setStatus("paused");
  } else if (store.status !== "connecting" && store.status !== "reconnecting") {
    store.setStatus("disconnected");
  }

  return nativeStatus;
}

export async function testPrinterConnection() {
  await ensureConnectedPrinter();
  return printRawCommand("! 0 200 200 160 1\r\nPAGE-WIDTH 570\r\nTEXT 7 0 20 30 HB LABEL PRINTER\r\nTEXT 4 0 20 78 Connection OK\r\nTEXT 4 0 20 118 TEST\r\nPRINT\r\n");
}

export async function printProductLabel(detail: ProductDetail, overrides?: ProductLabelOverrides, printType?: string | null) {
  await ensureConnectedPrinter();
  return printNativeProductLabel(buildPayload(detail, overrides), printType);
}

export async function printDiscountLabel(detail: ProductDetail, printType?: string | null) {
  await ensureConnectedPrinter();
  return printNativeDiscountLabel(buildPayload(detail), printType);
}

export async function printClearanceLabel(detail: ProductDetail) {
  await ensureConnectedPrinter();
  return printNativeClearanceLabel(buildPayload(detail));
}

export async function printBigDiscountLabel(detail: ProductDetail, printType?: string | null) {
  await ensureConnectedPrinter();
  return printNativeBigDiscountLabel(buildPayload(detail), printType);
}

export async function printWarehouseProductLabel(payload: WarehouseProductLabelPrintPayload) {
  await ensureConnectedPrinter();
  return printNativeWarehouseProductLabel(normalizeWarehouseProductLabelPayload(payload));
}

export async function printWarehouseLocationLabel(payload: WarehouseLocationLabelPrintPayload) {
  await ensureConnectedPrinter();
  return printNativeWarehouseLocationLabel(normalizeWarehouseLocationLabelPayload(payload));
}
