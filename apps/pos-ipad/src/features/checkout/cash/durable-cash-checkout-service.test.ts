import assert from "node:assert/strict";
import test from "node:test";

import {
  DurableCashCheckoutService,
  type DurableCashFulfilmentPlan,
  type DurableCashFulfilmentPlannerPort,
} from "./durable-cash-checkout-service";

import type {
  CartSnapshot,
  DurableCashOrderCommit,
  DurableCashOrderCommitPort,
  DurableCashOrderCommitResult,
  CashFulfilmentDraft,
  TerminalCheckoutContext,
} from "@/core/contracts";

class DurableCommitter implements DurableCashOrderCommitPort {
  public readonly commands: DurableCashOrderCommit[] = [];
  public calls = 0;
  public fail = false;
  public hold: Promise<void> | null = null;
  private readonly completed = new Map<string, { signature: string; result: DurableCashOrderCommitResult }>();

  public async completeDurableCashOrder(input: DurableCashOrderCommit): Promise<DurableCashOrderCommitResult> {
    this.calls += 1;
    const previous = this.completed.get(input.intent.checkoutIntentId);
    if (previous) {
      if (previous.signature !== input.intent.requestSignature) throw new Error("checkout intent signature mismatch");
      return { ...previous.result, replayed: true };
    }
    this.commands.push(input);
    await this.hold;
    if (this.fail) throw new Error("disk full");
    const result: DurableCashOrderCommitResult = {
      replayed: false,
      orderGuid: input.command.order.orderGuid,
      cashDueCents: input.intent.cashDueCents,
      changeCents: input.intent.changeCents,
    };
    this.completed.set(input.intent.checkoutIntentId, {
      signature: input.intent.requestSignature,
      result,
    });
    return result;
  }
}

class Planner implements DurableCashFulfilmentPlannerPort {
  public calls = 0;

  public async createDraft(command: DurableCashOrderCommit["command"]): Promise<DurableCashFulfilmentPlan> {
    this.calls += 1;
    const print = {
      jobId: `print-${command.order.orderGuid}`,
      orderGuid: command.order.orderGuid,
      printerId: "xprinter-1",
      receiptBytes: Uint8Array.of(27, 64),
      isReprint: false as const,
    };
    const draft: CashFulfilmentDraft = {
      print,
      drawer: command.requiresDrawer
        ? { eventId: `drawer-${command.order.orderGuid}`, orderGuid: command.order.orderGuid, printerId: print.printerId, printJobId: print.jobId, reason: "cash-sale" }
        : null,
    };
    return {
      draft,
      drawerDisposition: command.requiresDrawer ? "queued" : "not-required",
    };
  }
}

test("同一实例重复确认复用同一持久化订单，且只生成一次履约草稿", async () => {
  const committer = new DurableCommitter();
  const planner = new Planner();
  const service = createService(committer, planner, "same");

  const first = await service.complete(input("intent-same", cart(782), 1_000));
  const repeated = await service.complete(input("intent-same", cart(782), 1_000));

  assert.equal(first.orderGuid, repeated.orderGuid);
  assert.equal(first.cashDueCents, 780);
  assert.equal(first.changeCents, 220);
  assert.equal(committer.commands.length, 1);
  assert.equal(planner.calls, 1);
  assert.deepEqual(committer.commands[0]?.command.auditEvents[0]?.payload, {
    checkoutIntentId: "intent-same",
    localSequence: 1,
    cashDueCents: 780,
    changeCents: 220,
    requestingCashierId: "C1",
    requestingCashierName: "Alice",
    requestingUserGuid: "user-guid-c1",
  });
});

