import { paymentProviderAmountCents } from "./payment-amount";

import { normalizeCardSyncEvidence, type CardSyncEvidenceV1, type OnlinePaymentPort, type PaymentAttempt, type PaymentOperation, type PaymentProvider, type PaymentProviderReferences, type PaymentProviderResult } from "@hb/pos-domain/core/contracts/payment";
import { type Money } from "@hb/pos-domain/core/contracts/money";
import { type PaymentAttemptRepositoryPort } from "@hb/pos-domain/core/contracts/repositories";
import { auditActorPayload, type AuditActorSnapshot } from "@hb/pos-domain/core/contracts/audit-actor";
import { canTransitionPaymentAttempt } from "@hb/pos-domain/core/contracts/state-machines";

export type PaymentAttemptLedgerPort = Pick<
  PaymentAttemptRepositoryPort,
  "insertIfUnblocked" | "compareAndUpdate" | "get" | "findBlocking"
>;

export interface PersistedOrderDraftPort {
  /** 不存在持久化订单草稿时必须抛错；不得在此方法内临时创建草稿。 */
  assertPersisted(orderGuid: string): Promise<void>;
}

export interface PaymentProviderRegistryPort {
  get(provider: PaymentProvider): OnlinePaymentPort;
}

/**
 * 这是 provider 私有的恢复能力，不扩展通用 OnlinePaymentPort，避免把取消契约强加给
 * Linkly、Voucher 等既有实现。只有运行时明确暴露此方法的 provider 才会收到 signal。
 */
export type PaymentRecoveryControl = Readonly<{
  signal: AbortSignal;
  deadlineAtMs: number;
}>;

type AbortablePaymentRecoveryPort = Readonly<{
  recoverWithControl(
    attempt: PaymentAttempt,
    control: PaymentRecoveryControl,
  ): Promise<PaymentProviderResult>;
}>;

export interface PaymentConnectivityPort {
  isOnline(): Promise<boolean>;
}

export type PaymentActionBinding = Readonly<{
  orderGuid: string;
  actionId: string;
  requestSignature: string;
  attemptId: string;
  idempotencyKey: string;
  createdAtIso: string;
  actor: AuditActorSnapshot;
}>;

/**
 * 数据库实现必须在唯一键 (orderGuid, actionId) 下原子地“插入或返回已有值”。
 *
 * 同键冲突时不得覆盖已有绑定，必须返回原记录，由领域层核对 requestSignature。
 * 这个 Port 是 provider 边界前的耐久防重门；内存实现只允许用于测试。
 */
export interface PaymentActionBindingPort {
  bindOrGet(proposed: PaymentActionBinding): Promise<PaymentActionBinding>;
  getByAttempt(attemptId: string): Promise<PaymentActionBinding | null>;
}

export type TrustedRefundSeedIdentity = Readonly<{
  attemptId: string;
  idempotencyKey: string;
  orderGuid: string;
  createdAtIso: string;
}>;

export type TrustedOriginalTenderRefundProvider =
  | "square"
  | "linkly-cloud";

/**
 * 该范围只包含已纳入耐久 action 签名的 opaque capacityId 与不可变交易身份；
 * 受信任 hook 必须用它从 Vault 解析原支付引用，页面不能直接提供引用。
 */
export type TrustedRefundCapacityBinding = Readonly<{
  capacityId: string;
  actionId: string;
  orderGuid: string;
  provider: TrustedOriginalTenderRefundProvider;
  operation: "refund";
  amount: Money;
}>;

export type TrustedRefundReferenceSeedInput = Readonly<{
  identity: TrustedRefundSeedIdentity;
  provider: TrustedOriginalTenderRefundProvider;
  operation: "refund";
  action: PaymentActionBinding;
  capacity: TrustedRefundCapacityBinding;
}>;

/**
 * 判别联合故意不接受通用 references 对象：原卡退款只能返回对应 provider
 * 所需的唯一受保护引用。Voucher refund 是签发新券，必须从空 references 开始。
 */
export type TrustedRefundReferenceSeed =
  | Readonly<{
      provider: "square";
      paymentId: string;
    }>
  | Readonly<{
      provider: "linkly-cloud";
      rfn: string;
    }>;

export type TrustedRefundReferenceSeedHook = (
  input: TrustedRefundReferenceSeedInput,
) => Promise<TrustedRefundReferenceSeed>;

export type PaymentAttemptServiceOptions = Readonly<{
  ledger: PaymentAttemptLedgerPort;
  actionBindings: PaymentActionBindingPort;
  drafts: PersistedOrderDraftPort;
  providers: PaymentProviderRegistryPort;
  connectivity: PaymentConnectivityPort;
  createAttemptId(): string;
  createIdempotencyKey(): string;
  nowIso(): string;
  /**
   * 只能由生产组合根注入受保护 capacity vault；route/UI/startAttempt 不得提供 provider 引用。
   * 该 hook 仅用于 Square/Linkly 原卡退款，Voucher refund 不会调用。
   */
  trustedRefundReferenceSeed?: TrustedRefundReferenceSeedHook;
}>;

