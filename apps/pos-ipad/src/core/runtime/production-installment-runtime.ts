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
import type {
  FulfilmentActionResult,
  FulfilmentAuthorizationContext,
  FulfilmentLeaseGuard,
} from "@/features/fulfilment";
import {
  INSTALLMENTS_ADD_REPAYMENT_PERMISSION,
  INSTALLMENTS_CANCEL_PERMISSION,
  INSTALLMENTS_CONFIRM_PICKUP_PERMISSION,
  INSTALLMENTS_CREATE_PERMISSION,
  INSTALLMENTS_REPRINT_PERMISSION,
  INSTALLMENTS_VIEW_PERMISSION,
} from "@/features/installments/installment-authorization";
import { InstallmentCheckoutPresenter } from "@/features/installments/installment-checkout-presenter";
import { resolveInstallmentDateRange } from "@/features/installments/installment-date-filter";
import type {
  InstallmentAppendPaymentCommand,
  InstallmentCancelCommand,
  InstallmentCreateCommand,
  InstallmentDateFilter,
  InstallmentDetails,
  InstallmentDeviceScope,
  InstallmentLine,
  InstallmentPaymentCommand,
  InstallmentPaymentMethod,
  InstallmentCardProvider,
  InstallmentPickupCommand,
  InstallmentRepaymentCapabilities,
  InstallmentRepaymentClaim,
  InstallmentCancelClaim,
  InstallmentRefundCommand,
  InstallmentsRemotePort,
  InstallmentVoidCommand,
} from "@/features/installments/installment-models";
import {
  InstallmentPresenter,
  InstallmentWorkflowError,
  type InstallmentCreateDraft,
  type InstallmentCreateDraftPort,
  type InstallmentReprintPort,
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
  Readonly<{ kind: "cancel-refund"; refundPlanFingerprint?: string }>;

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

type FrozenInstallmentVoidCommand = InstallmentVoidCommand &
  Readonly<{ operationGuid: string; idempotencyKey: string }>;

type FrozenInstallmentPickupCommand = InstallmentPickupCommand &
  Readonly<{ operationGuid: string; idempotencyKey: string }>;

export type PersistedInstallmentLifecycleAction = Readonly<{
  operationGuid: string;
  idempotencyKey: string;
  kind: "void" | "pickup";
  installmentGuid: string;
  storeCode: string;
  /** 本次动作实际执行终端；command.deviceCode 必须与其一致。 */
  deviceCode: string;
  /** 服务端分期详情所属的原始终端，用于校验幂等回放结果。 */
  originalDeviceCode: string;
  command: FrozenInstallmentVoidCommand | FrozenInstallmentPickupCommand;
  intentFingerprint: string;
}>;

type PersistedInstallmentBlockingOperation =
  | Readonly<{ type: "payment"; action: PersistedInstallmentAction }>
  | Readonly<{
      type: "lifecycle";
      action: PersistedInstallmentLifecycleAction;
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
  loadLifecycleBlocking(
    terminal: TerminalScope,
  ): Promise<PersistedInstallmentLifecycleAction | null>;
  createLifecycleIfNone(
    candidate: PersistedInstallmentLifecycleAction,
  ): Promise<Readonly<{
    created: boolean;
    action: PersistedInstallmentLifecycleAction;
  }>>;
  completeLifecycle(input: Readonly<{
    operationGuid: string;
    terminal: TerminalScope;
  }>): Promise<void>;
  /**
   * claim 在 provider 前确定性拒绝时，原子地终结 Created；实现必须保证不存在
   * 可观察的 ProviderPending 中间态，也不得删除审计事实。
   */
  finalizeCreatedFailure?(input: Readonly<{
    actionId: string;
    reason:
      | "ClaimBusy"
      | "ClaimMismatch"
      | "ClaimReleased"
      | "PaymentMethodUnsupported";
    terminal: TerminalScope;
  }>): Promise<void>;
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
  /**
   * 只耐久绑定 provider plan，不调用 provider。claim 的 begin-provider 成功后，
   * runtime 才能进入 beginOrRecover；恢复必须复用这里返回的同一 attempt 身份。
   */
  prepareRepaymentClaim(
    persistedActionId: string,
  ): Promise<Readonly<{ provider: string; providerAttemptId: string }>>;
  beginOrRecover(
    persistedActionId: string,
  ): Promise<
    | Readonly<{ kind: "approved"; payment: InstallmentPaymentCommand }>
    | Readonly<{
        kind: "approved";
        refunds: readonly InstallmentApprovedRefund[];
      }>
    | Readonly<{ kind: "declined"; allRefundsDeclined?: boolean }>
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
    | Readonly<{ kind: "declined"; allRefundsDeclined?: boolean }>
    | Readonly<{ kind: "unknown" }>
  >;
}

/**
 * 组合根持有真实 fulfilment 服务；分期运行时只负责把同一可信 cashier lease
 * 绑定到授权上下文和发送前复核，Presenter 不能注入打印机或票据字节。
 */
export type InstallmentReceiptReprintRuntimePort = Readonly<{
  canReprint(details: InstallmentDetails): boolean;
  execute(
    installmentGuid: string,
    authorization: FulfilmentAuthorizationContext,
    assertActive: FulfilmentLeaseGuard,
  ): Promise<FulfilmentActionResult>;
}>;

export type ProductionInstallmentRuntimeDependencies = Readonly<{
  currentCashier: CurrentCashierSession;
  terminal: TerminalScope;
  activeCart: CartPort;
  connectivity: InstallmentConnectivityPort;
  api: InstallmentsRemotePort;
  snapshotCache: InstallmentSnapshotCachePort;
  actionStore: InstallmentActionStorePort;
  payments: InstallmentMutationPaymentPort;
  receiptReprint?: InstallmentReceiptReprintRuntimePort | null;
  voucherIntents: InstallmentVoucherIntentVaultPort;
  sha256Hex(material: string): Promise<string>;
  createId(): string;
  businessTimeZone: string;
  now(): Date;
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
        reprintPort: createInstallmentReprintPort(input, lease, terminal),
        trustedDeviceCode: terminal.deviceCode,
        trustedStoreCode: terminal.storeCode,
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
      const [payment, lifecycle] = await Promise.all([
        input.actionStore.loadBlocking(terminal),
        input.actionStore.loadLifecycleBlocking(terminal),
      ]);
      requireScopedLease(lease, terminal);
      if (payment && lifecycle) {
        throw new Error("Multiple installment actions require recovery.");
      }
      return payment !== null || lifecycle !== null;
    },
  });
}

