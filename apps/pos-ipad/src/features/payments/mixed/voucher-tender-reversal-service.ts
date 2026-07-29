import type {
  MixedTenderReversalCommand,
  MixedTenderReversalMutation,
  MixedTenderReversalPort,
} from "./mixed-payment-coordinator";

import type { PaymentAttempt } from "@/core/contracts";
import type {
  VoucherTenderReversalRecord,
  VoucherTenderReversalStorePort,
} from "@/core/db/sqlite-voucher-tender-reversal-store";
import type {
  VoucherApprovedPurchaseReleasePort,
} from "@/features/payments/runtime/payment-provider-registry";

export type {
  VoucherTenderReversalReason,
  VoucherTenderReversalRecord,
  VoucherTenderReversalState,
  VoucherTenderReversalStorePort,
} from "@/core/db/sqlite-voucher-tender-reversal-store";

export type VoucherTenderReversalAttemptPort = Readonly<{
  getAttempt(attemptId: string): Promise<PaymentAttempt | null>;
}>;

type AvailableVoucherRelease = Extract<
  VoucherApprovedPurchaseReleasePort,
  { status: "available" }
>;

export type VoucherTenderReversalServiceOptions = Readonly<{
  store: VoucherTenderReversalStorePort;
  paymentAttempts: VoucherTenderReversalAttemptPort;
  release: AvailableVoucherRelease;
}>;

type InflightVoucherReversal = Readonly<{
  signature: string;
  promise: Promise<MixedTenderReversalMutation>;
}>;

// 中文注释：actionId 是 provider 幂等边界；跨 service 实例也必须共享同一次外部撤券。
const sharedVoucherReversals = new Map<
  string,
  InflightVoucherReversal
>();

const REVERSAL_REASON = "SALE";
const RELEASED_PROOF = Object.freeze({
  state: "Cancelled",
  responseCode: "VOUCHER_RELEASED",
} as const);

export class VoucherTenderReversalService
implements MixedTenderReversalPort {
  public constructor(
    private readonly options: VoucherTenderReversalServiceOptions,
  ) {}

  public reverseTender(
    command: MixedTenderReversalCommand,
  ): Promise<MixedTenderReversalMutation> {
    const normalized = normalizeCommand(command);
    const signature = [
      normalized.orderGuid,
      normalized.tenderGuid,
    ].join("|");
    const active = sharedVoucherReversals.get(normalized.actionId);
    if (active) {
      if (active.signature === signature) return active.promise;
      return Promise.reject(
        new Error("VOUCHER_TENDER_REVERSAL_ACTION_CONFLICT"),
      );
    }

    const promise = this.reverseOnce(normalized);
    const entry = { signature, promise };
    sharedVoucherReversals.set(normalized.actionId, entry);
    promise.then(
      () => deleteInflightIfCurrent(normalized.actionId, entry),
      () => deleteInflightIfCurrent(normalized.actionId, entry),
    );
    return promise;
  }

  private async reverseOnce(
    command: MixedTenderReversalCommand,
  ): Promise<MixedTenderReversalMutation> {
    const prepared = await this.options.store.prepareOrLoad({
      actionId: command.actionId,
      orderGuid: command.orderGuid,
      sourceTenderGuid: command.tenderGuid,
      reason: REVERSAL_REASON,
    });
    if (prepared.state === "Reversed") {
      return terminalMutation(prepared, "reversed");
    }
    if (prepared.state === "Blocked") {
      return terminalMutation(prepared, "declined");
    }

    const recordError = validatePreparedRecord(prepared, command);
    if (recordError) {
      return mutation(
        await this.options.store.markBlocked(prepared, recordError),
        "declined",
        false,
      );
    }

    const attempt = await this.options.paymentAttempts.getAttempt(
      prepared.sourceAttemptId,
    );
    if (!attempt) {
      return mutation(
        await this.options.store.markBlocked(
          prepared,
          "VOUCHER_SOURCE_ATTEMPT_MISSING",
        ),
        "declined",
        false,
      );
    }
    const attemptError = validateSourceAttempt(attempt, prepared);
    if (attemptError) {
      return mutation(
        await this.options.store.markBlocked(prepared, attemptError),
        "declined",
        false,
      );
    }

    const submitted = await this.options.store.markSubmitted(prepared);
    if (submitted.state === "Reversed") {
      return terminalMutation(submitted, "reversed");
    }
    if (submitted.state === "Blocked") {
      return terminalMutation(submitted, "declined");
    }
    if (submitted.state !== "Submitted") {
      return mutation(
        await this.options.store.markBlocked(
          submitted,
          "VOUCHER_TENDER_REVERSAL_SUBMIT_STATE_INVALID",
        ),
        "declined",
        false,
      );
    }

    let providerResult;
    try {
      providerResult = await this.options.release.release(attempt);
    } catch {
      return mutation(
        await this.options.store.markUnknown(
          submitted,
          "VOUCHER_RELEASE_TRANSPORT_ERROR",
        ),
        "unknown",
        false,
      );
    }

    if (
      providerResult.state === "Cancelled" &&
      providerResult.responseCode === "VOUCHER_RELEASED"
    ) {
      return mutation(
        await this.options.store.commitReleased(
          submitted,
          RELEASED_PROOF,
        ),
        "reversed",
        false,
      );
    }

    const responseCode = safeResponseCode(providerResult.responseCode);
    if (
      providerResult.state === "Unknown" &&
      !isDeterministicReleaseFailure(responseCode)
    ) {
      return mutation(
        await this.options.store.markUnknown(
          submitted,
          responseCode ?? "VOUCHER_RELEASE_RESULT_UNKNOWN",
        ),
        "unknown",
        false,
      );
    }
    return mutation(
      await this.options.store.markBlocked(
        submitted,
        responseCode ?? "VOUCHER_RELEASE_REJECTED",
      ),
      "declined",
      false,
    );
  }
}

