
import {
  resolveInstallmentsAccess,
  type InstallmentsAccess,
} from "./installment-authorization";
import type {
  InstallmentDetails,
  InstallmentPaymentMethod,
} from "./installment-models";

import type { InstallmentStatus, InstallmentSummary } from "@/core/contracts";

export const INSTALLMENT_MINIMUM_TOTAL_CENTS = 5_000;
export const INSTALLMENT_MINIMUM_DOWN_PAYMENT_CENTS = 2_000;

export type InstallmentCreateDraftLine = Readonly<{
  lineKey: string;
  displayName: string;
  quantity: string;
  actualAmountCents: number;
}>;

export type InstallmentCreateDraft = Readonly<{
  revision: number;
  totalCents: number;
  lines: readonly InstallmentCreateDraftLine[];
}>;

export interface InstallmentCreateDraftPort {
  getSnapshot(): InstallmentCreateDraft | null;
  subscribe(listener: () => void): () => void;
}

export type InstallmentWorkflowCreateInput = Readonly<{
  draftRevision: number;
  customerName: string;
  customerPhone: string;
  note: string | null;
  downPaymentCents: number;
  method: InstallmentPaymentMethod;
  voucherReference: string | null;
  voucherReservationToken: null;
}>;

export type InstallmentWorkflowRepaymentInput = Readonly<{
  installmentGuid: string;
  amountCents: number;
  method: InstallmentPaymentMethod;
  voucherReference: string | null;
  voucherReservationToken: null;
}>;

/**
 * 组合根实现此 Port，并在每次写操作时重新复核在线状态、设备/收银员 lease、
 * 活动购物车 revision、支付 attempt 与 Unknown 恢复；Presenter 的检查只负责 UX。
 */
export interface InstallmentWorkflowPort {
  list(input: Readonly<{
    keyword: string | null;
    online: boolean;
    status: InstallmentStatus | null;
    take: 100;
  }>): Promise<readonly InstallmentSummary[]>;
  getDetails(input: Readonly<{
    installmentGuid: string;
    online: boolean;
  }>): Promise<InstallmentDetails | null>;
  recoverBlocking(): Promise<InstallmentDetails>;
  create(input: InstallmentWorkflowCreateInput): Promise<InstallmentDetails>;
  addRepayment(
    input: InstallmentWorkflowRepaymentInput,
  ): Promise<InstallmentDetails>;
  cancelWithRefund(input: Readonly<{
    installmentGuid: string;
    reason: string | null;
  }>): Promise<InstallmentDetails>;
  void(input: Readonly<{
    installmentGuid: string;
    reason: string;
  }>): Promise<InstallmentDetails>;
  confirmPickup(input: Readonly<{
    installmentGuid: string;
    note: string | null;
  }>): Promise<InstallmentDetails>;
}

export type InstallmentWorkflowErrorCode =
  | "authorization-declined"
  | "conflict"
  | "online-required"
  | "payment-recovery-required";

export class InstallmentWorkflowError extends Error {
  public constructor(
    public readonly code: InstallmentWorkflowErrorCode,
    message: string,
  ) {
    super(message);
    this.name = "InstallmentWorkflowError";
  }
}

export type InstallmentStatusCode =
  | "action-failed"
  | "authorization-declined"
  | "cancel-complete"
  | "conflict"
  | "create-complete"
  | "details-failed"
  | "details-unavailable"
  | "history-failed"
  | "invalid-create"
  | "invalid-repayment"
  | "online-required"
  | "payment-recovery-required"
  | "permission-required"
  | "pickup-complete"
  | "repayment-complete"
  | "recovery-complete"
  | "void-complete";

export type InstallmentPresenterState = Readonly<{
  access: InstallmentsAccess;
  busy: boolean;
  createDownPayment: string;
  createDraft: InstallmentCreateDraft | null;
  createNote: string;
  createPaymentMethod: InstallmentPaymentMethod;
  createVoucherReference: string;
  customerName: string;
  customerPhone: string;
  details: InstallmentDetails | null;
  detailsLoading: boolean;
  kind: "idle" | "loading" | "ready" | "unauthorized" | "failed";
  online: boolean;
  orders: readonly InstallmentSummary[];
  pane: "history" | "create";
  pickupNote: string;
  query: string;
  recoveryRequired: boolean;
  repaymentAmount: string;
  repaymentMethod: InstallmentPaymentMethod;
  repaymentVoucherReference: string;
  selectedGuid: string | null;
  statusCode: InstallmentStatusCode | null;
  statusFilter: InstallmentStatus | null;
  cancelReason: string;
  voidReason: string;
}>;

