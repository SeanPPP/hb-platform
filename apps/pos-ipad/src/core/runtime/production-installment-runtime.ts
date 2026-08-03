import {
  CurrentCashierSession,
  type TrustedCashierLease,
  type TrustedCashierSession,
} from "./current-cashier-session";
import type { InstallmentVoucherIntentVaultPort } from "./production-installment-payment-adapter";

import { HbposApiError } from "@/core/api/hbpos-api";
import type {
  InstallmentSnapshot,
  InstallmentStatus,
  InstallmentSummary,
} from "@/core/contracts";
import type { CartSnapshot } from "@/core/contracts/cart";
import { INSTALLMENT_SENSITIVE_PAYLOAD_REVISION } from "@/core/db/sqlite-installment-snapshot-repository";
import {
  INSTALLMENTS_ADD_REPAYMENT_PERMISSION,
  INSTALLMENTS_CANCEL_PERMISSION,
  INSTALLMENTS_CONFIRM_PICKUP_PERMISSION,
  INSTALLMENTS_CREATE_PERMISSION,
  INSTALLMENTS_VIEW_PERMISSION,
} from "@/features/installments/installment-authorization";
import { InstallmentCheckoutPresenter } from "@/features/installments/installment-checkout-presenter";
import type {
  InstallmentAppendPaymentCommand,
  InstallmentCancelCommand,
  InstallmentCreateCommand,
  InstallmentDetails,
  InstallmentLine,
  InstallmentPaymentCommand,
  InstallmentPaymentMethod,
  InstallmentCardProvider,
  InstallmentPickupCommand,
  InstallmentRefundCommand,
  InstallmentsRemotePort,
  InstallmentVoidCommand,
} from "@/features/installments/installment-models";
import {
  InstallmentPresenter,
  InstallmentWorkflowError,
  type InstallmentCreateDraft,
  type InstallmentCreateDraftPort,
  type InstallmentWorkflowCreateInput,
  type InstallmentWorkflowPort,
  type InstallmentWorkflowRepaymentInput,
} from "@/features/installments/installment-presenter";
import type { InstallmentsRuntimeFactory } from "@/features/installments/installment-runtime";
import { PAYMENT_PERMISSION } from "@/features/payments/runtime/payment-checkout-runtime";
import {
  installmentCreatePaymentEntry,
  type InstallmentCreatePaymentEntry,
  type InstallmentRepaymentPaymentEntry,
} from "@/features/payments/ui/unified-payment-entry";
import type {
  ActivePricingCartSession,
  ActivePricingCartLease,
  ActivePricingCartSessionSnapshot,
} from "@/features/sales/runtime";

type TerminalScope = Readonly<{ storeCode: string; deviceCode: string }>;

type CartPort = Pick<
  ActivePricingCartSession,
  "read" | "runExclusive" | "subscribe"
>;

type VoucherIntentStageInput = Parameters<
  InstallmentVoucherIntentVaultPort["stage"]
>[0];

type InstallmentActionCandidate = Readonly<{
  persisted: PersistedInstallmentAction;
  voucherIntent: VoucherIntentStageInput | null;
}>;

/** 运行时安全边界：只有组合根才能读取连通性真相。 */
export interface InstallmentConnectivityPort {
  isOnline(): Promise<boolean>;
}

/** SQLCipher 快照 port 只缓存浏览投影，不提供任何离线业务写入。 */
export interface InstallmentSnapshotCachePort {
  /**
   * 事务 upsert 当前页或单条 mutation 投影，绝不删除本次未返回的历史。
   * 只有未来带服务端完整版本的全量分页协议才允许另设 replace。
   */
  upsertForStore(
    storeCode: string,
    snapshots: readonly InstallmentSnapshot[],
  ): Promise<void>;
  listForStore(
    storeCode: string,
    limit: number,
    offset: number,
  ): Promise<readonly InstallmentSnapshot[]>;
}

export type InstallmentPaymentAction = Readonly<{
  actionId: string;
  idempotencyKey: string;
  kind: "create" | "repayment" | "cancel-refund";
  installmentGuid: string;
  paymentGuid: string | null;
  method: InstallmentPaymentMethod | null;
  amountCents: number | null;
}>;

export type InstallmentActionState =
  | "Created"
  | "ProviderPending"
  | "Unknown"
  | "Approved"
  | "BackendPending";

type InstallmentCreateActionCommand = Omit<
  InstallmentCreateCommand,
  "downPayment"
> &
  Readonly<{
    kind: "create";
    cartFingerprint: string;
    draftRevision: number;
    cardProvider?: InstallmentCardProvider;
    cashTenderedCents?: number;
  }>;

type InstallmentRepaymentActionCommand = Omit<
  InstallmentAppendPaymentCommand,
  "payment"
> &
  Readonly<{
    kind: "repayment";
    cardProvider?: InstallmentCardProvider;
    cashTenderedCents?: number;
  }>;

type InstallmentCancelActionCommand = Omit<
  InstallmentCancelCommand,
  "refunds"
> &
  Readonly<{ kind: "cancel-refund" }>;

export type InstallmentActionCommand =
  | Readonly<InstallmentCreateActionCommand>
  | Readonly<InstallmentRepaymentActionCommand>
  | Readonly<InstallmentCancelActionCommand>;

export type PersistedInstallmentAction = Readonly<{
  action: InstallmentPaymentAction;
  command: InstallmentActionCommand;
  deviceCode: string;
  intentFingerprint: string;
  state: InstallmentActionState;
  storeCode: string;
}>;

export interface InstallmentActionStorePort {
  loadBlocking(
    terminal: TerminalScope,
  ): Promise<PersistedInstallmentAction | null>;
  /**
   * 必须在单一事务内执行 terminal-scope compare-and-insert；
   * 若已有 blocking action，则返回旧 action，禁止插入 candidate。
   */
  createIfNone(
    candidate: PersistedInstallmentAction,
  ): Promise<Readonly<{
    created: boolean;
    action: PersistedInstallmentAction;
  }>>;
  transition(input: Readonly<{
    actionId: string;
    expectedState: InstallmentActionState;
    nextState: InstallmentActionState;
    terminal: TerminalScope;
  }>): Promise<PersistedInstallmentAction>;
  decline(input: Readonly<{
    actionId: string;
    expectedState: "ProviderPending" | "Unknown";
    terminal: TerminalScope;
  }>): Promise<void>;
  complete(input: Readonly<{
    actionId: string;
    expectedState: "BackendPending";
    terminal: TerminalScope;
  }>): Promise<void>;
}

export type InstallmentApprovedRefund = Readonly<{
  refund: InstallmentRefundCommand;
  originalTenderEvidenceId: string;
  refundAttemptId: string;
  sourceAttemptId: string;
  sourcePaymentGuid: string;
}>;

/**
 * 该 port 是支付 provider 的唯一入口。它持有耐久 attempt、受保护卡引用和券 token；
 * action 已由 InstallmentActionStorePort 持久化，因此这里只接受 persistedActionId。
 */
