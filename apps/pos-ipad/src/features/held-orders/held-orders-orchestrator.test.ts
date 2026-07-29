import assert from "node:assert/strict";
import test from "node:test";

import type {
  ActivePricingCartLeasePort,
  ActivePricingCartPort,
  ActivePricingCartSnapshot,
  HeldOrderAuthorizationPort,
} from "./held-orders-domain";
import {
  HOLD_ORDER_PERMISSION,
  RECALL_LIST_PERMISSION,
  RECALL_RESTORE_PERMISSION,
} from "./held-orders-domain";
import { HeldOrdersOrchestrator } from "./held-orders-orchestrator";
import { HeldOrdersPresenter } from "./held-orders-presenter";

import {
  createAud,
  type HeldOrderRecordRepositoryPort,
  type HeldOrderScope,
  type HeldOrderSummary,
  type HoldCartCommand,
  type PricingCartStateSnapshot,
  type RecallActiveBinding,
  type RecallClaim,
  type TerminalCartFence,
} from "@/core/contracts";

const identity = {
  storeCode: "BNE",
  deviceCode: "IPAD-1",
  cashierId: "CASHIER-1",
  cashierName: "Cashier",
};

function saleSnapshot(
  lineCount = 1,
  recallBinding: RecallActiveBinding | null = null,
  sessionRevision = 4,
  terminalRecoveryRequired = false,
): ActivePricingCartSnapshot {
  const lines = Array.from({ length: lineCount }, (_, index) => ({
    lineId: `line-${index + 1}`,
    productCode: `P-${index + 1}`,
    itemNumber: null,
    lookupCode: `CODE-${index + 1}`,
    displayName: `Product ${index + 1}`,
    quantity: 1,
    unitPriceCents: 1_100,
    basePriceSource: "catalog" as const,
    kind: "sale" as const,
    returnSourceKey: null,
    originalOrderGuid: null,
    originalOrderDetailGuid: null,
    discountState: { kind: "none" as const },
  }));
  const pricingState: PricingCartStateSnapshot = {
    revision: sessionRevision,
    mode: "sale",
    asOfIso: "2026-07-28T00:00:00.000Z",
    promotions: [],
    lines,
  };
  return {
    sessionRevision,
    recallBinding,
    terminalRecoveryRequired,
    pricingState,
    cart: toCart(pricingState),
  };
}

function returnSnapshot(): ActivePricingCartSnapshot {
  const snapshot = saleSnapshot();
  return {
    ...snapshot,
    pricingState: { ...snapshot.pricingState, mode: "return" },
    cart: { ...snapshot.cart, mode: "return" },
  };
}

function emptySnapshot(
  recallBinding: RecallActiveBinding | null = null,
  terminalRecoveryRequired = false,
): ActivePricingCartSnapshot {
  return saleSnapshot(0, recallBinding, 4, terminalRecoveryRequired);
}

class Cart implements ActivePricingCartPort {
  public readonly blockCalls: RecallActiveBinding[] = [];
  public readonly replaceCalls: Readonly<{
    pricingState: PricingCartStateSnapshot;
    recallBinding: RecallActiveBinding | null;
  }>[] = [];
  public readonly setBindingCalls: (RecallActiveBinding | null)[] = [];
  public readonly rejectReplaceAttempts = new Set<number>();
  public rejectBinding = false;
  public busy = false;

  public constructor(
    public value: ActivePricingCartSnapshot,
    private pendingRecallBinding: RecallActiveBinding | null = null,
  ) {}

  public forceSet(value: ActivePricingCartSnapshot): void {
    this.value = value;
    this.pendingRecallBinding = null;
  }

