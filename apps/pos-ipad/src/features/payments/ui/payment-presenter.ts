import type {
  Money,
  PaymentProvider,
  TenderMethod,
} from "@/core/contracts";
import type {
  LinklyOperatorPublicResult,
  LinklyOperatorRuntimePort,
  LinklyOperatorStatus,
  LinklySafeOperatorKey,
} from "@/features/payments/runtime/linkly-operator-runtime";
import {
  PaymentCheckoutRuntimeError,
  type PaymentCheckoutAllowedActions,
  type PaymentCheckoutErrorCode,
  type PaymentCheckoutPublicSnapshot,
  type PaymentCheckoutRuntimePort,
  type PaymentCheckoutStatus,
  type PaymentCheckoutTenderReversalRecovery,
  type PaymentCheckoutTender,
} from "@/features/payments/runtime/payment-checkout-runtime";
import type { PaymentProviderAvailability } from "@/features/payments/runtime/payment-provider-registry";

export type PaymentUiMethod = "cash" | PaymentProvider;

export type PaymentUiPhase =
  | "loading"
  | "ready"
  | "submitting"
  | "draft-prepared"
  | "awaiting-terminal"
  | "pending"
  | "offline-cash"
  | "unknown"
  | "recovery-required"
  | "cash-collection-ready"
  | "cash-confirming"
  | "partial"
  | "declined"
  | "cancelled"
  | "success";

export type PaymentFieldIssue =
  | "amount-required"
  | "amount-invalid"
  | "amount-exceeds-remaining"
  | "installment-customer-required"
  | "installment-down-payment-below-minimum"
  | "installment-total-below-minimum"
  | "voucher-required"
  | "method-unavailable"
  | "checkout-unavailable";

export type LinklyOperatorErrorCode =
  | "LINKLY_UNKNOWN_REQUIRES_RECOVERY"
  | "LINKLY_OPERATOR_STATE_INVALID"
  | "LINKLY_OPERATOR_KEY_NOT_ALLOWED";

export type PaymentUiRuntimeErrorCode =
  | PaymentCheckoutErrorCode
  | LinklyOperatorErrorCode
  | "INSTALLMENT_CASH_CONFIRMATION_REQUIRED"
  | "INSTALLMENT_CASH_CANCELLATION_FAILED";

export type PaymentPresenterTender = Readonly<{
  tenderGuid: string;
  method: TenderMethod;
  amount: Money;
  reversible: boolean;
  provider?: PaymentProvider | null;
}>;

export type PaymentCheckoutFlow =
  | "regular"
  | "installment-create"
  | "installment-repayment"
  | "installment-recovery";

export type PaymentCheckoutLine = Readonly<{
  lineKey: string;
  displayName: string;
  quantity: string;
  actualAmountCents: number;
}>;

export type InstallmentCustomerPresentation = Readonly<{
  name: string;
  phone: string;
  editable: boolean;
  editorOpen: boolean;
  draftName: string;
  draftPhone: string;
  installmentNumber: string | null;
}>;

export type PaymentCheckoutPresentation = Readonly<{
  flow: PaymentCheckoutFlow;
  lines: readonly PaymentCheckoutLine[];
  installmentCustomer: InstallmentCustomerPresentation | null;
  cash: Readonly<{
    tenderedCents: number;
    appliedCents: number;
    changeCents: number;
  }>;
  canConfirm: boolean;
  fullInstallmentConfirmationRequired: boolean;
  cashRepaymentStatus?: "idle" | "ready" | "confirming";
}>;

export type PaymentPresenterState = Readonly<{
  phase: PaymentUiPhase;
  busy: boolean;
  /** Square 后台轮询单独公开，避免把全局 busy 语义扩散到其他支付动作。 */
  recoveryInFlight?: boolean;
  initialized: boolean;
  providers: readonly PaymentProviderAvailability[];
  selectedMethod: PaymentUiMethod | null;
  amountText: string;
  voucherCaptured: boolean;
  sensitiveInputRevision: number;
  fieldIssue: PaymentFieldIssue | null;
  runtimeErrorCode: PaymentUiRuntimeErrorCode | null;
  orderGuid: string | null;
  total: Money;
  remaining: Money;
  tenders: readonly PaymentPresenterTender[];
  attemptId: string | null;
  provider: PaymentProvider | null;
  runtimeStatus: PaymentCheckoutStatus | null;
  allowedActions: PaymentCheckoutAllowedActions;
  tenderReversalRecovery: PaymentCheckoutTenderReversalRecovery | null;
  checkout: PaymentCheckoutPresentation;
  linkly: Readonly<{
    status: LinklyOperatorStatus | null;
    errorCode: LinklyOperatorErrorCode | null;
    allowedKeys: readonly LinklySafeOperatorKey[];
  }>;
}>;

