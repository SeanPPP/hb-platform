import type { CompleteCashOrderCommand } from "../../../core/contracts/order";
import type { DatabasePort, DatabaseTransactionPort } from "../../../core/contracts/repositories";

export type CashFulfilmentDraft = Readonly<{
  print: Readonly<{
    jobId: string;
    orderGuid: string;
    printerId: string;
    receiptBytes: Uint8Array;
    isReprint: false;
  }> | null;
  drawer: Readonly<{
    eventId: string;
    orderGuid: string;
    printJobId: string | null;
    reason: string;
  }> | null;
}>;

export interface AtomicCashOrderCommitPort {
  completeCashOrderWithFulfilment(
    command: CompleteCashOrderCommand,
    fulfilment: CashFulfilmentDraft,
  ): Promise<void>;
}

export interface CashFulfilmentPlannerPort {
  createDraft(command: CompleteCashOrderCommand): Promise<CashFulfilmentDraft>;
}

/**
 * CashCheckoutService 的窄适配器。
 *
 * 领域服务仍只认识冻结的 DatabasePort；本适配器先捕获它产生的唯一现金命令，
 * 再生成预渲染小票并交给 SQLCipher 原子 committer。runInTransaction 只有在真实
 * 订单 + 履约提交完成后才 resolve，因此 UI 不会提前清空购物车。
 */
export class AtomicCashCheckoutDatabaseAdapter implements DatabasePort {
  public constructor(
    private readonly committer: AtomicCashOrderCommitPort,
    private readonly planner: CashFulfilmentPlannerPort,
  ) {}

  public async runInTransaction<T>(
    operation: (transaction: DatabaseTransactionPort) => Promise<T>,
  ): Promise<T> {
    const capture: { command: CompleteCashOrderCommand | null } = {
      command: null,
    };
    const result = await operation({
      completeCashOrder: async (command) => {
        if (capture.command !== null) {
          throw new Error("Cash checkout attempted more than one durable completion.");
        }
        capture.command = command;
      },
    });
    if (capture.command === null) {
      throw new Error("Cash checkout did not produce a durable completion command.");
    }

    const fulfilment = await this.planner.createDraft(capture.command);
    await this.committer.completeCashOrderWithFulfilment(
      capture.command,
      fulfilment,
    );
    return result;
  }
}