export type StartPaymentAttemptInput = Readonly<{
  actionId: string;
  orderGuid: string;
  provider: PaymentProvider;
  operation: PaymentOperation;
  amount: Money;
  /** 在 action 首次绑定时冻结；恢复与完成订单时禁止回读当前登录会话。 */
  actor: AuditActorSnapshot;
  /** 原支付容量的 opaque Vault 句柄；只允许 refund 使用，绝不能承载 provider reference。 */
  refundCapacityId?: string;
}>;

export type PaymentAttemptExecutionResult = Readonly<{
  attempt: PaymentAttempt;
  receiptText: string | null;
  responseCode: string | null;
}>;

export class PaymentAttemptBlockedError extends Error {
  public constructor(public readonly blockingAttempt: PaymentAttempt) {
    super(`Order ${blockingAttempt.orderGuid} already has a blocking payment attempt.`);
    this.name = "PaymentAttemptBlockedError";
  }
}

export class PaymentAttemptStateError extends Error {
  public constructor(message: string) {
    super(message);
    this.name = "PaymentAttemptStateError";
  }
}

export type PaymentAttemptReferenceSeedErrorCode =
  | "TRUSTED_REFUND_REFERENCE_SEED_FAILED"
  | "TRUSTED_REFUND_REFERENCE_SEED_INVALID"
  | "TRUSTED_REFUND_REFERENCE_SEED_CONFLICT";

export class PaymentAttemptReferenceSeedError extends PaymentAttemptStateError {
  public constructor(public readonly code: PaymentAttemptReferenceSeedErrorCode) {
    super(`Trusted refund reference seed was rejected (${code}).`);
    this.name = "PaymentAttemptReferenceSeedError";
  }
}

export class PaymentActionBindingConflictError extends PaymentAttemptStateError {
  public readonly attemptId: string;

  public constructor(
    public readonly binding: PaymentActionBinding,
    message = "Payment action is already bound to a different immutable request.",
  ) {
    super(message);
    this.name = "PaymentActionBindingConflictError";
    this.attemptId = binding.attemptId;
  }
}

export class PaymentAttemptOfflineError extends Error {
  public constructor() {
    super("Online payment requires a network connection.");
    this.name = "PaymentAttemptOfflineError";
  }
}

export class PaymentAttemptNotFoundError extends Error {
  public constructor(attemptId: string) {
    super(`Payment attempt not found: ${attemptId}`);
    this.name = "PaymentAttemptNotFoundError";
  }
}

export class PaymentAttemptDurabilityError extends Error {
  public readonly recoveryRequired = true;
  public readonly cause: unknown;

  public constructor(
    public readonly attemptId: string,
    public readonly orderGuid: string,
    expectedState: PaymentAttempt["state"],
    nextState: PaymentAttempt["state"],
    cause?: unknown,
  ) {
    const detail = errorMessage(cause);
    super(
      `Payment attempt ${attemptId} could not durably transition ${expectedState} to ${nextState}; recovery is required${detail ? `: ${detail}` : "."}`,
    );
    this.name = "PaymentAttemptDurabilityError";
    this.cause = cause;
  }
}

type Inflight<T> = Readonly<{
  signature: string;
  promise: Promise<T>;
  ownerSignal: AbortSignal | null;
}>;

function isAbortablePaymentRecoveryPort(
  provider: OnlinePaymentPort,
): provider is OnlinePaymentPort & AbortablePaymentRecoveryPort {
  return (
    "recoverWithControl" in provider &&
    typeof provider.recoverWithControl === "function"
  );
}

// 中文注释：同一 JS runtime 内所有 service 实例共享订单锁，避免不同页面/容器各自实例化后并发触发终端。
const sharedOrderActions = new Map<string, Inflight<PaymentAttemptExecutionResult>>();

/**
 * 在线支付的耐久编排层。
 *
 * 本服务只在订单草稿、attempt、幂等键以及 Submitted 状态全部落库后调用 provider。
 * 任一通信歧义都进入 Unknown；恢复前既不重扣，也不自动取消、退款或切换 provider。
 */
export class PaymentAttemptService {
  public constructor(private readonly options: PaymentAttemptServiceOptions) {}

  /**
   * 只建立耐久 action/attempt，不越过 provider 边界。
   *
   * 返回完整 execution shape 是为了让内部 orchestrator 复用既有 attempt 身份与状态；
   * 本方法不会新增任何 route/UI 暴露面。
   */
  public prepareAttempt(
    input: StartPaymentAttemptInput,
  ): Promise<PaymentAttemptExecutionResult> {
    return runSharedOrderAction(
      input.orderGuid,
      `prepare:${startSignature(input)}`,
      async () => outcome(await this.prepareBoundAttemptOnce(input)),
    );
  }

  public startAttempt(input: StartPaymentAttemptInput): Promise<PaymentAttemptExecutionResult> {
    return runSharedOrderAction(
      input.orderGuid,
      `start:${startSignature(input)}`,
      () => this.startAttemptOnce(input),
    );
  }

  public async recoverAttempt(
    attemptId: string,
    control?: PaymentRecoveryControl,
  ): Promise<PaymentAttemptExecutionResult> {
    assertPaymentRecoveryControl(control);
    const observed = await this.requireAttempt(attemptId);
    return runSharedOrderAction(
      observed.orderGuid,
      `recover:${attemptId}`,
      async () => {
        const current = await this.requireAttempt(attemptId);
        assertSameImmutableAttemptIdentity(observed, current);
        return this.recoverAttemptOnce(current, control);
      },
      control?.signal,
    );
  }

