import {
  appendFileSync,
  lstatSync,
  mkdirSync,
  readFileSync,
  renameSync,
  writeFileSync,
} from "node:fs";
import { dirname, resolve } from "node:path";
import { pathToFileURL } from "node:url";

import {
  ValidationError,
  assertBoolean,
  assertEnum,
  assertExactKeys,
  assertFiniteNumber,
  assertPlainObject,
  assertSafeString,
} from "./lib/validation.mjs";

const MAX_JSON_BYTES = 1024 * 1024;
const MODES = ["observing", "frozen"];
const METRIC_NAME_PATTERN = /^[a-z][a-z0-9_.-]*(?:#lane=[a-z][a-z0-9-]*)?$/u;
const FROZEN_HARD_GATE_METRICS = new Set([
  "web.first_screen.bytes#lane=web",
  "web.largest_initial_chunk.bytes#lane=web",
]);
const QUALITY_LANES = new Set(["backend", "web", "pos-ipad", "pos-handheld"]);

function validateBudget(budget) {
  assertExactKeys(
    budget,
    { required: ["schemaVersion", "mode", "metrics"] },
    "budget",
  );
  if (budget.schemaVersion !== "QualityBaselineBudgetV1") {
    throw new ValidationError("budget.schemaVersion 必须为 QualityBaselineBudgetV1");
  }
  assertEnum(budget.mode, MODES, "budget.mode");
  assertPlainObject(budget.metrics, "budget.metrics");
  const entries = Object.entries(budget.metrics);
  if (entries.length > 1000) {
    throw new ValidationError("budget.metrics 最多允许 1000 项");
  }
  if (
    budget.mode === "frozen" &&
    (entries.length !== FROZEN_HARD_GATE_METRICS.size ||
      entries.some(([name]) => !FROZEN_HARD_GATE_METRICS.has(name)))
  ) {
    throw new ValidationError("frozen 模式只允许且必须包含两项 Web gzip 硬门禁");
  }

  for (const [name, rule] of entries) {
    assertSafeString(name, "budget metric 名称", {
      maxLength: 160,
      pattern: METRIC_NAME_PATTERN,
    });
    assertExactKeys(
      rule,
      { required: ["unit"], optional: ["min", "max", "required"] },
      `budget.metrics.${name}`,
    );
    if (!Object.hasOwn(rule, "min") && !Object.hasOwn(rule, "max")) {
      throw new ValidationError(`budget.metrics.${name} 至少需要 min 或 max`);
    }
    if (Object.hasOwn(rule, "min")) {
      assertFiniteNumber(rule.min, `budget.metrics.${name}.min`);
    }
    if (Object.hasOwn(rule, "max")) {
      assertFiniteNumber(rule.max, `budget.metrics.${name}.max`);
    }
    if (Object.hasOwn(rule, "min") && Object.hasOwn(rule, "max") && rule.min > rule.max) {
      throw new ValidationError(`budget.metrics.${name}.min 不得大于 max`);
    }
    assertSafeString(rule.unit, `budget.metrics.${name}.unit`, {
      maxLength: 32,
      pattern: /^[A-Za-z][A-Za-z0-9_/-]*$/u,
    });
    if (Object.hasOwn(rule, "required")) {
      assertBoolean(rule.required, `budget.metrics.${name}.required`);
    }
    if (
      budget.mode === "frozen" &&
      (!Object.hasOwn(rule, "max") ||
        Object.hasOwn(rule, "min") ||
        rule.unit !== "bytes" ||
        rule.required === false)
    ) {
      throw new ValidationError(
        `budget.metrics.${name} 的 frozen Web 硬门禁必须是 required bytes 上限`,
      );
    }
  }
  return budget;
}

function extractActualMetrics(actual) {
  assertPlainObject(actual, "actual");
  if (Array.isArray(actual.events)) {
    const eventMetrics = new Map();
    actual.events.forEach((event, index) => {
      const path = `actual.events[${index}]`;
      assertPlainObject(event, path);
      assertSafeString(event.metric, `${path}.metric`, {
        maxLength: 160,
        pattern: /^[a-z][a-z0-9_.-]*$/u,
      });
      assertFiniteNumber(event.value, `${path}.value`);
      const unit = assertSafeString(event.unit, `${path}.unit`, { maxLength: 32 });
      const lane = event.dimensions?.lane;
      if (lane !== undefined) {
        assertSafeString(lane, `${path}.dimensions.lane`, {
          maxLength: 64,
          pattern: /^[a-z][a-z0-9-]*$/u,
        });
      }
      const name = lane ? `${event.metric}#lane=${lane}` : event.metric;
      if (eventMetrics.has(name)) {
        throw new ValidationError(`actual.events 包含重复 metric ${name}`);
      }
      eventMetrics.set(name, { value: event.value, unit });
    });
    return eventMetrics;
  }
  const rawMetrics = actual.metrics;
  const metrics = new Map();

  if (Array.isArray(rawMetrics)) {
    rawMetrics.forEach((metric, index) => {
      const path = `actual.metrics[${index}]`;
      assertPlainObject(metric, path);
      assertSafeString(metric.name, `${path}.name`, {
        maxLength: 160,
        pattern: METRIC_NAME_PATTERN,
      });
      assertFiniteNumber(metric.value, `${path}.value`);
      const unit = Object.hasOwn(metric, "unit")
        ? assertSafeString(metric.unit, `${path}.unit`, { maxLength: 32 })
        : null;
      if (metrics.has(metric.name)) {
        throw new ValidationError(`actual.metrics 包含重复 metric ${metric.name}`);
      }
      metrics.set(metric.name, { value: metric.value, unit });
    });
    return metrics;
  }

  assertPlainObject(rawMetrics, "actual.metrics");
  for (const [name, rawValue] of Object.entries(rawMetrics)) {
    assertSafeString(name, "actual metric 名称", {
      maxLength: 160,
      pattern: METRIC_NAME_PATTERN,
    });
    if (typeof rawValue === "number") {
      assertFiniteNumber(rawValue, `actual.metrics.${name}`);
      metrics.set(name, { value: rawValue, unit: null });
      continue;
    }
    assertExactKeys(
      rawValue,
      { required: ["value"], optional: ["unit"] },
      `actual.metrics.${name}`,
    );
    assertFiniteNumber(rawValue.value, `actual.metrics.${name}.value`);
    const unit = Object.hasOwn(rawValue, "unit")
      ? assertSafeString(rawValue.unit, `actual.metrics.${name}.unit`, { maxLength: 32 })
      : null;
    metrics.set(name, { value: rawValue.value, unit });
  }
  return metrics;
}

function validateSelectedLanes(selectedLanes) {
  if (selectedLanes === undefined) return null;
  if (!Array.isArray(selectedLanes)) {
    throw new ValidationError("selectedLanes 必须是 lane 数组");
  }
  const selected = new Set();
  for (const lane of selectedLanes) {
    assertSafeString(lane, "selected lane", {
      maxLength: 64,
      pattern: /^[a-z][a-z0-9-]*$/u,
    });
    if (!QUALITY_LANES.has(lane)) {
      throw new ValidationError(`selectedLanes 包含未知 lane ${lane}`);
    }
    selected.add(lane);
  }
  return selected;
}

function isMetricSelected(name, selectedLanes) {
  if (selectedLanes === null) return true;
  const lane = /#lane=([a-z][a-z0-9-]*)$/u.exec(name)?.[1];
  return lane === undefined || selectedLanes.has(lane);
}

export function compareBudget({ budget, actual, selectedLanes }) {
  validateBudget(budget);
  const actualMetrics = extractActualMetrics(actual);
  const selectedLaneSet = validateSelectedLanes(selectedLanes);
  const comparisons = [];
  const violations = [];

  for (const [name, rule] of Object.entries(budget.metrics).sort(([left], [right]) =>
    left.localeCompare(right),
  )) {
    // frozen Web 预算只约束本次实际选择的 Web lane，避免 backend/POS-only 运行误失败。
    if (!isMetricSelected(name, selectedLaneSet)) continue;
    const actualMetric = actualMetrics.get(name);
    if (!actualMetric) {
      const required = rule.required !== false;
      const comparison = {
        name,
        value: null,
        unit: rule.unit,
        min: rule.min ?? null,
        max: rule.max ?? null,
        status: required ? "missing_required" : "missing_optional",
      };
      comparisons.push(comparison);
      if (required) violations.push({ ...comparison, type: "missing_required" });
      continue;
    }

    let status = "within_budget";
    if (actualMetric.unit !== rule.unit) {
      status = "unit_mismatch";
    } else if (Object.hasOwn(rule, "max") && actualMetric.value > rule.max) {
      status = "above_max";
    } else if (Object.hasOwn(rule, "min") && actualMetric.value < rule.min) {
      status = "below_min";
    }
    const comparison = {
      name,
      value: actualMetric.value,
      unit: actualMetric.unit,
      min: rule.min ?? null,
      max: rule.max ?? null,
      status,
    };
    comparisons.push(comparison);
    if (status !== "within_budget") violations.push({ ...comparison, type: status });
  }

  const unbudgetedMetrics = [...actualMetrics.keys()]
    .filter((name) => !Object.hasOwn(budget.metrics, name))
    .sort();
  const hasViolations = violations.length > 0;
  const status =
    budget.mode === "observing"
      ? hasViolations
        ? "observed_exceedance"
        : "observed"
      : hasViolations
        ? "failed"
        : "passed";
  return {
    exitCode: budget.mode === "frozen" && hasViolations ? 1 : 0,
    report: {
      schemaVersion: "QualityBaselineBudgetReportV1",
      mode: budget.mode,
      status,
      comparedMetricCount: comparisons.length,
      comparisons,
      violations,
      unbudgetedMetrics,
    },
  };
}

function readJsonFile(filePath, label) {
  const stat = lstatSync(filePath);
  if (!stat.isFile() || stat.isSymbolicLink()) {
    throw new ValidationError(`${label} 必须是普通 JSON 文件`);
  }
  if (stat.size > MAX_JSON_BYTES) {
    throw new ValidationError(`${label} 不得超过 ${MAX_JSON_BYTES} bytes`);
  }
  try {
    return JSON.parse(readFileSync(filePath, "utf8"));
  } catch {
    throw new ValidationError(`${label} 不是有效 JSON`);
  }
}

function writeJsonAtomic(filePath, value) {
  mkdirSync(dirname(filePath), { recursive: true });
  const temporaryPath = `${filePath}.${process.pid}.tmp`;
  writeFileSync(temporaryPath, `${JSON.stringify(value, null, 2)}\n`, {
    encoding: "utf8",
    mode: 0o600,
  });
  renameSync(temporaryPath, filePath);
}

function appendSummary(summaryPath, report) {
  if (!summaryPath) return;
  const rows = report.comparisons.length
    ? report.comparisons
        .map(
          (item) =>
            `| \`${item.name}\` | ${item.value ?? "—"} | ${item.unit ?? "—"} | ${item.min ?? "—"} | ${item.max ?? "—"} | ${item.status} |`,
        )
        .join("\n")
    : "| — | — | — | — | — | 尚未冻结阈值 |";
  appendFileSync(
    summaryPath,
    `\n## Quality budget\n\n- 模式：\`${report.mode}\`\n- 结论：\`${report.status}\`\n- 未纳入预算的观测指标：${report.unbudgetedMetrics.length}\n\n| Metric | 实际值 | 单位 | Min | Max | 状态 |\n| --- | ---: | --- | ---: | ---: | --- |\n${rows}\n`,
    "utf8",
  );
}

function parseArgs(argv) {
  const options = {};
  const valueOptions = new Map([
    ["--budget", "budget"],
    ["--actual", "actual"],
    ["--output", "output"],
    ["--summary", "summary"],
    ["--selected-lanes", "selectedLanes"],
  ]);
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument === "--help") {
      options.help = true;
    } else if (valueOptions.has(argument)) {
      const value = argv[++index];
      if (!value || value.startsWith("--")) throw new ValidationError(`${argument} 缺少值`);
      options[valueOptions.get(argument)] = value;
    } else {
      throw new ValidationError(`未知参数 ${argument}`);
    }
  }
  for (const required of ["budget", "actual", "output"]) {
    if (!options.help && !options[required]) {
      throw new ValidationError(`必须提供 --${required}`);
    }
  }
  return options;
}

function main(argv = process.argv.slice(2)) {
  const options = parseArgs(argv);
  if (options.help) {
    console.log(
      "用法: node scripts/performance/compare-json-budget.mjs --budget <budget.json> --actual <actual.json> --output <report.json> [--summary <markdown>]",
    );
    return;
  }
  const result = compareBudget({
    budget: readJsonFile(resolve(options.budget), "budget"),
    actual: readJsonFile(resolve(options.actual), "actual"),
    ...(options.selectedLanes
      ? { selectedLanes: JSON.parse(options.selectedLanes) }
      : {}),
  });
  writeJsonAtomic(resolve(options.output), result.report);
  appendSummary(options.summary ? resolve(options.summary) : null, result.report);
  console.log(`Quality budget：${result.report.mode} / ${result.report.status}`);
  process.exitCode = result.exitCode;
}

const isMain =
  process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href;
if (isMain) {
  try {
    main();
  } catch (error) {
    console.error(`Quality budget 比较失败：${error.message}`);
    process.exitCode = 2;
  }
}