export interface InstallmentMutationPaymentPort {
  listProviderAvailability?(): Promise<
    readonly import("@/features/payments/runtime/payment-provider-registry").PaymentProviderAvailability[]
  >;
  beginOrRecover(
    persistedActionId: string,
  ): Promise<
    | Readonly<{ kind: "approved"; payment: InstallmentPaymentCommand }>
    | Readonly<{
        kind: "approved";
        refunds: readonly InstallmentApprovedRefund[];
      }>
    | Readonly<{ kind: "declined" }>
    | Readonly<{ kind: "unknown" }>
  >;
  recoverBlocking(
    persistedActionId: string,
  ): Promise<
    | Readonly<{ kind: "approved"; payment: InstallmentPaymentCommand }>
    | Readonly<{
        kind: "approved";
        refunds: readonly InstallmentApprovedRefund[];
      }>
    | Readonly<{ kind: "declined" }>
    | Readonly<{ kind: "unknown" }>
  >;
}

export type ProductionInstallmentRuntimeDependencies = Readonly<{
  currentCashier: CurrentCashierSession;
  terminal: TerminalScope;
  activeCart: CartPort;
  connectivity: InstallmentConnectivityPort;
  api: InstallmentsRemotePort;
  snapshotCache: InstallmentSnapshotCachePort;
  actionStore: InstallmentActionStorePort;
  payments: InstallmentMutationPaymentPort;
  voucherIntents: InstallmentVoucherIntentVaultPort;
  sha256Hex(material: string): Promise<string>;
  createId(): string;
  nowIso(): string;
}>;

/**
 * 生产组合根只暴露零参数 presenter factory。可信身份、门店、支付 attempt、缓存和
 * 独占购物车 lease 均封装在闭包内，route 不能伪造任一输入。
 */
export function createProductionInstallmentRuntime(
  input: ProductionInstallmentRuntimeDependencies,
): InstallmentsRuntimeFactory {
  const terminal = normalizeTerminal(input.terminal);

  return Object.freeze({
    createPresenter(): InstallmentPresenter {
      const lease = input.currentCashier.createLease();
      const session = requireScopedLease(lease, terminal);
      const workflow = new LeaseBoundInstallmentWorkflow({
        input,
        lease,
        terminal,
      });
      return new InstallmentPresenter({
        createDrafts: new ActiveCartInstallmentDraftPort(input.activeCart),
        // 实际请求仍会调用 connectivity；这个初值仅避免 route 注入不可信状态。
        initialOnline: true,
        permissions: session.permissionCodes,
        workflow,
      });
    },
    prepareCreateCheckout(): InstallmentCreatePaymentEntry {
      const session = requireScopedLease(
        input.currentCashier.createLease(),
        terminal,
      );
      requirePermission(session, INSTALLMENTS_CREATE_PERMISSION);
      const draft = createDraft(input.activeCart.read());
      if (!draft || draft.lines.length === 0) {
        throw workflowError("conflict", "Installment cart is empty.");
      }
      return installmentCreatePaymentEntry({
        checkoutIntentId: runtimeId(input),
        expectedCartRevision: draft.revision,
      });
    },
    createCheckoutPresenter(
      entry: InstallmentCreatePaymentEntry | InstallmentRepaymentPaymentEntry | null,
    ): InstallmentCheckoutPresenter {
      const lease = input.currentCashier.createLease();
      const session = requireScopedLease(lease, terminal);
      const workflow = new LeaseBoundInstallmentWorkflow({
        input,
        lease,
        terminal,
      });
      return new InstallmentCheckoutPresenter({
        entry,
        createDrafts: new ActiveCartInstallmentDraftPort(input.activeCart),
        initialOnline: true,
        permissions: session.permissionCodes,
        workflow,
        createTenderId: input.createId,
      });
    },
    async hasRecoveryRequired(): Promise<boolean> {
      const lease = input.currentCashier.createLease();
      requireScopedLease(lease, terminal);
      const blocking = await input.actionStore.loadBlocking(terminal);
      requireScopedLease(lease, terminal);
      return blocking !== null;
    },
  });
}

class ActiveCartInstallmentDraftPort implements InstallmentCreateDraftPort {
  public constructor(private readonly activeCart: CartPort) {}

  public getSnapshot(): InstallmentCreateDraft | null {
    return createDraft(this.activeCart.read());
  }

  public subscribe(listener: () => void): () => void {
    return this.activeCart.subscribe(listener);
  }
}

class LeaseBoundInstallmentWorkflow implements InstallmentWorkflowPort {
  public constructor(
    private readonly context: Readonly<{
      input: ProductionInstallmentRuntimeDependencies;
      lease: TrustedCashierLease;
      terminal: TerminalScope;
    }>,
  ) {}

  public async listPaymentProviderAvailability() {
    requireScopedLease(this.context.lease, this.context.terminal);
    const availability =
      await this.context.input.payments.listProviderAvailability?.();
    requireScopedLease(this.context.lease, this.context.terminal);
    return Object.freeze([...(availability ?? [])]);
  }

  public async list(input: Readonly<{
    keyword: string | null;
    online: boolean;
    status: InstallmentStatus | null;
    take: 100;
  }>): Promise<readonly InstallmentSummary[]> {
    const session = requireScopedLease(
      this.context.lease,
      this.context.terminal,
    );
    requirePermission(session, INSTALLMENTS_VIEW_PERMISSION);
    const online = input.online && (await this.context.input.connectivity.isOnline());
    requireScopedLease(this.context.lease, this.context.terminal);

    if (!online) {
      const cached = await this.context.input.snapshotCache.listForStore(
        this.context.terminal.storeCode,
        input.take,
        0,
      );
      requireScopedLease(this.context.lease, this.context.terminal);
      return filterCached(cached, input);
    }

    try {
      const blocking = await this.loadBlockingAction();
      if (blocking) {
        await this.recoverPersistedAction(blocking);
      }
      const orders = await this.context.input.api.list({
        keyword: input.keyword,
        status: input.status,
        take: input.take,
      });
      requireScopedLease(this.context.lease, this.context.terminal);
      // 中文注释：筛选/分页响应不是全量快照，只能增量写入，不能删除未返回的历史。
      await this.context.input.snapshotCache.upsertForStore(
        this.context.terminal.storeCode,
        orders.map(toSnapshot),
      );
      requireScopedLease(this.context.lease, this.context.terminal);
      return Object.freeze([...orders]);
    } catch (error) {
      throw mapRemoteError(error);
    }
  }

  public async getDetails(input: Readonly<{
    installmentGuid: string;
    online: boolean;
  }>): Promise<InstallmentDetails | null> {
    const session = requireScopedLease(
      this.context.lease,
      this.context.terminal,
    );
    requirePermission(session, INSTALLMENTS_VIEW_PERMISSION);
    if (!input.online || !(await this.context.input.connectivity.isOnline())) {
      requireScopedLease(this.context.lease, this.context.terminal);
      return null;
    }
    try {
      const details = await this.context.input.api.getDetails(
        input.installmentGuid,
      );
      requireScopedLease(this.context.lease, this.context.terminal);
      return details;
    } catch (error) {
      throw mapRemoteError(error);
    }
  }

  public async recoverBlocking(): Promise<InstallmentDetails> {
    await this.assertOnlineAndScoped();
    const blocking = await this.loadBlockingAction();
    if (!blocking) {
      throw workflowError("conflict", "No installment action requires recovery.");
    }
    return this.recoverPersistedAction(blocking);
  }

