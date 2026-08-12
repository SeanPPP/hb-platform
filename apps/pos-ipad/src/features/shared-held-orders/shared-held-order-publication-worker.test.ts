import assert from "node:assert/strict";
import test from "node:test";

import type { SharedPayloadEncryptorPort } from "./shared-held-order-claim-repository";
import type {
  SharedHeldOrderCapabilities,
  SharedHeldOrderNetworkApiPort,
  SharedHeldOrderPublishResult,
} from "./shared-held-order-network-api";
import type {
  SharedHeldOrderPublicationQueuePort,
  SharedHeldOrderPublishDueRow,
  SharedHeldOrderEvaluationRow,
  ShareEvaluation,
} from "./shared-held-order-publication-queue";
import { SharedHeldOrderPublicationWorker } from "./shared-held-order-publication-worker";
import type { SharedSaleCartV1 } from "./shared-sale-cart-v1";

import type { PricingCartStateSnapshot } from "@/core/contracts";

const NOW = "2026-07-28T08:00:00.000Z";
const SCOPE = { storeCode: "BNE", deviceCode: "IPAD-1" } as const;

function legacyPricingState(): PricingCartStateSnapshot {
  return {
    revision: 2,
    mode: "sale",
    asOfIso: "2026-07-28T07:00:00.000Z",
    promotions: [],
    lines: [
      {
        lineId: "line-1",
        productCode: "P-1",
        itemNumber: null,
        lookupCode: "100",
        displayName: "Item",
        quantity: 1,
        unitPriceCents: 100,
        basePriceSource: "catalog",
        syncProvenance: { referenceCode: "REF", priceSource: 0 },
        kind: "sale",
        returnSourceKey: null,
        originalOrderGuid: null,
        originalOrderDetailGuid: null,
        discountState: { kind: "none" },
      },
    ],
  };
}

const fakeEncryptor: SharedPayloadEncryptorPort = {
  async encrypt(plaintext: string): Promise<Uint8Array> {
    return new TextEncoder().encode(
      Buffer.from(plaintext, "utf8").toString("base64"),
    );
  },
  async decrypt(ciphertext: Uint8Array): Promise<string> {
    return Buffer.from(new TextDecoder().decode(ciphertext), "base64").toString(
      "utf8",
    );
  },
};

async function ciphertextForLegacy(): Promise<Uint8Array> {
  return fakeEncryptor.encrypt(
    JSON.stringify({ version: 1, pricingState: legacyPricingState() }),
  );
}

class FakeQueue implements SharedHeldOrderPublicationQueuePort {
  public evaluations: readonly ShareEvaluation[] = [];
  public evaluatedHoldIds: string[] = [];
  public blockedReasons: string[] = [];
  public failures: string[] = [];
  public published: string[] = [];
  public due: readonly SharedHeldOrderPublishDueRow[] = [];
  public needsEvaluation: readonly SharedHeldOrderEvaluationRow[] = [];
  public applyResult: "updated" | "already-evaluated" | "not-found" = "updated";
  public markResult = true;

  public async requestShare(): Promise<"ineligible"> {
    return "ineligible";
  }

  public async listShareStates() {
    return [];
  }

  public async listNeedsEvaluation(): Promise<readonly SharedHeldOrderEvaluationRow[]> {
    return this.needsEvaluation;
  }

  public async applyShareEvaluation(input: Readonly<{
    holdId: string;
    evaluation: ShareEvaluation;
    evaluatedAtIso: string;
  }>): Promise<"updated" | "already-evaluated" | "not-found"> {
    this.evaluatedHoldIds.push(input.holdId);
    this.evaluations = [...this.evaluations, input.evaluation];
    return this.applyResult;
  }

  public async listDue(): Promise<readonly SharedHeldOrderPublishDueRow[]> {
    return this.due;
  }

  public async markPublished(input: Readonly<{
    holdId: string;
    remoteRevision: number;
    remoteUpdatedAtIso: string;
    expectedAttemptCount: number;
    publishedAtIso: string;
  }>): Promise<boolean> {
    this.published.push(input.holdId);
    return this.markResult;
  }

  public async recordPublishFailure(input: Readonly<{
    holdId: string;
    errorCode: string;
    failedAtIso: string;
  }>): Promise<boolean> {
    this.failures.push(input.errorCode);
    return true;
  }

