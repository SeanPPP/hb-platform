import assert from "node:assert/strict";
import { formatStatisticsFreshnessTime, getStatisticsFreshnessRefetchInterval, normalizeStatisticsFreshness } from "./statistics-freshness";

assert.deepEqual(
  normalizeStatisticsFreshness({ lastSuccessfulAtUtc: "2026-07-11T01:30:00Z", latestRunStatus: "Running" }),
  { lastSuccessfulAtUtc: "2026-07-11T01:30:00Z", latestRunStatus: "Running" },
);
assert.deepEqual(normalizeStatisticsFreshness({ latestRunStatus: "Unknown" }), {
  lastSuccessfulAtUtc: null,
  latestRunStatus: null,
});
assert.equal(formatStatisticsFreshnessTime("invalid"), null);
assert.match(formatStatisticsFreshnessTime("2026-07-11T01:30:00Z") ?? "", /^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$/);
assert.equal(getStatisticsFreshnessRefetchInterval({ latestRunStatus: "Running" }), 5_000);
assert.equal(getStatisticsFreshnessRefetchInterval({ latestRunStatus: "Success" }), false);
assert.equal(getStatisticsFreshnessRefetchInterval({ latestRunStatus: "Failed" }), false);
assert.equal(getStatisticsFreshnessRefetchInterval(undefined), false);