/**
 * PaymentScreen 只依赖这层公开 facade。普通 PaymentPresenter 与分期 checkout
 * presenter 都实现同一交互面，页面无需接触任一耐久账本。
 */
export interface PaymentScreenPresenter {
  getState(): PaymentPresenterState;
  subscribe(listener: () => void): () => void;
  initialize(): Promise<boolean>;
  destroy(): void;
  selectMethod(method: PaymentUiMethod): boolean;
  setAmountText(value: string): void;
  setVoucherCode(value: string): void;
  dismissError(): void;
  submitSelected(): Promise<boolean>;
  recover(options?: PaymentRecoverOptions): Promise<boolean>;
  cancel(): Promise<boolean>;
  removeTender(tenderGuid: string): Promise<boolean>;
  sendLinklyKey(key: LinklySafeOperatorKey): Promise<boolean>;
  markLinklyReceiptPrinted(): Promise<boolean>;
  acknowledgeLinkly(): Promise<boolean>;
  confirm?(options?: PaymentConfirmOptions): Promise<boolean>;
  /** React 已提交成功页面后通知 presenter；普通支付无需实现。 */
  recordSuccessRendered?(): void;
  openInstallmentCustomerEditor?(): void;
  setInstallmentCustomerDraftName?(value: string): void;
  setInstallmentCustomerDraftPhone?(value: string): void;
  saveInstallmentCustomer?(): void;
  cancelInstallmentCustomerEditor?(): void;
}

export type PaymentConfirmOptions = Readonly<{
  acknowledgeFullInstallmentPayment?: boolean;
}>;

export type PaymentRecoverOptions = Readonly<{
  background?: boolean;
}>;

export type PaymentCheckoutEntryContext = Readonly<{
  checkoutIntentId: string;
  expectedCartRevision: number;
  total: Money;
  lines?: readonly PaymentCheckoutLine[];
}>;

export type PaymentPresenterDependencies = Readonly<{
  runtime: PaymentCheckoutRuntimePort;
  linklyOperator?: LinklyOperatorRuntimePort;
  entry: PaymentCheckoutEntryContext | null;
  createActionId(): string;
}>;

export const LINKLY_SAFE_OPERATOR_KEYS = Object.freeze([
  "ok-cancel",
  "yes",
  "no",
  "authorise",
] as const satisfies readonly LinklySafeOperatorKey[]);

const EMPTY_ALLOWED_ACTIONS: PaymentCheckoutAllowedActions = Object.freeze({
  start: false,
  changeProvider: true,
  recover: false,
  cancel: false,
  addCash: false,
  removeTender: false,
});

const ZERO_AUD: Money = Object.freeze({ currency: "AUD", cents: 0 });

function emptyRegularCheckout(
  lines: readonly PaymentCheckoutLine[] = [],
): PaymentCheckoutPresentation {
  return Object.freeze({
    flow: "regular",
    lines: Object.freeze(
      lines.map((line) => Object.freeze({ ...line })),
    ),
    installmentCustomer: null,
    cash: Object.freeze({
      tenderedCents: 0,
      appliedCents: 0,
      changeCents: 0,
    }),
    canConfirm: false,
    fullInstallmentConfirmationRequired: false,
    cashRepaymentStatus: "idle",
  });
}

function resetCashPresentation(
  checkout: PaymentCheckoutPresentation,
): PaymentCheckoutPresentation {
  if (
    checkout.cash.tenderedCents === 0 &&
    checkout.cash.appliedCents === 0 &&
    checkout.cash.changeCents === 0
  ) {
    return checkout;
  }
  return Object.freeze({
    ...checkout,
    cash: Object.freeze({
      tenderedCents: 0,
      appliedCents: 0,
      changeCents: 0,
    }),
  });
}

/**
 * Presenter 的公开状态仅保留运行时已经脱敏的本地标识与金额。
 * 券码只短暂保留在私有字段，永远不会出现在 state、日志或错误文案中。
 */