  public async blockPublication(input: Readonly<{
    holdId: string;
    reason: string;
    atIso: string;
  }>): Promise<boolean> {
    this.blockedReasons.push(input.reason);
    return true;
  }
}

class FakeApi implements SharedHeldOrderNetworkApiPort {
  public capabilities: SharedHeldOrderCapabilities = {
    enabled: true,
    payloadVersion: 1,
    preparedTtlSeconds: 900,
    forceReleaseSupported: true,
  };
  public capabilitiesError: unknown = null;
  public publishError: unknown = null;
  public publishResponseOverride: SharedHeldOrderPublishResult | null = null;
  public publishedRequests: {
    holdGuid: string;
    idempotencyKey: string;
    cart: SharedSaleCartV1;
  }[] = [];

  public async getCapabilities(): Promise<SharedHeldOrderCapabilities> {
    if (this.capabilitiesError !== null) throw this.capabilitiesError;
    return this.capabilities;
  }

  public async publish(input: Readonly<{
    holdGuid: string;
    storeCode: string;
    deviceCode: string;
    cart: SharedSaleCartV1;
    idempotencyKey: string;
  }>): Promise<SharedHeldOrderPublishResult> {
    if (this.publishError !== null) throw this.publishError;
    this.publishedRequests.push({
      holdGuid: input.holdGuid,
      idempotencyKey: input.idempotencyKey,
      cart: input.cart,
    });
    return this.publishResponseOverride ?? {
      holdGuid: input.holdGuid,
      status: "Pending",
      revision: 7,
      createdAtIso: NOW,
      alreadyExists: false,
    };
  }

  public async listPending() {
    return [];
  }

  public async cancel(holdGuid: string) {
    return {
      holdGuid,
      status: "Cancelled" as const,
      revision: 8,
      updatedAtIso: NOW,
      alreadyCancelled: false,
    };
  }

  public async prepare(_input: Readonly<{
    holdGuid: string;
    claimGuid: string;
    idempotencyKey: string;
  }>): Promise<never> {
    throw new Error("not used");
  }

  public async activate(
    _input: Readonly<{ holdGuid: string; claimGuid: string }>,
  ): Promise<never> {
    throw new Error("not used");
  }

  public async release(
    _input: Readonly<{ holdGuid: string; claimGuid: string }>,
  ): Promise<never> {
    throw new Error("not used");
  }

  public async forceRelease(_input: Readonly<{
    holdGuid: string;
    claimGuid: string;
    reason: string;
  }>): Promise<never> {
    throw new Error("not used");
  }

  public async claimsMine() {
    return [];
  }
}

function dueRow(holdId: string): SharedHeldOrderPublishDueRow {
  return {
    holdId,
    storeCode: "BNE",
    deviceCode: "IPAD-1",
    payloadVersion: 1,
    payloadCiphertext: new Uint8Array(),
    publishAttemptCount: 1,
    nextPublishAtIso: null,
    remoteRevision: null,
    remoteUpdatedAtIso: null,
  };
}

function evalRow(holdId: string, ciphertext: Uint8Array): SharedHeldOrderEvaluationRow {
  return {
    holdId,
    storeCode: "BNE",
    deviceCode: "IPAD-1",
    payloadVersion: 1,
    payloadCiphertext: ciphertext,
  };
}

test("发布 worker：评估 NeedsEvaluation -> PendingPublish，随后发布并持久化远端 revision", async () => {
  const queue = new FakeQueue();
  const api = new FakeApi();
  queue.needsEvaluation = [
    evalRow("hold-1", await ciphertextForLegacy()),
  ];
  queue.due = [
    { ...dueRow("hold-1"), payloadCiphertext: await ciphertextForLegacy() },
  ];
  const worker = new SharedHeldOrderPublicationWorker({
    queue,
    api,
    encryptor: fakeEncryptor,
    nowIso: () => NOW,
    scope: SCOPE,
  });

  const result = await worker.runOnce();
  assert.equal(result.evaluatedOrders, 1);
  assert.equal(result.stagedPendingPublish, 1);
  assert.equal(result.published, 1);
  assert.equal(queue.published[0], "hold-1");
  assert.equal(api.publishedRequests[0]?.idempotencyKey, "hold-1");
  assert.equal(api.publishedRequests[0]?.cart.pricingState.revision, 2);
});

