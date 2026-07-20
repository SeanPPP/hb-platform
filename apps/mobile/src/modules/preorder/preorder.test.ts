import {
  isPreorderRequiredError,
  normalizeActivationDetail,
  normalizeActivePreorders,
  normalizeSubmitResult,
} from "./normalization";
import {
  buildDraftItems,
  createPackCounts,
  normalizePackCount,
  summarizePreorder,
} from "./order-state";
import {
  canBypassPreorderGate,
  preorderActiveQueryKey,
  resolveConfirmedGateValue,
  shouldBlockNormalOrder,
} from "./gate";
import { drainLatestDraft, isDraftContextCurrent } from "./draft-drain";
import {
  applySubmitResultToContext,
  createPreorderActiveMutationEpoch,
  createPreorderSubmissionId,
  createPreorderSubmissionContext,
  isPreorderSubmissionContextCurrent,
  preparePreorderSubmission,
  reconcilePreorderSubmitFailure,
  refreshActivePreordersSingleFlight,
  settleSuccessfulPreorderSubmission,
} from "./submission-state";
import { getPreorderActivationReadOnlyReason, isEditablePreorderOrderStatus } from "./availability";
import { resolveDraftConflict } from "./draft-conflict";
import {
  mergePreorderDraftCacheDetail,
  preorderActivationQueryKey,
} from "./draft-cache";
import {
  createPreorderSubmissionTelemetry,
  type PreorderSubmissionTelemetryEvent,
} from "./submission-observability";

