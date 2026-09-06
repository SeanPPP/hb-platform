import {
  createReportSnapshotKey,
  formatReportSnapshotTime,
  getCompleteReportSnapshot,
  getReportSnapshotDisplay,
  isReportScopeValid,
  MAX_REPORT_SNAPSHOTS,
  saveCompleteReportSnapshot,
  type CompleteReportSnapshot,
} from "./report-snapshot";

function assert(condition: unknown, message: string) {
  if (!condition) throw new Error(message);
}

function run() {
  const base = {
    accountIdentity: "account-A",
    tab: "product" as const,
    period: "week",
    startDate: "2026-09-01",
    endDate: "2026-09-07",
    compareStartDate: "2025-09-02",
    compareEndDate: "2025-09-08",
    compareMode: "ByWeek",
    branchCodes: ["B2", "B1"],
    scopeVersion: 12,
    supplierKind: "australia",
    supplierCode: "SUP-1",
    search: "abc",
    page: 2,
    pageSize: 20,
  };
  const sameWithDifferentBranchOrder = createReportSnapshotKey({ ...base, branchCodes: ["B1", "B2"] });
  assert(
    sameWithDifferentBranchOrder === createReportSnapshotKey(base),
    "authorized branch order must not split an equivalent snapshot",
  );
  assert(
    sameWithDifferentBranchOrder !== createReportSnapshotKey({ ...base, page: 3 }),
    "page must be part of the snapshot key",
  );
  assert(
    sameWithDifferentBranchOrder !== createReportSnapshotKey({ ...base, search: "different" }),
    "search must be part of the snapshot key",
  );
  const accountAKey = createReportSnapshotKey(base);
  const accountBKey = createReportSnapshotKey({ ...base, accountIdentity: "account-B" });
  assert(accountAKey !== accountBKey, "equal branch permissions must not merge different account snapshots");

  const cache = new Map<string, CompleteReportSnapshot<{ rows: number[] }>>();
  saveCompleteReportSnapshot(cache, "key", { rows: [1] }, { statisticUpdatedAt: "2026-09-06T01:02:03Z", cacheVersion: "v1" }, 123);
  const saved = cache.get("key");
  saveCompleteReportSnapshot(cache, accountAKey, { rows: [101] });
  assert(getCompleteReportSnapshot(cache, accountBKey) === undefined, "account B must not read account A's data");
  assert(saved?.data.rows[0] === 1 && saved.storedAt === 123, "complete snapshot should be stored atomically");
  assert(formatReportSnapshotTime("2026-09-06T01:02:03Z") !== null, "valid statistic time should be formatted");
  assert(formatReportSnapshotTime("invalid") === null, "invalid statistic time should fail closed");
  assert(
    getReportSnapshotDisplay(
      { data: { rows: [2] }, isFetching: false, isError: false },
      saved,
      () => undefined,
    )?.rows[0] === 1,
    "incomplete response must keep the exact prior complete snapshot",
  );
  assert(
    getReportSnapshotDisplay(
      { data: { rows: [2] }, isFetching: true, isError: false },
      saved,
      (data) => data,
    )?.rows[0] === 1,
    "old data retained during refetch must not replace the displayed snapshot",
  );
  const validScope = isReportScopeValid("account-A", { isSuccess: true, isError: false }, ["B1"]);
  const deniedScope = isReportScopeValid("account-A", { isSuccess: false, isError: true }, ["B1"]);
  assert(validScope && !deniedScope, "scope verification failure invalidates an earlier successful range");
  assert(!isReportScopeValid("", { isSuccess: true, isError: false }, ["B1"]), "logout must close the display gate");
  for (const live of [
    { data: { rows: [2] }, isFetching: false, isError: false },
    { data: undefined, isFetching: false, isError: true },
  ]) {
    assert(getReportSnapshotDisplay(live, saved, (data) => data, deniedScope) === undefined,
      "scope failure must hide both retained query data and a complete snapshot");
  }

  const bounded = new Map<string, CompleteReportSnapshot<number>>();
  for (let index = 0; index < MAX_REPORT_SNAPSHOTS; index++) saveCompleteReportSnapshot(bounded, String(index), index);
  getCompleteReportSnapshot(bounded, "0");
  saveCompleteReportSnapshot(bounded, "next", 99);
  assert(bounded.size === MAX_REPORT_SNAPSHOTS, "snapshot memory must have a fixed upper bound");
  assert(bounded.has("0") && !bounded.has("1") && bounded.has("next"), "least recently used conditions must be evicted first");
}

run();
console.log("report-snapshot tests passed");