test("发布 worker：损坏 payload 阻断 Blocked，不发布", async () => {
  const queue = new FakeQueue();
  const api = new FakeApi();
  queue.needsEvaluation = [
    evalRow(
      "hold-bad",
      await fakeEncryptor.encrypt(JSON.stringify({ version: 99 })),
    ),
  ];
  const worker = new SharedHeldOrderPublicationWorker({
    queue,
    api,
    encryptor: fakeEncryptor,
    nowIso: () => NOW,
    scope: SCOPE,
  });

  const result = await worker.runOnce();
  assert.equal(result.evaluatedOrders, 1);
  assert.equal(result.blocked, 1);
  assert.equal(queue.evaluations[0]?.outcome, "blocked");
  assert.equal(api.publishedRequests.length, 0);
});

test("发布 worker：NeedsEvaluation 第一行版本不支持 -> blocked，第二行有效 -> pending，不中断整轮", async () => {
  const queue = new FakeQueue();
  const api = new FakeApi();
  queue.needsEvaluation = [
    {
      ...evalRow("hold-v1", await ciphertextForLegacy()),
      payloadVersion: 99,
    },
    evalRow("hold-ok", await ciphertextForLegacy()),
  ];
  const worker = new SharedHeldOrderPublicationWorker({
    queue,
    api,
    encryptor: fakeEncryptor,
    nowIso: () => NOW,
    scope: SCOPE,
  });

  const result = await worker.runOnce();
  assert.equal(result.evaluatedOrders, 2);
  assert.equal(result.blocked, 1);
  assert.equal(result.stagedPendingPublish, 1);
  const first = queue.evaluations[0];
  assert.equal(first?.outcome, "blocked");
  if (first?.outcome === "blocked") {
    assert.equal(first.reason, "LEGACY_PAYLOAD_VERSION_UNSUPPORTED");
  }
  assert.equal(queue.evaluations[1]?.outcome, "pending-publish");
  assert.equal(api.publishedRequests.length, 0);
});

test("发布 worker：NeedsEvaluation JSON 损坏 -> blocked LEGACY_PAYLOAD_CORRUPTED，继续处理后续行", async () => {
  const queue = new FakeQueue();
  const api = new FakeApi();
  queue.needsEvaluation = [
    evalRow(
      "hold-bad-json",
      await fakeEncryptor.encrypt("{not-json"),
    ),
    evalRow("hold-ok", await ciphertextForLegacy()),
  ];
  const worker = new SharedHeldOrderPublicationWorker({
    queue,
    api,
    encryptor: fakeEncryptor,
    nowIso: () => NOW,
    scope: SCOPE,
  });

  const result = await worker.runOnce();
  assert.equal(result.evaluatedOrders, 2);
  assert.equal(result.blocked, 1);
  assert.equal(result.stagedPendingPublish, 1);
  const first = queue.evaluations[0];
  assert.equal(first?.outcome, "blocked");
  if (first?.outcome === "blocked") {
    assert.equal(first.reason, "LEGACY_PAYLOAD_CORRUPTED");
  }
  assert.equal(queue.evaluations[1]?.outcome, "pending-publish");
  assert.equal(api.publishedRequests.length, 0);
});

test("发布 worker：NeedsEvaluation 解密失败 -> blocked LEGACY_PAYLOAD_CORRUPTED，不中止整轮", async () => {
  const queue = new FakeQueue();
  const api = new FakeApi();
  const badCiphertext = new Uint8Array([1, 2, 3]);
  const encryptor: SharedPayloadEncryptorPort = {
    ...fakeEncryptor,
    async decrypt(ciphertext: Uint8Array): Promise<string> {
      if (ciphertext === badCiphertext) throw new Error("decrypt unavailable");
      return fakeEncryptor.decrypt(ciphertext);
    },
  };
  queue.needsEvaluation = [
    evalRow("hold-bad-decrypt", badCiphertext),
    evalRow("hold-ok", await ciphertextForLegacy()),
  ];
  const worker = new SharedHeldOrderPublicationWorker({
    queue,
    api,
    encryptor,
    nowIso: () => NOW,
    scope: SCOPE,
  });

  const result = await worker.runOnce();
  assert.equal(result.evaluatedOrders, 2);
  assert.equal(result.blocked, 1);
  assert.equal(result.stagedPendingPublish, 1);
  const first = queue.evaluations[0];
  assert.equal(first?.outcome, "blocked");
  if (first?.outcome === "blocked") {
    assert.equal(first.reason, "LEGACY_PAYLOAD_CORRUPTED");
  }
  assert.equal(queue.evaluations[1]?.outcome, "pending-publish");
  assert.equal(api.publishedRequests.length, 0);
});