  public async hasRecoveryRequired(): Promise<boolean> {
    return (await this.loadBlockingAction()) !== null;
  }

  public create(input: InstallmentWorkflowCreateInput): Promise<InstallmentDetails> {
    return this.context.input.activeCart.runExclusive(async (cartLease) => {
      await this.assertOnlineAndScoped();
      const blocking = await this.loadBlockingAction();
      if (blocking) {
        return this.executePersistedAction(blocking, cartLease);
      }
      this.requireCurrentPermission(INSTALLMENTS_CREATE_PERMISSION);
      this.requireTenderPermissions(input.method);
      const cart = cartLease.read();
      if (
        cart.cart.revision !== input.draftRevision ||
        cart.pricingState.revision !== input.draftRevision ||
        cart.cart.lines.length === 0
      ) {
        throw workflowError("conflict", "Installment cart revision changed.");
      }

      const candidate = await this.createCreateAction(input, cart.cart);
      const persisted = await this.persistCandidate(candidate);
      return this.executePersistedAction(persisted, cartLease);
    });
  }

  public async addRepayment(
    input: InstallmentWorkflowRepaymentInput,
  ): Promise<InstallmentDetails> {
    await this.assertOnlineAndScoped();
    const blocking = await this.loadBlockingAction();
    if (blocking) return this.executePersistedAction(blocking);
    this.requireCurrentPermission(INSTALLMENTS_ADD_REPAYMENT_PERMISSION);
    this.requireTenderPermissions(input.method);
    const candidate = await this.createRepaymentAction(input);
    const persisted = await this.persistCandidate(candidate);
    return this.executePersistedAction(persisted);
  }

  public async cancelWithRefund(input: Readonly<{
    installmentGuid: string;
    reason: string | null;
  }>): Promise<InstallmentDetails> {
    await this.assertOnlineAndScoped();
    const blocking = await this.loadBlockingAction();
    if (blocking) return this.executePersistedAction(blocking);
    this.requireCurrentPermission(INSTALLMENTS_CANCEL_PERMISSION);
    const candidate = await this.createCancelAction(input);
    const persisted = await this.persistCandidate(candidate);
    return this.executePersistedAction(persisted);
  }

  public async void(input: Readonly<{
    installmentGuid: string;
    reason: string;
  }>): Promise<InstallmentDetails> {
    await this.assertWriteAllowed(INSTALLMENTS_CANCEL_PERMISSION);
    const command: InstallmentVoidCommand = Object.freeze({
      ...identityFor(this.context.lease, this.context.terminal),
      installmentGuid: requiredText(input.installmentGuid, "installment guid"),
      voidedAtIso: runtimeIso(this.context.input),
      reason: requiredText(input.reason, "void reason"),
      idempotencyKey: runtimeId(this.context.input),
    });
    try {
      const details = await this.context.input.api.void(command);
      validateNonPaymentMutationResult(
        details,
        command.installmentGuid,
        this.context.terminal.storeCode,
        "Cancelled",
        "VoidCancel",
      );
      await this.cacheDetails(details);
      return details;
    } catch (error) {
      throw mapRemoteError(error);
    }
  }

  public async confirmPickup(input: Readonly<{
    installmentGuid: string;
    note: string | null;
  }>): Promise<InstallmentDetails> {
    await this.assertWriteAllowed(INSTALLMENTS_CONFIRM_PICKUP_PERMISSION);
    const command: InstallmentPickupCommand = Object.freeze({
      ...identityFor(this.context.lease, this.context.terminal),
      installmentGuid: requiredText(input.installmentGuid, "installment guid"),
      confirmedAtIso: runtimeIso(this.context.input),
      note: input.note,
    });
    try {
      const details = await this.context.input.api.confirmPickup(command);
      validateNonPaymentMutationResult(
        details,
        command.installmentGuid,
        this.context.terminal.storeCode,
        "PickedUp",
        null,
      );
      await this.cacheDetails(details);
      return details;
    } catch (error) {
      throw mapRemoteError(error);
    }
  }

  private async assertWriteAllowed(permission: string): Promise<void> {
    await this.assertOnlineAndScoped();
    if (await this.loadBlockingAction()) {
      throw paymentRecoveryError(
        "A persisted payment action must be recovered before another mutation.",
      );
    }
    this.requireCurrentPermission(permission);
  }

  private async assertOnlineAndScoped(): Promise<void> {
    const online = await this.context.input.connectivity.isOnline();
    if (!online) {
      throw workflowError("online-required", "Installment write requires online.");
    }
    requireScopedLease(this.context.lease, this.context.terminal);
  }

  private requireCurrentPermission(permission: string): void {
    const session = requireScopedLease(this.context.lease, this.context.terminal);
    requirePermission(session, permission);
  }

  private requireTenderPermissions(method: InstallmentPaymentMethod): void {
    const session = requireScopedLease(
      this.context.lease,
      this.context.terminal,
    );
    requirePermission(session, PAYMENT_PERMISSION.view);
    requirePermission(session, PAYMENT_PERMISSION.confirm);
    requirePermission(
      session,
      method === "cash"
        ? PAYMENT_PERMISSION.takeCash
        : method === "voucher"
          ? PAYMENT_PERMISSION.takeVoucher
          : PAYMENT_PERMISSION.takeCard,
    );
  }

  private async loadBlockingAction(): Promise<PersistedInstallmentAction | null> {
    const action = await this.context.input.actionStore.loadBlocking(
      this.context.terminal,
    );
    requireScopedLease(this.context.lease, this.context.terminal);
    return action ? validatePersistedAction(action, this.context.terminal) : null;
  }

  private recoverPersistedAction(
    blocking: PersistedInstallmentAction,
  ): Promise<InstallmentDetails> {
    if (blocking.action.kind === "create") {
      return this.context.input.activeCart.runExclusive((cartLease) =>
        this.executePersistedAction(blocking, cartLease),
      );
    }
    return this.executePersistedAction(blocking);
  }

  private async persistCandidate(
    candidate: InstallmentActionCandidate,
  ): Promise<PersistedInstallmentAction> {
    if (candidate.voucherIntent) {
      try {
        // 中文注释：券材料必须先按候选 actionId 耐久化；竞争失败只留下孤儿，
        // 绝不能把 losing candidate 的材料重绑到 terminal scope 的 winning action。
        await this.context.input.voucherIntents.stage(candidate.voucherIntent);
      } catch {
        throw new Error("Installment voucher intent could not be protected.");
      }
      requireScopedLease(this.context.lease, this.context.terminal);
    }
    const result = await this.context.input.actionStore.createIfNone(
      candidate.persisted,
    );
    requireScopedLease(this.context.lease, this.context.terminal);
    return validatePersistedAction(result.action, this.context.terminal);
  }