  public async cancelAttempt(attemptId: string): Promise<PaymentAttemptExecutionResult> {
    const observed = await this.requireAttempt(attemptId);
    return runSharedOrderAction(observed.orderGuid, `cancel:${attemptId}`, async () => {
      const current = await this.requireAttempt(attemptId);
      assertSameImmutableAttemptIdentity(observed, current);
      return this.cancelAttemptOnce(current);
    });
  }

  public getAttempt(attemptId: string): Promise<PaymentAttempt | null> {
    return this.options.ledger.get(attemptId);
  }

  public getBlockingAttempt(orderGuid: string): Promise<PaymentAttempt | null> {
    return this.options.ledger.findBlocking(orderGuid);
  }

  public async getActionActor(
    attemptId: string,
    orderGuid: string,
  ): Promise<AuditActorSnapshot> {
    const binding = await this.options.actionBindings.getByAttempt(attemptId);
    if (
      !binding ||
      binding.attemptId !== attemptId ||
      binding.orderGuid !== orderGuid
    ) {
      throw new PaymentAttemptStateError(
        "Payment attempt has no matching immutable action actor.",
      );
    }
    return normalizedActor(binding.actor);
  }

  private async startAttemptOnce(
    input: StartPaymentAttemptInput,
  ): Promise<PaymentAttemptExecutionResult> {
    const prepared = await this.prepareBoundAttemptOnce(input);
    return this.resumeBoundAttempt(prepared);
  }

  private async prepareBoundAttemptOnce(
    input: StartPaymentAttemptInput,
  ): Promise<PaymentAttempt> {
    assertStartInput(input);
    await this.options.drafts.assertPersisted(input.orderGuid);

    const binding = await this.bindAction(input);
    let boundAttempt = await this.options.ledger.get(binding.attemptId);
    if (boundAttempt) {
      assertBoundAttemptIdentity(boundAttempt, binding, input);
      return boundAttempt;
    }

    // 已知离线时保留 action 绑定，但不创建会阻塞订单的 Created attempt。
    await this.assertOnline();
    const emptyCreated = attemptFromBinding(binding, input);
    // 受保护原支付引用只能在 action/在线门禁完成后、首次 Created 落库前注入。
    const created = await this.seedTrustedRefundReferences(
      emptyCreated,
      binding,
      input.refundCapacityId,
    );

    // 中文注释：跨 service/process 的最终防线由仓储独占事务原子检查阻塞项并插入 Created。
    try {
      const blocking = await this.options.ledger.insertIfUnblocked(created);
      if (blocking) {
        if (blocking.attemptId !== binding.attemptId) {
          throw new PaymentAttemptBlockedError(blocking);
        }
        assertBoundAttemptIdentity(blocking, binding, input);
        boundAttempt = blocking;
      } else {
        boundAttempt = created;
      }
    } catch (error) {
      if (error instanceof PaymentAttemptBlockedError) throw error;

      // 唯一键竞争或提交结果不明时先读取绑定 attempt；存在且身份一致即可恢复。
      const concurrent = await this.options.ledger.get(binding.attemptId);
      if (!concurrent) {
        throw new PaymentAttemptDurabilityError(
          binding.attemptId,
          binding.orderGuid,
          "Created",
          "Created",
          error,
        );
      }
      assertBoundAttemptIdentity(concurrent, binding, input);
      boundAttempt = concurrent;
    }

    return boundAttempt;
  }

  private async recoverAttemptOnce(
    attempt: PaymentAttempt,
    control?: PaymentRecoveryControl,
  ): Promise<PaymentAttemptExecutionResult> {
    if (attempt.state === "Approved") {
      // 已批准但订单尚未完成时不再次触碰终端；上层应以同一 OrderGuid 继续落单。
      return outcome(attempt);
    }
    if (attempt.state === "Declined" || attempt.state === "Cancelled") {
      return outcome(attempt);
    }
    if (!["Created", "Submitted", "Pending", "Unknown"].includes(attempt.state)) {
      throw new PaymentAttemptStateError(
        `Payment attempt in ${attempt.state} state cannot be recovered.`,
      );
    }
    await this.options.drafts.assertPersisted(attempt.orderGuid);
    return this.resumeBoundAttempt(attempt, control);
  }

  private async bindAction(
    input: StartPaymentAttemptInput,
  ): Promise<PaymentActionBinding> {
    const proposed: PaymentActionBinding = {
      orderGuid: input.orderGuid,
      actionId: input.actionId,
      requestSignature: actionRequestSignature(input),
      attemptId: requiredGeneratedValue(this.options.createAttemptId(), "attempt id"),
      idempotencyKey: requiredGeneratedValue(
        this.options.createIdempotencyKey(),
        "idempotency key",
      ),
      createdAtIso: requiredIsoTimestamp(this.options.nowIso(), "binding creation"),
      actor: normalizedActor(input.actor),
    };

    let persisted: PaymentActionBinding;
    try {
      persisted = await this.options.actionBindings.bindOrGet(proposed);
    } catch (error) {
      // 写入可能已经提交但响应丢失；将提议 attemptId 交给恢复层，不生成第二套 ID。
      throw new PaymentAttemptDurabilityError(
        proposed.attemptId,
        proposed.orderGuid,
        "Created",
        "Created",
        error,
      );
    }
    assertBindingMatchesRequest(persisted, proposed);
    return persisted;
  }