  public async runExclusive<T>(
    operation: (lease: ActivePricingCartLeasePort) => T | Promise<T>,
  ): Promise<T> {
    if (this.busy) {
      throw Object.assign(new Error("cart busy"), {
        code: "ACTIVE_PRICING_CART_BUSY",
      });
    }
    this.busy = true;
    const lease: ActivePricingCartLeasePort = {
      read: () => this.value,
      blockForRecallRecovery: async (recallBinding) => {
        this.blockCalls.push(recallBinding);
        if (
          (this.pendingRecallBinding &&
            !sameBinding(this.pendingRecallBinding, recallBinding)) ||
          (this.value.recallBinding &&
            !sameBinding(this.value.recallBinding, recallBinding)) ||
          (!this.pendingRecallBinding &&
            !this.value.recallBinding &&
            this.value.cart.lines.length > 0)
        ) {
          throw new Error("terminal recovery binding mismatch");
        }
        if (!this.value.recallBinding) {
          this.pendingRecallBinding = recallBinding;
          this.value = {
            ...this.value,
            sessionRevision: this.value.sessionRevision + 1,
            terminalRecoveryRequired: true,
          };
        }
      },
      replace: async (pricingState, recallBinding) => {
        const attempt = this.replaceCalls.length + 1;
        (this.replaceCalls as {
          pricingState: PricingCartStateSnapshot;
          recallBinding: RecallActiveBinding | null;
        }[]).push({ pricingState, recallBinding });
        if (this.rejectReplaceAttempts.has(attempt)) {
          throw new Error("cart restore unavailable");
        }
        if (
          this.pendingRecallBinding
            ? !recallBinding ||
              !sameBinding(this.pendingRecallBinding, recallBinding)
            : !sameNullableBinding(this.value.recallBinding, recallBinding)
        ) {
          throw new Error("terminal recovery binding mismatch");
        }
        this.pendingRecallBinding = null;
        this.value = {
          sessionRevision: this.value.sessionRevision + 1,
          pricingState,
          cart: toCart(pricingState),
          recallBinding,
          terminalRecoveryRequired: false,
        };
      },
      setRecallBinding: async (recallBinding) => {
        this.setBindingCalls.push(recallBinding);
        if (this.rejectBinding) throw new Error("binding unavailable");
        if (
          this.pendingRecallBinding ||
          (recallBinding &&
            !sameNullableBinding(this.value.recallBinding, recallBinding))
        ) {
          throw new Error("terminal recovery binding mismatch");
        }
        this.value = {
          ...this.value,
          sessionRevision: this.value.sessionRevision + 1,
          recallBinding,
          terminalRecoveryRequired: false,
        };
      },
    };
    try {
      return await operation(lease);
    } finally {
      this.busy = false;
    }
  }
}

class Repository implements HeldOrderRecordRepositoryPort {
  public readonly holds: HoldCartCommand[] = [];
  public readonly claims: unknown[] = [];
  public readonly confirms: unknown[] = [];
  public readonly releases: unknown[] = [];
  public pending: HeldOrderSummary[] = [];
  public recoverable: RecallClaim[] = [];
  public holdFailure = false;
  public claimResult: RecallClaim | null = null;
  public loadClaimResult: RecallClaim | null = null;
  public confirmResult = true;
  public releaseResult = true;
  public listFailure = false;
  public holdGate: Promise<void> | null = null;
  public fence: TerminalCartFence | null = null;

  public async hold(command: HoldCartCommand): Promise<HeldOrderSummary> {
    this.holds.push(command);
    await this.holdGate;
    if (this.holdFailure) throw new Error("sqlite failure");
    this.fence = holdFence(command.holdId);
    return summary(command.holdId);
  }

  public async listPending(): Promise<readonly HeldOrderSummary[]> {
    if (this.listFailure) throw new Error("encrypted database unavailable");
    return this.pending;
  }

  public async claimRecall(input: {
    holdId: string;
    scope: HeldOrderScope;
    recallAttemptId: string;
  }): Promise<RecallClaim | null> {
    this.claims.push(input);
    if (this.claimResult) {
      this.fence = recallFence(
        this.claimResult.hold.holdId,
        this.claimResult.recallAttemptId,
      );
    }
    return this.claimResult;
  }

  public async getTerminalFence(): Promise<TerminalCartFence | null> {
    return this.fence;
  }

  public async loadRecallForFence(
    binding: RecallActiveBinding,
  ): Promise<RecallClaim | null> {
    const candidate =
      this.loadClaimResult ??
      this.recoverable.find((entry) => entry.hold.holdId === binding.holdId) ??
      this.claimResult;
    return candidate?.recallAttemptId === binding.recallAttemptId
      ? candidate
      : null;
  }

