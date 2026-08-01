import {
  PaymentAttemptStateError,
  type PaymentAttemptExecutionResult,
} from "./payment-attempt-service";

import type {
  AuditActorSnapshot,
  ApprovedPaymentOrderCommit,
  ApprovedPaymentOrderCommitPort,
  ApprovedPaymentOrderCommitResult,
} from "@/core/contracts";

export type ApprovedPaymentOrderCompletionPlan = Pick<
  ApprovedPaymentOrderCommit,
  "tenderGuid" | "completionAuditEvents" | "outbox" | "fulfilment"
>;

export interface ApprovedPaymentOrderCompletionPlannerPort {
  plan(
    execution: PaymentAttemptExecutionResult,
    actor: AuditActorSnapshot,
  ): Promise<ApprovedPaymentOrderCompletionPlan>;
}

export type ApprovedPaymentOrderCompletionServiceOptions = Readonly<{
  planner: ApprovedPaymentOrderCompletionPlannerPort;
  committer: ApprovedPaymentOrderCommitPort;
}>;

export class ApprovedPaymentOrderCompletionRecoveryRequiredError extends Error {
  public readonly recoveryRequired = true;
  public readonly cause: unknown;

  public constructor(
    public readonly attemptId: string,
    public readonly orderGuid: string,
    cause?: unknown,
  ) {
    super(
      `Approved payment attempt ${attemptId} could not complete order ${orderGuid}; recovery is required.`,
    );
    this.name = "ApprovedPaymentOrderCompletionRecoveryRequiredError";
    this.cause = cause;
  }
}

/**
 * 将已耐久批准的支付绑定回原订单。
 *
 * planner 只生成 tender、审计、outbox 与履约草稿；订单身份始终取自 attempt，
 * 因此重启恢复也不能创建或换绑新的 OrderGuid。
 */
export class ApprovedPaymentOrderCompletionService {
  public constructor(
    private readonly options: ApprovedPaymentOrderCompletionServiceOptions,
  ) {}

  public async complete(
    execution: PaymentAttemptExecutionResult,
    actor: AuditActorSnapshot,
  ): Promise<ApprovedPaymentOrderCommitResult> {
    const { attempt } = execution;
    if (attempt.state !== "Approved") {
      throw new PaymentAttemptStateError(
        `Payment attempt in ${attempt.state} state cannot complete an approved order.`,
      );
    }
    if (attempt.operation !== "purchase") {
      throw new PaymentAttemptStateError(
        "Only an approved purchase attempt can complete an order.",
      );
    }

    const plan = await this.options.planner.plan(execution, actor);
    const commit: ApprovedPaymentOrderCommit = {
      attemptId: attempt.attemptId,
      orderGuid: attempt.orderGuid,
      tenderGuid: plan.tenderGuid,
      completionAuditEvents: plan.completionAuditEvents,
      outbox: plan.outbox,
      fulfilment: plan.fulfilment,
    };

    try {
      return await this.options.committer.completeApprovedPaymentOrder(commit);
    } catch (error) {
      throw new ApprovedPaymentOrderCompletionRecoveryRequiredError(
        attempt.attemptId,
        attempt.orderGuid,
        error,
      );
    }
  }
}