  private async resumeBoundAttempt(
    attempt: PaymentAttempt,
    recoveryControl?: PaymentRecoveryControl,
  ): Promise<PaymentAttemptExecutionResult> {
    if (
      attempt.state === "Approved" ||
      attempt.state === "Declined" ||
      attempt.state === "Cancelled"
    ) {
      return outcome(attempt);
    }

    await this.assertOnline();
    const provider = this.providerFor(attempt.provider);
    if (attempt.state === "Created") {
      // Created → Submitted 的 CAS 成功后才能第一次越过 provider 边界。
      const submitted = transition(
        attempt,
        "Submitted",
        this.nextUpdatedAtIso(attempt),
        null,
      );
      await this.compareAndUpdateOrThrow(attempt, submitted);
      // 中文注释：自动恢复仍须先耐久进入 Submitted；CAS 成功后才把同一 signal
      // 交给 provider 的恢复能力，使首次 checkout/refund 请求也受截止时间约束。
      if (recoveryControl && isAbortablePaymentRecoveryPort(provider)) {
        return this.executeProvider(submitted, (value) =>
          provider.recoverWithControl(value, recoveryControl),
          recoveryControl,
        );
      }
      return this.executeProvider(
        submitted,
        submitted.operation === "refund"
          ? (value) => provider.refund(value)
          : (value) => provider.submit(value),
      );
    }

    // Submitted/Pending/Unknown 已可能到达终端，只能查询恢复，禁止再次 submit/refund。
    // 中文注释：取消能力是 Square 的可选扩展，其他 provider 继续调用原 recover，避免扩大通用契约。
    if (recoveryControl && isAbortablePaymentRecoveryPort(provider)) {
      return this.executeProvider(attempt, (value) =>
        provider.recoverWithControl(value, recoveryControl),
        recoveryControl,
      );
    }
    return this.executeProvider(attempt, (value) => provider.recover(value));
  }

  private async assertOnline(): Promise<void> {
    if (!(await this.options.connectivity.isOnline())) {
      throw new PaymentAttemptOfflineError();
    }
  }

  private async seedTrustedRefundReferences(
    attempt: PaymentAttempt,
    binding: PaymentActionBinding,
    refundCapacityId: string | undefined,
  ): Promise<PaymentAttempt> {
    const hook = this.options.trustedRefundReferenceSeed;
    if (
      attempt.operation !== "refund" ||
      attempt.provider === "voucher" ||
      !hook
    ) {
      return attempt;
    }
    if (refundCapacityId === undefined) {
      throw new PaymentAttemptReferenceSeedError(
        "TRUSTED_REFUND_REFERENCE_SEED_INVALID",
      );
    }

    let seed: TrustedRefundReferenceSeed;
    try {
      seed = await hook(
        trustedRefundSeedInput(attempt, binding, refundCapacityId),
      );
    } catch {
      // 不传播 Vault 异常文本，避免受保护引用或存储细节进入 UI/日志。
      throw new PaymentAttemptReferenceSeedError(
        "TRUSTED_REFUND_REFERENCE_SEED_FAILED",
      );
    }
    try {
      return applyTrustedRefundReferenceSeed(attempt, seed);
    } catch (error) {
      if (error instanceof PaymentAttemptReferenceSeedError) throw error;
      throw new PaymentAttemptReferenceSeedError(
        "TRUSTED_REFUND_REFERENCE_SEED_INVALID",
      );
    }
  }

  private async cancelAttemptOnce(
    attempt: PaymentAttempt,
  ): Promise<PaymentAttemptExecutionResult> {
    if (attempt.state === "Created") {
      const cancelled = transition(
        attempt,
        "Cancelled",
        this.nextUpdatedAtIso(attempt),
        null,
      );
      await this.compareAndUpdateOrThrow(attempt, cancelled);
      return outcome(cancelled);
    }
    if (!["Submitted", "Pending"].includes(attempt.state)) {
      throw new PaymentAttemptStateError(
        `Payment attempt in ${attempt.state} state cannot be cancelled.`,
      );
    }
    if (!(await this.options.connectivity.isOnline())) throw new PaymentAttemptOfflineError();

    const provider = this.providerFor(attempt.provider);
    return this.executeProvider(attempt, (value) => provider.cancel(value));
  }