  public async confirmHoldCartCleared(input: {
    scope: HeldOrderScope;
    holdId: string;
  }): Promise<boolean> {
    this.confirms.push(input);
    if (
      this.confirmResult &&
      this.fence?.kind === "HoldClear" &&
      this.fence.holdId === input.holdId
    ) {
      this.fence = null;
    }
    return this.confirmResult;
  }

  public async releaseRecallAfterCartCleared(input: {
    binding: RecallActiveBinding;
    releasedAtIso: string;
  }): Promise<boolean> {
    this.releases.push(input);
    if (this.releaseResult) this.fence = null;
    return this.releaseResult;
  }

  public async listRecoverable(): Promise<readonly RecallClaim[]> {
    return this.recoverable;
  }
}

function authorization(...permissions: string[]) {
  const calls: string[] = [];
  const disposed: string[] = [];
  const port: HeldOrderAuthorizationPort = {
    async authorizeAndRun({ permissionCode }, operation) {
      calls.push(permissionCode);
      if (!permissions.includes(permissionCode)) return { authorized: false };
      try {
        return { authorized: true, value: await operation() };
      } finally {
        disposed.push(permissionCode);
      }
    },
  };
  return { calls, disposed, port };
}

function gatedAuthorization(
  gate: Promise<void>,
  ...permissions: string[]
): HeldOrderAuthorizationPort {
  return {
    async authorizeAndRun({ permissionCode }, operation) {
      if (!permissions.includes(permissionCode)) return { authorized: false };
      await gate;
      return { authorized: true, value: await operation() };
    },
  };
}

function service(
  cart: Cart,
  repository: Repository,
  permissions = [
    HOLD_ORDER_PERMISSION,
    RECALL_LIST_PERMISSION,
    RECALL_RESTORE_PERMISSION,
  ],
  authorizationPort = authorization(...permissions).port,
) {
  let id = 0;
  return new HeldOrdersOrchestrator({
    activeCart: cart,
    authorization: authorizationPort,
    createId: () => `id-${++id}`,
    identity,
    nowIso: () => "2026-07-28T01:02:03.000Z",
    repository,
  });
}

function summary(
  holdId = "hold-1",
  overrides: Partial<HeldOrderSummary> = {},
): HeldOrderSummary {
  return {
    holdId,
    localSequence: 42,
    scope: { storeCode: "BNE", deviceCode: "IPAD-1" },
    heldBy: { cashierId: "CASHIER-1", cashierName: "Cashier" },
    status: "Pending",
    itemCount: 1,
    subtotalCents: 1_100,
    discountCents: 100,
    actualAmountCents: 1_000,
    heldAtIso: "2026-07-28T01:00:00.000Z",
    recallingAtIso: null,
    ...overrides,
  };
}

function claim(
  holdId = "hold-1",
  payload = saleSnapshot().pricingState,
  recallAttemptId = "persisted-attempt-7",
): RecallClaim {
  return {
    hold: summary(holdId, {
      status: "Recalling",
      recallingAtIso: "2026-07-28T01:01:00.000Z",
    }),
    recallAttemptId,
    payload: { version: 1, pricingState: payload },
  };
}

function holdFence(holdId: string): TerminalCartFence {
  return {
    scope: { storeCode: "BNE", deviceCode: "IPAD-1" },
    kind: "HoldClear",
    holdId,
    recallAttemptId: null,
    boundOrderGuid: null,
    createdAtIso: "2026-07-28T01:02:03.000Z",
  };
}

function recallFence(
  holdId = "hold-1",
  recallAttemptId = "persisted-attempt-7",
): TerminalCartFence {
  return {
    ...holdFence(holdId),
    kind: "RecallActive",
    recallAttemptId,
  };
}

function binding(
  holdId = "hold-1",
  recallAttemptId = "persisted-attempt-7",
): RecallActiveBinding {
  return {
    kind: "recalled",
    scope: { storeCode: "BNE", deviceCode: "IPAD-1" },
    holdId,
    recallAttemptId,
  };
}