export class PaymentPresenter {
  private state: PaymentPresenterState;
  private readonly listeners = new Set<() => void>();
  private actionInFlight: Promise<boolean> | null = null;
  private voucherCode = "";
  private destroyed = false;
  private lifecycleRevision = 0;

  public constructor(private readonly dependencies: PaymentPresenterDependencies) {
    const providers = safeProviderAvailability(dependencies.runtime);
    // 默认支付方式：现金可启动时优先现金，否则回退到第一个可用 provider。
    const selectedMethod = resolveInitialMethod(providers, dependencies);
    const total = dependencies.entry
      ? copyMoney(dependencies.entry.total)
      : ZERO_AUD;
    this.state = {
      phase: "loading",
      busy: false,
      recoveryInFlight: false,
      initialized: false,
      providers,
      selectedMethod,
      amountText:
        total.cents > 0 ? centsToAmountInput(total.cents) : "",
      voucherCaptured: false,
      sensitiveInputRevision: 0,
      fieldIssue: null,
      runtimeErrorCode: null,
      orderGuid: null,
      total,
      remaining: total,
      tenders: Object.freeze([]),
      attemptId: null,
      provider: null,
      runtimeStatus: null,
      allowedActions: Object.freeze({
        ...EMPTY_ALLOWED_ACTIONS,
        start: dependencies.entry !== null,
        addCash: isCashAvailable(dependencies),
      }),
      tenderReversalRecovery: null,
      checkout: emptyRegularCheckout(dependencies.entry?.lines),
      linkly: emptyLinklyState(),
    };
  }

  public readonly getState = (): PaymentPresenterState => this.state;