  private async executeProvider(
    attempt: PaymentAttempt,
    execute: (attempt: PaymentAttempt) => Promise<PaymentProviderResult>,
    controlledRecovery?: PaymentRecoveryControl,
  ): Promise<PaymentAttemptExecutionResult> {
    let providerResult: PaymentProviderResult;
    try {
      providerResult = await execute(attempt);
    } catch (error) {
      return this.persistUnknown(attempt, providerErrorCode(error));
    }

    if (isNeutralControlledRecoveryUnknown(providerResult, controlledRecovery)) {
      // 中文注释：页面卸载取消的仅是本次 Square 查询，不代表支付结论未知。
      // 保持原 attempt 可让重挂载在剩余自动恢复窗口内继续查询同一笔交易。
      return outcome(attempt);
    }

    let references: PaymentProviderReferences;
    try {
      references = mergeReferences(attempt.references, providerResult.references);
      assertResultTransition(attempt, providerResult.state);
    } catch (error) {
      const code =
        error instanceof ProviderReferenceConflictError
          ? "PROVIDER_REFERENCE_CONFLICT"
          : "PROVIDER_STATE_CONFLICT";
      return this.persistUnknown(attempt, code, providerResult);
    }
    let protectedSyncEvidence: CardSyncEvidenceV1 | undefined;
    try {
      protectedSyncEvidence = protectedEvidenceForAttempt(
        attempt,
        providerResult,
      );
    } catch {
      return this.persistUnknown(
        attempt,
        "PROVIDER_SYNC_EVIDENCE_INVALID",
        providerResult,
      );
    }

    const responseCode = normalizedProviderCode(providerResult.responseCode);
    const updated: PaymentAttempt = {
      ...attempt,
      state: providerResult.state,
      references,
      updatedAtIso: this.nextUpdatedAtIso(attempt),
      receiptText: providerResult.receiptText,
      responseCode,
      lastErrorCode:
        providerResult.state === "Declined" || providerResult.state === "Unknown"
          ? responseCode
          : null,
    };

    // 此写入失败可能代表“provider 已批准、App 随即崩溃”；保留原 Submitted/Pending
    // 也是阻塞态，恢复只能针对相同 attempt 和 OrderGuid，不能重新发起扣款。
    await this.compareAndUpdateOrThrow(
      attempt,
      updated,
      protectedSyncEvidence,
    );
    return outcome(updated);
  }

  private async persistUnknown(
    attempt: PaymentAttempt,
    errorCode: string,
    providerResult?: PaymentProviderResult,
  ): Promise<PaymentAttemptExecutionResult> {
    if (
      attempt.state !== "Unknown" &&
      !canTransitionPaymentAttempt(attempt.state, "Unknown")
    ) {
      throw new PaymentAttemptStateError(
        `Payment attempt in ${attempt.state} state cannot enter Unknown.`,
      );
    }

    const unknown: PaymentAttempt = {
      ...attempt,
      state: "Unknown",
      updatedAtIso: this.nextUpdatedAtIso(attempt),
      lastErrorCode: errorCode,
      receiptText: providerResult?.receiptText ?? attempt.receiptText ?? null,
      responseCode:
        providerResult === undefined
          ? attempt.responseCode ?? null
          : normalizedProviderCode(providerResult.responseCode),
    };
    await this.compareAndUpdateOrThrow(attempt, unknown);
    return outcome(unknown);
  }

  private providerFor(providerName: PaymentProvider): OnlinePaymentPort {
    const provider = this.options.providers.get(providerName);
    if (provider.provider !== providerName) {
      throw new PaymentAttemptStateError(
        `Payment provider registry returned ${provider.provider} for ${providerName}.`,
      );
    }
    return provider;
  }

  private async requireAttempt(attemptId: string): Promise<PaymentAttempt> {
    const attempt = await this.options.ledger.get(attemptId);
    if (!attempt) throw new PaymentAttemptNotFoundError(attemptId);
    return attempt;
  }

  private async compareAndUpdateOrThrow(
    expected: PaymentAttempt,
    next: PaymentAttempt,
    protectedSyncEvidence?: CardSyncEvidenceV1,
  ): Promise<void> {
    try {
      if (
        await this.options.ledger.compareAndUpdate(
          expected,
          next,
          protectedSyncEvidence,
        )
      ) {
        return;
      }
    } catch (error) {
      throw new PaymentAttemptDurabilityError(
        expected.attemptId,
        expected.orderGuid,
        expected.state,
        next.state,
        error,
      );
    }
    throw new PaymentAttemptDurabilityError(
      expected.attemptId,
      expected.orderGuid,
      expected.state,
      next.state,
    );
  }

  private nextUpdatedAtIso(attempt: PaymentAttempt): string {
    const previous = Date.parse(attempt.updatedAtIso);
    const candidate = Date.parse(this.options.nowIso());
    if (!Number.isFinite(previous) || !Number.isFinite(candidate)) {
      throw new TypeError("Payment attempt timestamps must be valid ISO dates.");
    }
    return new Date(Math.max(candidate, previous + 1)).toISOString();
  }
}

function runSharedOrderAction(
  orderGuid: string,
  signature: string,
  operation: () => Promise<PaymentAttemptExecutionResult>,
  ownerSignal?: AbortSignal,
): Promise<PaymentAttemptExecutionResult> {
  const active = sharedOrderActions.get(orderGuid);
  if (active) {
    if (
      active.signature === signature &&
      active.ownerSignal === (ownerSignal ?? null)
    ) {
      return active.promise;
    }
    return Promise.reject(
      new PaymentAttemptStateError(
        "Another payment action is already running for this order; recovery, cancellation, refund and provider switching are mutually exclusive.",
      ),
    );
  }

  // 先登记锁、后进入异步业务，确保同一事件循环内的两个 service 实例也只能有一个执行者。
  const promise = Promise.resolve().then(operation);
  const entry = { signature, promise, ownerSignal: ownerSignal ?? null };
  sharedOrderActions.set(orderGuid, entry);
  promise.then(
    () => deleteSharedOrderActionIfCurrent(orderGuid, entry),
    () => deleteSharedOrderActionIfCurrent(orderGuid, entry),
  );
  return promise;
}