  private async createCreateAction(
    input: InstallmentWorkflowCreateInput,
    cart: CartSnapshot,
  ): Promise<InstallmentActionCandidate> {
    const action = createPaymentAction(
      this.context.input,
      "create",
      runtimeId(this.context.input),
      input.method,
      input.downPaymentCents,
    );
    const session = requireScopedLease(
      this.context.lease,
      this.context.terminal,
    );
    const fingerprint = cartFingerprint(cart);
    const command: InstallmentCreateActionCommand = Object.freeze({
      ...identityFromSession(session),
      kind: "create",
      installmentGuid: action.installmentGuid,
      createdAtIso: runtimeIso(this.context.input),
      totalCents: cart.actualAmount.cents,
      downPaymentCents: input.downPaymentCents,
      lines: createLines(
        cart,
        cart.lines.map(() => runtimeId(this.context.input)),
      ),
      customerName: requiredText(input.customerName, "customer name"),
      customerPhone: requiredText(input.customerPhone, "customer phone"),
      note: input.note,
      cartFingerprint: fingerprint,
      draftRevision: input.draftRevision,
      ...paymentCommandMetadata(input),
    });
    return this.createActionCandidate({
      action,
      command,
      intentMaterial: createActionKey(input, fingerprint),
      voucherReference: input.voucherReference,
      voucherReservationToken: input.voucherReservationToken,
      cashierId: session.cashierId,
    });
  }

  private async createRepaymentAction(
    input: InstallmentWorkflowRepaymentInput,
  ): Promise<InstallmentActionCandidate> {
    const installmentGuid = requiredText(
      input.installmentGuid,
      "installment guid",
    );
    const action = createPaymentAction(
      this.context.input,
      "repayment",
      installmentGuid,
      input.method,
      input.amountCents,
    );
    const command: InstallmentRepaymentActionCommand = Object.freeze({
      ...identityFor(this.context.lease, this.context.terminal),
      kind: "repayment",
      installmentGuid,
      ...paymentCommandMetadata(input),
    });
    return this.createActionCandidate({
      action,
      command,
      intentMaterial: repaymentActionKey(input),
      voucherReference: input.voucherReference,
      voucherReservationToken: input.voucherReservationToken,
      cashierId: command.cashierId,
    });
  }

  private async createActionCandidate(input: Readonly<{
    action: InstallmentPaymentAction;
    command: InstallmentActionCommand;
    intentMaterial: string;
    voucherReference: string | null;
    voucherReservationToken: string | null;
    cashierId: string;
  }>): Promise<InstallmentActionCandidate> {
    const voucherIntent = createVoucherIntent({
      action: input.action,
      terminal: this.context.terminal,
      cashierId: input.cashierId,
      voucherReference: input.voucherReference,
      voucherReservationToken: input.voucherReservationToken,
    });
    const voucherMaterialDigest = voucherIntent
      ? await sha256Digest(
          this.context.input,
          voucherIntentDigestMaterial(voucherIntent, input.action),
        )
      : null;
    const intentFingerprint = await sha256Digest(
      this.context.input,
      actionIntentFingerprintMaterial({
        action: input.action,
        intentMaterial: input.intentMaterial,
        terminal: this.context.terminal,
        voucherMaterialDigest,
      }),
    );
    return Object.freeze({
      persisted: persistedCandidate(
        input.action,
        input.command,
        this.context.terminal,
        intentFingerprint,
      ),
      voucherIntent,
    });
  }

  private async createCancelAction(input: Readonly<{
    installmentGuid: string;
    reason: string | null;
  }>): Promise<InstallmentActionCandidate> {
    const installmentGuid = requiredText(
      input.installmentGuid,
      "installment guid",
    );
    const action = createPaymentAction(
      this.context.input,
      "cancel-refund",
      installmentGuid,
      null,
      null,
    );
    const command: InstallmentCancelActionCommand = Object.freeze({
      ...identityFor(this.context.lease, this.context.terminal),
      kind: "cancel-refund",
      installmentGuid,
      cancelledAtIso: runtimeIso(this.context.input),
      reason: input.reason,
      idempotencyKey: action.idempotencyKey,
    });
    return this.createActionCandidate({
      action,
      command,
      intentMaterial: cancelActionKey(input),
      voucherReference: null,
      voucherReservationToken: null,
      cashierId: command.cashierId,
    });
  }

  private async executePersistedAction(
    persistedInput: PersistedInstallmentAction,
    cartLease?: ActivePricingCartLease,
  ): Promise<InstallmentDetails> {
    let persisted = validatePersistedAction(
      persistedInput,
      this.context.terminal,
    );
    await this.assertOnlineAndScoped();
    this.requireCurrentPermission(permissionForAction(persisted.action.kind));
    if (persisted.action.method) {
      this.requireTenderPermissions(persisted.action.method);
    }

    let result: Awaited<
      ReturnType<InstallmentMutationPaymentPort["beginOrRecover"]>
    >;
    try {
      if (persisted.state === "Created") {
        persisted = await this.transitionAction(
          persisted,
          "ProviderPending",
        );
        result = await this.context.input.payments.beginOrRecover(
          persisted.action.actionId,
        );
      } else {
        result = await this.context.input.payments.recoverBlocking(
          persisted.action.actionId,
        );
      }
    } catch {
      throw paymentRecoveryError(
        "Payment provider request must be recovered before another action.",
      );
    }

    requireScopedLease(this.context.lease, this.context.terminal);
    if (result.kind === "unknown") {
      if (persisted.state === "ProviderPending") {
        persisted = await this.transitionAction(persisted, "Unknown");
      }
      throw paymentRecoveryError("Installment payment outcome is unknown.");
    }

    if (result.kind === "declined") {
      if (persisted.state !== "ProviderPending" && persisted.state !== "Unknown") {
        throw paymentRecoveryError(
          "Provider declined an action already recorded as approved.",
        );
      }
      try {
        await this.context.input.actionStore.decline({
          actionId: persisted.action.actionId,
          expectedState: persisted.state,
          terminal: this.context.terminal,
        });
      } catch {
        throw paymentRecoveryError(
          "Declined payment could not be released from durable recovery.",
        );
      }
      throw workflowError("authorization-declined", "Payment was declined.");
    }

    let payment: InstallmentPaymentCommand | null = null;
    let refunds: readonly InstallmentRefundCommand[] | null = null;
    if (
      (persisted.action.kind === "create" ||
        persisted.action.kind === "repayment") &&
      "payment" in result
    ) {
      payment = validateApprovedPayment(persisted.action, result.payment);
    } else if (
      persisted.action.kind === "cancel-refund" &&
      "refunds" in result
    ) {
      refunds = validateApprovedRefunds(persisted.action, result.refunds);
    } else {
      throw paymentRecoveryError(
        "Payment adapter returned an action of the wrong kind.",
      );
    }

    if (persisted.state === "ProviderPending" || persisted.state === "Unknown") {
      persisted = await this.transitionAction(persisted, "Approved");
    }
    if (persisted.state === "Approved") {
      persisted = await this.transitionAction(persisted, "BackendPending");
    }

    try {
      await this.assertOnlineAndScoped();
      this.requireCurrentPermission(permissionForAction(persisted.action.kind));
      const details = await this.submitFrozenAction(
        persisted,
        payment,
        refunds,
      );
      validatePaymentMutationResult(
        details,
        persisted,
        payment,
        refunds,
      );
      await this.cacheDetails(details);

      if (
        cartLease &&
        persisted.command.kind === "create" &&
        isFrozenCreateCartCurrent(cartLease.read(), persisted.command)
      ) {
        // 中文注释：只清除与冻结 action 完全相同的购物车；恢复期间的新购物车绝不受影响。
        cartLease.clearAfterCommittedOrder(persisted.action.installmentGuid);
      }
      await this.context.input.actionStore.complete({
        actionId: persisted.action.actionId,
        expectedState: "BackendPending",
        terminal: this.context.terminal,
      });
      requireScopedLease(this.context.lease, this.context.terminal);
      return details;
    } catch (error) {
      if (
        error instanceof InstallmentWorkflowError &&
        error.code === "payment-recovery-required"
      ) {
        throw error;
      }
      throw paymentRecoveryError(
        error instanceof Error
          ? error.message
          : "Approved installment action requires backend recovery.",
      );
    }
  }

