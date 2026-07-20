import { readFileSync } from "node:fs";
import { resolve } from "node:path";

function assertIncludes(source: string, expected: string, message: string) {
  if (!source.includes(expected)) {
    throw new Error(`${message}: missing ${expected}`);
  }
}

function assertNotIncludes(source: string, expected: string, message: string) {
  if (source.includes(expected)) {
    throw new Error(`${message}: unexpected ${expected}`);
  }
}

const root = process.cwd();
const rootLayout = readFileSync(resolve(root, "app/_layout.tsx"), "utf8");
const preorderLayout = readFileSync(resolve(root, "app/preorders/_layout.tsx"), "utf8");
const home = readFileSync(resolve(root, "app/(tabs)/home.tsx"), "utf8");
const cart = readFileSync(resolve(root, "app/(tabs)/cart.tsx"), "utf8");
const preorderDetail = readFileSync(
  resolve(root, "src/modules/preorder/preorder-detail-screen.tsx"),
  "utf8"
);
const preorderApi = readFileSync(resolve(root, "src/modules/preorder/api.ts"), "utf8");
const performSubmitSource = preorderDetail.slice(
  preorderDetail.indexOf("const performSubmit"),
  preorderDetail.indexOf("const confirmSubmit")
);

assertIncludes(rootLayout, '<Stack.Screen name="preorders" />', "root stack registers preorder");
assertIncludes(preorderLayout, "headerShown: false", "preorder is a standalone full-screen stack");
assertIncludes(home, "PreorderGateBanner", "home exposes the preorder gate");
assertIncludes(home, "canBypassPreorderGate(access)", "warehouse users bypass the home preorder prompt");
assertIncludes(cart, "PreorderGateBanner", "cart exposes the preorder gate");
assertNotIncludes(
  home,
  "if (preorderGate.normalOrderBlocked)",
  "preorder does not block normal product scans or cart changes"
);
assertIncludes(cart, 'mode: "add-to-cart"', "cart scans remain in add-to-cart mode");
assertNotIncludes(
  cart,
  "quantityLocked={preorderGate.normalOrderBlocked}",
  "preorder does not lock normal cart quantities"
);
if ((cart.match(/if \(preorderGate\.normalOrderBlocked\)/g) ?? []).length !== 2) {
  throw new Error("preorder gate must remain only on cart checkout confirmation and final submit");
}
assertIncludes(
  preorderDetail,
  "applySubmitResultToContext(submitContext, submitted)",
  "submit response uses the immutable activation/store/detail snapshot"
);
assertIncludes(
  preorderDetail,
  "preparePreorderSubmission({",
  "submit cancels pending autosave and only waits for an existing save"
);
assertIncludes(
  preorderDetail,
  "disabled={!isEditable || submitPending || leaveSavePending}",
  "submit remains clickable while an autosave request is already in flight"
);
assertNotIncludes(
  preorderDetail,
  'disabled={!isEditable || submitPending || leaveSavePending || saveStatus === "saving"}',
  "saving status must not prevent submit from waiting for the shared in-flight request"
);
assertIncludes(
  preorderDetail,
  "settleSuccessfulPreorderSubmission({",
  "successful POST unlocks and reports success before background refresh"
);
assertIncludes(
  preorderApi,
  '"X-Preorder-Submission-Id"',
  "submit API supports the optional preorder submission id header"
);
assertNotIncludes(
  performSubmitSource,
  "persistLatestDraft(",
  "submit path never manufactures a draft PUT"
);
if ((performSubmitSource.match(/submitPreorder\(/g) ?? []).length !== 1) {
  throw new Error("submit path must issue one POST");
}
assertIncludes(
  performSubmitSource,
  "const submissionId = createPreorderSubmissionId()",
  "each confirmed submit creates one submission id"
);
if ((performSubmitSource.match(/createPreorderSubmissionId\(\)/g) ?? []).length !== 1) {
  throw new Error("a confirmed submit must create exactly one reusable submission id");
}
for (const telemetryStage of [
  "telemetry.confirm()",
  "telemetry.waitSaveStart()",
  "telemetry.waitSaveEnd()",
  "telemetry.postStart()",
  'telemetry.postEnd("success")',
  'telemetry.postEnd("error")',
]) {
  assertIncludes(performSubmitSource, telemetryStage, `submit path records ${telemetryStage}`);
}
assertIncludes(
  preorderDetail,
  "telemetry.successFeedback()",
  "success feedback is observable before background refresh completion"
);
assertIncludes(
  preorderDetail,
  'telemetry.backgroundActiveRefreshFinish("success")',
  "successful active refresh completion is observable"
);
assertIncludes(
  preorderDetail,
  'telemetry.backgroundActiveRefreshFinish("error")',
  "failed active refresh completion remains observable without changing submit success"
);
assertNotIncludes(
  preorderDetail,
  "await fetchPreorderActivation(submitContext.activationGuid",
  "normal successful submit does not GET detail"
);
if ((preorderDetail.match(/fetchActivePreorders\(submitContext\.storeCode, signal\)/g) ?? []).length !== 1) {
  throw new Error("successful submit must issue exactly one active GET through the single-flight refresh");
}
if ((preorderDetail.match(/isDraftContextCurrent\(activeDraftContextKeyRef\.current, snapshotContextKey\)/g) ?? []).length < 3) {
  throw new Error("draft save success and failure paths must both guard the activation/store context");
}
assertIncludes(
  preorderDetail,
  'errorCode === "PREORDER_DRAFT_CONFLICT"',
  "a draft conflict triggers explicit recovery instead of repeating the stale revision"
);
assertIncludes(
  preorderDetail,
  't("conflict.useServer")',
  "draft conflict can adopt the latest server draft"
);
assertIncludes(
  preorderDetail,
  't("conflict.keepLocal")',
  "draft conflict can preserve local input and retry with the latest revision"
);
assertIncludes(
  preorderDetail,
  't("leave.unsavedTitle")',
  "every failed leave-save offers an explicit safe exit"
);
assertIncludes(
  preorderDetail,
  't("leave.discard")',
  "discarding any unsaved draft remains an explicit action"
);
assertIncludes(
  preorderDetail,
  "reconcilePreorderSubmitFailure",
  "an ambiguous submit response is reconciled before reporting failure"
);
assertIncludes(
  preorderDetail,
  'submitFailure.kind === "draft-conflict"',
  "a submit conflict that remains Draft enters the same server/local coordinator"
);
assertIncludes(
  preorderDetail,
  'presentDraftConflict(submitFailure.detail, "submit")',
  "draft coordination explicitly cancels the current submit instead of automatically resubmitting"
);
assertIncludes(
  preorderDetail,
  "lastSavedFingerprintRef.current = recovered.savedFingerprint ?? null",
  "keeping local input invalidates the pre-conflict saved fingerprint and forces a real save"
);
assertIncludes(
  preorderDetail,
  "usePreventRemove((hasUnsavedDraft || submitPending) && !allowRemove",
  "an in-flight submit blocks gestures and navigation removal"
);
assertIncludes(
  preorderDetail,
  "disabled={submitPending || leaveSavePending}",
  "the header back button is disabled while submitting"
);
if ((preorderDetail.match(/isPreorderSubmissionContextCurrent\(/g) ?? []).length < 6) {
  throw new Error("every async submit stage must guard against activation/store context changes");
}
assertIncludes(
  preorderDetail,
  "mergePreorderDraftCacheDetail(current, response)",
  "a successful draft save writes its full response into the activation cache monotonically"
);
assertIncludes(
  preorderDetail,
  "snapshot.queryKey",
  "a stale save response remains scoped to its immutable activation/store cache key"
);
assertIncludes(
  preorderDetail,
  "presentDraftConflict(cachedDetail)",
  "a lower-revision save response opens the same server/local conflict coordinator"
);
assertIncludes(
  preorderDetail,
  "getPreorderActivationReadOnlyReason(detail",
  "activation status and effective window determine whether quantities are editable"
);
assertIncludes(
  preorderDetail,
  't(`detail.readOnlyReason.${activationReadOnlyReason}`)',
  "inactive activation presents a clear read-only reason"
);

console.log("preorder route contract tests passed");