export type InstallmentPresenterOptions = Readonly<{
  createDrafts: InstallmentCreateDraftPort;
  initialOnline: boolean;
  permissions: readonly string[];
  workflow: InstallmentWorkflowPort;
}>;

export class InstallmentPresenter {
  private readonly listeners = new Set<() => void>();
  private readonly workflow: InstallmentWorkflowPort;
  private readonly unsubscribeDrafts: () => void;
  private state: InstallmentPresenterState;
  private destroyed = false;
  private loadGeneration = 0;
  private detailGeneration = 0;
  private actionInFlight: Promise<void> | null = null;

  public constructor(private readonly options: InstallmentPresenterOptions) {
    this.workflow = options.workflow;
    const createDraft = readDraft(options.createDrafts);
    this.state = {
      access: resolveInstallmentsAccess(options.permissions),
      busy: false,
      createDownPayment: defaultDownPayment(createDraft),
      createDraft,
      createNote: "",
      createPaymentMethod: "cash",
      createVoucherReference: "",
      customerName: "",
      customerPhone: "",
      details: null,
      detailsLoading: false,
      kind: "idle",
      online: options.initialOnline,
      orders: [],
      pane: "history",
      pickupNote: "",
      query: "",
      recoveryRequired: false,
      repaymentAmount: "",
      repaymentMethod: "cash",
      repaymentVoucherReference: "",
      selectedGuid: null,
      statusCode: null,
      statusFilter: null,
      cancelReason: "",
      voidReason: "",
    };
    this.unsubscribeDrafts = options.createDrafts.subscribe(() => {
      if (this.destroyed) return;
      const nextDraft = readDraft(options.createDrafts);
      const currentDownPayment = parseAud(this.state.createDownPayment);
      this.patch({
        createDraft: nextDraft,
        createDownPayment:
          currentDownPayment === null ||
          !nextDraft ||
          currentDownPayment > nextDraft.totalCents
            ? defaultDownPayment(nextDraft)
            : this.state.createDownPayment,
      });
    });
  }

  public readonly getState = (): InstallmentPresenterState => this.state;

  public readonly subscribe = (listener: () => void): (() => void) => {
    if (this.destroyed) return () => undefined;
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  };

  public destroy(): void {
    if (this.destroyed) return;
    this.destroyed = true;
    this.loadGeneration += 1;
    this.detailGeneration += 1;
    this.unsubscribeDrafts();
    this.listeners.clear();
  }

  public setOnline(online: boolean): void {
    if (this.destroyed || this.state.online === online) return;
    this.patch({ online });
  }

  public setSearchQuery(query: string): void {
    if (this.destroyed) return;
    this.patch({
      query: query.slice(0, 120),
      statusCode: null,
    });
  }

  public setStatusFilter(status: InstallmentStatus | null): void {
    if (this.destroyed) return;
    this.patch({ statusFilter: status, statusCode: null });
  }

  public showHistory(): void {
    if (this.destroyed) return;
    this.patch({ pane: "history", statusCode: null });
  }

  public showCreate(): void {
    if (this.destroyed) return;
    if (!this.state.access.canCreate) {
      this.patch({ statusCode: "permission-required" });
      return;
    }
    this.patch({ pane: "create", statusCode: null });
  }

  public setCustomerName(value: string): void {
    this.patchText("customerName", value, 256);
  }

  public setCustomerPhone(value: string): void {
    this.patchText("customerPhone", value, 128);
  }

  public setCreateNote(value: string): void {
    this.patchText("createNote", value, 2_000);
  }

  public setCreateDownPayment(value: string): void {
    this.patchText("createDownPayment", value, 32);
  }

  public setCreatePaymentMethod(method: InstallmentPaymentMethod): void {
    if (this.destroyed) return;
    this.patch({
      createPaymentMethod: method,
      createVoucherReference:
        method === "voucher" ? this.state.createVoucherReference : "",
      statusCode: null,
    });
  }

  public setCreateVoucherReference(value: string): void {
    this.patchText("createVoucherReference", value, 512);
  }

  public setRepaymentAmount(value: string): void {
    this.patchText("repaymentAmount", value, 32);
  }

  public setRepaymentMethod(method: InstallmentPaymentMethod): void {
    if (this.destroyed) return;
    this.patch({
      repaymentMethod: method,
      repaymentVoucherReference:
        method === "voucher"
          ? this.state.repaymentVoucherReference
          : "",
      statusCode: null,
    });
  }

  public setRepaymentVoucherReference(value: string): void {
    this.patchText("repaymentVoucherReference", value, 512);
  }

