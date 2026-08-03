import type { InstallmentStatus, InstallmentSummary } from "@/core/contracts";
import type { components } from "@/generated/hbpos/schema";

export type InstallmentPaymentMethod = "cash" | "card" | "voucher";
export type InstallmentCardProvider = "square" | "linkly-cloud";
export type InstallmentPaymentStatus = "Recorded" | "Voided";
export type InstallmentDeviceScope = "store" | "device";
export type InstallmentDatePreset =
  | "all"
  | "today"
  | "last7"
  | "last30"
  | "custom";
export type InstallmentDateFilter = Readonly<{
  preset: InstallmentDatePreset;
  fromDate: string | null;
  toDate: string | null;
}>;

export type InstallmentLine = Readonly<{
  installmentLineGuid: string;
  productCode: string;
  referenceCode: string | null;
  displayName: string;
  lookupCode: string;
  quantity: string;
  unitPriceCents: number;
  discountCents: number;
  actualAmountCents: number;
  itemNumber: string | null;
}>;

export type InstallmentPayment = Readonly<{
  paymentGuid: string;
  method: InstallmentPaymentMethod;
  amountCents: number;
  status: InstallmentPaymentStatus;
  recordedAtIso: string;
  cashierId: string;
  deviceCode: string;
  cardType: string | null;
  maskedCardNumber: string | null;
}>;

export type InstallmentPickupInfo = Readonly<{
  pickedUpAtIso: string;
  pickedUpBy: string;
  note: string | null;
}>;

export type InstallmentCancellationInfo = Readonly<{
  kind: "RefundCancel" | "VoidCancel";
  cancelledAtIso: string;
  cancelledBy: string;
  reason: string | null;
}>;

export type InstallmentDetails = InstallmentSummary &
  Readonly<{
    cashierId: string;
    minimumDownPaymentCents: number;
    lines: readonly InstallmentLine[];
    payments: readonly InstallmentPayment[];
    pickupInfo: InstallmentPickupInfo | null;
    cancellationInfo: InstallmentCancellationInfo | null;
    note: string | null;
  }>;

export type InstallmentHistoryQuery = Readonly<{
  deviceCode?: string | null;
  createdFromIso?: string | null;
  createdToIso?: string | null;
  keyword: string | null;
  skip: number;
  status: InstallmentStatus | null;
  take: 20 | 50 | 51 | 100 | 200;
}>;

type CardTransaction =
  components["schemas"]["CardTransactionDto"];

export type InstallmentPaymentCommand = Readonly<{
  paymentGuid: string;
  method: InstallmentPaymentMethod;
  amountCents: number;
  reference: string | null;
  reservationToken: string | null;
  cardTransactions: readonly CardTransaction[];
  idempotencyKey: string;
}>;

export type InstallmentRefundCommand = Readonly<{
  paymentGuid: string;
  method: InstallmentPaymentMethod;
  amountCents: number;
  reference: string | null;
  cardTransactions: readonly CardTransaction[];
  idempotencyKey: string;
}>;

export type InstallmentIdentity = Readonly<{
  deviceCode: string;
  cashierId: string;
  cashierName: string;
}>;

export type InstallmentCreateCommand = InstallmentIdentity &
  Readonly<{
    installmentGuid: string;
    createdAtIso: string;
    totalCents: number;
    downPaymentCents: number;
    lines: readonly InstallmentLine[];
    downPayment: InstallmentPaymentCommand;
    customerName: string;
    customerPhone: string;
    note: string | null;
  }>;

export type InstallmentAppendPaymentCommand = InstallmentIdentity &
  Readonly<{
    installmentGuid: string;
    payment: InstallmentPaymentCommand;
  }>;

export type InstallmentCancelCommand = InstallmentIdentity &
  Readonly<{
    installmentGuid: string;
    cancelledAtIso: string;
    refunds: readonly InstallmentRefundCommand[];
    reason: string | null;
    idempotencyKey: string;
  }>;

export type InstallmentVoidCommand = InstallmentIdentity &
  Readonly<{
    installmentGuid: string;
    voidedAtIso: string;
    reason: string;
    idempotencyKey: string;
  }>;

export type InstallmentPickupCommand = InstallmentIdentity &
  Readonly<{
    installmentGuid: string;
    confirmedAtIso: string;
    note: string | null;
  }>;

/**
 * 这是已完成支付编排之后的低层 Hbpos Port。银行卡/券的授权、Unknown 恢复、
 * 退款 attempt 与受保护引用必须由上层 runtime 完成，React UI 不接触这些材料。
 */
export interface InstallmentsRemotePort {
  list(
    query: InstallmentHistoryQuery,
  ): Promise<readonly InstallmentSummary[]>;
  getDetails(
    installmentGuid: string,
  ): Promise<InstallmentDetails | null>;
  create(
    command: InstallmentCreateCommand,
  ): Promise<InstallmentDetails>;
  appendPayment(
    command: InstallmentAppendPaymentCommand,
  ): Promise<InstallmentDetails>;
  cancelWithRefund(
    command: InstallmentCancelCommand,
  ): Promise<InstallmentDetails>;
  void(command: InstallmentVoidCommand): Promise<InstallmentDetails>;
  confirmPickup(
    command: InstallmentPickupCommand,
  ): Promise<InstallmentDetails>;
}