  private async transitionAction(
    persisted: PersistedInstallmentAction,
    nextState: InstallmentActionState,
  ): Promise<PersistedInstallmentAction> {
    const transitioned = await this.context.input.actionStore.transition({
      actionId: persisted.action.actionId,
      expectedState: persisted.state,
      nextState,
      terminal: this.context.terminal,
    });
    requireScopedLease(this.context.lease, this.context.terminal);
    return validatePersistedAction(transitioned, this.context.terminal);
  }

  private submitFrozenAction(
    persisted: PersistedInstallmentAction,
    payment: InstallmentPaymentCommand | null,
    refunds: readonly InstallmentRefundCommand[] | null,
  ): Promise<InstallmentDetails> {
    if (persisted.command.kind === "create" && payment) {
      const {
        kind: _kind,
        cartFingerprint: _cartFingerprint,
        draftRevision: _draftRevision,
        cardProvider: _cardProvider,
        cashTenderedCents: _cashTenderedCents,
        ...command
      } =
        persisted.command;
      return this.context.input.api.create(
        Object.freeze({ ...command, downPayment: payment }),
      );
    }
    if (persisted.command.kind === "repayment" && payment) {
      const {
        kind: _kind,
        cardProvider: _cardProvider,
        cashTenderedCents: _cashTenderedCents,
        ...command
      } = persisted.command;
      return this.context.input.api.appendPayment(
        Object.freeze({ ...command, payment }),
      );
    }
    if (persisted.command.kind === "cancel-refund" && refunds) {
      const { kind: _kind, ...command } = persisted.command;
      return this.context.input.api.cancelWithRefund(
        Object.freeze({ ...command, refunds }),
      );
    }
    throw paymentRecoveryError("Frozen action does not match provider approval.");
  }

  private async cacheDetails(details: InstallmentDetails): Promise<void> {
    const storeCode = this.context.terminal.storeCode;
    await this.context.input.snapshotCache.upsertForStore(storeCode, [
      toSnapshot(details),
    ]);
    requireScopedLease(this.context.lease, this.context.terminal);
  }
}

function createPaymentAction(
  input: Pick<ProductionInstallmentRuntimeDependencies, "createId">,
  kind: InstallmentPaymentAction["kind"],
  installmentGuid: string,
  method: InstallmentPaymentMethod | null,
  amountCents: number | null,
): InstallmentPaymentAction {
  const actionId = runtimeId(input);
  if (kind === "cancel-refund") {
    if (method !== null || amountCents !== null) {
      throw new Error("Cancel/refund action cannot contain a new tender.");
    }
    return Object.freeze({
      actionId,
      idempotencyKey: actionId,
      kind,
      installmentGuid: requiredText(installmentGuid, "installment guid"),
      paymentGuid: null,
      method: null,
      amountCents: null,
    });
  }
  if (!method || amountCents === null) {
    throw new Error("Payment action requires method and amount.");
  }
  return Object.freeze({
    actionId,
    idempotencyKey: actionId,
    kind,
    installmentGuid: requiredText(installmentGuid, "installment guid"),
    paymentGuid: runtimeId(input),
    method,
    amountCents: positiveInteger(amountCents, "payment amount"),
  });
}

function createVoucherIntent(input: Readonly<{
  action: InstallmentPaymentAction;
  terminal: TerminalScope;
  cashierId: string;
  voucherReference: string | null;
  voucherReservationToken: string | null;
}>): VoucherIntentStageInput | null {
  if (input.action.method !== "voucher") {
    if (
      input.voucherReference !== null ||
      input.voucherReservationToken !== null
    ) {
      throw new Error(
        "Voucher material is invalid for the selected payment method.",
      );
    }
    return null;
  }
  if (
    input.action.paymentGuid === null ||
    input.action.amountCents === null
  ) {
    throw new Error("Voucher action identity is incomplete.");
  }
  const voucherReservationToken =
    input.voucherReservationToken === null
      ? null
      : requiredText(
          input.voucherReservationToken,
          "voucher reservation token",
        );
  return Object.freeze({
    actionId: input.action.actionId,
    installmentGuid: input.action.installmentGuid,
    paymentGuid: input.action.paymentGuid,
    storeCode: input.terminal.storeCode,
    deviceCode: input.terminal.deviceCode,
    cashierId: requiredText(input.cashierId, "cashier id"),
    amountCents: input.action.amountCents,
    voucherReference: requiredText(
      input.voucherReference ?? "",
      "voucher reference",
    ),
    voucherReservationToken,
  });
}

function voucherIntentDigestMaterial(
  intent: VoucherIntentStageInput,
  action: InstallmentPaymentAction,
): string {
  return JSON.stringify({
    domain: "hb-pos/installment/voucher-intent/v1",
    scope: {
      storeCode: intent.storeCode,
      deviceCode: intent.deviceCode,
      cashierId: intent.cashierId,
    },
    action: {
      actionId: intent.actionId,
      kind: action.kind,
      installmentGuid: intent.installmentGuid,
      paymentGuid: intent.paymentGuid,
      method: action.method,
      amountCents: intent.amountCents,
    },
    voucher: {
      reference: intent.voucherReference,
      reservationToken: intent.voucherReservationToken,
    },
  });
}

function actionIntentFingerprintMaterial(input: Readonly<{
  action: InstallmentPaymentAction;
  intentMaterial: string;
  terminal: TerminalScope;
  voucherMaterialDigest: string | null;
}>): string {
  return JSON.stringify({
    domain: "hb-pos/installment/action-intent/v2",
    scope: {
      storeCode: input.terminal.storeCode,
      deviceCode: input.terminal.deviceCode,
    },
    action: {
      actionId: input.action.actionId,
      kind: input.action.kind,
      installmentGuid: input.action.installmentGuid,
      paymentGuid: input.action.paymentGuid,
      method: input.action.method,
      amountCents: input.action.amountCents,
    },
    intentMaterial: input.intentMaterial,
    voucherMaterialDigest: input.voucherMaterialDigest,
  });
}

async function sha256Digest(
  input: Pick<ProductionInstallmentRuntimeDependencies, "sha256Hex">,
  material: string,
): Promise<string> {
  let digest: string;
  try {
    digest = (await input.sha256Hex(material)).trim().toLowerCase();
  } catch {
    throw new Error("Installment intent digest could not be created.");
  }
  if (!/^[0-9a-f]{64}$/.test(digest)) {
    throw new Error("Installment intent digest could not be created.");
  }
  return `sha256:${digest}`;
}

