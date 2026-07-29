import type { CartSnapshot } from "./cart";
import type {
  ApprovedPaymentOrderCommit,
  ApprovedPaymentOrderCommitResult,
  AuditEventDraft,
  CompleteCashOrderCommand,
  DurableCashOrderCommit,
  DurableCashOrderCommitResult,
  LocalOrder,
  OutboxMessageDraft,
} from "./order";
import type { CardSyncEvidenceV1, PaymentAttempt } from "./payment";
import type { DrawerEventState, PrintJobState } from "./state-machines";

export type OutboxLease = Readonly<{
  messageId: string;
  leaseId: string;
  aggregateId: string;
  kind: OutboxMessageDraft["kind"];
  payloadJson: string;
  attemptCount: number;
}>;

export interface DatabaseTransactionPort {
  completeCashOrder(command: CompleteCashOrderCommand): Promise<void>;
}

export interface DatabasePort {
  runInTransaction<T>(operation: (transaction: DatabaseTransactionPort) => Promise<T>): Promise<T>;
}

export interface DurableCashOrderCommitPort {
  /**
   * checkout intent、订单、outbox、审计、打印和钱箱必须在同一独占事务提交。
   * 同一 intent + 签名重放返回原结果；同一 intent 换内容必须拒绝。
   */
  completeDurableCashOrder(
    input: DurableCashOrderCommit,
  ): Promise<DurableCashOrderCommitResult>;
}

export interface ApprovedPaymentOrderCommitPort {
  /**
   * 读取 Approved attempt 并在同一事务中校验原 OrderGuid、金额、provider，
   * 唯一插入 tender。累计 tender 等于订单应付额时，同时完成订单、outbox、
   * 审计和履约；部分混合支付只进入 Completing。
   */
  completeApprovedPaymentOrder(
    input: ApprovedPaymentOrderCommit,
  ): Promise<ApprovedPaymentOrderCommitResult>;
}

export interface OrderRepositoryPort {
  nextLocalSequence(): Promise<number>;
  saveDraft(order: LocalOrder): Promise<void>;
  getByGuid(orderGuid: string): Promise<LocalOrder | null>;
  listLocal(limit: number, beforeSequence?: number): Promise<readonly LocalOrder[]>;
  transition(orderGuid: string, expectedState: LocalOrder["state"], nextState: LocalOrder["state"]): Promise<boolean>;
}

export interface HeldOrderRepositoryPort {
  hold(holdId: string, snapshot: CartSnapshot, localSequence: number): Promise<void>;
  resume(holdId: string): Promise<CartSnapshot | null>;
  remove(holdId: string): Promise<void>;
}

export interface PaymentAttemptRepositoryPort {
  /**
   * 在同一独占事务内检查订单阻塞 attempt 并插入 Created。
   * 返回 null 表示插入成功；否则返回必须先恢复/完成的已有 attempt。
   */
  insertIfUnblocked(attempt: PaymentAttempt): Promise<PaymentAttempt | null>;
  /** 仅当持久状态和更新时间仍等于 expected 时更新，禁止末写覆盖支付结果。 */
  compareAndUpdate(
    expected: PaymentAttempt,
    next: PaymentAttempt,
    protectedSyncEvidence?: CardSyncEvidenceV1,
  ): Promise<boolean>;
  get(attemptId: string): Promise<PaymentAttempt | null>;
  /**
   * 包含 Created/Submitted/Pending/Unknown，以及 Approved 但尚未写入 order_tenders 的 attempt。
   */
  findBlocking(orderGuid: string): Promise<PaymentAttempt | null>;
}

export interface OutboxRepositoryPort {
  enqueue(message: OutboxMessageDraft): Promise<void>;
  leaseReady(limit: number, leaseSeconds: number): Promise<readonly OutboxLease[]>;
  markSucceeded(lease: OutboxLease): Promise<void>;
  releaseRetry(lease: OutboxLease, nextAttemptAtIso: string, errorCode: string): Promise<void>;
  markBlocked403(lease: OutboxLease, errorCode: string): Promise<void>;
  markRejected(lease: OutboxLease, errorCode: string): Promise<void>;
}

export interface AuditRepositoryPort {
  append(events: readonly AuditEventDraft[]): Promise<void>;
  listPending(limit: number): Promise<readonly AuditEventDraft[]>;
  markUploaded(eventIds: readonly string[]): Promise<void>;
}

export interface PrintJobRepositoryPort {
  transition(jobId: string, expected: PrintJobState, next: PrintJobState): Promise<boolean>;
}

export interface DrawerEventRepositoryPort {
  transition(eventId: string, expected: DrawerEventState, next: DrawerEventState): Promise<boolean>;
}
