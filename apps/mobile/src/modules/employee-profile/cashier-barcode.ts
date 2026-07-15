export type PendingCashierBarcodePrintConfirmation = {
  attemptId: string;
  barcode: string;
  phase: "printing" | "printed";
  createdAt: string;
};

function isPendingCashierBarcodePrintConfirmation(
  value: unknown
): value is PendingCashierBarcodePrintConfirmation {
  const candidate = value as Partial<PendingCashierBarcodePrintConfirmation> | null;
  return Boolean(
    candidate
    && typeof candidate.attemptId === "string"
    && candidate.attemptId.trim()
    && typeof candidate.barcode === "string"
    && candidate.barcode.trim()
    && (candidate.phase === "printing" || candidate.phase === "printed")
    && typeof candidate.createdAt === "string"
    && Number.isFinite(Date.parse(candidate.createdAt))
  );
}

export async function loadCashierBarcodePrintPending(
  storage: CashierPrintSecureStorageAdapter,
  userIdentity: string,
  session: CashierPrintSecureSession
) {
  const rawValue = await queueCashierPrintSecureRead(storage, session);
  if (!rawValue) {
    return null;
  }
  let value: unknown;
  try {
    value = JSON.parse(rawValue);
  } catch {
    await queueCashierPrintSecureWrite(storage, session, null);
    return null;
  }
  const candidate = value as ({ userIdentity?: unknown } & Partial<PendingCashierBarcodePrintConfirmation>);
  if (candidate.userIdentity === userIdentity && isPendingCashierBarcodePrintConfirmation(candidate)) {
    const { attemptId, barcode, phase, createdAt } = candidate;
    return { attemptId, barcode, phase, createdAt };
  }
  // 单一安全 key 可能残留上一账号记录；身份不匹配时删除且绝不返回其内容。
  await queueCashierPrintSecureWrite(storage, session, null);
  return null;
}

export async function saveCashierBarcodePrintPending(
  storage: CashierPrintSecureStorageAdapter,
  userIdentity: string,
  pending: PendingCashierBarcodePrintConfirmation | null,
  session: CashierPrintSecureSession
) {
  const written = await queueCashierPrintSecureWrite(
    storage,
    session,
    pending ? JSON.stringify({ userIdentity, ...pending }) : null
  );
  if (!written) {
    throw new Error("Cashier print secure session expired.");
  }
}

export function canRefreshCashierBarcode(
  pending: PendingCashierBarcodePrintConfirmation | null,
  pendingLoaded: boolean
) {
  return pendingLoaded && pending === null;
}

export async function resolveUncertainCashierBarcodePrint(
  pending: PendingCashierBarcodePrintConfirmation,
  choice: "printed" | "notPrinted"
) {
  return choice === "printed" ? { ...pending, phase: "printed" as const } : null;
}

export function buildCashierBarcodePrintConfirmationRequest(
  barcode: string,
  printAttemptId: string
) {
  const normalized = barcode.trim();
  if (!normalized) {
    throw new Error("Cashier barcode is required.");
  }
  const normalizedAttemptId = printAttemptId.trim();
  if (!normalizedAttemptId) {
    throw new Error("Print attempt ID is required.");
  }
  return { barcode: normalized, printAttemptId: normalizedAttemptId };
}

export class CashierBarcodePrintConfirmationError extends Error {
  constructor(
    cause: unknown,
    public readonly pendingConfirmation: PendingCashierBarcodePrintConfirmation
  ) {
    super(cause instanceof Error ? cause.message : String(cause));
    this.cause = cause;
  }
}

export class CashierBarcodePendingChangedError extends Error {
  constructor() {
    super("Cashier barcode changed while print confirmation was pending.");
  }
}

export class CashierBarcodeRevalidationError extends Error {
  constructor() {
    super("Unable to revalidate cashier barcode before printing.");
  }
}

export async function prepareNewCashierBarcodePrint({
  cachedBarcode,
  refetchBarcode,
}: {
  cachedBarcode: string;
  refetchBarcode: () => Promise<string | null>;
}) {
  const latestBarcode = await refetchBarcode();
  if (!latestBarcode) {
    throw new CashierBarcodeRevalidationError();
  }
  if (latestBarcode !== cachedBarcode) {
    throw new CashierBarcodePendingChangedError();
  }
  return latestBarcode;
}

