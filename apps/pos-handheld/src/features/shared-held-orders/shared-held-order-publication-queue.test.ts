import assert from "node:assert/strict";
import test from "node:test";

import {
  SqliteSharedHeldOrderPublicationQueue,
} from "./shared-held-order-publication-queue";
import {
  TEST_NOW_ISO,
  fakeEncryptor,
  insertHeldOrderRow,
  openTestDatabase,
} from "./shared-held-order-test-support";

const SCOPE = { storeCode: "S1", deviceCode: "HANDHELD-01" } as const;
const PAYLOAD = new TextEncoder().encode("encrypted-held-order-payload");

test("显式共享进入耐久队列，发布版本锁定且失败按退避重试", async () => {
  const connection = await openTestDatabase();
  const queue = new SqliteSharedHeldOrderPublicationQueue(connection);

  await insertHeldOrderRow(connection, {
    holdId: "hold-queue-1",
    payloadCiphertext: PAYLOAD,
    deviceCode: SCOPE.deviceCode,
  });
  await insertHeldOrderRow(connection, {
    holdId: "hold-queue-foreign",
    payloadCiphertext: PAYLOAD,
    deviceCode: "HANDHELD-02",
  });

  assert.deepEqual(await queue.listNeedsEvaluation(SCOPE, 10), []);
  assert.equal(
    await queue.requestShare({
      holdId: "hold-queue-1",
      scope: SCOPE,
      requestedAtIso: TEST_NOW_ISO,
    }),
    "requested",
  );
  assert.equal(
    await queue.requestShare({
      holdId: "hold-queue-1",
      scope: SCOPE,
      requestedAtIso: "2026-07-28T09:00:00.000Z",
    }),
    "already-requested",
  );
  assert.equal(
    await queue.requestShare({
      holdId: "hold-queue-foreign",
      scope: SCOPE,
      requestedAtIso: TEST_NOW_ISO,
    }),
    "not-found",
  );

  const needsEvaluation = await queue.listNeedsEvaluation(SCOPE, 10);
  assert.deepEqual(needsEvaluation.map((row) => row.holdId), ["hold-queue-1"]);
  assert.deepEqual(needsEvaluation[0]?.payloadCiphertext, PAYLOAD);
  assert.equal(
    await queue.applyShareEvaluation({
      holdId: "hold-queue-1",
      evaluation: { outcome: "pending-publish" },
      evaluatedAtIso: TEST_NOW_ISO,
    }),
    "updated",
  );

  assert.equal(
    await queue.pinPublicationPayloadVersion({
      holdId: "hold-queue-1",
      expectedAttemptCount: 0,
      payloadVersion: 2,
    }),
    2,
  );
  assert.equal(
    await queue.pinPublicationPayloadVersion({
      holdId: "hold-queue-1",
      expectedAttemptCount: 0,
      payloadVersion: 1,
    }),
    2,
  );
  assert.equal(
    await queue.recordPublishFailure({
      holdId: "hold-queue-1",
      errorCode: "NETWORK",
      failedAtIso: TEST_NOW_ISO,
    }),
    true,
  );
  assert.equal((await queue.listDue(SCOPE, TEST_NOW_ISO, 10)).length, 0);
  const retryDue = await queue.listDue(SCOPE, "2026-07-28T08:00:30.000Z", 10);
  assert.equal(retryDue.length, 1);
  assert.equal(retryDue[0]?.publishAttemptCount, 1);
  assert.equal(retryDue[0]?.publicationPayloadVersion, 2);
  assert.deepEqual(
    await queue.listShareStates(SCOPE, 10),
    [{
      holdId: "hold-queue-1",
      shareState: "PendingPublish",
      blockReason: null,
      requestedAtIso: TEST_NOW_ISO,
      isSyntheticSharedClaim: false,
    }],
  );

  assert.equal(
    await queue.markPublished({
      holdId: "hold-queue-1",
      remoteRevision: 7,
      remoteUpdatedAtIso: "2026-07-28T08:00:31.000Z",
      expectedAttemptCount: 1,
      publishedAtIso: "2026-07-28T08:00:31.000Z",
    }),
    true,
  );
  assert.equal((await queue.listDue(SCOPE, "2026-07-28T09:00:00.000Z", 10)).length, 0);
  await connection.close();
});