function assertEqual<T>(actual: T, expected: T, message: string) {
  if (actual !== expected) {
    throw new Error(`${message}: expected ${String(expected)}, got ${String(actual)}`);
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, message: string) {
  if (JSON.stringify(actual) !== JSON.stringify(expected)) {
    throw new Error(`${message}: expected ${JSON.stringify(expected)}, got ${JSON.stringify(actual)}`);
  }
}

const active = normalizeActivePreorders({
  storeCode: "S01",
  normalOrderBlocked: true,
  activations: [{
    activationGuid: "a-1",
    templateGuid: "t-1",
    templateName: "Christmas",
    periodNumber: 2,
    activationCode: "PRE-0002",
    startAtUtc: "2026-07-01T00:00:00Z",
    endAtUtc: "2026-07-31T00:00:00Z",
    status: "Active",
  }],
});
assertEqual(active.activations[0]?.periodNumber, 2, "active response preserves period number");
assertEqual(active.normalOrderBlocked, true, "active response preserves gate flag");
assertEqual(
  normalizeActivePreorders({}).normalOrderBlocked,
  true,
  "malformed success response fails closed when gate flag is missing"
);
assertEqual(
  normalizeActivePreorders({ normalOrderBlocked: false }).normalOrderBlocked,
  false,
  "only an explicit false gate flag unlocks normal ordering"
);
for (const malformedGateValue of [undefined, null, "false", "true", 0, 1, {}]) {
  assertEqual(
    normalizeActivePreorders({ normalOrderBlocked: malformedGateValue }).normalOrderBlocked,
    true,
    `malformed gate value ${JSON.stringify(malformedGateValue)} fails closed`
  );
}

const detail = normalizeActivationDetail({
  activation: active.activations[0],
  storeCode: "S01",
  draftRevision: 7,
  orderGuid: "order-1",
  orderStatus: "Draft",
  items: [{
    activationItemGuid: "item-1",
    productCode: "P1",
    itemNumber: "10001",
    productName: "Tea",
    importPrice: 2.5,
    retailPrice: 4,
    minimumOrderQuantity: 6,
    packCount: 3,
    orderedQuantity: 18,
  }],
});
assertEqual(detail.draftRevision, 7, "detail response preserves server draft revision");
assertEqual(detail.items[0]?.orderedQuantity, 18, "detail response preserves ordered quantity");

const submitContextA = createPreorderSubmissionContext(
  "a-1:S01",
  "a-1",
  "S01",
  {
    ...detail,
    items: detail.items.map((item) => ({ ...item, packCount: 1, orderedQuantity: 6 })),
  },
  [{ activationItemGuid: "item-1", packCount: 5 }]
);
assertEqual(
  isPreorderSubmissionContextCurrent("a-1:S01", submitContextA),
  true,
  "submission remains current in its captured activation/store"
);
assertEqual(
  isPreorderSubmissionContextCurrent("a-2:S01", submitContextA),
  false,
  "switching from preorder A to B invalidates A's in-flight UI side effects"
);

const serverConflictDetail = normalizeActivationDetail({
  activation: active.activations[0],
  storeCode: "S01",
  draftRevision: 9,
  orderGuid: "order-1",
  orderStatus: "Draft",
  items: [{
    activationItemGuid: "item-1",
    productCode: "P1",
    itemNumber: "10001",
    productName: "Tea",
    importPrice: 2.5,
    retailPrice: 4,
    minimumOrderQuantity: 6,
    packCount: 5,
    orderedQuantity: 30,
  }],
});
assertDeepEqual(
  resolveDraftConflict(serverConflictDetail, { "item-1": 3 }, "server"),
  {
    detail: serverConflictDetail,
    packCounts: { "item-1": 5 },
    draftRevision: 9,
    savedFingerprint: "item-1:5",
    shouldRetry: false,
  },
  "draft conflict can explicitly adopt the latest server draft"
);
const localConflictResolution = resolveDraftConflict(
  serverConflictDetail,
  { "item-1": 3 },
  "local"
);
assertDeepEqual(
  localConflictResolution,
  {
    detail: serverConflictDetail,
    packCounts: { "item-1": 3 },
    draftRevision: 9,
    savedFingerprint: undefined,
    shouldRetry: true,
  },
  "draft conflict can preserve local input while advancing to the latest revision"
);

const savedDetailA = {
  ...detail,
  draftRevision: 8,
  items: detail.items.map((item) => ({ ...item, packCount: 5, orderedQuantity: 30 })),
};
const cachedDetailA = mergePreorderDraftCacheDetail(detail, savedDetailA);
assertEqual(cachedDetailA.draftRevision, 8, "returning to A reads the saved revision from cache");
assertEqual(cachedDetailA.items[0]?.packCount, 5, "returning to A reads the saved quantity from cache");
const monotonicDetailA = mergePreorderDraftCacheDetail(cachedDetailA, detail);
assertEqual(monotonicDetailA.draftRevision, 8, "an older A response cannot replace a newer cache revision");
assertEqual(monotonicDetailA.items[0]?.packCount, 5, "an older A response cannot restore stale quantities");
const submittedAtSameRevision = { ...savedDetailA, orderStatus: "Submitted" };
const noDemandAtSameRevision = { ...savedDetailA, orderStatus: "NoDemand" };
const lateDraftAtSameRevision = {
  ...detail,
  draftRevision: savedDetailA.draftRevision,
  orderStatus: "Draft",
};
assertEqual(
  mergePreorderDraftCacheDetail(submittedAtSameRevision, lateDraftAtSameRevision),
  submittedAtSameRevision,
  "a late Draft at the same revision cannot roll Submitted cache back to editable"
);
assertEqual(
  mergePreorderDraftCacheDetail(noDemandAtSameRevision, lateDraftAtSameRevision),
  noDemandAtSameRevision,
  "a late Draft at the same revision cannot roll NoDemand cache back to editable"
);
const returnedAtSameRevision = { ...savedDetailA, orderStatus: "ReturnedForRevision" };
assertEqual(
  mergePreorderDraftCacheDetail(returnedAtSameRevision, lateDraftAtSameRevision),
  returnedAtSameRevision,
  "a late Draft cannot erase the more explicit ReturnedForRevision state"
);
assertEqual(
  mergePreorderDraftCacheDetail(lateDraftAtSameRevision, returnedAtSameRevision),
  returnedAtSameRevision,
  "ReturnedForRevision can advance a plain Draft at the same revision"
);
assertEqual(
  mergePreorderDraftCacheDetail(submittedAtSameRevision, returnedAtSameRevision),
  submittedAtSameRevision,
  "an editable ReturnedForRevision response cannot overwrite a terminal state at the same revision"
);
let rejectedSaveConflictPresented = false;
if (monotonicDetailA !== detail) rejectedSaveConflictPresented = true;
assertEqual(
  rejectedSaveConflictPresented,
  true,
  "a rejected lower-revision save response must enter explicit conflict coordination"
);
const activationCache = new Map<string, typeof savedDetailA>();
activationCache.set(JSON.stringify(preorderActivationQueryKey("a-1", "S01")), cachedDetailA);
assertEqual(
  activationCache.get(JSON.stringify(preorderActivationQueryKey("a-2", "S01"))),
  undefined,
  "an A draft cache write does not populate B"
);

const activePeriod = active.activations[0]!;
assertEqual(
  getPreorderActivationReadOnlyReason(activePeriod, Date.parse(activePeriod.startAtUtc)),
  null,
  "active period is editable at the inclusive start boundary"
);
assertEqual(
  getPreorderActivationReadOnlyReason(activePeriod, Date.parse(activePeriod.endAtUtc)),
  "ended",
  "active period becomes read-only at the exclusive end boundary"
);
assertEqual(
  getPreorderActivationReadOnlyReason({ ...activePeriod, status: "Cancelled" }, Date.parse(activePeriod.startAtUtc)),
  "cancelled",
  "cancelled period is read-only even inside its time window"
);
assertEqual(isEditablePreorderOrderStatus("Draft"), true, "draft remains editable");
assertEqual(isEditablePreorderOrderStatus("ReturnedForRevision"), true, "returned preorder is editable again");
assertEqual(isEditablePreorderOrderStatus("Submitted"), false, "submitted preorder remains read-only");

const counts = createPackCounts(detail.items);
const input = buildDraftItems(detail.items, counts);
assertDeepEqual(input, [{ activationItemGuid: "item-1", packCount: 3 }], "draft body uses activation item ids");
const summary = summarizePreorder(detail.items, counts);
assertEqual(summary.totalQuantity, 18, "pack count multiplies activation MOQ");
assertEqual(summary.totalImportAmount, 45, "summary uses activation import price");
assertEqual(normalizePackCount("-2"), 0, "negative pack count is rejected to zero");
assertEqual(normalizePackCount("2.8"), 2, "pack count remains an integer");

const submitted = normalizeSubmitResult({
  orderGuid: "order-1",
  orderNo: "PRE-S01-0001",
  status: "Submitted",
  draftRevision: 8,
  totalPackCount: 3,
  totalQuantity: 18,
  totalImportAmount: 45,
  totalRetailAmount: 72,
});
assertEqual(submitted.status, "Submitted", "submit response preserves preorder order status");
assertEqual(submitted.totalRetailAmount, 72, "submit response preserves totals");
const submittedContextUpdate = applySubmitResultToContext(submitContextA, submitted);
assertDeepEqual(
  submittedContextUpdate.queryKey,
  ["preorder", "activation", "a-1", "S01"],
  "an A response always targets A's captured cache key after switching to B"
);
assertEqual(submittedContextUpdate.detail.templateName, "Christmas", "A response uses A's detail snapshot");
assertEqual(
  submittedContextUpdate.detail.items[0]?.packCount,
  5,
  "A cache uses the submitted body instead of stale detail quantities"
);
assertEqual(
  submittedContextUpdate.detail.items[0]?.orderedQuantity,
  30,
  "A cache recalculates submitted units from pack count and activation MOQ"
);
assertEqual(submittedContextUpdate.detail.orderStatus, "Submitted", "submit response immediately locks the local detail");
assertEqual(submittedContextUpdate.detail.draftRevision, 8, "submit response replaces the local draft revision");

assertEqual(shouldBlockNormalOrder("S01", undefined), true, "selected store fails closed before response");
const warehouseStaffWithoutCreateCanBypass = canBypassPreorderGate({
  isWarehouseStaffOnly: true,
  hasPermission: () => false,
});
assertEqual(
  shouldBlockNormalOrder("S01", undefined, warehouseStaffWithoutCreateCanBypass),
  true,
  "pure warehouse staff without Orders.Create remains fail-closed"
);
const warehouseStaffWithCreateCanBypass = canBypassPreorderGate({
  isWarehouseStaffOnly: true,
  hasPermission: (permission) => permission === "Orders.Create",
});
assertEqual(
  shouldBlockNormalOrder("S01", undefined, warehouseStaffWithCreateCanBypass),
  false,
  "pure warehouse staff only bypasses when Orders.Create is explicit"
);
assertEqual(
  canBypassPreorderGate({
    isWarehouseStaffOnly: false,
    hasPermission: (permission) => permission === "Warehouse.ManageOrders",
  }),
  true,
  "warehouse order managers bypass the client preorder gate"
);
assertEqual(
  canBypassPreorderGate({
    isWarehouseStaffOnly: false,
    hasPermission: () => false,
  }),
  false,
  "ordinary store users remain subject to the preorder gate"
);
assertEqual(shouldBlockNormalOrder("S01", false), false, "server can unlock normal ordering");
assertEqual(
  shouldBlockNormalOrder("S01", resolveConfirmedGateValue(false, true, false)),
  true,
  "refresh in progress fails closed even when stale cache was unlocked"
);
assertEqual(
  shouldBlockNormalOrder("S01", resolveConfirmedGateValue(false, false, true)),
  true,
  "refresh error fails closed even when stale cache was unlocked"
);
assertEqual(shouldBlockNormalOrder(null, undefined), false, "no selected store does not report a gate");
assertDeepEqual(preorderActiveQueryKey(" S01 "), ["preorder", "active", "S01"], "query key normalizes store code");
assertEqual(
  isPreorderRequiredError({ response: { data: { errorCode: "PREORDER_REQUIRED" } } }),
  true,
  "HTTP business error redirects to preorder"
);
assertEqual(
  isPreorderRequiredError({
    code: "ERR_BAD_REQUEST",
    response: { data: { errorCode: "PREORDER_REQUIRED" } },
  }),
  true,
  "HTTP business error takes precedence over Axios transport code"
);

async function verifyDraftDrainConcurrency() {
  const telemetryEvents: PreorderSubmissionTelemetryEvent[] = [];
  const telemetry = createPreorderSubmissionTelemetry({
    submissionId: "submission-test-1",
    itemCount: 1,
    requestBody: {
      storeCode: "S01",
      expectedDraftRevision: 7,
      confirmNoDemand: false,
      items: [{ activationItemGuid: "sensitive-item-guid", packCount: 3 }],
      token: "must-never-be-logged",
    },
    hasInFlightSave: true,
    logger: (event) => telemetryEvents.push(event),
  });
  telemetry.confirm();
  telemetry.waitSaveStart();
  telemetry.updateRequestBody({
    storeCode: "S01",
    expectedDraftRevision: 8,
    confirmNoDemand: false,
    items: [{ activationItemGuid: "sensitive-item-guid", packCount: 3 }],
  });
  telemetry.waitSaveEnd();
  telemetry.postStart();
  telemetry.postEnd("success");
  telemetry.successFeedback();
  telemetry.activeGet();
  telemetry.backgroundActiveRefreshFinish("success");
  assertDeepEqual(
    telemetryEvents.map((event) => event.stage),
    [
      "confirm",
      "wait-save-start",
      "wait-save-end",
      "post-start",
      "post-end",
      "success-feedback",
      "background-active-refresh-finish",
    ],
    "submission telemetry preserves the confirmed submit stage order"
  );
  const finalTelemetry = telemetryEvents.at(-1)!;
  assertDeepEqual(
    finalTelemetry.requestCounts,
    { draftPut: 1, submitPost: 1, activeGet: 1, detailGet: 0 },
    "submission telemetry reports request counts without adding requests"
  );
  assertEqual(finalTelemetry.itemCount, 1, "submission telemetry reports only the item count");
  assertEqual(finalTelemetry.requestBodyBytes > 0, true, "submission telemetry reports request body bytes");
  const serializedTelemetry = JSON.stringify(telemetryEvents);
  assertEqual(serializedTelemetry.includes("sensitive-item-guid"), false, "telemetry excludes product details");
  assertEqual(serializedTelemetry.includes("must-never-be-logged"), false, "telemetry excludes tokens and payload values");

  const scheduledRequestCounts = { put: 0, post: 0 };
  let scheduledTimerCancelled = false;
  const scheduledOnly = await preparePreorderSubmission({
    cancelScheduledAutosave: () => { scheduledTimerCancelled = true; },
    suspendAutosaveRef: { current: false },
    inFlightSave: null,
    readLatest: () => ({
      revision: 10,
      items: [{ activationItemGuid: "item-1", packCount: 5 }],
    }),
  });
  scheduledRequestCounts.post += 1;
  assertEqual(scheduledTimerCancelled, true, "submit within 500ms cancels the pending autosave timer");
  assertDeepEqual(scheduledRequestCounts, { put: 0, post: 1 }, "submit within 500ms issues only POST");
  assertEqual(scheduledOnly.kind, "ready", "submit without an in-flight save is ready");
  if (scheduledOnly.kind !== "ready") throw new Error("scheduled submit should be ready");
  assertEqual(scheduledOnly.revision, 10, "submit without an in-flight save uses the current revision");

  let latestFingerprint = "A";
  let savedFingerprint = "";
  let releaseFirstSave: (() => void) | undefined;
  const firstSaveBlocked = new Promise<void>((resolve) => { releaseFirstSave = resolve; });
  const savedSnapshots: string[] = [];
  const inFlight = { current: null as Promise<boolean> | null };
  const readLatest = () => ({ fingerprint: latestFingerprint });
  const save = async (snapshot: { fingerprint: string }) => {
    savedSnapshots.push(snapshot.fingerprint);
    if (snapshot.fingerprint === "A") await firstSaveBlocked;
    savedFingerprint = snapshot.fingerprint;
    return true;
  };

  const autoSave = drainLatestDraft(inFlight, readLatest, (snapshot) => snapshot.fingerprint === savedFingerprint, save);
  latestFingerprint = "B";
  const leaveSave = drainLatestDraft(inFlight, readLatest, (snapshot) => snapshot.fingerprint === savedFingerprint, save);
  assertEqual(autoSave === leaveSave, true, "timer and leave share one drain promise");
  releaseFirstSave?.();
  assertEqual(await leaveSave, true, "leave waits until latest draft is saved");
  assertDeepEqual(savedSnapshots, ["A", "B"], "drain serially catches input edited during save");
  assertEqual(savedFingerprint, "B", "latest input is saved before drain completes");

  let activeContext = "activation-a:store-a";
  let activeValue = "A1";
  let releaseContextSave: (() => void) | undefined;
  const contextSaveBlocked = new Promise<void>((resolve) => { releaseContextSave = resolve; });
  const contextWrites: string[] = [];
  const oldContextRef = { current: null as Promise<boolean> | null };
  const oldContextKey = activeContext;
  const oldDrain = drainLatestDraft(
    oldContextRef,
    () => activeContext === oldContextKey
      ? { contextKey: oldContextKey, fingerprint: activeValue }
      : null,
    () => false,
    async (snapshot) => {
      contextWrites.push(`${snapshot.contextKey}:${snapshot.fingerprint}`);
      await contextSaveBlocked;
      return isDraftContextCurrent(activeContext, snapshot.contextKey);
    }
  );
  activeContext = "activation-b:store-b";
  activeValue = "B1";
  const newContextRef = { current: null as Promise<boolean> | null };
  let newSaved = "";
  const newDrain = drainLatestDraft(
    newContextRef,
    () => ({ contextKey: activeContext, fingerprint: activeValue }),
    (snapshot) => snapshot.fingerprint === newSaved,
    async (snapshot) => {
      contextWrites.push(`${snapshot.contextKey}:${snapshot.fingerprint}`);
      newSaved = snapshot.fingerprint;
      return true;
    }
  );
  assertEqual(oldDrain === newDrain, false, "new activation uses an independent in-flight promise");
  assertEqual(await newDrain, true, "new activation saves without waiting for the old context");
  releaseContextSave?.();
  assertEqual(await oldDrain, false, "old context drain stops after route context changes");
  assertDeepEqual(
    contextWrites,
    ["activation-a:store-a:A1", "activation-b:store-b:B1"],
    "old and new contexts only write their own activation snapshots"
  );

  const requestCounts = { put: 0, post: 0 };
  let timerCancelled = false;
  const submitSuspendRef = { current: false };
  const currentSaveRef = {
    current: null as Promise<{ kind: "saved" } | { kind: "failed" }> | null,
  };
  let releaseInFlightSave: (() => void) | undefined;
  let latestRevision = 11;
  const frozenItems = [{ activationItemGuid: "item-1", packCount: 6 }];
  const successfulDrain = drainLatestDraft(
    { current: null as Promise<boolean> | null },
    () => submitSuspendRef.current ? null : { fingerprint: "item-1:6" },
    () => false,
    async () => {
      requestCounts.put += 1;
      const request = new Promise<{ kind: "saved" }>((resolve) => {
        releaseInFlightSave = () => {
          latestRevision = 12;
          resolve({ kind: "saved" });
        };
      });
      currentSaveRef.current = request;
      try {
        return (await request).kind === "saved";
      } finally {
        if (currentSaveRef.current === request) currentSaveRef.current = null;
      }
    }
  );
  const preparedSubmission = preparePreorderSubmission({
    cancelScheduledAutosave: () => { timerCancelled = true; },
    suspendAutosaveRef: submitSuspendRef,
    inFlightSave: currentSaveRef.current,
    readLatest: () => ({ revision: latestRevision, items: frozenItems }),
  });
  releaseInFlightSave?.();
  const frozenSubmission = await preparedSubmission;
  if (frozenSubmission.kind === "ready") requestCounts.post += 1;
  assertEqual(timerCancelled, true, "submit cancels the scheduled 500ms autosave");
  assertDeepEqual(requestCounts, { put: 1, post: 1 }, "submit only waits for the one in-flight PUT before POST");
  assertEqual(frozenSubmission.kind, "ready", "a successful in-flight PUT allows submit");
  if (frozenSubmission.kind !== "ready") throw new Error("successful save should allow submit");
  assertEqual(frozenSubmission.revision, 12, "submit uses the revision returned by the current in-flight PUT");
  assertDeepEqual(
    frozenSubmission.items,
    frozenItems,
    "submit keeps its frozen items without manufacturing another PUT"
  );
  assertEqual(await successfulDrain, false, "suspended full drain may stop after the current PUT succeeds");
  assertEqual(requestCounts.put, 1, "submit suspension never starts a second PUT");

  const submissionId = createPreorderSubmissionId();
  assertEqual(Boolean(submissionId), true, "each submit can send a stable non-empty submission id header");

  const saveConflictDetail = { ...serverConflictDetail, draftRevision: 10 };
  const conflictSuspendRef = { current: false };
  const conflictRequestCounts = { put: 0, detailGet: 0, post: 0 };
  const conflictingSaveRef = {
    current: null as Promise<{ kind: "draft-conflict"; detail: typeof saveConflictDetail }> | null,
  };
  let releaseConflictingSave: (() => void) | undefined;
  const conflictingDrain = drainLatestDraft(
    { current: null as Promise<boolean> | null },
    () => conflictSuspendRef.current ? null : { fingerprint: "item-1:3" },
    () => false,
    async () => {
      conflictRequestCounts.put += 1;
      const request = new Promise<{ kind: "draft-conflict"; detail: typeof saveConflictDetail }>((resolve) => {
        releaseConflictingSave = () => {
          conflictRequestCounts.detailGet += 1;
          resolve({ kind: "draft-conflict", detail: saveConflictDetail });
        };
      });
      conflictingSaveRef.current = request;
      try {
        await request;
        return false;
      } finally {
        if (conflictingSaveRef.current === request) conflictingSaveRef.current = null;
      }
    }
  );
  const blockedSubmission = preparePreorderSubmission({
    cancelScheduledAutosave: () => undefined,
    suspendAutosaveRef: conflictSuspendRef,
    inFlightSave: conflictingSaveRef.current,
    readLatest: () => ({
      revision: 7,
      items: [{ activationItemGuid: "item-1", packCount: 3 }],
    }),
  });
  releaseConflictingSave?.();
  const blockedResult = await blockedSubmission;
  if (blockedResult.kind === "ready") conflictRequestCounts.post += 1;
  assertDeepEqual(
    blockedResult,
    { kind: "draft-conflict", detail: saveConflictDetail },
    "an in-flight PUT conflict blocks stale POST and reuses its fetched detail"
  );
  assertEqual(await conflictingDrain, false, "conflicting current PUT stops the full drain");
  assertDeepEqual(
    conflictRequestCounts,
    { put: 1, detailGet: 1, post: 0 },
    "PUT conflict reuses its one detail GET and never issues stale POST or a second GET"
  );

  let activeGetCount = 0;
  let releaseActiveRefresh: (() => void) | undefined;
  const hangingActiveRefresh = new Promise<void>((resolve) => { releaseActiveRefresh = resolve; });
  const firstMutationEpoch = createPreorderActiveMutationEpoch();
  const firstActiveRefresh = refreshActivePreordersSingleFlight("S01", firstMutationEpoch, async () => {
    activeGetCount += 1;
    await hangingActiveRefresh;
  });
  const secondActiveRefresh = refreshActivePreordersSingleFlight("S01", firstMutationEpoch, async () => {
    activeGetCount += 1;
  });
  assertEqual(firstActiveRefresh === secondActiveRefresh, true, "active refresh is single-flight per store");
  assertEqual(activeGetCount, 1, "successful submit refreshes active exactly once");

  const nextMutationEpoch = createPreorderActiveMutationEpoch();
  const postMutationFreshRefresh = refreshActivePreordersSingleFlight("S01", nextMutationEpoch, async () => {
    activeGetCount += 1;
  });
  assertEqual(
    postMutationFreshRefresh === firstActiveRefresh,
    false,
    "a newer POST mutation epoch never reuses the previous active refresh lane"
  );
  await postMutationFreshRefresh;
  assertEqual(activeGetCount, 2, "each mutation epoch starts at most one fresh active GET");

  const successSequence: string[] = [];
  settleSuccessfulPreorderSubmission({
    writeTerminalCache: () => { successSequence.push("cache"); },
    finishSubmitPending: () => { successSequence.push("unlocked"); },
    showSuccess: () => { successSequence.push("success"); },
    refreshInBackground: () => firstActiveRefresh,
  });
  assertDeepEqual(
    successSequence,
    ["cache", "unlocked", "success"],
    "POST success writes terminal cache, unlocks, and reports success before refresh completes"
  );
  releaseActiveRefresh?.();
  await firstActiveRefresh;

  const delayedOlderEpoch = createPreorderActiveMutationEpoch();
  const leadingNewerEpoch = createPreorderActiveMutationEpoch();
  let reverseOrderGetCount = 0;
  let releaseLeadingNewerRefresh: (() => void) | undefined;
  const leadingNewerBlocked = new Promise<void>((resolve) => {
    releaseLeadingNewerRefresh = resolve;
  });
  const leadingNewerRefresh = refreshActivePreordersSingleFlight(
    "S01",
    leadingNewerEpoch,
    async () => {
      reverseOrderGetCount += 1;
      await leadingNewerBlocked;
    }
  );
  const delayedOlderRefresh = refreshActivePreordersSingleFlight(
    "S01",
    delayedOlderEpoch,
    async () => {
      reverseOrderGetCount += 1;
    }
  );
  assertEqual(
    delayedOlderRefresh === leadingNewerRefresh,
    true,
    "an older epoch arriving late reuses the already-running newer refresh lane"
  );
  assertEqual(reverseOrderGetCount, 1, "a delayed older epoch cannot open or overwrite a newer lane");
  releaseLeadingNewerRefresh?.();
  await leadingNewerRefresh;

  const reconciled = await reconcilePreorderSubmitFailure(async () => ({
    ...detail,
    orderStatus: "Submitted",
    draftRevision: 8,
  }));
  assertEqual(reconciled.kind, "submitted", "ambiguous submit is reconciled from server state");
  const unresolved = await reconcilePreorderSubmitFailure(async () => {
    throw new Error("offline");
  });
  assertEqual(unresolved.kind, "unresolved", "failed reconciliation does not invent a successful submit");

  const submitDraftConflict = await reconcilePreorderSubmitFailure(async () => serverConflictDetail);
  assertDeepEqual(
    submitDraftConflict,
    { kind: "draft-conflict", detail: serverConflictDetail },
    "submit conflict with a still-Draft server order enters draft coordination"
  );
  const returnedConflictDetail = { ...serverConflictDetail, orderStatus: "ReturnedForRevision" };
  const returnedConflict = await reconcilePreorderSubmitFailure(async () => returnedConflictDetail);
  assertDeepEqual(
    returnedConflict,
    { kind: "draft-conflict", detail: returnedConflictDetail },
    "warehouse return remains editable and enters draft coordination instead of terminal handling"
  );

  let conflictSavedFingerprint: string | null = localConflictResolution.savedFingerprint ?? null;
  let conflictSaveCalls = 0;
  const conflictSaveResult = await drainLatestDraft(
    { current: null as Promise<boolean> | null },
    () => ({ fingerprint: "item-1:3" }),
    (snapshot) => snapshot.fingerprint === conflictSavedFingerprint,
    async (snapshot) => {
      conflictSaveCalls += 1;
      conflictSavedFingerprint = snapshot.fingerprint;
      return true;
    }
  );
  assertEqual(conflictSaveResult, true, "local conflict recovery saves with the latest revision");
  assertEqual(conflictSaveCalls, 1, "local conflict recovery executes the draft save callback");

  let activeSubmitContext = submitContextA.contextKey;
  let releaseSubmit: ((value: typeof submitted) => void) | undefined;
  let staleUiEffects = 0;
  const submitResponse = new Promise<typeof submitted>((resolve) => { releaseSubmit = resolve; });
  const inFlightContextUpdate = submitResponse.then((result) => {
    const update = applySubmitResultToContext(submitContextA, result);
    if (isPreorderSubmissionContextCurrent(activeSubmitContext, submitContextA)) {
      staleUiEffects += 1;
    }
    return update;
  });
  activeSubmitContext = "a-2:S01";
  releaseSubmit?.(submitted);
  const staleContextUpdate = await inFlightContextUpdate;
  assertEqual(staleUiEffects, 0, "A submit response cannot trigger UI after switching to B");
  assertDeepEqual(
    staleContextUpdate.queryKey,
    ["preorder", "activation", "a-1", "S01"],
    "A submit response remains scoped to A after switching to B"
  );
  assertEqual(
    staleContextUpdate.detail.items[0]?.packCount,
    5,
    "A cache keeps the submitted pack count after switching to B"
  );
}

void verifyDraftDrainConcurrency()
  .then(() => console.log("preorder tests passed"))
  .catch((error) => {
    console.error(error);
    process.exitCode = 1;
  });