test("发布 worker：capability disabled 只记录退避，本地挂单不动", async () => {
  const queue = new FakeQueue();
  const api = new FakeApi();
  api.capabilities = {
    enabled: false,
    payloadVersion: 1,
    preparedTtlSeconds: 900,
    forceReleaseSupported: true,
  };
  queue.due = [
    { ...dueRow("hold-1"), payloadCiphertext: await ciphertextForLegacy() },
  ];
  const worker = new SharedHeldOrderPublicationWorker({
    queue,
    api,
    encryptor: fakeEncryptor,
    nowIso: () => NOW,
    scope: SCOPE,
  });

  const result = await worker.runOnce();
  assert.equal(result.failedCapability, 1);
  assert.equal(queue.failures[0], "SHARED_HELD_ORDER_DISABLED");
  assert.equal(api.publishedRequests.length, 0);
});

test("发布 worker：capability 网络不可用对每个 due 行记录稳定退避码，本地挂单不动", async () => {
  const queue = new FakeQueue();
  const api = new FakeApi();
  api.capabilitiesError = new Error("network down");
  queue.due = [
    { ...dueRow("hold-1"), payloadCiphertext: await ciphertextForLegacy() },
    { ...dueRow("hold-2"), payloadCiphertext: await ciphertextForLegacy() },
  ];
  const worker = new SharedHeldOrderPublicationWorker({
    queue,
    api,
    encryptor: fakeEncryptor,
    nowIso: () => NOW,
    scope: SCOPE,
  });

  const result = await worker.runOnce();
  assert.equal(result.failedCapability, 2);
  assert.deepEqual(queue.failures, [
    "SHARED_HELD_ORDER_CAPABILITY_UNAVAILABLE",
    "SHARED_HELD_ORDER_CAPABILITY_UNAVAILABLE",
  ]);
  assert.equal(api.publishedRequests.length, 0);
});

test("发布 worker：publish 失败记录退避并保留 PendingPublish；重试同 key", async () => {
  const queue = new FakeQueue();
  const api = new FakeApi();
  api.publishError = Object.assign(new Error("network"), {
    code: "SHARED_HELD_ORDER_BUSY",
  });
  queue.due = [
    { ...dueRow("hold-1"), payloadCiphertext: await ciphertextForLegacy() },
  ];
  const worker = new SharedHeldOrderPublicationWorker({
    queue,
    api,
    encryptor: fakeEncryptor,
    nowIso: () => NOW,
    scope: SCOPE,
  });

  const result = await worker.runOnce();
  assert.equal(result.failedPublish, 1);
  assert.equal(queue.failures[0], "SHARED_HELD_ORDER_BUSY");
  assert.equal(queue.published.length, 0);

  api.publishError = null;
  const retried = await worker.runOnce();
  assert.equal(retried.published, 1);
  assert.equal(api.publishedRequests[0]?.idempotencyKey, "hold-1");
});

test("发布 worker：响应 holdGuid 与请求不一致 -> 不 markPublished，记录稳定退避", async () => {
  const queue = new FakeQueue();
  const api = new FakeApi();
  api.publishResponseOverride = {
    holdGuid: "hold-OTHER",
    status: "Pending",
    revision: 7,
    createdAtIso: NOW,
    alreadyExists: false,
  };
  queue.due = [
    { ...dueRow("hold-1"), payloadCiphertext: await ciphertextForLegacy() },
  ];
  const worker = new SharedHeldOrderPublicationWorker({
    queue,
    api,
    encryptor: fakeEncryptor,
    nowIso: () => NOW,
    scope: SCOPE,
  });

  const result = await worker.runOnce();
  assert.equal(result.failedPublish, 1);
  assert.equal(result.published, 0);
  assert.equal(queue.failures[0], "SHARED_HELD_ORDER_MISMATCH");
  assert.equal(queue.published.length, 0);
});

