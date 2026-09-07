import assert from "node:assert/strict";
import {
  buildConfirmationSummary,
  buildNativePolicyPayload,
  buildOtaPolicyPayload,
  isPolicyConflict,
  mergeHandheldPolicyCandidates,
  parseBuildNumber,
  parseHandheldBuildNumber,
  validateHandheldCandidate,
  validateHandheldPolicy,
  validateNativePolicy,
  validateRegistration,
} from "./logic";
import type { HandheldCandidate } from "./types";

const base = {
  enabled: true,
  releaseId: "r-1",
  minimumSupportedVersion: "1.2.3",
  minimumSupportedBuildNumber: "47",
  releaseMessage: "notes",
  targetScope: "all" as const,
  targetStoreGuids: [],
};

assert.equal(parseBuildNumber("47", true), 47);
assert.equal(parseBuildNumber("2147483648"), null);
assert.equal(parseHandheldBuildNumber("0"), null);
assert.equal(parseHandheldBuildNumber("47"), 47);
assert.equal(parseHandheldBuildNumber("2147483648"), null);
const candidateFixture = (
  id: string,
  lane: HandheldCandidate["lane"],
): HandheldCandidate => ({
  id,
  lane,
  platform: lane.startsWith("ios") ? "ios" : "android",
  kind: lane.endsWith("ota") ? "ota" : "native",
  version: "1.0.0",
  buildNumber: "47",
  runtimeVersion: null,
  channel: null,
  updateId: null,
  updateGroupId: null,
  message: null,
  dashboardUrl: null,
  downloadUrl: "https://example.test/download",
  appStoreUrl: null,
  createdAt: "2026-09-06T00:00:00Z",
  activatable: true,
  blockedReason: null,
});
const boundCandidate = candidateFixture("bound", "ios-native");
assert.deepEqual(
  mergeHandheldPolicyCandidates(
    [],
    [
      {
        lane: "ios-native",
        candidateId: "bound",
        candidate: boundCandidate,
      },
    ],
  ),
  [boundCandidate],
);
assert.deepEqual(
  mergeHandheldPolicyCandidates(
    [boundCandidate],
    [
      {
        lane: "ios-native",
        candidateId: "bound",
        candidate: candidateFixture("bound", "android-native"),
      },
    ],
  ),
  [boundCandidate],
);
assert.equal(
  validateRegistration("mobile-ios", {
    appStoreId: "123",
    buildNumber: "47",
    storefront: "au",
  }),
  "appStoreIdInvalid",
);
assert.equal(
  validateRegistration("pos-handheld", {
    appStoreId: "1234567890",
    buildNumber: "0",
    storefront: "au",
  }),
  "buildNumberInvalid",
);
assert.equal(
  validateRegistration("pos-handheld", {
    appStoreId: "1234567890",
    buildNumber: "2147483648",
    storefront: "au",
  }),
  null,
);
assert.equal(
  validateRegistration("pos-ipad", {
    appStoreId: "1234567890",
    buildNumber: "47",
    storefront: "australia",
  }),
  "storefrontInvalid",
);
assert.equal(
  validateNativePolicy({ ...base, releaseId: "" }),
  "releaseRequired",
);
assert.equal(
  validateNativePolicy({ ...base, minimumSupportedVersion: "" }),
  "minimumVersionRequired",
);
assert.equal(
  validateNativePolicy(
    { ...base, targetScope: "stores", targetStoreGuids: [] },
    true,
  ),
  "storesRequired",
);
assert.deepEqual(buildNativePolicyPayload(base, 3), {
  expectedPolicyVersion: 3,
  enabled: true,
  releaseId: "r-1",
  minimumSupportedVersion: "1.2.3",
  minimumSupportedBuildNumber: 47,
  releaseMessage: "notes",
});
assert.deepEqual(
  buildOtaPolicyPayload(
    {
      enabled: false,
      required: true,
      targetReleaseId: "r-2",
      releaseMessage: "x",
    },
    4,
  ),
  {
    expectedPolicyVersion: 4,
    enabled: false,
    required: false,
    targetReleaseId: null,
    releaseMessage: null,
  },
);
assert.equal(isPolicyConflict({ response: { status: 409 } }), true);
assert.equal(
  isPolicyConflict({
    response: { status: 400 },
    code: "APP_UPDATE_POLICY_VERSION_CONFLICT",
  }),
  true,
);
assert.equal(
  validateHandheldCandidate(
    { id: "candidate", activatable: false, blockedReason: "blocked" },
    true,
  ),
  "candidateBlocked",
);
assert.equal(
  validateHandheldCandidate(
    { id: "candidate", activatable: true, blockedReason: null },
    false,
  ),
  "candidateBlocked",
);
assert.equal(
  validateHandheldCandidate(
    {
      id: "candidate",
      activatable: true,
      blockedReason: "POS_HANDHELD_UPDATE_CANDIDATE_FINGERPRINT_MISMATCH",
    },
    false,
  ),
  null,
);
assert.equal(
  validateHandheldPolicy(
    { ...base, minimumSupportedBuildNumber: "0" },
    "android-native",
    { id: "candidate", activatable: true, blockedReason: null },
    true,
  ),
  "minimumBuildInvalid",
);
assert.deepEqual(
  buildConfirmationSummary(
    base,
    { releaseLabel: "1.2.3 (47)" },
    {
      enabled: "Enabled",
      disabled: "Disabled",
      release: "Release",
      noRelease: "None",
      required: "Required",
      optional: "Optional",
      allDevices: "All devices",
      minimumVersion: "Minimum version",
      minimumBuild: "Minimum Build",
      notes: "Notes",
      scope: "Scope",
    },
  )[0],
  "Enabled：Enabled",
);
console.log("app-downloads logic tests: ok");