test("新服务实例由持久化 Port 回放原订单和金额，且绝不再次请求钱箱", async () => {
  const committer = new DurableCommitter();
  const first = createService(committer, new Planner(), "restart-a");
  const second = createService(committer, new Planner(), "restart-b");
  const request = input("intent-restart", cart(783), 1_000);

  const saved = await first.complete(request);
  const replayed = await second.complete(request);

  assert.equal(replayed.orderGuid, saved.orderGuid);
  assert.equal(replayed.cashDueCents, saved.cashDueCents);
  assert.equal(replayed.changeCents, saved.changeCents);
  assert.equal(replayed.postCommit.requestDrawer, false);
  assert.equal(replayed.postCommit.drawerDisposition, "replayed");
  assert.equal(committer.calls, 2);
  assert.equal(committer.commands.length, 1);
});

test("同一 intent 并发确认只提交一笔订单", async () => {
  const committer = new DurableCommitter();
  let release!: () => void;
  committer.hold = new Promise<void>((resolve) => { release = resolve; });
  const planner = new Planner();
  const service = createService(committer, planner, "concurrent");

  const first = service.complete(input("intent-concurrent", cart(500), 500));
  const second = service.complete(input("intent-concurrent", cart(500), 500));
  await new Promise((resolve) => setImmediate(resolve));
  assert.equal(committer.commands.length, 1);
  assert.equal(planner.calls, 1);
  release();
  const [left, right] = await Promise.all([first, second]);
  assert.equal(left.orderGuid, right.orderGuid);
});

test("同一 intent 换购物车或现金金额立即拒绝，绝不产生第二笔提交", async () => {
  const committer = new DurableCommitter();
  const service = createService(committer, new Planner(), "signature");
  await service.complete(input("intent-signature", cart(500), 500));

  await assert.rejects(() => service.complete(input("intent-signature", cart(600), 600)), /signature/i);
  await assert.rejects(() => service.complete(input("intent-signature", cart(500), 505)), /signature/i);
  assert.equal(committer.commands.length, 1);
});

test("普通购物车显式提交 none context，cash-v2 签名固定包含 terminal context", async () => {
  const committer = new DurableCommitter();
  const request = input("intent-terminal-none", cart(500), 500);
  let resolvedCart: CartSnapshot | null = null;
  const service = createService(
    committer,
    new Planner(),
    "terminal-none",
    async () => true,
    async (snapshot) => {
      resolvedCart = snapshot;
      return { kind: "none" };
    },
  );

  await service.complete(request);

  assert.equal(resolvedCart, request.cart);
  const committed = committer.commands[0];
  assert.deepEqual(committed?.terminalContext, { kind: "none" });
  assert.equal(committed?.recalledHoldCompletion, null);
  assert.match(committed?.intent.requestSignature ?? "", /^cash-v2:/);
  assert.match(
    committed?.intent.requestSignature ?? "",
    /"terminalContext":\{"kind":"none"\}/,
  );
});

test("取单购物车把 binding 纳入签名，并以最终订单和成交时间生成脱敏 ORDER_RECALL", async () => {
  const committer = new DurableCommitter();
  const terminalContext: TerminalCheckoutContext = {
    kind: "recalled",
    scope: { storeCode: "S1", deviceCode: "IPAD1" },
    holdId: "hold-42",
    recallAttemptId: "recall-attempt-7",
  };
  const service = createService(
    committer,
    new Planner(),
    "terminal-recalled",
    async () => true,
    async () => terminalContext,
  );

  await service.complete(input("intent-terminal-recalled", cart(782), 1_000));

  const committed = committer.commands[0];
  assert.deepEqual(committed?.terminalContext, terminalContext);
  assert.match(committed?.intent.requestSignature ?? "", /^cash-v2:/);
  assert.match(committed?.intent.requestSignature ?? "", /"holdId":"hold-42"/);
  assert.match(
    committed?.intent.requestSignature ?? "",
    /"recallAttemptId":"recall-attempt-7"/,
  );
  assert.deepEqual(committed?.recalledHoldCompletion, {
    binding: terminalContext,
    recalledAtIso: "2026-07-28T05:00:00.000Z",
    recallAudit: {
      eventId: "terminal-recalled-5",
      eventType: "ORDER_RECALL",
      occurredAtIso: "2026-07-28T05:00:00.000Z",
      orderGuid: "terminal-recalled-1",
      correlationId: "hold-42",
      payload: {
        source: "ipad-pos",
        action: "recall",
        result: "completed",
        storeCode: "S1",
        deviceCode: "IPAD1",
        cashierId: "C1",
        requestingCashierId: "C1",
        requestingCashierName: "Alice",
        requestingUserGuid: "user-guid-c1",
        itemCount: 1,
        actualAmountCents: 782,
        localSequence: 1,
      },
    },
  });
  const auditJson = JSON.stringify(
    committed?.recalledHoldCompletion?.recallAudit,
  );
  assert.doesNotMatch(
    auditJson,
    /token|barcode|lookupCode|productCode|displayName/i,
  );
});

