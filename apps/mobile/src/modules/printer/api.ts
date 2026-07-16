import type { ProductDetail } from "@/modules/product-maintenance/types";
import {
  buildEmployeeCashierBarcodeLabelCommand,
} from "@/modules/printer/cpcl-labels";
import {
  connectPrinter,
  disconnectPrinter,
  getPrinterStatus as getNativePrinterStatus,
  printNativeBigDiscountLabel,
  printNativeClearanceLabel,
  printNativeDiscountLabel,
  printNativeProductLabel,
  printNativeWarehouseLocationLabel,
  printNativeWarehouseProductLabel,
  printRawCommand,
  scanPrinters,
} from "@/modules/printer/native";
import { buildReceiptPrinterTestCommand } from "@/modules/printer/receipt";
import { PrinterStorage } from "@/modules/printer/storage";
import { usePrinterStore, useReceiptPrinterStore } from "@/modules/printer/state";
import type {
  EmployeeCashierBarcodeLabelPrintPayload,
  PrinterDevice,
  ProductLabelPrintPayload,
  SavedPrinter,
  WarehouseLocationLabelPrintPayload,
  WarehouseProductLabelPrintPayload,
} from "@/modules/printer/types";
import { isIosReviewSessionActive } from "@/modules/ios-review/session";

const IOS_REVIEW_LABEL_PRINTER: SavedPrinter = {
  name: "App Review Label Printer",
  address: "IOS-REVIEW-LABEL",
};
const IOS_REVIEW_RECEIPT_PRINTER: SavedPrinter = {
  name: "App Review Receipt Printer",
  address: "IOS-REVIEW-RECEIPT",
};

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
  const middlePackageQuantity = toNullableNumber(data.middlePackageQuantity ?? data.MiddlePackageQuantity);
  return {
    locationGuid: String(data.locationGuid ?? data.LocationGuid ?? ""),
    locationCode: toNullableString(data.locationCode ?? data.LocationCode),
    locationBarcode: toNullableString(data.locationBarcode ?? data.LocationBarcode),
    itemNumber: toNullableString(data.itemNumber ?? data.ItemNumber),
    productName: toNullableString(data.productName ?? data.ProductName),
    middlePackageQuantity: middlePackageQuantity && middlePackageQuantity > 0 ? middlePackageQuantity : 1,
    productCount: toNullableNumber(data.productCount ?? data.ProductCount) ?? 0,
  };
}