function assertPaymentRecoveryControl(
  control: PaymentRecoveryControl | undefined,
): void {
  if (!control) return;
  if (!Number.isFinite(control.deadlineAtMs)) {
    throw new PaymentAttemptStateError(
      "Payment recovery requires a finite absolute deadline.",
    );
  }
}

function deleteSharedOrderActionIfCurrent(
  orderGuid: string,
  expected: Inflight<PaymentAttemptExecutionResult>,
): void {
  if (sharedOrderActions.get(orderGuid) === expected) sharedOrderActions.delete(orderGuid);
}

function assertSameImmutableAttemptIdentity(
  observed: PaymentAttempt,
  current: PaymentAttempt,
): void {
  if (sameImmutableAttemptIdentity(observed, current)) return;
  throw new PaymentAttemptDurabilityError(
    current.attemptId,
    observed.orderGuid,
    observed.state,
    current.state,
    new Error("immutable attempt identity changed"),
  );
}

class ProviderReferenceConflictError extends Error {}

const referenceKeys = [
  "checkoutId",
  "paymentId",
  "sessionId",
  "txnRef",
  "rfn",
  "voucherReservationToken",
] as const satisfies readonly (keyof PaymentProviderReferences)[];

function mergeReferences(
  current: PaymentProviderReferences,
  incoming: PaymentProviderReferences,
): PaymentProviderReferences {
  const merged = { ...current };
  for (const key of referenceKeys) {
    const previous = current[key];
    const next = incoming[key];
    if (previous !== null && next !== null && previous !== next) {
      throw new ProviderReferenceConflictError(`Conflicting provider reference: ${key}`);
    }
    if (next !== null) merged[key] = next;
  }
  return merged;
}

function protectedEvidenceForAttempt(
  attempt: PaymentAttempt,
  result: PaymentProviderResult,
): CardSyncEvidenceV1 | undefined {
  const supplied = result.protectedSyncEvidence;
  if (result.state !== "Approved") {
    if (supplied !== undefined && supplied !== null) {
      throw new PaymentAttemptStateError(
        "Only an approved card result may carry protected sync evidence.",
      );
    }
    return undefined;
  }
  if (attempt.provider === "voucher") {
    if (supplied !== undefined && supplied !== null) {
      throw new PaymentAttemptStateError(
        "Voucher results cannot carry card sync evidence.",
      );
    }
    return undefined;
  }
  if (supplied === undefined || supplied === null) {
    throw new PaymentAttemptStateError(
      "Approved card result is missing protected sync evidence.",
    );
  }
  const evidence = normalizeCardSyncEvidence(supplied);
  const magnitude = Math.abs(attempt.amount.cents);
  if (
    !Number.isSafeInteger(magnitude) ||
    magnitude <= 0 ||
    evidence.provider !== attempt.provider ||
    evidence.operation !== attempt.operation ||
    evidence.amountCents !== magnitude
  ) {
    throw new PaymentAttemptStateError(
      "Protected sync evidence does not match the payment attempt.",
    );
  }
  return evidence;
}

function assertResultTransition(
  attempt: PaymentAttempt,
  next: PaymentProviderResult["state"],
): void {
  if (attempt.state === next) return;
  if (!canTransitionPaymentAttempt(attempt.state, next)) {
    throw new PaymentAttemptStateError(
      `Provider result cannot transition ${attempt.state} to ${next}.`,
    );
  }
}

function transition(
  attempt: PaymentAttempt,
  state: PaymentAttempt["state"],
  updatedAtIso: string,
  lastErrorCode: string | null,
): PaymentAttempt {
  if (!canTransitionPaymentAttempt(attempt.state, state)) {
    throw new PaymentAttemptStateError(
      `Payment attempt cannot transition ${attempt.state} to ${state}.`,
    );
  }
  return { ...attempt, state, updatedAtIso, lastErrorCode };
}

function outcome(
  attempt: PaymentAttempt,
): PaymentAttemptExecutionResult {
  return {
    attempt,
    receiptText: attempt.receiptText ?? null,
    responseCode: attempt.responseCode ?? null,
  };
}

function emptyReferences(): PaymentProviderReferences {
  return {
    checkoutId: null,
    paymentId: null,
    sessionId: null,
    txnRef: null,
    rfn: null,
    voucherReservationToken: null,
  };
}

function trustedRefundSeedInput(
  attempt: PaymentAttempt,
  binding: PaymentActionBinding,
  refundCapacityId: string,
): TrustedRefundReferenceSeedInput {
  if (
    attempt.operation !== "refund" ||
    attempt.provider === "voucher"
  ) {
    throw new PaymentAttemptReferenceSeedError(
      "TRUSTED_REFUND_REFERENCE_SEED_INVALID",
    );
  }
  const identity = Object.freeze({
    attemptId: attempt.attemptId,
    idempotencyKey: attempt.idempotencyKey,
    orderGuid: attempt.orderGuid,
    createdAtIso: attempt.createdAtIso,
  });
  const amount = Object.freeze({ ...attempt.amount });
  const action = Object.freeze({ ...binding });
  const capacity = Object.freeze({
    capacityId: trustedSeedText(refundCapacityId),
    actionId: binding.actionId,
    orderGuid: attempt.orderGuid,
    provider: attempt.provider,
    operation: "refund" as const,
    amount,
  });
  return Object.freeze({
    identity,
    provider: attempt.provider,
    operation: "refund" as const,
    action,
    capacity,
  });
}