function createInstallmentReprintPort(
  input: ProductionInstallmentRuntimeDependencies,
  lease: TrustedCashierLease,
  terminal: TerminalScope,
): InstallmentReprintPort | null {
  const receiptReprint = input.receiptReprint;
  if (!receiptReprint) return null;

  const assertActive = (): TrustedCashierSession => {
    const active = requireScopedLease(lease, terminal);
    requirePermission(active, INSTALLMENTS_VIEW_PERMISSION);
    requirePermission(active, INSTALLMENTS_REPRINT_PERMISSION);
    return active;
  };

  return Object.freeze({
    canReprint(details: InstallmentDetails): boolean {
      try {
        const active = assertActive();
        return (
          details.storeCode === active.storeCode &&
          details.deviceCode === active.deviceCode &&
          receiptReprint.canReprint(details)
        );
      } catch {
        // capability 是渲染期只读查询；会话已失效时必须隐藏动作，不能让页面崩溃。
        return false;
      }
    },
    async reprintExistingInstallment(installmentGuid: string): Promise<void> {
      const active = assertActive();
      const result = await receiptReprint.execute(
        installmentGuid,
        {
          actionId: input.createId(),
          permissionCode: INSTALLMENTS_REPRINT_PERMISSION,
          authorizationMode: "current-cashier",
          requestingCashierId: active.cashierId,
          requestingCashierName: active.cashierName,
          requestingUserGuid: active.userGuid,
          authorizingCashierId: null,
        },
        () => {
          assertActive();
        },
      );
      if (result.state !== "Printed") {
        throw Object.assign(
          new Error("Installment receipt reprint failed."),
          {
            code:
              result.errorCode ??
              `REPRINT_${result.state.toUpperCase().replaceAll("-", "_")}`,
          },
        );
      }
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

  public async getRepaymentCapabilities(): Promise<InstallmentRepaymentCapabilities> {
    await this.assertOnlineAndScoped();
    this.requireCurrentPermission(INSTALLMENTS_VIEW_PERMISSION);
    try {
      const capabilities = validateRepaymentCapabilities(
        await this.context.input.api.getCapabilities(),
      );
      requireScopedLease(this.context.lease, this.context.terminal);
      return capabilities;
    } catch (error) {
      throw mapRemoteError(error);
    }
  }

  public async listPaymentProviderAvailability() {
    requireScopedLease(this.context.lease, this.context.terminal);
    const availability =
      await this.context.input.payments.listProviderAvailability?.();
    requireScopedLease(this.context.lease, this.context.terminal);
    return Object.freeze([...(availability ?? [])]);
  }

  public async list(input: Readonly<{
    dateFilter: InstallmentDateFilter;
    deviceScope: InstallmentDeviceScope;
    keyword: string | null;
    online: boolean;
    skip: number;
    status: InstallmentStatus | null;
    take: 51;
  }>): Promise<readonly InstallmentSummary[]> {
    const session = requireScopedLease(
      this.context.lease,
      this.context.terminal,
    );
    requirePermission(session, INSTALLMENTS_VIEW_PERMISSION);
    let online = false;
    try {
      online =
        input.online &&
        (await this.context.input.connectivity.isOnline());
    } catch {
      throw workflowError(
        "online-required",
        "Installment history requires an online connection.",
      );
    }
    requireScopedLease(this.context.lease, this.context.terminal);

    if (!online) {
      throw workflowError(
        "online-required",
        "Installment history requires an online connection.",
      );
    }

    try {
      const blocking = await this.loadBlockingOperation();
      if (blocking) {
        await this.recoverBlockingOperation(blocking);
      }
      const range = resolveInstallmentDateRange(
        input.dateFilter,
        this.context.input.now(),
        this.context.input.businessTimeZone,
      );
      if (!range) {
        throw workflowError(
          "conflict",
          "Installment date filter is invalid.",
        );
      }
      const orders = await this.context.input.api.list({
        createdFromIso: range.createdFromIso,
        createdToIso: range.createdToIso,
        deviceCode: deviceCodeForScope(
          input.deviceScope,
          this.context.terminal,
        ),
        keyword: input.keyword,
        skip: input.skip,
        status: input.status,
        take: input.take,
      });
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
    let online = false;
    try {
      online =
        input.online &&
        (await this.context.input.connectivity.isOnline());
    } catch {
      throw workflowError(
        "online-required",
        "Installment details require an online connection.",
      );
    }
    if (!online) {
      requireScopedLease(this.context.lease, this.context.terminal);
      throw workflowError(
        "online-required",
        "Installment details require an online connection.",
      );
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
    const blocking = await this.loadBlockingOperation();
    if (!blocking) {
      throw workflowError("conflict", "No installment action requires recovery.");
    }
    return this.recoverBlockingOperation(blocking);
  }

  public async hasRecoveryRequired(): Promise<boolean> {
    return (await this.loadBlockingOperation()) !== null;
  }

  public create(input: InstallmentWorkflowCreateInput): Promise<InstallmentDetails> {
    return this.context.input.activeCart.runExclusive(async (cartLease) => {
      await this.assertOnlineAndScoped();
      const blocking = await this.loadBlockingOperation();
      if (blocking) {
        return blocking.type === "payment" && blocking.action.action.kind === "create"
          ? this.executePersistedAction(blocking.action, cartLease)
          : this.recoverBlockingOperation(blocking);
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
    const blocking = await this.loadBlockingOperation();
    if (blocking) return this.recoverBlockingOperation(blocking);
    this.requireCurrentPermission(INSTALLMENTS_ADD_REPAYMENT_PERMISSION);
    this.requireTenderPermissions(input.method);
    const capabilities = await this.getRequiredRepaymentCapabilities();
    if (input.method === "card" && !capabilities.cardRepaymentSupported) {
      throw workflowError(
        "conflict",
        "Card installment repayment is not supported by this server.",
      );
    }
    await this.assertInitialMutationScope(
      requiredText(input.installmentGuid, "installment guid"),
      capabilities.crossDeviceRepaymentEnabled,
    );
    const candidate = await this.createRepaymentAction(input);
    const persisted = await this.persistCandidate(candidate);
    return this.executePersistedAction(persisted);
  }

  public async cancelWithRefund(input: Readonly<{
    installmentGuid: string;
    reason: string | null;
  }>): Promise<InstallmentDetails> {
    await this.assertOnlineAndScoped();
    const blocking = await this.loadBlockingOperation();
    if (blocking) return this.recoverBlockingOperation(blocking);
    this.requireCurrentPermission(INSTALLMENTS_CANCEL_PERMISSION);
    const capabilities = await this.getRequiredCancelClaimCapabilities();
    const details = await this.assertInitialMutationScope(
      requiredText(input.installmentGuid, "installment guid"),
      capabilities.crossDeviceCancelRefundEnabled,
    );
    const candidate = await this.createCancelAction(input, details);
    const persisted = await this.persistCandidate(candidate);
    return this.executePersistedAction(persisted);
  }

  public async void(input: Readonly<{
    installmentGuid: string;
    reason: string;
  }>): Promise<InstallmentDetails> {
    await this.assertOnlineAndScoped();
    const blocking = await this.loadBlockingOperation();
    if (blocking) return this.recoverBlockingOperation(blocking);
    this.requireCurrentPermission(INSTALLMENTS_CANCEL_PERMISSION);
    const installmentGuid = requiredText(
      input.installmentGuid,
      "installment guid",
    );
    const details = await this.assertInitialMutationScope(
      installmentGuid,
      true,
    );
    if (details.deviceCode !== this.context.terminal.deviceCode) {
      const capabilities = await this.getRepaymentCapabilities();
      if (!capabilities.crossDeviceVoidEnabled) {
        throw workflowError(
          "conflict",
          "Installment payment scope does not match the current terminal.",
        );
      }
    }
    const operationGuid = runtimeId(this.context.input);
    const command: FrozenInstallmentVoidCommand = Object.freeze({
      ...identityFor(this.context.lease, this.context.terminal),
      installmentGuid,
      voidedAtIso: runtimeIso(this.context.input),
      reason: requiredText(input.reason, "void reason"),
      operationGuid,
      idempotencyKey: operationGuid,
    });
    const persisted = await this.persistLifecycleCandidate(
      "void",
      details.deviceCode,
      command,
    );
    return this.executePersistedLifecycleAction(persisted);
  }

  public async confirmPickup(input: Readonly<{
    installmentGuid: string;
    note: string | null;
  }>): Promise<InstallmentDetails> {
    await this.assertOnlineAndScoped();
    const blocking = await this.loadBlockingOperation();
    if (blocking) return this.recoverBlockingOperation(blocking);
    this.requireCurrentPermission(INSTALLMENTS_CONFIRM_PICKUP_PERMISSION);
    const installmentGuid = requiredText(
      input.installmentGuid,
      "installment guid",
    );
    const details = await this.assertInitialMutationScope(
      installmentGuid,
      true,
    );
    if (details.deviceCode !== this.context.terminal.deviceCode) {
      const capabilities = await this.getRepaymentCapabilities();
      if (!capabilities.crossDevicePickupEnabled) {
        throw workflowError(
          "conflict",
          "Installment payment scope does not match the current terminal.",
        );
      }
    }
    const operationGuid = runtimeId(this.context.input);
    const command: FrozenInstallmentPickupCommand = Object.freeze({
      ...identityFor(this.context.lease, this.context.terminal),
      installmentGuid,
      confirmedAtIso: runtimeIso(this.context.input),
      note: optionalText(input.note),
      operationGuid,
      idempotencyKey: operationGuid,
    });
    const persisted = await this.persistLifecycleCandidate(
      "pickup",
      details.deviceCode,
      command,
    );
    return this.executePersistedLifecycleAction(persisted);
  }

  private async assertOnlineAndScoped(): Promise<void> {
    const online = await this.context.input.connectivity.isOnline();
    if (!online) {
      throw workflowError("online-required", "Installment write requires online.");
    }
    requireScopedLease(this.context.lease, this.context.terminal);
  }

  private async getRequiredRepaymentCapabilities(): Promise<InstallmentRepaymentCapabilities> {
    const capabilities = await this.getRepaymentCapabilities();
    if (
      !capabilities.repaymentClaimsSupported
    ) {
      throw workflowError(
        "service-unavailable",
        "Repayment claims are not available on this server.",
      );
    }
    return capabilities;
  }

  private async getRequiredCancelClaimCapabilities(): Promise<InstallmentRepaymentCapabilities> {
    const capabilities = await this.getRepaymentCapabilities();
    if (capabilities.cancelClaimsSupported !== true) {
      throw workflowError(
        "service-unavailable",
        "Cancellation claims are not available on this server.",
      );
    }
    return capabilities;
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

  private async loadBlockingOperation(): Promise<PersistedInstallmentBlockingOperation | null> {
    const [payment, lifecycle] = await Promise.all([
      this.loadBlockingAction(),
      this.context.input.actionStore.loadLifecycleBlocking(
        this.context.terminal,
      ),
    ]);
    requireScopedLease(this.context.lease, this.context.terminal);
    if (payment && lifecycle) {
      throw paymentRecoveryError(
        "Multiple persisted installment actions require recovery.",
      );
    }
    if (payment) return Object.freeze({ type: "payment", action: payment });
    if (lifecycle) {
      return Object.freeze({
        type: "lifecycle",
        action: validatePersistedLifecycleAction(
          lifecycle,
          this.context.terminal,
        ),
      });
    }
    return null;
  }

  private recoverBlockingOperation(
    blocking: PersistedInstallmentBlockingOperation,
  ): Promise<InstallmentDetails> {
    return blocking.type === "payment"
      ? this.recoverPersistedAction(blocking.action)
      : this.executePersistedLifecycleAction(blocking.action);
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

  private async persistLifecycleCandidate(
    kind: PersistedInstallmentLifecycleAction["kind"],
    originalDeviceCode: string,
    command: FrozenInstallmentVoidCommand | FrozenInstallmentPickupCommand,
  ): Promise<PersistedInstallmentLifecycleAction> {
    const candidate = Object.freeze({
      operationGuid: command.operationGuid,
      idempotencyKey: command.idempotencyKey,
      kind,
      installmentGuid: command.installmentGuid,
      storeCode: this.context.terminal.storeCode,
      deviceCode: this.context.terminal.deviceCode,
      originalDeviceCode,
      command,
      intentFingerprint: await sha256Digest(
        this.context.input,
        lifecycleIntentFingerprintMaterial({
          kind,
          originalDeviceCode,
          storeCode: this.context.terminal.storeCode,
          command,
        }),
      ),
    }) satisfies PersistedInstallmentLifecycleAction;
    const result = await this.context.input.actionStore.createLifecycleIfNone(
      candidate,
    );
    requireScopedLease(this.context.lease, this.context.terminal);
    return validatePersistedLifecycleAction(
      result.action,
      this.context.terminal,
    );
  }

  private async executePersistedLifecycleAction(
    persistedInput: PersistedInstallmentLifecycleAction,
  ): Promise<InstallmentDetails> {
    const persisted = validatePersistedLifecycleAction(
      persistedInput,
      this.context.terminal,
    );
    this.requireCurrentPermission(
      persisted.kind === "void"
        ? INSTALLMENTS_CANCEL_PERMISSION
        : INSTALLMENTS_CONFIRM_PICKUP_PERMISSION,
    );
    let details: InstallmentDetails;
    try {
      details =
        persisted.kind === "void"
          ? await this.context.input.api.void(
              persisted.command as FrozenInstallmentVoidCommand,
            )
          : await this.context.input.api.confirmPickup(
              persisted.command as FrozenInstallmentPickupCommand,
            );
    } catch (error) {
      // 中文注释：只有真实的远程请求失败才映射为网络、授权或服务端错误。
      throw mapRemoteError(error);
    }
    validateLifecycleMutationResult(details, persisted);
    try {
      // 中文注释：服务端已确认提交成功后，本地快照缓存与 lifecycle CAS 是恢复必需步骤。
      // 这两步失败绝不能伪装成普通远程错误，必须标记 payment-recovery-required，
      // 否则 presenter 不会进入 recoveryRequired，操作会永远卡住。
      await this.cacheDetails(details);
      await this.context.input.actionStore.completeLifecycle({
        operationGuid: persisted.operationGuid,
        terminal: this.context.terminal,
      });
    } catch {
      throw paymentRecoveryError(
        "Installment lifecycle committed remotely but local settlement failed; recovery is required.",
      );
    }
    return details;
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
  }>, details: InstallmentDetails): Promise<InstallmentActionCandidate> {
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
      refundPlanFingerprint: await createCancelRefundPlanFingerprint(
        this.context.input,
        details,
      ),
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

    if (persisted.action.kind === "repayment") {
      return this.executeRepaymentClaimAction(persisted);
    }
    if (persisted.action.kind === "cancel-refund") {
      return this.executeCancelClaimAction(persisted);
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

  private async executeRepaymentClaimAction(
    persistedInput: PersistedInstallmentAction,
  ): Promise<InstallmentDetails> {
    let persisted = persistedInput;
    const action = persisted.action;
    if (
      action.kind !== "repayment" ||
      action.paymentGuid === null ||
      action.method === null ||
      action.amountCents === null
    ) {
      throw paymentRecoveryError("Repayment action identity is incomplete.");
    }
    const identity = Object.freeze({
      installmentGuid: action.installmentGuid,
      operationGuid: action.actionId,
    });

    let claim = await this.loadOrCreateRepaymentClaim(
      persisted,
      identity,
    );

    if (claim.status === "Committed") {
      return this.finishCommittedRepayment(persisted, claim);
    }
    if (claim.status === "Released" || claim.status === "Declined") {
      await this.releaseUnapprovedRepayment(persisted);
      throw workflowError(
        claim.status === "Declined" ? "authorization-declined" : "conflict",
        `Repayment claim is ${claim.status.toLowerCase()}.`,
      );
    }

    let binding: Readonly<{ provider: string; providerAttemptId: string }>;
    try {
      binding = await this.context.input.payments.prepareRepaymentClaim(
        action.actionId,
      );
      validateClaimBinding(binding);
    } catch (error) {
      if (claim.status === "Prepared" && persisted.state === "Created") {
        try {
          const released = await this.context.input.api.resolveRepaymentClaim({
            ...identity,
            outcome: "Released",
          });
          validateRepaymentClaim(released, action);
          if (released.status === "Released") {
            await this.releaseUnapprovedRepayment(persisted);
          }
        } catch {
          // 释放失败时保留本地阻塞 action，避免不确定的 claim 被其他操作越过。
        }
      }
      throw paymentRecoveryError(
        error instanceof Error
          ? error.message
          : "Repayment provider binding requires recovery.",
      );
    }

    if (claim.provider !== null || claim.providerAttemptId !== null) {
      if (
        claim.provider !== binding.provider ||
        claim.providerAttemptId !== binding.providerAttemptId
      ) {
        if (persisted.state === "Created") {
          await this.finalizeCreatedClaimFailure(
            persisted,
            "ClaimMismatch",
          );
          throw workflowError(
            "claim-review-required",
            "Repayment claim provider binding mismatch requires review.",
          );
        }
        throw workflowError(
          "conflict",
          "Repayment claim provider binding does not match this device.",
        );
      }
    }

    try {
      if (claim.status === "Prepared" || claim.status === "Unknown") {
        claim = await this.context.input.api.beginRepaymentClaimProvider({
          ...identity,
          ...binding,
        });
        validateRepaymentClaim(claim, action, binding);
      }
      if (claim.status !== "ProviderPending") {
        throw workflowError(
          "conflict",
          "Repayment claim is not ready for provider recovery.",
        );
      }
    } catch (error) {
      const deterministic =
        repaymentClaimDeterministicFailure(error) ??
        (repaymentClaimLocalMismatch(error) ? "ClaimMismatch" : null);
      if (deterministic && persisted.state === "Created") {
        await this.finalizeCreatedClaimFailure(persisted, deterministic);
        throw workflowError(
          deterministic === "ClaimMismatch"
            ? "claim-review-required"
            : "conflict",
          deterministic === "ClaimMismatch"
            ? "Repayment claim binding mismatch requires review."
            : "Another repayment claim is active.",
        );
      }
      throw mapRemoteError(error);
    }

    const authorize = persisted.state === "Created";
    if (persisted.state === "Created") {
      persisted = await this.transitionAction(persisted, "ProviderPending");
    }

    let result: Awaited<
      ReturnType<InstallmentMutationPaymentPort["beginOrRecover"]>
    >;
    try {
      result = authorize
        ? await this.context.input.payments.beginOrRecover(action.actionId)
        : await this.context.input.payments.recoverBlocking(action.actionId);
    } catch {
      await this.markRepaymentUnknown(
        persisted,
        identity,
        action,
        binding,
      );
      throw paymentRecoveryError(
        "Payment provider request must be recovered before another action.",
      );
    }
    requireScopedLease(this.context.lease, this.context.terminal);

    if (result.kind === "unknown") {
      await this.markRepaymentUnknown(persisted, identity, action, binding);
      throw paymentRecoveryError("Installment payment outcome is unknown.");
    }

    if (result.kind === "declined") {
      try {
        const declined = await this.context.input.api.resolveRepaymentClaim({
          ...identity,
          outcome: "Declined",
        });
        validateRepaymentClaim(declined, action, binding);
        if (declined.status !== "Declined") {
          throw paymentRecoveryError("Repayment claim decline was not recorded.");
        }
        await this.context.input.actionStore.decline({
          actionId: action.actionId,
          expectedState:
            persisted.state === "Unknown" ? "Unknown" : "ProviderPending",
          terminal: this.context.terminal,
        });
      } catch (error) {
        throw paymentRecoveryError(
          error instanceof Error
            ? error.message
            : "Declined repayment requires recovery.",
        );
      }
      throw workflowError("authorization-declined", "Payment was declined.");
    }

    if (!("payment" in result)) {
      throw paymentRecoveryError(
        "Payment adapter returned an action of the wrong kind.",
      );
    }
    const payment = validateApprovedPayment(action, result.payment);
    claim = await this.commitRepaymentClaimWithRecovery(
      persisted,
      payment,
      binding,
    );
    return this.finishCommittedRepayment(persisted, claim, payment);
  }

  private async loadOrCreateRepaymentClaim(
    persisted: PersistedInstallmentAction,
    identity: Readonly<{ installmentGuid: string; operationGuid: string }>,
  ): Promise<InstallmentRepaymentClaim> {
    const action = persisted.action;
    const validate = (claim: InstallmentRepaymentClaim) => {
      requireScopedLease(this.context.lease, this.context.terminal);
      validateRepaymentClaim(claim, action);
      return claim;
    };
    if (persisted.state !== "Created") {
      try {
        return validate(
          await this.context.input.api.getRepaymentClaim(identity),
        );
      } catch (error) {
        throw mapRemoteError(error);
      }
    }

    try {
      // Created 可能来自上次 create 回包丢失；先 GET 同 operation，404 才创建。
      return validate(
        await this.context.input.api.getRepaymentClaim(identity),
      );
    } catch (error) {
      if (
        repaymentClaimLocalMismatch(error) ||
        repaymentClaimDeterministicFailure(error) === "ClaimMismatch"
      ) {
        await this.finalizeCreatedClaimFailure(
          persisted,
          "ClaimMismatch",
        );
        throw workflowError(
          "claim-review-required",
          "Repayment claim mismatch requires review.",
        );
      }
      if (!repaymentClaimNotFound(error)) throw mapRemoteError(error);
    }

    if (
      action.paymentGuid === null ||
      action.method === null ||
      action.amountCents === null
    ) {
      throw paymentRecoveryError("Repayment action identity is incomplete.");
    }
    try {
      return validate(
        await this.context.input.api.createRepaymentClaim({
          ...identity,
          paymentGuid: action.paymentGuid,
          amountCents: action.amountCents,
          method: action.method,
          idempotencyKey: action.idempotencyKey,
        }),
      );
    } catch (error) {
      const deterministic =
        repaymentClaimDeterministicFailure(error) ??
        (repaymentClaimLocalMismatch(error) ? "ClaimMismatch" : null);
      if (deterministic) {
        await this.finalizeCreatedClaimFailure(persisted, deterministic);
        throw workflowError(
          deterministic === "ClaimMismatch"
            ? "claim-review-required"
            : "conflict",
          deterministic === "ClaimMismatch"
            ? "Repayment claim mismatch requires review."
            : deterministic === "PaymentMethodUnsupported"
              ? "Card installment repayment is not supported by this server."
            : "Another repayment claim is active.",
        );
      }
      if (repaymentClaimCreateAmbiguous(error)) {
        try {
          return validate(
            await this.context.input.api.getRepaymentClaim(identity),
          );
        } catch (recoveryError) {
          if (repaymentClaimLocalMismatch(recoveryError)) {
            await this.finalizeCreatedClaimFailure(
              persisted,
              "ClaimMismatch",
            );
            throw workflowError(
              "claim-review-required",
              "Repayment claim mismatch requires review.",
            );
          }
          // GET 也未确认 claim；保留 Created，下一轮仍以同 operation GET/create。
        }
      }
      throw mapRemoteError(error);
    }
  }

  private async finalizeCreatedClaimFailure(
    persisted: PersistedInstallmentAction,
    reason:
      | "ClaimBusy"
      | "ClaimMismatch"
      | "ClaimReleased"
      | "PaymentMethodUnsupported",
  ): Promise<void> {
    const finalize = this.context.input.actionStore.finalizeCreatedFailure;
    if (!finalize) {
      throw paymentRecoveryError(
        "Created repayment failure cannot be durably finalized.",
      );
    }
    await finalize.call(this.context.input.actionStore, {
      actionId: persisted.action.actionId,
      reason,
      terminal: this.context.terminal,
    });
    requireScopedLease(this.context.lease, this.context.terminal);
  }

  private async markRepaymentUnknown(
    persisted: PersistedInstallmentAction,
    identity: Readonly<{ installmentGuid: string; operationGuid: string }>,
    action: InstallmentPaymentAction,
    binding: Readonly<{ provider: string; providerAttemptId: string }>,
  ): Promise<void> {
    if (persisted.state === "ProviderPending") {
      await this.transitionAction(persisted, "Unknown");
    } else if (persisted.state !== "Unknown") {
      throw paymentRecoveryError(
        "Provider uncertainty cannot be recorded from the current state.",
      );
    }
    try {
      const unknown = await this.context.input.api.resolveRepaymentClaim({
        ...identity,
        outcome: "Unknown",
      });
      validateRepaymentClaim(unknown, action, binding);
    } catch {
      // 本地 Unknown 已先耐久化；远端失败留待同一 action 的下一轮恢复。
    }
  }

  private async commitRepaymentClaimWithRecovery(
    persisted: PersistedInstallmentAction,
    payment: InstallmentPaymentCommand,
    binding: Readonly<{ provider: string; providerAttemptId: string }>,
  ): Promise<InstallmentRepaymentClaim> {
    const command = Object.freeze({
      installmentGuid: persisted.action.installmentGuid,
      operationGuid: persisted.action.actionId,
      reference: payment.reference,
      reservationToken: payment.reservationToken,
      cardTransactions: payment.cardTransactions,
    });
    const validate = (claim: InstallmentRepaymentClaim) => {
      validateRepaymentClaim(claim, persisted.action, binding);
      return claim;
    };
    try {
      return validate(
        await this.context.input.api.commitRepaymentClaim(command),
      );
    } catch (firstError) {
      let observed: InstallmentRepaymentClaim;
      try {
        observed = validate(
          await this.context.input.api.getRepaymentClaim({
            installmentGuid: command.installmentGuid,
            operationGuid: command.operationGuid,
          }),
        );
      } catch {
        throw paymentRecoveryError(
          firstError instanceof Error
            ? firstError.message
            : "Repayment claim commit requires recovery.",
        );
      }
      if (observed.status === "Committed") return observed;
      if (observed.status !== "ProviderPending") {
        throw paymentRecoveryError(
          `Repayment claim commit requires recovery from ${observed.status}.`,
        );
      }
      try {
        return validate(
          await this.context.input.api.commitRepaymentClaim(command),
        );
      } catch (error) {
        throw paymentRecoveryError(
          error instanceof Error
            ? error.message
            : "Repayment claim commit requires recovery.",
        );
      }
    }
  }

  private async finishCommittedRepayment(
    persistedInput: PersistedInstallmentAction,
    claim: InstallmentRepaymentClaim,
    approvedPayment?: InstallmentPaymentCommand,
  ): Promise<InstallmentDetails> {
    if (claim.status !== "Committed" || !claim.commit) {
      throw paymentRecoveryError(
        "Committed repayment claim is missing its server result.",
      );
    }
    let persisted = persistedInput;
    const action = persisted.action;
    const payment =
      approvedPayment ??
      Object.freeze({
        paymentGuid: claim.paymentGuid,
        method: claim.method,
        amountCents: claim.amountCents,
        reference: null,
        reservationToken: null,
        cardTransactions: Object.freeze([]),
        idempotencyKey: claim.idempotencyKey,
      });
    validatePaymentMutationResult(claim.commit.details, persisted, payment, null);
    if (persisted.state === "Created") {
      persisted = await this.transitionAction(persisted, "ProviderPending");
    }
    if (persisted.state === "ProviderPending" || persisted.state === "Unknown") {
      persisted = await this.transitionAction(persisted, "Approved");
    }
    if (persisted.state === "Approved") {
      persisted = await this.transitionAction(persisted, "BackendPending");
    }
    if (persisted.state !== "BackendPending") {
      throw paymentRecoveryError("Committed repayment action state is invalid.");
    }
    await this.cacheDetails(claim.commit.details);
    await this.context.input.actionStore.complete({
      actionId: action.actionId,
      expectedState: "BackendPending",
      terminal: this.context.terminal,
    });
    requireScopedLease(this.context.lease, this.context.terminal);
    return claim.commit.details;
  }

  private async releaseUnapprovedRepayment(
    persistedInput: PersistedInstallmentAction,
  ): Promise<void> {
    let persisted = persistedInput;
    if (persisted.state === "Created") {
      await this.finalizeCreatedClaimFailure(persisted, "ClaimReleased");
      return;
    }
    if (persisted.state !== "ProviderPending" && persisted.state !== "Unknown") {
      throw paymentRecoveryError("Approved repayment cannot be released.");
    }
    await this.context.input.actionStore.decline({
      actionId: persisted.action.actionId,
      expectedState: persisted.state,
      terminal: this.context.terminal,
    });
  }

  private async executeCancelClaimAction(
    persistedInput: PersistedInstallmentAction,
  ): Promise<InstallmentDetails> {
    let persisted = persistedInput;
    const action = persisted.action;
    if (action.kind !== "cancel-refund" || persisted.command.kind !== "cancel-refund") {
      throw paymentRecoveryError("Cancel action identity is incomplete.");
    }
    const cancelCommand = persisted.command;
    const identity = Object.freeze({
      installmentGuid: action.installmentGuid,
      operationGuid: action.actionId,
    });
    let claim = await this.loadOrCreateCancelClaim(persisted, identity);
    if (claim.status === "Committed") {
      return this.finishCommittedCancel(persisted, claim, null);
    }
    if (claim.status === "Released" || claim.status === "Declined") {
      await this.releaseUnapprovedRepayment(persisted);
      throw workflowError("conflict", `Cancel claim is ${claim.status.toLowerCase()}.`);
    }
    try {
      if (claim.status === "Prepared" || claim.status === "Unknown") {
        claim = await this.context.input.api.beginCancelClaimRefund(identity);
        validateCancelClaim(claim, action, cancelCommand);
      }
      if (claim.status !== "RefundPending") {
        throw workflowError("conflict", "Cancel claim is not ready for refund recovery.");
      }
    } catch (error) {
      if (cancelClaimDeterministicFailure(error) && persisted.state === "Created") {
        await this.finalizeCreatedClaimFailure(persisted, "ClaimBusy");
      }
      throw mapRemoteError(error);
    }

    const authorize = persisted.state === "Created";
    if (authorize) persisted = await this.transitionAction(persisted, "ProviderPending");
    let result: Awaited<ReturnType<InstallmentMutationPaymentPort["beginOrRecover"]>>;
    try {
      result = authorize
        ? await this.context.input.payments.beginOrRecover(action.actionId)
        : await this.context.input.payments.recoverBlocking(action.actionId);
    } catch {
      await this.markCancelUnknown(persisted, identity);
      throw paymentRecoveryError("Refund provider request must be recovered before another action.");
    }
    if (result.kind === "unknown") {
      await this.markCancelUnknown(persisted, identity);
      throw paymentRecoveryError("Installment refund outcome is unknown.");
    }
    if (result.kind === "declined") {
      if (result.allRefundsDeclined !== true) {
        await this.markCancelUnknown(persisted, identity);
        throw paymentRecoveryError(
          "Refund decline does not prove that no earlier refund was approved.",
        );
      }
      try {
        const declined = await this.context.input.api.resolveCancelClaim({ ...identity, outcome: "Declined" });
        validateCancelClaim(declined, action, cancelCommand);
        if (declined.status !== "Declined") throw paymentRecoveryError("Cancel claim decline was not recorded.");
        await this.context.input.actionStore.decline({
          actionId: action.actionId,
          expectedState: persisted.state === "Unknown" ? "Unknown" : "ProviderPending",
          terminal: this.context.terminal,
        });
      } catch (error) {
        throw paymentRecoveryError(error instanceof Error ? error.message : "Declined refund requires recovery.");
      }
      throw workflowError("authorization-declined", "Refund was declined.");
    }
    if (!("refunds" in result)) throw paymentRecoveryError("Payment adapter returned an action of the wrong kind.");
    const refunds = validateApprovedRefunds(action, result.refunds);
    claim = await this.commitCancelClaimWithRecovery(persisted, refunds);
    return this.finishCommittedCancel(persisted, claim, refunds);
  }

  private async loadOrCreateCancelClaim(
    persisted: PersistedInstallmentAction,
    identity: Readonly<{ installmentGuid: string; operationGuid: string }>,
  ): Promise<InstallmentCancelClaim> {
    const command = persisted.command;
    if (command.kind !== "cancel-refund") throw paymentRecoveryError("Cancel command is invalid.");
    const refundPlanFingerprint = command.refundPlanFingerprint;
    if (!refundPlanFingerprint) {
      throw paymentRecoveryError("Cancel action predates the required central claim plan.");
    }
    const validate = (claim: InstallmentCancelClaim) => {
      validateCancelClaim(claim, persisted.action, command);
      return claim;
    };
    try {
      return validate(await this.context.input.api.getCancelClaim(identity));
    } catch (error) {
      if (!repaymentClaimNotFound(error)) {
        if (cancelClaimDeterministicFailure(error) && persisted.state === "Created") {
          await this.finalizeCreatedClaimFailure(persisted, "ClaimBusy");
        }
        throw mapRemoteError(error);
      }
    }
    if (persisted.state !== "Created") {
      throw paymentRecoveryError(
        "Cancel claim is missing during provider recovery; creating a new claim is unsafe.",
      );
    }
    try {
      return validate(await this.context.input.api.createCancelClaim({
        ...identity,
        idempotencyKey: persisted.action.idempotencyKey,
        reason: command.reason,
        refundPlanFingerprint,
      }));
    } catch (error) {
      if (cancelClaimDeterministicFailure(error) && persisted.state === "Created") {
        await this.finalizeCreatedClaimFailure(persisted, "ClaimBusy");
      }
      throw mapRemoteError(error);
    }
  }

  private async markCancelUnknown(
    persisted: PersistedInstallmentAction,
    identity: Readonly<{ installmentGuid: string; operationGuid: string }>,
  ): Promise<void> {
    if (persisted.state === "ProviderPending") {
      persisted = await this.transitionAction(persisted, "Unknown");
    }
    try {
      await this.context.input.api.resolveCancelClaim({ ...identity, outcome: "Unknown" });
    } catch {
      // 本地 Unknown 已先耐久化，远端锁由同一发起机恢复时继续处理。
    }
  }

  private async commitCancelClaimWithRecovery(
    persisted: PersistedInstallmentAction,
    refunds: readonly InstallmentRefundCommand[],
  ): Promise<InstallmentCancelClaim> {
    const command = Object.freeze({
      installmentGuid: persisted.action.installmentGuid,
      operationGuid: persisted.action.actionId,
      refunds,
    });
    const validate = (claim: InstallmentCancelClaim) => {
      if (persisted.command.kind !== "cancel-refund") throw paymentRecoveryError("Cancel command is invalid.");
      validateCancelClaim(claim, persisted.action, persisted.command);
      return claim;
    };
    try {
      return validate(await this.context.input.api.commitCancelClaim(command));
    } catch (firstError) {
      let observed: InstallmentCancelClaim;
      try {
        observed = validate(await this.context.input.api.getCancelClaim(command));
      } catch {
        throw paymentRecoveryError(firstError instanceof Error ? firstError.message : "Cancel claim commit requires recovery.");
      }
      if (observed.status === "Committed") return observed;
      if (observed.status !== "RefundPending") {
        throw paymentRecoveryError(`Cancel claim commit requires recovery from ${observed.status}.`);
      }
      try {
        return validate(await this.context.input.api.commitCancelClaim(command));
      } catch (error) {
        throw paymentRecoveryError(error instanceof Error ? error.message : "Cancel claim commit requires recovery.");
      }
    }
  }

  private async finishCommittedCancel(
    persistedInput: PersistedInstallmentAction,
    claim: InstallmentCancelClaim,
    refunds: readonly InstallmentRefundCommand[] | null,
  ): Promise<InstallmentDetails> {
    if (claim.status !== "Committed" || !claim.commit) throw paymentRecoveryError("Committed cancel claim is missing its server result.");
    let persisted = persistedInput;
    const details = claim.commit.details;
    if (
      details.installmentGuid !== persisted.action.installmentGuid ||
      details.storeCode !== persisted.storeCode ||
      details.status !== "Cancelled" ||
      details.cancellationInfo?.kind !== "RefundCancel"
    ) {
      throw paymentRecoveryError("Refund cancellation was not confirmed.");
    }
    if (refunds) validatePaymentMutationResult(details, persisted, null, refunds);
    if (persisted.state === "Created") persisted = await this.transitionAction(persisted, "ProviderPending");
    if (persisted.state === "ProviderPending" || persisted.state === "Unknown") persisted = await this.transitionAction(persisted, "Approved");
    if (persisted.state === "Approved") persisted = await this.transitionAction(persisted, "BackendPending");
    if (persisted.state !== "BackendPending") throw paymentRecoveryError("Committed cancel action state is invalid.");
    await this.cacheDetails(details);
    await this.context.input.actionStore.complete({ actionId: persisted.action.actionId, expectedState: "BackendPending", terminal: this.context.terminal });
    return details;
  }

  private async assertInitialMutationScope(
    installmentGuid: string,
    crossDeviceRepaymentAllowed: boolean,
  ): Promise<InstallmentDetails> {
    let details: InstallmentDetails;
    try {
      // 中文注释：跨设备详情可只读，但首次还款/退款必须在 candidate、券材料
      // 和 durable action 产生前复核 scope；失败不能留下不可恢复的 Created action。
      const loaded = await this.context.input.api.getDetails(
        installmentGuid,
      );
      if (!loaded) {
        throw workflowError(
          "conflict",
          "Installment details are unavailable for payment scope validation.",
        );
      }
      details = loaded;
    } catch (error) {
      throw mapRemoteError(error);
    }
    requireScopedLease(this.context.lease, this.context.terminal);

    if (
      details.installmentGuid !== installmentGuid ||
      details.storeCode !== this.context.terminal.storeCode ||
      (details.deviceCode !== this.context.terminal.deviceCode &&
        !crossDeviceRepaymentAllowed)
    ) {
      throw workflowError(
        "conflict",
        "Installment payment scope does not match the current terminal.",
      );
    }
    return details;
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

function lifecycleIntentFingerprintMaterial(input: Readonly<{
  kind: PersistedInstallmentLifecycleAction["kind"];
  originalDeviceCode: string;
  storeCode: string;
  command: FrozenInstallmentVoidCommand | FrozenInstallmentPickupCommand;
}>): string {
  return JSON.stringify({
    domain: "hb-pos/installment/lifecycle-intent/v1",
    kind: input.kind,
    scope: {
      storeCode: input.storeCode,
      executingDeviceCode: input.command.deviceCode,
      originalDeviceCode: input.originalDeviceCode,
    },
    command: input.command,
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

function validatePersistedLifecycleAction(
  persisted: PersistedInstallmentLifecycleAction,
  terminal: TerminalScope,
): PersistedInstallmentLifecycleAction {
  if (
    persisted.storeCode !== terminal.storeCode ||
    persisted.deviceCode !== terminal.deviceCode ||
    persisted.command.deviceCode !== terminal.deviceCode
  ) {
    throw paymentRecoveryError(
      "Persisted lifecycle action terminal scope is invalid.",
    );
  }
  recoveryUuid(persisted.operationGuid, "lifecycle operation guid");
  if (
    persisted.idempotencyKey !== persisted.operationGuid ||
    persisted.command.operationGuid !== persisted.operationGuid ||
    persisted.command.idempotencyKey !== persisted.idempotencyKey ||
    persisted.command.installmentGuid !== persisted.installmentGuid
  ) {
    throw paymentRecoveryError(
      "Persisted lifecycle action identity is invalid.",
    );
  }
  recoveryText(persisted.installmentGuid, "lifecycle installment guid");
  recoveryText(persisted.originalDeviceCode, "original device code");
  recoveryText(persisted.command.cashierId, "persisted cashier id");
  recoveryText(persisted.command.cashierName, "persisted cashier name");
  if (!/^sha256:[0-9a-f]{64}$/iu.test(persisted.intentFingerprint)) {
    throw paymentRecoveryError(
      "Persisted lifecycle action fingerprint is invalid.",
    );
  }
  if (persisted.kind === "void") {
    const command = persisted.command as FrozenInstallmentVoidCommand;
    recoveryText(command.reason, "persisted void reason");
    if (!Number.isFinite(Date.parse(command.voidedAtIso))) {
      throw paymentRecoveryError("Persisted void timestamp is invalid.");
    }
  } else if (persisted.kind === "pickup") {
    const command = persisted.command as FrozenInstallmentPickupCommand;
    if (!Number.isFinite(Date.parse(command.confirmedAtIso))) {
      throw paymentRecoveryError("Persisted pickup timestamp is invalid.");
    }
    if (command.note !== null) {
      recoveryText(command.note, "persisted pickup note");
    }
  } else {
    throw paymentRecoveryError("Persisted lifecycle action kind is invalid.");
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

function validateRepaymentCapabilities(
  capabilities: InstallmentRepaymentCapabilities,
): InstallmentRepaymentCapabilities {
  if (
    typeof capabilities.repaymentClaimsSupported !== "boolean" ||
    typeof capabilities.repaymentClaimsRequired !== "boolean" ||
    typeof capabilities.cardRepaymentSupported !== "boolean" ||
    typeof capabilities.crossDeviceRepaymentEnabled !== "boolean" ||
    typeof capabilities.crossDeviceCancelRefundEnabled !== "boolean" ||
    typeof capabilities.crossDeviceVoidEnabled !== "boolean" ||
    typeof capabilities.crossDevicePickupEnabled !== "boolean" ||
    !Number.isSafeInteger(capabilities.preparedClaimTtlSeconds) ||
    capabilities.preparedClaimTtlSeconds < 0
  ) {
    throw new Error("Installment repayment capabilities are invalid.");
  }
  return Object.freeze({ ...capabilities });
}

async function createCancelRefundPlanFingerprint(
  input: Pick<ProductionInstallmentRuntimeDependencies, "sha256Hex">,
  details: InstallmentDetails,
): Promise<string> {
  const payments = details.payments
    .filter((payment) => payment.status === "Recorded" && payment.amountCents > 0)
    .map((payment) => [payment.paymentGuid, payment.method, payment.amountCents])
    .sort((left, right) => String(left[0]).localeCompare(String(right[0])));
  if (payments.length === 0) throw workflowError("conflict", "Installment has no refundable payments.");
  return sha256Digest(input, JSON.stringify({ installmentGuid: details.installmentGuid, payments }));
}

function validateCancelClaim(
  claim: InstallmentCancelClaim,
  action: InstallmentPaymentAction,
  command: InstallmentCancelActionCommand,
): void {
  if (
    action.kind !== "cancel-refund" ||
    claim.installmentGuid !== action.installmentGuid ||
    claim.operationGuid !== action.actionId ||
    claim.idempotencyKey !== action.idempotencyKey ||
    !command.refundPlanFingerprint ||
    claim.refundPlanFingerprint !== command.refundPlanFingerprint
  ) {
    throw workflowError("conflict", "Cancel claim does not match the durable action.");
  }
}

function cancelClaimDeterministicFailure(error: unknown): boolean {
  if (!(error instanceof HbposApiError) || error.kind !== "http") return false;
  return error.status === 409 || error.status === 400;
}

function validateClaimBinding(
  binding: Readonly<{ provider: string; providerAttemptId: string }>,
): void {
  recoveryText(binding.provider, "repayment claim provider");
  recoveryText(
    binding.providerAttemptId,
    "repayment claim provider attempt id",
  );
}

function validateRepaymentClaim(
  claim: InstallmentRepaymentClaim,
  action: InstallmentPaymentAction,
  binding?: Readonly<{ provider: string; providerAttemptId: string }>,
): void {
  if (
    action.kind !== "repayment" ||
    action.paymentGuid === null ||
    action.method === null ||
    action.amountCents === null ||
    claim.installmentGuid !== action.installmentGuid ||
    claim.operationGuid !== action.actionId ||
    claim.paymentGuid !== action.paymentGuid ||
    claim.amountCents !== action.amountCents ||
    claim.method !== action.method ||
    claim.idempotencyKey !== action.idempotencyKey
  ) {
    throw workflowError(
      "conflict",
      "Repayment claim does not match the durable action.",
    );
  }
  if (
    (claim.provider === null) !== (claim.providerAttemptId === null) ||
    (binding &&
      (claim.provider !== binding.provider ||
        claim.providerAttemptId !== binding.providerAttemptId))
  ) {
    throw workflowError(
      "conflict",
      "Repayment claim provider binding is invalid.",
    );
  }
  if (
    claim.status === "Prepared" &&
    (claim.provider !== null || claim.providerAttemptId !== null)
  ) {
    throw workflowError(
      "conflict",
      "Prepared repayment claim cannot contain a provider binding.",
    );
  }
  if (
    (claim.status === "ProviderPending" || claim.status === "Unknown") &&
    (claim.provider === null || claim.providerAttemptId === null)
  ) {
    throw workflowError(
      "conflict",
      "Repayment claim provider binding is missing.",
    );
  }
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
  const refundIdempotencyKeys = new Set<string>();
  const refundAttemptIds = new Set<string>();
  const sourceAttemptIds = new Set<string>();
  const sourcePaymentGuids = new Set<string>();
  const evidenceIds = new Set<string>();
  for (const approved of approvedRefunds) {
    const refund = approved.refund;
    if (
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
    const expectedIdempotencyKey = `${recoveryUuid(
      action.actionId,
      "cancel operation guid",
    )}:refund:${sourcePaymentGuid}`;
    const evidenceId = recoveryText(
      approved.originalTenderEvidenceId,
      "original tender evidence id",
    );
    if (
      refundPaymentGuids.has(paymentGuid) ||
      refund.idempotencyKey !== expectedIdempotencyKey ||
      refundIdempotencyKeys.has(refund.idempotencyKey) ||
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
    refundIdempotencyKeys.add(refund.idempotencyKey);
    refundAttemptIds.add(refundAttemptId);
    sourceAttemptIds.add(sourceAttemptId);
    sourcePaymentGuids.add(sourcePaymentGuid);
    evidenceIds.add(evidenceId);
  }
  return Object.freeze(
    approvedRefunds.map((approved) =>
      Object.freeze({
        ...approved.refund,
        originalPaymentGuid: approved.sourcePaymentGuid,
      }),
    ),
  );
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

function validateLifecycleMutationResult(
  details: InstallmentDetails,
  persisted: PersistedInstallmentLifecycleAction,
): void {
  const commonMatches =
    details.installmentGuid === persisted.installmentGuid &&
    details.storeCode === persisted.storeCode &&
    details.deviceCode === persisted.originalDeviceCode;
  if (persisted.kind === "void") {
    const command = persisted.command as FrozenInstallmentVoidCommand;
    const cancellation = details.cancellationInfo;
    if (
      !commonMatches ||
      details.status !== "Cancelled" ||
      cancellation?.kind !== "VoidCancel" ||
      cancellation.cancelledAtIso !== command.voidedAtIso ||
      cancellation.reason !== command.reason
    ) {
      throw paymentRecoveryError(
        "Void response does not match the frozen lifecycle command.",
      );
    }
    return;
  }

  const command = persisted.command as FrozenInstallmentPickupCommand;
  const pickup = details.pickupInfo;
  if (
    !commonMatches ||
    details.status !== "PickedUp" ||
    pickup?.pickedUpAtIso !== command.confirmedAtIso ||
    pickup.note !== command.note
  ) {
    throw paymentRecoveryError(
      "Pickup response does not match the frozen lifecycle command.",
    );
  }
}

function mapRemoteError(error: unknown): Error {
  if (error instanceof InstallmentWorkflowError) return error;
  if (error instanceof HbposApiError) {
    const code = error.code?.trim().toLowerCase() ?? "";
    if (code === "device_scope_forbidden") {
      return workflowError(
        "conflict",
        "Installment device scope does not match the current terminal.",
      );
    }
    if (error.status === 401 || error.status === 403) {
      return workflowError(
        "authorization-declined",
        "Installment permission is unavailable.",
      );
    }
    if (error.status === 409 || error.code?.toLowerCase().includes("conflict")) {
      return workflowError(
        "conflict",
        "Installment request conflicted with current state.",
      );
    }
    if (error.kind === "transport") {
      return workflowError(
        "online-required",
        "Installment requires an online connection.",
      );
    }
    if (
      (error.status !== undefined && error.status >= 500) ||
      code.includes("service_unavailable") ||
      code.includes("service-unavailable")
    ) {
      return workflowError(
        "service-unavailable",
        "Installment service is temporarily unavailable.",
      );
    }
  }
  return new Error("Installment remote request failed.");
}

function repaymentClaimNotFound(error: unknown): boolean {
  return (
    error instanceof HbposApiError &&
    (error.status === 404 ||
      error.code?.trim().toUpperCase() === "CLAIM_NOT_FOUND")
  );
}

function repaymentClaimDeterministicFailure(
  error: unknown,
): "ClaimBusy" | "ClaimMismatch" | "PaymentMethodUnsupported" | null {
  if (!(error instanceof HbposApiError)) return null;
  const code = error.code?.trim().toUpperCase();
  if (
    error.status === 400 &&
    code === "INSTALLMENT_REPAYMENT_PAYMENT_METHOD_UNSUPPORTED"
  ) {
    return "PaymentMethodUnsupported";
  }
  if (error.status !== 409) return null;
  if (code === "INSTALLMENT_REPAYMENT_BUSY") return "ClaimBusy";
  if (code === "INSTALLMENT_REPAYMENT_CLAIM_MISMATCH") {
    return "ClaimMismatch";
  }
  return null;
}

function repaymentClaimCreateAmbiguous(error: unknown): boolean {
  return (
    error instanceof HbposApiError &&
    (error.kind === "transport" ||
      (error.status !== undefined && error.status >= 500))
  );
}

function repaymentClaimLocalMismatch(error: unknown): boolean {
  return (
    error instanceof InstallmentWorkflowError && error.code === "conflict"
  );
}

function deviceCodeForScope(
  scope: InstallmentDeviceScope,
  terminal: TerminalScope,
): string | null {
  if (scope === "store") return null;
  if (scope === "device") return terminal.deviceCode;
  throw workflowError("conflict", "Installment device scope is invalid.");
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

function optionalText(value: string | null): string | null {
  const normalized = value?.trim() ?? "";
  return normalized.length === 0 ? null : normalized;
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
