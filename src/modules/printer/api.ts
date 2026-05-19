import type { ProductDetail } from "@/modules/product-maintenance/types";
import {
  buildClearanceLabelCpcl,
  buildDiscountLabelCpcl,
} from "@/modules/printer/cpcl";
import {
  connectPrinter,
  disconnectPrinter,
  getPrinterStatus,
  printNativeProductLabel,
  printRawCommand,
  scanPrinters,
} from "@/modules/printer/native";
import { PrinterStorage } from "@/modules/printer/storage";
import { usePrinterStore } from "@/modules/printer/state";
import type { PrinterDevice, SavedPrinter } from "@/modules/printer/types";

function toSavedPrinter(device: PrinterDevice | SavedPrinter): SavedPrinter {
  return {
    name: device.name ?? null,
    address: device.address,
  };
}

function buildPayload(detail: ProductDetail) {
  return {
    productName: detail.productName,
    itemNumber: detail.itemNumber,
    grade: detail.grade,
    supplierName: detail.localSupplierName,
    barcode: detail.barcode,
    retailPrice: detail.storePrice?.retailPrice ?? null,
    discountRate: detail.storePrice?.discountRate ?? null,
    clearanceBarcode: detail.clearancePrice?.clearanceBarcode ?? null,
    clearancePrice: detail.clearancePrice?.clearancePrice ?? null,
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

export async function printProductLabel(detail: ProductDetail) {
  await ensureConnectedPrinter();
  return printNativeProductLabel(buildPayload(detail));
}

export async function printDiscountLabel(detail: ProductDetail) {
  await ensureConnectedPrinter();
  return printRawCommand(buildDiscountLabelCpcl(buildPayload(detail)));
}

export async function printClearanceLabel(detail: ProductDetail) {
  await ensureConnectedPrinter();
  return printRawCommand(buildClearanceLabelCpcl(buildPayload(detail)));
}