function applyTrustedRefundReferenceSeed(
  attempt: PaymentAttempt,
  seedValue: unknown,
): PaymentAttempt {
  if (
    attempt.operation !== "refund" ||
    attempt.provider === "voucher"
  ) {
    throw new PaymentAttemptReferenceSeedError(
      "TRUSTED_REFUND_REFERENCE_SEED_INVALID",
    );
  }
  if (referenceKeys.some((key) => attempt.references[key] !== null)) {
    throw new PaymentAttemptReferenceSeedError(
      "TRUSTED_REFUND_REFERENCE_SEED_CONFLICT",
    );
  }

  const referenceKey = trustedRefundReferenceKey(attempt.provider);
  const seed = trustedSeedRecord(seedValue);
  assertExactTrustedSeedKeys(seed, ["provider", referenceKey]);
  if (seed.provider !== attempt.provider) {
    throw new PaymentAttemptReferenceSeedError(
      "TRUSTED_REFUND_REFERENCE_SEED_CONFLICT",
    );
  }

  const protectedReference = trustedSeedText(seed[referenceKey]);

  return {
    ...attempt,
    references: referencesWithTrustedRefundSeed(
      attempt.provider,
      protectedReference,
    ),
  };
}

function trustedRefundReferenceKey(
  provider: TrustedOriginalTenderRefundProvider,
): "paymentId" | "rfn" {
  switch (provider) {
    case "square":
      return "paymentId";
    case "linkly-cloud":
      return "rfn";
  }
}

function referencesWithTrustedRefundSeed(
  provider: TrustedOriginalTenderRefundProvider,
  protectedReference: string,
): PaymentProviderReferences {
  switch (provider) {
    case "square":
      return {
        ...emptyReferences(),
        paymentId: protectedReference,
      };
    case "linkly-cloud":
      return {
        ...emptyReferences(),
        rfn: protectedReference,
      };
  }
}

function trustedSeedRecord(value: unknown): Record<string, unknown> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new PaymentAttemptReferenceSeedError(
      "TRUSTED_REFUND_REFERENCE_SEED_INVALID",
    );
  }
  const prototype = Object.getPrototypeOf(value);
  if (prototype !== Object.prototype && prototype !== null) {
    throw new PaymentAttemptReferenceSeedError(
      "TRUSTED_REFUND_REFERENCE_SEED_INVALID",
    );
  }
  return value as Record<string, unknown>;
}

function assertExactTrustedSeedKeys(
  value: Record<string, unknown>,
  expected: readonly string[],
): void {
  const actual = Reflect.ownKeys(value);
  if (
    actual.some((key) => typeof key !== "string") ||
    actual.length !== expected.length ||
    expected.some((key) => !Object.prototype.hasOwnProperty.call(value, key))
  ) {
    throw new PaymentAttemptReferenceSeedError(
      "TRUSTED_REFUND_REFERENCE_SEED_INVALID",
    );
  }
}

function trustedSeedText(value: unknown): string {
  if (
    typeof value !== "string" ||
    !value ||
    value.trim() !== value
  ) {
    throw new PaymentAttemptReferenceSeedError(
      "TRUSTED_REFUND_REFERENCE_SEED_INVALID",
    );
  }
  return value;
}

function assertStartInput(input: StartPaymentAttemptInput): void {
  if (!input.actionId.trim()) throw new TypeError("actionId is required.");
  if (!input.orderGuid.trim()) throw new TypeError("orderGuid is required.");
  normalizedActor(input.actor);
  if (
    input.operation !== "refund" &&
    input.refundCapacityId !== undefined
  ) {
    throw new TypeError(
      "refundCapacityId is only valid for refund operations.",
    );
  }
  if (input.refundCapacityId !== undefined) {
    const capacityId = input.refundCapacityId;
    if (
      typeof capacityId !== "string" ||
      !capacityId ||
      capacityId.trim() !== capacityId
    ) {
      throw new TypeError(
        "refundCapacityId must be a non-empty opaque identifier.",
      );
    }
  }
  if (paymentProviderAmountCents(input.operation, input.amount) !== null) {
    return;
  }
  if (input.operation === "refund") {
    throw new TypeError(
      "Refund amount must be negative AUD integer cents with a safe positive magnitude.",
    );
  }
  throw new TypeError("Payment amount must be positive AUD integer cents.");
}

function startSignature(input: StartPaymentAttemptInput): string {
  const signature: (string | null)[] = [
    input.actionId,
    input.orderGuid,
    input.provider,
    input.operation,
    input.amount.currency,
    String(input.amount.cents),
    actorSignature(input.actor),
  ];
  if (input.operation === "refund") {
    signature.push(input.refundCapacityId ?? null);
  }
  return signature.join("|");
}