  public setCancelReason(value: string): void {
    this.patchText("cancelReason", value, 1_000);
  }

  public setVoidReason(value: string): void {
    this.patchText("voidReason", value, 1_000);
  }

  public setPickupNote(value: string): void {
    this.patchText("pickupNote", value, 1_000);
  }

  public async load(): Promise<void> {
    if (this.destroyed) return;
    if (!this.state.access.canView) {
      this.patch({
        kind: "unauthorized",
        orders: [],
        statusCode: "permission-required",
      });
      return;
    }
    const generation = ++this.loadGeneration;
    const input = {
      keyword: optionalTrimmed(this.state.query),
      online: this.state.online,
      status: this.state.statusFilter,
      take: 100 as const,
    };
    this.patch({ kind: "loading", statusCode: null });
    try {
      const orders = await this.workflow.list(input);
      if (!this.isCurrentLoad(generation)) return;
      this.patch({
        kind: "ready",
        orders: Object.freeze([...orders]),
      });
    } catch (error) {
      if (!this.isCurrentLoad(generation)) return;
      const failureCode = workflowFailureCode(error);
      const recoveryRequired =
        failureCode === "payment-recovery-required";
      this.patch({
        kind: this.state.orders.length > 0 ? "ready" : "failed",
        recoveryRequired:
          this.state.recoveryRequired || recoveryRequired,
        statusCode: recoveryRequired
          ? "payment-recovery-required"
          : "history-failed",
      });
    }
  }

  public async select(installmentGuid: string): Promise<void> {
    if (this.destroyed || !this.state.access.canView) return;
    const generation = ++this.detailGeneration;
    this.patch({
      details: null,
      detailsLoading: true,
      selectedGuid: installmentGuid,
      statusCode: null,
    });
    try {
      const details = await this.workflow.getDetails({
        installmentGuid,
        online: this.state.online,
      });
      if (!this.isCurrentDetail(generation, installmentGuid)) return;
      this.patch({
        details,
        detailsLoading: false,
        statusCode: details ? null : "details-unavailable",
      });
    } catch {
      if (!this.isCurrentDetail(generation, installmentGuid)) return;
      this.patch({
        details: null,
        detailsLoading: false,
        statusCode: "details-failed",
      });
    }
  }

  public create(): Promise<void> {
    const guard = this.guardWrite(this.state.access.canCreate);
    if (guard) return guard;
    const draft = this.state.createDraft;
    const downPaymentCents = parseAud(this.state.createDownPayment);
    const customerName = requiredTrimmed(this.state.customerName);
    const customerPhone = requiredTrimmed(this.state.customerPhone);
    const voucherReference = optionalTrimmed(
      this.state.createVoucherReference,
    );
    if (
      !draft ||
      draft.lines.length === 0 ||
      draft.totalCents < INSTALLMENT_MINIMUM_TOTAL_CENTS ||
      downPaymentCents === null ||
      downPaymentCents < INSTALLMENT_MINIMUM_DOWN_PAYMENT_CENTS ||
      downPaymentCents > draft.totalCents ||
      !customerName ||
      !customerPhone ||
      (this.state.createPaymentMethod === "voucher" && !voucherReference)
    ) {
      this.patch({ statusCode: "invalid-create" });
      return Promise.resolve();
    }
    const input: InstallmentWorkflowCreateInput = {
      customerName,
      customerPhone,
      downPaymentCents,
      draftRevision: draft.revision,
      method: this.state.createPaymentMethod,
      note: optionalTrimmed(this.state.createNote),
      voucherReference:
        this.state.createPaymentMethod === "voucher"
          ? voucherReference
          : null,
      voucherReservationToken: null,
    };
    // 中文注释：券码只在本次调用闭包中短暂保留；workflow 开始前先从公开状态清除，
    // 即使在线 query/lock 失败也不得恢复到可观察 state。
    this.patch({ createVoucherReference: "" });
    return this.runAction(
      () => this.workflow.create(input),
      "create-complete",
      true,
    );
  }

  public recoverBlocking(): Promise<void> {
    if (this.destroyed) return Promise.resolve();
    if (!this.state.online) {
      this.patch({ statusCode: "online-required" });
      return Promise.resolve();
    }
    return this.runAction(
      () => this.workflow.recoverBlocking(),
      "recovery-complete",
    );
  }

