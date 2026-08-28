export type InstallmentStatus =
  | "Active"
  | "PaidOff"
  | "PickedUp"
  | "Cancelled";

export type InstallmentCancellationKind = "RefundCancel" | "VoidCancel";

export type InstallmentSummary = Readonly<{
  installmentGuid: string;
  installmentNumber: string;
  storeCode: string;
  deviceCode: string;
  cashierName: string;
  customerName: string;
  customerPhone: string | null;
  createdAtIso: string;
  totalCents: number;
  downPaymentCents: number;
  paidCents: number;
  balanceCents: number;
  status: InstallmentStatus;
  updatedAtIso: string;
}>;

export type InstallmentSnapshot = InstallmentSummary &
  Readonly<{
    note: string | null;
    encryptedSensitiveRevision: number;
  }>;

/**
 * 分期本地缓存只用于离线浏览。所有 create/repayment/pickup/cancel/void 写操作
 * 必须通过在线 runtime，并复用耐久支付 attempt；仓储不得提供离线写业务状态的方法。
 */
export interface InstallmentSnapshotRepositoryPort {
  replaceForStore(
    storeCode: string,
    snapshots: readonly InstallmentSnapshot[],
  ): Promise<void>;
  listForStore(
    storeCode: string,
    limit: number,
    offset: number,
  ): Promise<readonly InstallmentSnapshot[]>;
  get(
    storeCode: string,
    installmentGuid: string,
  ): Promise<InstallmentSnapshot | null>;
}

export function canTransitionInstallment(
  from: InstallmentStatus,
  to: InstallmentStatus,
): boolean {
  return (
    (from === "Active" && (to === "PaidOff" || to === "Cancelled")) ||
    (from === "PaidOff" && to === "PickedUp")
  );
}
