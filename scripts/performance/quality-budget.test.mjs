import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

import { compareBudget } from "./compare-json-budget.mjs";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const budgetScript = resolve(scriptDirectory, "compare-json-budget.mjs");
const webBudgetFixture = resolve(scriptDirectory, "fixtures/web-budget");
const frozenBackendOnlyFixture = join(webBudgetFixture, "frozen-backend-only-actual.json");

function actual(value = 1250) {
  return {
    schemaVersion: 1,
    events: [
      {
        eventId: "44444444-4444-4444-8444-444444444444",
        metric: "ci.run.duration",
        observedAt: "2026-08-25T01:00:00.000Z",
        value,
        unit: "ms",
        dimensions: { lane: "backend" },
      },
    ],
  };
}

function budget(mode, max = 1000) {
  return {
    schemaVersion: "QualityBaselineBudgetV1",
    mode,
    metrics: {
      "ci.run.duration#lane=backend": {
        max,
        unit: "ms",
        required: true,
      },
    },
  };
}

test("observing 模式报告超阈值但退出码保持 0", () => {
  const result = compareBudget({ budget: budget("observing"), actual: actual() });
  assert.equal(result.exitCode, 0);
  assert.equal(result.report.status, "observed_exceedance");
  assert.equal(result.report.violations.length, 1);
  assert.equal(result.report.violations[0].type, "above_max");
});

test("frozen 模式拒绝把共享 runner CI 用时配置成硬门禁", () => {
  assert.throws(
    () => compareBudget({ budget: budget("frozen"), actual: actual() }),
    /Web|硬门禁|frozen/i,
  );
});

test("comparator 严格拒绝未知配置字段和重复实际 metric", () => {
  const invalidBudget = budget("observing");
  invalidBudget.metrics["ci.run.duration#lane=backend"].typo = 1;
  assert.throws(
    () => compareBudget({ budget: invalidBudget, actual: actual() }),
    /未知字段|typo/i,
  );

  const duplicate = actual();
  duplicate.events.push({ ...duplicate.events[0] });
  assert.throws(
    () => compareBudget({ budget: budget("observing"), actual: duplicate }),
    /重复|duplicate/i,
  );
});

function webActual(firstScreenBytes = 1200, largestChunkBytes = 700) {
  return {
    schemaVersion: 1,
    events: [
      {
        eventId: "55555555-5555-4555-8555-555555555555",
        metric: "web.first_screen.bytes",
        observedAt: "2026-08-25T01:00:00.000Z",
        value: firstScreenBytes,
        unit: "bytes",
        dimensions: { lane: "web" },
      },
      {
        eventId: "66666666-6666-4666-8666-666666666666",
        metric: "web.largest_initial_chunk.bytes",
        observedAt: "2026-08-25T01:00:00.000Z",
        value: largestChunkBytes,
        unit: "bytes",
        dimensions: { lane: "web" },
      },
    ],
  };
}

function webBudget(mode) {
  return {
    schemaVersion: "QualityBaselineBudgetV1",
    mode,
    metrics: {
      "web.first_screen.bytes#lane=web": {
        max: 1000,
        unit: "bytes",
        required: true,
      },
      "web.largest_initial_chunk.bytes#lane=web": {
        max: 600,
        unit: "bytes",
        required: true,
      },
    },
  };
}

test("Web 两项 budget 在 observing 只记录，在 frozen 任一超限即硬失败", () => {
  const observing = compareBudget({
    budget: webBudget("observing"),
    actual: webActual(),
  });
  assert.equal(observing.exitCode, 0);
  assert.equal(observing.report.status, "observed_exceedance");
  assert.deepEqual(
    observing.report.violations.map((item) => item.name).sort(),
    [
      "web.first_screen.bytes#lane=web",
      "web.largest_initial_chunk.bytes#lane=web",
    ],
  );

  const frozen = compareBudget({
    budget: webBudget("frozen"),
    actual: webActual(900, 700),
  });
  assert.equal(frozen.exitCode, 1);
  assert.equal(frozen.report.status, "failed");
  assert.deepEqual(
    frozen.report.violations.map((item) => item.name),
    ["web.largest_initial_chunk.bytes#lane=web"],
  );

  const withinBudget = compareBudget({
    budget: webBudget("frozen"),
    actual: webActual(1000, 600),
  });
  assert.equal(withinBudget.exitCode, 0);
  assert.equal(withinBudget.report.status, "passed");

  const missing = compareBudget({
    budget: webBudget("frozen"),
    actual: { schemaVersion: 1, events: [] },
  });
  assert.equal(missing.exitCode, 1);
  assert.equal(missing.report.violations.length, 2);
  assert.ok(missing.report.violations.every((item) => item.type === "missing_required"));
});

test("frozen Web budget 只在本次选择 Web lane 时执行", () => {
  const backendOnly = compareBudget({
    budget: webBudget("frozen"),
    actual: JSON.parse(readFileSync(frozenBackendOnlyFixture, "utf8")),
    selectedLanes: ["backend"],
  });
  assert.equal(backendOnly.exitCode, 0);
  assert.equal(backendOnly.report.status, "passed");
  assert.deepEqual(backendOnly.report.comparisons, []);

  const selectedWeb = compareBudget({
    budget: webBudget("frozen"),
    actual: actual(900),
    selectedLanes: ["web"],
  });
  assert.equal(selectedWeb.exitCode, 1);
  assert.equal(selectedWeb.report.violations.length, 2);
});

test("comparator CLI 从含两项 Web budget 的文件执行 observing/frozen 口径", () => {
  const directory = mkdtempSync(join(tmpdir(), "web-budget-cli-"));
  try {
    const actualPath = join(webBudgetFixture, "actual.json");
    const observingOutput = join(directory, "observing-report.json");
    const observing = spawnSync(
      process.execPath,
      [
        budgetScript,
        "--budget",
        join(webBudgetFixture, "observing.json"),
        "--actual",
        actualPath,
        "--output",
        observingOutput,
      ],
      { encoding: "utf8" },
    );
    assert.equal(observing.status, 0, observing.stderr);
    assert.equal(
      JSON.parse(readFileSync(observingOutput, "utf8")).status,
      "observed_exceedance",
    );

    const frozenOutput = join(directory, "frozen-report.json");
    const frozen = spawnSync(
      process.execPath,
      [
        budgetScript,
        "--budget",
        join(webBudgetFixture, "frozen.json"),
        "--actual",
        actualPath,
        "--output",
        frozenOutput,
      ],
      { encoding: "utf8" },
    );
    assert.equal(frozen.status, 1, frozen.stderr);
    const frozenReport = JSON.parse(readFileSync(frozenOutput, "utf8"));
    assert.equal(frozenReport.status, "failed");
    assert.equal(frozenReport.violations.length, 2);
  } finally {
    rmSync(directory, { recursive: true, force: true });
  }
});