  public addRepayment(): Promise<void> {
    const guard = this.guardWrite(
      this.state.access.canAddRepayment,
    );
    if (guard) return guard;
    const details = this.state.details;
    const amountCents = parseAud(this.state.repaymentAmount);
    const voucherReference = optionalTrimmed(
      this.state.repaymentVoucherReference,
    );
    if (
      !details ||
      details.status !== "Active" ||
      details.balanceCents <= 0 ||
      amountCents === null ||
      amountCents <= 0 ||
      amountCents > details.balanceCents ||
      (this.state.repaymentMethod === "voucher" && !voucherReference)
    ) {
      this.patch({ statusCode: "invalid-repayment" });
      return Promise.resolve();
    }
    const input: InstallmentWorkflowRepaymentInput = {
      amountCents,
      installmentGuid: details.installmentGuid,
      method: this.state.repaymentMethod,
      voucherReference:
        this.state.repaymentMethod === "voucher"
          ? voucherReference
          : null,
      voucherReservationToken: null,
    };
    this.patch({ repaymentVoucherReference: "" });
    return this.runAction(
      () => this.workflow.addRepayment(input),
      "repayment-complete",
    );
  }

  public cancelWithRefund(): Promise<void> {
    const guard = this.guardWrite(this.state.access.canCancel);
    if (guard) return guard;
    const details = this.state.details;
    if (
      !details ||
      details.status !== "Active" ||
      details.balanceCents <= 0
    ) {
      this.patch({ statusCode: "action-failed" });
      return Promise.resolve();
    }
    return this.runAction(
      () =>
        this.workflow.cancelWithRefund({
          installmentGuid: details.installmentGuid,
          reason: optionalTrimmed(this.state.cancelReason),
        }),
      "cancel-complete",
    );
  }

  public voidSelected(): Promise<void> {
    const guard = this.guardWrite(this.state.access.canCancel);
    if (guard) return guard;
    const details = this.state.details;
    if (
      !details ||
      details.status !== "Active" ||
      details.balanceCents <= 0
    ) {
      this.patch({ statusCode: "action-failed" });
      return Promise.resolve();
    }
    return this.runAction(
      () =>
        this.workflow.void({
          installmentGuid: details.installmentGuid,
          reason:
            optionalTrimmed(this.state.voidReason) ?? "作废分期单",
        }),
      "void-complete",
    );
  }

  public confirmPickup(): Promise<void> {
    const guard = this.guardWrite(
      this.state.access.canConfirmPickup,
    );
    if (guard) return guard;
    const details = this.state.details;
    if (!details || details.status !== "PaidOff") {
      this.patch({ statusCode: "action-failed" });
      return Promise.resolve();
    }
    return this.runAction(
      () =>
        this.workflow.confirmPickup({
          installmentGuid: details.installmentGuid,
          note: optionalTrimmed(this.state.pickupNote),
        }),
      "pickup-complete",
    );
  }

  private guardWrite(hasPermission: boolean): Promise<void> | null {
    if (this.destroyed) return Promise.resolve();
    if (!hasPermission) {
      this.patch({ statusCode: "permission-required" });
      return Promise.resolve();
    }
    if (!this.state.online) {
      this.patch({ statusCode: "online-required" });
      return Promise.resolve();
    }
    if (this.state.recoveryRequired) {
      this.patch({ statusCode: "payment-recovery-required" });
      return Promise.resolve();
    }
    if (this.actionInFlight) return this.actionInFlight;
    return null;
  }

  private runAction(
    operation: () => Promise<InstallmentDetails>,
    successCode: InstallmentStatusCode,
    returnToHistory = false,
  ): Promise<void> {
    if (this.actionInFlight) return this.actionInFlight;
    let running!: Promise<void>;
    running = (async () => {
      this.patch({ busy: true, statusCode: null });
      try {
        const details = await operation();
        if (this.destroyed) return;
        const orders = upsertSummary(this.state.orders, details);
        this.patch({
          busy: false,
          details,
          kind: "ready",
          orders,
          pane: returnToHistory ? "history" : this.state.pane,
          recoveryRequired: false,
          selectedGuid: details.installmentGuid,
          statusCode: successCode,
        });
      } catch (error) {
        if (this.destroyed) return;
        const statusCode = workflowFailureCode(error);
        this.patch({
          busy: false,
          recoveryRequired:
            this.state.recoveryRequired ||
            statusCode === "payment-recovery-required",
          statusCode,
        });
      } finally {
        if (this.actionInFlight === running) {
          this.actionInFlight = null;
        }
      }
    })();
    this.actionInFlight = running;
    return running;
  }