function sameBinding(
  left: RecallActiveBinding,
  right: RecallActiveBinding,
): boolean {
  return (
    left.holdId === right.holdId &&
    left.recallAttemptId === right.recallAttemptId &&
    left.scope.storeCode === right.scope.storeCode &&
    left.scope.deviceCode === right.scope.deviceCode
  );
}

function sameNullableBinding(
  left: RecallActiveBinding | null,
  right: RecallActiveBinding | null,
): boolean {
  return (
    left === right ||
    (left !== null && right !== null && sameBinding(left, right))
  );
}

function toCart(
  state: PricingCartStateSnapshot,
): ActivePricingCartSnapshot["cart"] {
  const lines = state.lines.map((line) => ({
    ...line,
    quantity: String(line.quantity),
    unitPrice: createAud(line.unitPriceCents),
    discount: createAud(0),
    actualAmount: createAud(line.unitPriceCents),
    priceSource: line.basePriceSource,
  }));
  const cents = lines.reduce(
    (total, line) => total + line.actualAmount.cents,
    0,
  );
  return {
    revision: state.revision,
    mode: state.mode,
    lines,
    subtotal: createAud(cents),
    discount: createAud(0),
    actualAmount: createAud(cents),
  };
}

test("挂单使用精确权限，Pending+审计提交后才清车并确认 HoldClear fence", async () => {
  const deniedRepository = new Repository();
  const denied = service(
    new Cart(saleSnapshot()),
    deniedRepository,
    ["Permissions.PosTerminal.Sales"],
  );
  assert.deepEqual(await denied.hold(), {
    ok: false,
    code: "authorization-denied",
  });
  assert.equal(deniedRepository.holds.length, 0);

  const cart = new Cart(saleSnapshot());
  const repository = new Repository();
  const result = await service(cart, repository).hold();
  assert.equal(result.code, "held");
  assert.equal(cart.value.cart.lines.length, 0);
  assert.equal(repository.holds[0]?.payload.pricingState.lines.length, 1);
  assert.equal(repository.confirms.length, 1);
  assert.equal(repository.fence, null);
  assert.equal(
    JSON.stringify(repository.holds[0]?.audit.payload).includes("Product"),
    false,
  );
});

test("主管授权等待结束后在 lease 内重读最新车，不会保存旧快照再清掉新商品", async () => {
  let release!: () => void;
  const gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  const cart = new Cart(saleSnapshot(1));
  const repository = new Repository();
  const subject = service(
    cart,
    repository,
    [HOLD_ORDER_PERMISSION],
    gatedAuthorization(gate, HOLD_ORDER_PERMISSION),
  );
  const pending = subject.hold();
  cart.forceSet(saleSnapshot(2, null, 5));
  release();

  assert.equal((await pending).code, "held");
  assert.equal(repository.holds[0]?.payload.pricingState.lines.length, 2);
  assert.equal(cart.value.cart.lines.length, 0);
});

test("挂单提交后清车或 fence 确认失败都保持 fail-closed 结果", async () => {
  const clearCart = new Cart(saleSnapshot());
  clearCart.rejectReplaceAttempts.add(1);
  const clearRepository = new Repository();
  const clearResult = await service(clearCart, clearRepository).hold();
  assert.equal(clearResult.code, "hold-committed-cart-not-cleared");
  assert.equal(clearCart.value.cart.lines.length, 1);
  assert.equal(clearRepository.fence?.kind, "HoldClear");

  const confirmRepository = new Repository();
  confirmRepository.confirmResult = false;
  const confirmResult = await service(
    new Cart(saleSnapshot()),
    confirmRepository,
  ).hold();
  assert.equal(confirmResult.code, "hold-fence-not-cleared");
  assert.equal(confirmRepository.fence?.kind, "HoldClear");
});

test("空车、退货车、仓储失败和并行双击均不形成第二笔挂单", async () => {
  assert.equal(
    (await service(new Cart(emptySnapshot()), new Repository()).hold()).code,
    "cart-empty",
  );
  assert.equal(
    (await service(new Cart(returnSnapshot()), new Repository()).hold()).code,
    "sale-mode-required",
  );

  const failedRepository = new Repository();
  failedRepository.holdFailure = true;
  const failedCart = new Cart(saleSnapshot());
  assert.equal(
    (await service(failedCart, failedRepository).hold()).code,
    "hold-failed",
  );
  assert.equal(failedCart.value.cart.lines.length, 1);

  let release!: () => void;
  const gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  const repository = new Repository();
  repository.holdGate = gate;
  const subject = service(new Cart(saleSnapshot()), repository);
  const first = subject.hold();
  assert.deepEqual(await subject.hold(), {
    ok: false,
    code: "operation-in-progress",
  });
  release();
  assert.equal((await first).code, "held");
  assert.equal(repository.holds.length, 1);
});