export async function prepareUncertainCashierBarcodeReprint({
  pending,
  refetchBarcode,
  onPendingChange,
}: {
  pending: PendingCashierBarcodePrintConfirmation;
  refetchBarcode: () => Promise<string | null>;
  onPendingChange: (pending: PendingCashierBarcodePrintConfirmation | null) => Promise<void>;
}) {
  const latestBarcode = await refetchBarcode();
  if (!latestBarcode) {
    throw new CashierBarcodeRevalidationError();
  }
  await onPendingChange(null);
  if (latestBarcode !== pending.barcode) {
    // 用户选择重新打印前必须以服务端最新条码为准，旧码变化时只清理并提示。
    throw new CashierBarcodePendingChangedError();
  }
  return latestBarcode;
}

export async function executeCashierBarcodePrint<T>({
  pending,
  barcode,
  createAttemptId,
  now = () => new Date().toISOString(),
  printLabel,
  confirmPrint,
  onPendingChange,
}: {
  pending: PendingCashierBarcodePrintConfirmation | null;
  barcode: string;
  createAttemptId: () => string;
  now?: () => string;
  printLabel: (barcode: string) => Promise<void>;
  confirmPrint: (pending: PendingCashierBarcodePrintConfirmation) => Promise<T>;
  onPendingChange: (pending: PendingCashierBarcodePrintConfirmation | null) => Promise<void>;
}) {
  let confirmation = pending?.barcode === barcode ? pending : null;
  if (pending && !confirmation) {
    // 服务端条码已经变化时，旧 attempt 不能再确认，避免把旧标签计入新条码。
    await onPendingChange(null);
    throw new CashierBarcodePendingChangedError();
  }
  if (confirmation?.phase === "printing") {
    throw new Error("Printing result is uncertain and requires user confirmation.");
  }
  if (!confirmation) {
    confirmation = {
      attemptId: createAttemptId(),
      barcode,
      phase: "printing",
      createdAt: now(),
    };
    // 必须先落盘再调用打印机；崩溃恢复时 printing 表示结果不确定，禁止自动重复出纸。
    await onPendingChange(confirmation);
    await printLabel(barcode);
    confirmation = { ...confirmation, phase: "printed" };
    await onPendingChange(confirmation);
  }
  try {
    const result = await confirmPrint(confirmation);
    await onPendingChange(null);
    return result;
  } catch (error) {
    throw new CashierBarcodePrintConfirmationError(error, confirmation);
  }
}

export function isCashierBarcodeChangedError(error: unknown) {
  const candidate = error as {
    code?: unknown;
    message?: unknown;
    response?: { data?: { code?: unknown; errorCode?: unknown; message?: unknown } };
  } | null;
  const values = [
    candidate?.code,
    candidate?.message,
    candidate?.response?.data?.code,
    candidate?.response?.data?.errorCode,
    candidate?.response?.data?.message,
  ]
    .map((value) => String(value ?? "").toLowerCase())
    .join(" ");
  return values.includes("cashier_barcode_changed") || values.includes("条码已刷新");
}

export type CashierBarcodePrintErrorKind =
  | "noPrinter"
  | "bluetoothDisabled"
  | "permissionDenied"
  | "connectionFailed"
  | "unknown";

export function classifyCashierBarcodePrintError(error: unknown): CashierBarcodePrintErrorKind {
  const message = error instanceof Error ? error.message.toLowerCase() : String(error ?? "").toLowerCase();
  if (message.includes("no label printer") || message.includes("no printer")) {
    return "noPrinter";
  }
  if (message.includes("permission") || message.includes("denied")) {
    return "permissionDenied";
  }
  if (message.includes("disabled") || message.includes("turned off") || message.includes("not enabled")) {
    return "bluetoothDisabled";
  }
  if (message.includes("connect") || message.includes("socket")) {
    return "connectionFailed";
  }
  return "unknown";
}
import {
  queueCashierPrintSecureRead,
  queueCashierPrintSecureWrite,
  type CashierPrintSecureSession,
  type CashierPrintSecureStorageAdapter,
} from "../../shared/storage/cashier-print-secure-session";