  private patchText<
    K extends
      | "cancelReason"
      | "createDownPayment"
      | "createNote"
      | "createVoucherReference"
      | "customerName"
      | "customerPhone"
      | "pickupNote"
      | "repaymentAmount"
      | "repaymentVoucherReference"
      | "voidReason",
  >(key: K, value: string, maxLength: number): void {
    if (this.destroyed) return;
    this.patch({
      [key]: value.slice(0, maxLength),
      statusCode: null,
    } as Pick<InstallmentPresenterState, K | "statusCode">);
  }

  private isCurrentLoad(generation: number): boolean {
    return !this.destroyed && generation === this.loadGeneration;
  }

  private isCurrentDetail(
    generation: number,
    installmentGuid: string,
  ): boolean {
    return (
      !this.destroyed &&
      generation === this.detailGeneration &&
      this.state.selectedGuid === installmentGuid
    );
  }

  private patch(
    patch: Partial<InstallmentPresenterState>,
  ): void {
    if (this.destroyed) return;
    this.state = Object.freeze({ ...this.state, ...patch });
    for (const listener of this.listeners) listener();
  }
}

function readDraft(
  port: InstallmentCreateDraftPort,
): InstallmentCreateDraft | null {
  try {
    const draft = port.getSnapshot();
    if (
      !draft ||
      !Number.isSafeInteger(draft.revision) ||
      draft.revision < 0 ||
      !Number.isSafeInteger(draft.totalCents) ||
      draft.totalCents < 0 ||
      !Array.isArray(draft.lines)
    ) {
      return null;
    }
    const lines = draft.lines.map((line) => {
      if (
        typeof line.lineKey !== "string" ||
        line.lineKey.length === 0 ||
        typeof line.displayName !== "string" ||
        line.displayName.length === 0 ||
        typeof line.quantity !== "string" ||
        line.quantity.length === 0 ||
        !Number.isSafeInteger(line.actualAmountCents) ||
        line.actualAmountCents < 0
      ) {
        throw new Error("Invalid installment draft line.");
      }
      return Object.freeze({ ...line });
    });
    return Object.freeze({
      revision: draft.revision,
      totalCents: draft.totalCents,
      lines: Object.freeze(lines),
    });
  } catch {
    return null;
  }
}

function defaultDownPayment(
  draft: InstallmentCreateDraft | null,
): string {
  if (!draft || draft.totalCents <= 0) return "";
  return formatInputCents(
    Math.min(
      draft.totalCents,
      INSTALLMENT_MINIMUM_DOWN_PAYMENT_CENTS,
    ),
  );
}

function parseAud(value: string): number | null {
  const normalized = value.trim();
  if (!/^(?:0|[1-9]\d*)(?:\.\d{1,2})?$/u.test(normalized)) {
    return null;
  }
  const [dollars, decimals = ""] = normalized.split(".");
  const cents = Number(dollars) * 100 + Number(decimals.padEnd(2, "0"));
  return Number.isSafeInteger(cents) ? cents : null;
}

function formatInputCents(cents: number): string {
  return `${Math.floor(cents / 100)}.${String(cents % 100).padStart(2, "0")}`;
}

function optionalTrimmed(value: string): string | null {
  const normalized = value.trim();
  return normalized.length === 0 ? null : normalized;
}

function requiredTrimmed(value: string): string | null {
  return optionalTrimmed(value);
}

function workflowFailureCode(error: unknown): InstallmentStatusCode {
  if (error instanceof InstallmentWorkflowError) {
    if (error.code === "authorization-declined") {
      return "authorization-declined";
    }
    if (error.code === "conflict") return "conflict";
    if (error.code === "online-required") return "online-required";
    return "payment-recovery-required";
  }
  return "action-failed";
}

function upsertSummary(
  orders: readonly InstallmentSummary[],
  details: InstallmentDetails,
): readonly InstallmentSummary[] {
  const summary = summaryFromDetails(details);
  const index = orders.findIndex(
    (order) => order.installmentGuid === details.installmentGuid,
  );
  if (index < 0) return Object.freeze([summary, ...orders]);
  const next = [...orders];
  next[index] = summary;
  return Object.freeze(next);
}

function summaryFromDetails(
  details: InstallmentDetails,
): InstallmentSummary {
  return Object.freeze({
    installmentGuid: details.installmentGuid,
    installmentNumber: details.installmentNumber,
    storeCode: details.storeCode,
    deviceCode: details.deviceCode,
    cashierName: details.cashierName,
    customerName: details.customerName,
    customerPhone: details.customerPhone,
    createdAtIso: details.createdAtIso,
    totalCents: details.totalCents,
    downPaymentCents: details.downPaymentCents,
    paidCents: details.paidCents,
    balanceCents: details.balanceCents,
    status: details.status,
    updatedAtIso: details.updatedAtIso,
  });
}