function terminalMutation(
  record: VoucherTenderReversalRecord,
  state: "reversed" | "declined",
): MixedTenderReversalMutation {
  return mutation(record, state, true);
}

function mutation(
  record: VoucherTenderReversalRecord,
  state: "reversed" | "unknown" | "declined",
  replayed: boolean,
): MixedTenderReversalMutation {
  if (state === "reversed" && !record.reversalTenderGuid?.trim()) {
    throw new Error("VOUCHER_TENDER_REVERSAL_RESULT_INVALID");
  }
  return {
    state,
    replayed,
    reversalTenderGuid:
      state === "reversed" ? record.reversalTenderGuid : null,
    truth: record.truth,
  };
}

function validatePreparedRecord(
  record: VoucherTenderReversalRecord,
  command: MixedTenderReversalCommand,
): string | null {
  if (
    record.actionId !== command.actionId ||
    record.orderGuid !== command.orderGuid ||
    record.sourceTenderGuid !== command.tenderGuid ||
    record.reason !== REVERSAL_REASON
  ) {
    return "VOUCHER_TENDER_REVERSAL_BINDING_MISMATCH";
  }
  if (
    !record.sourceAttemptId.trim() ||
    record.amount.currency !== "AUD" ||
    !Number.isSafeInteger(record.amount.cents) ||
    record.amount.cents <= 0 ||
    record.truth.orderGuid !== record.orderGuid ||
    record.truth.state !== "Completing"
  ) {
    return "VOUCHER_TENDER_REVERSAL_SOURCE_INVALID";
  }
  const source = record.truth.tenders.find(
    (tender) => tender.tenderGuid === record.sourceTenderGuid,
  );
  if (
    !source ||
    source.method !== "voucher" ||
    source.amount.currency !== record.amount.currency ||
    source.amount.cents !== record.amount.cents
  ) {
    return "VOUCHER_TENDER_REVERSAL_SOURCE_INVALID";
  }
  return null;
}

function validateSourceAttempt(
  attempt: PaymentAttempt,
  record: VoucherTenderReversalRecord,
): string | null {
  if (
    attempt.attemptId !== record.sourceAttemptId ||
    attempt.orderGuid !== record.orderGuid ||
    attempt.provider !== "voucher" ||
    attempt.operation !== "purchase" ||
    attempt.state !== "Approved" ||
    attempt.amount.currency !== record.amount.currency ||
    attempt.amount.cents !== record.amount.cents
  ) {
    return "VOUCHER_SOURCE_ATTEMPT_BINDING_INVALID";
  }
  return null;
}

function isDeterministicReleaseFailure(code: string | null): boolean {
  return (
    code === "VOUCHER_RESERVATION_REQUIRED" ||
    code === "VOUCHER_PROVIDER_MISMATCH" ||
    code === "VOUCHER_PURCHASE_OPERATION_REQUIRED" ||
    code === "VOUCHER_APPROVED_ATTEMPT_REQUIRED" ||
    code === "VOUCHER_PROTECTED_REFERENCE_CONFLICT" ||
    code === "VOUCHER_PROTECTED_REFERENCE_INVALID" ||
    code === "VOUCHER_PROTECTED_STATE_MISSING" ||
    code === "VOUCHER_APPROVED_STATE_INVALID" ||
    code === "VOUCHER_AMOUNT_INVALID" ||
    code === "VOUCHER_STORE_CONFLICT" ||
    code === "VOUCHER_CASHIER_CONFLICT"
  );
}

function safeResponseCode(value: string | null): string | null {
  const normalized = value?.trim().toUpperCase() ?? "";
  return /^[A-Z0-9][A-Z0-9_.:-]{0,63}$/.test(normalized)
    ? normalized
    : null;
}

function normalizeCommand(
  command: MixedTenderReversalCommand,
): MixedTenderReversalCommand {
  return {
    actionId: requiredId(command.actionId),
    orderGuid: requiredId(command.orderGuid),
    tenderGuid: requiredId(command.tenderGuid),
  };
}

function requiredId(value: string): string {
  const normalized = value.trim();
  if (!normalized) {
    throw new TypeError("Voucher tender reversal identity is required.");
  }
  return normalized;
}

function deleteInflightIfCurrent(
  actionId: string,
  entry: InflightVoucherReversal,
): void {
  if (sharedVoucherReversals.get(actionId) === entry) {
    sharedVoucherReversals.delete(actionId);
  }
}