function persistedCandidate(
  action: InstallmentPaymentAction,
  command: InstallmentActionCommand,
  terminal: TerminalScope,
  intentFingerprint: string,
): PersistedInstallmentAction {
  return Object.freeze({
    action,
    command,
    deviceCode: terminal.deviceCode,
    intentFingerprint: requiredText(intentFingerprint, "intent fingerprint"),
    state: "Created",
    storeCode: terminal.storeCode,
  });
}

function validatePersistedAction(
  persisted: PersistedInstallmentAction,
  terminal: TerminalScope,
): PersistedInstallmentAction {
  if (
    persisted.storeCode !== terminal.storeCode ||
    persisted.deviceCode !== terminal.deviceCode ||
    persisted.command.deviceCode !== terminal.deviceCode
  ) {
    throw paymentRecoveryError("Persisted action terminal scope is invalid.");
  }
  const action = persisted.action;
  recoveryUuid(action.actionId, "persisted action id");
  if (
    action.idempotencyKey !== action.actionId ||
    action.installmentGuid !== persisted.command.installmentGuid ||
    action.kind !== persisted.command.kind
  ) {
    throw paymentRecoveryError("Persisted action identity is invalid.");
  }
  recoveryText(persisted.command.cashierId, "persisted cashier id");
  recoveryText(persisted.command.cashierName, "persisted cashier name");

  if (action.kind === "cancel-refund") {
    if (
      action.paymentGuid !== null ||
      action.method !== null ||
      action.amountCents !== null ||
      persisted.command.kind !== "cancel-refund" ||
      persisted.command.idempotencyKey !== action.idempotencyKey
    ) {
      throw paymentRecoveryError("Persisted refund action is invalid.");
    }
  } else {
    if (
      action.paymentGuid === null ||
      action.method === null ||
      action.amountCents === null ||
      persisted.command.kind === "cancel-refund"
    ) {
      throw paymentRecoveryError("Persisted payment action is invalid.");
    }
    recoveryUuid(action.paymentGuid, "persisted payment guid");
    if (!Number.isSafeInteger(action.amountCents) || action.amountCents <= 0) {
      throw paymentRecoveryError("Persisted payment amount is invalid.");
    }
    const command = persisted.command;
    const validSelection =
      action.method === "card"
        ? command.cashTenderedCents === undefined &&
          (command.cardProvider === undefined ||
            command.cardProvider === "square" ||
            command.cardProvider === "linkly-cloud")
        : action.method === "cash"
          ? command.cardProvider === undefined &&
            (command.cashTenderedCents === undefined ||
              (Number.isSafeInteger(command.cashTenderedCents) &&
                command.cashTenderedCents >= action.amountCents))
          : command.cardProvider === undefined &&
            command.cashTenderedCents === undefined;
    if (!validSelection) {
      throw paymentRecoveryError(
        "Persisted payment selection is invalid.",
      );
    }
  }
  return persisted;
}

function permissionForAction(kind: InstallmentPaymentAction["kind"]): string {
  if (kind === "create") return INSTALLMENTS_CREATE_PERMISSION;
  if (kind === "repayment") return INSTALLMENTS_ADD_REPAYMENT_PERMISSION;
  return INSTALLMENTS_CANCEL_PERMISSION;
}

function identityFromSession(
  session: TrustedCashierSession,
): Readonly<{ deviceCode: string; cashierId: string; cashierName: string }> {
  return Object.freeze({
    deviceCode: session.deviceCode,
    cashierId: session.cashierId,
    cashierName: session.cashierName,
  });
}

function cartFingerprint(cart: CartSnapshot): string {
  return JSON.stringify({
    mode: cart.mode,
    totalCents: cart.actualAmount.cents,
    lines: cart.lines.map((line) => ({
      lineId: line.lineId,
      productCode: line.productCode,
      referenceCode: line.syncProvenance?.referenceCode ?? null,
      displayName: line.displayName,
      lookupCode: line.lookupCode,
      quantity: line.quantity,
      unitPriceCents: line.unitPrice.cents,
      discountCents: line.discount.cents,
      actualAmountCents: line.actualAmount.cents,
      itemNumber: line.itemNumber,
    })),
  });
}

function isFrozenCreateCartCurrent(
  snapshot: ActivePricingCartSessionSnapshot,
  command: InstallmentCreateActionCommand,
): boolean {
  return (
    snapshot.cart.revision === command.draftRevision &&
    snapshot.pricingState.revision === command.draftRevision &&
    cartFingerprint(snapshot.cart) === command.cartFingerprint
  );
}

function createDraft(
  snapshot: ActivePricingCartSessionSnapshot,
): InstallmentCreateDraft | null {
  const cart = snapshot.cart;
  if (cart.mode !== "installment" && cart.mode !== "sale") return null;
  return Object.freeze({
    revision: cart.revision,
    totalCents: cart.actualAmount.cents,
    lines: Object.freeze(
      cart.lines.map((line) =>
        Object.freeze({
          lineKey: line.lineId,
          displayName: line.displayName,
          quantity: line.quantity,
          actualAmountCents: line.actualAmount.cents,
        }),
      ),
    ),
  });
}

function createLines(
  cart: CartSnapshot,
  lineGuids: readonly string[],
): readonly InstallmentLine[] {
  if (cart.lines.length !== lineGuids.length) {
    throw workflowError("conflict", "Installment cart lines changed.");
  }
  return Object.freeze(
    cart.lines.map((line, index) =>
      Object.freeze({
        installmentLineGuid: lineGuids[index] ?? "",
        productCode: line.productCode,
        referenceCode: line.syncProvenance?.referenceCode ?? null,
        displayName: line.displayName,
        lookupCode: line.lookupCode,
        quantity: line.quantity,
        unitPriceCents: line.unitPrice.cents,
        discountCents: line.discount.cents,
        actualAmountCents: line.actualAmount.cents,
        itemNumber: line.itemNumber,
      }),
    ),
  );
}

function filterCached(
  snapshots: readonly InstallmentSnapshot[],
  input: Readonly<{ keyword: string | null; status: InstallmentStatus | null; take: number }>,
): readonly InstallmentSummary[] {
  const keyword = input.keyword?.trim().toLowerCase() ?? "";
  return Object.freeze(
    snapshots
      .filter((snapshot) => !input.status || snapshot.status === input.status)
      .filter((snapshot) => {
        if (!keyword) return true;
        return [
          snapshot.installmentNumber,
          snapshot.customerName,
          snapshot.customerPhone ?? "",
        ].some((value) => value.toLowerCase().includes(keyword));
      })
      .slice(0, input.take)
      .map(toSummary),
  );
}

function toSnapshot(summary: InstallmentSummary): InstallmentSnapshot {
  return Object.freeze({
    ...toSummary(summary),
    note: null,
    encryptedSensitiveRevision: INSTALLMENT_SENSITIVE_PAYLOAD_REVISION,
  });
}

