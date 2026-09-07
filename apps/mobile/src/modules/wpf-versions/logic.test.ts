import {
  canSavePolicy,
  compareVersions,
  createLatestRequestGuard,
  getPolicyValidationError,
  inferRollback,
  policySummaryMatchesRequest,
} from "./logic";

function assert(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(message);
}

assert(
  compareVersions("1.2.10", "1.2.9")! > 0,
  "semantic version comparison should compare numeric parts",
);
assert(
  inferRollback("1.2.0", "1.3.0"),
  "lower target must be treated as rollback",
);
assert(!inferRollback("1.3.0", "1.3.0"), "same target is not rollback");

const basePolicy = {
  targetVersion: "1.3.0",
  minimumSupportedVersion: "1.2.0",
  targetScope: "all" as const,
  targetStoreGuids: [],
  targetDeviceRegistrationIds: [],
};
assert(
  getPolicyValidationError(basePolicy) === null,
  "valid all-target policy should pass",
);
assert(
  getPolicyValidationError({ ...basePolicy, activeVersions: ["1.2.0"] }) ===
    "targetVersionUnavailable",
  "target must be an active release in the current channel",
);
assert(
  getPolicyValidationError({ ...basePolicy, activeVersions: ["1.3.0"] }) ===
    "minimumVersionUnavailable",
  "minimum must be an active release in the current channel",
);
assert(
  getPolicyValidationError({
    ...basePolicy,
    minimumSupportedVersion: "1.4.0",
  }) === "minimumAboveTarget",
  "minimum above target must fail",
);
assert(
  getPolicyValidationError({
    ...basePolicy,
    targetScope: "devices",
    targetDeviceRegistrationIds: [],
  }) === "devicesRequired",
  "device policy must require devices",
);
assert(
  canSavePolicy({
    ...basePolicy,
    targetScope: "stores",
    targetStoreGuids: ["store-1"],
  }),
  "selected store policy should pass",
);

const policySummary = {
  channel: "production",
  targetVersion: "1.3.0",
  minimumSupportedVersion: "1.2.0",
  forceUpdate: true,
  isRollback: false,
  targetScope: "devices" as const,
  targetStoreGuids: [],
  targetDeviceRegistrationIds: [7],
  targetStoreSummaries: [],
  targetDeviceSummaries: [],
  policyUpdatedAt: null,
  policyUpdatedBy: null,
};
assert(
  policySummaryMatchesRequest(
    {
      ...basePolicy,
      channel: "production",
      forceUpdate: true,
      isRollback: false,
      targetScope: "devices",
      targetDeviceRegistrationIds: [7],
    },
    policySummary,
  ),
  "policy readback should match submitted fields",
);
assert(
  !policySummaryMatchesRequest(
    {
      ...basePolicy,
      channel: "production",
      forceUpdate: false,
      isRollback: false,
      targetScope: "devices",
      targetDeviceRegistrationIds: [7],
    },
    policySummary,
  ),
  "policy readback mismatch must fail verification",
);
assert(
  !policySummaryMatchesRequest(
    {
      ...basePolicy,
      channel: "production",
      forceUpdate: true,
      isRollback: false,
      targetScope: "devices",
      targetDeviceRegistrationIds: [8],
    },
    null,
  ),
  "empty policy readback must fail verification",
);

const guard = createLatestRequestGuard();
const first = guard.next();
const second = guard.next();
assert(
  !guard.isCurrent(first) && guard.isCurrent(second),
  "newer request must invalidate older response",
);
guard.invalidate();
assert(!guard.isCurrent(second), "invalidate must isolate in-flight response");

console.log("wpf versions logic tests passed");