test("发布 worker：PendingPublish payload 确定性损坏时转 Blocked，不无限退避", async () => {
  const queue = new FakeQueue();
  const api = new FakeApi();
  queue.due = [
    {
      ...dueRow("hold-corrupted"),
      payloadVersion: 99,
      payloadCiphertext: new Uint8Array([1, 2, 3]),
    },
  ];
  const worker = new SharedHeldOrderPublicationWorker({
    queue,
    api,
    encryptor: fakeEncryptor,
    nowIso: () => NOW,
    scope: SCOPE,
  });

  const result = await worker.runOnce();

  assert.equal(result.blocked, 1);
  assert.equal(result.failedPublish, 0);
  assert.deepEqual(queue.blockedReasons, ["LEGACY_PAYLOAD_VERSION_UNSUPPORTED"]);
  assert.deepEqual(queue.failures, []);
  assert.deepEqual(queue.published, []);
});

test("发布 worker：幂等重放远端已 Claimed 仍按匹配 holdGuid markPublished，不要求 status Pending", async () => {
  const queue = new FakeQueue();
  const api = new FakeApi();
  api.publishResponseOverride = {
    holdGuid: "hold-1",
    status: "Claimed",
    revision: 7,
    createdAtIso: NOW,
    alreadyExists: true,
  };
  queue.due = [
    { ...dueRow("hold-1"), payloadCiphertext: await ciphertextForLegacy() },
  ];
  const worker = new SharedHeldOrderPublicationWorker({
    queue,
    api,
    encryptor: fakeEncryptor,
    nowIso: () => NOW,
    scope: SCOPE,
  });

  const result = await worker.runOnce();
  assert.equal(result.published, 1);
  assert.equal(queue.published[0], "hold-1");
  assert.equal(queue.failures.length, 0);
});

test("发布 worker：删除竞态返回 Cancelled 时稳定阻断队列且绝不重新标记为 Published", async () => {
  const queue = new FakeQueue();
  const api = new FakeApi();
  api.publishResponseOverride = {
    holdGuid: "hold-1",
    status: "Cancelled",
    revision: 8,
    createdAtIso: NOW,
    alreadyExists: true,
  };
  queue.due = [
    { ...dueRow("hold-1"), payloadCiphertext: await ciphertextForLegacy() },
  ];
  const worker = new SharedHeldOrderPublicationWorker({
    queue,
    api,
    encryptor: fakeEncryptor,
    nowIso: () => NOW,
    scope: SCOPE,
  });

  const result = await worker.runOnce();

  assert.equal(result.blocked, 1);
  assert.equal(result.failedPublish, 0);
  assert.equal(result.published, 0);
  assert.deepEqual(queue.blockedReasons, ["SHARED_HELD_ORDER_CANCELLED"]);
  assert.deepEqual(queue.failures, []);
  assert.deepEqual(queue.published, []);
});

test("发布 worker：只评估和发布当前门店设备 scope 的队列行", async () => {
  const queue = new FakeQueue();
  const api = new FakeApi();
  queue.needsEvaluation = [
    {
      ...evalRow("hold-foreign-eval", await ciphertextForLegacy()),
      storeCode: "SYD",
    },
    evalRow("hold-local-eval", await ciphertextForLegacy()),
  ];
  queue.due = [
    {
      ...dueRow("hold-foreign-due"),
      deviceCode: "IPAD-2",
      payloadCiphertext: await ciphertextForLegacy(),
    },
    {
      ...dueRow("hold-local-due"),
      payloadCiphertext: await ciphertextForLegacy(),
    },
  ];
  const worker = new SharedHeldOrderPublicationWorker({
    queue,
    api,
    encryptor: fakeEncryptor,
    nowIso: () => NOW,
    scope: SCOPE,
  });

  const result = await worker.runOnce();

  assert.equal(result.evaluatedOrders, 1);
  assert.equal(result.stagedPendingPublish, 1);
  assert.deepEqual(queue.evaluatedHoldIds, ["hold-local-eval"]);
  assert.equal(result.published, 1);
  assert.deepEqual(
    api.publishedRequests.map((request) => request.holdGuid),
    ["hold-local-due"],
  );
  assert.deepEqual(queue.published, ["hold-local-due"]);
  assert.deepEqual(queue.failures, []);
});