  public readonly subscribe = (listener: () => void): (() => void) => {
    if (this.destroyed) return () => undefined;
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public initialize(): Promise<boolean> {
    if (this.state.initialized) return Promise.resolve(true);
    return this.runExclusive(async (revision) => {
      this.patchIfCurrent(revision, {
        phase: "loading",
        runtimeErrorCode: null,
        fieldIssue: null,
      });
      const recovery = await this.dependencies.runtime.findRecoveryRequired();
      if (!this.isCurrent(revision)) return false;
      if (recovery) {
        this.applySnapshot(recovery);
      } else {
        this.patch({
          phase: "ready",
          initialized: true,
          allowedActions: Object.freeze({
            ...EMPTY_ALLOWED_ACTIONS,
            start: this.dependencies.entry !== null,
            addCash: isCashAvailable(this.dependencies),
          }),
        });
      }
      return true;
    });
  }

  public load(orderGuid: string): Promise<boolean> {
    return this.runExclusive(async (revision) => {
      this.patchIfCurrent(revision, {
        phase: "loading",
        runtimeErrorCode: null,
        fieldIssue: null,
      });
      const snapshot = await this.dependencies.runtime.read(orderGuid);
      if (!this.isCurrent(revision)) return false;
      this.applySnapshot(snapshot);
      return true;
    });
  }

  public destroy(): void {
    if (this.destroyed) return;
    this.destroyed = true;
    this.lifecycleRevision += 1;
    this.voucherCode = "";
    this.listeners.clear();
  }

  public selectMethod(method: PaymentUiMethod): boolean {
    if (
      this.destroyed ||
      this.state.busy ||
      !canSelectPaymentMethod(this.state, method)
    ) {
      this.patch({ fieldIssue: "method-unavailable" });
      return false;
    }
    if (method !== "voucher") this.clearVoucher();
    this.patch({
      selectedMethod: method,
      fieldIssue: null,
      runtimeErrorCode: null,
      checkout:
        method === "cash"
          ? this.state.checkout
          : resetCashPresentation(this.state.checkout),
    });
    return true;
  }

  public setAmountText(value: string): void {
    if (this.destroyed || this.state.busy) return;
    this.patch({
      amountText: value,
      fieldIssue: null,
    });
  }

  public setVoucherCode(value: string): void {
    if (this.destroyed || this.state.busy) return;
    this.voucherCode = value.trim();
    this.patch({
      voucherCaptured: this.voucherCode.length > 0,
      fieldIssue:
        this.state.fieldIssue === "voucher-required"
          ? null
          : this.state.fieldIssue,
    });
  }

  public dismissError(): void {
    if (this.destroyed || this.state.busy) return;
    this.patch({
      runtimeErrorCode: null,
      linkly: {
        ...this.state.linkly,
        errorCode: null,
      },
    });
  }

  public submitSelected(): Promise<boolean> {
    const method = this.state.selectedMethod;
    if (!method || !canSubmitPaymentMethod(this.state, method)) {
      this.patch({ fieldIssue: "method-unavailable" });
      return Promise.resolve(false);
    }
    const amount = parseAudInput(this.state.amountText);
    if (!this.state.amountText.trim()) {
      this.patch({ fieldIssue: "amount-required" });
      return Promise.resolve(false);
    }
    if (!amount) {
      this.patch({ fieldIssue: "amount-invalid" });
      return Promise.resolve(false);
    }
    if (
      method !== "cash" &&
      amount.cents > this.state.remaining.cents
    ) {
      this.patch({ fieldIssue: "amount-exceeds-remaining" });
      return Promise.resolve(false);
    }
    if (method === "voucher" && !this.voucherCode) {
      this.patch({ fieldIssue: "voucher-required" });
      return Promise.resolve(false);
    }

    return this.runExclusive(async (revision) => {
      this.patchIfCurrent(revision, {
        phase: "submitting",
        runtimeErrorCode: null,
        fieldIssue: null,
      });
      const actionId = this.dependencies.createActionId();
      const appliedAmount =
        method === "cash" && amount.cents > this.state.remaining.cents
          ? copyMoney(this.state.remaining)
          : amount;
      let snapshot: PaymentCheckoutPublicSnapshot | null;
      try {
        if (method === "cash") {
          const orderGuid = this.state.orderGuid;
          if (!orderGuid) {
            const entry = this.dependencies.entry;
            if (!entry || !this.dependencies.runtime.startCash) {
              this.patchIfCurrent(revision, {
                fieldIssue: "checkout-unavailable",
              });
              return false;
            }
            snapshot = await this.dependencies.runtime.startCash({
              checkoutIntentId: entry.checkoutIntentId,
              expectedCartRevision: entry.expectedCartRevision,
              actionId,
              amount,
            });
          } else {
            snapshot = await this.dependencies.runtime.addCash({
              actionId,
              orderGuid,
              amount,
            });
          }
        } else if (
          this.state.runtimeStatus === "draft-prepared" &&
          this.state.orderGuid
        ) {
          snapshot = await this.dependencies.runtime.resumeCurrent({
            actionId,
            provider: method,
            amount,
            ...(method === "voucher"
              ? { voucherCode: this.voucherCode }
              : {}),
          });
        } else {
          const entry = this.dependencies.entry;
          if (!entry) {
            this.patchIfCurrent(revision, {
              fieldIssue: "checkout-unavailable",
            });
            return false;
          }
          snapshot = await this.dependencies.runtime.start({
            checkoutIntentId: entry.checkoutIntentId,
            expectedCartRevision: entry.expectedCartRevision,
            actionId,
            provider: method,
            amount,
            ...(method === "voucher"
              ? { voucherCode: this.voucherCode }
              : {}),
          });
        }
      } finally {
        if (method === "voucher") this.clearVoucher();
      }
      if (!snapshot || !this.isCurrent(revision)) return false;
      this.applySnapshot(snapshot);
      if (method === "cash") {
        const committedCents =
          snapshot.tenders.find((tender) => tender.method === "cash")
            ?.amount.cents ?? appliedAmount.cents;
        this.patchIfCurrent(revision, {
          checkout: Object.freeze({
            ...this.state.checkout,
            cash: Object.freeze({
              tenderedCents: amount.cents,
              appliedCents: committedCents,
              changeCents: Math.max(
                0,
                amount.cents - committedCents,
              ),
            }),
          }),
        });
      }
      return snapshot.status === "completed" || snapshot.status === "partial";
    });
  }

  public recover(options: PaymentRecoverOptions = {}): Promise<boolean> {
    const orderGuid = this.state.orderGuid;
    const tenderReversalRecovery = this.state.tenderReversalRecovery;
    const retryTenderReversal =
      this.dependencies.runtime.retryTenderReversal?.bind(
        this.dependencies.runtime,
      );
    if (
      !this.state.allowedActions.recover ||
      !orderGuid ||
      tenderReversalRecovery?.status === "blocked" ||
      (tenderReversalRecovery !== null && !retryTenderReversal)
    ) {
      return Promise.resolve(false);
    }
    return this.runExclusive(async (revision) => {
      if (!options.background) {
        this.patchIfCurrent(revision, {
          phase: "submitting",
          runtimeErrorCode: null,
        });
      }
      const snapshot = tenderReversalRecovery
        ? await retryTenderReversal!({
            orderGuid,
            tenderGuid: tenderReversalRecovery.tenderGuid,
          })
        : this.state.attemptId
          ? await this.dependencies.runtime.recover({
              orderGuid,
              attemptId: this.state.attemptId,
            })
          : await this.dependencies.runtime.resumeCurrent();
      if (!snapshot || !this.isCurrent(revision)) return false;
      this.applySnapshot(snapshot);
      return snapshot.status === "completed" || snapshot.status === "partial";
    }, { background: options.background === true });
  }

  public cancel(): Promise<boolean> {
    if (
      !this.state.allowedActions.cancel ||
      !this.state.orderGuid
    ) {
      return Promise.resolve(false);
    }
    return this.runExclusive(async (revision) => {
      this.patchIfCurrent(revision, {
        phase: "submitting",
        runtimeErrorCode: null,
      });
      const snapshot = this.state.attemptId
        ? await this.dependencies.runtime.cancel({
            orderGuid: this.state.orderGuid!,
            attemptId: this.state.attemptId,
          })
        : await this.dependencies.runtime.abandonPrepared({
            orderGuid: this.state.orderGuid!,
            actionId: this.dependencies.createActionId(),
          });
      if (!this.isCurrent(revision)) return false;
      this.applySnapshot(snapshot);
      return snapshot.status === "cancelled";
    });
  }

  public removeTender(tenderGuid: string): Promise<boolean> {
    if (
      !this.state.allowedActions.removeTender ||
      !this.state.orderGuid ||
      !this.state.tenders.some(
        (tender) =>
          tender.tenderGuid === tenderGuid && tender.reversible,
      )
    ) {
      return Promise.resolve(false);
    }
    return this.runExclusive(async (revision) => {
      this.patchIfCurrent(revision, {
        phase: "submitting",
        runtimeErrorCode: null,
      });
      const snapshot = await this.dependencies.runtime.removeTender({
        orderGuid: this.state.orderGuid!,
        actionId: this.dependencies.createActionId(),
        tenderGuid,
      });
      if (!this.isCurrent(revision)) return false;
      this.applySnapshot(snapshot);
      return snapshot.errorCode === null;
    });
  }

  public sendLinklyKey(key: LinklySafeOperatorKey): Promise<boolean> {
    if (
      !this.dependencies.linklyOperator ||
      this.state.provider !== "linkly-cloud" ||
      !this.state.attemptId ||
      !LINKLY_SAFE_OPERATOR_KEYS.includes(key)
    ) {
      return Promise.resolve(false);
    }
    return this.runExclusive(async (revision) => {
      const result = await this.dependencies.linklyOperator!.sendKey({
        attemptId: this.state.attemptId!,
        key,
      });
      if (!this.isCurrent(revision)) return false;
      this.applyLinklyResult(result);
      if (
        result.status === "completed" ||
        result.status === "cancelled"
      ) {
        const snapshot = await this.dependencies.runtime.recover({
          orderGuid: this.state.orderGuid!,
          attemptId: result.attemptId,
        });
        if (!this.isCurrent(revision)) return false;
        this.applySnapshot(snapshot);
      }
      return result.errorCode === null;
    });
  }

  public markLinklyReceiptPrinted(): Promise<boolean> {
    return this.runLinklyConfirmation("printed");
  }

  public acknowledgeLinkly(): Promise<boolean> {
    return this.runLinklyConfirmation("acknowledged");
  }

  private runLinklyConfirmation(
    action: "printed" | "acknowledged",
  ): Promise<boolean> {
    if (
      !this.dependencies.linklyOperator ||
      this.state.provider !== "linkly-cloud" ||
      !this.state.attemptId
    ) {
      return Promise.resolve(false);
    }
    return this.runExclusive(async (revision) => {
      const result =
        action === "printed"
          ? await this.dependencies.linklyOperator!.markReceiptPrinted(
              this.state.attemptId!,
            )
          : await this.dependencies.linklyOperator!.acknowledge(
              this.state.attemptId!,
            );
      if (!this.isCurrent(revision)) return false;
      this.applyLinklyResult(result);
      return result.errorCode === null;
    });
  }

  private applySnapshot(snapshot: PaymentCheckoutPublicSnapshot): void {
    if (this.destroyed) return;
    const tenders = copyTenders(snapshot.tenders);
    const tenderReversalRecovery = copyTenderReversalRecovery(
      snapshot.tenderReversalRecovery,
    );
    const activeMethods = tenders.map((tender) => tender.method);
    const currentMethod = this.state.selectedMethod;
    const selectedMethod =
      currentMethod &&
      isMethodSelectableFromSnapshot(
        currentMethod,
        snapshot,
        this.state.providers,
        activeMethods,
      )
        ? currentMethod
        : firstAvailableMethod(
            snapshot,
            this.state.providers,
            activeMethods,
          );
    this.state = {
      ...this.state,
      phase: phaseForSnapshot(snapshot),
      initialized: true,
      selectedMethod,
      amountText:
        snapshot.remaining.cents > 0
          ? centsToAmountInput(snapshot.remaining.cents)
          : "",
      fieldIssue: null,
      runtimeErrorCode: snapshot.errorCode,
      orderGuid: snapshot.orderGuid,
      total: copyMoney(snapshot.total),
      remaining: copyMoney(snapshot.remaining),
      tenders,
      attemptId: snapshot.attemptId,
      provider: snapshot.provider,
      runtimeStatus: snapshot.status,
      allowedActions: Object.freeze({
        ...snapshot.allowedActions,
        ...(tenderReversalRecovery?.status === "blocked"
          ? { recover: false }
          : {}),
      }),
      tenderReversalRecovery,
      checkout: tenders.some((tender) => tender.method === "cash")
        ? this.state.checkout
        : resetCashPresentation(this.state.checkout),
      linkly: linklyStateForSnapshot(snapshot, this.state.linkly),
    };
    this.emit();
  }

  private applyLinklyResult(result: LinklyOperatorPublicResult): void {
    if (this.destroyed || result.attemptId !== this.state.attemptId) return;
    const errorCode = stableLinklyError(result.errorCode);
    this.patch({
      runtimeErrorCode: errorCode ?? this.state.runtimeErrorCode,
      linkly: Object.freeze({
        status: result.status,
        errorCode,
        allowedKeys: Object.freeze([...result.allowedKeys]),
      }),
      ...(result.status === "recovery-required"
        ? { phase: "recovery-required" as const }
        : {}),
    });
  }

  private clearVoucher(): void {
    this.voucherCode = "";
    if (this.destroyed) return;
    this.patch({
      voucherCaptured: false,
      sensitiveInputRevision: this.state.sensitiveInputRevision + 1,
    });
  }

  private runExclusive(
    operation: (revision: number) => Promise<boolean>,
    options: Readonly<{ background?: boolean }> = {},
  ): Promise<boolean> {
    if (this.destroyed) return Promise.resolve(false);
    if (this.actionInFlight) return this.actionInFlight;
    const revision = ++this.lifecycleRevision;
    if (options.background) {
      this.patch({ recoveryInFlight: true });
    } else {
      this.patch({ busy: true });
    }
    const pending = Promise.resolve()
      .then(() => operation(revision))
      .catch((error: unknown) => {
        if (this.isCurrent(revision)) {
          this.patch({
            runtimeErrorCode: runtimeErrorCode(error),
          });
        }
        return false;
      })
      .finally(() => {
        if (this.actionInFlight === pending) {
          this.actionInFlight = null;
          if (this.isCurrent(revision)) {
            this.patch(
              options.background
                ? { recoveryInFlight: false }
                : { busy: false },
            );
          }
        }
      });
    this.actionInFlight = pending;
    return pending;
  }

  private patchIfCurrent(
    revision: number,
    patch: Partial<PaymentPresenterState>,
  ): void {
    if (this.isCurrent(revision)) this.patch(patch);
  }

  private isCurrent(revision: number): boolean {
    return !this.destroyed && revision === this.lifecycleRevision;
  }

  private patch(patch: Partial<PaymentPresenterState>): void {
    if (this.destroyed) return;
    this.state = { ...this.state, ...patch };
    this.emit();
  }

  private emit(): void {
    for (const listener of this.listeners) listener();
  }
}

export function parseAudInput(value: string): Money | null {
  const normalized = value.trim();
  if (!/^(?:0|[1-9]\d*)(?:\.\d{1,2})?$/.test(normalized)) {
    return null;
  }
  const [dollars = "", fraction = ""] = normalized.split(".");
  const cents =
    Number(dollars) * 100 + Number(fraction.padEnd(2, "0") || "0");
  if (!Number.isSafeInteger(cents) || cents <= 0) return null;
  return Object.freeze({ currency: "AUD", cents });
}

export function canSelectPaymentMethod(
  state: PaymentPresenterState,
  method: PaymentUiMethod,
): boolean {
  if (state.busy) return false;
  const activeMethods = state.tenders.map((tender) => tender.method);
  if (state.checkout.flow !== "regular") {
    if (!state.allowedActions.start || activeMethods.length > 0) return false;
    return method === "cash"
      ? state.allowedActions.addCash
      : providerAvailable(state.providers, method);
  }
  if (method === "cash") {
    return (
      state.allowedActions.addCash &&
      !activeMethods.includes("cash")
    );
  }
  if (
    !providerAvailable(state.providers, method) ||
    !state.allowedActions.changeProvider ||
    activeMethods.includes(tenderMethodForProvider(method))
  ) {
    return false;
  }
  return state.allowedActions.start;
}

export function canSubmitPaymentMethod(
  state: PaymentPresenterState,
  method: PaymentUiMethod,
): boolean {
  return canSelectPaymentMethod(
    { ...state, busy: false },
    method,
  );
}

function phaseForStatus(status: PaymentCheckoutStatus): PaymentUiPhase {
  switch (status) {
    case "draft-prepared":
      return "draft-prepared";
    case "awaiting-terminal":
      return "awaiting-terminal";
    case "pending":
      return "pending";
    case "unknown":
      return "unknown";
    case "recovery-required":
      return "recovery-required";
    case "partial":
      return "partial";
    case "completed":
      return "success";
    case "declined":
      return "declined";
    case "cancelled":
      return "cancelled";
  }
}

function phaseForSnapshot(
  snapshot: PaymentCheckoutPublicSnapshot,
): PaymentUiPhase {
  if (
    snapshot.status === "recovery-required" &&
    snapshot.errorCode === "ONLINE_REQUIRED" &&
    snapshot.allowedActions.addCash
  ) {
    return "offline-cash";
  }
  return phaseForStatus(snapshot.status);
}

function safeProviderAvailability(
  runtime: PaymentCheckoutRuntimePort,
): readonly PaymentProviderAvailability[] {
  try {
    return Object.freeze(
      runtime.listProviderAvailability().map((entry) =>
        Object.freeze({ ...entry }),
      ),
    );
  } catch {
    return Object.freeze([]);
  }
}

function firstAvailableMethod(
  snapshot: PaymentCheckoutPublicSnapshot,
  providers: readonly PaymentProviderAvailability[],
  activeMethods: readonly TenderMethod[],
): PaymentUiMethod | null {
  if (
    snapshot.allowedActions.addCash &&
    !activeMethods.includes("cash")
  ) {
    return "cash";
  }
  if (!snapshot.allowedActions.start) return null;
  return firstAvailableProvider(providers, activeMethods);
}

/**
 * 支付页面默认支付方式：现金可用时优先现金，否则回退到第一个可用 provider。
 * 判定条件与初始 allowedActions.addCash 共用 isCashAvailable，保持一致；
 * 用户手动选择后由 selectMethod 覆盖，applySnapshot 按快照可选择性保留当前
 * 选择（现金不可选时回退 firstAvailableMethod），不重复执行默认解析。
 */
function resolveInitialMethod(
  providers: readonly PaymentProviderAvailability[],
  dependencies: PaymentPresenterDependencies,
): PaymentUiMethod | null {
  if (isCashAvailable(dependencies)) return "cash";
  return firstAvailableProvider(providers, []) ?? null;
}

/**
 * 现金结账是否可用：存在结账入口且运行时实现 startCash。
 * 默认支付方式与初始 addCash 权限（含无恢复时的 ready 分支）共用同一判定，
 * 避免失配。
 */
function isCashAvailable(dependencies: PaymentPresenterDependencies): boolean {
  return (
    dependencies.entry !== null &&
    typeof dependencies.runtime.startCash === "function"
  );
}

function firstAvailableProvider(
  providers: readonly PaymentProviderAvailability[],
  activeMethods: readonly TenderMethod[],
): PaymentProvider | null {
  return (
    providers.find(
      (entry) =>
        entry.available &&
        !activeMethods.includes(tenderMethodForProvider(entry.provider)),
    )?.provider ?? null
  );
}

function isMethodSelectableFromSnapshot(
  method: PaymentUiMethod,
  snapshot: PaymentCheckoutPublicSnapshot,
  providers: readonly PaymentProviderAvailability[],
  activeMethods: readonly TenderMethod[],
): boolean {
  if (method === "cash") {
    return (
      snapshot.allowedActions.addCash &&
      !activeMethods.includes("cash")
    );
  }
  return (
    snapshot.allowedActions.start &&
    snapshot.allowedActions.changeProvider &&
    providerAvailable(providers, method) &&
    !activeMethods.includes(tenderMethodForProvider(method))
  );
}

function providerAvailable(
  providers: readonly PaymentProviderAvailability[],
  provider: PaymentProvider,
): boolean {
  return providers.some(
    (entry) => entry.provider === provider && entry.available,
  );
}

function tenderMethodForProvider(provider: PaymentProvider): TenderMethod {
  return provider === "voucher" ? "voucher" : "card";
}

function copyTenders(
  tenders: readonly PaymentCheckoutTender[],
): readonly PaymentPresenterTender[] {
  return Object.freeze(
    tenders.map((tender) =>
      Object.freeze({
        tenderGuid: tender.tenderGuid,
        method: tender.method,
        amount: copyMoney(tender.amount),
        reversible: tender.reversible,
      }),
    ),
  );
}

function copyTenderReversalRecovery(
  recovery: PaymentCheckoutTenderReversalRecovery | undefined,
): PaymentCheckoutTenderReversalRecovery | null {
  if (!recovery) return null;
  return Object.freeze({
    tenderGuid: recovery.tenderGuid,
    status: recovery.status,
  });
}

function copyMoney(value: Money): Money {
  return Object.freeze({
    currency: value.currency,
    cents: value.cents,
  });
}

function centsToAmountInput(cents: number): string {
  return (cents / 100).toFixed(2);
}

function emptyLinklyState(): PaymentPresenterState["linkly"] {
  return Object.freeze({
    status: null,
    errorCode: null,
    allowedKeys: Object.freeze([]),
  });
}

function linklyStateForSnapshot(
  snapshot: PaymentCheckoutPublicSnapshot,
  previous: PaymentPresenterState["linkly"],
): PaymentPresenterState["linkly"] {
  if (snapshot.provider !== "linkly-cloud" || !snapshot.attemptId) {
    return emptyLinklyState();
  }
  if (
    snapshot.status === "awaiting-terminal" ||
    snapshot.status === "pending"
  ) {
    return Object.freeze({
      status: "in-progress",
      errorCode: null,
      // 初次响应尚无 allowedKeys；仅展示受枚举约束的安全按键，运行时会再次校验。
      allowedKeys:
        previous.status === "in-progress" && previous.allowedKeys.length > 0
          ? previous.allowedKeys
          : LINKLY_SAFE_OPERATOR_KEYS,
    });
  }
  if (snapshot.status === "unknown") {
    return Object.freeze({
      status: "recovery-required",
      errorCode: "LINKLY_UNKNOWN_REQUIRES_RECOVERY",
      allowedKeys: Object.freeze([]),
    });
  }
  if (snapshot.status === "cancelled") {
    return Object.freeze({
      status: "cancelled",
      errorCode: null,
      allowedKeys: Object.freeze([]),
    });
  }
  if (snapshot.status === "completed" || snapshot.status === "partial") {
    return Object.freeze({
      status: "completed",
      errorCode: null,
      allowedKeys: Object.freeze([]),
    });
  }
  return previous;
}

function runtimeErrorCode(error: unknown): PaymentUiRuntimeErrorCode {
  if (error instanceof PaymentCheckoutRuntimeError) return error.code;
  return "PAYMENT_CHECKOUT_FAILED";
}

function stableLinklyError(
  value: string | null,
): LinklyOperatorErrorCode | null {
  if (value === null) return null;
  return LINKLY_OPERATOR_ERRORS.has(value as LinklyOperatorErrorCode)
    ? (value as LinklyOperatorErrorCode)
    : null;
}

const LINKLY_OPERATOR_ERRORS = new Set<LinklyOperatorErrorCode>([
  "LINKLY_UNKNOWN_REQUIRES_RECOVERY",
  "LINKLY_OPERATOR_STATE_INVALID",
  "LINKLY_OPERATOR_KEY_NOT_ALLOWED",
]);