test("terminal context resolver 异常发生在单飞签名之前，绝不规划或落库", async () => {
  const committer = new DurableCommitter();
  const planner = new Planner();
  const service = createService(
    committer,
    planner,
    "terminal-error",
    async () => true,
    async () => {
      throw new Error("active cart changed");
    },
  );

  await assert.rejects(
    () => service.complete(input("intent-terminal-error", cart(500), 500)),
    /active cart changed/i,
  );
  assert.equal(planner.calls, 0);
  assert.equal(committer.calls, 0);
  assert.equal(committer.commands.length, 0);
});

test("取单 binding 与本次门店设备不一致时在规划和落库前拒绝", async () => {
  const committer = new DurableCommitter();
  const planner = new Planner();
  const service = createService(
    committer,
    planner,
    "terminal-scope",
    async () => true,
    async () => ({
      kind: "recalled",
      scope: { storeCode: "S1", deviceCode: "OTHER-IPAD" },
      holdId: "hold-other-device",
      recallAttemptId: "attempt-other-device",
    }),
  );

  await assert.rejects(
    () => service.complete(input("intent-terminal-scope", cart(500), 500)),
    /scope mismatch/i,
  );
  assert.equal(planner.calls, 0);
  assert.equal(committer.calls, 0);
});

test("同一 intent 和 context 保持幂等，切换 terminal context 后签名冲突", async () => {
  const committer = new DurableCommitter();
  let terminalContext: TerminalCheckoutContext = { kind: "none" };
  const service = createService(
    committer,
    new Planner(),
    "terminal-signature",
    async () => true,
    async () => terminalContext,
  );
  const request = input("intent-terminal-signature", cart(500), 500);

  const first = await service.complete(request);
  const repeated = await service.complete(request);
  assert.equal(repeated.orderGuid, first.orderGuid);
  assert.equal(committer.commands.length, 1);

  terminalContext = {
    kind: "recalled",
    scope: { storeCode: "S1", deviceCode: "IPAD1" },
    holdId: "hold-new",
    recallAttemptId: "attempt-new",
  };
  await assert.rejects(() => service.complete(request), /signature mismatch/i);
  assert.equal(committer.commands.length, 1);
});

test("持久化提交失败时不返回 completed，修复后同 intent 可重试", async () => {
  const committer = new DurableCommitter();
  committer.fail = true;
  const service = createService(committer, new Planner(), "failure");
  const request = input("intent-failure", cart(500), 500);

  await assert.rejects(() => service.complete(request), /disk full/i);
  committer.fail = false;
  const completed = await service.complete(request);
  assert.equal(completed.completed, true);
  assert.equal(completed.canClearCart, true);
  assert.equal(committer.commands.length, 2);
});

test("零金额订单没有现金 tender 且不请求钱箱", async () => {
  const committer = new DurableCommitter();
  const service = createService(committer, new Planner(), "zero");

  const completed = await service.complete(input("intent-zero", cart(0), null));

  assert.equal(committer.commands[0]?.command.order.tenders.length, 0);
  assert.equal(completed.postCommit.requestDrawer, false);
  assert.equal(completed.postCommit.drawerDisposition, "not-required");
});