test("启动恢复围栏阻断挂单和新取单，不能把隐藏 pending 车当作普通空车", async () => {
  const expectedBinding = binding("recover-here", "attempt-restart");
  const cart = new Cart(
    emptySnapshot(null, true),
    expectedBinding,
  );
  const repository = new Repository();
  repository.fence = recallFence("recover-here", "attempt-restart");
  repository.claimResult = claim();
  const subject = service(cart, repository);

  assert.equal((await subject.hold()).code, "terminal-fence-blocked");
  assert.equal(
    (await subject.recall("another-hold")).code,
    "terminal-fence-blocked",
  );
  assert.equal(repository.holds.length, 0);
  assert.equal(repository.claims.length, 0);
  assert.equal(cart.blockCalls.length, 0);
});

test("取单需要两个精确权限；恢复后保持 Recalling binding，不提前 complete/release", async () => {
  const deniedRepository = new Repository();
  const denied = service(
    new Cart(emptySnapshot()),
    deniedRepository,
    [RECALL_LIST_PERMISSION],
  );
  assert.equal((await denied.recall("hold-1")).code, "authorization-denied");
  assert.equal(deniedRepository.claims.length, 0);

  const cart = new Cart(emptySnapshot());
  const repository = new Repository();
  repository.claimResult = claim();
  const result = await service(cart, repository).recall("hold-1");
  assert.equal(result.code, "recalled");
  assert.deepEqual(cart.value.recallBinding, binding());
  assert.equal(cart.value.cart.lines.length, 1);
  assert.equal(repository.fence?.kind, "RecallActive");
  assert.equal(repository.releases.length, 0);
});

test("取单授权等待期间车变为非空时不 claim、不覆盖当前商品", async () => {
  let release!: () => void;
  const gate = new Promise<void>((resolve) => {
    release = resolve;
  });
  const cart = new Cart(emptySnapshot());
  const repository = new Repository();
  repository.claimResult = claim();
  const subject = service(
    cart,
    repository,
    [RECALL_LIST_PERMISSION, RECALL_RESTORE_PERMISSION],
    gatedAuthorization(
      gate,
      RECALL_LIST_PERMISSION,
      RECALL_RESTORE_PERMISSION,
    ),
  );
  const pending = subject.recall("hold-1");
  cart.forceSet(saleSnapshot(1, null, 9));
  release();

  assert.equal((await pending).code, "cart-not-empty");
  assert.equal(repository.claims.length, 0);
  assert.equal(cart.value.cart.lines[0]?.productCode, "P-1");
});

test("恢复校验失败只在空车内释放精确 RecallActive；release 失败保留 binding/fence", async () => {
  const releasedCart = new Cart(emptySnapshot());
  releasedCart.rejectReplaceAttempts.add(1);
  const releasedRepository = new Repository();
  releasedRepository.claimResult = claim();
  const released = await service(
    releasedCart,
    releasedRepository,
  ).recall("hold-1");
  assert.equal(released.code, "restore-failed");
  assert.equal(releasedRepository.releases.length, 1);
  assert.equal(releasedRepository.fence, null);
  assert.equal(releasedCart.value.recallBinding, null);

  const blockedCart = new Cart(emptySnapshot());
  blockedCart.rejectReplaceAttempts.add(1);
  const blockedRepository = new Repository();
  blockedRepository.claimResult = claim();
  blockedRepository.releaseResult = false;
  const blocked = await service(
    blockedCart,
    blockedRepository,
  ).recall("hold-1");
  assert.equal(blocked.code, "release-failed");
  assert.deepEqual(blockedCart.value.recallBinding, binding());
  assert.equal(blockedRepository.fence?.kind, "RecallActive");
});