function toSummary(value: InstallmentSummary): InstallmentSummary {
  return Object.freeze({
    installmentGuid: value.installmentGuid,
    installmentNumber: value.installmentNumber,
    storeCode: value.storeCode,
    deviceCode: value.deviceCode,
    cashierName: value.cashierName,
    customerName: value.customerName,
    customerPhone: value.customerPhone,
    createdAtIso: value.createdAtIso,
    totalCents: value.totalCents,
    downPaymentCents: value.downPaymentCents,
    paidCents: value.paidCents,
    balanceCents: value.balanceCents,
    status: value.status,
    updatedAtIso: value.updatedAtIso,
  });
}

function identityFor(
  lease: TrustedCashierLease,
  terminal: TerminalScope,
): Readonly<{ deviceCode: string; cashierId: string; cashierName: string }> {
  const session = requireScopedLease(lease, terminal);
  return Object.freeze({
    deviceCode: session.deviceCode,
    cashierId: session.cashierId,
    cashierName: session.cashierName,
  });
}

function requireScopedLease(
  lease: TrustedCashierLease,
  terminal: TerminalScope,
): TrustedCashierSession {
  const session = lease.get();
  if (
    session.storeCode !== terminal.storeCode ||
    session.deviceCode !== terminal.deviceCode
  ) {
    throw workflowError("authorization-declined", "Cashier terminal scope changed.");
  }
  return session;
}

function requirePermission(
  session: TrustedCashierSession,
  permission: string,
): void {
  if (!session.permissionCodes.includes(permission)) {
    throw workflowError("authorization-declined", "Installment permission was revoked.");
  }
}

function validateApprovedPayment(
  action: InstallmentPaymentAction,
  payment: InstallmentPaymentCommand,
): InstallmentPaymentCommand {
  if (
    action.paymentGuid === null ||
    action.method === null ||
    action.amountCents === null ||
    payment.paymentGuid !== action.paymentGuid ||
    payment.method !== action.method ||
    payment.amountCents !== action.amountCents ||
    payment.idempotencyKey !== action.idempotencyKey
  ) {
    throw workflowError(
      "payment-recovery-required",
      "Payment adapter returned a command outside the allow-list.",
    );
  }
  return payment;
}

function validateApprovedRefunds(
  action: InstallmentPaymentAction,
  approvedRefunds: readonly InstallmentApprovedRefund[],
): readonly InstallmentRefundCommand[] {
  if (action.kind !== "cancel-refund" || approvedRefunds.length === 0) {
    throw workflowError(
      "payment-recovery-required",
      "Refund adapter returned no recoverable commands.",
    );
  }
  const refundPaymentGuids = new Set<string>();
  const refundAttemptIds = new Set<string>();
  const sourceAttemptIds = new Set<string>();
  const sourcePaymentGuids = new Set<string>();
  const evidenceIds = new Set<string>();
  for (const approved of approvedRefunds) {
    const refund = approved.refund;
    if (
      refund.idempotencyKey !== action.idempotencyKey ||
      !Number.isSafeInteger(refund.amountCents) ||
      refund.amountCents <= 0
    ) {
      throw workflowError(
        "payment-recovery-required",
        "Refund command is outside the action allow-list.",
      );
    }
    if (!["cash", "card", "voucher"].includes(refund.method)) {
      throw paymentRecoveryError("Refund method is invalid.");
    }
    const paymentGuid = recoveryUuid(
      refund.paymentGuid,
      "refund payment guid",
    );
    const refundAttemptId = recoveryText(
      approved.refundAttemptId,
      "refund attempt id",
    );
    const sourceAttemptId = recoveryText(
      approved.sourceAttemptId,
      "source attempt id",
    );
    const sourcePaymentGuid = recoveryUuid(
      approved.sourcePaymentGuid,
      "source payment guid",
    );
    const evidenceId = recoveryText(
      approved.originalTenderEvidenceId,
      "original tender evidence id",
    );
    if (
      refundPaymentGuids.has(paymentGuid) ||
      refundAttemptIds.has(refundAttemptId) ||
      sourceAttemptIds.has(sourceAttemptId) ||
      sourcePaymentGuids.has(sourcePaymentGuid) ||
      evidenceIds.has(evidenceId)
    ) {
      throw workflowError(
        "payment-recovery-required",
        "Refund recovery evidence contains duplicate tenders.",
      );
    }
    refundPaymentGuids.add(paymentGuid);
    refundAttemptIds.add(refundAttemptId);
    sourceAttemptIds.add(sourceAttemptId);
    sourcePaymentGuids.add(sourcePaymentGuid);
    evidenceIds.add(evidenceId);
  }
  return Object.freeze(approvedRefunds.map((approved) => approved.refund));
}

function validatePaymentMutationResult(
  details: InstallmentDetails,
  persisted: PersistedInstallmentAction,
  payment: InstallmentPaymentCommand | null,
  refunds: readonly InstallmentRefundCommand[] | null,
): void {
  if (
    details.installmentGuid !== persisted.action.installmentGuid ||
    details.storeCode !== persisted.storeCode
  ) {
    throw paymentRecoveryError(
      "Installment response does not match the frozen action scope.",
    );
  }

  if (persisted.command.kind === "create" && payment) {
    if (
      (details.status !== "Active" && details.status !== "PaidOff") ||
      details.deviceCode !== persisted.deviceCode ||
      details.cashierId !== persisted.command.cashierId ||
      details.cashierName !== persisted.command.cashierName ||
      details.customerName !== persisted.command.customerName ||
      details.customerPhone !== persisted.command.customerPhone ||
      details.totalCents !== persisted.command.totalCents ||
      details.downPaymentCents !== persisted.command.downPaymentCents ||
      !sameInstallmentLines(details.lines, persisted.command.lines)
    ) {
      throw paymentRecoveryError(
        "Create response does not match the frozen installment command.",
      );
    }
    requireRecordedPayment(
      details,
      payment,
      persisted.command.cashierId,
      persisted.deviceCode,
      payment.amountCents,
    );
    return;
  }

  if (persisted.command.kind === "repayment" && payment) {
    if (details.status !== "Active" && details.status !== "PaidOff") {
      throw paymentRecoveryError("Repayment response has an invalid status.");
    }
    requireRecordedPayment(
      details,
      payment,
      persisted.command.cashierId,
      persisted.deviceCode,
      payment.amountCents,
    );
    return;
  }

  if (persisted.command.kind === "cancel-refund" && refunds) {
    if (
      details.status !== "Cancelled" ||
      details.cancellationInfo?.kind !== "RefundCancel"
    ) {
      throw paymentRecoveryError("Refund cancellation was not confirmed.");
    }
    const recordedRefunds = details.payments.filter(
      (payment) =>
        payment.status === "Recorded" && payment.amountCents < 0,
    );
    if (recordedRefunds.length !== refunds.length) {
      throw paymentRecoveryError(
        "Refund response contains uncorrelated refund tenders.",
      );
    }
    for (const refund of refunds) {
      requireRecordedPayment(
        details,
        refund,
        persisted.command.cashierId,
        persisted.deviceCode,
        -refund.amountCents,
      );
    }
    return;
  }

  throw paymentRecoveryError(
    "Installment response cannot be correlated with provider approval.",
  );
}