test("离线退货容量不足时不规划小票也不提交", async () => {
  const committer = new DurableCommitter();
  const planner = new Planner();
  const service = createService(committer, planner, "return", async () => false);

  await assert.rejects(() => service.complete(input("intent-return", cart(-500, "return"), -500)), /capacity/i);
  assert.equal(planner.calls, 0);
  assert.equal(committer.commands.length, 0);
});

test("本地序号必须为正整数，零值不得进入订单账本", async () => {
  const committer = new DurableCommitter();
  const planner = new Planner();
  const service = new DurableCashCheckoutService(committer, planner, {
    createId: () => "id-1",
    nowIso: () => "2026-07-28T05:00:00.000Z",
    returnCapacity: async () => true,
    nextLocalSequence: async () => 0,
  });

  await assert.rejects(
    () => service.complete(input("intent-zero-sequence", cart(500), 500)),
    /local sequence/i,
  );
  assert.equal(planner.calls, 0);
  assert.equal(committer.commands.length, 0);
});

test("缺少外设配置时把最终命令固化为 never/无钱箱，现金账本仍可提交", async () => {
  const committer = new DurableCommitter();
  class DisabledPeripheralPlanner implements DurableCashFulfilmentPlannerPort {
    public calls = 0;

    public async createDraft(): Promise<DurableCashFulfilmentPlan> {
      this.calls += 1;
      return {
        draft: { print: null, drawer: null },
        drawerDisposition: "unavailable",
      };
    }
  }
  const planner = new DisabledPeripheralPlanner();
  const service = createService(
    committer,
    planner,
    "no-peripheral",
  );

  const result = await service.complete(
    input("intent-no-peripheral", cart(500), 500),
  );

  assert.equal(result.completed, true);
  assert.deepEqual(result.postCommit, {
    requestDrawer: false,
    drawerDisposition: "unavailable",
    printPolicy: "never",
  });
  assert.equal(committer.commands[0]?.command.printPolicy, "never");
  assert.equal(committer.commands[0]?.command.requiresDrawer, false);
  assert.deepEqual(committer.commands[0]?.fulfilment, {
    print: null,
    drawer: null,
  });
  assert.equal(planner.calls, 1);
});

function createService(
  committer: DurableCommitter,
  planner: DurableCashFulfilmentPlannerPort,
  scope: string,
  returnCapacity: (snapshot: CartSnapshot) => Promise<boolean> = async () => true,
  resolveTerminalContext: (
    snapshot: CartSnapshot,
  ) => TerminalCheckoutContext | Promise<TerminalCheckoutContext> = async () => ({
    kind: "none",
  }),
): DurableCashCheckoutService {
  let id = 0;
  return new DurableCashCheckoutService(committer, planner, {
    createId: () => `${scope}-${++id}`,
    nowIso: () => "2026-07-28T05:00:00.000Z",
    returnCapacity,
    nextLocalSequence: async () => 1,
  }, {
    resolve: resolveTerminalContext,
  });
}

function input(checkoutIntentId: string, snapshot: CartSnapshot, cashTenderedCents: number | null) {
  return {
    checkoutIntentId,
    cart: snapshot,
    cashTenderedCents,
    storeCode: "S1",
    deviceCode: "IPAD1",
    cashierId: "C1",
    cashierName: "Alice",
    userGuid: "user-guid-c1",
  };
}

function cart(amount: number, kind: "sale" | "return" = "sale"): CartSnapshot {
  return {
    revision: 1,
    mode: kind === "return" ? "return" : "sale",
    subtotal: { currency: "AUD", cents: amount }, discount: { currency: "AUD", cents: 0 }, actualAmount: { currency: "AUD", cents: amount },
    lines: [{ lineId: "L1", productCode: "P1", itemNumber: null, lookupCode: "1", displayName: "Tea", quantity: "1", unitPrice: { currency: "AUD", cents: Math.abs(amount) }, discount: { currency: "AUD", cents: 0 }, actualAmount: { currency: "AUD", cents: amount }, priceSource: "catalog", kind, returnSourceKey: kind === "return" ? "R1" : null, originalOrderGuid: kind === "return" ? "O1" : null, originalOrderDetailGuid: null }],
  };
}