test("重启后无需先 list，可按耐久 fence 的原 attempt 精确恢复", async () => {
  const expectedBinding = binding("recover-here", "attempt-restart");
  const cart = new Cart(
    emptySnapshot(null, true),
    expectedBinding,
  );
  const repository = new Repository();
  repository.fence = recallFence("recover-here", "attempt-restart");
  repository.loadClaimResult = claim(
    "recover-here",
    saleSnapshot(2).pricingState,
    "attempt-restart",
  );

  const result = await service(cart, repository).recover("recover-here");
  assert.equal(result.code, "recovered");
  assert.equal(cart.value.cart.lines.length, 2);
  assert.deepEqual(
    cart.value.recallBinding,
    expectedBinding,
  );
  assert.equal(cart.value.terminalRecoveryRequired, false);
  assert.deepEqual(cart.blockCalls, [expectedBinding]);
  assert.equal(repository.releases.length, 0);
});

test("重启围栏允许用精确 binding 直接 release，不需要公开或恢复冻结商品", async () => {
  const expectedBinding = binding("recover-here", "attempt-restart");
  const cart = new Cart(
    emptySnapshot(null, true),
    expectedBinding,
  );
  const repository = new Repository();
  repository.fence = recallFence("recover-here", "attempt-restart");

  const result = await service(cart, repository).release("recover-here");

  assert.equal(result.code, "released");
  assert.equal(cart.value.cart.lines.length, 0);
  assert.equal(cart.value.recallBinding, null);
  assert.equal(cart.value.terminalRecoveryRequired, false);
  assert.equal(repository.releases.length, 1);
});

test("显式 release 先清车再执行 Recalling→Pending+删 fence，失败时保留内部 binding", async () => {
  const activeBinding = binding();
  const cart = new Cart(saleSnapshot(1, activeBinding));
  const repository = new Repository();
  repository.fence = recallFence();
  repository.loadClaimResult = claim();
  const released = await service(cart, repository).release("hold-1");
  assert.equal(released.code, "released");
  assert.equal(cart.value.cart.lines.length, 0);
  assert.equal(cart.value.recallBinding, null);
  assert.equal(repository.fence, null);

  const failedCart = new Cart(saleSnapshot(1, activeBinding));
  const failedRepository = new Repository();
  failedRepository.fence = recallFence();
  failedRepository.releaseResult = false;
  const failed = await service(
    failedCart,
    failedRepository,
  ).release("hold-1");
  assert.equal(failed.code, "release-failed");
  assert.equal(failedCart.value.cart.lines.length, 0);
  assert.deepEqual(failedCart.value.recallBinding, activeBinding);
  assert.equal(failedRepository.fence?.kind, "RecallActive");
});

test("列表只显示本 scope 的 Pending/Recalling，并按本地序号倒序", async () => {
  const repository = new Repository();
  repository.pending = [
    summary("pending-old", { localSequence: 2 }),
    summary("other", {
      localSequence: 99,
      scope: { storeCode: "SYD", deviceCode: "IPAD-2" },
    }),
  ];
  repository.recoverable = [
    claim("recover-new"),
    {
      ...claim("recover-other"),
      hold: summary("recover-other", {
        status: "Recalling",
        scope: { storeCode: "SYD", deviceCode: "IPAD-2" },
      }),
    },
  ];

  assert.deepEqual(
    (await service(new Cart(emptySnapshot()), repository).list()).map(
      (row) => row.holdId,
    ),
    ["recover-new", "pending-old"],
  );
});

test("presenter 对危险 fence 结果刷新列表，已授权 callback 异常显示 failed 而非 unauthorized", async () => {
  const repository = new Repository();
  const cart = new Cart(saleSnapshot());
  cart.rejectReplaceAttempts.add(1);
  const presenter = new HeldOrdersPresenter(service(cart, repository));
  const result = await presenter.hold();
  assert.equal(result.code, "hold-committed-cart-not-cleared");
  assert.equal(presenter.state.kind, "ready");

  repository.listFailure = true;
  await presenter.refresh();
  assert.equal(presenter.state.kind, "failed");
  assert.deepEqual(presenter.state.rows, []);
});