function requireRecordedPayment(
  details: InstallmentDetails,
  command: Pick<
    InstallmentPaymentCommand,
    "paymentGuid" | "method" | "amountCents"
  >,
  cashierId: string,
  deviceCode: string,
  responseAmountCents: number,
): void {
  const matched = details.payments.find(
    (payment) =>
      payment.paymentGuid === command.paymentGuid &&
      payment.method === command.method &&
      payment.amountCents === responseAmountCents &&
      payment.status === "Recorded" &&
      payment.cashierId === cashierId &&
      payment.deviceCode === deviceCode,
  );
  if (!matched) {
    throw paymentRecoveryError(
      "Installment response is missing the approved tender.",
    );
  }
}

function sameInstallmentLines(
  actual: readonly InstallmentLine[],
  expected: readonly InstallmentLine[],
): boolean {
  if (actual.length !== expected.length) return false;
  return expected.every((line, index) => {
    const candidate = actual[index];
    return (
      candidate?.installmentLineGuid === line.installmentLineGuid &&
      candidate.productCode === line.productCode &&
      candidate.referenceCode === line.referenceCode &&
      candidate.displayName === line.displayName &&
      candidate.lookupCode === line.lookupCode &&
      candidate.quantity === line.quantity &&
      candidate.unitPriceCents === line.unitPriceCents &&
      candidate.discountCents === line.discountCents &&
      candidate.actualAmountCents === line.actualAmountCents &&
      candidate.itemNumber === line.itemNumber
    );
  });
}

function validateNonPaymentMutationResult(
  details: InstallmentDetails,
  installmentGuid: string,
  storeCode: string,
  status: InstallmentStatus,
  cancellationKind: "VoidCancel" | null,
): void {
  if (
    details.installmentGuid !== installmentGuid ||
    details.storeCode !== storeCode ||
    details.status !== status ||
    (cancellationKind !== null &&
      details.cancellationInfo?.kind !== cancellationKind)
  ) {
    throw workflowError(
      "conflict",
      "Installment mutation response does not match the command.",
    );
  }
}

function mapRemoteError(error: unknown): Error {
  if (error instanceof InstallmentWorkflowError) return error;
  if (error instanceof HbposApiError) {
    if (error.status === 401 || error.status === 403) {
      return workflowError("authorization-declined", error.message);
    }
    if (error.status === 409 || error.code?.toLowerCase().includes("conflict")) {
      return workflowError("conflict", error.message);
    }
    if (error.kind === "transport") {
      return workflowError("online-required", error.message);
    }
  }
  return error instanceof Error ? error : new Error("Installment remote request failed.");
}

function workflowError(
  code: ConstructorParameters<typeof InstallmentWorkflowError>[0],
  message: string,
): InstallmentWorkflowError {
  return new InstallmentWorkflowError(code, message);
}

function normalizeTerminal(terminal: TerminalScope): TerminalScope {
  return Object.freeze({
    storeCode: requiredText(terminal.storeCode, "store code"),
    deviceCode: requiredText(terminal.deviceCode, "device code"),
  });
}

function runtimeId(
  input: Pick<ProductionInstallmentRuntimeDependencies, "createId">,
): string {
  return runtimeUuid(input.createId(), "runtime action id");
}

function runtimeIso(
  input: Pick<ProductionInstallmentRuntimeDependencies, "nowIso">,
): string {
  const value = requiredText(input.nowIso(), "runtime timestamp");
  const timestamp = Date.parse(value);
  if (!Number.isFinite(timestamp)) {
    throw new Error("runtime timestamp must be an ISO timestamp.");
  }
  return new Date(timestamp).toISOString();
}

function runtimeUuid(value: string, label: string): string {
  const normalized = requiredText(value, label).toLowerCase();
  if (
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(
      normalized,
    )
  ) {
    throw new Error(`${label} must be a UUID.`);
  }
  return normalized;
}

function recoveryUuid(value: string, label: string): string {
  try {
    return runtimeUuid(value, label);
  } catch {
    throw paymentRecoveryError(`${label} is invalid.`);
  }
}

function recoveryText(value: string, label: string): string {
  try {
    return requiredText(value, label);
  } catch {
    throw paymentRecoveryError(`${label} is invalid.`);
  }
}

function requiredText(value: string, label: string): string {
  const normalized = value.trim();
  if (!normalized) throw new Error(`${label} is required.`);
  return normalized;
}

function positiveInteger(value: number, label: string): number {
  if (!Number.isSafeInteger(value) || value <= 0) {
    throw new Error(`${label} must be a positive safe integer.`);
  }
  return value;
}

function paymentRecoveryError(message: string): InstallmentWorkflowError {
  return workflowError("payment-recovery-required", message);
}

function paymentCommandMetadata(
  input: Pick<
    InstallmentWorkflowCreateInput,
    "method" | "downPaymentCents" | "cardProvider" | "cashTenderedCents"
  > |
    Pick<
      InstallmentWorkflowRepaymentInput,
      "method" | "amountCents" | "cardProvider" | "cashTenderedCents"
    >,
): Readonly<{
  cardProvider?: InstallmentCardProvider;
  cashTenderedCents?: number;
}> {
  const amountCents =
    "downPaymentCents" in input
      ? input.downPaymentCents
      : input.amountCents;
  if (input.method === "card") {
    if (
      input.cashTenderedCents !== undefined ||
      (input.cardProvider !== undefined &&
        input.cardProvider !== "square" &&
        input.cardProvider !== "linkly-cloud")
    ) {
      throw workflowError("conflict", "Installment card provider is invalid.");
    }
    return input.cardProvider === undefined
      ? Object.freeze({})
      : Object.freeze({ cardProvider: input.cardProvider });
  }
  if (input.method === "cash") {
    if (input.cardProvider !== undefined) {
      throw workflowError("conflict", "Installment cash selection is invalid.");
    }
    if (input.cashTenderedCents === undefined) return Object.freeze({});
    const cashTenderedCents = positiveInteger(
      input.cashTenderedCents,
      "cash tendered amount",
    );
    if (cashTenderedCents < amountCents) {
      throw workflowError("conflict", "Installment cash amount is invalid.");
    }
    return Object.freeze({ cashTenderedCents });
  }
  if (
    input.cardProvider !== undefined ||
    input.cashTenderedCents !== undefined
  ) {
    throw workflowError("conflict", "Installment voucher selection is invalid.");
  }
  return Object.freeze({});
}

function createActionKey(
  input: InstallmentWorkflowCreateInput,
  fingerprint: string,
): string {
  return JSON.stringify({
    kind: "create",
    cartFingerprint: fingerprint,
    draftRevision: input.draftRevision,
    customerName: input.customerName,
    customerPhone: input.customerPhone,
    note: input.note,
    downPaymentCents: input.downPaymentCents,
    method: input.method,
    cardProvider: input.cardProvider,
    cashTenderedCents: input.cashTenderedCents,
  });
}

function repaymentActionKey(input: InstallmentWorkflowRepaymentInput): string {
  return JSON.stringify({
    kind: "repayment",
    installmentGuid: input.installmentGuid,
    amountCents: input.amountCents,
    method: input.method,
    cardProvider: input.cardProvider,
    cashTenderedCents: input.cashTenderedCents,
  });
}

function cancelActionKey(input: Readonly<{ installmentGuid: string; reason: string | null }>): string {
  return JSON.stringify({ kind: "cancel-refund", ...input });
}