async function ensureConnectedPrinter() {
  if (isIosReviewSessionActive()) {
    // 审核模式只展示打印成功结果，不能读取蓝牙状态或连接真实设备。
    return;
  }
  const store = usePrinterStore.getState();
  const status = await getNativePrinterStatus();
  const savedPrinter = await PrinterStorage.getPrinter();
  if (status.connected && savedPrinter?.address && status.address === savedPrinter.address) {
    store.setStatus("connected");
    return;
  }

  if (store.autoReconnectPaused) {
    store.setStatus("paused");
    throw new Error("Printer auto-connect is paused. Reconnect it in Settings first.");
  }

  if (!savedPrinter?.address) {
    store.setStatus("disconnected");
    throw new Error("No label printer has been selected yet.");
  }

  if (status.connected && status.address !== savedPrinter.address) {
    // 小票测试会临时占用原生 socket；标签打印前必须确保当前连接回到标签打印机。
    await disconnectPrinter();
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
  if (isIosReviewSessionActive()) {
    // 固定演示设备让审核员可完整体验扫描、选择和测试流程。
    return [
      { ...IOS_REVIEW_LABEL_PRINTER, bonded: true, connected: false },
      { ...IOS_REVIEW_RECEIPT_PRINTER, bonded: true, connected: false },
    ];
  }
  return scanPrinters();
}

export async function getPrinterStatus() {
  if (isIosReviewSessionActive()) {
    return {
      supported: true,
      enabled: true,
      connected: true,
      address: IOS_REVIEW_LABEL_PRINTER.address,
    };
  }
  return getNativePrinterStatus();
}

export async function selectPrinter(device: PrinterDevice) {
  if (isIosReviewSessionActive()) {
    const store = usePrinterStore.getState();
    store.setSavedPrinter(toSavedPrinter(device));
    store.setLastError(null);
    store.setStatus("connected");
    return true;
  }
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
  if (isIosReviewSessionActive()) {
    return IOS_REVIEW_LABEL_PRINTER;
  }
  return PrinterStorage.getPrinter();
}

export async function getSavedReceiptPrinter() {
  if (isIosReviewSessionActive()) {
    return IOS_REVIEW_RECEIPT_PRINTER;
  }
  return PrinterStorage.getReceiptPrinter();
}

export async function clearSavedPrinter() {
  if (isIosReviewSessionActive()) {
    const store = usePrinterStore.getState();
    store.setSavedPrinter(null);
    store.setLastError(null);
    store.setStatus("idle");
    return;
  }
  await disconnectPrinter();
  await PrinterStorage.clearPrinter();
  const store = usePrinterStore.getState();
  store.setSavedPrinter(null);
  store.setAutoReconnectPaused(false);
  store.setLastError(null);
  store.setStatus("idle");
}

export async function hydrateSavedPrinter() {
  if (isIosReviewSessionActive()) {
    const store = usePrinterStore.getState();
    store.setSavedPrinter(IOS_REVIEW_LABEL_PRINTER);
    store.setStatus("connected");
    store.setHydrated(true);
    return IOS_REVIEW_LABEL_PRINTER;
  }
  const savedPrinter = await PrinterStorage.getPrinter();
  const store = usePrinterStore.getState();
  store.setSavedPrinter(savedPrinter);
  store.setHydrated(true);
  return savedPrinter;
}

export async function hydrateSavedReceiptPrinter() {
  if (isIosReviewSessionActive()) {
    const store = useReceiptPrinterStore.getState();
    store.setSavedPrinter(IOS_REVIEW_RECEIPT_PRINTER);
    store.setStatus("idle");
    store.setHydrated(true);
    return IOS_REVIEW_RECEIPT_PRINTER;
  }
  const savedPrinter = await PrinterStorage.getReceiptPrinter();
  const store = useReceiptPrinterStore.getState();
  store.setSavedPrinter(savedPrinter);
  store.setHydrated(true);
  return savedPrinter;
}

export async function connectSavedPrinter(options?: { status?: "connecting" | "reconnecting" }) {
  if (isIosReviewSessionActive()) {
    const store = usePrinterStore.getState();
    store.setSavedPrinter(IOS_REVIEW_LABEL_PRINTER);
    store.setLastError(null);
    store.setStatus("connected");
    return true;
  }
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
  if (isIosReviewSessionActive()) {
    const pause = options?.pauseAutoReconnect ?? false;
    const store = usePrinterStore.getState();
    store.setLastError(null);
    store.setStatus(pause ? "paused" : "disconnected");
    return true;
  }
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
  if (isIosReviewSessionActive()) {
    const status = {
      supported: true,
      enabled: true,
      connected: true,
      address: IOS_REVIEW_LABEL_PRINTER.address,
    };
    const store = usePrinterStore.getState();
    store.setSavedPrinter(IOS_REVIEW_LABEL_PRINTER);
    store.setLastError(null);
    store.setStatus("connected");
    return status;
  }
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
  if (isIosReviewSessionActive()) {
    return true;
  }
  await ensureConnectedPrinter();
  return printRawCommand("! 0 200 200 160 1\r\nPAGE-WIDTH 570\r\nTEXT 7 0 20 30 HB LABEL PRINTER\r\nTEXT 4 0 20 78 Connection OK\r\nTEXT 4 0 20 118 TEST\r\nPRINT\r\n");
}

export async function selectReceiptPrinter(device: PrinterDevice) {
  if (isIosReviewSessionActive()) {
    const store = useReceiptPrinterStore.getState();
    store.setSavedPrinter(toSavedPrinter(device));
    store.setLastError(null);
    store.setStatus("idle");
    return true;
  }
  const nextPrinter = toSavedPrinter(device);
  await PrinterStorage.setReceiptPrinter(nextPrinter);
  const store = useReceiptPrinterStore.getState();
  store.setSavedPrinter(nextPrinter);
  store.setAutoReconnectPaused(false);
  store.setLastError(null);
  store.setStatus("idle");
  return true;
}

export async function clearSavedReceiptPrinter() {
  if (isIosReviewSessionActive()) {
    const store = useReceiptPrinterStore.getState();
    store.setSavedPrinter(null);
    store.setLastError(null);
    store.setStatus("idle");
    return;
  }
  const savedPrinter = await PrinterStorage.getReceiptPrinter();
  const nativeStatus = await getPrinterStatus();
  if (savedPrinter?.address && nativeStatus.connected && nativeStatus.address === savedPrinter.address) {
    await disconnectPrinter();
  }

  await PrinterStorage.clearReceiptPrinter();
  const store = useReceiptPrinterStore.getState();
  store.setSavedPrinter(null);
  store.setAutoReconnectPaused(false);
  store.setLastError(null);
  store.setStatus("idle");
}

export async function testReceiptPrinterConnection() {
  if (isIosReviewSessionActive()) {
    return true;
  }
  const savedPrinter = await PrinterStorage.getReceiptPrinter();
  const receiptStore = useReceiptPrinterStore.getState();
  if (!savedPrinter?.address) {
    receiptStore.setStatus("disconnected");
    throw new Error("No receipt printer has been selected yet.");
  }

  receiptStore.setSavedPrinter(savedPrinter);
  receiptStore.setStatus("connecting");
  receiptStore.setLastError(null);

  const labelStoreBeforeTest = usePrinterStore.getState();
  const previousLabelAutoReconnectPaused = labelStoreBeforeTest.autoReconnectPaused;
  labelStoreBeforeTest.setAutoReconnectPaused(true);
  if (labelStoreBeforeTest.savedPrinter) {
    labelStoreBeforeTest.setStatus("paused");
  }

  let testSucceeded = false;
  let testError: unknown = null;
  let cleanupError: unknown = null;

  try {
    const connected = await connectPrinter(savedPrinter.address);
    if (!connected) {
      receiptStore.setStatus("error");
      throw new Error("Unable to connect to the saved receipt printer.");
    }

    receiptStore.setStatus("connected");
    await printRawCommand(buildReceiptPrinterTestCommand());
    testSucceeded = true;
  } catch (error) {
    testError = error;
    receiptStore.setStatus("error");
    receiptStore.setLastError(error instanceof Error ? error.message : String(error));
  } finally {
    try {
      await disconnectPrinter();
    } catch (error) {
      cleanupError = error;
    }

    const labelStoreAfterTest = usePrinterStore.getState();
    labelStoreAfterTest.setAutoReconnectPaused(previousLabelAutoReconnectPaused);
    // 原生蓝牙模块只有一个 socket，小票测试结束后恢复标签打印机原自动重连策略。
    labelStoreAfterTest.setStatus(
      labelStoreAfterTest.savedPrinter
        ? previousLabelAutoReconnectPaused
          ? "paused"
          : "disconnected"
        : "idle"
    );
  }

  if (cleanupError) {
    const cleanupMessage = cleanupError instanceof Error ? cleanupError.message : String(cleanupError);
    receiptStore.setStatus("error");
    if (testError) {
      const testMessage = testError instanceof Error ? testError.message : String(testError);
      receiptStore.setLastError(`${testMessage}; disconnect failed: ${cleanupMessage}`);
    } else {
      receiptStore.setLastError(cleanupMessage);
    }
    if (testSucceeded) {
      throw new Error(`Receipt test print was sent, but disconnect failed: ${cleanupMessage}`);
    }
  }

  if (testError) {
    throw testError;
  }

  receiptStore.setStatus("disconnected");
  return true;
}

export async function printProductLabel(detail: ProductDetail, overrides?: ProductLabelOverrides, printType?: string | null) {
  if (isIosReviewSessionActive()) {
    return true;
  }
  await ensureConnectedPrinter();
  return printNativeProductLabel(buildPayload(detail, overrides), printType);
}

export async function printProductLabelPayload(payload: ProductLabelPrintPayload, printType?: string | null) {
  if (isIosReviewSessionActive()) {
    return true;
  }
  await ensureConnectedPrinter();
  return printNativeProductLabel(payload, printType);
}

export async function printDiscountLabel(detail: ProductDetail, printType?: string | null) {
  if (isIosReviewSessionActive()) {
    return true;
  }
  await ensureConnectedPrinter();
  return printNativeDiscountLabel(buildPayload(detail), printType);
}

export async function printClearanceLabel(detail: ProductDetail) {
  if (isIosReviewSessionActive()) {
    return true;
  }
  await ensureConnectedPrinter();
  return printNativeClearanceLabel(buildPayload(detail));
}

export async function printBigDiscountLabel(detail: ProductDetail, printType?: string | null) {
  if (isIosReviewSessionActive()) {
    return true;
  }
  await ensureConnectedPrinter();
  return printNativeBigDiscountLabel(buildPayload(detail), printType);
}

export async function printWarehouseProductLabel(payload: WarehouseProductLabelPrintPayload) {
  if (isIosReviewSessionActive()) {
    return true;
  }
  await ensureConnectedPrinter();
  return printNativeWarehouseProductLabel(normalizeWarehouseProductLabelPayload(payload));
}

export async function printWarehouseLocationLabel(payload: WarehouseLocationLabelPrintPayload) {
  if (isIosReviewSessionActive()) {
    return true;
  }
  await ensureConnectedPrinter();
  return printNativeWarehouseLocationLabel(normalizeWarehouseLocationLabelPayload(payload));
}

export async function printEmployeeCashierBarcodeLabel(
  payload: EmployeeCashierBarcodeLabelPrintPayload
) {
  if (isIosReviewSessionActive()) {
    return true;
  }
  const status = await getPrinterStatus();
  if (status.supported && !status.enabled) {
    throw new Error("Bluetooth is disabled.");
  }
  await ensureConnectedPrinter();
  // 员工条码复用标签打印机、蓝牙权限、GB18030 编码和现有单连接链路。
  return printRawCommand(buildEmployeeCashierBarcodeLabelCommand(payload));
}