function actionRequestSignature(input: StartPaymentAttemptInput): string {
  const signature: (string | number | null)[] = [
    input.provider,
    input.operation,
    input.amount.currency,
    input.amount.cents,
  ];
  if (input.operation === "refund") {
    signature.push(input.refundCapacityId ?? null);
  }
  return JSON.stringify(signature);
}

function assertBindingMatchesRequest(
  persisted: PaymentActionBinding,
  proposed: PaymentActionBinding,
): void {
  if (
    persisted.orderGuid !== proposed.orderGuid ||
    persisted.actionId !== proposed.actionId ||
    persisted.requestSignature !== proposed.requestSignature
  ) {
    throw new PaymentActionBindingConflictError(persisted);
  }
  if (
    !persisted.attemptId.trim() ||
    !persisted.idempotencyKey.trim() ||
    !Number.isFinite(Date.parse(persisted.createdAtIso))
  ) {
    throw new PaymentActionBindingConflictError(
      persisted,
      "Persisted payment action binding has invalid immutable identity.",
    );
  }
  try {
    normalizedActor(persisted.actor);
  } catch {
    throw new PaymentActionBindingConflictError(
      persisted,
      "Persisted payment action binding has invalid immutable actor.",
    );
  }
}

function attemptFromBinding(
  binding: PaymentActionBinding,
  input: StartPaymentAttemptInput,
): PaymentAttempt {
  return {
    attemptId: binding.attemptId,
    idempotencyKey: binding.idempotencyKey,
    orderGuid: binding.orderGuid,
    provider: input.provider,
    operation: input.operation,
    amount: { ...input.amount },
    state: "Created",
    references: emptyReferences(),
    createdAtIso: binding.createdAtIso,
    updatedAtIso: binding.createdAtIso,
    lastErrorCode: null,
    receiptText: null,
    responseCode: null,
  };
}

function assertBoundAttemptIdentity(
  attempt: PaymentAttempt,
  binding: PaymentActionBinding,
  input: StartPaymentAttemptInput,
): void {
  const matches =
    attempt.attemptId === binding.attemptId &&
    attempt.idempotencyKey === binding.idempotencyKey &&
    attempt.orderGuid === binding.orderGuid &&
    attempt.provider === input.provider &&
    attempt.operation === input.operation &&
    attempt.amount.currency === input.amount.currency &&
    attempt.amount.cents === input.amount.cents &&
    attempt.createdAtIso === binding.createdAtIso;
  if (matches) return;
  throw new PaymentAttemptDurabilityError(
    binding.attemptId,
    binding.orderGuid,
    attempt.state,
    attempt.state,
    new Error("bound attempt immutable identity mismatch"),
  );
}

function sameImmutableAttemptIdentity(
  left: PaymentAttempt,
  right: PaymentAttempt,
): boolean {
  return (
    left.attemptId === right.attemptId &&
    left.idempotencyKey === right.idempotencyKey &&
    left.orderGuid === right.orderGuid &&
    left.provider === right.provider &&
    left.operation === right.operation &&
    left.amount.currency === right.amount.currency &&
    left.amount.cents === right.amount.cents &&
    left.createdAtIso === right.createdAtIso
  );
}

function requiredGeneratedValue(value: string, label: string): string {
  if (!value.trim()) throw new Error(`Generated ${label} is empty.`);
  return value;
}

function actorSignature(actor: AuditActorSnapshot): string {
  return JSON.stringify(auditActorPayload(actor));
}

function normalizedActor(actor: AuditActorSnapshot): AuditActorSnapshot {
  const payload = auditActorPayload(actor);
  return Object.freeze({
    cashierId: payload.requestingCashierId,
    cashierName: payload.requestingCashierName,
    userGuid: payload.requestingUserGuid,
  });
}

function requiredIsoTimestamp(value: string, label: string): string {
  if (!Number.isFinite(Date.parse(value))) {
    throw new TypeError(`Generated ${label} timestamp is invalid.`);
  }
  return value;
}

function providerErrorCode(error: unknown): string {
  if (error && typeof error === "object" && "code" in error) {
    const code = normalizedProviderCode((error as { code?: unknown }).code);
    if (code) return code;
  }
  return "PAYMENT_PROVIDER_EXCEPTION";
}

function normalizedProviderCode(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const normalized = value.trim().toUpperCase();
  return /^[A-Z0-9][A-Z0-9_.:-]{0,63}$/.test(normalized) ? normalized : null;
}

function isNeutralControlledRecoveryUnknown(
  providerResult: PaymentProviderResult,
  control: PaymentRecoveryControl | undefined,
): boolean {
  if (!control || providerResult.state !== "Unknown") return false;

  const code = normalizedProviderCode(providerResult.responseCode);
  // 中文注释：只有 Square 受控恢复的已知中断/截止短码才中性收口；其他 Unknown
  // 仍须按原逻辑持久化，避免把网络歧义或证据冲突误认为页面卸载。
  return (
    code === "SQUARE_RECOVERY_DEADLINE_EXCEEDED" ||
    (control.signal.aborted &&
      (code === "REQUEST_ABORTED" || code === "SQUARE_RECOVERY_ABORTED"))
  );
}

function errorMessage(error: unknown): string | null {
  return error instanceof Error && error.message.trim() ? error.message.trim() : null;
}
